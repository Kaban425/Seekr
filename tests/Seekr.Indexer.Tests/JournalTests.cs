using FluentAssertions;
using Seekr.Indexer.Mft;
using Seekr.Indexer.Service;

namespace Seekr.Indexer.Tests;

public class JournalTests
{
    [Fact]
    public void OldCursors_DoNotAllowUnboundedJournalScanning()
    {
        var checkpoint = new JournalCheckpoint(1, 100);
        checkpoint.IsWithinReadBudget(100 + JournalCheckpoint.MaxReadLag).Should().BeTrue();
        checkpoint.IsWithinReadBudget(101 + JournalCheckpoint.MaxReadLag).Should().BeFalse();
        checkpoint.IsWithinReadBudget(99).Should().BeFalse();
        new JournalCheckpoint(1, -1).IsWithinReadBudget(100).Should().BeFalse();
    }

    [Fact]
    public void NativeProbe_CancelledBeforeOpeningVolume()
    {
        using var stop = new CancellationTokenSource();
        stop.Cancel();
        Action probe = () => MftVolumeReader.ProbeJournal('C', default, stop.Token);
        probe.Should().Throw<OperationCanceledException>();
    }

    [Theory]
    [InlineData(7UL, 100L, 7UL, 50L, 120L, true)]
    [InlineData(7UL, 120L, 7UL, 50L, 120L, true)]
    [InlineData(7UL, 100L, 8UL, 50L, 120L, false)]
    [InlineData(7UL, 10L, 7UL, 50L, 120L, false)]
    [InlineData(7UL, 130L, 7UL, 50L, 120L, false)]
    [InlineData(0UL, 0L, 7UL, 0L, 120L, false)]
    public void Checkpoint_RejectsResetWrappedOrFuturePositions(ulong id, long position,
        ulong currentId, long first, long next, bool expected)
    {
        new JournalCheckpoint(id, position).CanResume(currentId, first, next).Should().Be(expected);
    }

    [Theory]
    [InlineData(JournalChangeKind.Baseline)]
    [InlineData(JournalChangeKind.Unchanged)]
    [InlineData(JournalChangeKind.Changed)]
    [InlineData(JournalChangeKind.Unavailable)]
    public void Wire_RoundTripsFixedSizeResponse(JournalChangeKind kind)
    {
        var probe = new JournalProbe(kind, new(19, 123456));
        var bytes = JournalWire.Encode(probe);
        bytes.Should().HaveCount(JournalWire.ResponseLength);
        JournalWire.Decode(bytes).Should().Be(probe);
    }

    [Fact]
    public void Wire_RejectsTruncatedUnknownAndNegativeResponses()
    {
        var bytes = JournalWire.Encode(new(JournalChangeKind.Changed, new(1, 5)));
        Action truncated = () => JournalWire.Decode(bytes.AsSpan(0, 8));
        truncated.Should().Throw<InvalidDataException>();
        bytes[4] = 255;
        Action unknown = () => JournalWire.Decode(bytes);
        unknown.Should().Throw<InvalidDataException>();
        bytes[4] = (byte)JournalChangeKind.Changed;
        Array.Fill(bytes, (byte)255, 13, 8);
        unknown.Should().Throw<InvalidDataException>();
    }
}
