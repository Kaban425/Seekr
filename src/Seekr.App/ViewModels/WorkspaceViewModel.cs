using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Seekr.App.Utils;
using Seekr.Core.Index;
using Seekr.Core.Search;
using Seekr.Indexer;

namespace Seekr.App.ViewModels;

public sealed partial class WorkspaceViewModel : ObservableObject, IDisposable
{
    private const int DefaultResultLimit = 500;
    private static readonly TimeSpan TypingDelay = TimeSpan.FromMilliseconds(60);

    private string? _resultsQuery;
    private string? _resultsPath;
    private readonly Services.SharedIndexService _indexes;
    private NameIndex? _liveScopeIndex;
    private string? _liveScopePath;
    private NameIndex? _index => _indexes.Index;
    private string _indexStatus = "Индексация…";
    private CancellationTokenSource? _pendingSearch;
    private readonly CancellationTokenSource _lifetime = new();
    private bool _disposed;
    private bool _initialized;
    private int _resultLimit = DefaultResultLimit;
    private bool _autoRefreshIndex = true;
    private int _autoRefreshIntervalSeconds = 10;
    private bool _showFolderSizes = true;
    private readonly bool _persistViewSettings;
    private string _navigationDriveSignature = string.Empty;

    [ObservableProperty]
    private string _query = string.Empty;

    [ObservableProperty]
    private IReadOnlyList<ResultItem> _results = [];

    [ObservableProperty]
    private string _status = "Индексация…";

    [ObservableProperty]
    private string? _notice;

    [ObservableProperty]
    private bool _isIndexing = true;

    [ObservableProperty]
    private ObservableCollection<NavigationItem> _navigationItems = new();

    [ObservableProperty]
    private NavigationItem? _selectedNavigationItem;

    [ObservableProperty]
    private string? _currentPath;

    [ObservableProperty]
    private IReadOnlyList<BreadcrumbItem> _breadcrumb = [new("Этот компьютер", 0)];

    [ObservableProperty]
    private string _title = "Этот компьютер";

    [ObservableProperty]
    private ImageSource? _icon;

    public WorkspaceViewModel(Services.SharedIndexService? indexes = null, Services.FileOperationService? operations = null, bool persistViewSettings = true)
    {
        _persistViewSettings = persistViewSettings;
        _operations = operations ?? Services.FileOperationService.Default;
        _operations.Completed += OperationsCompleted;
        _indexes = indexes ?? Services.SharedIndexService.Default;
        _indexes.Changed += IndexChanged;
        SubscribeOperationActivity();
        LoadSettings();
        BuildNavigation();
        UpdateIcon();
        Services.SettingsService.Changed += SettingsChanged;
    }

    private void LoadSettings()
    {
        _loadingAppearanceSettings = true;
        try
        {
        var settings = Services.SettingsService.Load();
        _resultLimit = settings.SearchResultLimit > 0 ? settings.SearchResultLimit : DefaultResultLimit;
        _autoRefreshIndex = settings.AutoRefreshIndex;
        _autoRefreshIntervalSeconds = settings.AutoRefreshIntervalSeconds > 0 ? settings.AutoRefreshIntervalSeconds : 10;
        _showFolderSizes = settings.ShowFolderSizes;
        CompactMode = settings.CompactMode;
        ShowPreviewPane = settings.ShowPreviewPane;
        OpenWithSingleClick = settings.OpenWithSingleClick;
        _showHiddenFiles = settings.ShowHiddenFiles;
        _showSystemFiles = settings.ShowSystemFiles;
        AlwaysShowCheckboxes = settings.AlwaysShowCheckboxes;
        ShowSizeBars = settings.ShowSizeBars;
        }
        finally { _loadingAppearanceSettings = false; }
    }

