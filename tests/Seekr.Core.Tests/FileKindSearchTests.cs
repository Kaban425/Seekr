using Seekr.Core.Index;
using Seekr.Core.Search;

namespace Seekr.Core.Tests;

public sealed class FileKindSearchTests
{
    [Fact]
    public void LargeFilesExcludeDriveRootsAndDirectoryAggregates()
    {
        var now = DateTime.UtcNow;
        var index = new NameIndex([
            new(0,-1,@"C:\",true,0,now,now),new(1,0,"Folder",true,0,now,now),
            new(2,1,"large.bin",false,200_000_000,now,now),new(3,1,"small.txt",false,10,now,now)]);
        var results = index.Search(SearchQuery.Parse("kind:file size:>100MB"),100);
        Assert.Equal("large.bin",Assert.Single(results).Entry.Name);
        Assert.All(results,result=>Assert.False(result.Entry.IsDirectory));
        Assert.All(index.Search(SearchQuery.Parse("kind:folder size:>100MB"),100),result=>Assert.True(result.Entry.IsDirectory));
        Assert.False(SearchQuery.Parse("kind:file").IsEmpty);
        Assert.NotNull(SearchQuery.Parse("kind:invalid").Error);
    }
}
