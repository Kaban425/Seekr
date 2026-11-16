using System.IO;
using System.Text.Json;

namespace Seekr.App.Services;

public static class DiagnosticService
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    public static string DirectoryPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Seekr", "Diagnostics");
    public static string? Write(Exception exception, string context, string? directory = null)
    {
        ArgumentNullException.ThrowIfNull(exception);
        try
        {
            directory ??= DirectoryPath;
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, $"report-{DateTime.UtcNow:yyyyMMdd-HHmmssfff}-{Guid.NewGuid():N}.json");
            var details = exception.ToString();
            File.WriteAllText(path, JsonSerializer.Serialize(new
            {
                utc = DateTime.UtcNow, version = BuildInfo.Version, context,
                runtime = Environment.Version.ToString(), os = Environment.OSVersion.VersionString,
                exception = details.Length > 32000 ? details[..32000] : details,
            }, Options));
            foreach (var old in new DirectoryInfo(directory).EnumerateFiles("report-*.json").OrderByDescending(file => file.LastWriteTimeUtc).Skip(20)) old.Delete();
            return path;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        { System.Diagnostics.Debug.WriteLine("Diagnostic report could not be saved."); return null; }
    }
}
