using Seekr.App.Services;

namespace Seekr.App.Tests;

public sealed class DirectoryBrowserTests
{
    [Fact]
    public void BrowseDoesNotNeedAnIndexAndReadsCurrentMetadata()
    {
        var path = Path.Combine(Path.GetTempPath(), "Seekr-browser-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        try
        {
            File.WriteAllText(Path.Combine(path, "note.txt"), "hello");
            Directory.CreateDirectory(Path.Combine(path, "Folder"));
            var result = DirectoryBrowser.Read(path, true, true, default);
            Assert.Null(result.Error);
            Assert.Equal(2, result.Entries.Count);
            Assert.Equal(5, result.Entries.Single(x => x.Entry.Name == "note.txt").EffectiveSize);
            Assert.Equal(-1, result.Entries.Single(x => x.Entry.IsDirectory).EffectiveSize);
        }
        finally { Directory.Delete(path, true); }
    }

    [Fact]
    public void MissingFolderProducesAnExplanation()
    {
        var result = DirectoryBrowser.Read(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")), true, true, default);
        Assert.NotNull(result.Error);
        Assert.Empty(result.Entries);
    }
}
