using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Seekr.App.Services;
using Seekr.App.Utils;
using Seekr.Core.Index;
using Seekr.Core.Search;

namespace Seekr.App.ViewModels;

public sealed partial class WorkspaceViewModel
{
    [RelayCommand] private void ReportIssue() { if (!_disposed) new FeedbackWindow { Owner = Application.Current.MainWindow }.ShowDialog(); }
    [RelayCommand]
    private void ShowHelp()
    {
        if (_disposed) return;
        var window = new QuickStartWindow { Owner = Application.Current.MainWindow };
        window.ShowDialog();
    }

    private readonly Stack<string?> _back = new();
    private readonly Stack<string?> _forward = new();
    private bool _batchSelection;
    private bool _restoringHistory;
    private bool _syncingSidebar;
    private bool _showHiddenFiles;
    private bool _showSystemFiles;
    private CancellationTokenSource? _previewCancellation;

    [ObservableProperty] private bool _compactMode;
    [ObservableProperty] private bool _showPreviewPane;
    [ObservableProperty] private bool _openWithSingleClick;
    [ObservableProperty] private bool _filtersOpen;
    [ObservableProperty] private bool _isOperating;
    [ObservableProperty] private string _selectionText = string.Empty;
    [ObservableProperty] private int _selectedCount;
    [ObservableProperty] private string _previewText = "Выберите файл для просмотра сведений.";
    [ObservableProperty] private string _savedSearchName = string.Empty;
    [ObservableProperty] private bool _saveSearchOpen;
    [ObservableProperty] private bool _onlyFileTypes;
    [ObservableProperty] private string _extensionFilter = string.Empty;
    [ObservableProperty] private string _minimumSizeMb = string.Empty;
    [ObservableProperty] private string _dateFilter = "Любая дата";

    public string SearchScope => string.IsNullOrEmpty(CurrentPath) ? "Все доступные диски" : CurrentPath;
    public string EmptyStateTitle => HasErrorMessage ? (HasQuery ? "Не удалось выполнить поиск" : "Не удалось открыть папку")
        : IsSearching ? (HasQuery ? "Ищем файлы…" : "Открываем папку…")
        : HasQuery ? (SearchContents ? "Нет совпадений в проверенных файлах" : "Ничего не найдено") : CurrentPath is not null ? "Папка пуста" : "Ваши файлы — под рукой";
    public string EmptyStateHint => HasErrorMessage ? "Проверьте сообщение выше. Можно повторить попытку или выбрать другую папку."
        : IsSearching ? "Можно продолжать работу. Результаты появятся по мере готовности."
        : HasQuery ? (SearchContents ? "Поддерживаются текст, исходный код и основной текст DOCX. Ограничения — в строке состояния." : "Попробуйте другое имя или сбросьте условия поиска.")
        : CurrentPath is not null ? "Здесь пока нет файлов. Можно создать новую папку."
        : "Откройте диск слева или найдите файл по имени, типу и дате.";
    public Icons.IconKind EmptyStateSymbol => HasErrorMessage ? Icons.IconKind.Warning : HasQuery ? Icons.IconKind.Search : Icons.IconKind.Folder;
    public bool CanRetryEmptyState => HasErrorMessage && !IsSearching;
    public bool CanResetEmptySearch => HasQuery && !HasErrorMessage && !IsSearching;
    public bool CanCreateInEmptyFolder => CurrentPath is not null && !HasQuery && !HasErrorMessage && !IsSearching;

    private void UpdateEmptyPresentation()
    {
        OnPropertyChanged(nameof(EmptyStateTitle)); OnPropertyChanged(nameof(EmptyStateHint));
        OnPropertyChanged(nameof(EmptyStateSymbol)); OnPropertyChanged(nameof(CanRetryEmptyState));
        OnPropertyChanged(nameof(CanResetEmptySearch)); OnPropertyChanged(nameof(CanCreateInEmptyFolder));
    }
    [RelayCommand] private void ResetEmptySearch() => Query = string.Empty;
    public IReadOnlyList<string> DateFilters { get; } = ["Любая дата", "Сохранить из запроса", "Сегодня", "Эта неделя", "Этот месяц", "Старше года"];

    private void SettingsChanged(object? sender, EventArgs e)
    {
        if (_disposed) return;
        LoadSettings();
        UpdateIcon();
        BuildNavigation();
        UpdateSidebarSelection(CurrentPath);
        _ = RunSearchAsync(Query);
    }

    partial void OnIsIndexingChanged(bool value) => OnPropertyChanged(nameof(EmptyStateTitle));

    partial void OnResultsChanging(IReadOnlyList<ResultItem> value)
    {
        foreach (var item in Results) item.PropertyChanged -= SelectionChanged;
    }

