using Seekr.Core.Index;

namespace Seekr.Indexer.Mft;

/// <summary>
/// Turns flat MFT records (in arbitrary order) into parent-first <see cref="FileEntry"/> items.
/// </summary>
public static class MftTreeBuilder
{
    /// <summary>MFT index of the volume's root directory on every NTFS volume.</summary>
    public const ulong RootIndex = 5;

    private const ulong IndexMask = 0x0000_FFFF_FFFF_FFFF;

    /// <summary>
    /// Builds entries breadth-first from the root. Records whose parent chain does not reach
    /// the root (orphans, cycles, metadata files) are left out.
    /// </summary>
    public static IReadOnlyList<FileEntry> Build(string rootName, IEnumerable<MftRecord> records, bool enrichMetadata = true, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootName);
        ArgumentNullException.ThrowIfNull(records);

        var children = GroupByParent(records, cancellationToken);
        var entries = new List<FileEntry>
        {
            new(0, FileEntry.NoParent, rootName, true, 0, DateTime.MinValue, DateTime.MinValue),
        };
        var pending = new Queue<(ulong Index, int Id)>();
        pending.Enqueue((RootIndex, 0));

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (parentIndex, parentId) = pending.Dequeue();
            if (!children.Remove(parentIndex, out var kids))
            {
                continue;
            }

            foreach (var record in kids)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var id = entries.Count;
                entries.Add(new FileEntry(id, parentId, record.Name, record.IsDirectory, 0, DateTime.MinValue, DateTime.MinValue) { FileReferenceNumber = record.Frn });
                if (record.IsDirectory)
                {
                    pending.Enqueue((record.Frn, id));
                }
            }
        }

        if (enrichMetadata)
        {
            EnrichMetadata(entries, cancellationToken);
        }

        return entries;
    }

    /// <summary>
    /// USN enumeration does not return sizes or timestamps, so fill them in from the file system.
    /// </summary>
    private static void EnrichMetadata(List<FileEntry> entries, CancellationToken token)
    {
        var paths = new string[entries.Count];
        paths[0] = entries[0].Name.TrimEnd('\\');
        for (var i = 1; i < entries.Count; i++)
        {
            token.ThrowIfCancellationRequested();
            paths[i] = Path.Combine(paths[entries[i].ParentId], entries[i].Name);
        }

        var options = new ParallelOptions { MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount / 2, 1, 4), CancellationToken = token };
        Parallel.For(0, entries.Count, options, i =>
        {
            try
            {
                if (entries[i].IsDirectory)
                {
                    var info = new DirectoryInfo(paths[i]);
                    if (info.Exists)
                    {
                        entries[i] = entries[i] with
                        {
                            ModifiedUtc = info.LastWriteTimeUtc,
                            CreatedUtc = info.CreationTimeUtc,
                        };
                    }
                }
                else
                {
                    var info = new FileInfo(paths[i]);
                    if (info.Exists)
                    {
                        entries[i] = entries[i] with
                        {
                            Size = info.Length,
                            ModifiedUtc = info.LastWriteTimeUtc,
                            CreatedUtc = info.CreationTimeUtc,
                        };
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                // Keep zero/min values for entries we cannot read.
            }
        });
    }

    private static Dictionary<ulong, List<MftRecord>> GroupByParent(IEnumerable<MftRecord> records, CancellationToken token)
    {
        var children = new Dictionary<ulong, List<MftRecord>>();
        foreach (var record in records)
        {
            token.ThrowIfCancellationRequested();
            if ((record.Frn & IndexMask) == RootIndex)
            {
                continue; // the root itself is represented by rootName
            }

            var parent = (record.ParentFrn & IndexMask) == RootIndex ? RootIndex : record.ParentFrn;
            if (!children.TryGetValue(parent, out var list))
            {
                list = [];
                children[parent] = list;
            }

            list.Add(record);
        }

        return children;
    }
}
