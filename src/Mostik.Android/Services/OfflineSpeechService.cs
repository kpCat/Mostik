using System.Collections.Concurrent;
using Android.Content;
using Android.Media;
using Android.OS;
using Android.Speech.Tts;
using Mostik.Core;
using AudioStream = Android.Media.Stream;

namespace Mostik.Mobile.Services;

public sealed class OfflineSpeechService : IDisposable
{
    private readonly Context _context;
    private readonly Handler _main = new(Looper.MainLooper!);
    private readonly ConcurrentDictionary<string, TaskCompletionSource<bool>> _pending = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private TextToSpeech? _engine;
    private TaskCompletionSource<bool>? _initialization;
    private InitListener? _initListener;
    private ProgressListener? _progressListener;
    private bool _disposed;
    public float Rate { get; set; } = 0.95f;
    public string EngineName => _engine?.DefaultEngine ?? "не выбран";

    public OfflineSpeechService(Context context) => _context = context.ApplicationContext!;

    private Task OnMainAsync(Action action)
    {
        if (Looper.MyLooper() == Looper.MainLooper) { action(); return Task.CompletedTask; }
        var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _main.Post(() => { try { action(); done.TrySetResult(true); } catch (Exception ex) { done.TrySetException(ex); } });
        return done.Task;
    }

    public async Task InitializeAsync(CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await OnMainAsync(() =>
        {
            if (_initialization is not null) return;
            _initialization = new(TaskCreationOptions.RunContinuationsAsynchronously);
            var completion = _initialization;
            _initListener = new InitListener(status =>
            {
                if ((int)status == 0) completion.TrySetResult(true);
                else completion.TrySetException(new InvalidOperationException("Системный синтезатор речи не запустился. Откройте настройки голосов."));
            });
            _engine = new TextToSpeech(_context, _initListener);
        });
        await _initialization!.Task.WaitAsync(TimeSpan.FromSeconds(12), token);
        await OnMainAsync(() =>
        {
            if (_progressListener is not null) return;
            _progressListener = new ProgressListener(this);
            if ((int)_engine!.SetOnUtteranceProgressListener(_progressListener) != 0)
                throw new InvalidOperationException("Не удалось подключить уведомления озвучки.");
            using var builder = new AudioAttributes.Builder();
            using var attributes = builder.SetUsage(AudioUsageKind.Media)!.SetContentType(AudioContentType.Speech)!.Build();
            _engine.SetAudioAttributes(attributes);
        });
    }

    // Called only while the application operation gate is held.
    public async Task ReinitializeAsync(CancellationToken token)
    {
        await OnMainAsync(ShutdownEngine);
        await InitializeAsync(token);
    }

    private Voice? FindVoice(string language) => _engine?.Voices?
        .Where(v => v.Locale?.Language == language && VoicePolicy.IsOfflineReady(v.IsNetworkConnectionRequired, v.Features))
        .OrderByDescending(v => (int)v.Quality).ThenBy(v => (int)v.Latency).FirstOrDefault();

    public bool HasVoice(string language)
    {
        try { return _initialization?.Task.IsCompletedSuccessfully == true && FindVoice(language) is not null; }
        catch (Java.Lang.Exception) { return false; }
    }

    public string Describe(string language)
    {
        try { return FindVoice(language)?.Name ?? "офлайн-голос не установлен"; }
        catch (Java.Lang.Exception) { return "ошибка получения голосов"; }
    }

