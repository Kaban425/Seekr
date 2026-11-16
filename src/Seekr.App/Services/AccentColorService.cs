using System.Globalization;
using System.Windows.Media;
using Wpf.Ui.Appearance;

namespace Seekr.App.Services;

public static class AccentColorService
{
    public static void ApplyCurrentAccentColor()
    {
        var settings = SettingsService.Load();
        var theme = ThemeService.GetCurrentTheme();

        if (settings.AccentColor == "System" || string.IsNullOrWhiteSpace(settings.AccentColor))
        {
            ApplicationAccentColorManager.ApplySystemAccent();
            return;
        }

        if (TryParseColor(settings.AccentColor, out var color))
        {
            ApplicationAccentColorManager.Apply(color, theme, systemAccentColor: false);
        }
    }

    private static bool TryParseColor(string hex, out Color color)
    {
        try
        {
            color = (Color)ColorConverter.ConvertFromString(hex.StartsWith('#') ? hex : "#" + hex);
            return true;
        }
        catch (FormatException)
        {
            color = default;
            return false;
        }
    }
}

