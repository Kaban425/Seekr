using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace Seekr.App.Services;

public static class FeedbackService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    public const string RepositoryUrl = "https://github.com/Kaban425/Seekr-Feedback";
    public static string BuildDiagnostics() => JsonSerializer.Serialize(new
    {
        kind = "manual-export", utc = DateTime.UtcNow, version = BuildInfo.Version,
        os = Environment.OSVersion.VersionString, architecture = RuntimeInformation.OSArchitecture.ToString(),
        runtime = RuntimeInformation.FrameworkDescription, process64Bit = Environment.Is64BitProcess,
    }, JsonOptions);

    public static Uri BuildIssueUri(string title, string steps, string expected, string actual)
    {
        static string Limit(string value, int count) => value.Length <= count ? value : value[..count] + "…";
        // Unicode expands substantially in a URL; retain a small, portable draft.
        var body = $"Версия: {BuildInfo.Version}\nWindows: {Environment.OSVersion.Version}\n\nДействия:\n{Limit(steps, 300)}\n\nОжидалось:\n{Limit(expected, 180)}\n\nПолучилось:\n{Limit(actual, 300)}\n\nПроверьте текст перед отправкой. Диагностика прикрепляется вручную, если нужна.";
        return new Uri(RepositoryUrl + "/issues/new?title=" + Uri.EscapeDataString(Limit(title, 100)) + "&body=" + Uri.EscapeDataString(body));
    }

    public static void Open(Uri uri)
    {
        if (uri.Scheme != Uri.UriSchemeHttps || uri.Host != "github.com") throw new ArgumentException("Unsupported feedback URL.", nameof(uri));
        using var process = Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
    }
}
