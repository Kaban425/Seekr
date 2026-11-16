using System.Windows;
using System.Windows.Media;

namespace Seekr.App.Services;

internal static class VisualPalette
{
    public static void Apply(bool dark)
    {
        if (Application.Current is null) return;
        Set("Canvas", dark ? "#202020" : "#FFFFFF");
        Set("WorkspaceBackground", dark ? "#202020" : "#FFFFFF");
        Set("Sidebar", dark ? "#191919" : "#F5F5F5");
        Set("MenuSurface", dark ? "#292929" : "#FAFAFA");
        Set("Surface", dark ? "#292929" : "#FAFAFA");
        Set("Hover", dark ? "#333333" : "#EEEEEE");
        Set("Pressed", dark ? "#3D3D3D" : "#E2E2E2");
        Set("Selection", dark ? "#354353" : "#DFEAF5");
        Set("InactiveSelection", dark ? "#30343A" : "#ECEFF2");
        Set("Divider", dark ? "#284A4A4A" : "#35A0A0A0");
        Set("Border", dark ? "#4A4A4A" : "#D8D8D8");
        Set("TextPrimary", dark ? "#F4F4F4" : "#202020");
        Set("TextSecondary", dark ? "#BEBEBE" : "#5B5B5B");
        Set("Focus", dark ? "#FFFFFF" : "#1A1A1A");
        Set("ErrorSurface", dark ? "#382526" : "#FCEBEC");
        Set("ErrorText", dark ? "#FFB4AB" : "#9C2328");
        Set("ErrorBorder", dark ? "#E78C86" : "#B53232");
    }

    private static void Set(string key, string color)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        brush.Freeze();
        Application.Current.Resources["Seekr." + key] = brush;
    }
}

