using FluentAssertions;
using Seekr.Indexer.Mft;

namespace Seekr.Indexer.Tests;

public class IndexChangeMonitorTests
{
    private sealed class Clock : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(int seconds) => _now = _now.AddSeconds(seconds);
    }

    [Fact]
    public void FailedRefresh_RemainsDirtyUntilAcknowledged()
    {
        var kind = JournalChangeKind.Baseline;
        var monitor = new IndexChangeMonitor(() => ['C'], (_, _, _) => new(kind, new(1, 100)));
        monitor.Poll(default).Should().BeTrue();
        monitor.AcknowledgeRefresh();
        kind = JournalChangeKind.Unchanged;
        monitor.Poll(default).Should().BeFalse();
        kind = JournalChangeKind.Changed;
        monitor.Poll(default).Should().BeTrue();
        kind = JournalChangeKind.Unchanged;
        monitor.Poll(default).Should().BeTrue("a failed index load must not discard the dirty flag");
        monitor.AcknowledgeRefresh();
        monitor.Poll(default).Should().BeFalse();
    }

    [Fact]
    public void Poll_ReplaysFromPositionCapturedBeforeRefresh()
    {
        var seen = new List<JournalCheckpoint>();
        long next = 100;
        var monitor = new IndexChangeMonitor(() => ['C'], (_, previous, _) =>
        {
            seen.Add(previous);
            return new(previous == default ? JournalChangeKind.Baseline : JournalChangeKind.Changed, new(1, next));
        });
        monitor.Poll(default);
        next = 200; // files change while the initial index is being loaded
        monitor.AcknowledgeRefresh();
        monitor.Poll(default).Should().BeTrue();
        seen.Should().Equal(default, new JournalCheckpoint(1, 100));
    }

    [Fact]
    public void UnavailableJournal_BacksOffAndRefreshesPeriodically()
    {
        var clock = new Clock();
        var calls = 0;
        var monitor = new IndexChangeMonitor(() => ['C'], (_, _, _) =>
        {
            calls++;
            throw new EndOfStreamException("older service");
        }, clock);
        monitor.Poll(default).Should().BeTrue();
        monitor.AcknowledgeRefresh();
        clock.Advance(10);
        monitor.Poll(default).Should().BeFalse();
        calls.Should().Be(1);
        clock.Advance(21);
        monitor.Poll(default).Should().BeTrue();
        monitor.UsesJournal.Should().BeFalse();
    }

    [Fact]
    public void RemovedDrive_TriggersRefresh()
    {
        char[] drives = ['C', 'D'];
        var monitor = new IndexChangeMonitor(() => drives, (_, _, _) => new(JournalChangeKind.Unchanged, new(1, 100)));
        monitor.Poll(default);
        monitor.AcknowledgeRefresh();
        drives = ['C'];
        monitor.Poll(default).Should().BeTrue();
    }

    [Fact]
    public void Permissions_AreRevalidatedEvenWithoutJournalEvents()
    {
        var clock = new Clock();
        var monitor = new IndexChangeMonitor(() => ['C'], (_, _, _) => new(JournalChangeKind.Unchanged, new(1, 100)), clock);
        monitor.Poll(default);
        monitor.AcknowledgeRefresh();
        clock.Advance(61);
        monitor.Poll(default).Should().BeTrue();
    }

    [Fact]
    public void Cancellation_IsNotConvertedIntoFallback()
    {
        var monitor = new IndexChangeMonitor(() => ['C'], (_, _, token) =>
        {
            token.ThrowIfCancellationRequested();
            return null;
        });
        using var stop = new CancellationTokenSource();
        stop.Cancel();
        Action poll = () => monitor.Poll(stop.Token);
        poll.Should().Throw<OperationCanceledException>();
    }
}
