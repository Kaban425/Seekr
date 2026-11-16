using System.Windows;
using System.Windows.Media;

namespace Seekr.App.Services;

public static class ScaleService
{
    public static void ApplyCurrentScale()
    {
        var settings = SettingsService.Load();
        ApplyScale(settings.UiScale);
    }

    public static void ApplyScale(double scale)
    {
        if (Application.Current is null) return;
        scale = double.IsFinite(scale) ? Math.Clamp(scale, 0.75, 1.5) : 1.0;
        // Only app content scales; native caption controls keep their OS metrics.
        Application.Current.Resources["Seekr.UiScale"] = scale;
        var transform = new ScaleTransform(scale, scale);
        transform.Freeze();
        Application.Current.Resources["Seekr.ContentScale"] = transform;
    }
}
