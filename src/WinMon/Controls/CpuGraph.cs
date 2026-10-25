using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using WinMon.Monitoring;
using WinMon.Preferences;

// ReSharper disable ConvertConditionalTernaryExpressionToSwitchExpression
// ReSharper disable ConvertIfStatementToSwitchStatement

namespace WinMon.Controls;

/// <summary>An automatic capture on the chart's top edge, drawn at the sample that completed its rule.</summary>
/// <param name="Moment">Where it is drawn, in <see cref="CpuGraph.Moment"/> seconds.</param>
/// <param name="BrushKey">The resource color, so the marker matches that resource's trace.</param>
/// <param name="Selected">Whether the selected sample is this capture's peak.</param>
/// <param name="Tag">The host's own capture record.</param>
internal sealed record ChartMarker(double Moment, string BrushKey, string Tooltip, bool Selected, object Tag);

/// <summary>Draw time-based resource graphs and auto-scaled process sparklines.</summary>
public sealed class CpuGraph : FrameworkElement
{
    private IReadOnlyList<MonitorSample> _samples = [];
    private ProcessUsage? _process;
    private ProcessSort _processSort;
    private double?[]? _processValues;
    private MonitorSample? _selected;
    private double _end;
    private int _seconds = 120;
    private double _networkScale = 1_000_000;

    private double _selectionY = 0.5;

    // Every graph redraws each second. Reusing points, frozen pens and brushes, and shaped labels keeps
    // a redraw from allocating them again and from sending new resources to the render thread.
    private readonly List<Point> _segment = [];
    private readonly List<Point> _binned = [];
    private Point[] _trace = [];
    private readonly Dictionary<(Brush, double), Brush> _fills = [];
    private readonly Dictionary<(Brush, double, DashStyle?), Pen> _pens = [];
    private readonly Dictionary<(string, Brush, double), FormattedText> _labels = [];
    private readonly Dictionary<(string, double), double> _labelWidths = [];
    private static readonly Typeface LabelTypeface = new("Segoe UI");
    private static readonly int[] MetricOrder = [0, 1, 2, 5, 3, 4];

    internal Rect PlotBounds => Plot();

    internal bool SelectionInPlot => _selected is not null && _samples.Any(s => ReferenceEquals(s, _selected)) &&
                                     Moment(_selected) >= _end - _seconds;

    internal Point SelectionAnchor
    {
        get
        {
            var plot = Plot();
            return new Point(_selected is null ? plot.Right : Math.Clamp(X(_selected, plot), plot.Left, plot.Right),
                plot.Top + _selectionY * plot.Height);
        }
    }

    /// <summary>Get or set whether to draw percentage and time guides.</summary>
    public bool ShowGrid { get; set; }

    /// <summary>Full-view chart presentation; process and compact sparklines ignore this choice.</summary>
    public ResourceChartType ChartType { get; set; }

    /// <summary>Resource enlarged in the focus chart.</summary>
    public int FocusMetric { get; set; }

    /// <summary>Localized names for the six resource lanes.</summary>
    public string[] MetricLabels { get; set; } = [];

    /// <summary>Render one resource as an edge-to-edge filled chart in a compact metric tile.</summary>
    internal int? TileMetric { get; set; }

    /// <summary>Series visibility bit mask: CPU, memory, disk, network, GPU.</summary>
    public int VisibleMetrics { get; set; } = 31;

    /// <summary>Use line patterns as an additional resource cue for the colorblind palette.</summary>
    internal bool UseSeriesPatterns { get; set; }

    /// <summary>Upper CPU percentage shown; process sparklines scale to their visible peak.</summary>
    public double ScaleMaximum { get; private set; } = 100;

    /// <summary>Notify the host when a sample is selected for historical inspection.</summary>
    public event EventHandler<MonitorSample>? SampleSelected;

    /// <summary>Notify the host when keyboard navigation requests the live view.</summary>
    public event EventHandler? LiveRequested;

    /// <summary>Automatic capture markers, oldest first; drawn only on the full resource chart.</summary>
    internal IReadOnlyList<ChartMarker> Markers { get; set; } = [];

    /// <summary>Notify the host when a marker is clicked or reached with Ctrl+Left or Ctrl+Right.</summary>
    internal event EventHandler<ChartMarker>? MarkerSelected;

    /// <summary>Replace the visible series and invalidate the drawing.</summary>
    /// <param name="history">Chronologically ordered snapshots.</param>
    /// <param name="windowSeconds">Visible time range in seconds.</param>
    /// <param name="selection">Selected historical time, if any.</param>
    /// <param name="identity">Optional process identity for a sparkline.</param>
    public void SetData(IReadOnlyList<MonitorSample> history, int windowSeconds, MonitorSample? selection,
        ProcessUsage? identity = null)
    {
        ArgumentNullException.ThrowIfNull(history);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(windowSeconds);
        var data = identity is null ? null : ProcessGraphData.Build(history, windowSeconds, [identity])[0];
        SetDataCore(history, windowSeconds, selection, identity, data);
    }

