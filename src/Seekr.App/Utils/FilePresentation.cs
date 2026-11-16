using System.IO;
using System.Windows.Media;
using Seekr.App.Icons;

namespace Seekr.App.Utils;

/// <summary>Bounded category-based icon lookup; never reads icons/logos from Windows or installed applications.</summary>
public static class FilePresentation
{
    public static ImageSource GetIcon(bool isDirectory, string? extension) => IconArtwork.Image(isDirectory ? IconKind.Folder : Category(extension));
    public static ImageSource GetPathIcon(string path) => IconArtwork.Image(
        string.Equals(Path.GetPathRoot(path)?.TrimEnd('\\', '/'), path.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase) ? IconKind.Drive : IconKind.Folder);

    public static IconKind Category(string? extension) => extension?.TrimStart('.').ToLowerInvariant() switch
    {
        "pdf" => IconKind.Pdf,
        "txt" or "md" or "rtf" or "doc" or "docx" or "odt" or "csv" or "xls" or "xlsx" or "ppt" or "pptx" => IconKind.Document,
        "jpg" or "jpeg" or "png" or "gif" or "webp" or "bmp" or "svg" or "ico" or "heic" or "tiff" => IconKind.Picture,
        "mp3" or "wav" or "flac" or "ogg" or "aac" or "m4a" => IconKind.Music,
        "mp4" or "mkv" or "avi" or "mov" or "webm" or "wmv" => IconKind.Video,
        "zip" or "rar" or "7z" or "tar" or "gz" or "bz2" => IconKind.Archive,
        "cs" or "js" or "ts" or "tsx" or "jsx" or "py" or "go" or "rs" or "cpp" or "h" or "html" or "css" or "json" or "xml" or "yaml" or "yml" or "xaml" => IconKind.Code,
        "exe" or "msi" or "msix" or "appx" or "dll" or "lnk" => IconKind.Application,
        _ => IconKind.File,
    };

    public static string GetTypeName(bool isDirectory, string? extension)
    {
        if (isDirectory) return "Папка с файлами";
        if (string.IsNullOrEmpty(extension)) return "Файл";
        var format = extension.TrimStart('.').ToUpperInvariant();
        if (format == "LNK") return "Ярлык";
        if (format == "DLL") return "Библиотека DLL";
        return Category(extension) switch
        {
            IconKind.Pdf => "Документ PDF", IconKind.Document => format == "TXT" ? "Текстовый документ" : "Документ " + format,
            IconKind.Picture => "Изображение " + format, IconKind.Music => "Аудиофайл " + format,
            IconKind.Video => "Видеофайл " + format, IconKind.Archive => "Архив " + format,
            IconKind.Code => "Исходный код " + format, IconKind.Application => format == "EXE" ? "Приложение" : "Пакет " + format,
            _ => format + "-файл",
        };
    }
}
