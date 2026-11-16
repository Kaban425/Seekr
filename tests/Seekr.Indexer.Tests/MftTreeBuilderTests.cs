using FluentAssertions;
using Seekr.Core.Index;
using Seekr.Core.Search;
using Seekr.Indexer.Mft;

namespace Seekr.Indexer.Tests;

public class MftTreeBuilderTests
{
    // Real references carry a 16-bit sequence number in the top bits.
    private static ulong Ref(ulong index, ushort sequence = 1) => index | ((ulong)sequence << 48);

    [Fact]
    public void Build_ProducesIndexWithFullPaths()
    {
        MftRecord[] records =
        [
            new(Ref(11), Ref(10), "dota2.exe", false), // child listed before its parent
            new(Ref(10), Ref(MftTreeBuilder.RootIndex, 5), "Games", true),
            new(Ref(12), Ref(MftTreeBuilder.RootIndex, 5), "readme.md", false),
        ];

        var index = new NameIndex(MftTreeBuilder.Build(@"C:\", records));

        index.Count.Should().Be(4);
        index.Search(SearchQuery.Parse("dota"), 10).Single().FullPath.Should().Be(@"C:\Games\dota2.exe");
        index.Search(SearchQuery.Parse("readme"), 10).Single().FullPath.Should().Be(@"C:\readme.md");
    }

    [Fact]
    public void Build_DropsOrphansAndRootRecord()
    {
        MftRecord[] records =
        [
            new(Ref(MftTreeBuilder.RootIndex), Ref(MftTreeBuilder.RootIndex), ".", true),
            new(Ref(20), Ref(99), "orphan.txt", false),
        ];

        var entries = MftTreeBuilder.Build(@"C:\", records);

        entries.Select(e => e.Name).Should().Equal(@"C:\");
    }

    [Fact]
    public void Build_IgnoresDirectoryCycles()
    {
        MftRecord[] records =
        [
            new(Ref(30), Ref(31), "a", true),
            new(Ref(31), Ref(30), "b", true),
        ];

        MftTreeBuilder.Build(@"C:\", records).Should().ContainSingle();
    }
}
