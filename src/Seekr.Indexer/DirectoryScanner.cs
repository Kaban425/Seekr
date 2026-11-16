using Seekr.Core.Index;

namespace Seekr.Indexer;

/// <summary>
/// Builds index entries by walking a directory tree. Works without admin rights and on any
/// file system; it is the fallback for the faster NTFS MFT reader.
/// </summary>
public static class DirectoryScanner
{
    private static readonly EnumerationOptions Options = new()
    {
        IgnoreInaccessible = true,
        RecurseSubdirectories = false,
        // Skip junctions and symlinks so the walk cannot loop or leave the tree.
        AttributesToSkip = 0,
        ReturnSpecialDirectories = false,
    };

    /// <summary>
    /// Scans <paramref name="rootPath"/> breadth-first. The root entry's name is the full root
    /// path, so parents always come before their children as <see cref="NameIndex"/> requires.
    /// </summary>
    public static IReadOnlyList<FileEntry> Scan(string rootPath, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);

        cancellationToken.ThrowIfCancellationRequested();
        var root = new DirectoryInfo(Path.GetFullPath(rootPath));
        if (!root.Exists)
        {
            throw new DirectoryNotFoundException($"Directory not found: {root.FullName}");
        }

        var entries = new List<FileEntry>
        {
            new(0, FileEntry.NoParent, root.FullName, true, 0, root.LastWriteTimeUtc, root.CreationTimeUtc),
        };
        var pending = new Queue<(DirectoryInfo Directory, int Id)>();
        pending.Enqueue((root, 0));

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (directory, parentId) = pending.Dequeue();

            foreach (var info in EnumerateSafely(directory, cancellationToken))
            {
                try
                {
                    if (info.Attributes.HasFlag(FileAttributes.ReparsePoint) && info.LinkTarget is not null) continue;
                    var id = entries.Count;
                    var isDirectory = info is DirectoryInfo;
                    var size = info is FileInfo file ? file.Length : 0;
                    entries.Add(new FileEntry(id, parentId, info.Name, isDirectory, size, info.LastWriteTimeUtc, info.CreationTimeUtc));

                    if (info is DirectoryInfo child)
                    {
                        pending.Enqueue((child, id));
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
        }

        return entries;
    }

    private static IEnumerable<FileSystemInfo> EnumerateSafely(DirectoryInfo directory, CancellationToken token)
    {
        IEnumerator<FileSystemInfo> iterator;
        try { iterator = directory.EnumerateFileSystemInfos("*", Options).GetEnumerator(); }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException) { yield break; }
        using (iterator)
        {
            while (true)
            {
                token.ThrowIfCancellationRequested();
                FileSystemInfo current;
                try { if (!iterator.MoveNext()) yield break; current = iterator.Current; }
                catch (Exception ex) when (ex is UnauthorizedAccessException or IOException) { yield break; }
                yield return current;
            }
        }
    }
}
