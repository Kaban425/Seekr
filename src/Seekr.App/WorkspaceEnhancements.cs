using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Seekr.App.ViewModels;

namespace Seekr.App;

public partial class WorkspaceView
{
    private Point _fileDragStart;
    private bool _fileDragArmed;
    private WorkspaceViewModel? _sessionWorkspace;
    private bool _restoredScroll;
    private void InitializeEnhancements()
    {
        ResultsList.PreviewMouseLeftButtonDown += (_, e) =>
        {
            _fileDragStart = e.GetPosition(ResultsList);
            _fileDragArmed = e.OriginalSource is DependencyObject source
                && ItemsControl.ContainerFromElement(ResultsList, source) is ListViewItem;
        };
        ResultsList.PreviewMouseMove += (_, e) =>
        {
            if (!_fileDragArmed || e.LeftButton != MouseButtonState.Pressed || DataContext is not WorkspaceViewModel vm) return;
            var point = e.GetPosition(ResultsList);
            if (Math.Abs(point.X - _fileDragStart.X) < SystemParameters.MinimumHorizontalDragDistance
                && Math.Abs(point.Y - _fileDragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            _fileDragArmed = false;
            var paths = vm.SelectedPaths.ToArray();
            if (paths.Length == 0) return;
            var data = new DataObject(DataFormats.FileDrop, paths);
            DragDrop.DoDragDrop(ResultsList, data, DragDropEffects.Copy | DragDropEffects.Move);
        };
        ResultsList.AllowDrop = true;
        ResultsList.DragOver += (_, e) =>
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) && DataContext is WorkspaceViewModel { CurrentPath: not null }
                ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        };
        ResultsList.Drop += (_, e) =>
        {
            e.Handled = true;
            if (DataContext is not WorkspaceViewModel vm || e.Data.GetData(DataFormats.FileDrop) is not string[] paths) return;
            var destination = e.OriginalSource is DependencyObject source
                && ItemsControl.ContainerFromElement(ResultsList, source) is ListViewItem { DataContext: ResultItem { IsDirectory: true } folder }
                ? folder.FullPath : vm.CurrentPath;
            if (destination is null) return;
            var menu = new ContextMenu();
            foreach (var (label, move) in new[] { ("Копировать сюда", false), ("Переместить сюда", true) })
            {
                var item = new MenuItem { Header = label };
                item.Click += (_, _) => vm.TransferPaths(paths, move, destination);
                menu.Items.Add(item);
            }
            menu.Items.Add(new MenuItem { Header = "Отмена" });
            menu.PlacementTarget = ResultsList; menu.IsOpen = true;
        };
        ResultsList.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler((_, e) =>
        {
            if (_restoredScroll && DataContext is WorkspaceViewModel vm && e.VerticalChange != 0)
                vm.SessionScrollOffset = e.VerticalOffset;
        }));
        DataContextChanged += (_, _) =>
        {
            if (_sessionWorkspace is not null) _sessionWorkspace.PropertyChanged -= SessionResultsChanged;
            _sessionWorkspace = DataContext as WorkspaceViewModel;
            _restoredScroll = false;
            if (_sessionWorkspace is not null) _sessionWorkspace.PropertyChanged += SessionResultsChanged;
            RestoreScroll();
        };
        Loaded += (_, _) =>
        {
            if (_sessionWorkspace is null && DataContext is WorkspaceViewModel current)
            { _sessionWorkspace = current; current.PropertyChanged += SessionResultsChanged; }
            RestoreScroll();
        };
        Unloaded += (_, _) =>
        {
            if (_sessionWorkspace is not null) _sessionWorkspace.PropertyChanged -= SessionResultsChanged;
            _sessionWorkspace = null;
        };
    }
    private void SessionResultsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(WorkspaceViewModel.Results) or nameof(WorkspaceViewModel.IsSearching)) RestoreScroll();
    }
    private void RestoreScroll()
    {
        if (_restoredScroll || !IsLoaded || DataContext is not WorkspaceViewModel vm || vm.IsSearching) return;
        Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() =>
        {
            if (_restoredScroll || !IsLoaded || !ReferenceEquals(vm, DataContext) || vm.IsSearching) return;
            ResultsList.UpdateLayout();
            FindResultsScroll(ResultsList)?.ScrollToVerticalOffset(vm.SessionScrollOffset);
            _restoredScroll = true;
        }));
    }
    private static ScrollViewer? FindResultsScroll(DependencyObject parent)
    {
        if (parent is ScrollViewer viewer) return viewer;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            if (FindResultsScroll(VisualTreeHelper.GetChild(parent, i)) is { } found) return found;
        return null;
    }
}
