using System.Collections.Concurrent;
using System.Windows;
using System.Windows.Media;

namespace Seekr.App.Icons;

/// <summary>Original Seekr paths on a 24-unit grid. No shell extraction, font glyphs or external icon assets.</summary>
public static class IconArtwork
{
    private static readonly ConcurrentDictionary<IconKind, DrawingImage> Images = new();
    private static readonly Dictionary<IconKind, Geometry> Paths = BuildPaths();

    public static string Style { get; private set; } = "Pastel";
    private static bool _dark;
    public static void SetStyle(string style, bool dark = false) { if (Style == style && _dark == dark) return; Style = style; _dark = dark; Images.Clear(); }
    public static Geometry Geometry(IconKind kind) => Paths.GetValueOrDefault(kind) ?? System.Windows.Media.Geometry.Empty;

    public static DrawingImage Image(IconKind kind) => Images.GetOrAdd(kind, CreateImage);

    public static Pen Pen(Brush brush) => new(brush, Style == "Pixel" ? 2 : 1.65) { StartLineCap = Style == "Pixel" ? PenLineCap.Square : PenLineCap.Round, EndLineCap = Style == "Pixel" ? PenLineCap.Square : PenLineCap.Round, LineJoin = Style == "Pixel" ? PenLineJoin.Miter : PenLineJoin.Round };

