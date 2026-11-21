using System.Windows.Threading;
using Seekr.App.Services;
using Seekr.App.ViewModels;
using Seekr.Core.Index;

namespace Seekr.App.Tests;

public sealed partial class WorkspaceTests
{
    [Fact]
    public void DownloadsPresetsSwitchFromAllDisksAndSpaceMapToScopedFileSearch()
    {
        RunSta(() =>
        {
            using var workspace = new WorkspaceViewModel(persistViewSettings: false)
            {
                SpaceMapOpen = true,
                SearchAllDisks = true,
            };
            var downloads = DownloadsFolder.GetPath();
            Assert.False(string.IsNullOrEmpty(downloads));
            workspace.FindInDownloadsCommand.Execute("today");
            Assert.Equal(downloads, workspace.CurrentPath);
            Assert.False(workspace.SearchAllDisks);
            Assert.True(workspace.IsTableView);
            Assert.Equal("kind:file dm:today", workspace.Query);
            workspace.FindInDownloadsCommand.Execute("week");
            Assert.Equal("kind:file dm:thisweek", workspace.Query);
            workspace.FindInDownloadsCommand.Execute("pdf");
            Assert.Equal("kind:file ext:pdf", workspace.Query);
            Assert.True(workspace.IsSortDescending);
        });
    }

    [Fact]
    public void DriveRowsShowCapacityAndFreeSpaceWithoutFolderEstimates()
    {
        var entry = new SearchResult(new FileEntry(0, -1, "C:\\", true, 0, DateTime.MinValue, DateTime.MinValue), "C:\\", 1000);
        var drive = new ResultItem(entry, false, new DriveSpace(1000, 250, "NTFS", 400));
        Assert.True(drive.IsDrive); Assert.True(drive.HasMeasuredSize); Assert.False(drive.IsSizePartial);
        Assert.Equal("1000 B", drive.SizeString); Assert.Equal("250 B", drive.FreeSpaceString); Assert.Equal("Диск · NTFS", drive.TypeString);
        Assert.Empty(drive.ModifiedString);
        Assert.Equal(600L, drive.UsedBytes);
        Assert.Equal("Занято 600 B из 1000 B · Доступно 250 B", drive.MapSize);
        var unavailable = new ResultItem(entry);
        Assert.Null(unavailable.UsedBytes);
        Assert.Equal("—", unavailable.SizeString); Assert.Equal(-1, unavailable.EffectiveSize);
        Assert.Contains("недоступны", unavailable.SizeStatus);
        var file = new ResultItem(new SearchResult(new FileEntry(1, -1, "note.txt", false, 5, DateTime.UtcNow, DateTime.UtcNow), @"C:\note.txt", 5));
        Assert.False(file.IsDrive); Assert.Empty(file.FreeSpaceString);
    }

