using System.Windows;
using System.Windows.Media;
using Seekr.App.Icons;

namespace Seekr.App.Services;

/// <summary>Static vector backgrounds: no bitmap decoding, animation or per-row effects.</summary>
public static class AppearanceService
{
    public static void Apply(AppSettings settings)
    {
        if (Application.Current is not { } app) return;
        var dark = ThemeService.GetCurrentTheme() == Wpf.Ui.Appearance.ApplicationTheme.Dark;
        VisualPalette.Apply(dark);
        DesignPalette.Apply(settings.Design, dark);
        var displayFont = settings.Font == "Pixel" ? new FontFamily(new Uri("pack://application:,,,/Seekr.App;component/"), "./Assets/Fonts/#Press Start 2P") : new FontFamily(settings.Font);
        app.Resources["Seekr.TextFont"] = settings.Font == "Pixel" ? new FontFamily("Consolas") : displayFont;
        app.Resources["Seekr.CommandFont"] = displayFont;
        app.Resources["Seekr.CommandFontSize"] = settings.Font == "Pixel" ? 10.0 : 13.0;
        IconArtwork.SetStyle(settings.IconStyle, dark);
        var colors = settings.Background switch
        {
            "Aurora" => ("#173532", "#292039", "#233950"),
            "Ocean" => ("#152C42", "#213C4A", "#1C253D"),
            "Lavender" => ("#30223C", "#282B48", "#352A38"),
            "Sand" => ("#393024", "#30362A", "#372C31"),
            _ => ("#202020", "#202020", "#202020"),
        };
        Color Tint(string hex)
        {
            var c = (Color)ColorConverter.ConvertFromString(hex);
            if (!dark) c = Color.FromRgb((byte)(230 + c.R / 12), (byte)(230 + c.G / 12), (byte)(230 + c.B / 12));
            return c;
        }
        if (settings.Background != "Solid")
        {
            var brush = new LinearGradientBrush(new GradientStopCollection { new(Tint(colors.Item1), 0), new(Tint(colors.Item2), .5), new(Tint(colors.Item3), 1) }, 35);
            brush.Freeze(); app.Resources["Seekr.Canvas"] = brush;
        }
        if (WallpaperCatalog.Create(settings.Background, dark) is { } wallpaper) app.Resources["Seekr.Canvas"] = wallpaper;
        if (settings.Background == "Custom" && !string.IsNullOrWhiteSpace(settings.CustomWallpaper))
        {
            try { app.Resources["Seekr.Canvas"] = WallpaperCatalog.Custom(settings.CustomWallpaper, dark); }
            catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException or FormatException) { System.Diagnostics.Trace.TraceWarning("Wallpaper unavailable: {0}", ex.GetType().Name); }
        }
        app.Resources["Seekr.WorkspaceBackground"] = app.Resources["Seekr.Canvas"];
    }
}
