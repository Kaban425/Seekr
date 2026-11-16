using System.IO;
using System.Text.Json;

namespace Seekr.App.Services;

public static class SettingsService
{
    private static readonly object PublicationLock = new();
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Seekr", "settings.json");

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static event EventHandler? Changed;
    internal static void NotifyChanged() => Changed?.Invoke(null, EventArgs.Empty);

    public static AppSettings Load(string? filePath = null)
    {
        var path = filePath ?? SettingsPath;
        try
        {
            if (File.Exists(path))
            {
                using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
                using var reader = new StreamReader(file);
                var json = reader.ReadToEnd();
                var settings = JsonSerializer.Deserialize<AppSettings>(json, Options);
                if (settings is not null)
                {
                    return Sanitize(settings);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // Fall through to defaults.
        }

        return new AppSettings();
    }

    public static bool Save(AppSettings settings, string? filePath = null, bool notify = true)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings = Sanitize(settings);
        var path = filePath ?? SettingsPath;
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            File.WriteAllText(temporary, JsonSerializer.Serialize(settings, Options));
            lock (PublicationLock) File.Move(temporary, path, overwrite: true);
            if (filePath is null && notify) Changed?.Invoke(null, EventArgs.Empty);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { System.Diagnostics.Trace.TraceWarning("Settings temporary file cleanup failed."); }
        }
    }

    public static string? Update(Func<AppSettings, string?> change, string? filePath = null)
    {
        lock (PublicationLock)
        {
            var settings = Load(filePath);
            var error = change(settings);
            if (error is not null) return error;
            if (!Save(settings, filePath, notify: false)) return "Не удалось сохранить настройки.";
        }
        if (filePath is null) Changed?.Invoke(null, EventArgs.Empty);
        return null;
    }

    private static AppSettings Sanitize(AppSettings settings)
    {
        if (settings.Design == "DarkGlass") settings.Design = "Graphite";
        if (settings.Design is not ("Default" or "Polar" or "Velvet" or "Rose" or "Arcade" or "Sakura" or "Terminal" or "Studio" or "Lagoon" or "Blueprint" or "Paper" or "Graphite")) settings.Design = "Default";
        if (settings.Font is not ("Segoe UI" or "Calibri" or "Arial" or "Verdana" or "Consolas" or "Georgia" or "Pixel")) settings.Font = "Segoe UI";
        if (settings.Background is not ("Solid" or "Aurora" or "Ocean" or "Lavender" or "Sand" or "PhotoCoupe" or "PhotoLake" or "PhotoRally" or "PhotoGarden" or "PhotoSnow" or "PhotoPolar" or "PhotoVelvet" or "PhotoRose" or "Custom" or "PhotoArcade" or "PhotoSakura" or "PhotoBlueprint" or "PhotoPaper")) settings.Background = "Solid";
        if (settings.IconStyle is not ("Pastel" or "Outline" or "Mono" or "Pixel" or "Anime" or "Rune" or "Tactical" or "Neon" or "Blueprint" or "Paper" or "Minimal" or "Polar" or "Velvet" or "Rose" or "Glass")) settings.IconStyle = "Pastel";
        settings.CustomAccent ??= "";
        settings.CustomWallpaper ??= "";
        settings.HiddenNavigationPaths ??= [];
        settings.SavedSearches = (settings.SavedSearches ?? []).Where(s => s is not null && !string.IsNullOrWhiteSpace(s.Name) && !string.IsNullOrWhiteSpace(s.Query)).Take(100).ToList();
        settings.PinnedFolders ??= [];
        settings.ColumnWidths = (settings.ColumnWidths ?? []).Where(x => double.IsFinite(x.Value)).ToDictionary(x => x.Key, x => Math.Clamp(x.Value, 48, 1000));
        settings.HiddenColumns ??= [];
        settings.ColumnOrder ??= [];
        settings.SearchResultLimit = Math.Clamp(settings.SearchResultLimit, 100, 10000);
        settings.AutoRefreshIntervalSeconds = Math.Clamp(settings.AutoRefreshIntervalSeconds, 5, 300);
        settings.UiScale = double.IsFinite(settings.UiScale) ? Math.Clamp(settings.UiScale, 0.75, 1.5) : 1.0;
        settings.SidebarWidth = double.IsFinite(settings.SidebarWidth) ? Math.Clamp(settings.SidebarWidth, 180, 400) : 260;
        settings.Theme = settings.Theme switch
        {
            "Dark" or "Light" or "System" => settings.Theme,
            _ => "System",
        };
        return settings;
    }
}


