using Mostik.Core;
using Xamarin.Google.MLKit.Common.Model;
using Xamarin.Google.MLKit.NL.Translate;

namespace Mostik.Mobile.Services;

public sealed class OfflineTranslator : IDisposable
{
    private readonly Dictionary<string, ITranslator> _clients = new();

    private ITranslator Client(Direction direction)
    {
        if (_clients.TryGetValue(direction.Source, out var existing)) return existing;
        using var builder = new TranslatorOptions.Builder();
        using var options = builder.SetSourceLanguage(direction.Source)!.SetTargetLanguage(direction.Target)!.Build();
        var translator = Translation.GetClient(options!);
        _clients.Add(direction.Source, translator);
        return translator;
    }

    public async Task<bool> IsReadyAsync(CancellationToken token)
    {
        var manager = RemoteModelManager.Instance;
        foreach (string language in new[] { "ru", "pl" })
        {
            using var builder = new TranslateRemoteModel.Builder(language);
            using var model = builder.Build();
            using var task = manager.IsModelDownloaded(model!);
            var result = await GoogleTask.AwaitAsync(task, token);
            if (!string.Equals(result?.ToString(), "true", StringComparison.OrdinalIgnoreCase)) return false;
        }
        return true;
    }

    // The ONLY entry point allowed to download translation models; called from explicit setup action.
    public async Task DownloadAsync(bool wifiOnly, IProgress<string> progress, CancellationToken token)
    {
        using var builder = new DownloadConditions.Builder();
        if (wifiOnly) builder.RequireWifi();
        using var conditions = builder.Build();
        foreach (var direction in new[] { Direction.RuToPl, Direction.PlToRu })
        {
            token.ThrowIfCancellationRequested();
            progress.Report("ML Kit: " + direction.Label + " — загрузка пакетов…");
            using var task = Client(direction).DownloadModelIfNeeded(conditions!);
            await GoogleTask.AwaitAsync(task, token);
        }
        if (!await IsReadyAsync(token)) throw new InvalidOperationException("ML Kit не подтвердил наличие обеих моделей.");
        // A real inference in each direction catches model/registrar failures before leaving Wi-Fi.
        _ = await TranslateAsync("Здравствуйте. Сколько это стоит?", Direction.RuToPl, token);
        _ = await TranslateAsync("Dzień dobry. Ile to kosztuje?", Direction.PlToRu, token);
        progress.Report("Пакеты перевода установлены. Осталась проверка голосов и авиарежима.");
    }

    public async Task<string> TranslateAsync(string source, Direction direction, CancellationToken token)
    {
        string text = TextPolicy.Phrase(source);
        if (!await IsReadyAsync(token)) throw new InvalidOperationException("Пакеты перевода отсутствуют. Откройте «Пакеты» при наличии сети.");
        // No DownloadModelIfNeeded here: conversation must not secretly depend on a download.
        using var task = Client(direction).Translate(text);
        var result = await GoogleTask.AwaitAsync(task, token);
        return TextPolicy.Phrase(result?.ToString());
    }

    public void Dispose()
    {
        foreach (var client in _clients.Values) { client.Close(); client.Dispose(); }
        _clients.Clear();
    }
}
