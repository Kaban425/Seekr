namespace Seekr.Core.Index;

/// <summary>
/// One file or directory in the name index. <see cref="Id"/> equals the entry's position
/// in the index; roots (for example <c>C:\</c>) have <see cref="NoParent"/> as parent.
/// </summary>
public sealed record FileEntry(
    int Id,
    int ParentId,
    string Name,
    bool IsDirectory,
    long Size,
    DateTime ModifiedUtc,
    DateTime CreatedUtc)
{
    public const int NoParent = -1;
    /// <summary>Original NTFS file reference, including sequence. Service-only identity evidence; not persisted on the wire.</summary>
    public ulong FileReferenceNumber { get; init; }

    /// <summary>Lowercase extension without the dot, or empty for directories and extensionless files.</summary>
    public string Extension => IsDirectory ? string.Empty : Path.GetExtension(Name).TrimStart('.').ToLowerInvariant();
}
