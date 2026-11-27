using System.Windows;
using System.Windows.Media;
using MonCore.Monitoring;
using WinMon.Monitoring;
using WinMon.Preferences;

namespace WinMon.Controls;

/// <summary>
///     Focus breakdowns drawn from readings every sample already carries: CPU kernel share, memory composition and
///     commit, GPU dedicated memory, and disk throughput and response time lanes.
/// </summary>
public sealed partial class CpuGraph
{
    // Series beyond the six resources (0-5) and the network directions (6-7).
    private const int KernelSeries = 8;
    private const int GpuMemorySeries = 9;
    private const int CommitSeries = 10;
    private const int ReadSeries = 11;
    private const int WriteSeries = 12;
    private const int ResponseSeries = 13;

    // Cumulative tops of the memory stack, bottom to top.
    private const int InUseTop = 14;
    private const int ModifiedTop = 15;
    private const int StandbyTop = 16;
    private const int FreeTop = 17;

    // Layers overlap from the baseline, so each band's tone is the product of the layers above it; the
    // opacities step the in-use band darkest and free lightest.
    private static readonly (int Top, double Opacity)[] MemoryLayers =
        [(FreeTop, 0.05), (StandbyTop, 0.18), (ModifiedTop, 0.3), (InUseTop, 0.15)];
    private const double KernelBandOpacity = 0.3;
    private const double FocusWashOpacity = 0.08;

    // Half the dashes of DashStyles.Dot: stroking that denser pattern cost about 1.5 ms per GPU Focus render.
    private static readonly DashStyle SparseDot = Frozen(new DashStyle([1d, 3d], 0));

    private double _diskRateScale = 1_000_000;
    private double _responseScale = 1;

    /// <summary>
    ///     Cumulative percentages of total memory: in use without modified pages, then modified, standby and free.
    ///     Used memory is total minus available, so it already includes modified pages.
    /// </summary>
    internal static (double InUse, double Modified, double Standby, double Free)? MemoryStack(ResourceSample r)
    {
        if (r is not { TotalMemoryBytes: > 0 and var total, UsedMemoryBytes: { } used, ModifiedBytes: { } modified,
                StandbyBytes: { } standby, FreeBytes: { } free }) return null;
        double Percent(double bytes) => Math.Clamp(bytes * 100 / total, 0, 100);
        return (Percent((double)used - Math.Min(modified, used)), Percent(used), Percent((double)used + standby),
            Percent((double)used + standby + free));
    }

    /// <summary>Committed memory as a share of the commit limit.</summary>
    internal static double? CommitPercent(ResourceSample r) =>
        r is { CommitLimitBytes: > 0 and var limit, CommittedBytes: { } committed }
            ? Math.Clamp(committed * 100d / limit, 0, 100)
            : null;

    /// <summary>Dedicated GPU memory in use as a share of its total.</summary>
    internal static double? GpuMemoryPercent(ResourceSample r) =>
        r is { GpuMemoryTotalBytes: > 0 and var total, GpuMemoryUsedBytes: { } used }
            ? Math.Clamp(used * 100d / total, 0, 100)
            : null;

    private bool FocusesDisk => ChartType == ResourceChartType.Focus && FocusedResource() is 2 or 5;
    private bool FocusesSecondaryDisk => FocusedResource() == 5;

    private double? DetailValue(MonitorSample sample, int series)
    {
        var r = sample.Resources;
        var secondary = FocusesSecondaryDisk;
        return series switch
        {
            KernelSeries => r.CpuKernelPercent,
            GpuMemorySeries => GpuMemoryPercent(r),
            CommitSeries => CommitPercent(r),
            ReadSeries => (secondary ? r.SecondaryDiskReadBytesPerSecond : r.DiskReadBytesPerSecond) * 100 / _diskRateScale,
            WriteSeries => (secondary ? r.SecondaryDiskWriteBytesPerSecond : r.DiskWriteBytesPerSecond) * 100 / _diskRateScale,
            ResponseSeries => (secondary ? r.SecondaryDiskResponseSeconds : r.DiskResponseSeconds) * 100_000 / _responseScale,
            InUseTop => MemoryStack(r)?.InUse,
            ModifiedTop => MemoryStack(r)?.Modified,
            StandbyTop => MemoryStack(r)?.Standby,
            FreeTop => MemoryStack(r)?.Free,
            _ => null
        };
    }

