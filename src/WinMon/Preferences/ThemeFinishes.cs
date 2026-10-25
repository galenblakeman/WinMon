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
        byte HeaderOpacity = 255);

    internal static readonly string[] Names =
        ["Copper", "Gunmetal", "Porcelain", "MidnightInk", "EvergreenInstrument",
            "FrostedPorcelain", "SmokedSapphire", "GlassRim"];

    internal static bool IsFinish(string? name) => Names.Contains(name);

    internal static Finish For(string name) => name switch
    {
        "Copper" => new("#574033", "#71503E", "#A67556", "#77513C", "#171D24",
            "#2E2927", "#B77A4D", "#15191E", "#96664B", "#FFF1E6", "#E0BEAA",
            "#956E59", "#A47D68", "#EFC398", false),
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
        "GlassRim" => new("#15191E", "#20262D", "#32343F", "#30343E", "#15191E",
            "#15191E", "#324050", "#FFFFFF", "#3D4855", "#EDF2F6", "#B3BDC8",
            "#46515F", "#566473", "#6B7588", false, false, "#8DA8CB", BackdropKind.Mica, 255, 185, 200),
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, "Unknown appearance finish")
    };

    /// <summary>A flat title finish or reflected section header, both with fine horizontal grain.</summary>
    internal static DrawingBrush ChromeBrush(Finish finish, bool header)
    {
        var baseColor = (Color)ColorConverter.ConvertFromString(header ? finish.Header : finish.Chrome);
        var highlight = (Color)ColorConverter.ConvertFromString(finish.Highlight);
        var reflection = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(0, 1)
        };
        // The native caption-button area has a flat DWM tint; keep the adjacent title's edges flat too.
        reflection.GradientStops.Add(new GradientStop(header ? highlight : baseColor, 0));
        reflection.GradientStops.Add(new GradientStop(baseColor, 0.16));
        reflection.GradientStops.Add(new GradientStop(baseColor, 0.76));
        reflection.GradientStops.Add(new GradientStop(header ? highlight : baseColor, 1));
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
