using System.Windows;
using System.Windows.Media;

namespace WinMon.Preferences;

/// <summary>Appearance finishes; resource colors remain controlled by the chart palette.</summary>
internal static class ThemeFinishes
{
    internal enum BackdropKind { None = 0, Mica = 2, Acrylic = 3 }

    internal sealed record Finish(
        string Surface, string Raised, string Chrome, string Header, string Plot,
        string Search, string Selected, string SelectedText, string SegmentSelected,
        string Text, string Muted, string Line, string GaugeTrack, string Highlight,
        bool Light, bool Brushed = true, string? UiAccent = null,
        BackdropKind Backdrop = BackdropKind.None, byte PanelOpacity = 255, byte ChromeOpacity = 255,
        byte HeaderOpacity = 255, bool GlassSheen = false, string? TitleText = null,
        string? ChipSelectedText = null);

    internal static readonly string[] Names =
        ["Candy", "Copper", "Crystal", "EvergreenInstrument", "FrostedPorcelain", "Gunmetal",
            "MidnightInk", "Porcelain", "Prism", "SmokedSapphire", "Slate", "Wildberry"];

    internal static bool IsFinish(string? name) => Names.Contains(name);

    internal static string SelectedChipText(Finish finish) =>
        finish.ChipSelectedText ?? (finish.Light ? finish.Text : finish.SelectedText);

    internal static string InactiveTabText(string name, Finish finish) => name switch
    {
        "Candy" => "#4E3B51",
        "Copper" => "#FFF9F4",
        "Crystal" => "#E0EBF4",
        "Slate" => "#E6EFF4",
        _ => finish.Muted
    };

