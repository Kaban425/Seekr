using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Seekr.App.Icons;

/// <summary>Theme-aware vector action icon, drawn without an icon font.</summary>
public sealed class SeekrIcon : FrameworkElement
{
    public static readonly DependencyProperty KindProperty = DependencyProperty.Register(nameof(Kind), typeof(IconKind), typeof(SeekrIcon),
        new FrameworkPropertyMetadata(IconKind.None, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty ForegroundProperty = Control.ForegroundProperty.AddOwner(typeof(SeekrIcon),
        new FrameworkPropertyMetadata(Brushes.Black, FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsRender));

    public IconKind Kind { get => (IconKind)GetValue(KindProperty); set => SetValue(KindProperty, value); }
    public Brush Foreground { get => (Brush)GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }

    protected override Size MeasureOverride(Size availableSize) => new(Math.Min(16, availableSize.Width), Math.Min(16, availableSize.Height));

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        var size = Math.Min(ActualWidth, ActualHeight);
        if (size <= 0 || Kind == IconKind.None) return;
        drawingContext.PushTransform(new TranslateTransform((ActualWidth - size) / 2, (ActualHeight - size) / 2));
        drawingContext.PushTransform(new ScaleTransform(size / 24, size / 24));
        if(IconArtwork.Style=="Glass" && GlassIconAtlas.Supports(Kind)) drawingContext.DrawDrawing(IconArtwork.Image(Kind).Drawing);
        else drawingContext.DrawGeometry(null, IconArtwork.Pen(Foreground), IconArtwork.Geometry(Kind));
        drawingContext.Pop(); drawingContext.Pop();
    }
}
