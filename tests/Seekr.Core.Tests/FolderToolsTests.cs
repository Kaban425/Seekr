using Seekr.Core.Operations;

namespace Seekr.Core.Tests;

public sealed class FolderToolsTests : IDisposable
{
    private readonly string _root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "Seekr-tools-" + Guid.NewGuid().ToString("N"))).FullName;
    [Fact]
    public async Task LargeMoveCompletesWithoutPreparingAnExpensiveUndoSnapshot()
    {
        var source = Path.Combine(_root, "large.bin");
        using (var stream = File.Create(source)) stream.SetLength(65L * 1024 * 1024);
        var target = Directory.CreateDirectory(Path.Combine(_root, "target")).FullName;
        var report = await FileTransfer.ExecuteAsync([source], target, true, _ => Task.FromResult(ConflictChoice.Skip));
        Assert.Empty(report.Failed); Assert.Single(report.Completed); Assert.Empty(report.UndoMoves);
        Assert.NotNull(report.UndoUnavailable); Assert.False(File.Exists(source));
        Assert.Equal(65L * 1024 * 1024, new FileInfo(Path.Combine(target, "large.bin")).Length);
    }
    [Fact]
    public void DuplicateRevalidationRefusesAChangedRetainedCopy()
    {
        var keep = Path.Combine(_root, "keep.txt"); var remove = Path.Combine(_root, "remove.txt");
        File.WriteAllText(keep, "same"); File.WriteAllText(remove, "same");
        var report = FolderAnalysis.Scan(_root, true);
        Assert.Equal(2, report.Items.Count);
        File.WriteAllText(keep, "changed");
        Assert.Throws<IOException>(() => FolderAnalysis.ValidateDuplicateSelection(report.Items, new HashSet<string> { remove }));
        Assert.True(File.Exists(remove)); Assert.True(File.Exists(keep));
    }
    public void Dispose() => Directory.Delete(_root, true);
}
