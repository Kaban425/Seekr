using FluentAssertions;
using Seekr.Indexer.Service;

namespace Seekr.Indexer.Tests;

public class DirectoryAccessTests
{
    [Theory]
    [InlineData(@"C:\", @"\\?\C:\")]
    [InlineData(@"C:\Users\secret.", @"\\?\C:\Users\secret.")]
    [InlineData(@"\\?\C:\a", @"\\?\C:\a")]
    public void ToExtendedPath_AlwaysDisablesWin32Normalisation(string path, string expected)
    {
        DirectoryAccess.ToExtendedPath(path).Should().Be(expected);
    }

    [Fact]
    public void CanList_ReturnsTrueForReadableFolder()
    {
        DirectoryAccess.CanList(Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar)).Should().BeTrue();
    }

    [Fact]
    public void CanList_ReturnsFalseForMissingFolder()
    {
        DirectoryAccess.CanList(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))).Should().BeFalse();
    }

    [Fact]
    public void CanList_DoesNotTreatTrailingDotAsSameFolder()
    {
        var real = Path.Combine(Path.GetTempPath(), "seekr-dot-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(real);
        try
        {
            DirectoryAccess.CanList(real + ".").Should().BeFalse();
        }
        finally
        {
            Directory.Delete(real);
        }
    }
}
