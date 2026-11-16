using Seekr.Core.Index;
using Seekr.Core.Search;
using FluentAssertions;

namespace Seekr.Core.Tests;

public sealed class SearchRegressionTests
{
    private static readonly DateTime Time = DateTime.UtcNow;

    [Fact]
    public async Task ParallelSearchesOnOneIndexHaveStableIndependentResults()
    {
        var entries = new List<FileEntry> { new(0, -1, @"C:\", true, 0, Time, Time) };
        for (var i = 1; i <= 10_000; i++) entries.Add(new(i, 0, $"file-{i}.{(i % 2 == 0 ? "PDF" : "txt")}", false, i, Time, Time));
        var index = new NameIndex(entries);
        var query = SearchQuery.Parse("ext:pdf");
        var expected = index.Search(query, 50, order: SearchOrder.Size, descending: true).Select(x => x.FullPath).ToArray();
        var searches = Enumerable.Range(0, 16).Select(_ => Task.Run(() => index.Search(query, 50, order: SearchOrder.Size, descending: true)));
        foreach (var results in await Task.WhenAll(searches)) Assert.Equal(expected, results.Select(x => x.FullPath));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => new NameIndex(entries, cancellation.Token));
        Assert.ThrowsAny<OperationCanceledException>(() => index.Search(query, 50, cancellationToken: cancellation.Token));
        Assert.Equal(expected, index.Search(query, 50, order: SearchOrder.Size, descending: true).Select(x => x.FullPath));
    }

    [Fact]
    public void SizeFilterChecksRecursiveFolderSize()
    {
        var index = new NameIndex([
            new(0, -1, @"C:\", true, 0, Time, Time),
            new(1, 0, "small", true, 0, Time, Time),
            new(2, 1, "note.txt", false, 5, Time, Time)]);
        index.Search(SearchQuery.Parse("size:>100MB"), 10).Should().BeEmpty();
    }

    [Fact]
    public void TodayUsesLocalMidnight()
    {
        var query = SearchQuery.Parse("dm:today");
        query.ModifiedMin.Should().Be(DateTime.Today.ToUniversalTime());
        query.ModifiedMax.Should().Be(DateTime.Today.AddDays(1).ToUniversalTime().AddTicks(-1));
    }

    [Fact]
    public void InvalidFilterMustNotBecomeAnUnfilteredSearch()
    {
        var index = new NameIndex([new(0, -1, @"C:\", true, 0, Time, Time),
            new(1, 0, "note.txt", false, 5, Time, Time)]);
        index.Search(SearchQuery.Parse("note size:oops"), 10).Should().BeEmpty();
    }

    [Fact]
    public void StrictSizeComparisonExcludesBoundary()
    {
        var index = new NameIndex([new(0, -1, @"C:\", true, 0, Time, Time),
            new(1, 0, "note.txt", false, 100, Time, Time)]);
        index.Search(SearchQuery.Parse("note size:>100"), 10).Should().BeEmpty();
    }
    [Fact]
    public void FolderScopeIsAppliedBeforeLimit()
    {
        var index = new NameIndex([
            new(0, -1, @"C:\", true, 0, Time, Time),
            new(1, 0, "notes.txt", false, 1, Time, Time),
            new(2, 0, "Target", true, 0, Time, Time),
            new(3, 2, "notes-long.txt", false, 1, Time, Time)]);
        index.Search(SearchQuery.Parse("notes"), 1, @"C:\Target")
            .Select(r => r.Entry.Name).Should().Equal("notes-long.txt");
    }

    [Fact]
    public void DateBeforeFilterIncludesOlderYears()
    {
        var query = SearchQuery.Parse("dm:<2025-11-09");
        query.ModifiedMin.Should().BeNull();
        query.ModifiedMax.Should().Be(new DateTime(2025, 11, 9).ToUniversalTime().AddTicks(-1));
    }
    [Theory]
    [InlineData("dm:>9999-12-31")]
    [InlineData("dm:<0001-01-01")]
    public void ExtremeDatesAreReportedWithoutThrowing(string text)
    {
        SearchQuery.Parse(text).Error.Should().NotBeNull();
    }
}