    internal static Finish For(string name) => name switch
    {
        "Candy" => new("#FFF8F1", "#FFFFFF", "#F7A6C7", "#F7A6C7", "#FFFCF9",
            "#FFFFFF", "#C71D68", "#FFFFFF", "#A9EFE3", "#2E2134", "#655369",
            "#D3C6CE", "#D9D2D2", "#FF78AA", true, false, "#BF155D"),
        "Copper" => new("#574033", "#71503E", "#A67556", "#77513C", "#171D24",
            "#2E2927", "#B77A4D", "#15191E", "#96664B", "#FFF1E6", "#E0BEAA",
            "#956E59", "#A47D68", "#EFC398", false, ChipSelectedText: "#FFFFFF"),
        "Gunmetal" => new("#1D252C", "#2A343D", "#3A454D", "#303B43", "#151D26",
            "#1B252E", "#49687C", "#FFFFFF", "#465B68", "#ECF3F5", "#B6C5CB",
            "#53626B", "#667985", "#849BA6", false),
        "Porcelain" => new("#F5F2EB", "#FFFEFA", "#E8E2D8", "#EEE9E0", "#FFFEFA",
            "#FFFEFA", "#365F85", "#FFFFFF", "#D6E4EF", "#242B31", "#59636A",
            "#CCC5B9", "#D5D0C7", "#FFFEFA", true, false, "#365F85"),
        "MidnightInk" => new("#101A28", "#1B2A3D", "#23364C", "#1D3045", "#0D1724",
            "#0D1724", "#315A82", "#FFFFFF", "#294766", "#EAF1F7", "#B9C9D8",
            "#43566A", "#52677D", "#36536E", false, false, "#477DAE"),
        "EvergreenInstrument" => new("#172522", "#23352F", "#30463D", "#273C34", "#111E1B",
            "#111E1B", "#456C4B", "#FFFFFF", "#345540", "#EFF3EA", "#BFCCBE",
            "#52665A", "#627968", "#78967A", false, false, "#87B782"),
        "FrostedPorcelain" => new("#F2F4F6", "#FFFFFF", "#EDF2F8", "#F1F5FA", "#FFFFFF",
            "#FFFFFF", "#38698E", "#FFFFFF", "#DCE9F3", "#202B39", "#536476",
            "#B9C8D5", "#C4D0DA", "#FFFFFF", true, false, "#38698E", BackdropKind.Acrylic, 226, 212, 230),
        "SmokedSapphire" => new("#10213C", "#1B3050", "#19395F", "#1A3658", "#0B1930",
            "#0B1930", "#315D90", "#FFFFFF", "#2B527F", "#F1F6FF", "#C2D2E5",
            "#496787", "#56718E", "#4E82B1", false, false, "#5C9BE0", BackdropKind.Acrylic, 228, 208, 224),
        "Crystal" => new("#1C2938", "#344B61", "#537399", "#435E7D", "#121B27",
            "#142333", "#426992", "#FFFFFF", "#516C89", "#EDF5FC", "#C5D3E0",
            "#5A7894", "#5C7185", "#95C5E7", false, false, "#A5D0F2",
            BackdropKind.Mica, 240, 215, 229, true),
        "Slate" => new("#2B3A48", "#3D5061", "#526878", "#4D6374", "#111A23",
            "#14212E", "#56768C", "#FFFFFF", "#566E80", "#F1F6F9", "#CAD6DD",
            "#647986", "#718591", "#B7CBD7", false, false, "#B6CAD7",
            BackdropKind.Acrylic, 210, 190, 205),
        "Prism" => new("#1A2031", "#303A53", "#332F5C", "#303A63", "#101624",
            "#151F32", "#455A91", "#FFFFFF", "#43527A", "#F1F2FF", "#C0C3E0",
            "#5467A0", "#636F93", "#8C76D7", false, false, "#8DCAFF",
            BackdropKind.Mica, 238, 216, 224, true),
        "Wildberry" => new("#211629", "#38203F", "#492447", "#4A2549", "#1D1424",
            "#25162C", "#753259", "#FFF3F9", "#5C2B4F", "#FFF1FA", "#D0AFC8",
            "#69405F", "#796878", "#BA5A8A", false, false, "#E36B9D"),
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, "Unknown appearance finish")
    };

    /// <summary>A flat title finish or reflected section header, both with fine horizontal grain.</summary>
    internal static DrawingBrush ChromeBrush(Finish finish, bool header)
    {
        var baseColor = (Color)ColorConverter.ConvertFromString(header ? finish.Header : finish.Chrome);
        var highlight = (Color)ColorConverter.ConvertFromString(finish.Highlight);
        var sheen = Color.FromRgb(
            (byte)((baseColor.R * 4 + highlight.R) / 5),
            (byte)((baseColor.G * 4 + highlight.G) / 5),
            (byte)((baseColor.B * 4 + highlight.B) / 5));
        var reflection = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(0, 1)
        };
        // Keep the edges at the base color so the header's reflection does not resemble a thick border.
        // The title remains flat to meet the native caption-button area.
        reflection.GradientStops.Add(new GradientStop(baseColor, 0));
        reflection.GradientStops.Add(new GradientStop(header ? sheen : baseColor, 0.5));
        reflection.GradientStops.Add(new GradientStop(baseColor, 1));
        var drawing = Grain(new Rect(0, 0, 1, 40), reflection, finish.Light, 1, 40);
        var brush = new DrawingBrush(drawing)
        {
            Viewbox = new Rect(0, 0, 1, 40),
            ViewboxUnits = BrushMappingMode.Absolute,
            Viewport = new Rect(0, 0, 1, 1),
            ViewportUnits = BrushMappingMode.RelativeToBoundingBox,
            Stretch = Stretch.Fill
        };
        brush.Freeze();
        return brush;
    }

    /// <summary>Small absolute tiles keep the grain the same size in compact and full views.</summary>
    internal static DrawingBrush PanelBrush(Finish finish)
        => TiledGrainBrush(finish.Surface, finish.Light);

    /// <summary>A restrained horizontal reflection within translucent glass headers.</summary>
    internal static LinearGradientBrush GlassHeaderBrush(Finish finish, byte opacity)
    {
        var baseColor = (Color)ColorConverter.ConvertFromString(finish.Header);
        var highlight = (Color)ColorConverter.ConvertFromString(finish.Highlight);
        var sheen = Color.FromArgb(opacity,
            (byte)((baseColor.R * 2 + highlight.R) / 3),
            (byte)((baseColor.G * 2 + highlight.G) / 3),
            (byte)((baseColor.B * 2 + highlight.B) / 3));
        var edge = Color.FromArgb(opacity, baseColor.R, baseColor.G, baseColor.B);
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0), EndPoint = new Point(1, 0),
            GradientStops = new GradientStopCollection
            {
                new(edge, 0), new(sheen, 0.5), new(edge, 1)
            }
        };
        brush.Freeze();
        return brush;
    }

    private static DrawingBrush TiledGrainBrush(string colorHex, bool light)
    {
        const double tile = 16;
        var color = (Color)ColorConverter.ConvertFromString(colorHex);
        var drawing = Grain(new Rect(0, 0, tile, tile), new SolidColorBrush(color), light, tile, tile);
        var brush = new DrawingBrush(drawing)
        {
            Viewbox = new Rect(0, 0, tile, tile),
            ViewboxUnits = BrushMappingMode.Absolute,
            Viewport = new Rect(0, 0, tile, tile),
            ViewportUnits = BrushMappingMode.Absolute,
            TileMode = TileMode.Tile,
            Stretch = Stretch.Fill
        };
        brush.Freeze();
        return brush;
    }

    private static DrawingGroup Grain(Rect area, Brush baseBrush, bool light, double width, double height)
    {
        var group = new DrawingGroup();
        group.Children.Add(new GeometryDrawing(baseBrush, null, new RectangleGeometry(area)));
        for (var index = 0; index < (int)(height / 1.4); index++)
        {
            var y = 0.55 + index * 1.4;
            var bright = index % 3 == 0;
            var alpha = (byte)(bright ? (light ? 18 : 15) : (light ? 14 : 12));
            var ink = bright ? Colors.White : Colors.Black;
            var pen = new Pen(new SolidColorBrush(Color.FromArgb(alpha, ink.R, ink.G, ink.B)),
                index % 4 == 0 ? 0.4 : 0.25);
            group.Children.Add(new GeometryDrawing(null, pen,
                new LineGeometry(new Point(0, y), new Point(width, y))));
        }
        group.Freeze();
        return group;
    }
}
