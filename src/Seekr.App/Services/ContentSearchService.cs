using System.Diagnostics;
using System.IO;
using Seekr.Core.Index;
using Seekr.Core.Search;

namespace Seekr.App.Services;

public sealed record ContentScanProgress(int Inspected, int Skipped);

public static class ContentSearchService
{
    public static DirectoryListing Search(WorkspaceSearchRequest request, CancellationToken token, IProgress<ContentScanProgress>? progress = null)
    {
        if (request.Query.Terms.Count == 0) return new([], "Для поиска содержимого введите хотя бы одно слово.");
        var roots = request.SearchPath is not null ? [request.SearchPath] : DriveInfo.GetDrives().Where(x => x.IsReady).Select(x => x.Name).ToArray();
        if (request.SearchPath is not null && !Directory.Exists(request.SearchPath)) return new([], "Папка поиска недоступна.");
        var folders = new Stack<string>(roots);
        var found = new List<SearchResult>();
        var queueLimited = false;
        var inspected = 0; var skipped = 0; var visited = 0; var files = 0;
        var started = Stopwatch.StartNew();
        bool Budget() => files < 10000 && visited < 5000 && started.Elapsed < TimeSpan.FromSeconds(15);
        while (folders.TryPop(out var folder) && Budget())
        {
            token.ThrowIfCancellationRequested(); visited++;
            try
            {
                if (new DirectoryInfo(folder).LinkTarget is not null) { skipped++; continue; }
                foreach (var item in new DirectoryInfo(folder).EnumerateFileSystemInfos("*", new EnumerationOptions { AttributesToSkip = 0, IgnoreInaccessible = false }))
                {
                    token.ThrowIfCancellationRequested();
                    if (!Budget()) break;
                    try
                    {
                        if (item.Attributes.HasFlag(FileAttributes.ReparsePoint) && item.LinkTarget is not null) { skipped++; continue; }
                        if ((!request.ShowHidden && item.Attributes.HasFlag(FileAttributes.Hidden)) || (!request.ShowSystem && item.Attributes.HasFlag(FileAttributes.System))) continue;
                        if (item is DirectoryInfo child) { if (folders.Count + visited < 5000) folders.Push(child.FullName); else { queueLimited = true; skipped++; } continue; }
                        files++;
                        if (item is not FileInfo file || !MetadataMatches(file, request.Query)) continue;
                        if (!ContentReaders.Supports(file.FullName)) { skipped++; continue; }
                        var text = ContentReaders.Read(file.FullName, token); inspected++;
                        if (request.Query.Terms.All(term => text.Contains(term, StringComparison.OrdinalIgnoreCase)))
                            found.Add(new(new FileEntry(found.Count, -1, file.Name, false, file.Length, file.LastWriteTimeUtc, file.CreationTimeUtc), file.FullName, file.Length));
                        if ((inspected + skipped) % 25 == 0) progress?.Report(new(inspected, skipped));
                    }
                    catch (OperationCanceledException) { throw; }
#pragma warning disable CA1031 // An untrusted/corrupt document must be skipped rather than break the scan.
                    catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
#pragma warning restore CA1031
                    { skipped++; }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { skipped++; }
        }
        token.ThrowIfCancellationRequested();
        IEnumerable<SearchResult> ordered = request.Order switch
        {
            SearchOrder.Size => request.Descending ? found.OrderByDescending(x => x.EffectiveSize) : found.OrderBy(x => x.EffectiveSize),
            SearchOrder.Modified => request.Descending ? found.OrderByDescending(x => x.Entry.ModifiedUtc) : found.OrderBy(x => x.Entry.ModifiedUtc),
            SearchOrder.Created => request.Descending ? found.OrderByDescending(x => x.Entry.CreatedUtc) : found.OrderBy(x => x.Entry.CreatedUtc),
            SearchOrder.Type => request.Descending ? found.OrderByDescending(x => x.Entry.Extension, StringComparer.OrdinalIgnoreCase) : found.OrderBy(x => x.Entry.Extension, StringComparer.OrdinalIgnoreCase),
            _ => request.Descending ? found.OrderByDescending(x => x.FullPath, StringComparer.OrdinalIgnoreCase) : found.OrderBy(x => x.FullPath, StringComparer.OrdinalIgnoreCase),
        };
        var limited = queueLimited || !Budget() || folders.Count > 0;
        return new(ordered.Take(request.Limit).ToArray(), null, Notice: $"Содержимое: проверено {inspected}, пропущено {skipped}"
            + (limited ? ". Достигнут лимит: 15 с / 10 000 файлов / 5000 папок; сузьте область." : "")
            + (found.Count > request.Limit ? $". Совпадений {found.Count}, показано {request.Limit}." : ""));
    }

    private static bool MetadataMatches(FileInfo file, SearchQuery query) =>
        (query.Extensions.Count == 0 || query.Extensions.Contains(file.Extension.TrimStart('.').ToLowerInvariant()))
        && (query.SizeMin is null || file.Length >= query.SizeMin) && (query.SizeMax is null || file.Length <= query.SizeMax)
        && (query.ModifiedMin is null || file.LastWriteTimeUtc >= query.ModifiedMin) && (query.ModifiedMax is null || file.LastWriteTimeUtc <= query.ModifiedMax)
        && (query.CreatedMin is null || file.CreationTimeUtc >= query.CreatedMin) && (query.CreatedMax is null || file.CreationTimeUtc <= query.CreatedMax);
}
