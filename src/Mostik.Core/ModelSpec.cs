namespace Mostik.Core;

public sealed record ModelSpec(string Language, string Title, string Id, string Url,
    string Sha256, long DownloadBytes, long MaxExtractedBytes)
{
    public void Validate()
    {
        _ = Direction.FromSource(Language);
        if (Id != $"vosk-model-small-{Language}-0.22") throw new InvalidDataException("Неизвестный пакет модели.");
        if (!Uri.TryCreate(Url, UriKind.Absolute, out var uri) || uri.Scheme != "https"
            || uri.Host != "alphacephei.com" || uri.AbsolutePath != $"/vosk/models/{Id}.zip")
            throw new InvalidDataException("Недопустимый адрес модели.");
        if (Sha256.Length != 64 || Sha256.Any(c => !Uri.IsHexDigit(c)))
            throw new InvalidDataException("Недопустимая SHA-256.");
        if (DownloadBytes <= 0 || DownloadBytes > 128L * 1024 * 1024
            || MaxExtractedBytes < DownloadBytes || MaxExtractedBytes > 512L * 1024 * 1024)
            throw new InvalidDataException("Недопустимый размер модели.");
    }
}
