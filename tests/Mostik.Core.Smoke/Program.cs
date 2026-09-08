using System.IO.Compression;
using System.Text.Json;
using Mostik.Core;

// Проверяем только реально значимые правила; Android/Vosk/ML Kit эти тесты НЕ эмулируют.
int passed = 0, failed = 0;
async Task Check(string name, Func<Task> test)
{
    try { await test(); Console.WriteLine("PASS " + name); passed++; }
    catch (Exception ex) { Console.Error.WriteLine("FAIL " + name + ": " + ex); failed++; }
}
void Require(bool value) { if (!value) throw new Exception("Проверяемое условие не выполнено."); }
Task Sync(Action action) { action(); return Task.CompletedTask; }
async Task Throws<T>(Func<Task> action) where T : Exception
{
    try { await action(); } catch (T) { return; }
    throw new Exception("Ожидалось исключение " + typeof(T).Name);
}

await Check("направления RU/PL", () => Sync(() => {
    Require(Direction.FromSource("ru") == Direction.RuToPl);
    Require(Direction.FromSource("pl") == Direction.PlToRu);
    Require(Direction.RuToPl.Target == "pl" && Direction.PlToRu.Target == "ru");
}));
await Check("неизвестный язык отклоняется", () => Throws<ArgumentException>(() => Sync(() => Direction.FromSource("en"))));
await Check("очистка пробелов", () => Sync(() => Require(TextPolicy.Phrase("  Добрый\n день\t ") == "Добрый день")));
await Check("отрицания и числа не меняются", () => Sync(() => Require(TextPolicy.Phrase("Не 15, а 50. Не надо.") == "Не 15, а 50. Не надо.")));
await Check("польские символы сохраняются", () => Sync(() => Require(TextPolicy.Phrase("Dzień dobry. Nie potrzebuję torby.") == "Dzień dobry. Nie potrzebuję torby.")));
await Check("пустая фраза запрещена", () => Throws<ArgumentException>(() => Sync(() => TextPolicy.Phrase(" \n "))));
await Check("null-фраза запрещена", () => Throws<ArgumentException>(() => Sync(() => TextPolicy.Phrase(null))));
await Check("ограничение длины", () => Throws<ArgumentException>(() => Sync(() => TextPolicy.Phrase(new string('a', TextPolicy.MaxCharacters + 1)))));
await Check("граничная длина", () => Sync(() => Require(TextPolicy.Phrase(new string('а', TextPolicy.MaxCharacters)).Length == TextPolicy.MaxCharacters)));
await Check("Vosk final JSON", () => Sync(() => Require(TextPolicy.ReadVoskText("{\"text\":\" привет \"}") == "привет")));
await Check("Vosk partial JSON", () => Sync(() => Require(TextPolicy.ReadVoskText("{\"partial\":\"dzień\"}", true) == "dzień")));
await Check("Vosk отсутствующее поле", () => Sync(() => Require(TextPolicy.ReadVoskText("{}") == "")));
await Check("Vosk неверный тип поля", () => Sync(() => Require(TextPolicy.ReadVoskText("{\"text\":5}") == "")));
await Check("Vosk повреждённый JSON", () => Throws<JsonException>(() => Sync(() => TextPolicy.ReadVoskText("{invalid"))));
await Check("голос с сетью отклоняется", () => Sync(() => Require(!VoicePolicy.IsOfflineReady(true, []))));
await Check("неустановленный голос отклоняется", () => Sync(() => Require(!VoicePolicy.IsOfflineReady(false, ["notInstalled"]))));
await Check("установленный офлайн-голос разрешён", () => Sync(() => Require(VoicePolicy.IsOfflineReady(false, ["embeddedTts"]))));
await Check("голос без признаков загрузки", () => Sync(() => Require(VoicePolicy.IsOfflineReady(false, null))));
var spec = new ModelSpec("ru", "Русская речь", "vosk-model-small-ru-0.22",
    "https://alphacephei.com/vosk/models/vosk-model-small-ru-0.22.zip", new string('a', 64), 100, 1000);
