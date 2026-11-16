using System.IO;
using Seekr.Core.Index;
using Seekr.Core.Search;
using Seekr.Indexer;

namespace Seekr.App.Services;

public sealed record WorkspaceSearchRequest(SearchQuery Query, string? Path, string? SearchPath,
    NameIndex? Index, int Limit, SearchOrder Order, bool Descending, bool ShowHidden, bool ShowSystem, bool ForceLiveScope = false, bool SearchContents = false);

/// <summary>Executes one immutable request off the UI thread; owns no view state or persistent path cache.</summary>
public static class WorkspaceSearchService
{
    public static Task<DirectoryListing> ExecuteAsync(WorkspaceSearchRequest request, CancellationToken token, IProgress<ContentScanProgress>? progress = null)
        => Task.Run(() => request.SearchContents && !request.Query.IsEmpty ? ContentSearchService.Search(request, token, progress) : Execute(request, token), token);

    private static DirectoryListing Execute(WorkspaceSearchRequest request, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (request.Query.IsEmpty)
        {
            if (!string.IsNullOrEmpty(request.Path)) return DirectoryBrowser.Read(request.Path, request.ShowHidden, request.ShowSystem, token);
            var drives = new List<SearchResult>();
            var space = new Dictionary<string, DriveSpace>(StringComparer.OrdinalIgnoreCase);
            foreach (var drive in DriveInfo.GetDrives())
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    if (drive.IsReady)
                    {
                        var capacity = new DriveSpace(drive.TotalSize, drive.AvailableFreeSpace, drive.DriveFormat);
                        space[drive.Name] = capacity;
                        drives.Add(new(new FileEntry(drives.Count, -1, drive.Name, true, 0, DateTime.MinValue, DateTime.MinValue), drive.Name, capacity.Total));
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
            return new(drives, null, DriveSpaces: space);
        }

        var index = request.Index;
        var rebuilt = false;
        if (request.SearchPath is not null && (request.ForceLiveScope || index is null || !index.RootPaths.Any(root =>
            request.SearchPath.Equals(root.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)
            || request.SearchPath.StartsWith(root.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase))))
            { index = new NameIndex(DirectoryScanner.Scan(request.SearchPath, token), token); rebuilt = true; }
        if (index is null) return new([], null);
        bool Visible(string path)
        {
            if (request.ShowHidden && request.ShowSystem) return true;
            try
            {
                var attributes = File.GetAttributes(path);
                return (request.ShowHidden || !attributes.HasFlag(FileAttributes.Hidden))
                    && (request.ShowSystem || !attributes.HasFlag(FileAttributes.System));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
        }
        var matches = index.Search(request.Query, request.Limit, request.SearchPath, Visible, request.Order, request.Descending, token);
        var driveSpaces = new Dictionary<string, DriveSpace>(StringComparer.OrdinalIgnoreCase);
        foreach (var match in matches.Where(x => x.Entry.IsDirectory))
        {
            token.ThrowIfCancellationRequested();
            if (!string.Equals(Path.GetPathRoot(match.FullPath)?.TrimEnd('\\'), match.FullPath.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) continue;
            try
            {
                var drive = new DriveInfo(match.FullPath);
                if (drive.IsReady) driveSpaces[match.FullPath] = new(drive.TotalSize, drive.AvailableFreeSpace, drive.DriveFormat);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { }
        }
        return new(matches, null, rebuilt ? index : null, DriveSpaces: driveSpaces);
    }
}
