using Seekr.App.Services;

namespace Seekr.App.Tests;

public sealed class FileLockTests
{
    [Fact]
    public void NativeInspectorFindsOwnOpenHandleButCannotCloseIt()
    {
        var path = Path.Combine(Path.GetTempPath(), "Seekr-lock-" + Guid.NewGuid().ToString("N") + ".txt");
        try
        {
            using var file = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
            var result = FileLockService.Inspect([path], default);
            var own = Assert.Single(result.Applications, x => x.ProcessId == Environment.ProcessId);
            Assert.False(own.CanClose);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task OwnProcessCannotBeClosedEvenWithForgedEligibility()
    {
        var result = await FileLockService.CloseAsync(new(Environment.ProcessId, 0, "own", true), true, default);
        Assert.Contains("нельзя", result, StringComparison.Ordinal);
    }
}
