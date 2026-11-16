using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Seekr.App.Icons;

internal static class GlassIconAtlas
{
    private static readonly Lazy<BitmapImage> Atlas = new(() =>
    {
        var image=new BitmapImage(); image.BeginInit();
        image.UriSource=new Uri("pack://application:,,,/Seekr.App;component/Assets/Icons/dark-glass-atlas.png");
        image.CacheOption=BitmapCacheOption.OnLoad; image.DecodePixelWidth=512; image.EndInit(); image.Freeze(); return image;
    });
    public static bool Supports(IconKind kind)=>Cell(kind)>=0;
    private static int Cell(IconKind kind)=>kind switch
        {
            IconKind.Folder=>0,IconKind.Drive=>1,IconKind.File or IconKind.Document or IconKind.Pdf or IconKind.Application=>2,
            IconKind.Picture=>3,IconKind.Music=>4,IconKind.Video=>5,IconKind.Archive=>6,IconKind.Code=>7,
            IconKind.Home=>8,IconKind.Download=>9,IconKind.Person=>10,IconKind.Bookmark=>11,IconKind.Search=>12,IconKind.Settings=>13,IconKind.Delete=>14,IconKind.Copy or IconKind.Paste=>15,_=>-1,
        };
    public static DrawingImage? Image(IconKind kind)
    {
        var cell=Cell(kind);
        if(cell<0) return null;
        var atlas=Atlas.Value; var width=atlas.PixelWidth/4; var height=atlas.PixelHeight/4;
        var crop=new CroppedBitmap(atlas,new Int32Rect(cell%4*width,cell/4*height,width,height)); crop.Freeze();
        var image=new DrawingImage(new ImageDrawing(crop,new Rect(0,0,24,24))); image.Freeze(); return image;
    }
}
