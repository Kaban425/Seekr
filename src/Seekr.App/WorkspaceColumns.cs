using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Seekr.App.Services;
using Seekr.App.ViewModels;

namespace Seekr.App;

public partial class WorkspaceView
{
    private readonly Dictionary<string, GridViewColumn> _columns = [];
    private readonly Dictionary<string,double> _preferredColumnWidths = [];
    private readonly DispatcherTimer _columnSaveTimer = new() { Interval = TimeSpan.FromMilliseconds(600) };
    private bool _columnsReady;
    private bool _layingOutColumns;
    private bool _customNameWidth;
    private WorkspaceViewModel? _columnViewModel;
    private bool _compactSidebar;
    private GridLength _wideSidebarWidth;
    private bool _freeSpaceHiddenByUser;
    private int _freeSpaceColumnIndex = 3;

    private void InitializeColumns()
    {
        if (_columnsReady) return;
        _columnsReady = true;
        _layingOutColumns = true;
        if (_columns.Count == 0)
            foreach (var column in ResultsGrid.Columns) if (column.Header is string key) _columns[key] = column;
        var settings = SettingsService.Load();
        _freeSpaceHiddenByUser = settings.HiddenColumns.Contains("Свободно");
        _customNameWidth = _customNameWidth || settings.ColumnWidths.ContainsKey("Имя");
        foreach (var pair in _columns)
        {
            if (settings.ColumnWidths.TryGetValue(pair.Key, out var width)) _preferredColumnWidths[pair.Key]=width;
            else if(!_preferredColumnWidths.ContainsKey(pair.Key)) _preferredColumnWidths[pair.Key] = double.IsFinite(pair.Value.Width) ? pair.Value.Width : 154;
            pair.Value.Width=_preferredColumnWidths[pair.Key];
            if (pair.Key != "Имя" && settings.HiddenColumns.Contains(pair.Key)) ResultsGrid.Columns.Remove(pair.Value);
        }
        var selection = SelectionColumn;
        ResultsGrid.Columns.Remove(selection);
        ResultsGrid.Columns.Insert(0, selection);
        var visible = ResultsGrid.Columns.Where(x => x.Header is string).ToArray();
        foreach (var column in visible) ResultsGrid.Columns.Remove(column);
        foreach (var column in visible.OrderBy(x =>
        {
            var index = settings.ColumnOrder.IndexOf((string)x.Header);
            return index >= 0 ? index : int.MaxValue;
        })) ResultsGrid.Columns.Add(column);
        if (_columns.TryGetValue("Свободно", out var freeColumn) && ResultsGrid.Columns.Contains(freeColumn))
            _freeSpaceColumnIndex = ResultsGrid.Columns.IndexOf(freeColumn);
        _layingOutColumns = false;
        foreach (var column in _columns.Values)
            DependencyPropertyDescriptor.FromProperty(GridViewColumn.WidthProperty, typeof(GridViewColumn)).AddValueChanged(column, ColumnWidthChanged);
        ((INotifyCollectionChanged)ResultsGrid.Columns).CollectionChanged += ColumnOrderChanged;
        _columnSaveTimer.Tick += SaveColumnsTick;
        AttachWorkspaceHandlers();
        ResultsList.SizeChanged += ResultsViewportChanged;
        UpdateAdaptiveLayout();
    }

    private void ColumnWidthChanged(object? sender, EventArgs e)
    {
        if (_layingOutColumns) return;
        if (sender == _columns.GetValueOrDefault("Имя")) _customNameWidth = true;
        foreach(var pair in _columns) if(ReferenceEquals(sender,pair.Value) && double.IsFinite(pair.Value.Width)) _preferredColumnWidths[pair.Key]=pair.Value.Width;
        ScheduleColumnSave();
    }
    private void ColumnOrderChanged(object? sender, NotifyCollectionChangedEventArgs e) { if (!_layingOutColumns) ScheduleColumnSave(); }
    private void ScheduleColumnSave() { _columnSaveTimer.Stop(); _columnSaveTimer.Start(); }
    private void SaveColumnsTick(object? sender, EventArgs e) { _columnSaveTimer.Stop(); SaveColumns(); }

