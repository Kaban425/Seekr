using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Seekr.App.ViewModels;

namespace Seekr.App;

public partial class WorkspaceView
{
    private void AttachWorkspaceHandlers()
    {
        DetachWorkspaceHandlers();
        _columnViewModel = DataContext as WorkspaceViewModel;
        if (_columnViewModel is not null)
        {
            _columnViewModel.PropertyChanged += WorkspacePropertyChanged;
            _columnViewModel.AddressEditRequested += AddressEditRequested;
        }
        UpdateSortHeaders();
        UpdateAdaptiveLayout();
    }

    private void DetachWorkspaceHandlers()
    {
        if (_columnViewModel is null) return;
        _columnViewModel.PropertyChanged -= WorkspacePropertyChanged;
        _columnViewModel.AddressEditRequested -= AddressEditRequested;
        _columnViewModel = null;
    }

    private void UpdateSortHeaders()
    {
        foreach (var pair in _columns)
            pair.Value.HeaderStringFormat = _columnViewModel?.SortColumn == pair.Key
                ? (_columnViewModel.IsSortAscending ? "{0} ↑" : "{0} ↓") : "{0}";
    }

    private void AddressEditRequested(object? sender, EventArgs e)
    {
        if (DataContext is not WorkspaceViewModel vm) return;
        if (AddressEditor.Visibility != Visibility.Visible) AddressEditor.Text = vm.CurrentPath ?? string.Empty;
        AddressBreadcrumbs.Visibility = Visibility.Collapsed;
        AddressEditor.Visibility = Visibility.Visible;
        Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
        {
            if (!IsLoaded || AddressEditor.Visibility != Visibility.Visible) return;
            AddressEditor.Focus();
            AddressEditor.SelectAll();
        }));
    }

    private void EndAddressEdit(bool focusResults)
    {
        if (AddressEditor is null) return;
        AddressEditor.Visibility = Visibility.Collapsed;
        AddressBreadcrumbs.Visibility = Visibility.Visible;
        if (focusResults) ResultsList.Focus();
    }

    private void AddressEditor_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { EndAddressEdit(true); e.Handled = true; }
        else if (e.Key == Key.Enter && DataContext is WorkspaceViewModel vm)
        {
            if (vm.TryNavigateAddress(AddressEditor.Text)) EndAddressEdit(true);
            e.Handled = true;
        }
    }

    private void AddressBreadcrumbs_ScrollChanged(object sender, ScrollChangedEventArgs e)
    { if (e.ExtentWidthChange != 0 || e.ViewportWidthChange != 0) AddressBreadcrumbs.ScrollToRightEnd(); }

    private void AddressEditor_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) => EndAddressEdit(false);
}
