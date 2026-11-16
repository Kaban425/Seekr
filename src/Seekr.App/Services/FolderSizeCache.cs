namespace Seekr.App.Services;

/// <summary>UI-owned bounded cache. Watcher/operation invalidation is authoritative; TTL bounds missed events.</summary>
public sealed class FolderSizeCache(int capacity = 256, TimeProvider? clock = null)
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private readonly Dictionary<string, (FolderMeasurement Value, DateTimeOffset Saved)> _entries = new(StringComparer.OrdinalIgnoreCase);
    public FolderMeasurement? Get(string path)
    {
        if (!_entries.TryGetValue(path, out var entry)) return null;
        if (_clock.GetUtcNow() - entry.Saved <= TimeSpan.FromSeconds(30)) return entry.Value;
        _entries.Remove(path); return null;
    }
    public void Put(string path, FolderMeasurement measurement)
    {
        _entries[path] = (measurement, _clock.GetUtcNow());
        while (_entries.Count > Math.Max(1, capacity)) _entries.Remove(_entries.MinBy(x => x.Value.Saved).Key);
    }
    public void Clear() => _entries.Clear();
}
