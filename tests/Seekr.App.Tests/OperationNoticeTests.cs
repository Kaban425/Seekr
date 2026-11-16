using Seekr.App.Services;
using Seekr.App.ViewModels;

namespace Seekr.App.Tests;

public sealed partial class WorkspaceTests
{
    private static void VerifyCompactOperationPanel()
    {
        using var operations = new FileOperationService();
        using var notice = new OperationNoticeViewModel(operations);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var job = operations.EnqueueAction("Копирование: фотографии → Архив", ["fixture"], async (paths, _, token) =>
        { await release.Task.WaitAsync(token); return new(paths, [], 0); });
        foreach (var theme in VisualThemes)
        {
            ThemeService.ApplyTheme(theme);
            var panel = new OperationNotice { DataContext = notice };
            panel.Measure(new System.Windows.Size(380, 300));
            panel.Arrange(new System.Windows.Rect(0, 0, 380, panel.DesiredSize.Height));
            panel.UpdateLayout();
            Assert.Equal(System.Windows.Visibility.Visible, panel.Visibility);
            Assert.InRange(panel.ActualHeight, 80, 260);
            SaveVisual(panel, "operation-notice-" + theme.ToLowerInvariant());
        }
        release.SetResult();
        WaitFor(job.Completion);
    }

    [Fact]
    public void CompactNoticeTracksRunningJobInsteadOfNewestQueuedJob()
    {
        RunSta(() =>
        {
            using var operations = new FileOperationService();
            using var notice = new OperationNoticeViewModel(operations);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var first = operations.EnqueueAction("first", ["fixture"], async (paths, _, token) =>
            { await release.Task.WaitAsync(token); return new(paths, [], 0); });
            Assert.True(notice.IsVisible);
            Assert.Same(first, notice.Current);
            var second = operations.EnqueueAction("second", ["fixture"], async (paths, _, token) =>
            { await release.Task.WaitAsync(token); return new(paths, [], 0); });
            Assert.Same(first, notice.Current);
            Assert.Equal(2, notice.ActiveCount);
            notice.DismissCommand.Execute(null);
            Assert.False(notice.IsVisible);
            Assert.False(first.IsFinished);
            release.SetResult();
            WaitFor(Task.WhenAll(first.Completion, second.Completion));
            Assert.False(notice.IsVisible);
            var next = operations.EnqueueAction("next", ["fixture"], (paths, _, _) => Task.FromResult(new Seekr.Core.Operations.TransferReport(paths, [], 0)));
            Assert.True(notice.IsVisible);
            WaitFor(next.Completion);
        });
    }

    [Fact]
    public void CompactNoticeHidesSuccessButKeepsErrorsUntilDismissed()
    {
        RunSta(() =>
        {
            using var operations = new FileOperationService();
            using var notice = new OperationNoticeViewModel(operations, TimeSpan.FromMilliseconds(20));
            var success = operations.EnqueueAction("success", ["fixture"], (paths, _, _) => Task.FromResult(new Seekr.Core.Operations.TransferReport(paths, [], 0)));
            WaitFor(success.Completion);
            WaitFor(WaitUntilHidden());
            var failed = operations.EnqueueAction("failure", ["fixture"], (_, _, _) => throw new IOException("fixture"));
            WaitFor(failed.Completion);
            WaitFor(Task.Delay(60));
            Assert.True(notice.IsVisible);
            Assert.Same(failed, notice.Current);
            notice.DismissCommand.Execute(null);
            Assert.False(notice.IsVisible);
            async Task WaitUntilHidden() { while (notice.IsVisible) await Task.Delay(5); }
        });
    }
}
