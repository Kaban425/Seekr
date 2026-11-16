using Seekr.Core.Index;

namespace Seekr.Indexer.Service;

/// <summary>
/// Removes entries the requesting user could not see in Explorer. The MFT lists every file on
/// the volume, including other users' private folders, so the service must not hand it out as is.
/// </summary>
public static class AccessFilter
{
    /// <summary>
    /// Keeps an entry only if every folder above it can be listed by the user. A folder the user
    /// cannot open still appears by name (Explorer shows it too) but its contents are dropped,
    /// and nothing below it is checked.
    /// </summary>
    /// <param name="entries">Parent-first entries of one volume.</param>
    /// <param name="canList">
    /// Whether the user may list the directory at the given full path. Must be thread-safe when
    /// <paramref name="maxDegreeOfParallelism"/> is above 1.
    /// </param>
    /// <param name="maxDegreeOfParallelism">How many folders to check at once; folders of one depth run together.</param>
    public static IReadOnlyList<FileEntry> Filter(
        IReadOnlyList<FileEntry> entries, Func<string, bool> canList, int maxDegreeOfParallelism = 1, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(canList);
        return Filter(entries, (path, _) => canList(path), maxDegreeOfParallelism, cancellationToken);
    }

    /// <summary>Checks permission against the original directory identity, not just its current path.</summary>
    public static IReadOnlyList<FileEntry> Filter(IReadOnlyList<FileEntry> entries,
        Func<string, FileEntry, bool> canList, int maxDegreeOfParallelism = 1, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(canList);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxDegreeOfParallelism, 1);

        cancellationToken.ThrowIfCancellationRequested();
        var (paths, levels) = CollectDirectories(entries, cancellationToken);
        var listable = CheckLevels(entries, paths, levels, canList, maxDegreeOfParallelism, cancellationToken);
        return KeepVisible(entries, listable, cancellationToken);
    }

    /// <summary>Full path of every directory, and directory indexes grouped by depth.</summary>
    private static (string?[] Paths, List<List<int>> Levels) CollectDirectories(IReadOnlyList<FileEntry> entries, CancellationToken token)
    {
        var paths = new string?[entries.Count];
        var depths = new int[entries.Count];
        var levels = new List<List<int>>();

        for (var i = 0; i < entries.Count; i++)
        {
            token.ThrowIfCancellationRequested();
            var entry = entries[i];
            if (!entry.IsDirectory)
            {
                continue;
            }

            var parent = entry.ParentId;
            var isRoot = parent == FileEntry.NoParent;
            paths[i] = isRoot ? entry.Name : Path.Combine(paths[parent]!, entry.Name);
            depths[i] = isRoot ? 0 : depths[parent] + 1;

            while (levels.Count <= depths[i])
            {
                levels.Add([]);
            }

            levels[depths[i]].Add(i);
        }

        return (paths, levels);
    }

    private static bool[] CheckLevels(
        IReadOnlyList<FileEntry> entries, string?[] paths, List<List<int>> levels,
        Func<string, FileEntry, bool> canList, int maxDegreeOfParallelism, CancellationToken token)
    {
        var listable = new bool[entries.Count];
        var options = new ParallelOptions { MaxDegreeOfParallelism = maxDegreeOfParallelism, CancellationToken = token };

        foreach (var level in levels)
        {
            // Only folders whose parent was listable are reachable; skip everything else unchecked.
            var reachable = level.Where(i => entries[i].ParentId == FileEntry.NoParent || listable[entries[i].ParentId]);
            if (maxDegreeOfParallelism == 1)
            {
                foreach (var i in reachable)
                {
                    token.ThrowIfCancellationRequested();
                    listable[i] = canList(paths[i]!, entries[i]);
                }
            }
            else
            {
                // Each iteration writes only its own slot, so no locking is needed.
                Parallel.ForEach(reachable.ToList(), options, i => listable[i] = canList(paths[i]!, entries[i]));
            }
        }

        return listable;
    }

    private static List<FileEntry> KeepVisible(IReadOnlyList<FileEntry> entries, bool[] listable, CancellationToken token)
    {
        var newIds = new int[entries.Count];
        var result = new List<FileEntry>(entries.Count);

        for (var i = 0; i < entries.Count; i++)
        {
            token.ThrowIfCancellationRequested();
            var entry = entries[i];
            var parent = entry.ParentId;
            if (parent != FileEntry.NoParent && !listable[parent])
            {
                newIds[i] = -1;
                continue;
            }

            newIds[i] = result.Count;
            var newParent = parent == FileEntry.NoParent ? FileEntry.NoParent : newIds[parent];
            result.Add(entry with { Id = result.Count, ParentId = newParent });
        }

        return result;
    }
}
