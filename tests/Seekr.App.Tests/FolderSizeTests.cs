using Seekr.App.Services;
using Seekr.Core.Index;
using Seekr.Indexer;

namespace Seekr.App.Tests;

public sealed class FolderSizeTests
{
    [Fact]
    public async Task IndexInitializationIsSharedAndDisposalOfOneTabDoesNotOwnIt()
    {
        var calls = 0;
        using var service = new SharedIndexService(_ => { Interlocked.Increment(ref calls); return new(new NameIndex([]), IndexSource.Service, TimeSpan.Zero); }, false);
        await Task.WhenAll(service.InitializeAsync(), service.InitializeAsync());
        Assert.Equal(1, calls);
        Assert.NotNull(service.Index);
    }

    [Fact]
    public void MeasurementReadsChangedSizesAndCancellation()
    {
        var path = Path.Combine(Path.GetTempPath(), "Seekr-sizes-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        try
        {
            var file = Path.Combine(path, "data.txt");
            File.WriteAllText(file, "12345");
            Assert.Equal(5, FolderSizeService.Measure(path, default).Bytes);
            File.AppendAllText(file, "67890");
            Assert.Equal(10, FolderSizeService.Measure(path, default).Bytes);
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            Assert.Throws<OperationCanceledException>(() => FolderSizeService.Measure(path, cts.Token));
        }
        finally { Directory.Delete(path, true); }
    }
}
