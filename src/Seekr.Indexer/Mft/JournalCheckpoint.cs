namespace Seekr.Indexer.Mft;

/// <summary>A position in one incarnation of an NTFS journal; never contains file names.</summary>
public readonly record struct JournalCheckpoint(ulong JournalId, long NextUsn)
{
    // USNs are byte offsets. Old positions trigger a rescan instead of a long journal walk.
    public const long MaxReadLag = 4 * 1024 * 1024;

    public bool IsWithinReadBudget(long currentNextUsn) => NextUsn >= 0 && currentNextUsn >= NextUsn
        && currentNextUsn - NextUsn <= MaxReadLag;

    public bool CanResume(ulong journalId, long firstUsn, long nextUsn) =>
        JournalId != 0 && JournalId == journalId && NextUsn >= firstUsn && NextUsn <= nextUsn;
}

public enum JournalChangeKind : byte
{
    Baseline,
    Unchanged,
    Changed,
    Unavailable,
}

public sealed record JournalProbe(JournalChangeKind Kind, JournalCheckpoint Checkpoint);
