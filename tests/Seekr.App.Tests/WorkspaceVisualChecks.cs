using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Seekr.App.Services;
using Seekr.App.ViewModels;

namespace Seekr.App.Tests;

public sealed partial class WorkspaceTests
{
    private static readonly double[] VisualScales = [0.75, 1.0, 1.25, 1.5];
    private static readonly string[] VisualThemes = ["Dark", "Light"];
    private static readonly string[] ReadableSurfaces = ["Canvas", "Sidebar", "Surface", "Selection", "InactiveSelection", "Hover"];

    private static void VerifyVisualMatrix(App app, WorkspaceView view, WorkspaceViewModel vm)
    {
        vm.ShowPreviewPane = false;
        vm.CompactMode = false; vm.IsIndexing = false;
        vm.CurrentPath = @"C:\Проекты";
        vm.IsSearching = false;
        vm.ErrorMessage = null;
        vm.ShowSizeBars = true;
        vm.Results = [VisualItem(0, @"C:\Проекты\Документы", true, 320000000), VisualItem(1, @"C:\Проекты\Презентация.pdf", false, 12000000),
            VisualItem(2, @"C:\Проекты\Заметки.txt", false, 12000), VisualItem(3, @"C:\Проекты\Архив.zip", false, 24000000)];
        vm.Results[1].IsSelected = true;
        vm.Status = "Элементов: 4 · C:\\Проекты";
        var surface = new ContentControl { Content = view, HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch };
        surface.SetResourceReference(FrameworkElement.LayoutTransformProperty, "Seekr.ContentScale");
        var host = new Grid();
        host.Children.Add(surface);
        var owner = new Window { Content = host };
        var promptInput = new Wpf.Ui.Controls.TextBox { Text = "Выделенный текст", Margin = new Thickness(24), Width = 300 };
        promptInput.SetResourceReference(FrameworkElement.StyleProperty, "Seekr.FormInput");
        var prompt = new Wpf.Ui.Controls.FluentWindow { Content = promptInput, SizeToContent = SizeToContent.WidthAndHeight };
        WindowPresentation.ApplyDialog(prompt, promptInput);
        view.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
        try
        {
            foreach (var theme in VisualThemes)
            {
                ThemeService.ApplyTheme(theme);
                SaveIconAtlas(app, theme);
                foreach (var background in ReadableSurfaces)
                {
                    Assert.True(Contrast(Brush(app, "TextPrimary"), Brush(app, background)) >= 4.5, $"{theme}: primary text on {background}");
                    Assert.True(Contrast(Brush(app, "TextSecondary"), Brush(app, background)) >= 4.5, $"{theme}: secondary text on {background}");
                }
                Assert.True(Contrast(Brush(app, "ErrorText"), Brush(app, "ErrorSurface")) >= 4.5);
                Assert.True(Contrast(Brush(app, "Focus"), Brush(app, "Selection")) >= 3);
                var searchInput = (TextBox)view.FindName("SearchBox");
                Assert.True(Contrast(((SolidColorBrush)searchInput.SelectionTextBrush).Color, ((SolidColorBrush)searchInput.SelectionBrush).Color) >= 4.5);

                foreach (var scale in VisualScales)
                {
                    ScaleService.ApplyScale(scale);
                    foreach (var width in LayoutWidths)
                    {
                        Arrange(host, width);
                        Assert.Equal(scale, ((ScaleTransform)((ScrollViewer)prompt.Content).LayoutTransform).ScaleX);
                        Assert.Equal(Brush(app, "TextPrimary"), ((SolidColorBrush)prompt.Foreground).Color);
                        Assert.True(prompt.MaxHeight <= SystemParameters.WorkArea.Height);
                        Assert.True(Contrast(((SolidColorBrush)promptInput.SelectionTextBrush).Color, ((SolidColorBrush)promptInput.SelectionBrush).Color) >= 4.5);
                        Assert.True(((ScaleTransform)surface.LayoutTransform).ScaleX == scale, $"Scale {scale}; surface={((ScaleTransform)surface.LayoutTransform).ScaleX}; app={((ScaleTransform)app.FindResource("Seekr.ContentScale")).ScaleX}");
                        var address = (FrameworkElement)view.FindName("AddressBar");
                        var search = (FrameworkElement)view.FindName("SearchSurface");
                        Assert.True(address.ActualWidth >= 100, $"Address width at {theme}/{scale}/{width}: {address.ActualWidth}");
                        Assert.True(search.ActualWidth >= 200);
                        var narrow = view.ActualWidth < 760;
                        var delta = search.TranslatePoint(new(), view).Y - address.TranslatePoint(new(), view).Y;
                        if (narrow) Assert.True(delta >= 40);
                        else Assert.InRange(Math.Abs(delta), 0, 3);
                        var details = (FrameworkElement)view.FindName("DetailsButton");
                        var point = details.TranslatePoint(new Point(details.ActualWidth, details.ActualHeight), view);
                        Assert.True(point.X <= view.ActualWidth + 1, $"Toolbar exceeds viewport at {theme}/{scale}/{width}");
                        var scopeButton = (Button)view.FindName("SearchScopeButton");
                        var scopeText = Descendant<TextBlock>(scopeButton, _ => true);
                        Assert.NotNull(scopeText);
                        Assert.True(scopeText.DesiredSize.Width + scopeButton.Padding.Left + scopeButton.Padding.Right + 2 <= scopeButton.ActualWidth,
                            "The search scope label must fit inside its button.");
                        var expander = Descendant<System.Windows.Controls.Primitives.ToggleButton>(view, b => b.Name == "Expander" && b.Visibility == Visibility.Visible);
                        Assert.NotNull(expander);
                        Assert.InRange(expander.ActualWidth, 17.5, 18.5);
                        var arrow = (FrameworkElement)expander.Template.FindName("Arrow", expander);
                        var arrowPoint = arrow.TranslatePoint(new Point(), expander);
                        Assert.True(arrowPoint.X >= 0 && arrowPoint.X + arrow.ActualWidth <= expander.ActualWidth, $"Arrow x={arrowPoint.X}, glyph={arrow.ActualWidth}, button={expander.ActualWidth}, scale={scale}");
                        if (scale == 1.0 && width == 1100) SaveVisual(host, theme.ToLowerInvariant() + "-100");
                        if (theme == "Light" && scale == 1.5 && width == 840) SaveVisual(host, "light-150-narrow");
                    }
                }
            }
            var popup = (System.Windows.Controls.Primitives.Popup)view.FindName("FiltersPopup");
            Assert.Equal(System.Windows.Controls.Primitives.PlacementMode.Custom, popup.Placement);
            var placements = popup.CustomPopupPlacementCallback(new Size(320, 300), new Size(28, 30), new Point());
            Assert.Equal(-292, placements[0].Point.X);
            var sizeHeader = Descendant<GridViewColumnHeader>(view, header => header.Column?.Header is string name && name == "Размер");
            Assert.NotNull(sizeHeader);
            var gripper = (System.Windows.Controls.Primitives.Thumb)sizeHeader.Template.FindName("PART_HeaderGripper", sizeHeader);
            var beforeWidth = sizeHeader.Column.Width;
            gripper.RaiseEvent(new System.Windows.Controls.Primitives.DragStartedEventArgs(0, 0) { RoutedEvent = System.Windows.Controls.Primitives.Thumb.DragStartedEvent });
            gripper.RaiseEvent(new System.Windows.Controls.Primitives.DragDeltaEventArgs(20, 0) { RoutedEvent = System.Windows.Controls.Primitives.Thumb.DragDeltaEvent });
            gripper.RaiseEvent(new System.Windows.Controls.Primitives.DragCompletedEventArgs(20, 0, false) { RoutedEvent = System.Windows.Controls.Primitives.Thumb.DragCompletedEvent });
            Assert.True(sizeHeader.Column.Width > beforeWidth, "The custom header must preserve column resizing.");
            ThemeService.ApplyTheme("Light"); ScaleService.ApplyScale(1);
            vm.Results = []; vm.Query = "report ext:pdf"; vm.IsSearching = false;
            Arrange(host, 1100); SaveVisual(host, "light-empty-search");
            var panel = (Border)view.FindName("EmptyStatePanel");
            Assert.Equal(Visibility.Visible, panel.Visibility);
            var reset = Descendant<Button>(panel, button => button.Command == vm.ResetEmptySearchCommand);
            Assert.NotNull(reset);
            Assert.True(reset.IsVisible || reset.Visibility == Visibility.Visible);
            var hit = VisualTreeHelper.HitTest(host, reset.TranslatePoint(new Point(reset.ActualWidth / 2, reset.ActualHeight / 2), host))?.VisualHit;
            Assert.True(IsWithin(hit, reset), $"An empty-state action must not be covered by the empty file table. Hit={hit?.GetType().Name}, panel={panel.ActualWidth}/{panel.ActualHeight}, reset={reset.ActualWidth}/{reset.ActualHeight}, enabled={reset.IsEnabled}, scale={surface.LayoutTransform}, point={reset.TranslatePoint(new Point(reset.ActualWidth/2,reset.ActualHeight/2),host)}");
            vm.ErrorMessage = "Нет доступа к папке. Проверьте права или выберите другой путь.";
            Arrange(host, 1100); SaveVisual(host, "light-error");
            Assert.Equal(Visibility.Visible, ((FrameworkElement)view.FindName("ErrorBanner")).Visibility);
            vm.ErrorMessage = null; vm.IsSearching = true; vm.ShowSearchProgress = true;
            ThemeService.ApplyTheme("Dark"); Arrange(host, 1100); SaveVisual(host, "dark-loading");
            Assert.Equal(Visibility.Visible, ((FrameworkElement)view.FindName("SearchProgress")).Visibility);
        }
        finally
        {
            vm.IsSearching = false;
            view.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
            surface.Content = null;
            owner.Content = null; owner.Close(); prompt.Close();
            ScaleService.ApplyScale(1);
            ThemeService.ApplyTheme("Dark");
        }
    }

