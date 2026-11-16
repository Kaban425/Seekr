using Seekr.App.Services;
using Seekr.Core.Index;
using Seekr.Indexer;

namespace Seekr.App.Tests;

public sealed class SharedIndexTests
{
    [Fact]
    public async Task FallbackIndexKeepsUpdateRequiredVisibleAndRemainsSearchable()
    {
        var entries = new[] { new FileEntry(0, -1, @"C:\", true, 0, DateTime.MinValue, DateTime.MinValue), new FileEntry(1, 0, "fixture.txt", false, 3, DateTime.UtcNow, DateTime.UtcNow) };
        await using var service = new SharedIndexService(_ => new(new NameIndex(entries), IndexSource.AccessibleDrives, TimeSpan.Zero,
            [new(@"C:\", "Обход доступных папок", 2, "Обновите службу", DateTime.Now, true)]), false);
        await service.InitializeAsync();
        Assert.True(service.RequiresServiceUpdate); Assert.False(service.IsBusy);
        Assert.Single(service.Index!.Search(Seekr.Core.Search.SearchQuery.Parse("fixture"), 10));
    }

    [Fact]
    public async Task WatcherRetriesAfterTransientFailureAndDisposeDrainsLoad()
    {
        var polls = 0;
        var retried = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var monitor = new IndexChangeMonitor(() =>
        {
            var attempt = Interlocked.Increment(ref polls);
            if (attempt == 2) throw new IOException("temporary monitor failure");
            if (attempt > 2) retried.TrySetResult();
            return ['C'];
        }, (_, _, _) => new(Seekr.Indexer.Mft.JournalChangeKind.Unchanged, new(1, 1)));
        await using var service = new SharedIndexService(_ => new(new NameIndex([]), IndexSource.Service, TimeSpan.Zero),
            false, monitor, TimeSpan.FromMilliseconds(10));
        await service.InitializeAsync();
        await retried.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(polls >= 3);
        await service.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task DisposeCancelsActiveLoadWithoutPublishingIt()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var exited = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var service = new SharedIndexService(token =>
        {
            entered.SetResult();
            try { token.WaitHandle.WaitOne(); token.ThrowIfCancellationRequested(); }
            finally { exited.SetResult(); }
            return new(new NameIndex([]), IndexSource.Service, TimeSpan.Zero);
        }, false);
        var refresh = service.RefreshAsync();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await service.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(exited.Task.IsCompleted);
        Assert.True(refresh.IsCompletedSuccessfully);
        Assert.Null(service.Index);
    }

    [Fact]
    public async Task OverlappingRefreshesShareOneLoad()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        var attempts = 0;
        using var service = new SharedIndexService(token =>
        {
            Interlocked.Increment(ref attempts);
            started.TrySetResult();
            release.Wait(token);
            return new(new NameIndex([]), IndexSource.Service, TimeSpan.Zero);
        }, useCache: false);
        var first = service.RefreshAsync();
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var other = Enumerable.Range(0, 20).Select(_ => service.RefreshAsync()).ToArray();
        release.Set();
        await Task.WhenAll(other.Append(first)).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, attempts);
    }

    [Fact]
    public async Task FailedRefreshRetainsSnapshotAndCanBeRetried()
    {
        var attempts = 0;
        using var service = new SharedIndexService(_ => ++attempts == 2
            ? throw new IOException("temporary failure") : new(new NameIndex([]), IndexSource.Service, TimeSpan.Zero), false);
        await service.RefreshAsync();
        var snapshot = service.Index;
        await service.RefreshAsync();
        Assert.Same(snapshot, service.Index);
        Assert.Contains("temporary failure", service.Status, StringComparison.Ordinal);
        await service.RefreshAsync();
        Assert.Equal(3, attempts);
        Assert.NotSame(snapshot, service.Index);
    }
}