    private void SaveColumns()
    {
        if (!_columnsReady || DataContext is not WorkspaceViewModel { PersistViewSettings: true }) return;
        var settings = SettingsService.Load();
        settings.ColumnWidths = _columns.Where(x => x.Key != "Имя" || _customNameWidth)
            .ToDictionary(x => x.Key, x => _preferredColumnWidths.GetValueOrDefault(x.Key,154));
        settings.HiddenColumns = _columns.Where(x => x.Key == "Свободно" ? _freeSpaceHiddenByUser : !ResultsGrid.Columns.Contains(x.Value)).Select(x => x.Key).ToList();
        settings.ColumnOrder = ResultsGrid.Columns.Where(x => x.Header is string).Select(x => (string)x.Header).ToList();
        if (!settings.ColumnOrder.Contains("Свободно"))
            settings.ColumnOrder.Insert(Math.Clamp(_freeSpaceColumnIndex - 1, 0, settings.ColumnOrder.Count), "Свободно");
        SettingsService.Save(settings, notify: false);
    }

    private ContextMenu CreateColumnsMenu()
    {
        var menu = new ContextMenu();
        foreach (var pair in _columns)
        {
            if (pair.Key == "Свободно" && DataContext is not WorkspaceViewModel { IsHome: true }) continue;
            var item = new MenuItem { Header = pair.Key, IsCheckable = true, IsChecked = ResultsGrid.Columns.Contains(pair.Value), IsEnabled = pair.Key != "Имя" };
            item.Click += (_, _) =>
            {
                if (pair.Key == "Свободно") _freeSpaceHiddenByUser = !item.IsChecked;
                if (item.IsChecked) ResultsGrid.Columns.Add(pair.Value);
                else ResultsGrid.Columns.Remove(pair.Value);
                UpdateAdaptiveLayout();
            };
            menu.Items.Add(item);
        }
        var reset = new MenuItem { Header = "Сбросить ширину столбцов" };
        reset.Click += (_, _) =>
        {
            _layingOutColumns = true;
            foreach (var pair in _columns) pair.Value.Width = pair.Key switch { "Имя" => 300, "Размер" => 112, "Тип" => 144, _ => 154 };
            foreach(var pair in _columns) _preferredColumnWidths[pair.Key]=pair.Value.Width;
            _customNameWidth = false;
            _layingOutColumns = false;
            UpdateAdaptiveLayout(); ScheduleColumnSave();
        };
        menu.Items.Add(new Separator()); menu.Items.Add(reset);
        return menu;
    }

