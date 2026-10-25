using System.Windows;
using System.Windows.Media;

namespace WinMon.Preferences;

/// <summary>Appearance finishes; resource colors remain controlled by the chart palette.</summary>
internal static class ThemeFinishes
{
    internal sealed record Finish(
        string Surface, string Raised, string Chrome, string Header, string Plot,
        string Search, string Selected, string SelectedText, string SegmentSelected,
        string Text, string Muted, string Line, string GaugeTrack, string Highlight,
        bool Light, bool Brushed = true, string? UiAccent = null);

    internal static readonly string[] Names =
        ["BrushedSteel", "Copper", "Gunmetal", "BrushedNickel", "Porcelain", "MidnightInk", "EvergreenInstrument"];

    internal static bool IsFinish(string? name) => Names.Contains(name);

    internal static Finish For(string name) => name switch
    {
        "BrushedSteel" => new("#B8C0C6", "#D5DADF", "#C5CDD2", "#B6C0C7", "#B8C1C6",
            "#E4E8EB", "#315D85", "#FFFFFF", "#52708B", "#182B3A", "#455969",
            "#8999A4", "#A0AFBA", "#ECF1F3", true),
        "Copper" => new("#574033", "#71503E", "#A67556", "#77513C", "#171D24",
            "#2E2927", "#B77A4D", "#15191E", "#96664B", "#FFF1E6", "#E0BEAA",
            "#956E59", "#A47D68", "#EFC398", false),
        "Gunmetal" => new("#1D252C", "#2A343D", "#3A454D", "#303B43", "#151D26",
            "#1B252E", "#49687C", "#FFFFFF", "#465B68", "#ECF3F5", "#B6C5CB",
            "#53626B", "#667985", "#849BA6", false),
        "BrushedNickel" => new("#D0D2D1", "#EAEBE9", "#CFD1D0", "#DADBD9", "#C8C9C7",
            "#F2F2EF", "#456B88", "#FFFFFF", "#6B8293", "#202B32", "#50606A",
            "#ABB2B5", "#B4BDC1", "#FFFFFF", true),
        "Porcelain" => new("#F5F2EB", "#FFFEFA", "#E8E2D8", "#EEE9E0", "#FFFEFA",
            "#FFFEFA", "#365F85", "#FFFFFF", "#D6E4EF", "#242B31", "#59636A",
            "#CCC5B9", "#D5D0C7", "#FFFEFA", true, false, "#365F85"),
        "MidnightInk" => new("#101A28", "#1B2A3D", "#23364C", "#1D3045", "#0D1724",
            "#0D1724", "#315A82", "#FFFFFF", "#294766", "#EAF1F7", "#B9C9D8",
            "#43566A", "#52677D", "#36536E", false, false, "#477DAE"),
        "EvergreenInstrument" => new("#172522", "#23352F", "#30463D", "#273C34", "#111E1B",
            "#111E1B", "#456C4B", "#FFFFFF", "#345540", "#EFF3EA", "#BFCCBE",
            "#52665A", "#627968", "#78967A", false, false, "#87B782"),
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

    /// <summary>A low-contrast matte alloy plot for the two light metal finishes.</summary>
    internal static DrawingBrush PlotBrush(Finish finish)
        => TiledGrainBrush(finish.Plot, finish.Light);

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
