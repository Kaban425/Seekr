using Seekr.App.Services;
using Seekr.App.ViewModels;

namespace Seekr.App.Tests;

public sealed partial class WorkspaceTests
{
    [Fact]
    public void DiagnosticReportsAreLocalBoundedAndTolerateUnavailableStorage()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "Seekr-diagnostics-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            string? report = null;
            for (var i = 0; i < 22; i++) report = DiagnosticService.Write(new InvalidOperationException("fixture diagnostic marker"), "test", root);
            Assert.NotNull(report); Assert.True(File.Exists(report));
            Assert.Equal(20, Directory.GetFiles(root, "report-*.json").Length);
            var text = File.ReadAllText(report); Assert.Contains("fixture diagnostic marker", text); Assert.Contains("test", text);
            Assert.NotEmpty(BuildInfo.Version); Assert.Contains("сборка", BuildInfo.DisplayVersion);
            Assert.Null(DiagnosticService.Write(new IOException("fixture"), "test", report));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void SessionCheckpointRestoresBeforeNormalClosingAndKeepsBackup()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "Seekr-session-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var path = Path.Combine(root, "session.json");
            RunSta(() =>
            {
                using var shell = new ShellViewModel(false, true, path, TimeSpan.FromMilliseconds(25));
                shell.SelectedWorkspace!.CurrentPath = root;
                shell.NewTabCommand.Execute(null);
                shell.SelectedWorkspace!.CurrentPath = root; shell.SelectedWorkspace.Query = "ext:txt";
                shell.SelectedWorkspace.SessionScrollOffset = 75;
                shell.IsDualPane = true; shell.SecondaryWorkspace!.CurrentPath = root;
                shell.IsRightPaneActive = true;
                WaitFor(CheckpointArrived());
                Assert.False(SessionStore.Load(path)!.CleanShutdown);
                using var recovered = new ShellViewModel(false, true, path, TimeSpan.FromMilliseconds(25));
                Assert.True(recovered.SessionWasRecovered); Assert.Equal(2, recovered.Workspaces.Count);
                Assert.Equal("ext:txt", recovered.SelectedWorkspace!.Query);
                Assert.Equal(75, recovered.SelectedWorkspace.SessionScrollOffset);
                Assert.True(recovered.IsDualPane); Assert.True(recovered.IsRightPaneActive);
                async Task CheckpointArrived()
                {
                    while (SessionStore.Load(path) is not { Tabs.Length: 2, DualPane: true, RightActive: true }) await Task.Delay(5);
                }
            });
            Assert.True(SessionStore.Load(path)!.CleanShutdown);
            Assert.True(File.Exists(path + ".bak"));
            File.WriteAllText(path, "truncated");
            Assert.NotNull(SessionStore.Load(path));
            Assert.Empty(Directory.GetFiles(root, "*.tmp"));
        }
        finally { Directory.Delete(root, true); }
    }
}
