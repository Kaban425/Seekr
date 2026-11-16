using FluentAssertions;
using Seekr.Core.Index;
using Seekr.Core.Search;

namespace Seekr.Indexer.Tests;

public sealed class DirectoryScannerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "seekr-tests-" + Guid.NewGuid().ToString("N"));

    public DirectoryScannerTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "Games", "dota 2 beta"));
        Directory.CreateDirectory(Path.Combine(_root, "Empty"));
        File.WriteAllText(Path.Combine(_root, "Games", "dota 2 beta", "dota2.exe"), "exe");
        File.WriteAllText(Path.Combine(_root, "readme.md"), "hello");
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void Scan_ReturnsRootAndAllNestedEntries()
    {
        var entries = DirectoryScanner.Scan(_root, CancellationToken.None);

        entries.Select(e => e.Name).Should().BeEquivalentTo(
            _root, "Games", "dota 2 beta", "dota2.exe", "Empty", "readme.md");
    }

    [Fact]
    public void Scan_ProducesValidIndexWithCorrectPaths()
    {
        var index = new NameIndex(DirectoryScanner.Scan(_root, CancellationToken.None));

        var result = index.Search(SearchQuery.Parse("dota2"), limit: 10).Single();

        result.FullPath.Should().Be(Path.Combine(_root, "Games", "dota 2 beta", "dota2.exe"));
        result.Entry.IsDirectory.Should().BeFalse();
        result.Entry.Size.Should().Be(3);
    }

    [Fact]
    public void Scan_MissingRootThrows()
    {
        var act = () => DirectoryScanner.Scan(Path.Combine(_root, "nope"), CancellationToken.None);

        act.Should().Throw<DirectoryNotFoundException>();
    }

    [Fact]
    public void Scan_HonoursCancellation()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = () => DirectoryScanner.Scan(_root, cts.Token);

        act.Should().Throw<OperationCanceledException>();
    }
}
