using Seekr.Core.Operations;

namespace Seekr.Core.Tests;

public sealed class FileTransferTests : IDisposable
{
    private readonly string _root = Path.Combine(AppContext.BaseDirectory, "Seekr-transfer-" + Guid.NewGuid().ToString("N"));
    public FileTransferTests() { Directory.CreateDirectory(_root); }

    [Fact]
    public async Task CancellationWhileAwaitingConflictDoesNotTouchEitherFile()
    {
        var source = Path.Combine(_root, "conflict.txt");
        var target = Directory.CreateDirectory(Path.Combine(_root, "conflict-target")).FullName;
        await File.WriteAllTextAsync(source, "source");
        var existing = Path.Combine(target, "conflict.txt");
        await File.WriteAllTextAsync(existing, "existing");
        using var cancellation = new CancellationTokenSource();
        var waiting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var answer = new TaskCompletionSource<ConflictChoice>(TaskCreationOptions.RunContinuationsAsynchronously);
        var copy = FileTransfer.ExecuteAsync([source], target, false, _ => { waiting.SetResult(); return answer.Task; }, cancellationToken: cancellation.Token);
        await waiting.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        try { await Assert.ThrowsAnyAsync<OperationCanceledException>(() => copy.WaitAsync(TimeSpan.FromSeconds(2))); }
        finally { answer.TrySetResult(ConflictChoice.Skip); await copy.ContinueWith(_ => { }, TaskScheduler.Default); }
        Assert.Equal("source", await File.ReadAllTextAsync(source));
        Assert.Equal("existing", await File.ReadAllTextAsync(existing));
        Assert.Empty(Directory.GetFiles(target, "*.partial"));
    }

    [Fact]
    public async Task CopyKeepsSourceAndPublishesCompleteContents()
    {
        var source = Path.Combine(_root, "source.txt");
        var target = Path.Combine(_root, "target");
        Directory.CreateDirectory(target);
        await File.WriteAllTextAsync(source, "original");
        await FileTransfer.ExecuteAsync([source], target, false, _ => Task.FromResult(ConflictChoice.KeepBoth));
        Assert.Equal("original", await File.ReadAllTextAsync(source));
        Assert.Equal("original", await File.ReadAllTextAsync(Path.Combine(target, "source.txt")));
        Assert.Empty(Directory.GetFiles(target, "*.partial"));
    }

    [Fact]
    public async Task ConflictKeepBothDoesNotReplaceExistingFile()
    {
        var source = Path.Combine(_root, "note.txt");
        var target = Path.Combine(_root, "target");
        Directory.CreateDirectory(target);
        await File.WriteAllTextAsync(source, "new");
        await File.WriteAllTextAsync(Path.Combine(target, "note.txt"), "old");
        await FileTransfer.ExecuteAsync([source], target, false, _ => Task.FromResult(ConflictChoice.KeepBoth));
        Assert.Equal("old", await File.ReadAllTextAsync(Path.Combine(target, "note.txt")));
        Assert.Equal(2, Directory.GetFiles(target).Length);
    }

    [Fact]
    public async Task FolderCannotBeCopiedInsideItself()
    {
        var source = Path.Combine(_root, "Folder");
        Directory.CreateDirectory(Path.Combine(source, "inside"));
        await Assert.ThrowsAsync<IOException>(() => FileTransfer.ExecuteAsync([source], Path.Combine(source, "inside"), false, _ => Task.FromResult(ConflictChoice.KeepBoth)));
    }

    [Fact]
    public async Task CancellationLeavesSourceIntactAndNoIncompleteDestination()
    {
        var source = Path.Combine(_root, "source.bin");
        var target = Path.Combine(_root, "target");
        Directory.CreateDirectory(target);
        await File.WriteAllBytesAsync(source, new byte[1024 * 1024]);
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => FileTransfer.ExecuteAsync([source], target, false,
            _ => Task.FromResult(ConflictChoice.KeepBoth), cancellationToken: cts.Token));
        Assert.True(File.Exists(source));
        Assert.Empty(Directory.GetFiles(target));
    }

    [Fact]
    public async Task MoveFolderAndRenamePreserveContents()
    {
        var source = Path.Combine(_root, "Folder");
        Directory.CreateDirectory(source);
        File.WriteAllText(Path.Combine(source, "file.txt"), "hello");
        var target = Path.Combine(_root, "target");
        Directory.CreateDirectory(target);
        await FileTransfer.ExecuteAsync([source], target, true, _ => Task.FromResult(ConflictChoice.Skip));
        Assert.False(Directory.Exists(source));
        Assert.True(File.Exists(Path.Combine(target, "Folder", "file.txt")));
        FileTransfer.Rename(Path.Combine(target, "Folder"), "Renamed");
        Assert.True(File.Exists(Path.Combine(target, "Renamed", "file.txt")));
    }

    [Fact]
    public async Task MergeResumesPartialFolderMoveIntoOriginalDestination()
    {
        var source = Path.Combine(_root, "Folder");
        Directory.CreateDirectory(source);
        File.WriteAllText(Path.Combine(source, "remaining.txt"), "rest");
        var target = Path.Combine(_root, "target");
        Directory.CreateDirectory(Path.Combine(target, "Folder"));
        File.WriteAllText(Path.Combine(target, "Folder", "completed.txt"), "done");
        var report = await FileTransfer.ExecuteAsync([source], target, true, _ => Task.FromResult(ConflictChoice.Merge));
        Assert.Empty(report.Failed);
        Assert.False(Directory.Exists(source));
        Assert.Equal(2, Directory.GetFiles(Path.Combine(target, "Folder")).Length);
        Assert.False(Directory.Exists(Path.Combine(target, "Folder (1)")));
    }

    public void Dispose() { Directory.Delete(_root, true); }
}