    partial void OnSelectedNavigationItemChanged(NavigationItem? value)
    {
        if (value is null || value.IsSeparator || value.IsPlaceholder)
        {
            return;
        }

        if (_syncingSidebar) return;
        if (value.Search is { } saved)
        {
            SearchAllDisks = saved.Path is null; SearchContents = false;
            _sortColumn = saved.SortColumn; _sortAscending = saved.SortAscending;
            OnPropertyChanged(nameof(SortColumn)); OnPropertyChanged(nameof(IsSortAscending)); OnPropertyChanged(nameof(IsSortDescending)); OnPropertyChanged(nameof(SortDescription));
        }
        Query = value.Search?.Query ?? string.Empty;
        CurrentPath = value.Path;
        _ = RunSearchAsync(Query);
    }

    partial void OnCurrentPathChanging(string? value)
    {
        if (!_restoringHistory) { _back.Push(CurrentPath); _forward.Clear(); }
    }

    partial void OnCurrentPathChanged(string? value)
    {
        _liveScopeIndex = null; _liveScopePath = null; _scopeChanged = true; _sizeCache.Clear();
        NavigateBackCommand.NotifyCanExecuteChanged();
        NavigateForwardCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(SearchScope));
        UpdateSearchPresentation();
        WatchFolder(value);
        UpdateBreadcrumb();
        UpdateSidebarSelection(value);
        UpdateTitle();
        _ = RunSearchAsync(Query);
    }

    partial void OnQueryChanged(string value)
    {
        OnPropertyChanged(nameof(QuerySuggestions));
        UpdateSearchPresentation();
        UpdateTitle();
        UpdateSidebarSelection(CurrentPath);
        SaveSearchCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(EmptyStateTitle));
        OnPropertyChanged(nameof(EmptyStateHint));
        _ = RunSearchAsync(value);
    }

    private void UpdateTitle()
    {
        var location = string.IsNullOrEmpty(CurrentPath) ? "Этот компьютер" : System.IO.Path.GetFileName(CurrentPath.TrimEnd('\\'));
        if (string.IsNullOrEmpty(location)) location = CurrentPath ?? "Этот компьютер";
        Title = string.IsNullOrEmpty(Query) ? location : $"Поиск: {Query}";
        UpdateIcon();
    }

    private void UpdateIcon()
    {
        Icon = !string.IsNullOrWhiteSpace(Query) ? Icons.IconArtwork.Image(Icons.IconKind.Search)
            : string.IsNullOrEmpty(CurrentPath) ? Icons.IconArtwork.Image(Icons.IconKind.Home)
            : !string.IsNullOrEmpty(CurrentPath) && string.Equals(System.IO.Path.GetPathRoot(CurrentPath)?.TrimEnd('\\'), CurrentPath.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)
            ? FilePresentation.GetPathIcon(CurrentPath) : FilePresentation.GetIcon(true, string.Empty);
    }

    private void UpdateBreadcrumb()
    {
        if (string.IsNullOrEmpty(CurrentPath))
        {
            Breadcrumb = [new BreadcrumbItem("Этот компьютер", 0)];
            return;
        }

        var parts = CurrentPath.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);
        var list = new List<BreadcrumbItem>(parts.Length + 1) { new("Этот компьютер", 0) };
        for (var i = 0; i < parts.Length; i++)
        {
            list.Add(new BreadcrumbItem(parts[i], i + 1));
        }

        Breadcrumb = list;
    }

    private void UpdateSidebarSelection(string? path)
    {
        _syncingSidebar = true;
        try
        {
            SelectedNavigationItem = FlattenNavigation(NavigationItems).FirstOrDefault(n => !n.IsSeparator && !n.IsPlaceholder
                && string.Equals(n.Path, path, StringComparison.OrdinalIgnoreCase)
                && (n.Search is null ? string.IsNullOrEmpty(Query) : n.Search.Query == Query));
            foreach (var node in FlattenNavigation(NavigationItems)) node.IsSelected = ReferenceEquals(node, SelectedNavigationItem);
        }
        finally { _syncingSidebar = false; }
    }

    private void BuildNavigation()
    {
        var items = new ObservableCollection<NavigationItem>();
        items.Add(new NavigationItem("Главная", path: null, symbol: Icons.IconKind.Home));

        items.Add(CreateSeparator("Быстрый доступ"));
        AddKnownFolder(items, Environment.SpecialFolder.Desktop, "Рабочий стол", Icons.IconKind.Desktop);
        AddKnownFolder(items, Environment.SpecialFolder.MyPictures, "Изображения", Icons.IconKind.Picture);
        AddKnownFolder(items, Environment.SpecialFolder.MyDocuments, "Документы", Icons.IconKind.Document);
        AddKnownFolder(items, Environment.SpecialFolder.UserProfile, "Загрузки", Icons.IconKind.Download, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"));
        AddKnownFolder(items, Environment.SpecialFolder.MyMusic, "Музыка", Icons.IconKind.Music);
        AddKnownFolder(items, Environment.SpecialFolder.MyVideos, "Видео", Icons.IconKind.Video);
        AddKnownFolder(items, Environment.SpecialFolder.UserProfile, "Профиль", Icons.IconKind.Person);

        var navigationSettings = Services.SettingsService.Load();
        foreach (var pinned in navigationSettings.PinnedFolders.Where(p => !items.Any(n => n.Path == p)))
            items.Add(new NavigationItem(Path.GetFileName(pinned.TrimEnd('\\')), pinned, FilePresentation.GetPathIcon(pinned)));
        items.Add(CreateSeparator("Диски"));
        foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady))
        {
            var root = drive.RootDirectory.FullName;
            var label = string.IsNullOrEmpty(drive.VolumeLabel)
                ? $"Локальный диск ({drive.Name.TrimEnd('\\')})"
                : $"{drive.VolumeLabel} ({drive.Name.TrimEnd('\\')})";
            items.Add(new NavigationItem(label, root, FilePresentation.GetPathIcon(root)));
        }

        foreach (var node in items.Where(n => n.Path is not null && navigationSettings.HiddenNavigationPaths.Contains(n.Path, StringComparer.OrdinalIgnoreCase)).ToArray()) { items.Remove(node); node.Dispose(); }
        _navigationDriveSignature = GetDriveSignature();
        var searches = Services.SettingsService.Load().SavedSearches;
        if (searches.Count > 0) items.Add(CreateSeparator("Сохранённые поиски"));
        foreach (var search in searches)
            items.Add(new NavigationItem(search.Name, search.Path, symbol: Icons.IconKind.Search, canExpand: false) { Search = search });
        foreach (var item in NavigationItems) item.Dispose();
        NavigationItems = items;
    }

    private string GetDriveSignature()
    {
        try { return string.Join('|', DriveInfo.GetDrives().Where(d => d.IsReady).Select(d => d.Name)); }
        catch (IOException) { return _navigationDriveSignature; }
    }

    private static NavigationItem CreateSeparator(string label) =>
        new(label) { IsSeparator = true };

    private static void AddKnownFolder(ObservableCollection<NavigationItem> items, Environment.SpecialFolder kind, string fallbackLabel, Icons.IconKind symbol, string? explicitPath = null)
    {
        var path = explicitPath ?? Environment.GetFolderPath(kind);
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        items.Add(new NavigationItem(fallbackLabel, path, Icons.IconArtwork.Image(symbol), symbol));
    }

    private static IEnumerable<NavigationItem> FlattenNavigation(IEnumerable<NavigationItem> items)
    {
        foreach (var item in items)
        {
            yield return item;
            foreach (var child in FlattenNavigation(item.Children)) yield return child;
        }
    }

    public async Task InitializeAsync()
    {
        if (_initialized || _disposed) return;
        _initialized = true;
        _ = RunSearchAsync(Query);
        await _indexes.InitializeAsync();
    }

    private void IndexChanged(object? sender, EventArgs e)
    {
        if (_disposed) return;
        IsIndexing = _indexes.IsBusy;
        _indexStatus = _indexes.Status;
        if (string.IsNullOrEmpty(CurrentPath) && string.IsNullOrWhiteSpace(Query)) Status = _indexStatus;
        if (!_indexes.IsBusy)
        {
            _liveScopeIndex = null; _liveScopePath = null; _sizeCache.Clear();
            if (_navigationDriveSignature != GetDriveSignature())
            { BuildNavigation(); UpdateSidebarSelection(CurrentPath); }
            if (!string.IsNullOrWhiteSpace(Query) || string.IsNullOrEmpty(CurrentPath)) _ = RunSearchAsync(Query);
        }
    }

    private async Task RunSearchAsync(string text)
    {
        if (_disposed)
        {
            return;
        }

        ErrorMessage = null;
        _pendingSearch?.Cancel();
        _sizesCancellation?.Cancel();
        var sameContext = _resultsSearchContents == SearchContents && _resultsQuery == text && _resultsSearchAllDisks == SearchAllDisks && string.Equals(_resultsPath, CurrentPath, StringComparison.OrdinalIgnoreCase);
        var path = CurrentPath;
        var searchPath = SearchAllDisks ? null : path;
        var cts = new CancellationTokenSource();
        _pendingSearch = cts;
        IsSearching = true;
        var token = cts.Token;
        using var progressCancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        _ = ShowDelayedProgressAsync(cts, progressCancellation.Token);

        try
        {
            await Task.Delay(TypingDelay, token).ConfigureAwait(true);
            var query = SearchQuery.Parse(text);
            if (query.Error is not null) { Results = []; ReportError(query.Error); return; }
            var order = _sortColumn switch { "Имя" => SearchOrder.Name, "Размер" => SearchOrder.Size,
                "Дата изменения" => SearchOrder.Modified, "Дата создания" => SearchOrder.Created, "Тип" => SearchOrder.Type, _ => SearchOrder.Relevance };
            var request = new Services.WorkspaceSearchRequest(query, path, searchPath, !_scopeChanged && searchPath == _liveScopePath ? _liveScopeIndex ?? _index : _index, _resultLimit, order,
                !_sortAscending, _showHiddenFiles, _showSystemFiles, _scopeChanged, SearchContents);
            var progress = new Progress<Services.ContentScanProgress>(value =>
            {
                if (!_disposed && !token.IsCancellationRequested && ReferenceEquals(_pendingSearch, cts))
                    Status = $"Содержимое: проверено {value.Inspected}, пропущено {value.Skipped}…";
            });
            var listing = await Services.WorkspaceSearchService.ExecuteAsync(request, token, progress);
            token.ThrowIfCancellationRequested();
            if (listing.Snapshot is not null) { _liveScopeIndex = listing.Snapshot; _liveScopePath = searchPath; _scopeChanged = false; }
            var found = listing.Entries;
            var browsingError = listing.Error;

            token.ThrowIfCancellationRequested();

            var selectedPaths = sameContext ? Results.Where(x => x.IsSelected).Select(x => x.FullPath).ToHashSet(StringComparer.OrdinalIgnoreCase) : [];
            _resultsQuery = text;
            _resultsPath = path;
            _resultsSearchAllDisks = SearchAllDisks;
            _resultsSearchContents = SearchContents;
            Results = [.. found.Select(r => new ResultItem(r, _showFolderSizes, listing.DriveSpaces?.GetValueOrDefault(r.FullPath)) { IsSelected = selectedPaths.Contains(r.FullPath) })];
            ApplyCurrentSort();
            Status = query.IsEmpty && string.IsNullOrEmpty(CurrentPath)
                ? _indexStatus
                : $"{FormatCount(found.Count)} · {LocationLabel()}";
            if (browsingError is not null) ReportError(browsingError);
            if (listing.Notice is not null) Status += " · " + listing.Notice;
            UpdateSizeMap();
            _ = MeasureResultsAsync(Results, token);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or System.ComponentModel.Win32Exception)
        { if (!_disposed && !token.IsCancellationRequested && ReferenceEquals(_pendingSearch, cts)) { Results = []; ReportError("Не удалось прочитать файлы: " + ex.Message); } }
        catch (OperationCanceledException)
        {
            // A newer keystroke replaced this search.
        }
        finally
        {
            progressCancellation.Cancel();
            if (ReferenceEquals(_pendingSearch, cts))
            {
                _pendingSearch = null;
                if (!_disposed) IsSearching = false;
            }

            cts.Dispose();
        }
    }

    private string LocationLabel() =>
        SearchAllDisks && HasQuery || string.IsNullOrEmpty(CurrentPath) ? "все диски" : CurrentPath;

    [RelayCommand]
    private void NavigateUp()
    {
        if (string.IsNullOrEmpty(CurrentPath))
        {
            return;
        }

        var trimmed = CurrentPath.TrimEnd('\\');
        var parent = Path.GetDirectoryName(trimmed);
        CurrentPath = string.IsNullOrEmpty(parent) ? null : parent;
    }

    [RelayCommand]
    private void NavigateToSegment(object? parameter)
    {
        var segmentIndex = parameter switch
        {
            int i => i,
            string s when int.TryParse(s, out var parsed) => parsed,
            _ => -1
        };

        if (segmentIndex <= 0 || Breadcrumb.Count <= 1)
        {
            CurrentPath = null;
            return;
        }

        var parts = Breadcrumb.Skip(1).Take(segmentIndex).Select(b => b.Label).ToArray();
        if (parts.Length == 0)
        {
            CurrentPath = null;
            return;
        }

        var uncPrefix = CurrentPath?.StartsWith(@"\\", StringComparison.Ordinal) == true ? @"\\" : string.Empty;
        CurrentPath = uncPrefix + string.Join("\\", parts) + "\\";
    }

    [RelayCommand]
    private void NavigateTo(ResultItem? item)
    {
        if (item is null)
        {
            return;
        }

        if (item.IsDirectory)
        {
            Query = string.Empty;
            CurrentPath = item.FullPath;
        }
        else
        {
            OpenCommand.Execute(item);
        }
    }

    [RelayCommand]
    private async Task Refresh()
    {
        if (_disposed) return;
        try
        {
            BuildNavigation();
            await RunSearchAsync(Query);
            if (!string.IsNullOrWhiteSpace(Query)) await _indexes.RefreshAsync();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        { Status = "Не удалось обновить: " + ex.Message; }
    }

    [RelayCommand]
    private void FindLargeFiles()
    {
        SortBy("Размер"); SetSortDirectionCommand.Execute("descending");
        Query = "kind:file size:>100MB";
    }

    [RelayCommand]
    private void FindOldFiles() => Query = "dm:<" + DateTime.Today.AddYears(-1).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    [RelayCommand]
    private void FindTempFiles() => Query = "ext:tmp;log;bak;cache";

    [RelayCommand]
    private void SortByName() => SortBy("Имя");

    [RelayCommand]
    private void Filter()
    {
        if (!FiltersOpen) PopulateFilterFields();
        FiltersOpen = !FiltersOpen;
    }

    public void MoveNavigationItem(int fromIndex, int toIndex)
    {
        if (fromIndex < 0 || fromIndex >= NavigationItems.Count
            || toIndex < 0 || toIndex >= NavigationItems.Count
            || fromIndex == toIndex)
        {
            return;
        }

        NavigationItems.Move(fromIndex, toIndex);
    }

    private string _sortColumn = string.Empty;
    private bool _sortAscending = true;

    public void SortBy(string column)
    {
        if (column is not ("Имя" or "Размер" or "Свободно" or "Дата изменения" or "Дата создания" or "Тип")) return;

        if (_sortColumn == column)
        {
            _sortAscending = !_sortAscending;
        }
        else
        {
            _sortColumn = column;
            _sortAscending = true;
        }

        RefreshSort();
    }

    private void ApplyCurrentSort()
    {
        IEnumerable<ResultItem> sorted = _sortColumn switch
        {
            "Свободно" => _sortAscending ? Results.OrderBy(r => r.FreeBytes) : Results.OrderByDescending(r => r.FreeBytes),
            "Имя" => _sortAscending
                ? Results.OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
                : Results.OrderByDescending(r => r.Name, StringComparer.OrdinalIgnoreCase),
            "Дата изменения" => _sortAscending
                ? Results.OrderBy(r => r.ModifiedUtc)
                : Results.OrderByDescending(r => r.ModifiedUtc),
            "Дата создания" => _sortAscending
                ? Results.OrderBy(r => r.CreatedUtc)
                : Results.OrderByDescending(r => r.CreatedUtc),
            "Тип" => _sortAscending
                ? Results.OrderBy(r => r.TypeString, StringComparer.OrdinalIgnoreCase)
                : Results.OrderByDescending(r => r.TypeString, StringComparer.OrdinalIgnoreCase),
            "Размер" => _sortAscending
                ? Results.OrderBy(r => r.EffectiveSize)
                : Results.OrderByDescending(r => r.EffectiveSize),
            _ => Results,
        };

        var ordered = sorted.ToArray();
        if (!Results.SequenceEqual(ordered)) Results = ordered;
    }

    [RelayCommand]
    private void FindEmptyFolders()
    {
        if (_index is null)
        {
            return;
        }

        var entries = _index.Entries;
        var hasChildren = new HashSet<int>();
        for (var i = 0; i < entries.Count; i++)
        {
            if (entries[i].ParentId != FileEntry.NoParent)
            {
                hasChildren.Add(entries[i].ParentId);
            }
        }

        var prefix = string.IsNullOrEmpty(CurrentPath) ? null : CurrentPath.TrimEnd('\\') + "\\";
        var found = new List<SearchResult>();
        for (var i = 0; i < entries.Count && found.Count < _resultLimit; i++)
        {
            var entry = entries[i];
            if (!entry.IsDirectory || hasChildren.Contains(entry.Id))
            {
                continue;
            }

            var path = _index.GetFullPath(i);
            if (prefix is not null && !path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            found.Add(new SearchResult(entry, path, _index.GetEffectiveSize(i)));
        }

        Results = [.. found.Select(r => new ResultItem(r, _showFolderSizes))];
        Status = $"{FormatCount(found.Count)} пустых папок · {LocationLabel()}";
    }

    [RelayCommand]
    private void Open(ResultItem? item)
    {
        if (item is not null)
        {
            Launch(item.FullPath, null);
        }
    }

    [RelayCommand]
    private void ShowInFolder(ResultItem? item)
    {
        if (item is not null)
        {
            Launch("explorer.exe", $"/select,\"{item.FullPath}\"");
        }
    }

    [RelayCommand]
    private async Task Delete(ResultItem? item)
    {
        if (item is null)
        {
            return;
        }

        await DeleteItemsAsync([item], permanently: false);
    }

    [RelayCommand]
    private async Task PermanentDelete(ResultItem? item)
    {
        await DeleteItemsAsync(item is not null && !item.IsSelected ? [item] : SelectedRoots(), permanently: true);
    }

    private string FormatCount(int count) =>
        count >= _resultLimit ? $"Показаны первые {_resultLimit}" : $"Найдено: {count}";

    private void Launch(string target, string? arguments)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(target) { Arguments = arguments ?? string.Empty, UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Status = "Не удалось открыть: " + ex.Message;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        UnsubscribeOperationActivity();
        foreach (var node in NavigationItems) node.Dispose();
        _operations.Completed -= OperationsCompleted;
        _indexes.Changed -= IndexChanged;
        _sizesCancellation?.Cancel();
        _folderWatcher?.Dispose();
        Services.SettingsService.Changed -= SettingsChanged;
        foreach (var item in Results) item.PropertyChanged -= SelectionChanged;
        _previewCancellation?.Cancel();
        _disposed = true;
        _lifetime.Cancel();
        _pendingSearch?.Cancel();
        _lifetime.Dispose();
    }
}