    /// <summary>The resource whose device identity decides whether a series' trace may join across samples.</summary>
    private int SeriesSource(int series) => series switch
    {
        ReceiveSeries or SendSeries => 3,
        KernelSeries => 0,
        GpuMemorySeries => 4,
        CommitSeries or >= InUseTop => 1,
        ReadSeries or WriteSeries or ResponseSeries => FocusesSecondaryDisk ? 5 : 2,
        _ => series
    };

    /// <summary>Round the visible disk throughput and response peaks for the Disk Focus lanes.</summary>
    private void UpdateFocusDetailScales()
    {
        if (!FocusesDisk) return;
        var secondary = FocusesSecondaryDisk;
        double rate = 0, response = 0;
        foreach (var sample in _samples)
        {
            if (sample.IsGap || Moment(sample) < _end - _seconds) continue;
            var r = sample.Resources;
            rate = Math.Max(rate, Finite(secondary ? r.SecondaryDiskReadBytesPerSecond : r.DiskReadBytesPerSecond));
            rate = Math.Max(rate, Finite(secondary ? r.SecondaryDiskWriteBytesPerSecond : r.DiskWriteBytesPerSecond));
            response = Math.Max(response, Finite(secondary ? r.SecondaryDiskResponseSeconds : r.DiskResponseSeconds) * 1000);
        }

        _diskRateScale = NetworkChartScale.RoundUp(Math.Max(rate, 100_000));
        _responseScale = NetworkChartScale.RoundUp(Math.Max(response, 1));

        static double Finite(double? value) => value is { } number && double.IsFinite(number) ? number : 0;
    }

    /// <summary>Draw the focused resource's own breakdown on the main Focus plot, then its legend.</summary>
    private void DrawFocusDetails(DrawingContext dc, Rect plot, Rect main, int focus, Brush color, Brush text)
    {
        var legend = new Point(plot.Left + 5, plot.Top + 4);
        if (!HasFocusTemperature) legend.X += LabelWidth(MetricName(focus)) + 12;
        var right = plot.Right - 5;
        var contrast = SystemParameters.HighContrast;
        switch (focus)
        {
            case 0:
                if (contrast) DrawSeries(dc, main, color, KernelSeries, dashOverride: SparseDot);
                else DrawSeries(dc, main, color, KernelSeries, true, fillOpacity: KernelBandOpacity);
                LegendItem(dc, ref legend, right, Strings.ChartKernel, color, text,
                    contrast ? SparseDot : null, contrast ? 0 : Layered(FocusWashOpacity, KernelBandOpacity));
                break;
            case 1:
                if (!contrast)
                {
                    foreach (var (top, opacity) in MemoryLayers)
                        DrawSeries(dc, main, color, top, true, fillOpacity: opacity);
                    var names = (ReadOnlySpan<string>)[Strings.DetailFree, Strings.DetailStandby, Strings.DetailModified,
                        Strings.ChartInUse];
                    Span<double> tones = stackalloc double[4];
                    for (var layer = 0; layer < 4; layer++)
                        tones[layer] = Layered(layer == 0 ? 0 : tones[layer - 1], MemoryLayers[layer].Opacity);
                    for (var layer = 3; layer >= 0; layer--)
                        LegendItem(dc, ref legend, right, names[layer], color, text, null, tones[layer]);
                }

                DrawSeries(dc, main, color, CommitSeries, dashOverride: DashStyles.Dash);
                LegendItem(dc, ref legend, right, Strings.DetailCommittedLimit, color, text, DashStyles.Dash);
                break;
            case 2 or 5:
                // The I/O lane below draws read solid and write dashed; the lane itself is too short for a legend.
                LegendItem(dc, ref legend, right, Strings.DetailRead, color, text, DashStyles.Solid);
                LegendItem(dc, ref legend, right, Strings.DetailWrite, color, text, DashStyles.Dash);
                break;
            case 4:
                DrawSeries(dc, main, color, GpuMemorySeries, dashOverride: SparseDot);
                LegendItem(dc, ref legend, right, Strings.DetailDedicatedMemory, color, text, SparseDot);
                break;
        }
    }

