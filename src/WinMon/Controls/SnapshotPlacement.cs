using System.Windows;

namespace WinMon.Controls;

/// <summary>Keep the inspection panel inside the plot, flipping sides at the right edge.</summary>
internal static class SnapshotPlacement
{
    internal static Rect Calculate(Rect plot, Point anchor, Size desired)
    {
        var inset = Math.Min(8, Math.Min(plot.Width, plot.Height) / 4);
        plot.Inflate(-inset, -inset);
        var width = Math.Min(desired.Width, plot.Width);
        var height = Math.Min(desired.Height, plot.Height);
        var x = anchor.X + 14;
        if (x + width > plot.Right) x = anchor.X - 14 - width;
        return new Rect(Math.Clamp(x, plot.Left, plot.Right - width),
            Math.Clamp(anchor.Y - height / 2, plot.Top, plot.Bottom - height), width, height);
    }
}