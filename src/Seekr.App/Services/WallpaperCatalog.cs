using System.Collections.Concurrent;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Seekr.App.Services;

public static class WallpaperCatalog
{
    public static IReadOnlyList<string> Names { get; } = ["PhotoCoupe", "PhotoLake", "PhotoRally", "PhotoGarden", "PhotoSnow", "PhotoPolar", "PhotoVelvet", "PhotoRose", "PhotoArcade", "PhotoSakura", "PhotoBlueprint", "PhotoPaper"];
    private static readonly ConcurrentDictionary<string, BitmapImage> Images = new(StringComparer.Ordinal);
    private static string? _customKey;
    private static BitmapImage? _customImage;
    public static Brush Custom(string path, bool dark)
    {
        var info = new System.IO.FileInfo(path);
        if (!info.Exists || info.Length > 20L * 1024 * 1024) throw new System.IO.IOException("Нужен доступный файл до 20 МБ.");
        var key = info.FullName + info.Length + info.LastWriteTimeUtc.Ticks;
        if (_customKey != key || _customImage is null)
        {
            var image = BoundedImageLoader.Load(path, 1280, 20L * 1024 * 1024);
            _customImage = image; _customKey = key;
        }
        return Compose(_customImage, dark);
    }
    private static DrawingBrush Compose(BitmapImage image, bool dark)
    {
        var bounds = new Rect(0, 0, image.PixelWidth, image.PixelHeight);
        var group = new DrawingGroup(); group.Children.Add(new ImageDrawing(image, bounds));
        group.Children.Add(new GeometryDrawing(new SolidColorBrush(dark ? Color.FromArgb(205, 18, 22, 30) : Color.FromArgb(226, 255, 255, 255)), null, new RectangleGeometry(bounds)));
        var brush = new DrawingBrush(group) { Stretch = Stretch.UniformToFill }; brush.Freeze(); return brush;
    }
    public static Brush? Create(string name, bool dark)
    {
        if (!Names.Contains(name)) return null;
        var image = Images.GetOrAdd(name, key =>
        {
            var bitmap = new BitmapImage(); bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.DecodePixelWidth = 1280;
            bitmap.UriSource = new Uri($"pack://application:,,,/Seekr.App;component/Assets/Wallpapers/{key}.png");
            bitmap.EndInit(); bitmap.Freeze(); return bitmap;
        });
        var bounds = new Rect(0, 0, image.PixelWidth, image.PixelHeight);
        var group = new DrawingGroup();
        group.Children.Add(new ImageDrawing(image, bounds));
        var overlay = new SolidColorBrush(dark ? Color.FromArgb(205, 18, 22, 30) : Color.FromArgb(226, 255, 255, 255));
        group.Children.Add(new GeometryDrawing(overlay, null, new RectangleGeometry(bounds)));
        var brush = new DrawingBrush(group) { Stretch = Stretch.UniformToFill };
        brush.Freeze(); return brush;
    }
}


