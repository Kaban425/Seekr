using System.Windows;
using System.Windows.Media;

namespace Seekr.App.Services;

public static class DesignPalette
{
    public static void Apply(string design, bool dark)
    {
        if (Application.Current is not { } app) return;
        app.Resources["Seekr.ButtonStyle"] = design;
        app.Resources["Seekr.ControlRadius"] = new CornerRadius(design is "Polar" or "Arcade" or "Terminal" or "Blueprint" ? 0 : design == "Lagoon" ? 18 : design is "Rose" or "Sakura" ? 12 : design=="Graphite"?10:design is "Studio" or "Paper" ? 8 : 4);
        if (design == "Default") return;
        var (canvas, sidebar, surface, selection) = (design, dark) switch
        {
            ("Graphite", true) => ("#10161E", "#17222E", "#233647", "#344E65"),
            ("Graphite", false) => ("#F0F6FA", "#DFEAF1", "#FFFFFF", "#CEDFEA"),
            ("Blueprint", true) => ("#102333", "#132B3D", "#213D51", "#34556A"),
            ("Blueprint", false) => ("#EBF4FA", "#DEEBF4", "#F8FCFF", "#C8DEED"),
            ("Paper", true) => ("#28241F", "#302A23", "#40372D", "#594936"),
            ("Paper", false) => ("#F7F1E5", "#EEE4D1", "#FFFAF0", "#E4D3B5"),
            ("Terminal", true) => ("#111D1C", "#142321", "#1F3330", "#315149"),
            ("Terminal", false) => ("#EDF4EC", "#E2EBDF", "#F8FCF6", "#CEDDCB"),
            ("Studio", true) => ("#191A1D", "#202125", "#303136", "#4E3C30"),
            ("Studio", false) => ("#F6F3EF", "#EAE7E2", "#FFFFFF", "#EDDAC8"),
            ("Lagoon", true) => ("#15252D", "#1C3038", "#2C4149", "#3B555D"),
            ("Lagoon", false) => ("#EEF7F6", "#E0EFED", "#F9FDFC", "#CEE2DF"),
            ("Arcade", true) => ("#171B36", "#202445", "#2D3557", "#3E4969"),
            ("Sakura", true) => ("#232B3B", "#202635", "#30384B", "#44465C"),
            ("Arcade", false) => ("#F0E6D2", "#E4DAC9", "#FFF4DB", "#DDD0B5"),
            ("Sakura", false) => ("#EFEAF1", "#E7E0EB", "#F6F2F7", "#D8CBDF"),
            ("Polar", true) => ("#2E3440", "#262D38", "#3B4252", "#434C5E"),
            ("Velvet", true) => ("#282A36", "#22232D", "#343646", "#44475A"),
            ("Rose", true) => ("#242131", "#1D1B28", "#363044", "#494057"),
            ("Polar", false) => ("#ECEFF4", "#E5E9F0", "#F5F7FA", "#D4E4EF"),
            ("Velvet", false) => ("#F7F4FC", "#EEEAF5", "#FFFFFF", "#E5DBF1"),
            _ => ("#FAF3F7", "#F1E8EE", "#FFFFFF", "#EDDBE7"),
        };
        void Set(string name, string hex) { var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); brush.Freeze(); app.Resources["Seekr." + name] = brush; }
        Set("Canvas", canvas); Set("Sidebar", sidebar); Set("Surface", surface); Set("MenuSurface", surface);
        Set("Selection", selection); Set("InactiveSelection", surface); Set("Hover", surface); Set("Pressed", selection);
        Set("Divider", dark ? "#3862667A" : "#40BBB4C7");
        Set("Border", dark ? "#62667A" : "#BBB4C7");
        Set("TextPrimary", dark ? "#F1F2F8" : "#252333");
        Set("TextSecondary", dark ? "#CDD2E0" : "#51495C");
    }
}


