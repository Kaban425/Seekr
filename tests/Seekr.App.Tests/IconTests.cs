using Seekr.App.Icons;
using Seekr.App.Utils;

namespace Seekr.App.Tests;

public sealed class IconTests
{
    [Fact]
    public void FileTypeDescriptionsRetainFormatAndShortcutMeaning()
    {
        Assert.Equal("Ярлык", FilePresentation.GetTypeName(false, "lnk"));
        Assert.Equal("Библиотека DLL", FilePresentation.GetTypeName(false, "dll"));
        Assert.Equal("Архив ZIP", FilePresentation.GetTypeName(false, "zip"));
        Assert.Equal("Документ DOCX", FilePresentation.GetTypeName(false, "docx"));
    }
    [Fact]
    public void EveryNamedIconHasFrozenOriginalGeometryInsideTheGrid()
    {
        foreach (var kind in Enum.GetValues<IconKind>().Where(x => x != IconKind.None))
        {
            var image = IconArtwork.Image(kind);
            Assert.True(image.IsFrozen);
            Assert.False(image.Drawing.Bounds.IsEmpty);
            Assert.InRange(image.Drawing.Bounds.Left, 0, 24);
            Assert.InRange(image.Drawing.Bounds.Top, 0, 24);
            Assert.InRange(image.Drawing.Bounds.Right, 0, 24);
            Assert.InRange(image.Drawing.Bounds.Bottom, 0, 24);
        }
    }

    [Theory]
    [InlineData("PDF", IconKind.Pdf)]
    [InlineData("jpg", IconKind.Picture)]
    [InlineData("mp3", IconKind.Music)]
    [InlineData("zip", IconKind.Archive)]
    [InlineData("cs", IconKind.Code)]
    [InlineData("unregistered-extension", IconKind.File)]
    public void FileTypesUseSeekrCategoriesInsteadOfInstalledApplicationLogos(string extension, IconKind kind)
    {
        Assert.Same(IconArtwork.Image(kind), FilePresentation.GetIcon(false, extension));
    }

    [Fact]
    public async Task SharedIconsAreSafeAcrossThreadsAndUnknownExtensionsReuseOneImage()
    {
        var results = await Task.WhenAll(Enumerable.Range(0, 40).Select(i => Task.Run(() => FilePresentation.GetIcon(false, "unknown" + i))));
        Assert.All(results, icon => Assert.Same(IconArtwork.Image(IconKind.File), icon));
        Assert.Same(IconArtwork.Image(IconKind.Drive), FilePresentation.GetPathIcon(@"C:\"));
        Assert.Same(IconArtwork.Image(IconKind.Folder), FilePresentation.GetPathIcon(@"C:\Docs"));
    }
}
