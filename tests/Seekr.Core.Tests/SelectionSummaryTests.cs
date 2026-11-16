using FluentAssertions;
using Seekr.Core.Index;

namespace Seekr.Core.Tests;

public sealed class SelectionSummaryTests
{
    private static SearchResult Item(string path, bool folder = false) => new(
        new FileEntry(0, -1, path, folder, 10, DateTime.UtcNow, DateTime.UtcNow), path, 10);

    [Fact]
    public void ParentAndChildAreCountedOnce()
    {
        SelectionSummary.Roots([Item(@"C:\Docs", true), Item(@"C:\Docs\a.txt"), Item(@"C:\Docs\More", true)])
            .Select(x => x.FullPath).Should().Equal(@"C:\Docs");
    }

    [Fact]
    public void PrefixSiblingIsNotAChild()
    {
        SelectionSummary.Roots([Item(@"C:\Docs", true), Item(@"C:\Docs-old\a.txt")]).Should().HaveCount(2);
    }

    [Fact]
    public void RootsCaseAndDuplicatePathsAreHandled()
    {
        SelectionSummary.Roots([Item(@"C:\", true), Item(@"c:\Docs\a.txt"), Item(@"C:\", true)])
            .Select(x => x.FullPath).Should().Equal(@"C:\");
    }
}
