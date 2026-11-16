using Seekr.App.Services;
using Seekr.Core.Index;
using Seekr.Core.Search;
using Seekr.Indexer;

namespace Seekr.App.Tests;

public sealed class SearchExecutionTests
{
    [Fact]
    public async Task ConcurrentRequestsKeepScopeAndCancelledRequestDoesNotPoisonNextOne()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "Seekr-search-" + Guid.NewGuid().ToString("N"))).FullName;
        var folder = Directory.CreateDirectory(Path.Combine(root, "sub")).FullName;
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "outside.txt"), "test");
            await File.WriteAllTextAsync(Path.Combine(folder, "inside.pdf"), "test");
            var index = new NameIndex(DirectoryScanner.Scan(root, CancellationToken.None));
            var request = new WorkspaceSearchRequest(SearchQuery.Parse("ext:pdf"), root, root, index, 500, SearchOrder.Name, false, true, true);
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => WorkspaceSearchService.ExecuteAsync(request, cancellation.Token));
            var searches = Enumerable.Range(0, 10).Select(i => WorkspaceSearchService.ExecuteAsync(
                request with { Query = SearchQuery.Parse(i % 2 == 0 ? "ext:pdf" : "ext:txt"), SearchPath = i % 2 == 0 ? folder : root }, CancellationToken.None));
            var results = await Task.WhenAll(searches);
            for (var i = 0; i < results.Length; i++)
            {
                Assert.Null(results[i].Error);
                Assert.Equal(i % 2 == 0 ? "inside.pdf" : "outside.txt", Assert.Single(results[i].Entries).Entry.Name);
            }
            var missing = await WorkspaceSearchService.ExecuteAsync(request with { Query = SearchQuery.Parse(""), Path = Path.Combine(root, "missing") }, CancellationToken.None);
            Assert.NotNull(missing.Error);
            var valid = await WorkspaceSearchService.ExecuteAsync(request, CancellationToken.None);
            Assert.Null(valid.Error);
            Assert.Single(valid.Entries);
        }
        finally { Directory.Delete(root, true); }
    }
}
