using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Seekr.App.ViewModels;

public sealed partial class ShellViewModel : ObservableObject, IDisposable, IAsyncDisposable
{
    private readonly Services.SharedIndexService _indexes = new();
    private readonly Services.FileOperationService _operations = new();
    [ObservableProperty]
    private ObservableCollection<WorkspaceViewModel> _workspaces = new();

    [ObservableProperty]
    private WorkspaceViewModel? _selectedWorkspace;

    [ObservableProperty] private bool _isDualPane;
    [ObservableProperty] private bool _isRightPaneActive;
    [ObservableProperty] private WorkspaceViewModel? _secondaryWorkspace;
    private ClosedWorkspace? _secondarySnapshot;
    public WorkspaceViewModel? ActiveWorkspace => IsDualPane && IsRightPaneActive ? SecondaryWorkspace : SelectedWorkspace;
    public WorkspaceViewModel? OtherWorkspace => IsRightPaneActive ? SelectedWorkspace : SecondaryWorkspace;
    public Services.FileOperationService Operations => _operations;
    public string PaneActivity => IsRightPaneActive ? "Активна правая панель" : "Активна левая панель";
    private void UpdatePaneCommands()
    {
        OnPropertyChanged(nameof(ActiveWorkspace)); OnPropertyChanged(nameof(OtherWorkspace)); OnPropertyChanged(nameof(PaneActivity));
        CopyToOtherPaneCommand.NotifyCanExecuteChanged(); MoveToOtherPaneCommand.NotifyCanExecuteChanged();
    }
    partial void OnSelectedWorkspaceChanged(WorkspaceViewModel? value) { IsRightPaneActive = false; UpdatePaneCommands(); }
    partial void OnIsRightPaneActiveChanged(bool value) => UpdatePaneCommands();
    partial void OnSecondaryWorkspaceChanged(WorkspaceViewModel? value) => UpdatePaneCommands();
    partial void OnIsDualPaneChanged(bool value)
    {
        if (value)
        {
            var workspace = new WorkspaceViewModel(_indexes, _operations, persistViewSettings: _persistViewSettings);
            workspace.CurrentPath = _secondarySnapshot?.Path ?? SelectedWorkspace?.CurrentPath;
            workspace.Query = _secondarySnapshot?.Query ?? string.Empty;
            workspace.SearchAllDisks = _secondarySnapshot?.AllDisks ?? false;
            if (_secondarySnapshot is { } snapshot) RestoreSearch(workspace, snapshot);
            workspace.PropertyChanged += PaneWorkspaceChanged;
            SecondaryWorkspace = workspace;
            if (_initializeWorkspaces) _ = workspace.InitializeAsync();
        }
        else if (SecondaryWorkspace is { } workspace)
        {
            _secondarySnapshot = new(workspace.CurrentPath, workspace.Query, workspace.SearchAllDisks, workspace.SpaceMapOpen, workspace.SearchContents, workspace.SortColumn, workspace.IsSortAscending);
            workspace.PropertyChanged -= PaneWorkspaceChanged; workspace.Dispose(); SecondaryWorkspace = null;
            IsRightPaneActive = false;
        }
        UpdatePaneCommands();
    }
    private void PaneWorkspaceChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    { if (e.PropertyName is nameof(WorkspaceViewModel.SelectedCount) or nameof(WorkspaceViewModel.CurrentPath) or nameof(WorkspaceViewModel.IsOperating)) UpdatePaneCommands(); }
    [RelayCommand] private void ToggleDualPane() => IsDualPane = !IsDualPane;
    [RelayCommand] private void SwitchPane() { if (IsDualPane) IsRightPaneActive = !IsRightPaneActive; }
    private bool CanTransferToPane() => IsDualPane && ActiveWorkspace is { SelectedCount: > 0, IsOperating: false } && OtherWorkspace?.CurrentPath is not null;
    [RelayCommand(CanExecute = nameof(CanTransferToPane))] private void CopyToOtherPane() => TransferToPane(false);
    [RelayCommand(CanExecute = nameof(CanTransferToPane))] private void MoveToOtherPane() => TransferToPane(true);
    private void TransferToPane(bool move)
    {
        if (!CanTransferToPane()) return;
        _operations.Enqueue(ActiveWorkspace!.SelectedPaths, OtherWorkspace!.CurrentPath!, move, Utils.ConflictPrompt.ShowAsync);
    }

