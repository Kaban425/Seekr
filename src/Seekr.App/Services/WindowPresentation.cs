using System.Windows;
using System.Windows.Controls;

namespace Seekr.App.Services;

/// <summary>Shared content styling for programmatically created dialogs. Native captions stay unscaled.</summary>
public static class WindowPresentation
{
    public static void ApplyDialog(Window window, FrameworkElement content)
    {
        window.SetResourceReference(Control.ForegroundProperty, "Seekr.TextPrimary");
        window.SetResourceReference(Control.BackgroundProperty, "Seekr.Canvas");
        window.SetResourceReference(Control.FontFamilyProperty, "Seekr.TextFont");
        window.FontSize = 13;
        // FluentWindow defaults impose a large minimum even on short prompts.
        window.MinWidth = 320;
        window.MinHeight = 0;
        window.UseLayoutRounding = true;
        if (ReferenceEquals(window.Content, content)) window.Content = null;
        var viewport = new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto };
        viewport.SetResourceReference(FrameworkElement.LayoutTransformProperty, "Seekr.ContentScale");
        window.Content = viewport;
        ConstrainToWorkArea(window);
    }

    public static void ConstrainToWorkArea(Window window)
    {
        var area = SystemParameters.WorkArea;
        var width = Math.Max(240, area.Width - 24);
        var height = Math.Max(200, area.Height - 24);
        window.MinWidth = Math.Min(window.MinWidth, width);
        window.MinHeight = Math.Min(window.MinHeight, height);
        window.MaxWidth = width;
        window.MaxHeight = height;
        if (double.IsFinite(window.Width)) window.Width = Math.Min(window.Width, width);
        if (double.IsFinite(window.Height)) window.Height = Math.Min(window.Height, height);
    }
}
