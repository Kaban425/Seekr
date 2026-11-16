using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Seekr.App.ViewModels;

namespace Seekr.App;

public partial class WorkspaceView : UserControl
{
    private Point _dragStart;
    private NavigationItem? _draggedItem;

    public WorkspaceView()
    {
        InitializeComponent();
        InitializeEnhancements();
        FiltersPopup.CustomPopupPlacementCallback = AlignSearchPopup;
        SavedSearchPopup.CustomPopupPlacementCallback = AlignSearchPopup;
        PreviewKeyDown += Workspace_PreviewKeyDown;
        DataContextChanged += (_, _) => { if (_columnsReady) AttachWorkspaceHandlers(); EndAddressEdit(false); };
    }

    private static System.Windows.Controls.Primitives.CustomPopupPlacement[] AlignSearchPopup(Size popup, Size target, Point offset) =>
    [
        new(new Point(target.Width - popup.Width + offset.X, target.Height + 4 + offset.Y), System.Windows.Controls.Primitives.PopupPrimaryAxis.Horizontal),
        new(new Point(target.Width - popup.Width + offset.X, -popup.Height - 4 + offset.Y), System.Windows.Controls.Primitives.PopupPrimaryAxis.Horizontal),
    ];

    private void WorkspaceView_Loaded(object sender, RoutedEventArgs e)
    {
        SearchBox.Focus();
        InitializeColumns();
    }

