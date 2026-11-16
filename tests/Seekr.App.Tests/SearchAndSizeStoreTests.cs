using Seekr.App.Services;
using Seekr.Core.Index;
using Seekr.Core.Search;
using Seekr.Indexer;

namespace Seekr.App.Tests;

public sealed class SearchAndSizeStoreTests : IDisposable
{
    private readonly string _root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "Seekr-stores-" + Guid.NewGuid().ToString("N"))).FullName;
    [Fact]
    public void SavedQueriesRoundTripModeOrderingAndRejectStaleUpdates()
    {
        var store = new SavedSearchStore(Path.Combine(_root, "settings.json"));
        var original = new SavedSearch("Текст", "contract ext:txt", _root, true, "Размер", false);
        Assert.Null(store.Add(original));
        Assert.Equal(original, Assert.Single(store.List()));
        Assert.NotNull(store.Add(original with { Name = "текст" }));
        var changed = original with { Name = "Договоры" };
        Assert.Null(store.Update(original, changed));
        Assert.NotNull(store.Update(original, original));
        Assert.Null(store.Remove(changed)); Assert.Empty(store.List());
    }
    [Fact]
    public void MeasurementCacheExpiresAndEvictsOldEntries()
    {
        var clock = new Clock(); var cache = new FolderSizeCache(2, clock);
        var measurement = new FolderMeasurement(100, 1, DateTime.Now);
        cache.Put("one", measurement); clock.Advance(); cache.Put("two", measurement);
        clock.Advance(); cache.Put("three", measurement);
        Assert.Null(cache.Get("one")); Assert.Equal(measurement, cache.Get("three"));
        clock.Advance(31); Assert.Null(cache.Get("three"));
        cache.Clear(); Assert.Null(cache.Get("two"));
    }
    [Fact]
    public async Task LiveScopeIncludesChangesMissingFromCachedIndex()
    {
        var index = new NameIndex(DirectoryScanner.Scan(_root, CancellationToken.None));
        await File.WriteAllTextAsync(Path.Combine(_root, "new.txt"), "new");
        var request = new WorkspaceSearchRequest(SearchQuery.Parse("new"), _root, _root, index, 500, SearchOrder.Name, false, true, true, true);
        var result = await WorkspaceSearchService.ExecuteAsync(request, CancellationToken.None);
        Assert.Equal("new.txt", Assert.Single(result.Entries).Entry.Name);
        Assert.NotNull(result.Snapshot);
    }
    private sealed class Clock : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(int seconds = 1) => _now = _now.AddSeconds(seconds);
    }
    public void Dispose() => Directory.Delete(_root, true);
}
