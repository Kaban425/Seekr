using System.ComponentModel;
using Seekr.Indexer.Mft;
using Seekr.Indexer.Service;

namespace Seekr.Indexer;

/// <summary>
/// Tracks journal cursors separately from index loading. A failed refresh stays dirty until
/// acknowledged; positions are captured before loading so changes during a scan are not lost.
/// Only one caller may poll/acknowledge this object at a time.
/// </summary>
public sealed class IndexChangeMonitor(
    Func<IReadOnlyList<char>> drives,
    Func<char, JournalCheckpoint, CancellationToken, JournalProbe?> probe,
    TimeProvider? timeProvider = null)
{
    private static readonly TimeSpan FallbackInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan PermissionRefreshInterval = TimeSpan.FromMinutes(1);
    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;
    private readonly Dictionary<char, JournalCheckpoint> _positions = [];
    private DateTimeOffset _refreshedAt = DateTimeOffset.MinValue;
    private DateTimeOffset _retryAt = DateTimeOffset.MinValue;
    private bool _dirty = true;

    public bool UsesJournal { get; private set; }

    public static IndexChangeMonitor CreateDefault() => new(
        () => IndexLoader.FixedNtfsDrives().ToArray(),
        (letter, checkpoint, token) => IndexPipe.TryProbe(letter, checkpoint, token)
            ?? MftVolumeReader.ProbeJournal(letter, checkpoint, token));

    public bool Poll(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_clock.GetUtcNow() < _retryAt)
        {
            return _dirty || _clock.GetUtcNow() - _refreshedAt >= FallbackInterval;
        }

        var currentDrives = drives();
        if (!_positions.Keys.ToHashSet().SetEquals(currentDrives))
        {
            _dirty = true;
        }

        foreach (var removed in _positions.Keys.Except(currentDrives).ToArray())
        {
            _positions.Remove(removed);
        }

        UsesJournal = currentDrives.Count > 0;
        foreach (var letter in currentDrives)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var result = probe(letter, _positions.GetValueOrDefault(letter), cancellationToken);
                if (result is null || result.Kind == JournalChangeKind.Unavailable)
                {
                    UsesJournal = false;
                    continue;
                }

                _positions[letter] = result.Checkpoint;
                _dirty |= result.Kind is JournalChangeKind.Changed or JournalChangeKind.Baseline;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Win32Exception
                or TimeoutException || (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested))
            {
                UsesJournal = false;
            }
        }

        if (!UsesJournal)
        {
            _retryAt = _clock.GetUtcNow() + FallbackInterval;
        }

        return _dirty || _clock.GetUtcNow() - _refreshedAt >=
            (UsesJournal ? PermissionRefreshInterval : FallbackInterval);
    }

    public void AcknowledgeRefresh()
    {
        _dirty = false;
        _refreshedAt = _clock.GetUtcNow();
    }
}