    public async Task SpeakAsync(string text, string language, CancellationToken token)
    {
        _ = Direction.FromSource(language);
        text = TextPolicy.Phrase(text);
        await _gate.WaitAsync(token);
        string? id = null;
        try
        {
            await InitializeAsync(token);
            token.ThrowIfCancellationRequested();
            id = Guid.NewGuid().ToString("N");
            var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pending[id] = done;
            string utterance = id;
            using var cancellation = token.Register(() => _main.Post(() =>
            {
                if (_pending.ContainsKey(utterance)) Stop();
            }));
            await OnMainAsync(() =>
            {
                token.ThrowIfCancellationRequested();
                var voice = FindVoice(language) ?? throw new InvalidOperationException(
                    language == "ru" ? "Русский офлайн-голос не установлен. Перевод сохранён на экране; откройте «Пакеты → Голоса»."
                                     : "Польский офлайн-голос не установлен. Перевод сохранён на экране; откройте «Пакеты → Голоса».");
                var audio = (AudioManager?)_context.GetSystemService(Context.AudioService);
                if (audio?.GetStreamVolume(AudioStream.Music) == 0)
                    throw new InvalidOperationException("Громкость мультимедиа равна нулю. Прибавьте громкость телефона.");
                if ((int)_engine!.SetVoice(voice) != 0) throw new InvalidOperationException("Не удалось выбрать локальный голос.");
                if ((int)_engine.SetSpeechRate(Math.Clamp(Rate, 0.75f, 1.25f)) != 0)
                    throw new InvalidOperationException("Синтезатор отклонил скорость озвучки.");
                using var parameters = new Bundle();
                parameters.PutInt("streamType", (int)AudioStream.Music);
                using var spoken = new Java.Lang.String(text);
                if ((int)_engine.Speak(spoken, QueueMode.Flush, parameters, utterance) != 0)
                    throw new InvalidOperationException("Синтезатор отклонил озвучку. Проверьте установленный голос.");
            });
            try { await done.Task.WaitAsync(TimeSpan.FromSeconds(70), token); }
            catch { await OnMainAsync(Stop); throw; }
            // Acoustic tail is over before UI re-enables either microphone button.
            await Task.Delay(250, token);
        }
        finally
        {
            if (id is not null) _pending.TryRemove(id, out _);
            _gate.Release();
        }
    }

    public void Stop()
    {
        if (Looper.MyLooper() != Looper.MainLooper) { _main.Post(Stop); return; }
        _engine?.Stop();
        foreach (var pending in _pending.Values) pending.TrySetCanceled();
    }

    public Intent VoiceInstallIntent()
    {
        var intent = new Intent(TextToSpeech.Engine.ActionInstallTtsData);
        string? engine = _engine?.DefaultEngine;
        if (!string.IsNullOrWhiteSpace(engine)) intent.SetPackage(engine);
        return intent;
    }

    private void ShutdownEngine()
    {
        Stop();
        _engine?.Shutdown();
        _engine?.Dispose();
        _engine = null;
        _progressListener?.Dispose();
        _progressListener = null;
        _initListener?.Dispose();
        _initListener = null;
        _initialization = null;
        _pending.Clear();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        // Owner awaits the active operation before disposing.
        if (Looper.MyLooper() == Looper.MainLooper) ShutdownEngine(); else _main.Post(ShutdownEngine);
    }

    private sealed class InitListener(Action<OperationResult> callback) : Java.Lang.Object, TextToSpeech.IOnInitListener
    {
        public void OnInit(OperationResult status) => callback(status);
    }
    private sealed class ProgressListener(OfflineSpeechService owner) : UtteranceProgressListener
    {
        public override void OnStart(string? id) { }
        public override void OnDone(string? id)
        {
            if (id is not null && owner._pending.TryGetValue(id, out var source)) source.TrySetResult(true);
        }
        [Obsolete("Обязательная совместимость с Android API до 21.")]
        public override void OnError(string? id) => Fail(id);
        public override void OnError(string? id, TextToSpeechError errorCode) => Fail(id);
        private void Fail(string? id)
        {
            if (id is not null && owner._pending.TryGetValue(id, out var source))
                source.TrySetException(new InvalidOperationException("Озвучка завершилась ошибкой. Голос может быть скачан не полностью."));
        }
        public override void OnStop(string? id, bool interrupted)
        {
            if (id is not null && owner._pending.TryGetValue(id, out var source)) source.TrySetCanceled();
        }
    }
}
