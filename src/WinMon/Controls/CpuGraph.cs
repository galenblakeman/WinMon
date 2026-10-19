using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using WinMon.Monitoring;

namespace WinMon.Controls;

/// <summary>Draw time-based resource graphs and auto-scaled process sparklines.</summary>
public sealed class CpuGraph : FrameworkElement
{
    private IReadOnlyList<CpuSample> samples = Array.Empty<CpuSample>();
    private ProcessUsage? process;
    private double?[]? processValues;
    private CpuSample? selected;
    private double end;
    private int seconds = 120;

    /// <summary>Get or set whether to draw percentage and time guides.</summary>
    public bool ShowGrid { get; set; }

    /// <summary>Series visibility bit mask: CPU, memory, disk, network.</summary>
    public int VisibleMetrics { get; set; } = 15;

    /// <summary>Upper CPU percentage shown; process sparklines scale to their visible peak.</summary>
    public double ScaleMaximum { get; private set; } = 100;

    /// <summary>Notify the host when a sample is selected for historical inspection.</summary>
    public event EventHandler<CpuSample>? SampleSelected;

    /// <summary>Notify the host when keyboard navigation requests the live view.</summary>
    public event EventHandler? LiveRequested;

    /// <summary>Replace the visible series and invalidate the drawing.</summary>
    /// <param name="history">Chronologically ordered snapshots.</param>
    /// <param name="windowSeconds">Visible time range in seconds.</param>
    /// <param name="selection">Selected historical time, if any.</param>
    /// <param name="identity">Optional process identity for a sparkline.</param>
    public void SetData(IReadOnlyList<CpuSample> history, int windowSeconds, CpuSample? selection, ProcessUsage? identity = null)
    {
        ArgumentNullException.ThrowIfNull(history);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(windowSeconds);
        var data = identity is null ? null : ProcessGraphData.Build(history, windowSeconds, [identity])[0];
        SetDataCore(history, windowSeconds, selection, identity, data);
    }

    internal void SetProcessData(ProcessGraphData data, int windowSeconds, ProcessUsage identity) =>
        SetDataCore(data.Samples, windowSeconds, null, identity, data);

    private void SetDataCore(IReadOnlyList<CpuSample> history, int windowSeconds, CpuSample? selection,
        ProcessUsage? identity, ProcessGraphData? data)
    {
        samples = data?.Samples ?? history;
        processValues = data?.Values;
        seconds = windowSeconds;
        selected = selection;
        process = identity;
        end = history.Count > 0 ? Moment(history[^1]) : 0;
        ScaleMaximum = data?.ScaleMaximum ?? 100;
        InvalidateVisual();
    }

    internal static double ProcessScaleMaximum(IReadOnlyList<CpuSample> history, int windowSeconds, ProcessUsage identity)
    {
        return ProcessGraphData.Build(history, windowSeconds, [identity])[0].ScaleMaximum;
    }