    private void SearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && DataContext is WorkspaceViewModel vm && !string.IsNullOrEmpty(vm.Query))
        { vm.ResetEmptySearchCommand.Execute(null); e.Handled = true; }
        else if (e.Key == Key.Tab && Keyboard.Modifiers == ModifierKeys.None && DataContext is WorkspaceViewModel suggestions && suggestions.QuerySuggestions.Count > 0)
        { suggestions.InsertSuggestionCommand.Execute(suggestions.QuerySuggestions[0]); SearchBox.CaretIndex = SearchBox.Text.Length; e.Handled = true; }
        else if (e.Key == Key.Down && ResultsList.Items.Count > 0)
        {
            ResultsList.SelectedIndex = 0;
            FocusSelected();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && ResultsList.Items.Count > 0)
        {
            if (ResultsList.SelectedIndex < 0) ResultsList.SelectedIndex = 0;
            FocusSelected();
            e.Handled = true;
        }
    }

    private void ResultsList_KeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not WorkspaceViewModel vm)
        {
            return;
        }

        if (e.Key == Key.Enter && ResultsList.SelectedItem is ResultItem item)
        {
            Activate(item);
            e.Handled = true;
        }
        else if (e.Key == Key.Space && Keyboard.Modifiers == ModifierKeys.None)
        { vm.PreviewSelectedCommand.Execute(null); e.Handled = true; }
        else if (e.Key == Key.Delete)
        {
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
                vm.PermanentDeleteCommand.Execute(null);
            else vm.DeleteSelectedCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void Workspace_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not WorkspaceViewModel vm) return;
        if (e.Key == Key.Escape && (vm.FiltersOpen || vm.SaveSearchOpen))
        { vm.FiltersOpen = false; vm.SaveSearchOpen = false; SearchBox.Focus(); e.Handled = true; return; }
        if (e.Key == Key.F1)
        { vm.ShowHelpCommand.Execute(null); e.Handled = true; }
        else if (e.Key == Key.F5)
        { vm.RefreshCommand.Execute(null); e.Handled = true; }
        else if (e.SystemKey == Key.Up && Keyboard.Modifiers == ModifierKeys.Alt)
        { vm.NavigateUpCommand.Execute(null); e.Handled = true; }
        else if (e.Key == Key.N && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        { vm.CreateFolderCommand.Execute(null); e.Handled = true; }
        else if (e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control && vm.SaveSearchCommand.CanExecute(null))
        { vm.SaveSearchCommand.Execute(null); e.Handled = true; }
        else if (e.Key == Key.L && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        { vm.OpenPathCommand.Execute(null); e.Handled = true; }
        else if (e.Key == Key.F && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        { SearchBox.Focus(); SearchBox.SelectAll(); e.Handled = true; }
        else if (e.SystemKey == Key.Left && Keyboard.Modifiers.HasFlag(ModifierKeys.Alt))
        { if (vm.NavigateBackCommand.CanExecute(null)) vm.NavigateBackCommand.Execute(null); e.Handled = true; }
        else if (e.SystemKey == Key.Right && Keyboard.Modifiers.HasFlag(ModifierKeys.Alt))
        { if (vm.NavigateForwardCommand.CanExecute(null)) vm.NavigateForwardCommand.Execute(null); e.Handled = true; }
        else if (e.Key == Key.V && Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && !SearchBox.IsKeyboardFocusWithin && !AddressEditor.IsKeyboardFocusWithin && e.OriginalSource is not TextBox)
        { vm.PasteFilesCommand.Execute(null); e.Handled = true; }
        else if (e.Key == Key.C && Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && ResultsList.IsKeyboardFocusWithin)
        { vm.CopyFilesCommand.Execute(null); e.Handled = true; }
        else if (e.Key == Key.X && Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && ResultsList.IsKeyboardFocusWithin)
        { vm.CutFilesCommand.Execute(null); e.Handled = true; }
        else if (e.Key == Key.F2 && ResultsList.IsKeyboardFocusWithin)
        { vm.RenameSelectedCommand.Execute(null); e.Handled = true; }
        else if (e.Key == Key.A && Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && ResultsList.IsKeyboardFocusWithin)
        { vm.SelectAllCommand.Execute(null); e.Handled = true; }
        else if (e.Key == Key.Escape && !SearchBox.IsKeyboardFocusWithin && !AddressEditor.IsKeyboardFocusWithin)
        { vm.ClearSelectionCommand.Execute(null); e.Handled = true; }
    }

    private void SearchOptions_Click(object sender, RoutedEventArgs e)
    { SearchScopeButton.ContextMenu.PlacementTarget = SearchScopeButton; SearchScopeButton.ContextMenu.IsOpen = true; }

    private NavigationItem? _navigationContextItem;
    private void NavigationTree_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        _navigationContextItem = GetNavigationItemAt(e.GetPosition(NavigationTree));
        e.Handled = true;
    }
    private void NavigationTree_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        var item = _navigationContextItem ?? NavigationTree.SelectedItem as NavigationItem;
        _navigationContextItem = null;
        NavigationTree.Tag = item;
        if (item is null || item.IsSeparator || item.IsPlaceholder) e.Handled = true;
        if (NavigationTree.ContextMenu is { } menu)
            for (var index = 0; index < menu.Items.Count; index++)
                if (menu.Items[index] is MenuItem action)
                    action.Visibility = (index == 0 ? item?.Search is null && item?.Path is not null : item?.Search is not null)
                        ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ResultsList_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        ResultsList.Tag = null;
        if (e.OriginalSource is DependencyObject source && ItemsControl.ContainerFromElement(ResultsList, source) is ListViewItem row)
        {
            ResultsList.Tag = row.DataContext;
            if (!row.IsSelected) { ResultsList.SelectedItems.Clear(); row.IsSelected = true; }
        }
    }

    private void ResultsList_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not WorkspaceViewModel { OpenWithSingleClick: true } || Keyboard.Modifiers != ModifierKeys.None || e.ClickCount != 1) return;
        if (e.OriginalSource is not DependencyObject source) return;
        if (FindAncestor<System.Windows.Controls.Primitives.ButtonBase>(source) is not null) return;
        if (ItemsControl.ContainerFromElement(ResultsList, source) is ListViewItem { DataContext: ResultItem item }) Activate(item);
    }

    private void ResultsList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is WorkspaceViewModel { OpenWithSingleClick: true }) return;
        if (ResultsList.SelectedItem is ResultItem item)
        {
            if (DataContext is WorkspaceViewModel vm)
            {
                vm.NavigateToCommand.Execute(item);
            }
        }
    }

    private void ResultsList_ColumnHeaderClick(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is GridViewColumnHeader header && header.Column is not null
            && DataContext is WorkspaceViewModel vm)
        {
            if (header.Column.Header is string column) vm.SortBy(column);
        }
    }

    private void Activate(ResultItem item)
    {
        if (DataContext is not WorkspaceViewModel vm)
        {
            return;
        }

        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            vm.ShowInFolderCommand.Execute(item);
        }
        else
        {
            vm.NavigateToCommand.Execute(item);
        }
    }

    private void FocusSelected()
    {
        if (ResultsList.SelectedItem is { } selected) ResultsList.ScrollIntoView(selected);
        ResultsList.UpdateLayout();
        if (ResultsList.ItemContainerGenerator.ContainerFromIndex(ResultsList.SelectedIndex) is System.Windows.Controls.ListViewItem container)
        {
            container.Focus();
        }
    }

    private void OpenCommandMenu_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.ContextMenu is not null)
        {
            btn.ContextMenu.PlacementTarget = btn;
            btn.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            btn.ContextMenu.IsOpen = true;
        }
    }

    private void NavigationTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (DataContext is WorkspaceViewModel vm && e.NewValue is NavigationItem { IsSeparator: false, IsPlaceholder: false } item)
            vm.SelectedNavigationItem = item;
    }

    private void FilterPopup_Opened(object? sender, EventArgs e) => ExtensionFilterBox.Focus();
    private void PopupSurface_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not WorkspaceViewModel vm) return;
        if (e.Key == Key.Escape)
        { vm.FiltersOpen = vm.SaveSearchOpen = false; SearchBox.Focus(); e.Handled = true; }
        else if (e.Key == Key.Enter && sender is Border border)
        {
            if (border.Tag as string == "save") vm.ConfirmSaveSearchCommand.Execute(null);
            else vm.ApplyFiltersCommand.Execute(null);
            if (!vm.FiltersOpen && !vm.SaveSearchOpen) SearchBox.Focus();
            e.Handled = true;
        }
    }

    private void SearchBox_FocusChanged(object sender, KeyboardFocusChangedEventArgs e) => UpdateAdaptiveLayout();
    private void SaveSearchPopup_Opened(object? sender, EventArgs e) { SavedSearchNameBox.Focus(); SavedSearchNameBox.SelectAll(); }
    private void AddressBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (AddressEditor.Visibility == Visibility.Visible) return;
        if (e.OriginalSource is DependencyObject hit && FindAncestor<System.Windows.Controls.Primitives.ButtonBase>(hit) is null && DataContext is WorkspaceViewModel vm)
            vm.OpenPathCommand.Execute(null);
    }

    private static T? FindAncestor<T>(DependencyObject? source) where T : DependencyObject
    {
        for (var current = source; current is not null; current = current is System.Windows.Media.Visual or System.Windows.Media.Media3D.Visual3D
            ? System.Windows.Media.VisualTreeHelper.GetParent(current) : LogicalTreeHelper.GetParent(current))
            if (current is T match) return match;
        return null;
    }

    #region Navigation pane drag-and-drop

    private void NavigationTree_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(null);
        _draggedItem = e.OriginalSource is DependencyObject hit && FindAncestor<System.Windows.Controls.Primitives.ToggleButton>(hit) is not null
            ? null : GetNavigationItemAt(e.GetPosition(NavigationTree));
    }

    private void NavigationTree_MouseMove(object sender, MouseEventArgs e)
    {
        if (_draggedItem is null || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var current = e.GetPosition(null);
        if (Math.Abs(current.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(current.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        var item = _draggedItem;
        _draggedItem = null;
        if (item.IsSeparator || item.IsPlaceholder)
        {
            return;
        }

        DragDrop.DoDragDrop(NavigationTree, item, DragDropEffects.Move);
    }

    private void NavigationTree_DragEnter(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(typeof(NavigationItem)) && e.Data.GetData(typeof(NavigationItem)) is NavigationItem item && !item.IsSeparator)
        {
            e.Effects = DragDropEffects.Move;
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }

        e.Handled = true;
    }

    private void NavigationTree_Drop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (e.Data.GetData(typeof(NavigationItem)) is not NavigationItem dragged || dragged.IsSeparator)
        {
            return;
        }

        if (DataContext is not WorkspaceViewModel vm)
        {
            return;
        }

        var target = GetNavigationItemAt(e.GetPosition(NavigationTree));
        if (target is null || target == dragged || target.IsSeparator)
        {
            return;
        }

        var fromIndex = vm.NavigationItems.IndexOf(dragged);
        var toIndex = vm.NavigationItems.IndexOf(target);
        if (fromIndex >= 0 && toIndex >= 0)
        {
            vm.MoveNavigationItem(fromIndex, toIndex);
        }
    }

    private NavigationItem? GetNavigationItemAt(Point position)
    {
        if (NavigationTree.InputHitTest(position) is not DependencyObject hit)
        {
            return null;
        }

        var container = FindAncestor<TreeViewItem>(hit);
        return container?.DataContext as NavigationItem;
    }

    #endregion
}
