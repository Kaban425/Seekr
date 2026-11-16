using System.IO;
using System.IO.Compression;
using System.Text;
using System.Xml;

namespace Seekr.App.Services;

public static class ContentReaders
{
    public const int MaxBytes = 8 * 1024 * 1024;
    public const int MaxCharacters = 2 * 1024 * 1024;
    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".txt", ".md", ".csv", ".log", ".json", ".xml", ".yaml", ".yml", ".ini", ".cfg", ".cs", ".py", ".js", ".ts", ".tsx", ".jsx", ".html", ".css", ".cpp", ".h", ".go", ".rs", ".sql", ".xaml" };

    public static bool Supports(string path) => Path.GetExtension(path).ToLowerInvariant() is ".docx" || TextExtensions.Contains(Path.GetExtension(path));

    public static string Read(string path, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (file.Length > MaxBytes) throw new IOException("Файл превышает лимит 8 МБ.");
        using var bytes = new MemoryStream();
        var buffer = new byte[16384];
        while (true)
        {
            token.ThrowIfCancellationRequested();
            var count = file.Read(buffer);
            if (count == 0) break;
            if (bytes.Length + count > MaxBytes) throw new IOException("Файл вырос во время чтения.");
            bytes.Write(buffer, 0, count);
        }
        bytes.Position = 0;
        string text;
        switch (Path.GetExtension(path).ToLowerInvariant())
        {
            case ".docx": text = ReadDocx(bytes, token); break;
            default:
                var data = bytes.ToArray();
                var unicode = data.Length >= 2 && ((data[0] == 0xff && data[1] == 0xfe) || (data[0] == 0xfe && data[1] == 0xff));
                if (!unicode && data.AsSpan(0, Math.Min(data.Length, 8192)).Contains((byte)0)) throw new IOException("Двоичные данные.");
                using (var reader = new StreamReader(bytes, new UTF8Encoding(false, true), true, 4096, true))
                    text = reader.ReadToEnd();
                break;
        }
        token.ThrowIfCancellationRequested();
        if (text.Length > MaxCharacters) throw new IOException("Слишком много извлечённого текста.");
        return text;
    }

    private static string ReadDocx(Stream file, CancellationToken token)
    {
        using var zip = new ZipArchive(file, ZipArchiveMode.Read, leaveOpen: true);
        if (zip.Entries.Count > 10000) throw new IOException("Слишком много частей DOCX.");
        var entry = zip.GetEntry("word/document.xml") ?? throw new IOException("Нет основного текста DOCX.");
        if (entry.Length > MaxCharacters) throw new IOException("Основной текст DOCX превышает лимит.");
        using var content = entry.Open();
        using var xml = XmlReader.Create(content, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null,
            MaxCharactersInDocument = MaxCharacters, MaxCharactersFromEntities = 1024 });
        var result = new StringBuilder();
        while (xml.Read())
        {
            token.ThrowIfCancellationRequested();
            if (xml.NodeType == XmlNodeType.Text) result.Append(xml.Value);
            else if (xml.NodeType == XmlNodeType.EndElement && xml.LocalName is "p" or "tc") result.Append('\n');
            else if (xml.NodeType == XmlNodeType.Element && xml.LocalName is "tab" or "br") result.Append(' ');
        }
        return result.ToString();
    }

}
