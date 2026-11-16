using System.Collections.ObjectModel;
using System.IO;
using Seekr.Core.Index;
using Seekr.Indexer;

namespace Seekr.App.Services;

/// <summary>One owner per shell. Tabs borrow this service and never dispose it.</summary>
public sealed class SharedIndexService : IDisposable, IAsyncDisposable
{
    private readonly Func<CancellationToken, LoadedIndex> _load;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly bool _useCache;
    private readonly TimeSpan? _watchInterval;
    private readonly object _sync = new();
    private Task? _refresh;
    private Task _shutdown = Task.CompletedTask;
    private Task? _initialization;
    private Task? _watch;
    private readonly IndexChangeMonitor _monitor;
    private readonly System.Windows.Threading.Dispatcher _dispatcher = System.Windows.Threading.Dispatcher.CurrentDispatcher;
    private bool _disposed;
    private DateTime _lastSave;

    public static SharedIndexService Default { get; } = new();
    public NameIndex? Index { get; private set; }
    public bool IsBusy { get; private set; }
    public string Status { get; private set; } = "Поисковый индекс ещё не загружен";
    public DateTime? UpdatedAt { get; private set; }
    public ObservableCollection<VolumeIndexStatus> Volumes { get; } = [];
    public bool RequiresServiceUpdate => Volumes.Any(volume => volume.RequiresServiceUpdate);
    public event EventHandler? Changed;
    public bool CoversScope(string path) => Index is not null && Index.RootPaths.Any(root =>
        path.TrimEnd('\\').Equals(root.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)
        || path.StartsWith(root.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase));

    public SharedIndexService(Func<CancellationToken, LoadedIndex>? load = null, bool useCache = true, IndexChangeMonitor? monitor = null, TimeSpan? watchInterval = null)
    {
        var progress = new Progress<VolumeIndexStatus>(ReportVolume);
        _load = load ?? (token => IndexLoader.Load(token, progress));
        _monitor = monitor ?? (load is null ? IndexChangeMonitor.CreateDefault() : new IndexChangeMonitor(() => [], (_, _, _) => null));
        _watchInterval = watchInterval;
        _useCache = useCache;
    }

    private void ReportVolume(VolumeIndexStatus volume)
    {
        if (_disposed) return;
        if (!_dispatcher.CheckAccess()) { _dispatcher.BeginInvoke(() => ReportVolume(volume)); return; }
        var previous = Volumes.FirstOrDefault(x => x.Path == volume.Path);
        if (previous is not null) Volumes[Volumes.IndexOf(previous)] = volume;
        else Volumes.Add(volume);
        if (IsBusy) { Status = $"{volume.Path} · {volume.Mode}"; Changed?.Invoke(this, EventArgs.Empty); }
    }

    public Task InitializeAsync()
    {
        lock (_sync) return _disposed ? Task.CompletedTask : _initialization ??= StartAsync();
    }

    private async Task StartAsync()
    {
        await Task.Yield();
        if (_useCache)
        {
            var cached = await Task.Run(() => IndexCache.TryLoad(IndexCache.DefaultPath));
            if (_disposed) return;
            if (cached is not null)
            {
                var index = await Task.Run(() => new NameIndex(cached.Entries, _lifetime.Token));
                if (_disposed) return;
                Index = index;
                UpdatedAt = cached.SavedUtc.ToLocalTime();
                Status = "Сохранённый индекс · обновляю…";
                Changed?.Invoke(this, EventArgs.Empty);
            }
        }
        await RefreshAsync();
        if (!_disposed) _watch = WatchAsync(_lifetime.Token);
    }

    public Task RefreshAsync()
    {
        lock (_sync)
        {
            if (_disposed) return Task.CompletedTask;
            return _refresh is { IsCompleted: false } ? _refresh : _refresh = RefreshCoreAsync();
        }
    }

    private async Task RefreshCoreAsync()
    {
        // Publish the shared task before callbacks can request another refresh.
        await Task.Yield();
        if (_disposed) return;
        var token = _lifetime.Token;
        try
        {
            await _gate.WaitAsync(token);
            try
            {
                if (_disposed) return;
                IsBusy = true;
                Status = "Обновление поиска по дискам… Просмотр папок уже доступен.";
                Changed?.Invoke(this, EventArgs.Empty);
                await Task.Run(() => _monitor.Poll(token), token);
                var loaded = await Task.Run(() => _load(token), token);
                token.ThrowIfCancellationRequested();
                _monitor.AcknowledgeRefresh();
                Index = loaded.Index;
                UpdatedAt = DateTime.Now;
                Volumes.Clear();
                foreach (var volume in loaded.Volumes ?? []) Volumes.Add(volume);
                Status = $"{Index.Count:N0} файлов и папок · обновлено {UpdatedAt:HH:mm:ss}";
                if (Volumes.Any(x => x.Error is not null)) Status += " · есть ограничения (состояние поиска)";
                if (_useCache && DateTime.UtcNow - _lastSave > TimeSpan.FromMinutes(2))
                {
                    await Task.Run(() => IndexCache.Save(IndexCache.DefaultPath, new CachedIndex(loaded.Index.Entries, loaded.Source, DateTime.UtcNow), token), token);
                    _lastSave = DateTime.UtcNow;
                }
            }
            finally { IsBusy = false; _gate.Release(); if (!_disposed) Changed?.Invoke(this, EventArgs.Empty); }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or TimeoutException)
        { Status = "Не удалось обновить поиск: " + ex.Message; if (!_disposed) Changed?.Invoke(this, EventArgs.Empty); }
    }

    private async Task WatchAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                var settings = SettingsService.Load();
                await Task.Delay(_watchInterval ?? TimeSpan.FromSeconds(settings.AutoRefreshIntervalSeconds), token);
                if (!settings.AutoRefreshIndex && _watchInterval is null) continue;
                await _gate.WaitAsync(token);
                bool dirty;
                try { dirty = await Task.Run(() => _monitor.Poll(token), token); }
                finally { _gate.Release(); }
                if (dirty) await RefreshAsync();
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { return; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or TimeoutException)
            {
                if (!_disposed) { Status = "Не удалось проверить изменения. Повторю автоматически: " + ex.Message; Changed?.Invoke(this, EventArgs.Empty); }
            }
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
            _lifetime.Cancel();
            _shutdown = ReleaseAsync();
        }
    }

    private async Task ReleaseAsync()
    {
        try { await Task.WhenAll(_initialization ?? Task.CompletedTask, _refresh ?? Task.CompletedTask, _watch ?? Task.CompletedTask).ConfigureAwait(false); }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        finally { _lifetime.Dispose(); _gate.Dispose(); }
    }

    public async ValueTask DisposeAsync()
    {
        Dispose();
        await _shutdown.ConfigureAwait(false);
    }
}
