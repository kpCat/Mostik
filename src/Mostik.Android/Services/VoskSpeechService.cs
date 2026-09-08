using System.Diagnostics;
using Android.Media;
using Mostik.Core;

namespace Mostik.Mobile.Services;

public sealed record ListeningUpdate(string Stage, string Text = "", int Level = 0);

public sealed class VoskSpeechService : IDisposable
{
    private readonly ModelStore _models;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private nint _model;
    private string? _language;
    private int _finish;
    private bool _disposed;
    public long LastModelLoadMilliseconds { get; private set; }

    public VoskSpeechService(ModelStore models) => _models = models;
    public void FinishRecording() => Interlocked.Exchange(ref _finish, 1);

    public async Task<string> ListenAsync(string language, bool automaticEnd,
        IProgress<ListeningUpdate> progress, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            Interlocked.Exchange(ref _finish, 0);
            return await Task.Run(() => Listen(language, automaticEnd, progress, cancellationToken), cancellationToken)
                .ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    private string Listen(string language, bool automaticEnd, IProgress<ListeningUpdate> progress, CancellationToken token)
    {
        const int sampleRate = 16000;
        var spec = _models.For(language);
        if (!_models.IsReady(spec)) throw new InvalidOperationException("Сначала установите языковые пакеты в разделе «Пакеты».");
        progress.Report(new("loading"));
        var load = Stopwatch.StartNew();
        if (_language != language || _model == 0)
        {
            // One model in RAM: switching language costs a cold model load but avoids holding both models.
            ReleaseModel();
            VoskNative.vosk_set_log_level(-1);
            _model = VoskNative.vosk_model_new(_models.Folder(spec));
            if (_model == 0) throw new InvalidOperationException("Vosk не смог открыть модель. Проверьте пакет и свободную память.");
            _language = language;
        }
        LastModelLoadMilliseconds = load.ElapsedMilliseconds;
        token.ThrowIfCancellationRequested();
        nint recognizer = VoskNative.vosk_recognizer_new(_model, sampleRate);
        if (recognizer == 0) throw new InvalidOperationException("Не удалось создать распознаватель речи.");
        try
        {
            int minimum = AudioRecord.GetMinBufferSize(sampleRate, ChannelIn.Mono, Encoding.Pcm16bit);
            if (minimum <= 0) throw new InvalidOperationException("Телефон не поддерживает требуемый формат микрофона.");
            using var recorder = new AudioRecord(AudioSource.VoiceRecognition, sampleRate, ChannelIn.Mono,
                Encoding.Pcm16bit, Math.Max(minimum * 4, 16000));
            if (recorder.State != global::Android.Media.State.Initialized)
                throw new InvalidOperationException("Микрофон недоступен. Закройте другие приложения, использующие его.");
            byte[] bytes = new byte[3200]; // 100 ms of mono PCM16 at 16 kHz.
            var segments = new List<string>();
            var clock = Stopwatch.StartNew();
            long lastUpdate = -250;
            bool hasWords = false;
            string partial = "";
            recorder.StartRecording();
            if (recorder.RecordingState != RecordState.Recording)
                throw new InvalidOperationException("Микрофон не начал запись. Проверьте разрешение и системный переключатель микрофона.");
            progress.Report(new("listening"));
            try
            {
                while (Volatile.Read(ref _finish) == 0 && clock.Elapsed < TimeSpan.FromSeconds(30))
                {
                    token.ThrowIfCancellationRequested();
                    int count = recorder.Read(bytes, 0, bytes.Length, (int)AudioRecordReadOptions.NonBlocking);
                    if (count < 0) throw new IOException("Ошибка чтения микрофона: " + count);
                    if (count == 0) { Thread.Sleep(15); continue; }
                    if ((count & 1) != 0) throw new IOException("Некорректный PCM-буфер микрофона.");
                    int state = VoskNative.vosk_recognizer_accept_waveform(recognizer, bytes, count);
                    if (state < 0) throw new InvalidOperationException("Ошибка распознавания Vosk.");
                    if (state == 1)
                    {
                        string text = TextPolicy.ReadVoskText(VoskNative.Copy(VoskNative.vosk_recognizer_result(recognizer)));
                        if (text.Length > 0) { segments.Add(text); hasWords = true; }
                        partial = "";
                        if (automaticEnd && segments.Count > 0) break;
                    }
                    else if (clock.ElapsedMilliseconds - lastUpdate >= 180)
                    {
                        partial = TextPolicy.ReadVoskText(VoskNative.Copy(VoskNative.vosk_recognizer_partial_result(recognizer)), true);
                        hasWords |= partial.Length > 0;
                    }
                    if (clock.ElapsedMilliseconds - lastUpdate >= 180)
                    {
                        lastUpdate = clock.ElapsedMilliseconds;
                        progress.Report(new("listening", string.Join(" ", segments.Append(partial)).Trim(), Level(bytes, count)));
                    }
                    if (!hasWords && clock.Elapsed > TimeSpan.FromSeconds(10)) break;
                }
                token.ThrowIfCancellationRequested();
            }
            finally
            {
                // Release microphone before any translation or TTS; never feed the app's own voice into Vosk.
                if (recorder.RecordingState == RecordState.Recording) recorder.Stop();
            }
            string final = TextPolicy.ReadVoskText(VoskNative.Copy(VoskNative.vosk_recognizer_final_result(recognizer)));
            if (final.Length > 0) segments.Add(final);
            return TextPolicy.Phrase(string.Join(" ", segments));
        }
        finally { VoskNative.vosk_recognizer_free(recognizer); }
    }

    private static int Level(byte[] pcm, int count)
    {
        double energy = 0;
        for (int i = 0; i + 1 < count; i += 2)
        {
            short sample = (short)(pcm[i] | pcm[i + 1] << 8);
            energy += (double)sample * sample;
        }
        double rms = Math.Sqrt(energy / Math.Max(1, count / 2));
        return Math.Clamp((int)(100 * rms / 8000), 0, 100);
    }

    private void ReleaseModel()
    {
        if (_model != 0) { VoskNative.vosk_model_free(_model); _model = 0; }
        _language = null;
    }
    // Owner must await its operation task before calling Dispose: no concurrent native free/read.
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        ReleaseModel();
        _gate.Dispose();
    }
}
