using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.Input;
using Seekr.App.Services;
using Seekr.Core.Index;
using Seekr.Core.Search;

namespace Seekr.App.ViewModels;

public sealed partial class WorkspaceViewModel
{
    [RelayCommand] private void HideNavigation(NavigationItem? item)
    {
        if (_disposed || item?.Path is not { } path || item.Search is not null || item.IsSeparator) return;
        var error = SettingsService.Update(s => { if (!s.HiddenNavigationPaths.Contains(path, StringComparer.OrdinalIgnoreCase)) s.HiddenNavigationPaths.Add(path); s.PinnedFolders.RemoveAll(p => p.Equals(path, StringComparison.OrdinalIgnoreCase)); return null; });
        if (error is not null) ReportError(error);
    }
    [RelayCommand] private void PinCurrentFolder()
    {
        if (CurrentPath is not { } path) return;
        var error = SettingsService.Update(s => { s.HiddenNavigationPaths.RemoveAll(p => p.Equals(path, StringComparison.OrdinalIgnoreCase)); if (!s.PinnedFolders.Contains(path, StringComparer.OrdinalIgnoreCase)) s.PinnedFolders.Add(path); return null; });
        if (error is not null) ReportError(error);
    }
    public double SessionScrollOffset { get; set; }
    public IReadOnlyList<SearchSuggestion> QuerySuggestions => SearchSuggestions.For(Query);
    [RelayCommand] private void InsertSuggestion(SearchSuggestion? suggestion) { if (suggestion is not null) Query = SearchSuggestions.Insert(Query, suggestion.Token); }
    [RelayCommand] private void UndoLast()
    {
        var job = _operations.UndoLast();
        Status = job is null ? _operations.UndoStatus + " " + FileOperationService.UndoLimits : "Отмена поставлена в очередь. Подробности — в Операциях.";
    }
    [RelayCommand] private void PreviewSelected()
    {
        var item = Results.FirstOrDefault(x => x.IsSelected);
        if (item is null || item.IsDirectory) { Status = "Выберите файл для быстрого просмотра."; return; }
        new QuickPreviewWindow(item.FullPath) { Owner = Application.Current.MainWindow }.Show();
    }
    [RelayCommand] private void ShowClipboardHistory()
    {
        var choices = ClipboardHistory.Entries.Select(entry => new CommandChoice(entry.Label, () => TransferPaths(entry.Paths, entry.Move))).ToArray();
        if (choices.Length == 0) { Status = "История пуста. Скопируйте файлы в Seekr через Ctrl+C или Ctrl+X."; return; }
        new ChoiceWindow("История файлов · выберите набор для вставки", choices) { Owner = Application.Current.MainWindow }.ShowDialog();
    }
    public void TransferPaths(string[] paths, bool move, string? destination = null)
    {
        destination ??= CurrentPath;
        if (destination is null) { Status = "Откройте папку назначения."; return; }
        if (paths.Length == 0) return;
        _operations.Enqueue(paths, destination, move, Utils.ConflictPrompt.ShowAsync);
        Status = $"{(move ? "Перемещение" : "Копирование")}: {paths.Length}. Подробности — в Операциях.";
    }
    [RelayCommand] private void FindDuplicates() => AnalyzeFolder(true);
    [RelayCommand] private void CleanFolder() => AnalyzeFolder(false);
    private void AnalyzeFolder(bool duplicates)
    {
        var folder = CurrentPath;
        if (folder is null) { Status = "Откройте папку, которую нужно проверить."; return; }
        new FolderAnalysisWindow(folder, duplicates, (paths, validate) =>
        {
            var now = DateTime.UtcNow;
            var items = paths.Select((path, i) => new ResultItem(new SearchResult(new FileEntry(i, FileEntry.NoParent,
                Path.GetFileName(path), Directory.Exists(path), 0, now, now), path, 0))).ToArray();
            _ = DeleteItemsAsync(items, permanently: false, validate);
        }) { Owner = Application.Current.MainWindow }.Show();
    }
}

