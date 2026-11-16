using System.Windows;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace Seekr.App.Services;

public static class ThemeService
{
    public static void ApplyCurrentTheme()
    {
        var settings = SettingsService.Load();
        ApplyTheme(settings.Theme);
    }

    public static void ApplyTheme(string themeName)
    {
        var theme = Map(themeName);
        if (theme == ApplicationTheme.Unknown)
        {
            var system = ApplicationThemeManager.GetSystemTheme();
            theme = system switch
            {
                SystemTheme.Dark or SystemTheme.Glow or SystemTheme.CapturedMotion or SystemTheme.Flow => ApplicationTheme.Dark,
                _ => ApplicationTheme.Light,
            };
        }

        ApplicationThemeManager.Apply(theme, WindowBackdropType.None, updateAccent: true);
        VisualPalette.Apply(theme == ApplicationTheme.Dark);
    }

    public static ApplicationTheme GetCurrentTheme()
    {
        var current = ApplicationThemeManager.GetAppTheme();
        return current == ApplicationTheme.Unknown ? ApplicationTheme.Dark : current;
    }

    private static ApplicationTheme Map(string theme) => theme switch
    {
        "Dark" => ApplicationTheme.Dark,
        "Light" => ApplicationTheme.Light,
        _ => ApplicationTheme.Unknown,
    };
}