await Check("корректный каталог", () => Sync(spec.Validate));
await Check("HTTP модели запрещён", () => Throws<InvalidDataException>(() => Sync((spec with { Url = spec.Url.Replace("https:", "http:") }).Validate)));
await Check("чужой сервер модели запрещён", () => Throws<InvalidDataException>(() => Sync((spec with { Url = spec.Url.Replace("alphacephei.com", "example.com") }).Validate)));
await Check("некорректный SHA запрещён", () => Throws<InvalidDataException>(() => Sync((spec with { Sha256 = "abc" }).Validate)));
await Check("чрезмерный размер запрещён", () => Throws<InvalidDataException>(() => Sync((spec with { MaxExtractedBytes = long.MaxValue }).Validate)));

string root = Path.Combine(Path.GetTempPath(), "mostik-smoke-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try
{
    foreach (string name in new[] { "../escape", "model/../../escape", "/root", "C:/root", "model\\..\\escape", "model/./a", "bad\0name" })
        await Check("небезопасный ZIP-путь " + name.Replace('\0', '?'), () => Throws<InvalidDataException>(() => Sync(() => SafeZip.Resolve(root, name))));
    await Check("безопасный ZIP-путь", () => Sync(() => Require(SafeZip.Resolve(root, "model/file").StartsWith(root))));

    string MakeZip(params (string Name, string Text, bool Symlink)[] entries)
    {
        string file = Path.Combine(root, Guid.NewGuid().ToString("N") + ".zip");
        using var zip = ZipFile.Open(file, ZipArchiveMode.Create);
        foreach (var value in entries)
        {
            var entry = zip.CreateEntry(value.Name);
            if (value.Symlink) entry.ExternalAttributes = unchecked((int)(0xA000u << 16));
            using var writer = new StreamWriter(entry.Open()); writer.Write(value.Text);
        }
        return file;
    }
    string Destination() => Path.Combine(root, Guid.NewGuid().ToString("N"));
    await Check("распаковка корректного ZIP", async () => {
        string zip = MakeZip(("model/file", "проверка", false)), destination = Destination();
        await SafeZip.ExtractAsync(zip, destination, "model", 1000, CancellationToken.None);
        Require(File.ReadAllText(Path.Combine(destination, "model", "file")) == "проверка");
    });
    await Check("ZIP traversal запрещён", () => Throws<InvalidDataException>(() => SafeZip.ExtractAsync(MakeZip(("model/../../outside", "x", false)), Destination(), "model", 100, CancellationToken.None)));
    await Check("чужой корень ZIP запрещён", () => Throws<InvalidDataException>(() => SafeZip.ExtractAsync(MakeZip(("other/file", "x", false)), Destination(), "model", 100, CancellationToken.None)));
    await Check("symlink ZIP запрещён", () => Throws<InvalidDataException>(() => SafeZip.ExtractAsync(MakeZip(("model/link", "../outside", true)), Destination(), "model", 100, CancellationToken.None)));
    await Check("ZIP превышение лимита", () => Throws<InvalidDataException>(() => SafeZip.ExtractAsync(MakeZip(("model/file", new string('x', 1000), false)), Destination(), "model", 100, CancellationToken.None)));
    await Check("ZIP одинаковые имена запрещены", () => Throws<IOException>(() => SafeZip.ExtractAsync(MakeZip(("model/file", "a", false), ("model/file", "b", false)), Destination(), "model", 100, CancellationToken.None)));
    await Check("отмена распаковки", () => Throws<OperationCanceledException>(() => SafeZip.ExtractAsync(MakeZip(("model/file", "x", false)), Destination(), "model", 100, new CancellationToken(true))));
}
finally { Directory.Delete(root, true); }
Console.WriteLine($"Core smoke: {passed} PASS, {failed} FAIL. Android и телефон здесь не проверялись.");
return failed == 0 ? 0 : 1;
