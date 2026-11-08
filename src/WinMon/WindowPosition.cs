using System.Drawing;

namespace WinMon;

/// <summary>A user-chosen position in physical pixels, relative to its monitor's work area.</summary>
internal sealed record WindowPosition(string Monitor, Point Offset)
{
    internal static WindowPosition Capture(string monitor, Rectangle workArea, Point location)
    {
        return new WindowPosition(monitor, new Point(location.X - workArea.Left, location.Y - workArea.Top));
    }

    internal Point? Resolve(IEnumerable<(string Monitor, Rectangle WorkArea)> screens, Size windowSize)
    {
        foreach (var (monitor, area) in screens)
        {
            if (!string.Equals(monitor, Monitor, StringComparison.OrdinalIgnoreCase)) continue;
            return new Point(
                area.Left + Math.Clamp(Offset.X, 0, Math.Max(0, area.Width - windowSize.Width)),
                area.Top + Math.Clamp(Offset.Y, 0, Math.Max(0, area.Height - windowSize.Height)));
        }

        return null;
    }
}