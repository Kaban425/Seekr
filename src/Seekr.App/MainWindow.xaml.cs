using System.Windows;
using System.Windows.Input;
using Seekr.App.Services;
using Seekr.App.ViewModels;
using Wpf.Ui.Controls;

namespace Seekr.App;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001", Justification = "The shell view model is disposed when the window closes.")]
public partial class MainWindow : FluentWindow
{
    private readonly ShellViewModel _viewModel;
    private readonly OperationNoticeViewModel _operationNotice;

    public MainWindow() : this(new ShellViewModel()) { }

    public MainWindow(ShellViewModel viewModel)
    {
        _viewModel = viewModel;
        InitializeComponent();
        InitializeTools();
        var workArea = SystemParameters.WorkArea;
        Width = Math.Min(Width, Math.Max(MinWidth, workArea.Width - 24));
        Height = Math.Min(Height, Math.Max(MinHeight, workArea.Height - 24));
        DataContext = _viewModel;
        _operationNotice = new OperationNoticeViewModel(_viewModel.Operations);
        OperationPanel.DataContext = _operationNotice;
        PreviewKeyDown += FluentWindow_PreviewKeyDown;
        Closing += MainWindow_Closing;
        Closed += (_, _) => { _operationNotice.Dispose(); _viewModel.Dispose(); };
    }

    private bool _closing;
    private bool _shutdownFinished;
    private async void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_shutdownFinished) return;
        e.Cancel = true;
        if (_closing) return;
        _closing = true;
        IsEnabled = false;
        Title = "Seekr · завершение операций…";
        try { await _viewModel.DisposeAsync(); }
#pragma warning disable CA1031 // Closing must still complete if a background worker failed.
        catch (Exception ex) { System.Diagnostics.Trace.TraceError("Shutdown failed: {0}", ex.GetType().Name); }
#pragma warning restore CA1031
        finally { _shutdownFinished = true; _ = Dispatcher.BeginInvoke(new Action(Close)); }
    }

    private void Pane_Activated(object sender, RoutedEventArgs e)
    { if (sender is System.Windows.Controls.Border { Tag: string side }) _viewModel.IsRightPaneActive = side == "right"; }

    private void FluentWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_viewModel.ActiveWorkspace is { } workspace)
        {
            if (e.Key == Key.K && Keyboard.Modifiers == ModifierKeys.Control)
            { ShowCommandPalette(); e.Handled = true; return; }
            if (e.OriginalSource is not System.Windows.Controls.Primitives.TextBoxBase && e.OriginalSource is not System.Windows.Controls.PasswordBox)
            {
                if (e.Key == Key.Z && Keyboard.Modifiers == ModifierKeys.Control)
                { workspace.UndoLastCommand.Execute(null); e.Handled = true; return; }
                if (e.Key == Key.V && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
                { workspace.ShowClipboardHistoryCommand.Execute(null); e.Handled = true; return; }
            }
            if (e.Key == Key.F1) { workspace.ShowHelpCommand.Execute(null); e.Handled = true; return; }
            if (e.Key == Key.F5) { workspace.RefreshCommand.Execute(null); e.Handled = true; return; }
            if (e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control && workspace.SaveSearchCommand.CanExecute(null))
            { workspace.SaveSearchCommand.Execute(null); e.Handled = true; return; }
        }
        if (Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && e.Key == Key.D)
        { _viewModel.ToggleDualPaneCommand.Execute(null); e.Handled = true; }
        else if (Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && e.Key is Key.C or Key.X && _viewModel.IsDualPane)
        { var command = e.Key == Key.C ? _viewModel.CopyToOtherPaneCommand : _viewModel.MoveToOtherPaneCommand; if (command.CanExecute(null)) command.Execute(null); e.Handled = true; }
        else if (e.Key == Key.F6 && _viewModel.IsDualPane)
        { _viewModel.SwitchPaneCommand.Execute(null); (_viewModel.IsRightPaneActive ? RightPaneContent : LeftPaneContent).MoveFocus(new TraversalRequest(FocusNavigationDirection.First)); e.Handled = true; }
        else if (e.Key == Key.Tab && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) _viewModel.PreviousTabCommand.Execute(null);
            else _viewModel.NextTabCommand.Execute(null);
            LeftPaneContent.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
            e.Handled = true;
        }
    }

    private void FluentWindow_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.T && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) _viewModel.ReopenClosedTabCommand.Execute(null);
            else _viewModel.NewTabCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.W && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            _viewModel.CloseCurrentTabCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void TabList_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Middle || e.OriginalSource is not DependencyObject source) return;
        if (System.Windows.Controls.ItemsControl.ContainerFromElement(TabList, source) is System.Windows.Controls.ListBoxItem { DataContext: WorkspaceViewModel workspace })
        {
            _viewModel.CloseTabCommand.Execute(workspace);
            e.Handled = true;
        }
    }

    private void TabList_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        var viewer = FindScrollViewer(TabList);
        if (viewer is null) return;
        viewer.ScrollToHorizontalOffset(viewer.HorizontalOffset - e.Delta);
        e.Handled = true;
    }

    private static System.Windows.Controls.ScrollViewer? FindScrollViewer(DependencyObject parent)
    {
        for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
            if (child is System.Windows.Controls.ScrollViewer viewer) return viewer;
            if (FindScrollViewer(child) is { } nested) return nested;
        }
        return null;
    }

    private void FluentWindow_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            return;
        }

        var settings = SettingsService.Load();
        var delta = e.Delta > 0 ? 0.05 : -0.05;
        settings.UiScale = Math.Clamp(settings.UiScale + delta, 0.75, 1.5);
        SettingsService.Save(settings);
        ScaleService.ApplyCurrentScale();
        e.Handled = true;
    }
}
