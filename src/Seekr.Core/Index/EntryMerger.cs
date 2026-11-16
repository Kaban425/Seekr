namespace Seekr.Core.Index;

/// <summary>Joins per-volume entry lists into one list that <see cref="NameIndex"/> accepts.</summary>
public static class EntryMerger
{
    public static IReadOnlyList<FileEntry> Concat(IReadOnlyList<IReadOnlyList<FileEntry>> volumes)
    {
        ArgumentNullException.ThrowIfNull(volumes);

        var merged = new List<FileEntry>(volumes.Sum(v => v.Count));
        foreach (var volume in volumes)
        {
            var offset = merged.Count;
            foreach (var entry in volume)
            {
                var parent = entry.ParentId == FileEntry.NoParent ? FileEntry.NoParent : entry.ParentId + offset;
                merged.Add(entry with { Id = entry.Id + offset, ParentId = parent });
            }
        }

        return merged;
    }
}
