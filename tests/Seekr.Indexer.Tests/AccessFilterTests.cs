using FluentAssertions;
using Seekr.Core.Index;
using Seekr.Core.Search;
using Seekr.Indexer.Service;

namespace Seekr.Indexer.Tests;

public class AccessFilterTests
{
    private static readonly DateTime T = DateTime.MinValue;

    private static readonly FileEntry[] Volume =
    [
        new(0, FileEntry.NoParent, @"C:\", true, 0, T, T),
        new(1, 0, "Users", true, 0, T, T),
        new(2, 1, "me", true, 0, T, T),
        new(3, 2, "mine.txt", false, 0, T, T),
        new(4, 1, "other", true, 0, T, T),
        new(5, 4, "secret.txt", false, 0, T, T),
        new(6, 4, "Sub", true, 0, T, T),
        new(7, 6, "deep.txt", false, 0, T, T),
        new(8, 0, "public.txt", false, 0, T, T),
    ];

    [Fact]
    public void Filter_HidesContentsOfFoldersTheClientCannotList()
    {
        var visible = AccessFilter.Filter(Volume, path => path != @"C:\Users\other");

        visible.Select(e => e.Name).Should().Equal(@"C:\", "Users", "me", "mine.txt", "other", "public.txt");
    }

    [Fact]
    public void Filter_ResultIsAValidIndexWithCorrectPaths()
    {
        var index = new NameIndex(AccessFilter.Filter(Volume, path => path != @"C:\Users\other"));

        index.Search(SearchQuery.Parse("public"), 5).Single().FullPath.Should().Be(@"C:\public.txt");
        index.Search(SearchQuery.Parse("secret"), 5).Should().BeEmpty();
    }

    [Fact]
    public void Filter_ChecksEachDirectoryOnlyOnce()
    {
        var checkedPaths = new List<string>();

        AccessFilter.Filter(Volume, path => { checkedPaths.Add(path); return true; });

        checkedPaths.Should().OnlyHaveUniqueItems().And.HaveCount(5);
    }

    [Fact]
    public void Filter_InParallelGivesSameResultAsSequential()
    {
        var checkedPaths = new System.Collections.Concurrent.ConcurrentBag<string>();
        bool Rule(string path)
        {
            checkedPaths.Add(path);
            return path != @"C:\Users\other";
        }

        var parallel = AccessFilter.Filter(Volume, Rule, maxDegreeOfParallelism: 4);

        parallel.Should().BeEquivalentTo(AccessFilter.Filter(Volume, Rule), o => o.WithStrictOrdering());
        checkedPaths.Should().NotContain(@"C:\Users\other\Sub");
    }

    [Fact]
    public void Filter_RejectsZeroParallelism()
    {
        var act = () => AccessFilter.Filter(Volume, _ => true, maxDegreeOfParallelism: 0);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Filter_DoesNotCheckBelowADeniedFolder()
    {
        var checkedPaths = new List<string>();

        AccessFilter.Filter(Volume, path => { checkedPaths.Add(path); return path != @"C:\Users\other"; });

        checkedPaths.Should().NotContain(@"C:\Users\other\Sub");
    }
}
