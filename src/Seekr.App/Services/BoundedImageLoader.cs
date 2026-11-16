using System.IO;
using System.Windows.Media.Imaging;

namespace Seekr.App.Services;

internal static class BoundedImageLoader
{
    public static BitmapImage Load(string path, int maxSide, long maxFileBytes, CancellationToken token = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxSide);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (stream.Length > maxFileBytes) throw new IOException("Изображение слишком велико для предпросмотра.");
        token.ThrowIfCancellationRequested();
        var frame = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None).Frames[0];
        var width = frame.PixelWidth; var height = frame.PixelHeight;
        if (width <= 0 || height <= 0 || (long)width * height > 80_000_000)
            throw new IOException("Для предпросмотра поддерживаются изображения до 80 мегапикселей.");
        stream.Position = 0;
        var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad;
        if (Math.Max(width, height) > maxSide)
        {
            if (width >= height) image.DecodePixelWidth = maxSide;
            else image.DecodePixelHeight = maxSide;
        }
        image.StreamSource = stream; image.EndInit(); image.Freeze();
        token.ThrowIfCancellationRequested();
        return image;
    }
}
