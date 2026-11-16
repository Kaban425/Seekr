using Seekr.Core.Index;
using Seekr.Core.Search;

namespace Seekr.Core.Tests;

public sealed class SearchOrderTests
{
    [Fact]
    public void SizeDescendingIsAppliedBeforeLimit()
    {
        var time = DateTime.UtcNow;
        var index = new NameIndex([
            new(0, -1, @"C:\", true, 0, time, time),
            new(1, 0, "a.txt", false, 1, time, time),
            new(2, 0, "much-longer-name.txt", false, 1000, time, time)]);
        var results = index.Search(SearchQuery.Parse("ext:txt"), 1, order: SearchOrder.Size, descending: true);
        Assert.Equal("much-longer-name.txt", Assert.Single(results).Entry.Name);
    }

    [Fact]
    public void DateOrderAndScopePrecedeLimit()
    {
        var old = DateTime.UtcNow.AddDays(-5);
        var recent = DateTime.UtcNow;
        var index = new NameIndex([
            new(0, -1, @"C:\", true, 0, old, old),
            new(1, 0, "Docs", true, 0, old, old),
            new(2, 1, "a.txt", false, 1, old, old),
            new(3, 1, "z.txt", false, 1, recent, old),
            new(4, 0, "outside.txt", false, 1, recent.AddDays(1), old)]);
        var result = Assert.Single(index.Search(SearchQuery.Parse("ext:txt"), 1, @"C:\Docs", order: SearchOrder.Modified, descending: true));
        Assert.Equal("z.txt", result.Entry.Name);
    }
}