    private readonly bool _initializeWorkspaces;
    private readonly bool _persistViewSettings;
    private readonly string? _sessionPath;
    private readonly System.Windows.Threading.DispatcherTimer _sessionTimer = new();
    private string? _lastCheckpoint;
    public bool SessionWasRecovered { get; }
    private readonly List<ClosedWorkspace> _closedTabs = [];
    private sealed record ClosedWorkspace(string? Path, string Query, bool AllDisks, bool SpaceMap, bool Contents, string Sort, bool Ascending);

    public ShellViewModel(bool initializeWorkspaces = true, bool persistViewSettings = true, string? sessionPath = null, TimeSpan? checkpointInterval = null)
    {
        _initializeWorkspaces = initializeWorkspaces;
        _persistViewSettings = persistViewSettings;
        _sessionPath = sessionPath;
        var checkpointEnabled = persistViewSettings && (initializeWorkspaces || sessionPath is not null);
        var session = checkpointEnabled ? Services.SessionStore.Load(sessionPath) : null;
        SessionWasRecovered = session is { CleanShutdown: false };
        if (session is null) AddWorkspace();
        else
        {
            foreach (var tab in session.Tabs)
            {
                AddWorkspace(new(tab.Path, tab.Query ?? "", tab.AllDisks, tab.SpaceMap, false, tab.Sort ?? "Имя", tab.Ascending));
                SelectedWorkspace!.SessionScrollOffset = double.IsFinite(tab.Scroll) ? Math.Max(0, tab.Scroll) : 0;
            }
            SelectedWorkspace = Workspaces[Math.Clamp(session.Selected, 0, Workspaces.Count - 1)];
            if (session.Right is { } right)
                _secondarySnapshot = new(right.Path, right.Query ?? "", right.AllDisks, right.SpaceMap, false, right.Sort ?? "Имя", right.Ascending);
            IsDualPane = session.DualPane;
            if (SecondaryWorkspace is { } secondary && session.Right is { } restored)
                secondary.SessionScrollOffset = double.IsFinite(restored.Scroll) ? Math.Max(0, restored.Scroll) : 0;
            IsRightPaneActive = session.RightActive && IsDualPane;
        }
        if (checkpointEnabled)
        {
            _sessionTimer.Interval = checkpointInterval ?? TimeSpan.FromSeconds(2);
            _sessionTimer.Tick += SaveCheckpoint;
            _sessionTimer.Start();
            SaveCheckpoint(this, EventArgs.Empty);
        }
    }

    private Services.ShellSession CaptureSession(bool cleanShutdown)
        => new(Workspaces.Take(40).Select(Snapshot).ToArray(), Math.Max(0, Workspaces.IndexOf(SelectedWorkspace!)),
            SecondaryWorkspace is { } right ? Snapshot(right) : null, IsDualPane, IsRightPaneActive, cleanShutdown);

    private void SaveCheckpoint(object? sender, EventArgs e)
    {
        if (_disposed || Workspaces.Count == 0) return;
        var snapshot = CaptureSession(false);
        var serialized = System.Text.Json.JsonSerializer.Serialize(snapshot);
        if (serialized == _lastCheckpoint) return;
        if (Services.SessionStore.Save(snapshot, _sessionPath)) _lastCheckpoint = serialized;
    }

    private static Services.WorkspaceSession Snapshot(WorkspaceViewModel w)
        => new(w.CurrentPath, w.Query, w.SearchAllDisks, w.SearchContents, w.SpaceMapOpen, w.SortColumn, w.IsSortAscending, w.SessionScrollOffset);

    [RelayCommand] private void NextTab() => CycleTab(1);
    [RelayCommand] private void PreviousTab() => CycleTab(-1);
    private void CycleTab(int delta)
    {
        if (Workspaces.Count == 0) return;
        var index = SelectedWorkspace is null ? 0 : Workspaces.IndexOf(SelectedWorkspace);
        SelectedWorkspace = Workspaces[(index + delta + Workspaces.Count) % Workspaces.Count];
    }

