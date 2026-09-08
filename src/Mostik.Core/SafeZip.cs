using System.IO.Compression;

namespace Mostik.Core;

public static class SafeZip
{
    public static string Resolve(string destination, string entryName)
    {
        string name = entryName.Replace('\\', '/');
        if (name.Length == 0 || name.StartsWith('/') || name.Contains(':')
            || name.Split('/').Any(p => p is ".." or ".") || name.Contains('\0'))
            throw new InvalidDataException("Небезопасное имя внутри архива.");
        string root = Path.GetFullPath(destination).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string result = Path.GetFullPath(Path.Combine(root, name));
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!result.StartsWith(root, comparison)) throw new InvalidDataException("Выход за каталог распаковки.");
        return result;
    }

    public static async Task ExtractAsync(string archive, string destination, string expectedRoot,
        long limit, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(destination);
        using var zip = ZipFile.OpenRead(archive);
        if (zip.Entries.Count > 4096) throw new InvalidDataException("Слишком много файлов в архиве.");
        long total = 0;
        byte[] buffer = new byte[65536];
        foreach (var entry in zip.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string name = entry.FullName.Replace('\\', '/');
            if (!name.StartsWith(expectedRoot + "/", StringComparison.Ordinal))
                throw new InvalidDataException("Структура пакета не соответствует ожидаемой модели.");
            if (((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000)
                throw new InvalidDataException("Символические ссылки запрещены.");
            string path = Resolve(destination, name);
            if (name.EndsWith('/')) { Directory.CreateDirectory(path); continue; }
            if (entry.Length < 0 || entry.Length > limit - total)
                throw new InvalidDataException("Превышен допустимый размер распаковки.");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using var input = entry.Open();
            using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                65536, useAsync: true);
            long written = 0;
            int count;
            while ((count = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) != 0)
            {
                written += count;
                total += count;
                if (total > limit || written > entry.Length) throw new InvalidDataException("Превышен размер архива.");
                await output.WriteAsync(buffer.AsMemory(0, count), cancellationToken).ConfigureAwait(false);
            }
            if (written != entry.Length) throw new InvalidDataException("Неполный файл в архиве.");
        }
    }
}
