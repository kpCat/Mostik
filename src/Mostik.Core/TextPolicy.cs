using System.Text.Json;
using System.Text.RegularExpressions;

namespace Mostik.Core;

public static class TextPolicy
{
    public const int MaxCharacters = 1500;

    public static string Phrase(string? text)
    {
        string value = Regex.Replace(text ?? "", @"\s+", " ").Trim();
        if (value.Length == 0) throw new ArgumentException("Речь не распознана. Повторите или введите текст.");
        if (value.Length > MaxCharacters) throw new ArgumentException("Слишком длинная фраза. Разбейте её на части.");
        return value;
    }

    // Vosk owns the native JSON string. The caller copies it before the next native call.
    public static string ReadVoskText(string json, bool partial = false)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.TryGetProperty(partial ? "partial" : "text", out var value)
            && value.ValueKind == JsonValueKind.String ? value.GetString()?.Trim() ?? "" : "";
    }
}
