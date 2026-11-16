using System.Windows.Threading;
using Seekr.App.Services;
using Seekr.App.ViewModels;
using Seekr.Core.Index;

namespace Seekr.App.Tests;

public sealed partial class WorkspaceTests
{
    private static readonly string[] BackgroundOptions = ["Solid", "Aurora", "Ocean", "Lavender", "Sand", "PhotoCoupe", "PhotoLake", "PhotoRally", "PhotoGarden", "PhotoSnow", "PhotoPolar", "PhotoVelvet", "PhotoRose"];
    private static readonly Seekr.App.Icons.IconKind[] GalleryIcons = [Seekr.App.Icons.IconKind.Folder, Seekr.App.Icons.IconKind.Document, Seekr.App.Icons.IconKind.Picture];
    private static readonly string[] IconOptions = ["Pastel", "Outline", "Mono", "Pixel", "Anime", "Rune", "Tactical", "Neon", "Blueprint", "Paper", "Minimal", "Polar", "Velvet", "Rose"];
    private static readonly double[] LayoutWidths = [840, 1100, 1440];
    [Fact]
    public void WorkspaceAndSettingsResourcesLoadAndArrange()
    {
        RunSta(() =>
        {
            var application = new App { ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown };
            application.InitializeComponent();
            try
            {
                VerifyCompactOperationPanel();
                VerifyFeedbackForm();
                using var vm = new WorkspaceViewModel(persistViewSettings: false);
                var view = new WorkspaceView { DataContext = vm };
                view.Measure(new System.Windows.Size(1100, 700));
                view.Arrange(new System.Windows.Rect(0, 0, 1100, 700));
                Assert.NotNull(view.FindResource("SolidBackgroundFillColorSecondaryBrush"));
                Assert.NotNull(view.FindResource("SolidBackgroundFillColorTertiaryBrush"));
                Assert.NotNull(view.FindResource("ControlStrokeColorDefaultBrush"));
                view.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.FrameworkElement.LoadedEvent));
                var columns = ((System.Windows.Controls.GridView)view.FindName("ResultsGrid")).Columns;
                var freeVisibleAtHome = columns.Any(column => Equals(column.Header, "Свободно"));
                vm.CurrentPath = Path.GetTempPath();
                Assert.DoesNotContain(columns, column => Equals(column.Header, "Свободно"));
                vm.CurrentPath = null;
                Assert.Equal(freeVisibleAtHome, columns.Any(column => Equals(column.Header, "Свободно")));
                vm.Query = "note";
                Assert.DoesNotContain(columns, column => Equals(column.Header, "Свободно"));
                vm.Query = "";
                Assert.Equal(freeVisibleAtHome, columns.Any(column => Equals(column.Header, "Свободно")));
                vm.OpenPathCommand.Execute(null);
                var editor = (System.Windows.Controls.TextBox)view.FindName("AddressEditor");
                Assert.Equal(System.Windows.Visibility.Visible, editor.Visibility);
                vm.SortBy("Размер");
                Assert.Equal("{0} ↑", columns.Single(x => x.Header is string name && name == "Размер").HeaderStringFormat);
                using var secondWorkspace = new WorkspaceViewModel(persistViewSettings: false);
                view.DataContext = secondWorkspace;
                Assert.Equal(System.Windows.Visibility.Collapsed, editor.Visibility);
                vm.OpenPathCommand.Execute(null);
                Assert.Equal(System.Windows.Visibility.Collapsed, editor.Visibility);
                secondWorkspace.OpenPathCommand.Execute(null);
                Assert.Equal(System.Windows.Visibility.Visible, editor.Visibility);
                secondWorkspace.SortBy("Имя");
                secondWorkspace.SetSortDirectionCommand.Execute("descending");
                Assert.Equal("{0} ↓", columns.Single(x => x.Header is string name && name == "Имя").HeaderStringFormat);
                view.DataContext = vm;

                var checkboxColumn = (System.Windows.Controls.GridViewColumn)view.FindName("SelectionColumn");
                columns.Remove(checkboxColumn);
                columns.Insert(1, checkboxColumn);
                view.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.FrameworkElement.UnloadedEvent));
                view.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.FrameworkElement.LoadedEvent));
                Assert.Same(checkboxColumn, columns[0]);
                view.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.FrameworkElement.UnloadedEvent));
                vm.Results = [Item(0, @"C:\Docs", true, 100)];
                vm.CurrentPath = @"C:\Docs";
                foreach (var width in LayoutWidths)
                {
                    view.Measure(new System.Windows.Size(width, 700));
                    view.Arrange(new System.Windows.Rect(0, 0, width, 700));
                    view.UpdateLayout();
                    var header = (System.Windows.FrameworkElement)view.FindName("WorkspaceHeader");
                    Assert.InRange(header.ActualHeight, 90, 100);
                    var address = (System.Windows.FrameworkElement)view.FindName("AddressBar");
                    var search = (System.Windows.FrameworkElement)view.FindName("SearchBox");
                    Assert.InRange(Math.Abs(address.TranslatePoint(new(), view).Y - search.TranslatePoint(new(), view).Y), 0, 3);
                }
                var settings = new SettingsWindow();
                settings.Close();
                using var indexes = new SharedIndexService(_ => new(new NameIndex([]), Seekr.Indexer.IndexSource.Service, TimeSpan.Zero), false);
                var indexWindow = new IndexStatusWindow(indexes);
                indexWindow.Close();
                using var operations = new FileOperationService();
                var operationWindow = new OperationsWindow(operations);
                operationWindow.Close();
                using var locksWindow = new LocksWindow([]);
                locksWindow.Close();
                vm.ShowPreviewPane = true;
                view.Measure(new System.Windows.Size(840, 700));
                view.Arrange(new System.Windows.Rect(0, 0, 840, 700));
                view.UpdateLayout();
                Assert.Equal(System.Windows.Visibility.Visible, ((System.Windows.Controls.Border)view.FindName("PreviewPanel")).Visibility);
                Assert.Equal(2,System.Windows.Controls.Grid.GetColumn((System.Windows.Controls.Border)view.FindName("PreviewPanel")));
                VerifyDetailsAndAppearance(application,view,vm);
                VerifyVisualMatrix(application, view, vm);
                VerifyFileManagerV2Visuals();
                VerifySettingsAndMenuVisuals();
                VerifyLockWindowAndDesigns();
                var orphanCaption = new SeekrTitleBar();
                orphanCaption.TemplateButtonCommand.Execute(Wpf.Ui.Controls.TitleBarButtonType.Minimize);
                orphanCaption.TemplateButtonCommand.Execute(Wpf.Ui.Controls.TitleBarButtonType.Close);
                VerifyClipboardRoundTrip();
                var help = new QuickStartWindow();
                Assert.NotNull(help.Content); help.Close();
                var gallery = new System.Windows.Controls.WrapPanel { Width = 900 };
                foreach (var background in BackgroundOptions)
                {
                    AppearanceService.Apply(new AppSettings { Background = background, Font = "Verdana" });
                    Assert.IsAssignableFrom<System.Windows.Media.Brush>(application.Resources["Seekr.Canvas"]);
                    gallery.Children.Add(new System.Windows.Controls.Border { Width = 280, Height = 140, Margin = new System.Windows.Thickness(8), Background = (System.Windows.Media.Brush)application.Resources["Seekr.Canvas"], Child = new System.Windows.Controls.TextBlock { Text = background, Foreground = (System.Windows.Media.Brush)application.FindResource("Seekr.TextPrimary"), Margin = new System.Windows.Thickness(20) } });
                }
                foreach (var style in IconOptions)
                {
                    AppearanceService.Apply(new AppSettings { IconStyle = style });
                    var menuBrush = (System.Windows.Media.SolidColorBrush)application.FindResource("Seekr.MenuSurface");
                    Assert.Equal(255, menuBrush.Color.A);
                    Assert.Equal(1.0, menuBrush.Opacity);
                    Assert.True(Seekr.App.Icons.IconArtwork.Image(Seekr.App.Icons.IconKind.Folder).IsFrozen);
                    var icons = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
                    foreach (var kind in GalleryIcons)
                        icons.Children.Add(new System.Windows.Controls.Image { Source = Seekr.App.Icons.IconArtwork.Image(kind), Width = 48, Height = 48, Margin = new System.Windows.Thickness(12) });
                    var card = new System.Windows.Controls.StackPanel(); card.Children.Add(new System.Windows.Controls.TextBlock { Text = style, Foreground = System.Windows.Media.Brushes.White }); card.Children.Add(icons);
                    gallery.Children.Add(new System.Windows.Controls.Border { Width = 280, Height = 140, Margin = new System.Windows.Thickness(8), Background = System.Windows.Media.Brushes.Black, Child = card });
                }
                foreach (var theme in VisualThemes)
                {
                    ThemeService.ApplyTheme(theme);
                    foreach (var style in IconOptions)
                    {
                        AppearanceService.Apply(new AppSettings { IconStyle = style });
                        foreach (var kind in GalleryIcons)
                        {
                            var drawing = (System.Windows.Media.DrawingGroup)Seekr.App.Icons.IconArtwork.Image(kind).Drawing;
                            var body = (System.Windows.Media.GeometryDrawing)drawing.Children[1];
                            if (style != "Outline")
                                Assert.True(Contrast(((System.Windows.Media.SolidColorBrush)body.Pen.Brush).Color, ((System.Windows.Media.SolidColorBrush)body.Brush).Color) >= 3, $"{style}/{theme}/{kind}: internal detail contrast");
                        }
                    }
                }
                gallery.Measure(new System.Windows.Size(900, 1550)); gallery.Arrange(new System.Windows.Rect(0, 0, 900, 1550)); gallery.UpdateLayout(); SaveVisual(gallery, "customization-gallery");
                AppearanceService.Apply(new AppSettings());
                // Real HWND/DPI lifecycle checks run after detached visual fixtures.
                VerifySettingsCanBeReopened(application);
            }
            finally { application.Shutdown(); }
        });
    }

    [Theory]
    [InlineData("")]
    [InlineData("relative.txt")]
    [InlineData(@"C:\")]
    [InlineData(@"C:\*.txt")]
    [InlineData("C:\\first.txt\0C:\\second.txt")]
    public void DeleteRejectsUnsafePathsWithoutCallingTheShell(string path)
    {
        Assert.False(Seekr.App.Utils.RecycleBinHelper.MoveToRecycleBin(path));
        Assert.False(Seekr.App.Utils.RecycleBinHelper.DeletePermanently(path));
    }

    private static ResultItem Item(int id, string path, bool folder, long size) => new(new SearchResult(
        new FileEntry(id, -1, path, folder, size, DateTime.UtcNow, DateTime.UtcNow), path, size));

    [Fact]
    public void SelectionCountsRootsAndClearsWithoutDeleting()
    {
        RunSta(() =>
        {
            using var vm = new WorkspaceViewModel(persistViewSettings: false);
            vm.Results = [Item(0, @"C:\Docs", true, 100), Item(1, @"C:\Docs\note.txt", false, 20)];
            vm.SelectAllCommand.Execute(null);
            Assert.Equal(2, vm.SelectedCount);
            Assert.Contains("100 B", vm.SelectionText, StringComparison.Ordinal);
            Assert.True(vm.DeleteSelectedCommand.CanExecute(null));
            vm.ClearSelectionCommand.Execute(null);
            Assert.Equal(0, vm.SelectedCount);
            Assert.False(vm.DeleteSelectedCommand.CanExecute(null));
        });
    }

    [Fact]
    public void NavigationHistorySupportsBackAndForward()
    {
        RunSta(() =>
        {
            using var vm = new WorkspaceViewModel(persistViewSettings: false);
            vm.CurrentPath = @"C:\Docs";
            vm.CurrentPath = @"C:\Photos";
            vm.NavigateBackCommand.Execute(null);
            Assert.Equal(@"C:\Docs", vm.CurrentPath);
            vm.NavigateForwardCommand.Execute(null);
            Assert.Equal(@"C:\Photos", vm.CurrentPath);
        });
    }

    [Fact]
    public void VisualFilterPreservesExtensionFromTypedQuery()
    {
        RunSta(() =>
        {
            using var vm = new WorkspaceViewModel(persistViewSettings: false);
            vm.Query = "report ext:pdf size:10MB..20MB";
            vm.FilterCommand.Execute(null);
            vm.DateFilter = "Сегодня";
            vm.ApplyFiltersCommand.Execute(null);
            Assert.Contains("ext:pdf", vm.Query, StringComparison.Ordinal);
            Assert.Contains("size:10mb..20mb", vm.Query.ToLowerInvariant(), StringComparison.Ordinal);
            Assert.Contains("dm:today", vm.Query, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void SavedSearchSettingsRoundTripAndFailuresAreReported()
    {
        var folder = Path.Combine(Path.GetTempPath(), "Seekr-settings-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var path = Path.Combine(folder, "settings.json");
            var settings = new AppSettings { SavedSearches = [new("PDF за неделю", "ext:pdf dm:thisweek", @"C:\Docs")] };
            Assert.True(SettingsService.Save(settings, path));
            Assert.Equal(settings.SavedSearches, SettingsService.Load(path).SavedSearches);
            File.WriteAllText(path, "invalid json");
            Assert.Empty(SettingsService.Load(path).SavedSearches);
            Assert.False(SettingsService.Save(settings, folder));
        }
        finally { Directory.Delete(folder, recursive: true); }
    }

    [Fact]
    public void InvalidAppearanceValuesAreRepairedAndFailedSaveLeavesNoTemporaryFile()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "Seekr-settings-recovery-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var file = Path.Combine(root, "settings.json");
            Assert.True(SettingsService.Save(new AppSettings { UiScale = double.NaN, SidebarWidth = double.PositiveInfinity }, file));
            var settings = SettingsService.Load(file);
            Assert.Equal(1.0, settings.UiScale);
            Assert.Equal(260, settings.SidebarWidth);
            var directoryTarget = Directory.CreateDirectory(Path.Combine(root, "invalid-target")).FullName;
            Assert.False(SettingsService.Save(settings, directoryTarget));
            Assert.Empty(Directory.GetFiles(root, "*.tmp"));
        }
        finally { Directory.Delete(root, true); }
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
        thread.Start();
        thread.Join();
        if (error is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
    }
}