    internal void SetProcessData(ProcessGraphData data, int windowSeconds, ProcessUsage identity)
    {
        SetDataCore(data.Samples, windowSeconds, null, identity, data);
    }

    private void SetDataCore(IReadOnlyList<MonitorSample> history, int windowSeconds, MonitorSample? selection,
        ProcessUsage? identity, ProcessGraphData? data)
    {
        _samples = data?.Samples ?? history;
        _processValues = data?.Values;
        _seconds = windowSeconds;
        _selected = selection;
        if (selection is not null) ToolTip = null;
        _process = identity;
        _processSort = data?.Sort ?? ProcessSort.Cpu;
        _end = history.Count > 0 ? Moment(history[^1]) : 0;
        ScaleMaximum = data?.ScaleMaximum ?? 100;
        InvalidateVisual();
    }

    internal static double ProcessScaleMaximum(IReadOnlyList<MonitorSample> history, int windowSeconds,
        ProcessUsage identity)
    {
        return ProcessGraphData.Build(history, windowSeconds, [identity])[0].ScaleMaximum;
    }

    /// <inheritdoc/>
    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        if (ActualWidth < 2 || ActualHeight < 2) return;
        if ((ShowGrid || TileMetric == 3) && _process is null)
            _networkScale = NetworkScale(_samples.Where(s => Moment(s) >= _end - _seconds));
        var plot = Plot();
        if (plot.Width < 2 || plot.Height < 2) return;
        var accent = Resource("Accent", Brushes.LimeGreen);
        var line = Resource("Line", Brushes.DimGray);
        var muted = Resource("Muted", Brushes.Gray);
        drawingContext.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
        var alternative = ShowGrid && _process is null && TileMetric is null && ChartType != ResourceChartType.Overlay;
        if (ShowGrid)
        {
            drawingContext.DrawRectangle(Resource("PlotSurface", Brushes.Transparent), null, plot);
            if (!alternative || ChartType == ResourceChartType.Focus)
                for (var i = 0; i <= 4; i++)
            {
                var height = alternative ? plot.Height * 0.55 : plot.Height;
                var y = plot.Top + height * i / 4;
                drawingContext.DrawLine(CachedPen(line, 0.7), new Point(plot.Left, y), new Point(plot.Right, y));
                var focusNetwork = alternative && ChartType == ResourceChartType.Focus && FocusMetric == 3 &&
                                   (VisibleMetrics & 8) != 0;
                Label(drawingContext, focusNetwork
                        ? RateText.Format(_networkScale * (4 - i) / 4)
                        : Strings.Format(Strings.PercentInteger, 100 - i * 25),
                    new Point(alternative ? plot.Left - 8 : 0, Math.Max(0, y - 7)), muted,
                    alternative ? 1 : 0);
            }

            for (var i = 0; i <= 4; i++)
            {
                var x = plot.Left + plot.Width * i / 4;
                drawingContext.DrawLine(CachedPen(line, 0.5), new Point(x, plot.Top), new Point(x, plot.Bottom));
                var text = i == 0 ? Strings.Format(Strings.MinAgo, _seconds / 60d) :
                    i == 4 ? Strings.Now : Ago(_seconds * (4 - i) / 4d);
                Label(drawingContext, text, new Point(x, plot.Bottom + 5), muted, i == 0 ? 0 : i == 4 ? 1 : 0.5);
            }
        }

        var network = Resource("NetworkAccent", accent);
        drawingContext.PushClip(new RectangleGeometry(alternative
            ? new Rect(0, plot.Top, ActualWidth, plot.Height) : plot));
        if (TileMetric is { } tileMetric)
        {
            var key = tileMetric switch
            {
                0 => "CpuChartAccent", 1 => "MemoryChartAccent", 2 => "DiskChartAccent",
                3 => "NetworkChartAccent", 4 => "GpuChartAccent", _ => "SecondaryDiskChartAccent"
            };
            DrawSeries(drawingContext, plot, Resource(key, accent), tileMetric);
        }
        else if (_process is not null || !ShowGrid)
        {
            var key = _processSort switch
            {
                ProcessSort.Gpu => "GpuChartAccent", ProcessSort.Memory => "MemoryChartAccent",
                ProcessSort.Io => "DiskChartAccent", _ => "CpuChartAccent"
            };
            DrawSeries(drawingContext, plot, Resource(key, accent), 0);
        }
        else if (alternative)
        {
            DrawAlternative(drawingContext, plot, accent, line, muted);
        }
        else
        {
            var keys = new[]
                { "CpuChartAccent", "MemoryChartAccent", "DiskChartAccent", "NetworkChartAccent",
                    "GpuChartAccent", "SecondaryDiskChartAccent" };
            // Paint every wash first so later series cannot tint an earlier trace.
            if (!SystemParameters.HighContrast)
                for (var metric = 0; metric < keys.Length; metric++)
                    if ((VisibleMetrics & (1 << metric)) != 0)
                        DrawSeries(drawingContext, plot, Resource(keys[metric], accent), metric, true);
            for (var metric = 0; metric < keys.Length; metric++)
                if ((VisibleMetrics & (1 << metric)) != 0)
                    DrawSeries(drawingContext, plot, Resource(keys[metric], accent), metric);
        }

