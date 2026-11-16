using FluentAssertions;
using Seekr.Core.Index;
using Seekr.Core.Search;

namespace Seekr.Core.Tests;

public class NameIndexTests
{
    private static readonly DateTime Time = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static NameIndex BuildSample() => new(
    [
        new FileEntry(0, FileEntry.NoParent, @"C:\", true, 0, Time, Time),
        new FileEntry(1, 0, "Games", true, 0, Time, Time),
        new FileEntry(2, 1, "dota 2 beta", true, 0, Time, Time),
        new FileEntry(3, 2, "dota2.exe", false, 1024, Time, Time),
        new FileEntry(4, 0, "Notes", true, 0, Time, Time),
        new FileEntry(5, 4, "dota-guide.md", false, 10, Time, Time),
        new FileEntry(6, 4, "todo.txt", false, 5, Time, Time),
    ]);

    [Fact]
    public void Search_FindsEntriesContainingTermIgnoringCase()
    {
        var results = BuildSample().Search(SearchQuery.Parse("DOTA"), limit: 10);

        results.Select(r => r.Entry.Name).Should().BeEquivalentTo("dota 2 beta", "dota2.exe", "dota-guide.md");
    }

    [Fact]
    public void Search_RequiresAllTerms()
    {
        var results = BuildSample().Search(SearchQuery.Parse("dota exe"), limit: 10);

        results.Select(r => r.Entry.Name).Should().Equal("dota2.exe");
    }

    [Fact]
    public void Search_FiltersByExtensionAndSkipsDirectories()
    {
        var results = BuildSample().Search(SearchQuery.Parse("ext:md;txt"), limit: 10);

        results.Select(r => r.Entry.Name).Should().BeEquivalentTo("dota-guide.md", "todo.txt");
    }

    [Fact]
    public void Search_FiltersByCreatedDate()
    {
        var today = DateTime.Today.ToUniversalTime();
        var yesterday = today.AddDays(-1);
        var index = new NameIndex(
        [
            new FileEntry(0, FileEntry.NoParent, @"C:\", true, 0, Time, yesterday),
            new FileEntry(1, 0, "old.txt", false, 10, Time, yesterday),
            new FileEntry(2, 0, "new.txt", false, 10, Time, today),
        ]);

        var results = index.Search(SearchQuery.Parse("dc:today"), limit: 10);

        results.Select(r => r.Entry.Name).Should().BeEquivalentTo("new.txt");
    }

    [Fact]
    public void GetFullPath_RootKeepsTrailingSlash()
    {
        var index = new NameIndex(
        [
            new FileEntry(0, FileEntry.NoParent, @"D:\", true, 0, Time, Time),
            new FileEntry(1, 0, "Games", true, 0, Time, Time),
        ]);

        index.GetFullPath(0).Should().Be(@"D:\");
        index.GetFullPath(1).Should().Be(@"D:\Games");
    }

    [Fact]
    public void Search_ReturnsFullPath()
    {
        var result = BuildSample().Search(SearchQuery.Parse("dota2.exe"), limit: 10).Single();

        result.FullPath.Should().Be(@"C:\Games\dota 2 beta\dota2.exe");
    }

    [Fact]
    public void Search_RespectsLimit()
    {
        var results = BuildSample().Search(SearchQuery.Parse("o"), limit: 2);

        results.Should().HaveCount(2);
    }

    [Fact]
    public void Search_EmptyQueryReturnsNothing()
    {
        BuildSample().Search(SearchQuery.Parse(""), limit: 10).Should().BeEmpty();
    }

    [Fact]
    public void Constructor_RejectsIdsThatDoNotMatchPosition()
    {
        var act = () => new NameIndex([new FileEntry(5, FileEntry.NoParent, @"C:\", true, 0, Time, Time)]);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_RejectsUnknownParent()
    {
        var act = () => new NameIndex([new FileEntry(0, 7, "x", false, 0, Time, Time)]);

        act.Should().Throw<ArgumentException>();
    }
}
