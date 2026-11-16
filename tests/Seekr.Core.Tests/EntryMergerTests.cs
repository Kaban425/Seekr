using FluentAssertions;
using Seekr.Core.Index;
using Seekr.Core.Search;

namespace Seekr.Core.Tests;

public class EntryMergerTests
{
    private static readonly DateTime Time = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Concat_RenumbersSecondVolumeAndKeepsPaths()
    {
        FileEntry[] c = [new(0, FileEntry.NoParent, @"C:\", true, 0, Time, Time), new(1, 0, "a.txt", false, 1, Time, Time)];
        FileEntry[] d = [new(0, FileEntry.NoParent, @"D:\", true, 0, Time, Time), new(1, 0, "Games", true, 0, Time, Time), new(2, 1, "b.txt", false, 2, Time, Time)];

        var index = new NameIndex(EntryMerger.Concat([c, d]));

        index.Count.Should().Be(5);
        index.Search(SearchQuery.Parse("b.txt"), 10).Single().FullPath.Should().Be(@"D:\Games\b.txt");
        index.Search(SearchQuery.Parse("a.txt"), 10).Single().FullPath.Should().Be(@"C:\a.txt");
    }

    [Fact]
    public void Concat_NoVolumesGivesEmptyList()
    {
        EntryMerger.Concat([]).Should().BeEmpty();
    }
}
