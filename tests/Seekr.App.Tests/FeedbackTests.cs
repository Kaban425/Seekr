using Seekr.App.Services;

namespace Seekr.App.Tests;

public sealed class FeedbackTests
{
    [Fact]
    public void DiagnosticExportDoesNotContainUserPathsOrExceptionHistory()
    {
        var json = FeedbackService.BuildDiagnostics();
        Assert.Contains("version", json, StringComparison.Ordinal);
        Assert.DoesNotContain(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("exception", json, StringComparison.OrdinalIgnoreCase);
        using var document = System.Text.Json.JsonDocument.Parse(json);
        Assert.Equal("manual-export", document.RootElement.GetProperty("kind").GetString());
    }

    [Fact]
    public void IssueLinkEscapesUserInputAndStaysOnConfiguredRepository()
    {
        var uri = FeedbackService.BuildIssueUri("bug & labels=admin", "a?b#c", "ожидал", "получил");
        Assert.Equal("github.com", uri.Host);
        Assert.Equal("/Kaban425/Seekr-Feedback/issues/new", uri.AbsolutePath);
        Assert.Contains("%26", uri.Query, StringComparison.Ordinal);
        Assert.True(uri.AbsoluteUri.Length < 8000);
        var longUri = FeedbackService.BuildIssueUri(new string('x', 20000), new string('я', 20000), "", "");
        Assert.True(longUri.AbsoluteUri.Length < 8000);
    }
}
