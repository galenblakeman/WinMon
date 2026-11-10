using Spectre.Console.Rendering;

namespace WinMon.Cli;

public static class TerminalText
{
    public static string Fit(string value, int width)
    {
        if (width <= 0) return "";
        var clean = SnapshotReport.SafeText(value.Replace("\t", "  ", StringComparison.Ordinal));
        var segment = new Segment(clean);
        var clipped = Segment.Truncate(segment, width)?.Text ?? "";
        return clipped == clean ? clean : (Segment.Truncate(segment, width - 1)?.Text ?? "") + "…";
    }
}