    partial void OnResultsChanged(IReadOnlyList<ResultItem> value)
    {
        foreach (var item in value) item.PropertyChanged += SelectionChanged;
        UpdateSelection();
    }

    private void SelectionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!_batchSelection && e.PropertyName == nameof(ResultItem.IsSelected)) UpdateSelection();
    }

    public IReadOnlyList<string> SelectedPaths => SelectedRoots().Select(x => x.FullPath).ToArray();

    private ResultItem[] SelectedRoots()
    {
        var selected = Results.Where(x => x.IsSelected).ToArray();
        var paths = SelectionSummary.Roots(selected.Select((x, i) => new SearchResult(
            new FileEntry(i, -1, x.Name, x.IsDirectory, x.EffectiveSize, x.ModifiedUtc, x.CreatedUtc), x.FullPath, x.EffectiveSize)))
            .Select(x => x.FullPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return selected.Where(x => paths.Contains(x.FullPath)).ToArray();
    }

    private void UpdateSelection(bool updatePreview = true)
    {
        SelectedCount = Results.Count(x => x.IsSelected);
        var roots = SelectedRoots();
        var size = roots.Sum(x => (decimal)Math.Max(x.EffectiveSize, 0));
        SelectionText = $"Выбрано: {SelectedCount} · {ResultItem.FormatBytes((long)Math.Min(size, long.MaxValue))}"
            + (roots.Any(x => x.EffectiveSize < 0 || x.IsSizePartial) ? " · размер неполный" : string.Empty);
        DeleteSelectedCommand.NotifyCanExecuteChanged();
        CopyPathsCommand.NotifyCanExecuteChanged();
        if (updatePreview) _ = UpdatePreviewAsync();
    }

    private async Task UpdatePreviewAsync()
    {
        _previewCancellation?.Cancel();
        _previewCancellation?.Dispose();
        _previewCancellation = new CancellationTokenSource();
        var token = _previewCancellation.Token;
        var item = Results.FirstOrDefault(x => x.IsSelected);
        PreviewText = item is null ? "Выберите файл для просмотра сведений." : $"{item.Name}\n\n{item.FullPath}\n\nРазмер содержимого: {item.SizeString}\nИзменён: {item.ModifiedString}";
        if (ShowPreviewPane && item is not null)
        {
            try
            {
                var metadata = await Task.Run(() =>
                {
                    var details = $"\nТип: {Utils.FilePresentation.GetTypeName(item.IsDirectory, item.Extension)}\nСоздан: {File.GetCreationTime(item.FullPath):dd.MM.yyyy HH:mm}\nАтрибуты: {File.GetAttributes(item.FullPath)}";
                    var root = Path.GetPathRoot(item.FullPath);
                    if (root is not null && string.Equals(root.TrimEnd('\\'), item.FullPath.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                    { var drive = new DriveInfo(root); details += $"\n\nСвободно: {ResultItem.FormatBytes(drive.AvailableFreeSpace)} из {ResultItem.FormatBytes(drive.TotalSize)}\nФайловая система: {drive.DriveFormat}"; }
                    return details;
                }, token);
                if (token.IsCancellationRequested || _disposed) return;
                PreviewText += metadata;
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { if (!token.IsCancellationRequested && !_disposed) PreviewText += "\nЧасть сведений недоступна."; }
        }
        if (!ShowPreviewPane || item is null || item.IsDirectory || !new[] { "txt", "md", "log", "json", "csv", "xml", "cs" }.Contains(item.Extension)) return;
        try
        {
            var text = await Task.Run(async () =>
            {
                using var reader = new StreamReader(new FileStream(item.FullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete));
                var buffer = new char[4096];
                var count = await reader.ReadAsync(buffer.AsMemory(), token);
                return new string(buffer, 0, count);
            }, token);
            if (!token.IsCancellationRequested && !_disposed) PreviewText += "\n\n" + text;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { if (!token.IsCancellationRequested) PreviewText += "\n\nПредпросмотр недоступен."; }
    }

    private bool CanGoBack() => _back.Count > 0;
    private bool CanGoForward() => _forward.Count > 0;

    [RelayCommand(CanExecute = nameof(CanGoBack))]
    private void NavigateBack() => RestoreHistory(_back, _forward);

    [RelayCommand(CanExecute = nameof(CanGoForward))]
    private void NavigateForward() => RestoreHistory(_forward, _back);

    private void RestoreHistory(Stack<string?> from, Stack<string?> to)
    {
        if (from.Count == 0) return;
        to.Push(CurrentPath);
        _restoringHistory = true;
        try { CurrentPath = from.Pop(); }
        finally { _restoringHistory = false; }
        NavigateBackCommand.NotifyCanExecuteChanged();
        NavigateForwardCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private async Task CreateFolder()
    {
        if (string.IsNullOrEmpty(CurrentPath)) { Status = "Сначала откройте папку для создания новой."; return; }
        var name = TextPrompt.Show("Новая папка", "Имя папки", "Новая папка");
        if (name is null) return;
        if (!Seekr.Core.Operations.FileNameRules.IsValid(name))
        { Status = "Недопустимое имя папки."; return; }
        try
        {
            var path = Path.Combine(CurrentPath, name);
            if (Directory.Exists(path) || File.Exists(path)) { Status = "Такое имя уже существует."; return; }
            Directory.CreateDirectory(path);
            await Refresh();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        { Status = "Не удалось создать папку: " + ex.Message; }
    }

    private void PopulateFilterFields()
    {
        var tokens = Query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        ExtensionFilter = tokens.LastOrDefault(t => t.StartsWith("ext:", StringComparison.OrdinalIgnoreCase))?[4..] ?? string.Empty;
        var size = tokens.LastOrDefault(t => t.StartsWith("size:", StringComparison.OrdinalIgnoreCase));
        var parsed = SearchQuery.Parse(Query);
        MinimumSizeMb = parsed.SizeMin is { } min && parsed.SizeMax is null
            ? (Math.Max(0, min - 1) / (1024d * 1024)).ToString("0.########", CultureInfo.InvariantCulture) : string.Empty;
        // Unrepresented ranges stay in the query until explicitly replaced.
        _preservedSizeFilter = string.IsNullOrEmpty(MinimumSizeMb) ? size : null;
        _preservedDateFilter = tokens.LastOrDefault(t => t.StartsWith("dm:", StringComparison.OrdinalIgnoreCase));
        DateFilter = "Сохранить из запроса";
    }

    private string? _preservedSizeFilter;
    private string? _preservedDateFilter;

    [RelayCommand]
    private void ApplyFilters()
    {
        var terms = string.Join(' ', Query.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(t => !t.StartsWith("ext:", StringComparison.OrdinalIgnoreCase)
                && !t.StartsWith("size:", StringComparison.OrdinalIgnoreCase)
                && !t.StartsWith("dm:", StringComparison.OrdinalIgnoreCase)));
        var parts = new List<string> { OnlyFileTypes ? string.Empty : terms };
        if (OnlyFileTypes) SearchContents = false;
        var extensions = string.Join(';', ExtensionFilter.Split([';', ',', ' '], StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim().TrimStart('*', '.').ToLowerInvariant()).Distinct());
        if (extensions.Length > 0) parts.Add("ext:" + extensions);
        if (!OnlyFileTypes && !string.IsNullOrWhiteSpace(MinimumSizeMb))
        {
            if (!double.TryParse(MinimumSizeMb.Replace(',', '.'), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var size) || !double.IsFinite(size) || size < 0 || size > 1_000_000_000)
            { Status = "Введите положительный размер в МБ."; return; }
            parts.Add("size:>" + size.ToString(CultureInfo.InvariantCulture) + "MB");
        }
        if (!OnlyFileTypes && string.IsNullOrWhiteSpace(MinimumSizeMb) && _preservedSizeFilter is not null) parts.Add(_preservedSizeFilter);
        if (!OnlyFileTypes && DateFilter == "Сохранить из запроса" && _preservedDateFilter is not null) parts.Add(_preservedDateFilter);
        var date = DateFilter switch { "Сегодня" => "today", "Эта неделя" => "thisweek", "Этот месяц" => "thismonth", "Старше года" => "<" + DateTime.Today.AddYears(-1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), _ => null };
        if (!OnlyFileTypes && date is not null) parts.Add("dm:" + date);
        var next = string.Join(' ', parts.Where(x => !string.IsNullOrWhiteSpace(x)));
        if (SearchQuery.Parse(next).Error is { } error) { ReportError(error); return; }
        Query = next;
        FiltersOpen = false;
    }

    [RelayCommand]
    private void ClearFilters()
    {
        _preservedDateFilter = _preservedSizeFilter = null;
        ExtensionFilter = MinimumSizeMb = string.Empty;
        DateFilter = DateFilters[0];
        Query = string.Empty;
    }

    [RelayCommand]
    private void SearchEverywhere() => CurrentPath = null;

    private readonly SavedSearchStore _savedSearchStore = new();

    private bool CanSaveSearch() => !string.IsNullOrWhiteSpace(Query) && SearchQuery.Parse(Query) is { Error: null } parsed && (!SearchContents || parsed.Terms.Count > 0);

    [RelayCommand(CanExecute = nameof(CanSaveSearch))]
    private void SaveSearch()
    {
        SavedSearchName = Query;
        SaveSearchOpen = true;
    }

    [RelayCommand]
    private void ConfirmSaveSearch()
    {
        if (!CanSaveSearch() || string.IsNullOrWhiteSpace(SavedSearchName)) return;
        var error = _savedSearchStore.Add(new(SavedSearchName, Query, SearchAllDisks ? null : CurrentPath, SearchContents, _sortColumn, _sortAscending));
        if (error is not null) { ReportError(error); return; }
        SaveSearchOpen = false;
        Status = "Поиск сохранён. Он доступен в боковой панели.";
    }

    [RelayCommand]
    private void RemoveSavedSearch(NavigationItem? item)
    {
        if (_disposed || item?.Search is null) return;
        if (_savedSearchStore.Remove(item.Search) is { } error) ReportError(error);
    }

    [RelayCommand]
    private void RenameSavedSearch(NavigationItem? item)
    {
        if (item?.Search is not { } search) return;
        var name = TextPrompt.Show("Название поиска", "Новое название", search.Name, "Сохранить");
        if (name is not null && _savedSearchStore.Update(search, search with { Name = name }) is { } error) ReportError(error);
    }
    [RelayCommand]
    private void UpdateSavedSearch(NavigationItem? item)
    {
        if (item?.Search is not { } search || !CanSaveSearch()) return;
        var changed = new SavedSearch(search.Name, Query, SearchAllDisks ? null : CurrentPath, SearchContents, _sortColumn, _sortAscending);
        if (_savedSearchStore.Update(search, changed) is { } error) ReportError(error);
    }

    [RelayCommand]
    private void CancelSaveSearch() => SaveSearchOpen = false;

    [RelayCommand]
    private void ClearSelection()
    {
        _batchSelection = true;
        try { foreach (var item in Results) item.IsSelected = false; }
        finally { _batchSelection = false; UpdateSelection(); }
    }

    [RelayCommand]
    private void SelectAll()
    {
        _batchSelection = true;
        try { foreach (var item in Results) item.IsSelected = true; }
        finally { _batchSelection = false; UpdateSelection(); }
    }

    private bool CanUseSelection() => SelectedCount > 0 && !IsOperating;

    partial void OnIsOperatingChanged(bool value)
    {
        DeleteSelectedCommand.NotifyCanExecuteChanged();
        CopyPathsCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanUseSelection))]
    private void CopyPaths()
    {
        try { Clipboard.SetText(string.Join(Environment.NewLine, Results.Where(x => x.IsSelected).Select(x => x.FullPath))); }
        catch (System.Runtime.InteropServices.ExternalException) { Status = "Буфер обмена занят. Повторите попытку."; }
    }

    [RelayCommand]
    private void OpenRecycleBin() => Launch("explorer.exe", "shell:RecycleBinFolder");

    [RelayCommand(CanExecute = nameof(CanUseSelection))]
    private async Task DeleteSelected() => await DeleteItemsAsync(SelectedRoots(), permanently: false);

    private Task DeleteItemsAsync(ResultItem[] items, bool permanently, Func<string, CancellationToken, Task>? validate = null)
    {
        if (_disposed || IsOperating || items.Length == 0) return Task.CompletedTask;
        if (items.Any(x => string.Equals(Path.GetPathRoot(x.FullPath)?.TrimEnd('\\'), x.FullPath.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)))
        { Status = "Корневые папки дисков удалять нельзя."; return Task.CompletedTask; }
        var action = permanently ? "БЕЗВОЗВРАТНО удалить" : "Переместить в корзину";
        var names = string.Join("\n", items.Take(6).Select(x => "• " + x.FullPath));
        var message = $"{action}: {items.Length}?\n\n{names}" + (items.Length > 6 ? $"\n…ещё {items.Length - 6}" : string.Empty)
            + (permanently ? "\n\nВосстановление через корзину будет невозможно." : "\n\nЕсли корзина недоступна, операция остановится. Безвозвратное удаление нужно выбрать отдельно.");
        if (!ConfirmationWindow.Confirm(action, message, permanently ? "Удалить навсегда" : "В корзину")) return Task.CompletedTask;
        _operations.EnqueueAction(action, items.Select(x => x.FullPath).ToArray(), async (paths, progress, token) =>
        {
            var done = new List<string>();
            var failed = new List<Seekr.Core.Operations.TransferFailure>();
            foreach (var path in paths)
            {
                token.ThrowIfCancellationRequested();
                if (!File.Exists(path) && !Directory.Exists(path)) { done.Add(path); continue; }
                if (validate is not null)
                {
                    try { await validate(path, token); }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    { failed.Add(new(path, ex.Message)); continue; }
                }
                var deletion = await RecycleBinHelper.ExecuteWithResultAsync(path, permanently);
                if (deletion.Success) done.Add(path);
                else failed.Add(new(path, deletion.Error ?? "Windows не завершила удаление."));
                progress.Report(new(path, done.Count + failed.Count, paths.Count, done.Count + failed.Count, paths.Count));
            }
            return new(done, failed, 0);
        });
        // Progress and failures remain accessible through the status-bar Operations button.
        return Task.CompletedTask;
    }
}
