using System.Windows.Media;

namespace WinMon.Preferences;

/// <summary>Appearance-only metal finishes; resource colors remain controlled by the chart palette.</summary>
internal static class MetalThemes
{
    internal sealed record Finish(
        string Surface, string Raised, string Chrome, string Header, string Plot,
        string Search, string Selected, string SelectedText, string SegmentSelected,
        string Text, string Muted, string Line, string GaugeTrack, string Highlight,
        bool Light);

    internal static readonly string[] Names = ["BrushedSteel", "Copper", "Gunmetal", "BrushedNickel"];

    internal static bool IsMetal(string? name) => Names.Contains(name);

    internal static Finish For(string name) => name switch
    {
        "BrushedSteel" => new("#B8C0C6", "#D5DADF", "#C5CDD2", "#B6C0C7", "#142336",
            "#E4E8EB", "#315D85", "#FFFFFF", "#52708B", "#182B3A", "#455969",
            "#8999A4", "#A0AFBA", "#ECF1F3", true),
        "Copper" => new("#574033", "#71503E", "#A67556", "#77513C", "#171D24",
            "#2E2927", "#B77A4D", "#15191E", "#96664B", "#FFF1E6", "#E0BEAA",
            "#956E59", "#A47D68", "#EFC398", false),
        "Gunmetal" => new("#1D252C", "#2A343D", "#3A454D", "#303B43", "#151D26",
            "#1B252E", "#49687C", "#FFFFFF", "#465B68", "#ECF3F5", "#B6C5CB",
            "#53626B", "#667985", "#849BA6", false),
        "BrushedNickel" => new("#D0D2D1", "#EAEBE9", "#CFD1D0", "#DADBD9", "#172A40",
            "#F2F2EF", "#456B88", "#FFFFFF", "#6B8293", "#202B32", "#50606A",
            "#ABB2B5", "#B4BDC1", "#FFFFFF", true),
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, "Unknown metal finish")
    };

    /// <summary>A broad satin reflection gives chrome its material cue without a tiled texture in text areas.</summary>
    internal static LinearGradientBrush ChromeBrush(Finish finish, bool header)
    {
        var baseColor = (Color)ColorConverter.ConvertFromString(header ? finish.Header : finish.Chrome);
        var highlight = (Color)ColorConverter.ConvertFromString(finish.Highlight);
        var brush = new LinearGradientBrush
        {
            StartPoint = new System.Windows.Point(0, 0),
            EndPoint = new System.Windows.Point(0, 1)
        };
        brush.GradientStops.Add(new GradientStop(highlight, 0));
        brush.GradientStops.Add(new GradientStop(baseColor, 0.16));
        brush.GradientStops.Add(new GradientStop(baseColor, 0.76));
        brush.GradientStops.Add(new GradientStop(highlight, 1));
        brush.Freeze();
        return brush;
    }
}
