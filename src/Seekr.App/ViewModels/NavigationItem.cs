using System.Windows.Media;
using System.IO;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Seekr.App.ViewModels;

public sealed partial class NavigationItem : ObservableObject, IDisposable
{
    [ObservableProperty]
    private string _label = string.Empty;

    [ObservableProperty]
    private string? _path;

    [ObservableProperty]
    private ImageSource? _icon;

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private bool _isSeparator;

    [ObservableProperty]
    private Icons.IconKind _symbol;

    private readonly CancellationTokenSource _lifetime = new();
    private Task? _loadTask;
    private bool _disposed;
    private readonly bool _canExpand;
    [ObservableProperty] private bool _isExpanded;
    public bool IsPlaceholder { get; init; }
    public ObservableCollection<NavigationItem> Children { get; } = [];

    partial void OnIsExpandedChanged(bool value) { if (value) _ = LoadChildrenAsync(); }
    public Task LoadChildrenAsync() => _disposed ? Task.CompletedTask : _loadTask ??= LoadCoreAsync();
    private async Task LoadCoreAsync()
    {
        if (!_canExpand || string.IsNullOrEmpty(Path)) return;
        var token = _lifetime.Token;
        try
        {
            var settings = Services.SettingsService.Load();
            var entries = await Task.Run(() =>
            {
                var options = new EnumerationOptions { IgnoreInaccessible = false, AttributesToSkip =
                    (settings.ShowHiddenFiles ? 0 : FileAttributes.Hidden) | (settings.ShowSystemFiles ? 0 : FileAttributes.System) };
                var folders = new List<(string Name, string Path, bool Expand)>();
                var truncated = false;
                foreach (var folder in new DirectoryInfo(Path).EnumerateDirectories("*", options))
                {
                    token.ThrowIfCancellationRequested();
                    if (folders.Count >= 500) { truncated = true; break; }
                    folders.Add((folder.Name, folder.FullName, folder.LinkTarget is null));
                }
                return (Folders: folders.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToArray(), Truncated: truncated);
            }, token);
            if (_disposed) return;
            foreach (var child in Children) child.Dispose();
            Children.Clear();
            foreach (var entry in entries.Folders) Children.Add(new NavigationItem(entry.Name, entry.Path, Utils.FilePresentation.GetIcon(true, null), canExpand: entry.Expand));
            if (entries.Truncated) Children.Add(new NavigationItem("Первые 500 папок — остальные доступны в списке файлов") { IsPlaceholder = true });
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            if (!_disposed) { foreach (var child in Children) child.Dispose(); Children.Clear(); Children.Add(new NavigationItem("Нет доступа к папкам") { IsPlaceholder = true }); }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _lifetime.Cancel();
        foreach (var child in Children) child.Dispose();
        _lifetime.Dispose();
    }

    public Services.SavedSearch? Search { get; init; }

    public NavigationItem(string label, string? path = null, ImageSource? icon = null, Icons.IconKind symbol = Icons.IconKind.None, bool canExpand = true)
    {
        _label = label;
        _path = path;
        _icon = icon;
        _symbol = symbol == Icons.IconKind.None && path is not null ? Icons.IconKind.Folder : symbol;
        _canExpand = canExpand && path is not null;
        if (_canExpand) Children.Add(new NavigationItem("Загрузка…", canExpand: false) { IsPlaceholder = true });
    }
}
