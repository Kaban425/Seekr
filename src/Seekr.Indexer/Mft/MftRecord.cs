namespace Seekr.Indexer.Mft;

/// <summary>One file or directory as reported by the NTFS master file table.</summary>
/// <param name="Frn">File reference number: 48-bit MFT index plus 16-bit sequence.</param>
/// <param name="ParentFrn">Reference number of the containing directory.</param>
public sealed record MftRecord(ulong Frn, ulong ParentFrn, string Name, bool IsDirectory);
