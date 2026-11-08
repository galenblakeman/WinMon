namespace WinMon.Preferences;

/// <summary>The presentation remembered for one chart type.</summary>
public sealed record ResourceChartPreferences
{
    public ResourceChartType Type { get; init; }
    public int VisibleMetrics { get; init; } = 63;
    public int RangeSeconds { get; init; } = MonitorPreferences.ShortRangeSeconds;
    public bool ControlsCollapsed { get; init; } = true;
    public int FocusMetric { get; init; }

    internal ResourceChartPreferences Normalized() => this with
    {
        VisibleMetrics = VisibleMetrics & 63,
        RangeSeconds = RangeSeconds is 120 or 600 or 1800 or 3600 ? RangeSeconds : MonitorPreferences.ShortRangeSeconds,
        FocusMetric = Math.Clamp(FocusMetric, 0, 5)
    };
}
