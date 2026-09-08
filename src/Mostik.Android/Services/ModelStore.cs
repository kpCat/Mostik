using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Android.Content;
using Android.OS;
using Mostik.Core;

namespace Mostik.Mobile.Services;

public sealed class ModelStore : IDisposable
{
    private readonly string _root;
    private readonly global::Android.Net.ConnectivityManager? _connectivity;
    private readonly HttpClient _http = new(new HttpClientHandler { AllowAutoRedirect = false })
        { Timeout = Timeout.InfiniteTimeSpan };
    private readonly SemaphoreSlim _downloadGate = new(1, 1);
    public IReadOnlyList<ModelSpec> Models { get; }
    private static readonly string[] Required = ["am/final.mdl", "conf/mfcc.conf", "conf/model.conf",
        "graph/HCLr.fst", "graph/Gr.fst"];

    public ModelStore(Context context)
    {
        _connectivity = (global::Android.Net.ConnectivityManager?)context.GetSystemService(Context.ConnectivityService);
        _root = Path.Combine(context.NoBackupFilesDir!.AbsolutePath, "vosk");
        Directory.CreateDirectory(_root);
        using var stream = context.Assets!.Open("models.json");
        Models = JsonSerializer.Deserialize<ModelSpec[]>(stream) ?? throw new InvalidDataException("Нет каталога моделей.");
        if (Models.Count != 2 || Models.Select(m => m.Language).Distinct().Count() != 2)
            throw new InvalidDataException("Повреждён каталог моделей.");
        foreach (var model in Models) model.Validate();
    }

    public ModelSpec For(string language) => Models.Single(m => m.Language == language);
    public string Folder(ModelSpec model) => Path.Combine(_root, model.Id);
    private static bool ValidFiles(string folder) => Required.All(p =>
        File.Exists(Path.Combine(folder, p)) && new FileInfo(Path.Combine(folder, p)).Length > 0);

    public bool IsReady(ModelSpec model)
    {
        try
        {
            string folder = Folder(model);
            return File.Exists(Path.Combine(folder, ".complete"))
                && File.ReadAllText(Path.Combine(folder, ".complete")).Trim() == model.Sha256
                && ValidFiles(folder);
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    public async Task InstallAsync(ModelSpec model, bool wifiOnly, IProgress<string> progress, CancellationToken cancellationToken)
    {
        await _downloadGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        string? temporary = null;
        try
        {
            if (IsReady(model)) { progress.Report(model.Title + ": уже установлена"); return; }
            using var disk = new StatFs(_root);
            long needed = model.DownloadBytes + model.MaxExtractedBytes + 64L * 1024 * 1024;
            if (disk.AvailableBytes < needed) throw new IOException("Мало места. Освободите около 500 МБ и повторите.");
            temporary = Path.Combine(_root, ".install-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temporary);
            string archive = Path.Combine(temporary, "model.zip");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromMinutes(20));
            var token = timeout.Token;
            RequireWifi(wifiOnly);
            progress.Report(model.Title + ": загрузка…");
            using (var response = await _http.GetAsync(model.Url, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false))
            {
                if ((int)response.StatusCode is >= 300 and < 400)
                    throw new HttpRequestException("Сервер изменил адрес модели. Требуется обновление каталога, небезопасный редирект запрещён.");
                response.EnsureSuccessStatusCode();
                if (response.Content.Headers.ContentLength is long declared && declared != model.DownloadBytes)
                    throw new InvalidDataException("Размер пакета изменился. Установка остановлена.");
                using var source = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
                using var file = new FileStream(archive, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true);
                byte[] buffer = new byte[65536];
                long read = 0;
                int lastPercent = -1;
                int count;
                while ((count = await source.ReadAsync(buffer, token).ConfigureAwait(false)) != 0)
                {
                    RequireWifi(wifiOnly);
                    read += count;
                    if (read > model.DownloadBytes) throw new InvalidDataException("Пакет больше ожидаемого.");
                    await file.WriteAsync(buffer.AsMemory(0, count), token).ConfigureAwait(false);
                    int percent = (int)(100 * read / model.DownloadBytes);
                    if (percent != lastPercent) { progress.Report($"{model.Title}: {percent}%"); lastPercent = percent; }
                }
                if (read != model.DownloadBytes) throw new InvalidDataException("Загрузка оборвалась. Повторите установку при наличии сети.");
            }
            progress.Report(model.Title + ": проверка SHA-256…");
            using (var file = File.OpenRead(archive))
            {
                string actual = Convert.ToHexString(await SHA256.HashDataAsync(file, token).ConfigureAwait(false));
                if (!actual.Equals(model.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Контрольная сумма не совпала. Непроверенный пакет не установлен.");
            }
            string unpack = Path.Combine(temporary, "unpack");
            progress.Report(model.Title + ": распаковка…");
            await SafeZip.ExtractAsync(archive, unpack, model.Id, model.MaxExtractedBytes, token).ConfigureAwait(false);
            string staged = Path.Combine(unpack, model.Id);
            if (!ValidFiles(staged)) throw new InvalidDataException("В пакете отсутствуют обязательные файлы модели.");
            await File.WriteAllTextAsync(Path.Combine(staged, ".complete"), model.Sha256, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            string destination = Folder(model);
            if (Directory.Exists(destination)) Directory.Delete(destination, true);
            Directory.Move(staged, destination); // Same-volume atomic rename; never expose a partial installation.
            progress.Report(model.Title + ": установлена");
        }
        finally
        {
            if (temporary is not null)
            {
                try { Directory.Delete(temporary, true); } catch (IOException) { }
            }
            _downloadGate.Release();
        }
    }

    // Only call while the application operation gate is held and no model is being read.
    public void RemoveIncompleteDownloads()
    {
        foreach (string path in Directory.EnumerateDirectories(_root, ".install-*"))
        {
            try { Directory.Delete(path, true); } catch (IOException) { }
        }
    }
    private void RequireWifi(bool wifiOnly)
    {
        if (!wifiOnly) return;
        using var capabilities = _connectivity?.GetNetworkCapabilities(_connectivity.ActiveNetwork);
        if (capabilities?.HasTransport(global::Android.Net.TransportType.Wifi) != true)
            throw new IOException("Wi-Fi отключился. Загрузка остановлена; повторите её после подключения.");
    }
    public void Dispose() { _http.Dispose(); _downloadGate.Dispose(); }
}
