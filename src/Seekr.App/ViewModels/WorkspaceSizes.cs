using System.IO;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Seekr.App.Services;

namespace Seekr.App.ViewModels;

public sealed partial class WorkspaceViewModel
{
    private readonly FolderSizeCache _sizeCache = new();
    private bool _scopeChanged;
    private FolderWatcher? _folderWatcher;
    private CancellationTokenSource? _sizesCancellation;
    [ObservableProperty] private bool _spaceMapOpen;
    public IReadOnlyList<ResultItem> SpaceMapItems => Results.OrderByDescending(x => x.EffectiveSize).Take(50).ToArray();

    private void WatchFolder(string? path)
    {
        _folderWatcher?.Dispose();
        _folderWatcher = null;
        if (string.IsNullOrEmpty(path) || _disposed) return;
        try { _folderWatcher = new FolderWatcher(path, Dispatcher.CurrentDispatcher, () => { _sizeCache.Clear(); _scopeChanged = true; _ = RunSearchAsync(Query); }); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        { Status = "Автообновление папки недоступно: " + ex.Message; }
    }

    [RelayCommand]
    private async Task RecalculateSizes()
    {
        _sizeCache.Clear();
        await MeasureResultsAsync(Results, _lifetime.Token, force: true);
    }

    private async Task MeasureResultsAsync(IReadOnlyList<ResultItem> results, CancellationToken token, bool force = false)
    {
        _sizesCancellation?.Cancel();
        if (!force)
            foreach (var item in results.Where(x => x.IsDirectory && !x.IsDrive))
                if (_sizeCache.Get(item.FullPath) is { } cached) item.ApplyMeasurement(cached);
        var cts = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);
        _sizesCancellation = cts;
        try
        {
            // Parallel.ForEachAsync bounds IO pressure; UI changes always return to the UI dispatcher.
            var dispatcher = Dispatcher.CurrentDispatcher;
            await Parallel.ForEachAsync(results.Where(x => x.IsDirectory && !x.IsDrive && (_showFolderSizes || force) && (force || !x.HasMeasuredSize)),
                new ParallelOptions { MaxDegreeOfParallelism = 2, CancellationToken = cts.Token }, async (item, ct) =>
                {
                    var measured = await Task.Run(() => FolderSizeService.Measure(item.FullPath, ct), ct);
                    await dispatcher.InvokeAsync(() =>
                    {
                        if (!_disposed && !cts.IsCancellationRequested && ReferenceEquals(Results, results))
                        { _sizeCache.Put(item.FullPath, measured); item.ApplyMeasurement(measured); UpdateSizeMap(); UpdateSelection(updatePreview: false); }
                    });
                });
            if (!_disposed && ReferenceEquals(Results, results)) { UpdateSizeMap(); if (_sortColumn == "Размер") ApplyCurrentSort(); }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { if (!_disposed && !cts.IsCancellationRequested && ReferenceEquals(Results, results)) Status = "Не удалось подсчитать размер: " + ex.Message; }
        finally { if (ReferenceEquals(_sizesCancellation, cts)) _sizesCancellation = null; cts.Dispose(); }
    }

    private void UpdateSizeMap()
    {
        var largest = Results.Count == 0 ? 0 : Results.Max(x => x.EffectiveSize);
        foreach (var item in Results) item.SizeBarWidth = largest > 0 ? Math.Max(0, item.EffectiveSize) / (double)largest * 180 : 0;
        OnPropertyChanged(nameof(SpaceMapItems));
    }
}
