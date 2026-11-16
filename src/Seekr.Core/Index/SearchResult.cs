namespace Seekr.Core.Index;

public sealed record SearchResult(FileEntry Entry, string FullPath, long EffectiveSize)
{
    public SearchResult(FileEntry entry, string fullPath)
        : this(entry, fullPath, entry.IsDirectory ? 0 : entry.Size)
    {
    }
}
