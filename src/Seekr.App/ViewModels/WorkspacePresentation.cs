using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Seekr.App.Services;

namespace Seekr.App.ViewModels;

public sealed record SearchFilterChip(string Label, string Token);

public sealed partial class WorkspaceViewModel
{
    [ObservableProperty] private bool _isSearching;
    [ObservableProperty] private bool _showSearchProgress;
    partial void OnIsSearchingChanged(bool value) { if (!value) ShowSearchProgress = false; UpdateEmptyPresentation(); }
    private async Task ShowDelayedProgressAsync(CancellationTokenSource source, CancellationToken token)
    {
        try
        {
            await Task.Delay(180, token);
            if (!_disposed && IsSearching && ReferenceEquals(_pendingSearch, source)) ShowSearchProgress = true;
        }
        catch (OperationCanceledException) { }
    }
    partial void OnErrorMessageChanged(string? value) => UpdateEmptyPresentation();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasErrorMessage))]
    private string? _errorMessage;
    public bool HasErrorMessage => !string.IsNullOrWhiteSpace(ErrorMessage);
    private void ReportError(string message) { ErrorMessage = message; Status = message; }
    [RelayCommand] private void CancelSearch() { _pendingSearch?.Cancel(); Status = "Поиск отменён."; }
    [RelayCommand] private void ClearError() => ErrorMessage = null;

    public string SortColumn => _sortColumn;
    public bool IsSortAscending => _sortAscending;
    public bool IsSortDescending => !_sortAscending;
    public string SortDescription => string.IsNullOrEmpty(_sortColumn) ? "Порядок по умолчанию" : $"{_sortColumn} · по {(_sortAscending ? "возрастанию" : "убыванию")}";

    [RelayCommand]
    private void SetSortDirection(string? direction)
    {
        if (direction is not ("ascending" or "descending")) return;
        if (string.IsNullOrEmpty(_sortColumn)) _sortColumn = "Имя";
        _sortAscending = direction == "ascending";
        RefreshSort();
    }

    private void RefreshSort()
    {
        OnPropertyChanged(nameof(SortColumn));
        OnPropertyChanged(nameof(IsSortAscending));
        OnPropertyChanged(nameof(IsSortDescending));
        OnPropertyChanged(nameof(SortDescription));
        if (HasQuery) _ = RunSearchAsync(Query);
        else { ApplyCurrentSort(); UpdateSizeMap(); _ = MeasureResultsAsync(Results, _lifetime.Token); }
    }

    [ObservableProperty] private bool _searchContents;
    public bool SearchNames => !SearchContents;
    public string SearchModeLabel => SearchContents ? "Содержимое" : "Имена";
    partial void OnSearchContentsChanged(bool value)
    {
        OnPropertyChanged(nameof(SearchNames)); OnPropertyChanged(nameof(SearchModeLabel));
        UpdateSearchPresentation(); SaveSearchCommand.NotifyCanExecuteChanged(); _ = RunSearchAsync(Query);
    }
    [RelayCommand] private void SetSearchMode(string? mode) => SearchContents = mode == "content";

    private bool _loadingAppearanceSettings;
    private bool _resultsSearchAllDisks;
    private bool _resultsSearchContents;
    [ObservableProperty] private bool _searchAllDisks;
    [ObservableProperty] private bool _alwaysShowCheckboxes;
    [ObservableProperty] private bool _selectionModeActive;
    [ObservableProperty] private bool _showSizeBars;
    [ObservableProperty] private IReadOnlyList<SearchFilterChip> _activeFilters = [];

    public bool IsHome => CurrentPath is null && !HasQuery;
    public bool PersistViewSettings => _persistViewSettings;
    public bool HasQuery => !string.IsNullOrWhiteSpace(Query);
    public bool HasSelection => SelectedCount > 0;
    public bool ShowResultPaths => HasQuery;
    public bool ShowCheckboxes => AlwaysShowCheckboxes || SelectionModeActive || HasSelection;
    public bool IsTableView => !SpaceMapOpen;
    public string SearchScopeLabel => (SearchContents ? "Текст · " : "") + (SearchAllDisks || string.IsNullOrEmpty(CurrentPath) ? "Везде" : "В папке");
    public string SearchPlaceholder => SearchAllDisks || string.IsNullOrEmpty(CurrentPath)
        ? (SearchContents ? "Текст внутри файлов · все диски" : "Поиск по всем дискам") : (SearchContents ? "Текст внутри файлов: " : "Поиск: ") + (Path.GetFileName(CurrentPath.TrimEnd('\\')) is { Length: > 0 } name ? name : CurrentPath);
    public bool HasOperationNotice => _operations.Jobs.Any(x => !x.IsFinished || x.CanRetry);
    public string OperationsLabel => _operations.Jobs.Count(x => !x.IsFinished) is > 0 and var count ? $"Операции · {count}" : "Операции";

    private void UpdateSearchPresentation()
    {
        if (!HasQuery) SaveSearchOpen = false;
        UpdateEmptyPresentation();
        OnPropertyChanged(nameof(IsHome));
        OnPropertyChanged(nameof(HasQuery));
        OnPropertyChanged(nameof(ShowResultPaths));
        OnPropertyChanged(nameof(SearchScopeLabel));
        OnPropertyChanged(nameof(SearchPlaceholder));
        ActiveFilters = Query.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(token =>
        {
            var parts = token.Split(':', 2);
            if (parts.Length != 2) return null;
            var label = parts[0].ToLowerInvariant() switch { "kind"=>"Объекты", "ext" => "Тип", "size" => "Размер", "dm" => "Изменён", "dc" => "Создан", _ => null };
            var value = parts[1].ToLowerInvariant() switch { "file" or "files"=>"файлы", "folder" or "folders"=>"папки", "today" => "сегодня", "yesterday" => "вчера", "thisweek" => "эта неделя", "lastweek" => "прошлая неделя", "thismonth" => "этот месяц", _ => parts[1] };
            return label is null ? null : new SearchFilterChip(label + ": " + value, token);
        }).OfType<SearchFilterChip>().ToArray();
    }

    partial void OnSearchAllDisksChanged(bool value)
    {
        UpdateSearchPresentation();
        _ = RunSearchAsync(Query);
    }
    partial void OnAlwaysShowCheckboxesChanged(bool value) { OnPropertyChanged(nameof(ShowCheckboxes)); SaveViewPreferences(); }
    partial void OnSelectionModeActiveChanged(bool value) => OnPropertyChanged(nameof(ShowCheckboxes));
    partial void OnShowSizeBarsChanged(bool value) => SaveViewPreferences();
    partial void OnShowPreviewPaneChanged(bool value) => SaveViewPreferences();
    partial void OnCompactModeChanged(bool value) => SaveViewPreferences();
    partial void OnSelectedCountChanged(int value) { OnPropertyChanged(nameof(HasSelection)); OnPropertyChanged(nameof(ShowCheckboxes)); }

    private void SaveViewPreferences()
    {
        if (_loadingAppearanceSettings || _disposed || !_persistViewSettings) return;
        var settings = SettingsService.Load();
        settings.AlwaysShowCheckboxes = AlwaysShowCheckboxes;
        settings.ShowSizeBars = ShowSizeBars;
        settings.ShowPreviewPane = ShowPreviewPane;
        settings.CompactMode = CompactMode;
        SettingsService.Save(settings, notify: false);
    }

    [RelayCommand] private void TogglePreview() => ShowPreviewPane = !ShowPreviewPane;
    [RelayCommand] private void SetSearchScope(string? scope) => SearchAllDisks = scope == "all";
    [RelayCommand] private void RemoveFilter(SearchFilterChip? chip)
    {
        if (chip is not null) Query = string.Join(' ', Query.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(t => !t.Equals(chip.Token, StringComparison.OrdinalIgnoreCase)));
    }
    [RelayCommand] private void SetView(string? view)
    {
        SpaceMapOpen = view == "map";
        OnPropertyChanged(nameof(IsTableView));
        OnPropertyChanged(nameof(SpaceMapOpen));
    }
    [RelayCommand] private void SortResults(string? column) { if (column is not null) SortBy(column); }

    private void SubscribeOperationActivity()
    {
        _operations.Jobs.CollectionChanged += JobsChanged;
        foreach (var job in _operations.Jobs) job.PropertyChanged += JobChanged;
    }
    private void JobsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null) foreach (FileOperationJob job in e.OldItems) job.PropertyChanged -= JobChanged;
        if (e.NewItems is not null) foreach (FileOperationJob job in e.NewItems) job.PropertyChanged += JobChanged;
        UpdateOperationActivity();
    }
    private void JobChanged(object? sender, PropertyChangedEventArgs e)
    { if (e.PropertyName is nameof(FileOperationJob.IsFinished) or nameof(FileOperationJob.CanRetry)) UpdateOperationActivity(); }
    private void UpdateOperationActivity() { OnPropertyChanged(nameof(HasOperationNotice)); OnPropertyChanged(nameof(OperationsLabel)); }
    private void UnsubscribeOperationActivity()
    {
        _operations.Jobs.CollectionChanged -= JobsChanged;
        foreach (var job in _operations.Jobs) job.PropertyChanged -= JobChanged;
    }
}