        if (_selected is not null && _samples.Any(s => ReferenceEquals(s, _selected)) &&
            Moment(_selected) >= _end - _seconds)
        {
            var x = X(_selected, plot);
            drawingContext.DrawLine(CachedPen(muted, 1, DashStyles.Dash), new Point(x, plot.Top),
                new Point(x, plot.Bottom));
            if (alternative && ChartType != ResourceChartType.Heatmap && !_selected.IsGap)
                DrawAlternativeSelection(drawingContext, plot, x, _selected, accent);
            if (ShowGrid && _process is null && !alternative)
                for (var metric = 0; metric < 6; metric++)
                {
                    if ((VisibleMetrics & (1 << metric)) == 0 || _selected.IsGap) continue;
                    var value = metric == 3
                        ? NetworkBitsPerSecond(_selected.Resources) * 100 / _networkScale
                        : MetricValue(_selected, metric);
                    if (value is null) continue;
                    var key = metric switch
                    {
                        0 => "CpuChartAccent", 1 => "MemoryChartAccent", 2 => "DiskChartAccent",
                        3 => "NetworkChartAccent", 4 => "GpuChartAccent", _ => "SecondaryDiskChartAccent"
                    };
                    drawingContext.DrawEllipse(Resource("Raised", Brushes.Black), CachedPen(Resource(key, accent), 2),
                        new Point(x, plot.Bottom - Math.Clamp(value.Value, 0, 100) / 100 * plot.Height), 3.5, 3.5);
                }
        }