    private static double Layered(double below, double above) => 1 - (1 - below) * (1 - above);

    /// <summary>Draw a line or swatch sample and its text, skipping items that would pass the right edge.</summary>
    private void LegendItem(DrawingContext dc, ref Point origin, double right, string text, Brush color, Brush textBrush,
        DashStyle? dash = null, double fill = 0)
    {
        var width = 22 + LabelWidth(text);
        if (origin.X + width > right)
        {
            origin.X = double.PositiveInfinity;
            return;
        }

        if (fill > 0)
            dc.DrawRectangle(CachedFill(color, fill), CachedPen(color, 0.6), new Rect(origin.X, origin.Y + 3, 16, 10));
        else
            dc.DrawLine(CachedPen(color, 1.7, dash), new Point(origin.X, origin.Y + 8), new Point(origin.X + 16, origin.Y + 8));
        Label(dc, text, new Point(origin.X + 19, origin.Y), textBrush);
        origin.X += width + 10;
    }

    /// <summary>Lane names the Disk Focus adds above the context lanes.</summary>
    private static string[] DiskDetailLaneNames => [Strings.IO, Strings.ChartResponse];

    private int FocusDetailLaneCount => FocusesDisk ? 2 : 0;

    /// <summary>
    ///     A Focus context lane. Disk detail lanes come first at twice the height of a resource lane, because their
    ///     traces need a readable scale; <paramref name="slot"/> counts detail lanes, then resource lanes.
    /// </summary>
    private Rect ContextLane(Rect plot, int slot, int resources)
    {
        var details = FocusDetailLaneCount;
        var unit = plot.Height * 0.41 / (details * 2 + resources);
        var top = plot.Top + plot.Height * 0.59 + (slot < details ? slot * 2 : details + slot) * unit;
        return new Rect(plot.Left, top, plot.Width, slot < details ? unit * 2 : unit);
    }

    private string DiskDetailRange(int lane) => Strings.Format(Strings.LaneScaleRange,
        lane == 0 ? RateText.Format(_diskRateScale * 8) : Milliseconds(_responseScale));

    /// <summary>Disk Focus lanes: read solid and write dashed on a throughput scale, then response time.</summary>
    private void DrawDiskDetailLanes(DrawingContext dc, Rect plot, int resources, int focus, Brush accent, Brush line,
        Brush muted)
    {
        var color = MetricBrush(focus, accent);
        var text = MetricTextBrush(focus, accent);
        var names = DiskDetailLaneNames;
        for (var index = 0; index < 2; index++)
        {
            var lane = ContextLane(plot, index, resources);
            dc.DrawLine(CachedPen(line, 0.6), lane.TopLeft, lane.TopRight);
            // Name above range in the gutter when the lane is tall enough, as Small multiples does; else the
            // range moves inside the lane's top-right corner.
            var stacked = lane.Height >= 28;
            Label(dc, names[index], new Point(lane.Left - 8,
                lane.Top + (stacked ? Math.Max(1, lane.Height / 2 - 14) : Math.Max(2, lane.Height / 2 - 7))), text, 1);
            Label(dc, DiskDetailRange(index), stacked
                ? new Point(lane.Left - 8, lane.Top + lane.Height / 2)
                : new Point(lane.Right - 4, lane.Top + 1), muted, 1);
            var inset = new Rect(lane.Left, lane.Top + 3, lane.Width, Math.Max(1, lane.Height - 6));
            if (index == 0)
            {
                DrawSeries(dc, inset, color, ReadSeries, dashOverride: DashStyles.Solid);
                DrawSeries(dc, inset, color, WriteSeries, dashOverride: DashStyles.Dash);
            }
            else
            {
                DrawSeries(dc, inset, color, ResponseSeries, dashOverride: DashStyles.Solid);
            }
        }
    }

    internal static string Milliseconds(double value) =>
        (value < 10 ? value.ToString("0.##", System.Globalization.CultureInfo.CurrentCulture)
            : value.ToString("0", System.Globalization.CultureInfo.CurrentCulture)) + " ms";
}