    [Fact]
    public void ClosingSelectedTabRestoresSelectionWhenListBoxClearsItDuringRemoval()
    {
        RunSta(() =>
        {
            using var shell = new ShellViewModel(false, false);
            var first = shell.SelectedWorkspace!;
            shell.NewTabCommand.Execute(null);
            var second = shell.SelectedWorkspace!;
            shell.Workspaces.CollectionChanged += (_, e) =>
            {
                if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Remove)
                    shell.SelectedWorkspace = null; // Two-way ListBox selection during collection removal.
            };
            shell.CloseTabCommand.Execute(second);
            Assert.Same(first, shell.SelectedWorkspace);
            Assert.Same(first, shell.ActiveWorkspace);
            WaitFor(first.RefreshCommand.ExecuteAsync(null));
            Assert.False(first.IsSearching);
        });
    }
    [Fact]
    public void DualPaneCopyUsesSharedQueueAndRestoresIndependentSearch()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "Seekr-panes-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var target = Directory.CreateDirectory(Path.Combine(root, "target")).FullName;
            var file = Path.Combine(root, "note.txt"); File.WriteAllText(file, "content");
            RunSta(() =>
            {
                using var shell = new ShellViewModel(false, false);
                var left = shell.SelectedWorkspace!; left.CurrentPath = root;
                shell.ToggleDualPaneCommand.Execute(null);
                var right = shell.SecondaryWorkspace!; right.CurrentPath = target;
                left.Results = [new ResultItem(new SearchResult(new FileEntry(0, -1, "note.txt", false, 7, DateTime.UtcNow, DateTime.UtcNow), file, 7), true)];
                left.Results[0].IsSelected = true;
                Assert.Same(left, shell.ActiveWorkspace);
                Assert.True(shell.CopyToOtherPaneCommand.CanExecute(null));
                shell.CopyToOtherPaneCommand.Execute(null);
                WaitFor(shell.Operations.Jobs[0].Completion);
                Assert.Equal("Готово", shell.Operations.Jobs[0].Status);
                Assert.Equal("content", File.ReadAllText(Path.Combine(target, "note.txt")));
                Assert.True(File.Exists(file));
                shell.SwitchPaneCommand.Execute(null); Assert.Same(right, shell.ActiveWorkspace);
                right.Query = "marker ext:txt"; right.SearchContents = true; right.SortBy("Размер");
                right.SetSortDirectionCommand.Execute("descending");
                shell.ToggleDualPaneCommand.Execute(null); Assert.Same(left, shell.ActiveWorkspace);
                shell.ToggleDualPaneCommand.Execute(null);
                Assert.Equal(target, shell.SecondaryWorkspace!.CurrentPath);
                Assert.Equal("marker ext:txt", shell.SecondaryWorkspace.Query);
                Assert.True(shell.SecondaryWorkspace.SearchContents);
                Assert.Equal("Размер", shell.SecondaryWorkspace.SortColumn);
                Assert.False(shell.SecondaryWorkspace.IsSortAscending);
            });
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void BatchRenameDialogModelRequiresPreviewAndSupportsUndo()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "Seekr-rename-ui-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var file = Path.Combine(root, "one.txt"); File.WriteAllText(file, "keep");
            RunSta(() =>
            {
                using var queue = new FileOperationService();
                var vm = new BatchRenameViewModel([file], queue);
                Assert.False(vm.ApplyCommand.CanExecute(null));
                WaitFor(vm.PreviewCommand.ExecuteAsync(null));
                Assert.True(vm.ApplyCommand.CanExecute(null));
                vm.Pattern = "item_{n}"; Assert.False(vm.ApplyCommand.CanExecute(null));
                WaitFor(vm.PreviewCommand.ExecuteAsync(null));
                WaitFor(vm.ApplyCommand.ExecuteAsync(null));
                Assert.Equal("keep", File.ReadAllText(Path.Combine(root, "item_001.txt")));
                Assert.True(vm.UndoCommand.CanExecute(null));
                var canceledUndo = vm.UndoCommand.ExecuteAsync(null); vm.CancelCommand.Execute(null);
                WaitFor(canceledUndo); Assert.Single(queue.Jobs);
                Assert.True(File.Exists(Path.Combine(root, "item_001.txt")));
                WaitFor(vm.UndoCommand.ExecuteAsync(null));
                Assert.Equal("keep", File.ReadAllText(file));
                Assert.Empty(Directory.GetFiles(root, ".seekr-rename-*"));
            });
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void ReturningToFolderRebuildsSearchAfterChangesWhileAway()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "Seekr-live-return-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var a = Directory.CreateDirectory(Path.Combine(root, "a")).FullName;
            var b = Directory.CreateDirectory(Path.Combine(root, "b")).FullName;
            File.WriteAllText(Path.Combine(a, "marker-first.txt"), "a");
            RunSta(() =>
            {
                using var vm = new WorkspaceViewModel(persistViewSettings: false);
                vm.CurrentPath = a; vm.Query = "marker"; WaitFor(SearchFinished(vm));
                Assert.Single(vm.Results);
                vm.CurrentPath = b; WaitFor(SearchFinished(vm));
                File.WriteAllText(Path.Combine(a, "marker-second.txt"), "b");
                vm.CurrentPath = a; WaitFor(SearchFinished(vm));
                Assert.Equal(2, vm.Results.Count);
            });
        }
        finally { Directory.Delete(root, true); }
    }
    [Theory]
    [InlineData("Pixel", "Solid", "Consolas")]
    [InlineData("Anime", "PhotoGarden", "Calibri")]
    [InlineData("Rune", "PhotoSnow", "Verdana")]
    [InlineData("Tactical", "PhotoRally", "Segoe UI")]
    public void ArtProfilesRoundTripWithoutChangingFilePreferences(string style, string background, string font)
    {
        var vm = new SettingsViewModel(new AppSettings { SearchResultLimit = 250, ShowHiddenFiles = true });
        vm.ApplyArtProfileCommand.Execute(style);
        Assert.Equal(style, vm.Settings.IconStyle); Assert.Equal(background, vm.Settings.Background);
        Assert.Equal(font, vm.Settings.Font); Assert.True(vm.Settings.ShowHiddenFiles); Assert.Equal(250, vm.Settings.SearchResultLimit);
        var file = Path.Combine(Path.GetTempPath(), "Seekr-art-profile-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            Assert.True(SettingsService.Save(vm.Settings, file));
            var restored = SettingsService.Load(file);
            Assert.Equal(background, restored.Background); Assert.Equal(style, restored.IconStyle); Assert.Equal(font, restored.Font);
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public void LivePreferencesSaveAndRevertWithoutApplyButton()
    {
        var file = Path.Combine(Path.GetTempPath(), "Seekr-live-settings-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            RunSta(() =>
            {
                var initial = new AppSettings { Theme = "Dark", CustomAccent = "#123456" };
                Assert.True(SettingsService.Save(initial, file));
                var model = new SettingsViewModel(initial, file);
                model.Settings.Theme = "Light";
                WaitFor(Task.Delay(350));
                Assert.Equal("Light", SettingsService.Load(file).Theme);
                model.RevertCommand.Execute(null); WaitFor(Task.Delay(350));
                Assert.Equal("Dark", SettingsService.Load(file).Theme);
                Assert.Equal("#123456", SettingsService.Load(file).CustomAccent);
                model.FinishEditing();
            });
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public async Task PermanentDeletionRemovesOnlyTheTestFile()
    {
        var directory = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "Seekr-permanent-test-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var file = Path.Combine(directory, "удалить-тест.txt"); File.WriteAllText(file, "test fixture");
            var result = await Seekr.App.Utils.RecycleBinHelper.ExecuteWithResultAsync(file, true);
            Assert.True(result.Success, result.Error); Assert.False(File.Exists(file)); Assert.True(Directory.Exists(directory));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task LockedFileDeletionFailsThenSucceedsAfterUnlocking()
    {
        var directory = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "Seekr-lock-delete-test-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var file = Path.Combine(directory, "locked.txt"); File.WriteAllText(file, "temporary test data");
            using (var locked = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                var failed = await Seekr.App.Utils.RecycleBinHelper.ExecuteWithResultAsync(file, true);
                Assert.False(failed.Success); Assert.False(string.IsNullOrWhiteSpace(failed.Error)); Assert.True(File.Exists(file));
                var inspection = await Task.Run(() => FileLockService.Inspect([file], CancellationToken.None));
                var own = inspection.Applications.Where(x => x.ProcessId == Environment.ProcessId).ToArray();
                Assert.NotEmpty(own); Assert.All(own, x => Assert.False(x.CanClose));
            }
            var success = await Seekr.App.Utils.RecycleBinHelper.ExecuteWithResultAsync(file, true);
            Assert.True(success.Success, success.Error); Assert.False(File.Exists(file));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void TypeOnlySearchClearsTextModeAndNormalizesExtensions()
    {
        var directory = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "Seekr-type-test-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            File.WriteAllText(Path.Combine(directory, "alpha.txt"), "note"); File.WriteAllText(Path.Combine(directory, "beta.log"), "note");
            RunSta(() =>
            {
                using var vm = new WorkspaceViewModel(persistViewSettings: false);
                vm.CurrentPath = directory; vm.SearchContents = true; vm.Query = "txt size:>100MB";
                vm.FilterCommand.Execute(null); vm.ExtensionFilter = "*.TXT, .pdf"; vm.OnlyFileTypes = true;
                vm.ApplyFiltersCommand.Execute(null); WaitFor(SearchFinished(vm));
                Assert.False(vm.SearchContents); Assert.Equal("ext:txt;pdf", vm.Query);
                Assert.Equal("alpha.txt", Assert.Single(vm.Results).Name);
            });
        }
        finally { Directory.Delete(directory, true); }
    }
    [Fact]
    public async Task RecycleOnlyOperationMovesItsTemporaryFixtureWithoutWindowsDialogs()
    {
        var directory = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "Seekr-recycle-test-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var file = Path.Combine(directory, "fixture.txt"); File.WriteAllText(file, "fixture");
            var result = await Seekr.App.Utils.RecycleBinHelper.ExecuteWithResultAsync(file, false);
            Assert.True(result.Success, result.Error); Assert.False(File.Exists(file)); Assert.True(Directory.Exists(directory));
        }
        finally { Directory.Delete(directory, true); }
    }

    private static void VerifyClipboardRoundTrip()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "Seekr-clipboard-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var target = Directory.CreateDirectory(Path.Combine(root, "target")).FullName;
            var file = Path.Combine(root, "clip.txt"); File.WriteAllText(file, "clipboard content");
            using var queue = new FileOperationService();
            using var workspace = new WorkspaceViewModel(operations: queue, persistViewSettings: false);
            workspace.CurrentPath = root;
            workspace.Results = [new ResultItem(new SearchResult(new FileEntry(0, -1, "clip.txt", false, 17, DateTime.UtcNow, DateTime.UtcNow), file, 17), true)];
            workspace.Results[0].IsSelected = true;
            workspace.CopyFilesCommand.Execute(null);
            Assert.Contains(file, System.Windows.Clipboard.GetFileDropList().Cast<string>());
            workspace.CurrentPath = target; workspace.PasteFilesCommand.Execute(null);
            Assert.Single(queue.Jobs); WaitFor(queue.Jobs[0].Completion);
            Assert.Equal("clipboard content", File.ReadAllText(Path.Combine(target, "clip.txt")));
            Assert.True(File.Exists(file));
        }
        finally { Directory.Delete(root, true); }
    }
    private static async Task SearchFinished(WorkspaceViewModel vm)
    {
        while (vm.IsSearching) await Task.Delay(10);
    }
    private static void WaitFor(Task task)
    {
        var timeout = DateTime.UtcNow.AddSeconds(10);
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(5) };
        timer.Tick += (_, _) => { if (task.IsCompleted || DateTime.UtcNow >= timeout) frame.Continue = false; };
        timer.Start();
        try { Dispatcher.PushFrame(frame); } finally { timer.Stop(); }
        Assert.True(task.IsCompleted, "Operation timed out"); task.GetAwaiter().GetResult();
    }
}
