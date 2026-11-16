using Seekr.App.Services;
using Seekr.Core.Operations;

namespace Seekr.App.Tests;

public sealed class OperationServiceTests
{
    [Fact]
    public async Task QueuedCancellationDoesNotRunActionAndShutdownDrainsActiveJob()
    {
        await using var operations = new FileOperationService();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var active = operations.EnqueueAction("active", ["fixture"], async (paths, _, token) =>
        {
            entered.SetResult();
            await Task.Delay(Timeout.Infinite, token);
            return new(paths, [], 0);
        });
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var ran = false;
        var queued = operations.EnqueueAction("queued", ["fixture"], (paths, _, _) => { ran = true; return Task.FromResult(new TransferReport(paths, [], 0)); });
        queued.CancelCommand.Execute(null);
        await queued.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(ran);
        Assert.Equal("Отменено", queued.Status);
        await operations.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(active.IsFinished);
        Assert.Equal("Отменено", active.Status);
    }

    [Fact]
    public async Task UnexpectedJobFailureDoesNotBlockNextJobAndHistoryIsBounded()
    {
        await using var operations = new FileOperationService();
        var failed = operations.EnqueueAction("failure", ["fixture"], (_, _, _) => throw new InvalidOperationException("fixture"));
        await failed.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("Ошибка", failed.Status);
        for (var i = 0; i < 210; i++)
        {
            var job = operations.EnqueueAction("small", ["fixture"], (paths, _, _) => Task.FromResult(new TransferReport(paths, [], 0)));
            await job.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        }
        Assert.InRange(operations.Jobs.Count, 1, 200);
        Assert.All(operations.Jobs, job => Assert.True(job.IsFinished));
        Assert.Equal(0, operations.ActiveTaskCount);
    }

    [Fact]
    public async Task JobsAreSerializedAndFailuresCanBeRetried()
    {
        using var operations = new FileOperationService();
        var active = 0;
        var maximum = 0;
        var attempts = 0;
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var count = 0;
        operations.Completed += (_, _) => { if (Interlocked.Increment(ref count) == 2) completed.TrySetResult(); };
        async Task<TransferReport> Execute(IReadOnlyList<string> paths, IProgress<TransferProgress> progress, CancellationToken token)
        {
            var current = Interlocked.Increment(ref active);
            maximum = Math.Max(maximum, current);
            try
            {
                await Task.Delay(30, token);
                if (Interlocked.Increment(ref attempts) == 1) throw new IOException("test failure");
                return new(paths, [], 0);
            }
            finally { Interlocked.Decrement(ref active); }
        }
        var first = operations.EnqueueAction("first", ["test"], Execute);
        operations.EnqueueAction("second", ["test"], Execute);
        await completed.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(1, maximum);
        Assert.Equal("Ошибка", first.Status);
        Assert.True(first.CanRetry);
        var retryDone = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        operations.Completed += (_, _) => retryDone.TrySetResult();
        first.RetryCommand.Execute(null);
        await retryDone.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal("Готово", operations.Jobs[0].Status);
    }
}
