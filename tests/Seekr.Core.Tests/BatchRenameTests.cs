using Seekr.Core.Operations;

namespace Seekr.Core.Tests;

public sealed class BatchRenameTests : IDisposable
{
    private readonly string _root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "Seekr-batch-" + Guid.NewGuid().ToString("N"))).FullName;
    [Fact]
    public void PreviewThenExecuteAndUndoPreserveContents()
    {
        var a = Path.Combine(_root, "a.txt"); var b = Path.Combine(_root, "b.txt");
        File.WriteAllText(a, "a"); File.WriteAllText(b, "b");
        var plan = BatchRename.Preview([a, b], new("photo_{n}", 1));
        Assert.True(plan.IsValid);
        Assert.True(File.Exists(a));
        var result = BatchRename.Execute(plan, CancellationToken.None);
        Assert.True(result.Success);
        Assert.Equal("a", File.ReadAllText(Path.Combine(_root, "photo_001.txt")));
        var undo = BatchRename.Reverse(result.Changes);
        Assert.True(BatchRename.Execute(undo, CancellationToken.None).Success);
        Assert.Equal("a", File.ReadAllText(a)); Assert.Equal("b", File.ReadAllText(b));
    }
    [Fact]
    public void CollisionsReservedNamesAndStalePreviewAreRejected()
    {
        var a = Path.Combine(_root, "a.txt"); File.WriteAllText(a, "a");
        File.WriteAllText(Path.Combine(_root, "exists.txt"), "keep");
        Assert.False(BatchRename.Preview([a], new("exists", 1)).IsValid);
        Assert.False(BatchRename.Preview([a], new("CON", 1)).IsValid);
        var plan = BatchRename.Preview([a], new("new", 1));
        File.WriteAllText(a, "changed content");
        Assert.False(BatchRename.Execute(plan, CancellationToken.None).Success);
        Assert.Equal("keep", File.ReadAllText(Path.Combine(_root, "exists.txt")));
    }
    [Fact]
    public void CyclesAndCancellationDoNotOverwriteFiles()
    {
        var a = Path.Combine(_root, "a.txt"); var b = Path.Combine(_root, "b.txt");
        File.WriteAllText(a, "a"); File.WriteAllText(b, "b");
        var plan = BatchRename.FromChanges([new(a, b), new(b, a)]);
        Assert.True(BatchRename.Execute(plan, CancellationToken.None).Success);
        Assert.Equal("b", File.ReadAllText(a)); Assert.Equal("a", File.ReadAllText(b));
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        Assert.False(BatchRename.Execute(BatchRename.Reverse(plan.Rows.Select(x => x.Change).ToArray()), cancel.Token).Success);
        Assert.Equal("b", File.ReadAllText(a));
        Assert.Empty(Directory.GetFiles(_root, ".seekr-rename-*"));
    }
    [Fact]
    public void LockedSecondSourceRollsBackAlreadyStagedFirstSource()
    {
        var a = Path.Combine(_root, "a.txt"); var b = Path.Combine(_root, "b.txt");
        File.WriteAllText(a, "first"); File.WriteAllText(b, "second");
        var plan = BatchRename.Preview([a, b], new("item_{n}"));
        using (var locked = new FileStream(b, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var result = BatchRename.Execute(plan, CancellationToken.None);
            Assert.False(result.Success); Assert.Empty(result.RecoveryPaths);
            Assert.Equal("first", File.ReadAllText(a));
            Assert.Empty(Directory.GetFiles(_root, ".seekr-rename-*"));
        }
        Assert.Equal("second", File.ReadAllText(b));
    }
    public void Dispose() => Directory.Delete(_root, true);
}