    private void ColumnsSubmenu_Opened(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem item || e.OriginalSource != sender) return;
        var menu = CreateColumnsMenu();
        item.Items.Clear();
        while (menu.Items.Count > 0) { var child = menu.Items[0]; menu.Items.RemoveAt(0); item.Items.Add(child); }
    }

    private void WorkspaceView_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateAdaptiveLayout();
    private void WorkspacePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(WorkspaceViewModel.IsHome)) UpdateContextualColumns();
        if (e.PropertyName == nameof(WorkspaceViewModel.ShowPreviewPane)) UpdateAdaptiveLayout();
        if (e.PropertyName is nameof(WorkspaceViewModel.SortColumn) or nameof(WorkspaceViewModel.IsSortAscending)) UpdateSortHeaders();
    }

    private bool _showCompactSidebar;
    private void ToggleNavigation_Click(object sender, RoutedEventArgs e)
    { _showCompactSidebar = sender is MenuItem { IsChecked: true }; UpdateAdaptiveLayout(); }

    private void UpdateAdaptiveLayout()
    {
        UpdateContextualColumns();
        if (PreviewPanel is null || ResultsList is null) return;
        if (SearchColumn is not null && SearchBox is not null)
        {
            var narrow = ActualWidth > 0 && ActualWidth < 760;
            SearchRow.Height = new GridLength(narrow ? 46 : 0);
            Grid.SetRow(SearchSurface, narrow ? 1 : 0);
            Grid.SetColumn(SearchSurface, narrow ? 0 : 2);
            Grid.SetColumnSpan(SearchSurface, narrow ? 3 : 1);
            SearchSurface.Margin = narrow ? new Thickness(0, 0, 0, 8) : new Thickness(0);
            SearchColumn.Width = new GridLength(narrow ? 0 : ActualWidth < 1000 ? 290 : SearchBox.IsKeyboardFocusWithin ? 420 : 340);
            var labels = narrow ? Visibility.Collapsed : Visibility.Visible;
            CreateLabel.Visibility = labels; SortLabel.Visibility = labels;
            ViewLabel.Visibility = labels; DetailsLabel.Visibility = labels;
            CreateIcon.Margin = DetailsIcon.Margin = new Thickness(0, 0, narrow ? 0 : 8, 0);
            foreach (var button in new[] { CreateFolderButton, SortMenuButton, ViewMenuButton, DetailsButton })
                button.Padding = narrow ? new Thickness(8, 6, 8, 6) : new Thickness(12, 6, 12, 6);
            var tiny = ActualWidth > 0 && ActualWidth < 560;
            var extraLabels = ActualWidth >= 1050 ? Visibility.Visible : Visibility.Collapsed;
            SpaceMapLabel.Visibility = DownloadsLabel.Visibility = extraLabels;
            SpaceMapButton.Padding = DownloadsButton.Padding = new Thickness(extraLabels == Visibility.Visible ? 12 : 6, 6, extraLabels == Visibility.Visible ? 12 : 6, 6);
            SpaceMapButton.Visibility = DownloadsButton.Visibility = tiny ? Visibility.Collapsed : Visibility.Visible;
            EditActions.Visibility = tiny ? Visibility.Collapsed : Visibility.Visible;
            var hideSidebar = tiny && !_showCompactSidebar;
            SidebarSurface.Visibility = SidebarSplitter.Visibility = hideSidebar ? Visibility.Collapsed : Visibility.Visible;
            NavigationColumn.MinWidth = hideSidebar ? 0 : 140;
            SidebarSplitterColumn.Width = new GridLength(hideSidebar ? 0 : 4);
            if (hideSidebar) NavigationColumn.Width = new GridLength(0);
            else if (_compactSidebar) NavigationColumn.Width = new GridLength(160);
            if (narrow && !_compactSidebar)
            { _wideSidebarWidth = new GridLength(208); NavigationColumn.Width = new GridLength(160); _compactSidebar = true; }
            else if (!narrow && _compactSidebar)
            { NavigationColumn.Width = _wideSidebarWidth; _compactSidebar = false; }
        }
        PreviewPanel.Visibility = DataContext is WorkspaceViewModel { ShowPreviewPane: true } ? Visibility.Visible : Visibility.Collapsed;
        var overlay=ActualWidth<1050;
        Grid.SetColumn(PreviewPanel,overlay?2:3); Grid.SetColumnSpan(PreviewPanel,overlay?2:1);
        PreviewPanel.HorizontalAlignment=overlay?HorizontalAlignment.Right:HorizontalAlignment.Stretch;
        FitResultColumns();
    }

    private void ResultsViewportChanged(object sender,SizeChangedEventArgs e)=>FitResultColumns();
    private void FitResultColumns()
    {
        if (!_columnsReady || ResultsList.ActualWidth<=0) return;
        var preferred=ResultsGrid.Columns.Where(column=>column.Header is string).ToDictionary(column=>(string)column.Header,column=>_preferredColumnWidths.GetValueOrDefault((string)column.Header,154));
        var layout=AdaptiveColumnLayout.Calculate(preferred,ResultsList.ActualWidth,SelectionColumn.Width,_customNameWidth);
        _layingOutColumns = true;
        try { foreach(var column in ResultsGrid.Columns) if(column.Header is string key && layout.TryGetValue(key,out var width)) column.Width=width; }
        finally { _layingOutColumns = false; }
    }

    private void UpdateContextualColumns()
    {
        if (!_columnsReady || !_columns.TryGetValue("Свободно", out var column)) return;
        var show = !_freeSpaceHiddenByUser && DataContext is WorkspaceViewModel { IsHome: true };
        var present = ResultsGrid.Columns.Contains(column);
        if (show == present) return;
        var previous = _layingOutColumns;
        _layingOutColumns = true;
        try
        {
            if (show) ResultsGrid.Columns.Insert(Math.Clamp(_freeSpaceColumnIndex, 1, ResultsGrid.Columns.Count), column);
            else { _freeSpaceColumnIndex = ResultsGrid.Columns.IndexOf(column); ResultsGrid.Columns.Remove(column); }
        }
        finally { _layingOutColumns = previous; }
    }

    private void WorkspaceView_Unloaded(object sender, RoutedEventArgs e)
    {
        ResultsList.SizeChanged -= ResultsViewportChanged;
        if (!_columnsReady) return;
        SaveColumns();
        _columnSaveTimer.Stop();
        _columnSaveTimer.Tick -= SaveColumnsTick;
        foreach (var column in _columns.Values)
            DependencyPropertyDescriptor.FromProperty(GridViewColumn.WidthProperty, typeof(GridViewColumn)).RemoveValueChanged(column, ColumnWidthChanged);
        ((INotifyCollectionChanged)ResultsGrid.Columns).CollectionChanged -= ColumnOrderChanged;
        DetachWorkspaceHandlers();
        _columnsReady = false;
    }
}

