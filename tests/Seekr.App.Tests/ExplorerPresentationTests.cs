using System.Windows.Threading;
using Seekr.App.Services;
using Seekr.App.ViewModels;

namespace Seekr.App.Tests;

public sealed class ExplorerPresentationTests
{
    [Fact]
    public void FastSupersededSearchDoesNotFlashProgressOrEndTheNewerSearch()
    {
        var folder = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "Seekr-fast-search-" + Guid.NewGuid().ToString("N"))).FullName;
        File.WriteAllText(Path.Combine(folder, "first.txt"), "first");
        File.WriteAllText(Path.Combine(folder, "second.txt"), "second");
        try
        {
        RunSta(() =>
        {
            using var indexes = new SharedIndexService(_ => throw new InvalidOperationException("UI test must not load host indexes."), useCache: false);
            using var operations = new FileOperationService();
            using var vm = new WorkspaceViewModel(indexes, operations, persistViewSettings: false) { CurrentPath = folder };
            var ended = 0;
            var flashed = false;
            vm.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(vm.IsSearching) && !vm.IsSearching) ended++;
                if (args.PropertyName == nameof(vm.ShowSearchProgress) && vm.ShowSearchProgress) flashed = true;
            };
            vm.Query = "first";
            vm.Query = "second";
            Assert.True(vm.IsSearching);
            var timeout = DateTime.UtcNow.AddSeconds(5);
            var frame = new DispatcherFrame();
            var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(5) };
            timer.Tick += (_, _) => { if (!vm.IsSearching || DateTime.UtcNow >= timeout) frame.Continue = false; };
            timer.Start();
            try { Dispatcher.PushFrame(frame); }
            finally { timer.Stop(); }
            Assert.False(vm.IsSearching);
            Assert.Equal(1, ended);
            Assert.False(flashed);
            Assert.Equal("second.txt", Assert.Single(vm.Results).Name);
        });
        }
        finally { Directory.Delete(folder, true); }
    }

    [Fact]
    public void LoadingEmptyAndFailureStatesHaveDistinctMessagesAndActions()
    {
        RunSta(() =>
        {
            using var vm = new WorkspaceViewModel(persistViewSettings: false);
            vm.CurrentPath = Path.GetTempPath();
            Assert.True(vm.IsSearching);
            Assert.Contains("Открываем", vm.EmptyStateTitle, StringComparison.Ordinal);
            vm.IsSearching = false;
            Assert.Equal("Папка пуста", vm.EmptyStateTitle);
            vm.Query = "missing";
            vm.IsSearching = false;
            Assert.Equal("Ничего не найдено", vm.EmptyStateTitle);
            Assert.True(vm.CanResetEmptySearch);
            vm.ErrorMessage = "Нет доступа";
            Assert.Contains("Не удалось", vm.EmptyStateTitle, StringComparison.Ordinal);
            Assert.True(vm.CanRetryEmptyState);
            Assert.False(vm.CanResetEmptySearch);
            vm.ClearErrorCommand.Execute(null);
            Assert.False(vm.CanRetryEmptyState);
        });
    }
    [Fact]
    public void AddressValidationPreservesContextAndAcceptsQuotedFolderPaths()
    {
        var folder = Path.Combine(Path.GetTempPath(), "Seekr-address-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            RunSta(() =>
            {
                using var vm = new WorkspaceViewModel(persistViewSettings: false);
                vm.Query = "report";
                Assert.False(vm.TryNavigateAddress("relative-folder"));
                Assert.Equal("report", vm.Query);
                Assert.True(vm.HasErrorMessage);
                Assert.True(vm.TryNavigateAddress("\"" + folder + "\""));
                Assert.Equal(folder, vm.CurrentPath);
                Assert.Empty(vm.Query);
                Assert.False(vm.HasErrorMessage);
            });
        }
        finally { Directory.Delete(folder, true); }
    }

    [Fact]
    public void SortingExposesDirectionAndReordersTheWholeFolderList()
    {
        RunSta(() =>
        {
            using var vm = new WorkspaceViewModel(persistViewSettings: false);
            var time = DateTime.UtcNow;
            vm.Results = [
                new(new(new(0, -1, "large.txt", false, 100, time, time), @"C:\large.txt", 100)),
                new(new(new(1, -1, "small.txt", false, 10, time, time), @"C:\small.txt", 10))];
            vm.SortBy("Размер");
            Assert.True(vm.IsSortAscending);
            Assert.Equal("small.txt", vm.Results[0].Name);
            vm.SetSortDirectionCommand.Execute("descending");
            Assert.True(vm.IsSortDescending);
            Assert.Equal("large.txt", vm.Results[0].Name);
            Assert.Contains("убыванию", vm.SortDescription, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void TabCyclingAndReopeningPreserveWorkspaceContext()
    {
        RunSta(() =>
        {
            using var shell = new ShellViewModel(initializeWorkspaces: false, persistViewSettings: false);
            var first = shell.SelectedWorkspace;
            shell.NewTabCommand.Execute(null);
            var second = shell.SelectedWorkspace;
            shell.NewTabCommand.Execute(null);
            var third = shell.SelectedWorkspace!;
            third.CurrentPath = @"C:\Docs";
            third.Query = "report ext:pdf";
            third.SearchAllDisks = true;
            third.SpaceMapOpen = true;
            shell.NextTabCommand.Execute(null);
            Assert.Same(first, shell.SelectedWorkspace);
            shell.PreviousTabCommand.Execute(null);
            Assert.Same(third, shell.SelectedWorkspace);
            shell.CloseCurrentTabCommand.Execute(null);
            Assert.Same(second, shell.SelectedWorkspace);
            shell.ReopenClosedTabCommand.Execute(null);
            Assert.Equal(@"C:\Docs", shell.SelectedWorkspace!.CurrentPath);
            Assert.Equal("report ext:pdf", shell.SelectedWorkspace.Query);
            Assert.True(shell.SelectedWorkspace.SearchAllDisks);
            Assert.True(shell.SelectedWorkspace.SpaceMapOpen);
        });
    }

    [Fact]
    public void SearchScopeDoesNotNavigateAwayAndFilterChipsCanBeRemoved()
    {
        RunSta(() =>
        {
            using var vm = new WorkspaceViewModel(persistViewSettings: false);
            vm.CurrentPath = @"C:\Docs";
            vm.Query = "report ext:pdf size:>100MB";
            Assert.Equal(2, vm.ActiveFilters.Count);
            vm.SetSearchScopeCommand.Execute("all");
            Assert.Equal(@"C:\Docs", vm.CurrentPath);
            Assert.Equal("Везде", vm.SearchScopeLabel);
            vm.RemoveFilterCommand.Execute(vm.ActiveFilters.Single(x => x.Token.StartsWith("ext:", StringComparison.Ordinal)));
            Assert.DoesNotContain("ext:", vm.Query, StringComparison.Ordinal);
            Assert.Contains("report", vm.Query, StringComparison.Ordinal);
            Assert.Single(vm.ActiveFilters);
            vm.Query = string.Empty;
            Assert.False(vm.HasQuery);
        });
    }

    [Fact]
    public void EmptySavedSearchSectionIsHiddenAndKnownFoldersUseRussianLabels()
    {
        RunSta(() =>
        {
            using var vm = new WorkspaceViewModel(persistViewSettings: false);
            Assert.Contains(vm.NavigationItems, x => x.Label == "Рабочий стол");
            Assert.Contains(vm.NavigationItems, x => x.Label == "Загрузки");
            Assert.DoesNotContain(vm.NavigationItems, x => x.Label == "Desktop" || x.Label == "Downloads");
            if (SettingsService.Load().SavedSearches.Count == 0)
                Assert.DoesNotContain(vm.NavigationItems, x => x.IsSeparator && x.Label == "Сохранённые поиски");
        });
    }

    [Fact]
    public async Task NavigationTreeLoadsFoldersWithoutIncludingFiles()
    {
        var path = Path.Combine(Path.GetTempPath(), "Seekr-tree-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(path, "Child"));
        File.WriteAllText(Path.Combine(path, "file.txt"), "test");
        try
        {
            using var node = new NavigationItem("Root", path);
            await node.LoadChildrenAsync();
            Assert.Equal("Child", Assert.Single(node.Children).Label);
        }
        finally { Directory.Delete(path, true); }
    }

    private static void RunSta(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
            try { action(); } catch (Exception ex) { error = ex; }
            finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start(); thread.Join();
        if (error is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
    }
}
