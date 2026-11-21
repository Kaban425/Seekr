using System.IO;
using Seekr.Core.Index;

namespace Seekr.App.Services;

public sealed record DriveSpace(long Total, long Free, string Format, long TotalFree);
public sealed record DirectoryListing(IReadOnlyList<SearchResult> Entries, string? Error, Seekr.Core.Index.NameIndex? Snapshot = null, string? Notice = null,
    IReadOnlyDictionary<string, DriveSpace>? DriveSpaces = null);

public static class DirectoryBrowser
{
    public static DirectoryListing Read(string path, bool showHidden, bool showSystem, CancellationToken token)
    {
        var entries = new List<SearchResult>();
        try
        {
            var options = new EnumerationOptions { IgnoreInaccessible = false, AttributesToSkip = 0 };
            foreach (var item in new DirectoryInfo(path).EnumerateFileSystemInfos("*", options))
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    if ((!showHidden && item.Attributes.HasFlag(FileAttributes.Hidden)) || (!showSystem && item.Attributes.HasFlag(FileAttributes.System))) continue;
                    var directory = item.Attributes.HasFlag(FileAttributes.Directory);
                    var size = item is FileInfo file ? file.Length : -1;
                    entries.Add(new(new FileEntry(entries.Count, -1, item.Name, directory, directory ? 0 : size,
                        item.LastWriteTimeUtc, item.CreationTimeUtc), item.FullName, size));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
            return new(entries.OrderByDescending(x => x.Entry.IsDirectory).ThenBy(x => x.Entry.Name, StringComparer.OrdinalIgnoreCase).ToArray(), null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        { return new(entries, "Не удалось прочитать папку: " + ex.Message); }
    }
}
