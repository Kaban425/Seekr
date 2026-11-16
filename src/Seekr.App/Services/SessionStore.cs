using System.IO;
using System.Text.Json;

namespace Seekr.App.Services;

public sealed record WorkspaceSession(string? Path, string Query, bool AllDisks, bool Contents, bool SpaceMap, string Sort, bool Ascending, double Scroll);
public sealed record ShellSession(WorkspaceSession[] Tabs, int Selected, WorkspaceSession? Right, bool DualPane, bool RightActive, bool CleanShutdown = true);
public static class SessionStore
{
    private static readonly string SessionPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Seekr", "session.json");
    private static readonly object Sync = new();
    public static ShellSession? Load(string? filePath = null)
    {
        var path = filePath ?? SessionPath;
        return Read(path) ?? Read(path + ".bak");
    }
    private static ShellSession? Read(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            if (new FileInfo(path).Length > 1024 * 1024) return null;
            var session = JsonSerializer.Deserialize<ShellSession>(File.ReadAllText(path));
            return session is { Tabs.Length: > 0 and <= 40 } && session.Tabs.All(x => x is not null) ? session : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return null; }
    }
    public static bool Save(ShellSession session, string? filePath = null)
    {
        var path = filePath ?? SessionPath;
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            File.WriteAllText(temp, JsonSerializer.Serialize(session));
            lock (Sync)
            {
                if (Read(path) is not null) File.Replace(temp, path, path + ".bak", ignoreMetadataErrors: true);
                else File.Move(temp, path, overwrite: true);
            }
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { System.Diagnostics.Trace.TraceWarning("Session save failed: {0}", ex.Message); return false; }
        finally
        {
            try { if (File.Exists(temp)) File.Delete(temp); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { System.Diagnostics.Trace.TraceWarning("Session temporary file cleanup failed."); }
        }
    }
}
