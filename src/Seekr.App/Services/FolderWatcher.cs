using System.IO;
using System.Windows.Threading;

namespace Seekr.App.Services;

public sealed class FolderWatcher : IDisposable
{
    private readonly FileSystemWatcher _watcher;
    private readonly Timer _debounce;
    private bool _disposed;

    public FolderWatcher(string path, Dispatcher dispatcher, Action changed)
    {
        _debounce = new Timer(_ => { if (!_disposed) dispatcher.BeginInvoke(() => { if (!_disposed) changed(); }); }, null, Timeout.Infinite, Timeout.Infinite);
        _watcher = new FileSystemWatcher(path) { IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.Size | NotifyFilters.LastWrite | NotifyFilters.Attributes };
        _watcher.Changed += (_, _) => Schedule();
        _watcher.Created += (_, _) => Schedule();
        _watcher.Deleted += (_, _) => Schedule();
        _watcher.Renamed += (_, _) => Schedule();
        _watcher.Error += (_, _) => Schedule();
        _watcher.EnableRaisingEvents = true;
    }

    private void Schedule()
    {
        try { if (!_disposed) _debounce.Change(750, Timeout.Infinite); }
        catch (ObjectDisposedException) { }
    }

    public void Dispose() { _disposed = true; _watcher.Dispose(); _debounce.Dispose(); }
}