    private bool CanReopenClosedTab() => _closedTabs.Count > 0;
    [RelayCommand(CanExecute = nameof(CanReopenClosedTab))]
    private void ReopenClosedTab()
    {
        if (_closedTabs.Count == 0) return;
        var closed = _closedTabs[^1];
        _closedTabs.RemoveAt(_closedTabs.Count - 1);
        AddWorkspace(closed);
        ReopenClosedTabCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void NewTab()
    {
        AddWorkspace();
    }

    [RelayCommand]
    private void CloseTab(WorkspaceViewModel? workspace)
    {
        if (workspace is null || !Workspaces.Contains(workspace))
        {
            return;
        }

        var index = Workspaces.IndexOf(workspace);
        var wasSelected = ReferenceEquals(SelectedWorkspace, workspace);
        _closedTabs.Add(new(workspace.CurrentPath, workspace.Query, workspace.SearchAllDisks, workspace.SpaceMapOpen, workspace.SearchContents, workspace.SortColumn, workspace.IsSortAscending));
        if (_closedTabs.Count > 10) _closedTabs.RemoveAt(0);
        ReopenClosedTabCommand.NotifyCanExecuteChanged();
        workspace.PropertyChanged -= PaneWorkspaceChanged;
        workspace.Dispose();
        _ = Workspaces.Remove(workspace);

        if (wasSelected || SelectedWorkspace is null || !Workspaces.Contains(SelectedWorkspace))
        {
            SelectedWorkspace = Workspaces.Count > 0 ? Workspaces[Math.Min(index, Workspaces.Count - 1)] : null;
        }

        if (Workspaces.Count == 0)
        {
            AddWorkspace();
        }
    }

    [RelayCommand]
    private void CloseCurrentTab()
    {
        if (IsDualPane && IsRightPaneActive) { IsDualPane = false; return; }
        if (SelectedWorkspace is not null)
        {
            CloseTab(SelectedWorkspace);
        }
    }

    [RelayCommand]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822", Justification = "Command must be instance-bound.")]
    private void OpenSettings()
    {
        SettingsWindow.OpenFor(Application.Current.MainWindow);
    }

    private void AddWorkspace(ClosedWorkspace? closed = null)
    {
        var workspace = new WorkspaceViewModel(_indexes, _operations, persistViewSettings: _persistViewSettings);
        if (closed is not null)
        {
            workspace.CurrentPath = closed.Path;
            workspace.Query = closed.Query;
            workspace.SearchAllDisks = closed.AllDisks;
            workspace.SpaceMapOpen = closed.SpaceMap;
            RestoreSearch(workspace, closed);
        }
        workspace.PropertyChanged += PaneWorkspaceChanged;
        Workspaces.Add(workspace);
        SelectedWorkspace = workspace;
        if (_initializeWorkspaces) _ = workspace.InitializeAsync();
    }

    private static void RestoreSearch(WorkspaceViewModel workspace, ClosedWorkspace snapshot)
    {
        workspace.SearchContents = snapshot.Contents;
        if (!string.IsNullOrEmpty(snapshot.Sort)) workspace.SortBy(snapshot.Sort);
        workspace.SetSortDirectionCommand.Execute(snapshot.Ascending ? "ascending" : "descending");
        workspace.SpaceMapOpen = snapshot.SpaceMap;
    }

    private bool _disposed;
    private Task _shutdown = Task.CompletedTask;
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _sessionTimer.Stop(); _sessionTimer.Tick -= SaveCheckpoint;
        if (_persistViewSettings && (_initializeWorkspaces || _sessionPath is not null))
            Services.SessionStore.Save(CaptureSession(true), _sessionPath);
        foreach (var workspace in Workspaces)
        {
            workspace.PropertyChanged -= PaneWorkspaceChanged;
            workspace.Dispose();
        }

        if (SecondaryWorkspace is { } secondary) { secondary.PropertyChanged -= PaneWorkspaceChanged; secondary.Dispose(); }
        Workspaces.Clear();
        _shutdown = Task.WhenAll(_indexes.DisposeAsync().AsTask(), _operations.DisposeAsync().AsTask());
    }
    public async ValueTask DisposeAsync()
    {
        Dispose();
        await _shutdown.ConfigureAwait(false);
    }

}