    private static DrawingImage CreateImage(IconKind kind)
    {
        if(Style=="Glass" && GlassIconAtlas.Image(kind) is { } glass) return glass;
        var group = new DrawingGroup();
        // Transparent bounds keep every symbol optically aligned and equally sized.
        group.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new Rect(0, 0, 24, 24))));
        var (fill, line) = kind switch
        {
            IconKind.Folder or IconKind.Archive => ("#F1D29A", "#886529"),
            IconKind.Drive or IconKind.Desktop => ("#CCE4E9", "#397285"),
            IconKind.Picture or IconKind.Download => ("#D7EADD", "#417A5E"),
            IconKind.Music => ("#E9DDF1", "#7E5B93"),
            IconKind.Video or IconKind.Application => ("#DDE4F3", "#576D99"),
            IconKind.Pdf => ("#F5DDD5", "#9B5945"),
            IconKind.Code => ("#D7EAE5", "#3D7A6D"),
            _ => ("#E0E7EE", "#55738D"),
        };
        if (Style == "Mono") { fill = _dark ? "#323E50" : "#DCE2EB"; line = _dark ? "#C7D4E5" : "#36516D"; }
        if (Style == "Outline") line = _dark ? "#BDD3E9" : "#36516D";
        (fill, line) = Style switch
        {
            "Glass" => (_dark ? "#253747" : "#E4F1F7", _dark ? "#B6D6EA" : "#385C72"),
            "Polar" => (_dark ? "#304156" : "#DAEAF3", _dark ? "#A8D4E3" : "#355D73"),
            "Velvet" => (_dark ? "#373246" : "#E8DCF7", _dark ? "#C3A4F0" : "#654080"),
            "Rose" => (_dark ? "#443544" : "#F4DEEC", _dark ? "#F1BBDA" : "#854269"),
            "Pixel" => ("#EACD66", "#65542E"),
            "Anime" => ("#F8DBEB", "#A84180"),
            "Rune" => ("#4C3430", "#F2D196"),
            "Tactical" => ("#303D38", "#BFD394"),
            "Neon" => ("#26364C", "#70E8F0"),
            "Blueprint" => ("#203D60", "#B9DFFF"),
            "Paper" => ("#F4E5C9", "#765A43"),
            "Minimal" => (_dark ? "#303642" : "#E6EBF0", _dark ? "#DCE6F2" : "#34465E"),
            _ => (fill, line),
        };
        if (Style is "Pixel" or "Anime" or "Paper" && _dark)
        {
            line = Style == "Anime" ? "#FFC2E5" : "#F8E8B9";
            fill = Style switch { "Pixel" => "#514523", "Anime" => "#4D2944", _ => "#463B2C" };
        }
        var background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(fill));
        var foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(line));
        if (kind is IconKind.Folder or IconKind.Archive or IconKind.Drive or IconKind.File or IconKind.Document
            or IconKind.Pdf or IconKind.Picture or IconKind.Music or IconKind.Video or IconKind.Code or IconKind.Application)
        {
            var body = kind switch
            {
                IconKind.Folder => "M3,7 L3,5 Q3,3.5 4.5,3.5 L9,3.5 L11,6 L19.5,6 Q21,6 21,7.5 L20,19 Q20,20.5 18.5,20.5 L4.5,20.5 Q3,20.5 3,19 Z",
                IconKind.Drive => "M5,4 L19,4 L21,14 L21,19 Q21,20 20,20 L4,20 Q3,20 3,19 L3,14 Z",
                IconKind.Archive => "M4,5 L20,5 L20,20 L4,20 Z",
                _ => "M5,3 L15,3 L20,8 L20,21 L5,21 Z",
            };
            if (Style == "Pixel") body = kind == IconKind.Folder ? "M2,6 L2,4 L9,4 L9,6 L21,6 L21,20 L2,20 Z" : kind == IconKind.Drive ? "M4,4 L20,4 L20,20 L4,20 Z" : "M4,2 L16,2 L16,6 L20,6 L20,22 L4,22 Z";
            else if (Style == "Tactical") body = "M3,6 L7,3 L18,3 L21,6 L21,18 L18,21 L3,21 Z";
            else if (Style == "Anime") body = "M3,7 Q3,3 7,3 L17,3 Q21,3 21,7 L21,17 Q21,21 17,21 L7,21 Q3,21 3,17 Z";
            group.Children.Add(new GeometryDrawing(Style == "Outline" ? Brushes.Transparent : background, Pen(foreground), System.Windows.Media.Geometry.Parse(body)));
        }
        var detail = Geometry(kind);
        if (Style is "Pixel" or "Anime" or "Tactical")
            detail = System.Windows.Media.Geometry.Parse(kind switch
            {
                IconKind.Folder => "M6,10 L18,10 M15,16 L18,16",
                IconKind.Drive => "M6,14 L18,14 M7,17 L11,17 M16,17 L17,17",
                IconKind.Document => "M7,9 L17,9 M7,13 L17,13 M7,17 L14,17",
                IconKind.Picture => "M6,17 L10,11 L14,16 L17,12 L19,17 M7,8 L8,8",
                IconKind.Music => "M14,8 L14,16 L9,18 L7,16 L9,14 L14,14 M14,8 L18,9",
                IconKind.Video => "M9,8 L17,13 L9,18 Z",
                IconKind.Code => "M10,9 L6,13 L10,17 M15,9 L19,13 L15,17",
                IconKind.Archive => "M10,6 L14,6 L14,10 L10,10 L10,14 L14,14 L14,18 L10,18",
                _ => kind is IconKind.File or IconKind.Pdf or IconKind.Application ? "M8,10 L16,10 L16,17 L8,17 Z" : "",
            });
        if (detail.IsEmpty()) detail = Geometry(kind);
        group.Children.Add(new GeometryDrawing(null, Pen(foreground), detail));
        if (Style is "Rune" or "Anime" or "Neon")
            group.Children.Add(new GeometryDrawing(foreground, null, System.Windows.Media.Geometry.Parse("M20,1 L21,3 L23,4 L21,5 L20,7 L19,5 L17,4 L19,3 Z")));
        var image = new DrawingImage(group);
        image.Freeze();
        return image;
    }

    private static Dictionary<IconKind, Geometry> BuildPaths()
    {
        // Authored here for Seekr. Shared concepts (arrows, folders, pages) retain familiar meanings.
        var paths = new Dictionary<IconKind, string>
        {
            [IconKind.Back] = "M11,5 L4,12 L11,19 M4,12 L21,12",
            [IconKind.Forward] = "M13,5 L20,12 L13,19 M3,12 L20,12",
            [IconKind.Up] = "M5,11 L12,4 L19,11 M12,4 L12,21",
            [IconKind.Refresh] = "M20,9 C18,3 10,2 6,6 C1,11 5,20 12,20 C16,20 19,18 20,15 M20,3 L20,9 L14,9",
            [IconKind.Search] = "M17,10 A7,7 0 1 1 3,10 A7,7 0 1 1 17,10 M15,15 L21,21",
            [IconKind.Filter] = "M3,5 L21,5 L14,13 L14,20 L10,18 L10,13 Z M7,2 L17,2",
            [IconKind.Bookmark] = "M6,3 L18,3 L18,21 L12,17 L6,21 Z M9,7 L15,7",
            [IconKind.Add] = "M12,3 L12,21 M3,12 L21,12",
            [IconKind.Close] = "M5,5 L19,19 M19,5 L5,19",
            [IconKind.More] = "M5,11 A1,1 0 1 1 5,13 A1,1 0 1 1 5,11 M12,11 A1,1 0 1 1 12,13 A1,1 0 1 1 12,11 M19,11 A1,1 0 1 1 19,13 A1,1 0 1 1 19,11",
            [IconKind.Cut] = "M9,16 L19,3 M8,8 L19,21 M4,5 A3,3 0 1 1 4,11 A3,3 0 1 1 4,5 M4,14 A3,3 0 1 1 4,20 A3,3 0 1 1 4,14",
            [IconKind.Copy] = "M8,7 L20,7 L20,21 L8,21 Z M4,16 L4,3 L16,3",
            [IconKind.Paste] = "M8,5 L4,5 L4,21 L20,21 L20,5 L16,5 M8,3 L16,3 L16,7 L8,7 Z M8,12 L16,12 M8,16 L14,16",
            [IconKind.Rename] = "M3,8 L3,19 L16,19 M7,15 L8,10 L17,2 L21,6 L12,15 Z M15,4 L19,8",
            [IconKind.Delete] = "M4,6 L20,6 M9,3 L15,3 M6,6 L7,21 L17,21 L18,6 M10,10 L10.5,17 M14,10 L13.5,17",
            [IconKind.Sort] = "M5,3 L5,21 L2,17 M5,21 L8,17 M11,5 L21,5 M11,11 L18,11 M11,17 L15,17",
            [IconKind.List] = "M3,5 L5,5 M9,5 L21,5 M3,12 L5,12 M9,12 L21,12 M3,19 L5,19 M9,19 L21,19",
            [IconKind.Details] = "M3,4 L21,4 L21,20 L3,20 Z M14,4 L14,20 M17,8 L18,8 M17,12 L18,12",
            [IconKind.Settings] = "M4,6 L20,6 M4,12 L20,12 M4,18 L20,18 M8,3 L8,9 M16,9 L16,15 M10,15 L10,21",
            [IconKind.Check] = "M4,12 L9,18 L20,5",
            [IconKind.Chevron] = "M8,4 L16,12 L8,20",
            [IconKind.Warning] = "M12,2 L22,20 L2,20 Z M12,8 L12,13 M12,16 L12,17",
            [IconKind.Home] = "M2,11 L12,3 L22,11 M5,10 L5,21 L10,21 L10,15 L14,15 L14,21 L19,21 L19,10",
            [IconKind.Desktop] = "M3,4 L21,4 L21,16 L3,16 Z M12,16 L12,21 M7,21 L17,21",
            [IconKind.Download] = "M12,2 L12,15 M7,10 L12,15 L17,10 M4,16 L4,21 L20,21 L20,16",
            [IconKind.Person] = "M16,7 A4,4 0 1 1 8,7 A4,4 0 1 1 16,7 M4,21 C4,11 20,11 20,21",
            [IconKind.Folder] = "M3,7 L3,5 Q3,3.5 4.5,3.5 L9,3.5 L11,6 L19.5,6 Q21,6 21,7.5 L20,19 Q20,20.5 18.5,20.5 L4.5,20.5 Q3,20.5 3,19 Z M3,10 L20.5,10 M16,16 L18,16",
            [IconKind.Drive] = "M5,4 L19,4 L21,14 L21,19 Q21,20 20,20 L4,20 Q3,20 3,19 L3,14 Z M3,14 L21,14 M7,17 L12,17 M17,17 L18,17",
            [IconKind.File] = "M5,3 L15,3 L20,8 L20,21 L5,21 Z M15,3 L15,8 L20,8 M9,16 L15,16",
            [IconKind.Document] = "M5,3 L15,3 L20,8 L20,21 L5,21 Z M15,3 L15,8 L20,8 M8,12 L16,12 M8,16 L14,16",
            [IconKind.Pdf] = "M5,3 L15,3 L20,8 L20,21 L5,21 Z M15,3 L15,8 L20,8 M9,18 L9,11 L12,11 C16,11 16,15 12,15 L9,15",
            [IconKind.Picture] = "M5,3 L15,3 L20,8 L20,21 L5,21 Z M8,18 L11,13 L14,17 L16,14 L18,18 M8,9 L9,9",
            [IconKind.Music] = "M5,3 L15,3 L20,8 L20,21 L5,21 Z M14,10 L14,17 C14,20 8,20 8,17 C8,14 12,14 14,15 M14,10 L18,11",
            [IconKind.Video] = "M5,3 L15,3 L20,8 L20,21 L5,21 Z M10,11 L16,14.5 L10,18 Z",
            [IconKind.Archive] = "M4,5 L20,5 L20,20 L4,20 Z M4,9 L20,9 M9,5 L9,9 M14,12 L10,12 L10,15 L14,15 L14,18 L10,18",
            [IconKind.Code] = "M5,3 L15,3 L20,8 L20,21 L5,21 Z M10,11 L7,14 L10,17 M15,11 L18,14 L15,17 M13.5,10 L11.5,18",
            [IconKind.Application] = "M5,3 L15,3 L20,8 L20,21 L5,21 Z M8,11 L17,11 L17,18 L8,18 Z M8,14 L17,14",
        };
        return paths.ToDictionary(pair => pair.Key, pair =>
        {
            var geometry = System.Windows.Media.Geometry.Parse(pair.Value);
            geometry.Freeze();
            return geometry;
        });
    }
}