        drawingContext.Pop();
        if (ShowGrid && _process is null && _selected is not null && _samples.Any(s => ReferenceEquals(s, _selected)) &&
            Moment(_selected) >= _end - _seconds)
        {
            // Clamp the timestamp separately so its full text remains inside the chart at both ends.
            var text = $"{_selected.Timestamp:HH:mm:ss}";
            var formatted = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                LabelTypeface, 11, muted, VisualTreeHelper.GetDpi(this).PixelsPerDip);
            var x = Math.Clamp(X(_selected, plot) - formatted.Width / 2, plot.Left,
                Math.Max(plot.Left, plot.Right - formatted.Width));
            drawingContext.DrawRectangle(Resource("Raised", Brushes.Black), CachedPen(line, 1),
                new Rect(x - 2, plot.Bottom + 3, formatted.Width + 4, formatted.Height + 2));
            drawingContext.DrawText(formatted, new Point(x, plot.Bottom + 4));
        }

        if (ShowGrid && _process is null) DrawMarkers(drawingContext, plot);

        // Link utilization is nearly always ~0%, so the network trace uses its own throughput scale.
        if (ShowGrid && _process is null && !alternative && (VisibleMetrics & 8) != 0)
            Label(drawingContext, Strings.Format(Strings.NetworkScale0, RateText.Format(_networkScale)),
                new Point(plot.Right - 4, plot.Top + 2), network, 1);
    }

    private void DrawAlternative(DrawingContext dc, Rect plot, Brush accent, Brush line, Brush muted)
    {
        var metrics = MetricOrder.Where(i => (VisibleMetrics & (1 << i)) != 0).ToArray();
        if (metrics.Length == 0) return;
        if (ChartType == ResourceChartType.Focus)
        {
            var focus = metrics.Contains(FocusMetric) ? FocusMetric : metrics[0];
            var main = new Rect(plot.Left, plot.Top, plot.Width, plot.Height * 0.55);
            if (!SystemParameters.HighContrast)
                DrawSeries(dc, main, MetricBrush(focus, accent), focus, true);
            DrawSeries(dc, main, MetricBrush(focus, accent), focus);
            Label(dc, MetricName(focus), new Point(plot.Left + 5, plot.Top + 4), MetricTextBrush(focus, accent));
            var others = metrics.Where(i => i != focus).ToArray();
            for (var index = 0; index < others.Length; index++)
            {
                var lane = Lane(plot, index, others.Length, 0.59, 1);
                DrawLane(dc, lane, others[index], accent, line, muted, false, true);
            }
        }
        else
        {
            for (var index = 0; index < metrics.Length; index++)
            {
                var lane = Lane(plot, index, metrics.Length, 0, 1);
                DrawLane(dc, lane, metrics[index], accent, line, muted,
                    ChartType == ResourceChartType.Heatmap);
            }
        }

    }

    private void DrawAlternativeSelection(DrawingContext dc, Rect plot, double x, MonitorSample sample,
        Brush fallback)
    {
        var metrics = MetricOrder.Where(i => (VisibleMetrics & (1 << i)) != 0).ToArray();
        var focus = metrics.Contains(FocusMetric) ? FocusMetric : metrics.FirstOrDefault();
        var others = metrics.Where(i => i != focus).ToArray();
        for (var index = 0; index < metrics.Length; index++)
        {
            var metric = metrics[index];
            var value = metric == 3 ? NetworkBitsPerSecond(sample.Resources) * 100 / _networkScale
                : MetricValue(sample, metric);
            if (value is null) continue;
            Rect lane;
            if (ChartType == ResourceChartType.Focus)
                lane = metric == focus
                    ? new Rect(plot.Left, plot.Top, plot.Width, plot.Height * 0.55)
                    : Lane(plot, Array.IndexOf(others, metric), others.Length, 0.59, 1);
            else lane = Lane(plot, index, metrics.Length, 0, 1);
            if (ChartType != ResourceChartType.Focus || metric != focus)
                lane = new Rect(lane.Left, lane.Top + 3, lane.Width, Math.Max(1, lane.Height - 6));
            var y = lane.Bottom - Math.Clamp(value.Value, 0, 100) / 100 * lane.Height;
            dc.DrawEllipse(Resource("Raised", Brushes.Black), CachedPen(MetricBrush(metric, fallback), 2),
                new Point(x, y), 3.5, 3.5);
        }
    }

    private void DrawLane(DrawingContext dc, Rect lane, int metric, Brush accent, Brush line, Brush muted,
        bool heatmap, bool compact = false)
    {
        var color = MetricBrush(metric, accent);
        dc.DrawLine(CachedPen(line, 0.6), lane.TopLeft, lane.TopRight);
        Label(dc, MetricName(metric),
            new Point(lane.Left - 8, lane.Top + Math.Max(2, lane.Height / 2 - (compact ? 7 : 12))),
            MetricTextBrush(metric, accent), 1);
        if (!compact)
            Label(dc, metric == 3 ? RateText.Format(_networkScale) :
                Strings.Format(Strings.PercentInteger, 100),
                new Point(lane.Left - 8, lane.Top + Math.Max(14, lane.Height / 2 + 2)), muted, 1);
        var inset = new Rect(lane.Left, lane.Top + 3, lane.Width, Math.Max(1, lane.Height - 6));
        if (heatmap) DrawHeatmap(dc, inset, metric, color);
        else DrawSeries(dc, inset, color, metric);
    }

    private void DrawHeatmap(DrawingContext dc, Rect lane, int metric, Brush color)
    {
        var width = Math.Max(1, lane.Width / _seconds);
        ResourceSample? previousSource = null;
        foreach (var sample in _samples)
        {
            if (Moment(sample) < _end - _seconds) continue;
            var value = metric == 3
                ? NetworkBitsPerSecond(sample.Resources) * 100 / _networkScale
                : MetricValue(sample, metric);
            if (!sample.IsGap && value is { } amount &&
                (previousSource is null || SameSource(previousSource, sample.Resources, metric)))
            {
                var intensity = Math.Clamp((int)Math.Ceiling(amount / 100 * 12), 0, 12);
                dc.DrawRectangle(CachedFill(color, 0.08 + intensity * 0.075), null,
                    new Rect(Math.Max(lane.Left, X(sample, lane) - width / 2), lane.Top,
                        Math.Min(width + 0.5, lane.Right - Math.Max(lane.Left, X(sample, lane) - width / 2)),
                        lane.Height));
            }

            previousSource = sample.Resources;
        }
    }

    private static Rect Lane(Rect plot, int index, int count, double topFraction, double bottomFraction)
    {
        var height = plot.Height * (bottomFraction - topFraction) / count;
        return new Rect(plot.Left, plot.Top + plot.Height * topFraction + index * height, plot.Width, height);
    }

    private string MetricName(int metric) => metric < MetricLabels.Length ? MetricLabels[metric] : metric switch
    {
        0 => Strings.CPU, 1 => Strings.Memory, 2 or 5 => Strings.Disk,
        3 => Strings.Network, _ => Strings.GPU
    };

    private static string MetricBrushKey(int metric) => metric switch
    {
        0 => "CpuAccent", 1 => "MemoryAccent", 2 => "DiskAccent", 3 => "NetworkAccent",
        4 => "GpuAccent", _ => "SecondaryDiskAccent"
    };

    private static string ChartBrushKey(string key) => key switch
    {
        "CpuAccent" => "CpuChartAccent", "MemoryAccent" => "MemoryChartAccent",
        "DiskAccent" => "DiskChartAccent", "NetworkAccent" => "NetworkChartAccent",
        "GpuAccent" => "GpuChartAccent", "SecondaryDiskAccent" => "SecondaryDiskChartAccent",
        _ => key
    };

    private Brush MetricBrush(int metric, Brush fallback) => Resource(metric switch
    {
        0 => "CpuChartAccent", 1 => "MemoryChartAccent", 2 => "DiskChartAccent",
        3 => "NetworkChartAccent", 4 => "GpuChartAccent", _ => "SecondaryDiskChartAccent"
    }, fallback);

    private Brush MetricTextBrush(int metric, Brush fallback) => Resource(MetricBrushKey(metric), fallback);

    private void DrawMarkers(DrawingContext drawingContext, Rect plot)
    {
        var surface = Resource("PlotSurface", Brushes.Black);
        var text = Resource("Text", Brushes.White);
        foreach (var marker in Markers)
        {
            if (MarkerX(marker, plot) is not { } x) continue;
            var triangle = new StreamGeometry();
            using (var context = triangle.Open())
            {
                context.BeginFigure(new Point(x - 6, 0), true, true);
                context.LineTo(new Point(x + 6, 0), true, false);
                context.LineTo(new Point(x, plot.Top + 3), true, false);
            }

            triangle.Freeze();
            drawingContext.DrawGeometry(Resource(ChartBrushKey(marker.BrushKey), Brushes.LimeGreen),
                marker.Selected ? CachedPen(text, 2) : CachedPen(surface, 1), triangle);
        }
    }

    private double? MarkerX(ChartMarker marker, Rect plot)
    {
        if (marker.Moment < _end - _seconds || marker.Moment > _end + 1) return null;
        return plot.Right - (_end - marker.Moment) / _seconds * plot.Width;
    }

    /// <summary>The marker under a point on the top edge, if any.</summary>
    internal ChartMarker? MarkerAt(Point point)
    {
        if (!ShowGrid || _process is not null || Markers.Count == 0) return null;
        var plot = Plot();
        if (point.Y > plot.Top + 6) return null;
        return Markers.Where(m => MarkerX(m, plot) is { } x && Math.Abs(x - point.X) <= 8)
            .MinBy(m => Math.Abs(MarkerX(m, plot)!.Value - point.X));
    }

    private static string Ago(double secondsAgo)
    {
        return secondsAgo < 120 ? Strings.Format(Strings.S, secondsAgo) : Strings.Format(Strings.Min, secondsAgo / 60);
    }

    /// <summary>Busier-direction network throughput in bits per second.</summary>
    internal static double? NetworkBitsPerSecond(ResourceSample resources)
    {
        return resources is { ReceiveBytesPerSecond: { } rx, SendBytesPerSecond: { } tx } ? Math.Max(rx, tx) * 8 : null;
    }

    /// <summary>Round the visible network peak up to a 1-2-5 step, at least 1 Mbps so idle traffic stays low.</summary>
    internal static double NetworkScale(IEnumerable<MonitorSample> visible)
    {
        var peak = visible.Where(s => !s.IsGap).Select(s => NetworkBitsPerSecond(s.Resources) ?? 0).DefaultIfEmpty()
            .Max();
        var target = Math.Max(peak, 1_000_000);
        var magnitude = Math.Pow(10, Math.Floor(Math.Log10(target)));
        var step = target / magnitude;
        return (step <= 1 ? 1 : step <= 2 ? 2 : step <= 5 ? 5 : 10) * magnitude;
    }

    private void DrawSeries(DrawingContext drawingContext, Rect plot, Brush accent, int metric, bool fillOnly = false)
    {
        _segment.Clear();
        double? previous = null;
        ResourceSample? previousSource = null;
        for (var sampleIndex = 0; sampleIndex < _samples.Count; sampleIndex++)
        {
            var sample = _samples[sampleIndex];
            if (Moment(sample) < _end - _seconds) continue;
            var value = _process is not null ? _processValues![sampleIndex]
                : metric == 3 ? NetworkBitsPerSecond(sample.Resources) * 100 / _networkScale
                : MetricValue(sample, metric);
            if (sample.IsGap || value is null || (previous is not null && Moment(sample) - previous.Value > 2.5) ||
                (previousSource is not null && !SameSource(previousSource, sample.Resources, metric)))
            {
                DrawSegment(drawingContext, _segment, plot.Bottom, accent, ShowGrid, metric, fillOnly);
                _segment.Clear();
            }

            if (value is not null && !sample.IsGap)
                _segment.Add(new Point(X(sample, plot),
                    plot.Bottom - Math.Clamp(value.Value, 0, ScaleMaximum) / ScaleMaximum * plot.Height));
            previous = Moment(sample);
            previousSource = sample.Resources;
        }

        DrawSegment(drawingContext, _segment, plot.Bottom, accent, ShowGrid, metric, fillOnly);
        _segment.Clear();
    }

    /// <summary>Whether two samples measured the same network link, volume or adapter, so their trace may join.</summary>
    private static bool SameSource(ResourceSample previous, ResourceSample current, int metric)
    {
        return metric switch
        {
            2 => previous.DiskName == current.DiskName,
            3 => previous.NetworkId == current.NetworkId && previous.LinkBitsPerSecond == current.LinkBitsPerSecond,
            4 => previous.GpuId == current.GpuId,
            5 => previous.SecondaryDiskName == current.SecondaryDiskName,
            _ => true
        };
    }

    internal static double? MetricValue(MonitorSample sample, int metric)
    {
        return metric switch
        {
            0 => sample.TotalCpuPercent,
            1 => sample.Resources.MemoryPercent,
            2 => sample.Resources.DiskActivePercent,
            3 => sample.Resources.NetworkPercent,
            4 => sample.Resources.GpuPercent,
            5 => sample.Resources.SecondaryDiskActivePercent,
            _ => null
        };
    }

    /// <inheritdoc/>
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!ShowGrid) return;
        if (MarkerAt(e.GetPosition(this)) is { } marker)
        {
            ToolTip = marker.Tooltip;
            return;
        }

        if (_selected is not null)
        {
            ToolTip = null;
            return;
        }

        var sample = Nearest(e.GetPosition(this).X);
        ToolTip = sample is null ? Strings.NoSampleInThisInterval : DescribeSample(sample);
    }

    internal void RefreshLanguage()
    {
        if (ShowGrid)
        {
            var sample = IsMouseOver && _selected is null ? Nearest(Mouse.GetPosition(this).X) : null;
            ToolTip = !IsMouseOver || _selected is not null ? null :
                sample is null ? Strings.NoSampleInThisInterval : DescribeSample(sample);
        }

        _labels.Clear();
        InvalidateVisual();
    }

    /// <inheritdoc/>
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        Focus();
        if (MarkerAt(e.GetPosition(this)) is { } marker)
        {
            ToolTip = null;
            MarkerSelected?.Invoke(this, marker);
            return;
        }

        SelectAt(e.GetPosition(this));
    }

    internal void SelectAt(Point point)
    {
        if (!ShowGrid || Nearest(point.X) is not { } sample) return;
        var plot = Plot();
        _selectionY = plot.Height > 0 ? Math.Clamp((point.Y - plot.Top) / plot.Height, 0, 1) : 0.5;
        ToolTip = null;
        SampleSelected?.Invoke(this, sample);
    }

    /// <summary>Select the newest recorded interval without depending on the pointer or chart size.</summary>
    internal void SelectLatest()
    {
        if (!ShowGrid || _samples.Count == 0) return;
        _selectionY = 0.5;
        ToolTip = null;
        SampleSelected?.Invoke(this, _samples[^1]);
    }

    /// <inheritdoc/>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!ShowGrid || _samples.Count == 0) return;
        if (e.Key == Key.End)
        {
            LiveRequested?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
            return;
        }

        if (e.Key is Key.Left or Key.Right && Keyboard.Modifiers == ModifierKeys.Control)
        {
            // Step between captures; the selected capture's own marker is skipped, since its peak precedes it.
            var current = _selected is null ? _end + 1 : Moment(_selected);
            var marker = e.Key == Key.Left
                ? Markers.LastOrDefault(m => !m.Selected && m.Moment < current)
                : Markers.FirstOrDefault(m => !m.Selected && m.Moment > current);
            if (marker is not null) MarkerSelected?.Invoke(this, marker);
            e.Handled = true;
            return;
        }

        if (e.Key is not (Key.Left or Key.Right or Key.Home)) return;
        var visible = _samples.Where(s => Moment(s) >= _end - _seconds).ToArray();
        var index = _selected is null
            ? visible.Length - 1
            : Array.FindIndex(visible, s => ReferenceEquals(s, _selected));
        index = e.Key == Key.Home ? 0 : Math.Clamp(index + (e.Key == Key.Left ? -1 : 1), 0, visible.Length - 1);
        SampleSelected?.Invoke(this, visible[index]);
        e.Handled = true;
    }

    private MonitorSample? Nearest(double x)
    {
        if (_samples.Count == 0) return null;
        var plot = Plot();
        if (plot.Width <= 0) return null;
        var target = _end - _seconds + Math.Clamp((x - plot.Left) / plot.Width, 0, 1) * _seconds;
        var nearest = _samples.MinBy(s => Math.Abs(Moment(s) - target));
        return nearest is not null && Math.Abs(Moment(nearest) - target) <= 2 ? nearest : null;
    }

    private Rect Plot()
    {
        if (!ShowGrid) return new Rect(0, 0, ActualWidth, ActualHeight);
        if (ChartType == ResourceChartType.Overlay)
            return new Rect(39, 8, Math.Max(0, ActualWidth - 41), Math.Max(0, ActualHeight - 33));

        var labelWidth = LabelWidth(Strings.Format(Strings.PercentInteger, 100));
        foreach (var metric in MetricOrder)
        {
            if ((VisibleMetrics & (1 << metric)) == 0) continue;
            labelWidth = Math.Max(labelWidth, LabelWidth(MetricName(metric)));
            if (metric == 3)
                labelWidth = Math.Max(labelWidth, LabelWidth(RateText.Format(_networkScale)));
        }

        var left = Math.Ceiling(labelWidth + 10);
        return new Rect(left, 8, Math.Max(0, ActualWidth - left - 2), Math.Max(0, ActualHeight - 33));
    }

    private double LabelWidth(string text)
    {
        var pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        if (_labelWidths.TryGetValue((text, pixelsPerDip), out var width)) return width;
        if (_labelWidths.Count >= 64) _labelWidths.Clear();
        width = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            LabelTypeface, 11, Brushes.Black, pixelsPerDip).Width;
        _labelWidths[(text, pixelsPerDip)] = width;
        return width;
    }

    private double X(MonitorSample sample, Rect plot)
    {
        return plot.Right - (_end - Moment(sample)) / _seconds * plot.Width;
    }

    /// <summary>Map an observation to monotonic seconds for plotting; support wall-time-only imported fixtures.</summary>
    /// <param name="sample">Observation to position on the timeline.</param>
    /// <returns>Seconds on the observation clock.</returns>
    internal static double Moment(MonitorSample sample)
    {
        return ProcessRanking.Moment(sample);
    }

    private Brush Resource(string key, Brush fallback)
    {
        return TryFindResource(key) as Brush ?? fallback;
    }

    /// <summary>Draw a label at the requested anchor.</summary>
    /// <param name="dc">Drawing context for the label.</param>
    /// <param name="text">Text to draw.</param>
    /// <param name="origin">Anchor point for the label.</param>
    /// <param name="brush">Text foreground.</param>
    /// <param name="align">
    ///     Horizontal anchor: 0 places text right of the origin, 0.5 centers it and 1 ends it at the origin.
    /// </param>
    private void Label(DrawingContext dc, string text, Point origin, Brush brush, double align = 0)
    {
        var pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        // Axis labels rarely change, and shaping text is the costly part of drawing them.
        if (!_labels.TryGetValue((text, brush, pixelsPerDip), out var formatted))
        {
            if (_labels.Count >= 64) _labels.Clear();
            formatted = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, LabelTypeface,
                11, brush, pixelsPerDip);
            _labels[(text, brush, pixelsPerDip)] = formatted;
        }

        dc.DrawText(formatted, origin with { X = origin.X - formatted.Width * align });
    }

    /// <summary>A frozen pen reused across redraws; theme changes replace the brush and so the key.</summary>
    private Pen CachedPen(Brush brush, double thickness, DashStyle? dashStyle = null)
    {
        if (_pens.TryGetValue((brush, thickness, dashStyle), out var pen)) return pen;
        if (_pens.Count >= 64) _pens.Clear();
        // Freezing a pen freezes its brush; copy a theme brush rather than freeze the shared resource.
        pen = new Pen(brush.IsFrozen ? brush : brush.CloneCurrentValue(), thickness);
        if (dashStyle is not null) pen.DashStyle = dashStyle;
        pen.Freeze();
        return _pens[(brush, thickness, dashStyle)] = pen;
    }

    private Brush CachedFill(Brush brush, double opacity)
    {
        if (_fills.TryGetValue((brush, opacity), out var fill)) return fill;
        if (_fills.Count >= 64) _fills.Clear();
        fill = brush.Clone();
        fill.Opacity = opacity;
        fill.Freeze();
        return _fills[(brush, opacity)] = fill;
    }

    internal static string DescribeSample(MonitorSample sample)
    {
        var r = sample.Resources;
        var diskRate = r.DiskBytesPerSecond is { } disk ? Strings.Format(Strings.DiskRateSuffix, disk / 1_000_000) : "";
        var secondDisk = r.SecondaryDiskName.Length > 0
            ? Strings.Format(Strings.SecondaryDiskHistoryLine, r.SecondaryDiskName,
                Percent(r.SecondaryDiskActivePercent),
                r.SecondaryDiskBytesPerSecond is { } secondRate
                    ? Strings.Format(Strings.DiskRateSuffix, secondRate / 1_000_000)
                    : "")
            : "";
        var networkRate = r is { ReceiveBytesPerSecond: { } rx, SendBytesPerSecond: { } tx }
            ? Strings.Format(Strings.RxTxMbps, rx * 8 / 1_000_000, tx * 8 / 1_000_000)
            : "";
        var network = NetworkBitsPerSecond(r) is { } bits ? RateText.Format(bits) : Strings.UnavailableLowercase;
        return Strings.Format(Strings.HistoricalIntervalCPUMemoryDiskNetworkOfLinkGPUClick, sample.Timestamp,
            Percent(sample.TotalCpuPercent), Percent(r.MemoryPercent), r.DiskName, Percent(r.DiskActivePercent),
            diskRate, secondDisk, r.NetworkName, network, Percent(r.NetworkPercent), networkRate, Percent(r.GpuPercent),
            r.GpuEngine);

        static string Percent(double? value)
        {
            return value is { } number
                ? Strings.Format(Strings.PercentOneDecimal, number)
                : Strings.UnavailableLowercase;
        }
    }

    private void DrawSegment(DrawingContext dc, List<Point> points, double baseline, Brush accent, bool overlay,
        int metric, bool fillOnly)
    {
        if (points.Count == 0) return;
        if (points.Count == 1)
        {
            if (!fillOnly) dc.DrawEllipse(accent, null, points[0], 1.5, 1.5);
            return;
        }

        // Sparklines carry about two samples per pixel; averaging into 2px bins keeps a readable trace instead of a solid block.
        if (!overlay) points = Bin(points, 2, _binned);
        // Faint washes add depth while keeping the grid and crossing traces readable.
        if ((!overlay || fillOnly) && (TileMetric is null || !SystemParameters.HighContrast))
        {
            var area = new StreamGeometry();
            using (var context = area.Open())
            {
                context.BeginFigure(new Point(points[0].X, baseline), true, true);
                context.PolyLineTo(points, true, false);
                context.LineTo(new Point(points[^1].X, baseline), true, false);
            }

            area.Freeze();
            dc.DrawGeometry(CachedFill(accent, TileMetric is not null ? 0.23 : overlay ? 0.08 : 0.14), null, area);
        }

        if (fillOnly) return;
        var trace = new StreamGeometry();
        // PolyLineTo copies the points, so one buffer serves every segment.
        if (_trace.Length < points.Count) _trace = new Point[Math.Max(points.Count, _trace.Length * 2)];
        points.CopyTo(1, _trace, 0, points.Count - 1);
        using (var context = trace.Open())
        {
            context.BeginFigure(points[0], false, false);
            context.PolyLineTo(new ArraySegment<Point>(_trace, 0, points.Count - 1), true, false);
        }

        trace.Freeze();
        var dashStyle = overlay && metric == 5 ? DashStyles.Dash : null;
        if ((SystemParameters.HighContrast || UseSeriesPatterns) && (overlay || TileMetric is not null))
            dashStyle = metric switch
            {
                1 => DashStyles.Dash, 2 => DashStyles.Dot, 3 => DashStyles.DashDot, 4 => DashStyles.DashDotDot,
                5 => LongDash, _ => DashStyles.Solid
            };
        dc.DrawGeometry(null, CachedPen(accent, overlay ? 1.7 : 1.2, dashStyle), trace);
    }

    private static readonly DashStyle LongDash = Frozen(new DashStyle([6d, 2d], 0));

    private static DashStyle Frozen(DashStyle style)
    {
        style.Freeze();
        return style;
    }

    private static List<Point> Bin(List<Point> points, double width, List<Point> binned)
    {
        binned.Clear();
        for (var i = 0; i < points.Count;)
        {
            var bin = Math.Floor(points[i].X / width);
            double x = 0, y = 0;
            var count = 0;
            for (; i < points.Count && Math.Abs(Math.Floor(points[i].X / width) - bin) < 0.001; i++, count++)
            {
                x += points[i].X;
                y += points[i].Y;
            }

            binned.Add(new Point(x / count, y / count));
        }

        return binned;
    }
}
