using System.IO.Compression;
using System.Text;
using Seekr.App.Services;
using Seekr.Core.Search;

namespace Seekr.App.Tests;

public sealed class ContentSearchTests : IDisposable
{
    private readonly string _root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "Seekr-content-" + Guid.NewGuid().ToString("N"))).FullName;
    [Fact]
    public async Task FindsTermsInsideTextAndDocxRatherThanFileNames()
    {
        await File.WriteAllTextAsync(Path.Combine(_root, "one.txt"), "contract marker");
        await File.WriteAllTextAsync(Path.Combine(_root, "russian.txt"), "Договор аренды", Encoding.Unicode);
        using (var zip = ZipFile.Open(Path.Combine(_root, "two.docx"), ZipArchiveMode.Create))
        using (var writer = new StreamWriter(zip.CreateEntry("word/document.xml").Open()))
            writer.Write("<w:document xmlns:w='http://schemas.openxmlformats.org/wordprocessingml/2006/main'><w:p><w:r><w:t>con</w:t></w:r><w:r><w:t>tract marker</w:t></w:r></w:p></w:document>");
        var request = new WorkspaceSearchRequest(SearchQuery.Parse("contract marker"), _root, _root, null, 500, SearchOrder.Name, false, true, true, SearchContents: true);
        var found = await WorkspaceSearchService.ExecuteAsync(request, CancellationToken.None);
        Assert.Equal(2, found.Entries.Count);
        var russian = await WorkspaceSearchService.ExecuteAsync(request with { Query = SearchQuery.Parse("договор") }, CancellationToken.None);
        Assert.Equal("russian.txt", Assert.Single(russian.Entries).Entry.Name);
    }
    [Fact]
    public async Task ReportsSkippedBinaryAndMalformedDocsAndSupportsCancellation()
    {
        await File.WriteAllBytesAsync(Path.Combine(_root, "binary.txt"), [0, 1, 2]);
        using (var zip = ZipFile.Open(Path.Combine(_root, "bad.docx"), ZipArchiveMode.Create))
        using (var writer = new StreamWriter(zip.CreateEntry("word/document.xml").Open()))
            writer.Write("<!DOCTYPE document [<!ENTITY test SYSTEM 'file:///must-not-read'>]><document>&test;</document>");
        var request = new WorkspaceSearchRequest(SearchQuery.Parse("anything"), _root, _root, null, 500, SearchOrder.Name, false, true, true, SearchContents: true);
        var found = await WorkspaceSearchService.ExecuteAsync(request, CancellationToken.None);
        Assert.Empty(found.Entries); Assert.Contains("пропущено 2", found.Notice, StringComparison.Ordinal);
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => WorkspaceSearchService.ExecuteAsync(request, cancellation.Token));
    }
    public void Dispose() => Directory.Delete(_root, true);
}