    private static void SaveIconAtlas(App app, string theme)
    {
        var panel = new WrapPanel { Width = 760, Margin = new Thickness(24) };
        foreach (var kind in Enum.GetValues<Seekr.App.Icons.IconKind>().Where(x => x != Seekr.App.Icons.IconKind.None))
        {
            var cell = new StackPanel { Width = 100, Height = 84, Margin = new Thickness(4) };
            cell.Children.Add(new Seekr.App.Icons.SeekrIcon { Kind = kind, Width = 28, Height = 28,
                Foreground = (SolidColorBrush)app.FindResource("Seekr.TextPrimary"), HorizontalAlignment = HorizontalAlignment.Center });
            cell.Children.Add(new Image { Source = Seekr.App.Icons.IconArtwork.Image(kind), Width = 22, Height = 22,
                HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 0) });
            cell.Children.Add(new TextBlock { Text = kind.ToString(), FontSize = 11, TextAlignment = TextAlignment.Center,
                Foreground = (SolidColorBrush)app.FindResource("Seekr.TextSecondary"), Margin = new Thickness(0, 4, 0, 0) });
            panel.Children.Add(cell);
        }
        var host = new Border { Width = 808, Height = 610, Background = (SolidColorBrush)app.FindResource("Seekr.Canvas"), Child = panel };
        host.Measure(new Size(808, 610)); host.Arrange(new Rect(0, 0, 808, 610)); host.UpdateLayout();
        SaveVisual(host, "seekr-icons-" + theme.ToLowerInvariant());
    }

    private static ResultItem VisualItem(int id, string path, bool folder, long size) => new(new(
        new Seekr.Core.Index.FileEntry(id, -1, Path.GetFileName(path), folder, size, DateTime.UtcNow, DateTime.UtcNow), path, size));

    private static void Arrange(FrameworkElement element, double width)
    {
        element.Measure(new Size(width, 700));
        element.Arrange(new Rect(0, 0, width, 700));
        element.UpdateLayout();
    }

    private static T? Descendant<T>(DependencyObject parent, Func<T, bool> match) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T value && match(value)) return value;
            if (Descendant(child, match) is { } found) return found;
        }
        return null;
    }

    private static bool IsWithin(DependencyObject? child, DependencyObject parent)
    {
        for (; child is not null; child = VisualTreeHelper.GetParent(child)) if (child == parent) return true;
        return false;
    }

    private static Color Brush(App app, string key) => ((SolidColorBrush)app.FindResource("Seekr." + key)).Color;
    private static double Contrast(Color foreground, Color background)
    {
        static double Channel(byte value) { var v = value / 255.0; return v <= 0.04045 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4); }
        static double Luminance(Color c) => 0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B);
        var a = Luminance(foreground); var b = Luminance(background);
        return (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
    }

    private static void VerifyFileManagerV2Visuals()
    {
        using var shell = new ShellViewModel(false, false);
        shell.SelectedWorkspace!.CurrentPath = @"C:\Проекты";
        shell.SelectedWorkspace.Results = [VisualItem(0, @"C:\Проекты\Заметки.txt", false, 12000)];
        shell.SelectedWorkspace.IsSearching = false;
        shell.IsDualPane = true;
        shell.SecondaryWorkspace!.CurrentPath = @"D:\Архив";
        shell.SecondaryWorkspace.Results = [VisualItem(1, @"D:\Архив\Документы", true, 320000)];
        shell.SecondaryWorkspace.IsSearching = false;
        var window = new MainWindow(shell);
        var root = (FrameworkElement)window.Content;
        var firstTab = shell.SelectedWorkspace;
        shell.NewTabCommand.Execute(null); Arrange(root, 1440);
        shell.CloseCurrentTabCommand.Execute(null); Arrange(root, 1440);
        Assert.Same(firstTab, shell.SelectedWorkspace);
        Assert.Same(firstTab, Descendants<WorkspaceView>(root).First().DataContext);
        foreach (var theme in VisualThemes)
        {
            ThemeService.ApplyTheme(theme);
            foreach (var scale in new[] { 1.0, 1.5 })
            {
                ScaleService.ApplyScale(scale);
                Arrange(root, scale == 1 ? 1440 : 840);
                var caption = Descendants<SeekrTitleBar>(root).Single();
                Assert.NotNull(caption.Template);
                Assert.NotNull(caption.Template.FindName("PART_MinimizeButton", caption));
                Assert.NotNull(caption.Template.FindName("PART_CloseButton", caption));
                Assert.True(((FrameworkElement)window.FindName("TabList")).ActualHeight > 0);
                var views = Descendants<WorkspaceView>(root).ToArray();
                Assert.Equal(2, views.Length);
                foreach (var pane in views)
                {
                    Assert.True(((FrameworkElement)pane.FindName("SearchSurface")).ActualWidth > 200);
                    if (scale == 1.5) Assert.Equal(Visibility.Collapsed, ((FrameworkElement)pane.FindName("SidebarSurface")).Visibility);
                }
                SaveVisual(root, $"v2-dual-{theme.ToLowerInvariant()}-{scale * 100:0}");
            }
        }
        using var queue = new FileOperationService();
        var dialog = new BatchRenameWindow([], queue);
        var content = (FrameworkElement)dialog.Content;
        ScaleService.ApplyScale(1); Arrange(content, 800); SaveVisual(content, "v2-batch-rename");
        dialog.Close(); window.Close();
        ScaleService.ApplyScale(1); ThemeService.ApplyTheme("Dark");
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) yield return match;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }

    private static void VerifyLockWindowAndDesigns()
    {
        using (var window = new LocksWindow([], canRetryDelete: false, (_, token) => { while (!token.IsCancellationRequested) Thread.Sleep(1); token.ThrowIfCancellationRequested(); return new([], false); }))
        {
            var closed = false; window.Closed += (_, _) => closed = true;
            window.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
            var close = (Button)window.FindName("CloseWindowButton");
            close.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            WaitFor(window.InspectionCompletion);
            Assert.True(closed);
            Assert.Equal(Visibility.Collapsed, ((Button)window.FindName("RetryDeleteButton")).Visibility);
        }
        using (var window = new LocksWindow([], inspect: (_, _) => new([], false)))
        {
            window.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent)); WaitFor(window.InspectionCompletion);
            var root = (FrameworkElement)window.Content; window.Content = null;
            var host = new Border { Child = root }; host.SetResourceReference(Border.BackgroundProperty, "Seekr.MenuSurface"); window.Content = host;
            foreach (var scale in new[] { 1.0, 1.5 })
            { ScaleService.ApplyScale(scale); Arrange(host, 680); var close = (Button)window.FindName("CloseWindowButton"); var point = close.TranslatePoint(new Point(), host); Assert.True(close.ActualWidth > 0 && point.X >= 0 && point.X + close.ActualWidth * scale <= 681); SaveVisual(host, $"locks-close-{scale * 100:0}"); }
            window.Close();
        }
        using var vm = new WorkspaceViewModel(persistViewSettings: false);
        vm.Results = [VisualItem(0, @"C:\Notes.txt", false, 123)]; vm.IsSearching = false;
        var view = new WorkspaceView { DataContext = vm };
        var designOwner = new Window { Content = view };
        foreach (var design in new SettingsViewModel(new AppSettings()).Designs.Where(x => x != "Default"))
        {
            var profileFile = Path.Combine(Path.GetTempPath(), "Seekr-profile-" + Guid.NewGuid().ToString("N") + ".json");
            var profile = new SettingsViewModel(new AppSettings(), profileFile);
            profile.ApplyDesignCommand.Execute(design); profile.FinishEditing();
            var appearance = SettingsService.Load(profileFile); File.Delete(profileFile);
            Assert.Equal(design, appearance.Design);
            foreach (var theme in VisualThemes)
            {
                ThemeService.ApplyTheme(theme);
                AppearanceService.Apply(appearance);
                vm.Results = [VisualItem(0, @"C:\Notes.txt", false, 123)];
                foreach (var surface in ReadableSurfaces)
                {
                    if (surface == "Canvas") continue;
                    Assert.True(Contrast(Brush((App)Application.Current, "TextPrimary"), Brush((App)Application.Current, surface)) >= 4.5, design + "/" + theme + "/" + surface);
                    Assert.True(Contrast(Brush((App)Application.Current, "TextSecondary"), Brush((App)Application.Current, surface)) >= 4.5, design + "/" + theme + "/" + surface);
                }
                ScaleService.ApplyScale(1); Arrange(view, 1100);
                Assert.True(Descendants<Seekr.App.Icons.SeekrIcon>(view).First(icon => icon.Kind == Seekr.App.Icons.IconKind.Back).ActualWidth >= 10, design + " back icon must fit");
                SaveVisual(view, "design-" + design.ToLowerInvariant() + "-" + theme.ToLowerInvariant());
            }
        }
        designOwner.Content = null; designOwner.Close();
        ThemeService.ApplyTheme("Dark"); AppearanceService.Apply(new AppSettings()); ScaleService.ApplyScale(1);
    }

    private static void VerifySettingsAndMenuVisuals()
    {
        VerifyPersonalizationAssetsAndConfirmation();
        foreach (var scale in new[] { 1.0, 1.5 })
        {
            var error = new ErrorWindow(null);
            var content = (FrameworkElement)error.Content;
            ScaleService.ApplyScale(scale); Arrange(content, 580); SaveVisual(content, $"release-error-{scale * 100:0}");
            Assert.DoesNotContain(Descendants<TextBlock>(content), text => text.Text.Contains("StackTrace", StringComparison.Ordinal));
            var closed = false; error.Closed += (_, _) => closed = true;
            Descendant<Button>(content, button => Equals(button.Content, "Закрыть"))!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(closed);
        }
        ScaleService.ApplyScale(1);
        using (var indexes = new SharedIndexService(useCache: false))
        {
            var status = new IndexStatusWindow(indexes);
            var closed = false; status.Closed += (_, _) => closed = true;
            var content = (FrameworkElement)status.Content;
            ScaleService.ApplyScale(1); Arrange(content, 800); SaveVisual(content, "index-status-close");
            var close = (Button)status.FindName("CloseWindowButton"); Assert.True(close.ActualWidth > 0);
            close.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Assert.True(closed);
        }
        var settings = new SettingsWindow();
        var root = (FrameworkElement)settings.Content;
        foreach (var theme in VisualThemes)
        {
            ThemeService.ApplyTheme(theme);
            AppearanceService.Apply(new AppSettings { Design="Graphite" });
            Assert.Equal(255,((SolidColorBrush)Application.Current.Resources["Seekr.MenuSurface"]).Color.A);
            foreach (var scale in new[] { 1.0, 1.5 })
            {
                ScaleService.ApplyScale(scale); Arrange(root, 820);
                Assert.NotNull(Descendant<Button>(root, b => Equals(b.Content, "Вернуть изменения")));
                SaveVisual(root, $"settings-live-{theme.ToLowerInvariant()}-{scale * 100:0}");
            }
        }
        settings.Close(); ScaleService.ApplyScale(1); ThemeService.ApplyTheme("Dark"); AppearanceService.Apply(new AppSettings());
    }

    private static void VerifyPersonalizationAssetsAndConfirmation()
    {
        var folder = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "Seekr-personalization-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            foreach (var size in new[] { (16, 32), (16, 4096), (4096, 16) })
            {
                var path = Path.Combine(folder, $"{size.Item1}-{size.Item2}.png");
                var bitmap = BitmapSource.Create(size.Item1, size.Item2, 96, 96, PixelFormats.Bgra32, null, new byte[size.Item1 * size.Item2 * 4], size.Item1 * 4);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using (var output = File.Create(path)) encoder.Save(output);
                var wallpaper = (DrawingBrush)WallpaperCatalog.Custom(path, true);
                Assert.True(wallpaper.IsFrozen);
                var image = (BitmapSource)((ImageDrawing)((DrawingGroup)wallpaper.Drawing).Children[0]).ImageSource;
                Assert.InRange(image.PixelWidth, 1, 1280); Assert.InRange(image.PixelHeight, 1, 1280);
                if (size.Item1 == 16 && size.Item2 == 32) Assert.Equal(16, image.PixelWidth);
                var preview = BoundedImageLoader.Load(path, 1800, 50L * 1024 * 1024);
                Assert.InRange(preview.PixelWidth, 1, 1800); Assert.InRange(preview.PixelHeight, 1, 1800);
                if (size.Item1 == 16 && size.Item2 == 32) Assert.Equal(16, preview.PixelWidth);
                using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
                Assert.Throws<OperationCanceledException>(() => BoundedImageLoader.Load(path, 1800, 50L * 1024 * 1024, cancelled.Token));
            }
            var config = Path.Combine(folder, "settings.json");
            var settings = new AppSettings { Design = "Arcade", Font = "Pixel", Background = "Custom", CustomWallpaper = Path.Combine(folder, "16-32.png"), HiddenNavigationPaths = [@"C:\Users\test\Downloads"] };
            Assert.True(SettingsService.Save(settings, config));
            var restored = SettingsService.Load(config);
            Assert.Equal(settings.CustomWallpaper, restored.CustomWallpaper); Assert.Equal(settings.HiddenNavigationPaths, restored.HiddenNavigationPaths);
            Assert.Equal("Arcade", restored.Design); Assert.Equal("Pixel", restored.Font);
            var invalid = Path.Combine(folder, "invalid.png"); File.WriteAllText(invalid, "not an image");
            AppearanceService.Apply(new AppSettings { Background = "Custom", CustomWallpaper = invalid });
        }
        finally { Directory.Delete(folder, true); }
        foreach (var theme in VisualThemes)
        {
            ThemeService.ApplyTheme(theme); AppearanceService.Apply(new AppSettings());
            var dialog = new ConfirmationWindow("Удалить выбранные файлы?", "В корзину будут перемещены только выбранные файлы.\n\nC:\\Documents\\Example.txt", "В корзину");
            var root = (FrameworkElement)dialog.Content; dialog.Content = null;
            var host = new Border { Child = root }; dialog.Content = host;
            foreach (var scale in new[] { 1.0, 1.5 })
            {
                ScaleService.ApplyScale(scale); host.Measure(new Size(560, 340)); host.Arrange(new Rect(0, 0, 560, 340)); host.UpdateLayout();
                Assert.NotNull(Descendant<Button>(root, button => Equals(button.Content, "Отмена")));
                SaveVisual(host, $"delete-confirm-{theme.ToLowerInvariant()}-{scale * 100:0}");
            }
            var closed = false; dialog.Closed += (_, _) => closed = true;
            Descendant<Button>(root, button => Equals(button.Content, "Отмена"))!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(closed);
        }
        ScaleService.ApplyScale(1); ThemeService.ApplyTheme("Dark"); AppearanceService.Apply(new AppSettings());
    }

    private static void SaveVisual(FrameworkElement element, string name)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Seekr.slnx"))) root = root.Parent;
        Assert.NotNull(root);
        var directory = Directory.CreateDirectory(Path.Combine(root.FullName, "artifacts", "visual-polish")).FullName;
        var bitmap = new RenderTargetBitmap((int)element.ActualWidth, (int)element.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(element);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(Path.Combine(directory, name + ".png")); encoder.Save(file);
    }
}



