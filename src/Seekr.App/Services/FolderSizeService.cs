using System.IO;

namespace Seekr.App.Services;

public sealed record FolderMeasurement(long Bytes, int Skipped, DateTime UpdatedAt)
{
    public string Description => (Skipped > 0 ? $"Неполный подсчёт: пропущено {Skipped}. " : "Подсчитано. ") + $"Обновлено {UpdatedAt:HH:mm:ss}";
}

public static class FolderSizeService
{
    public static FolderMeasurement Measure(string path, CancellationToken token)
    {
        long total = 0;
        var skipped = 0;
        var folders = new Stack<string>();
        folders.Push(path);
        while (folders.TryPop(out var folder))
        {
            token.ThrowIfCancellationRequested();
            try
            {
                if (new DirectoryInfo(folder).LinkTarget is not null) { skipped++; continue; }
                foreach (var item in new DirectoryInfo(folder).EnumerateFileSystemInfos("*", new EnumerationOptions { AttributesToSkip = 0, IgnoreInaccessible = false }))
                {
                    token.ThrowIfCancellationRequested();
                    try
                    {
                        if (item.Attributes.HasFlag(FileAttributes.ReparsePoint) && item.LinkTarget is not null) { skipped++; continue; }
                        if (item is DirectoryInfo directory) folders.Push(directory.FullName);
                        else if (item is FileInfo file) total = checked(total + file.Length);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OverflowException) { skipped++; }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OverflowException) { skipped++; }
        }
        return new(total, skipped, DateTime.Now);
    }
}