    /// <inheritdoc/>
    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        if (ActualWidth < 2 || ActualHeight < 2) return;
        var plot = Plot();
        if (plot.Width < 2 || plot.Height < 2) return;
        var accent = Resource("Accent", Brushes.LimeGreen);
        var line = Resource("Line", Brushes.DimGray);
        var muted = Resource("Muted", Brushes.Gray);
        drawingContext.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
        if (ShowGrid)
        {
            for (var i = 0; i <= 4; i++)
            {
                var y = plot.Top + plot.Height * i / 4;
                drawingContext.DrawLine(new Pen(line, 0.7), new Point(plot.Left, y), new Point(plot.Right, y));
                Label(drawingContext, $"{100 - i * 25}%", new Point(0, Math.Max(0, y - 7)), muted);
            }
            for (var i = 0; i <= 8; i++)
            {
                var x = plot.Left + plot.Width * i / 8;
                drawingContext.DrawLine(new Pen(line, 0.5), new Point(x, plot.Top), new Point(x, plot.Bottom));
            }
            Label(drawingContext, $"{seconds / 60:0.#} min ago", new Point(plot.Left, plot.Bottom + 5), muted);
            Label(drawingContext, "Now", new Point(Math.Max(plot.Left, plot.Right - 26), plot.Bottom + 5), muted);
        }
        drawingContext.PushClip(new RectangleGeometry(plot));
        if (process is not null || !ShowGrid)
            DrawSeries(drawingContext, plot, accent, 0);
        else
        {
            var keys = new[] { "CpuAccent", "MemoryAccent", "DiskAccent", "NetworkAccent" };
            for (var metric = 0; metric < keys.Length; metric++)
                if ((VisibleMetrics & (1 << metric)) != 0)
                    DrawSeries(drawingContext, plot, Resource(keys[metric], accent), metric);
        }
        if (selected is not null && samples.Any(s => ReferenceEquals(s, selected)) && Moment(selected) >= end - seconds)
        {
            var x = X(selected, plot);
            drawingContext.DrawLine(new Pen(muted, 1) { DashStyle = DashStyles.Dash }, new Point(x, plot.Top), new Point(x, plot.Bottom));
        }
        drawingContext.Pop();
    }

    private void DrawSeries(DrawingContext drawingContext, Rect plot, Brush accent, int metric)
    {
        var segment = new List<Point>();
        double? previous = null;
        string? previousSource = null;
        for (var sampleIndex = 0; sampleIndex < samples.Count; sampleIndex++)
        {
            var sample = samples[sampleIndex];
            if (Moment(sample) < end - seconds) continue;
            double? value = process is null ? MetricValue(sample, metric) : processValues![sampleIndex];
            var source = metric == 3 ? sample.Resources.NetworkId + "/" + sample.Resources.LinkBitsPerSecond : metric == 2 ? sample.Resources.DiskName : "";
            if (sample.IsGap || value is null || (previous is not null && Moment(sample) - previous.Value > 2.5) || previousSource != source)
            {
                DrawSegment(drawingContext, segment, plot.Bottom, accent, ShowGrid, metric);
                segment.Clear();
            }
            if (value is not null && !sample.IsGap)
                segment.Add(new Point(X(sample, plot), plot.Bottom - Math.Clamp(value.Value, 0, ScaleMaximum) / ScaleMaximum * plot.Height));
            previous = Moment(sample);
            previousSource = source;
        }
        DrawSegment(drawingContext, segment, plot.Bottom, accent, ShowGrid, metric);
    }

    internal static double? MetricValue(CpuSample sample, int metric) => metric switch
    {
        0 => sample.TotalCpuPercent,
        1 => sample.Resources.MemoryPercent,
        2 => sample.Resources.DiskActivePercent,
        3 => sample.Resources.NetworkPercent,
        _ => null
    };

    /// <inheritdoc/>
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!ShowGrid) return;
        var sample = Nearest(e.GetPosition(this).X);
        ToolTip = sample is null ? "No sample in this interval" : DescribeSample(sample);
    }

    /// <inheritdoc/>
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        Focus();
        if (ShowGrid && Nearest(e.GetPosition(this).X) is CpuSample sample) SampleSelected?.Invoke(this, sample);
    }

    /// <inheritdoc/>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!ShowGrid || samples.Count == 0) return;
        if (e.Key == Key.End) { LiveRequested?.Invoke(this, EventArgs.Empty); e.Handled = true; return; }
        if (e.Key is not (Key.Left or Key.Right or Key.Home)) return;
        var visible = samples.Where(s => Moment(s) >= end - seconds).ToArray();
        var index = selected is null ? visible.Length - 1 : Array.FindIndex(visible, s => ReferenceEquals(s, selected));
        index = e.Key == Key.Home ? 0 : Math.Clamp(index + (e.Key == Key.Left ? -1 : 1), 0, visible.Length - 1);
        SampleSelected?.Invoke(this, visible[index]);
        e.Handled = true;
    }

    private CpuSample? Nearest(double x)
    {
        if (samples.Count == 0) return null;
        var plot = Plot();
        if (plot.Width <= 0) return null;
        var target = end - seconds + Math.Clamp((x - plot.Left) / plot.Width, 0, 1) * seconds;
        var nearest = samples.MinBy(s => Math.Abs(Moment(s) - target));
        return nearest is not null && Math.Abs(Moment(nearest) - target) <= 2 ? nearest : null;
    }

    private Rect Plot() => ShowGrid ? new Rect(39, 8, Math.Max(0, ActualWidth - 41), Math.Max(0, ActualHeight - 33)) : new Rect(0, 0, ActualWidth, ActualHeight);
    private double X(CpuSample sample, Rect plot) => plot.Right - (end - Moment(sample)) / seconds * plot.Width;
    /// <summary>Map an observation to monotonic seconds for plotting; support wall-time-only imported fixtures.</summary>
    /// <param name="sample">Observation to position on the timeline.</param>
    /// <returns>Seconds on the observation clock.</returns>
    internal static double Moment(CpuSample sample) => sample.MonotonicTimestamp > 0 ? sample.MonotonicTimestamp / (double)Stopwatch.Frequency : sample.Timestamp.UtcTicks / (double)TimeSpan.TicksPerSecond;
    private Brush Resource(string key, Brush fallback) => TryFindResource(key) as Brush ?? fallback;

    private void Label(DrawingContext dc, string text, Point origin, Brush brush) => dc.DrawText(new FormattedText(text, CultureInfo.CurrentCulture,
        FlowDirection.LeftToRight, new Typeface("Segoe UI"), 11, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip), origin);

    internal static string DescribeSample(CpuSample sample)
    {
        static string Percent(double? value) => value is double number ? $"{number:0.0}%" : "unavailable";
        var r = sample.Resources;
        var diskRate = r.DiskBytesPerSecond is double disk ? $" · {disk / 1_000_000:0.00} MB/s" : "";
        var networkRate = r.ReceiveBytesPerSecond is double rx && r.SendBytesPerSecond is double tx
            ? $"\nRx {rx * 8 / 1_000_000:0.00} / Tx {tx * 8 / 1_000_000:0.00} Mbps" : "";
        return $"{sample.Timestamp:HH:mm:ss} · Historical interval\nCPU {Percent(sample.TotalCpuPercent)}\nMemory {Percent(r.MemoryPercent)}\nDisk {r.DiskName} {Percent(r.DiskActivePercent)}{diskRate}\nNetwork {r.NetworkName} {Percent(r.NetworkPercent)}{networkRate}\nClick to hold; top readouts stay live";
    }

    private static void DrawSegment(DrawingContext dc, List<Point> points, double baseline, Brush accent, bool overlay, int metric)
    {
        if (points.Count == 0) return;
        if (points.Count == 1) { dc.DrawEllipse(accent, null, points[0], 1.5, 1.5); return; }
        if (!overlay)
        {
            var area = new StreamGeometry();
            using (var context = area.Open())
            {
                context.BeginFigure(new Point(points[0].X, baseline), true, true);
                context.PolyLineTo(points, true, false);
                context.LineTo(new Point(points[^1].X, baseline), true, false);
            }
            var fill = accent.Clone();
            fill.Opacity = 0.16;
            dc.DrawGeometry(fill, null, area);
        }
        var trace = new StreamGeometry();
        using (var context = trace.Open())
        {
            context.BeginFigure(points[0], false, false);
            context.PolyLineTo(points.Skip(1).ToArray(), true, false);
        }
        var pen = new Pen(accent, overlay ? 1.7 : 1.4);
        if (SystemParameters.HighContrast && overlay)
            pen.DashStyle = metric switch { 1 => DashStyles.Dash, 2 => DashStyles.Dot, 3 => DashStyles.DashDot, _ => DashStyles.Solid };
        dc.DrawGeometry(null, pen, trace);
    }
}



