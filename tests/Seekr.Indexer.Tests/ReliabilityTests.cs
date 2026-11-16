using System.Text;
using Seekr.Core.Index;
using Seekr.Indexer.Mft;
using Seekr.Indexer.Service;

namespace Seekr.Indexer.Tests;

public sealed class ReliabilityTests
{
    private static readonly FileEntry[] Entries = [
        new(0, -1, @"C:\", true, 0, DateTime.MinValue, DateTime.MinValue),
        new(1, 0, "private", true, 0, DateTime.MinValue, DateTime.MinValue) { FileReferenceNumber = 123 },
        new(2, 1, "secret.txt", false, 10, DateTime.MinValue, DateTime.MinValue)];

    [Fact]
    public void ReusedMftDirectoryNumberCannotInheritChildrenFromOldSequence()
    {
        const ulong oldParent = (1UL << 48) | 10;
        const ulong newParent = (2UL << 48) | 10;
        MftRecord[] records = [new(newParent, 5, "replacement", true), new(11, oldParent, "private.txt", false)];
        var entries = MftTreeBuilder.Build(@"C:\", records, false);
        Assert.DoesNotContain(entries, entry => entry.Name == "private.txt");
        Assert.Equal(newParent, entries.Single(entry => entry.Name == "replacement").FileReferenceNumber);
    }

    [Fact]
    public void CancellationDuringPermissionCheckStopsBeforeAnotherLevel()
    {
        using var cancellation = new CancellationTokenSource();
        Assert.ThrowsAny<OperationCanceledException>(() => AccessFilter.Filter(Entries,
            _ => { cancellation.Cancel(); return true; }, cancellationToken: cancellation.Token));
    }

    [Fact]
    public void ChangedDirectoryIdentityHidesSnapshotContents()
    {
        var filtered = AccessFilter.Filter(Entries, (_, entry) => entry.ParentId == -1 || entry.FileReferenceNumber == 999);
        Assert.DoesNotContain(filtered, entry => entry.Name == "secret.txt");
        Assert.Contains(filtered, entry => entry.Name == "private");
        Assert.False(DirectoryAccess.CanListSnapshot(Path.GetTempPath(), 0));
        Assert.False(DirectoryAccess.CanListSnapshot(Path.GetTempPath(), ulong.MaxValue));
    }

    [Fact]
    public void TreeBuildObservesCancellationWhileGroupingRecords()
    {
        using var cancellation = new CancellationTokenSource();
        IEnumerable<MftRecord> Records()
        {
            yield return new(10, 5, "first", true);
            cancellation.Cancel();
            yield return new(11, 5, "second", false);
        }
        Assert.ThrowsAny<OperationCanceledException>(() => MftTreeBuilder.Build(@"C:\", Records(), false, cancellation.Token));
    }

    [Fact]
    public async Task StreamedResponseMatchesLegacyFormatAndUsesBoundedWrites()
    {
        var entries = new List<FileEntry> { Entries[0] };
        for (var i = 1; i <= 10_000; i++) entries.Add(new(i, 0, $"file-{i}.txt", false, i, DateTime.MinValue, DateTime.MinValue));
        using var expected = new MemoryStream();
        IndexWire.Write(expected, entries);
        using var actual = new ChunkStream();
        await IndexWire.WriteAsync(actual, entries, CancellationToken.None);
        Assert.Equal(expected.ToArray(), actual.ToArray());
        Assert.InRange(actual.MaximumWrite, 1, 66_560);
        actual.Position = 0;
        Assert.Equal(entries, IndexWire.Read(actual));
    }

    [Fact]
    public async Task NonReadingClientCannotPreventResponseCancellation()
    {
        using var stream = new BlockedStream();
        using var cancellation = new CancellationTokenSource();
        var response = IndexWire.WriteAsync(stream, Entries, cancellation.Token);
        await stream.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => response.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Theory]
    [InlineData("..", 0, 0, 0)]
    [InlineData("folder\\file", 0, 0, 0)]
    [InlineData("file", -1, 0, 0)]
    [InlineData("file", 0, -1, 0)]
    [InlineData("file", 0, 0, 2)]
    public void MalformedResponseFieldsAreRejected(string name, long size, long ticks, byte flag)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, true);
        writer.Write("SKR2"u8); writer.Write(2);
        writer.Write(-1); writer.Write((byte)1); writer.Write(@"C:\"); writer.Write(0L); writer.Write(0L); writer.Write(0L);
        writer.Write(0); writer.Write(flag); writer.Write(name); writer.Write(size); writer.Write(ticks); writer.Write(0L);
        stream.Position = 0;
        Assert.Throws<InvalidDataException>(() => IndexWire.Read(stream));
    }

    [Fact]
    public async Task ConcurrentCacheSavesLeaveOneCompleteSnapshotAndCancellationKeepsIt()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "Seekr-cache-race-" + Guid.NewGuid().ToString("N"))).FullName;
        var path = Path.Combine(root, "index.bin");
        try
        {
            var snapshot = new CachedIndex([Entries[0]], IndexSource.Service, DateTime.UtcNow);
            await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => Task.Run(() => IndexCache.Save(path, snapshot))));
            Assert.Single(IndexCache.TryLoad(path)!.Entries);
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            Assert.ThrowsAny<OperationCanceledException>(() => IndexCache.Save(path, snapshot, cancellation.Token));
            Assert.Single(IndexCache.TryLoad(path)!.Entries);
            Assert.Single(Directory.GetFiles(root));
        }
        finally { Directory.Delete(root, true); }
    }

    private sealed class ChunkStream : MemoryStream
    {
        public int MaximumWrite { get; private set; }
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            MaximumWrite = Math.Max(MaximumWrite, buffer.Length);
            return base.WriteAsync(buffer, cancellationToken);
        }
    }

    private sealed class BlockedStream : MemoryStream
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Entered.TrySetResult();
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }
    }
}
