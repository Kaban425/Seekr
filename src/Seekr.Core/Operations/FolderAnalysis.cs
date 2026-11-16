using System.Security.Cryptography;

namespace Seekr.Core.Operations;

public sealed record AnalysisItem(string Path, long Bytes, string Reason, string Group);
public sealed record AnalysisReport(IReadOnlyList<AnalysisItem> Items, int Skipped, bool Truncated);

/// <summary>Read-only, bounded scans. Links are never followed; deletion is a separate user action.</summary>
public static class FolderAnalysis
{
    public const int EntryLimit = 100_000;
    public static AnalysisReport Scan(string folder, bool duplicates, IProgress<string>? progress = null, CancellationToken token = default)
    {
        folder = Path.GetFullPath(folder);
        FileTransfer.EnsureNoReparse(folder);
        long lastProgress = 0;
        void Report(string path)
        {
            if (Environment.TickCount64 - lastProgress < 150) return;
            lastProgress = Environment.TickCount64; progress?.Report(path);
        }
        var files = new List<FileInfo>();
        var candidates = new List<AnalysisItem>();
        var pending = new Stack<string>(); pending.Push(folder);
        var skipped = 0; var visited = 0; var truncated = false;
        var options = new EnumerationOptions { RecurseSubdirectories = false, IgnoreInaccessible = false, AttributesToSkip = 0 };
        while (pending.TryPop(out var directory))
        {
            token.ThrowIfCancellationRequested();
            Report(directory);
            try
            {
                FileTransfer.EnsureNoReparse(directory);
                var empty = true;
                foreach (var path in Directory.EnumerateFileSystemEntries(directory, "*", options))
                {
                    token.ThrowIfCancellationRequested(); empty = false;
                    if (++visited > EntryLimit) { truncated = true; break; }
                    try
                    {
                        var attributes = File.GetAttributes(path);
                        if (attributes.HasFlag(FileAttributes.ReparsePoint) && (attributes.HasFlag(FileAttributes.Directory) ? new DirectoryInfo(path).LinkTarget : new FileInfo(path).LinkTarget) is not null) { skipped++; continue; }
                        if (attributes.HasFlag(FileAttributes.Directory)) pending.Push(path);
                        else
                        {
                            var info = new FileInfo(path);
                            if (duplicates) files.Add(info);
                            else
                            {
                                var extension = info.Extension.ToLowerInvariant();
                                var oldInstaller = extension is ".exe" or ".msi" or ".msix" or ".iso"
                                    && info.LastWriteTimeUtc < DateTime.UtcNow.AddDays(-90);
                                var largeArchive = extension is ".zip" or ".rar" or ".7z" or ".tar" or ".gz"
                                    && info.Length >= 100L * 1024 * 1024;
                                if (oldInstaller || largeArchive)
                                    candidates.Add(new(path, info.Length, oldInstaller ? "Установщик / образ старше 90 дней — проверьте назначение" : "Архив от 100 МБ", "Кандидаты на очистку"));
                            }
                        }
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { skipped++; }
                }
                if (!duplicates && empty && directory != folder) candidates.Add(new(directory, 0, "Пустая папка", "Кандидаты на очистку"));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { skipped++; }
            if (truncated) break;
        }
        if (!duplicates) return new(candidates.OrderByDescending(x => x.Bytes).ToArray(), skipped, truncated);
        var groupNumber = 0;
        foreach (var sizeGroup in files.GroupBy(x => x.Length).Where(x => x.Count() > 1))
        {
            var hashes = new Dictionary<string, List<FileInfo>>(StringComparer.Ordinal);
            foreach (var file in sizeGroup)
            {
                token.ThrowIfCancellationRequested(); Report(file.FullName);
                try
                {
                    FileTransfer.EnsureNoReparse(file.FullName);
                    using var stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.Read);
                    var hash = Fingerprint(stream, token);
                    if (!hashes.TryGetValue(hash, out var group)) hashes[hash] = group = [];
                    group.Add(file);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { skipped++; }
            }
            foreach (var group in hashes.Values.Where(x => x.Count > 1))
            {
                var label = $"Группа {++groupNumber} · {group.Count} копии";
                foreach (var file in group) candidates.Add(new(file.FullName, file.Length, "Совпадают размер и SHA-256 содержимого", label));
            }
        }
        return new(candidates, skipped, truncated);
    }

    public static void ValidateDuplicateSelection(IReadOnlyList<AnalysisItem> all, IReadOnlySet<string> selected, CancellationToken token = default)
    {
        foreach (var group in all.GroupBy(x => x.Group).Where(g => g.Any(x => selected.Contains(x.Path))))
        {
            var keep = group.FirstOrDefault(x => !selected.Contains(x.Path))
                ?? throw new IOException("Оставьте хотя бы одну копию в каждой группе.");
            string Hash(string path)
            {
                token.ThrowIfCancellationRequested();
                FileTransfer.EnsureNoReparse(path);
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                return Fingerprint(stream, token);
            }
            var expected = Hash(keep.Path);
            foreach (var item in group.Where(x => selected.Contains(x.Path)))
                if (Hash(item.Path) != expected) throw new IOException("Файл изменился после сканирования. Повторите поиск дубликатов: " + item.Path);
        }
    }

    internal static string Fingerprint(Stream stream, CancellationToken token)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[128 * 1024];
        int count;
        while ((count = stream.Read(buffer)) != 0) { token.ThrowIfCancellationRequested(); hash.AppendData(buffer, 0, count); }
        return Convert.ToHexString(hash.GetHashAndReset());
    }
}
