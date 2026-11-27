using System.Windows;
using System.Windows.Media;
using MonCore.Monitoring;
using WinMon.Preferences;

namespace WinMon.Controls;

public sealed partial class CpuGraph
{
    public TemperatureUnit TemperatureUnit { get; set; }
    private const int TemperatureAxisStep = 10;
    private const int TemperatureAxisPadding = 3;
    private const double TemperatureAxisShrinkInterval = 5;
    private const double TemperatureTickSpacing = 18;
    private (int Minimum, int Maximum)? _temperatureAxis;
    private (int Seconds, int Metrics, ResourceChartType Chart) _temperatureAxisContext;
    private double _temperatureAxisEnd;
    private double _temperatureAxisAdjustedAt;
    private CaptureMetric? _focusTemperatureMetric;
    private (Color Accent, Color Text, Brush? Brush) _focusTemperatureBrush;
    private bool HasFocusTemperature => ChartType == ResourceChartType.Focus &&
                                        _focusTemperatureMetric.HasValue && _temperatureAxis.HasValue;
    private const double FocusTemperaturePlotTop = 27;
    private static readonly DashStyle FocusTemperatureDash = Frozen(new DashStyle([4d, 3d], 0));
    internal (int Minimum, int Maximum) TemperatureAxis => _temperatureAxis ?? (0, 150);
    private static readonly (CaptureMetric Metric, int Resource)[] TemperatureSeries =
    [
        (CaptureMetric.CpuTemperature, 0), (CaptureMetric.GpuTemperature, 4),
        (CaptureMetric.PrimaryDiskTemperature, 2), (CaptureMetric.SecondaryDiskTemperature, 5)
    ];

    internal static double? TemperatureValue(MonitorSample sample, CaptureMetric metric) =>
        sample.IsGap ? null : ThermalMetrics.Value(metric, sample.Resources);

    private void UpdateTemperatureAxis()
    {
        _focusTemperatureMetric = null;
        var temperatureMask = 0;
        if (ShowGrid && _process is null && TileMetric is null)
        {
            if (ChartType == ResourceChartType.Temperature)
                temperatureMask = VisibleMetrics;
            else if (ChartType == ResourceChartType.Focus)
            {
                var focus = FocusedResource();
                foreach (var (metric, resource) in TemperatureSeries)
                    if (resource == focus)
                    {
                        _focusTemperatureMetric = metric;
                        temperatureMask = 1 << resource;
                        break;
                    }
            }
        }

        var context = (_seconds, temperatureMask, ChartType);
        var target = temperatureMask == 0 ? null : VisibleTemperatureBounds(temperatureMask);
        if (target is not { } bounds)
        {
            _temperatureAxis = null;
            return;
        }

        if (_temperatureAxis is not { } current || context != _temperatureAxisContext ||
            _end < _temperatureAxisEnd || _end - _temperatureAxisEnd > _seconds)
        {
            _temperatureAxis = bounds;
            _temperatureAxisAdjustedAt = _end;
        }
        else if (bounds.Minimum < current.Minimum || bounds.Maximum > current.Maximum)
        {
            _temperatureAxis = (Math.Min(current.Minimum, bounds.Minimum), Math.Max(current.Maximum, bounds.Maximum));
            _temperatureAxisAdjustedAt = _end;
        }
        else if (bounds == current)
        {
            _temperatureAxisAdjustedAt = _end;
        }
        else if (_end - _temperatureAxisAdjustedAt >= TemperatureAxisShrinkInterval)
        {
            // Advance by one 10 °C step per interval, including after a delayed refresh.
            _temperatureAxis = (Math.Min(current.Minimum + TemperatureAxisStep, bounds.Minimum),
                Math.Max(current.Maximum - TemperatureAxisStep, bounds.Maximum));
            _temperatureAxisAdjustedAt = _end;
        }
        _temperatureAxisContext = context;
        _temperatureAxisEnd = _end;
    }

    private (int Minimum, int Maximum)? VisibleTemperatureBounds(int temperatureMask)
    {
        var minimum = double.PositiveInfinity;
        var maximum = double.NegativeInfinity;
        foreach (var sample in _samples)
        {
            if (!InTemperatureWindow(sample) || sample.IsGap) continue;
            foreach (var (metric, resource) in TemperatureSeries)
            {
                if ((temperatureMask & (1 << resource)) == 0 || ThermalMetrics.Value(metric, sample.Resources) is not { } value)
                    continue;
                minimum = Math.Min(minimum, value);
                maximum = Math.Max(maximum, value);
            }
        }
        if (double.IsPositiveInfinity(minimum)) return null;
        return (Math.Max(0, (int)Math.Floor((minimum - TemperatureAxisPadding) / TemperatureAxisStep) * TemperatureAxisStep),
            (int)Math.Ceiling((maximum + TemperatureAxisPadding) / TemperatureAxisStep) * TemperatureAxisStep);
    }

    private bool InTemperatureWindow(MonitorSample sample)
    {
        var moment = Moment(sample);
        return moment >= _end - _seconds && moment <= _end;
    }

    private void DrawTemperatures(DrawingContext dc, Rect plot, Brush line, Brush muted)
    {
        dc.DrawRectangle(Resource("PlotSurface", Brushes.Transparent), null, plot);
        DrawTemperatureTicks(plot, TemperatureAxisStep, DrawTick);
        void DrawTick(int value)
        {
            var y = TemperatureY(value, plot);
            dc.DrawLine(CachedPen(line, .6), new Point(plot.Left, y), new Point(plot.Right, y));
            Label(dc, GaugeReadoutText.Temperature(value, TemperatureUnit), new Point(plot.Left - 6, y - 7), muted, 1);
        }
        var legendX = plot.Left;
        foreach (var (metric, resource) in TemperatureSeries)
        {
            if ((VisibleMetrics & (1 << resource)) == 0) continue;
            var brush = MetricBrush(resource, muted);
            var name = MetricName(resource);
            Label(dc, name, new Point(legendX, 5), brush);
            legendX += LabelWidth(name) + 20;
            DrawTemperatureTrace(dc, plot, brush, metric, resource);
            if (_selected is { } selected && SelectionInPlot)
                DrawTemperatureSelection(dc, plot, brush, metric, selected, X(selected, plot));
        }
        if (SelectionInPlot)
            dc.DrawLine(CachedPen(muted, 1, DashStyles.Dash), new Point(X(_selected!, plot), plot.Top), new Point(X(_selected!, plot), plot.Bottom));
        Label(dc, Ago(_seconds), new Point(plot.Left, plot.Bottom + 5), muted);
        Label(dc, Strings.Now, new Point(plot.Right, plot.Bottom + 5), muted, 1);
        DrawMarkers(dc, plot);
        DrawSelectedTimestamp(dc, plot, line, muted);
    }

    private double TemperatureY(double celsius, Rect plot)
    {
        var (minimum, maximum) = TemperatureAxis;
        return plot.Bottom - (celsius - minimum) / (maximum - minimum) * plot.Height;
    }

    private void DrawTemperatureTicks(Rect plot, int step, Action<int> drawTick)
    {
        var (minimum, maximum) = TemperatureAxis;
        var span = maximum - minimum;
        var minimumTickStep = TemperatureTickSpacing * span / plot.Height;
        var tickStep = Math.Max(span > 120 ? 50 : span > 60 ? 20 : step,
            (int)Math.Ceiling(minimumTickStep / step) * step);
        drawTick(maximum);
        if (plot.Height < TemperatureTickSpacing) return;
        drawTick(minimum);
        for (var value = minimum + tickStep; value <= maximum - minimumTickStep; value += tickStep)
            drawTick(value);
    }

    private void DrawTemperatureTrace(DrawingContext dc, Rect plot, Brush brush, CaptureMetric metric, int resource,
        DashStyle? dash = null)
    {
        _segment.Clear();
        MonitorSample? previous = null;
        foreach (var sample in _samples)
        {
            if (!InTemperatureWindow(sample)) continue;
            var value = TemperatureValue(sample, metric);
            if (value is null || (previous is not null && (Moment(sample) - Moment(previous) > 2.5 ||
                !SameTemperatureSource(previous.Resources, sample.Resources, resource))))
            {
                DrawSegment(dc, _segment, plot.Bottom, brush, true, resource, false, false, dash);
                _segment.Clear();
            }
            if (value is { } celsius) _segment.Add(new Point(X(sample, plot), TemperatureY(celsius, plot)));
            previous = value.HasValue ? sample : null;
        }
        DrawSegment(dc, _segment, plot.Bottom, brush, true, resource, false, false, dash);
        _segment.Clear();
    }

    private static bool SameTemperatureSource(ResourceSample previous, ResourceSample current, int resource) =>
        resource == 0
            ? previous.CpuTemperature is { } before && current.CpuTemperature is { } after &&
              before.Generation == after.Generation && before.HardwareId == after.HardwareId && before.SensorId == after.SensorId
            : SameSource(previous, current, resource);

    private void DrawTemperatureSelection(DrawingContext dc, Rect plot, Brush brush, CaptureMetric metric,
        MonitorSample selected, double x)
    {
        if (InTemperatureWindow(selected) && TemperatureValue(selected, metric) is { } temperature)
            dc.DrawEllipse(Resource("Raised", Brushes.Black), CachedPen(brush, 2),
                new Point(x, TemperatureY(temperature, plot)), 3, 3);
    }

    private double FocusTemperatureAxisWidth()
    {
        var (minimum, maximum) = TemperatureAxis;
        return Math.Ceiling(Math.Max(LabelWidth(GaugeReadoutText.Temperature(minimum, TemperatureUnit)),
            LabelWidth(GaugeReadoutText.Temperature(maximum, TemperatureUnit))) + 13);
    }

    private Brush FocusTemperatureBrush(int resource, Brush fallback)
    {
        var text = Resource("Text", fallback);
        if (SystemParameters.HighContrast || MetricBrush(resource, fallback) is not SolidColorBrush accent ||
            text is not SolidColorBrush foreground) return text;
        if (_focusTemperatureBrush is var (oldAccent, oldText, cached) && cached is not null &&
            oldAccent == accent.Color && oldText == foreground.Color) return cached;

        static byte Blend(byte accent, byte text) => (byte)((accent * 2 + text * 3) / 5);
        var color = Color.FromRgb(Blend(accent.Color.R, foreground.Color.R),
            Blend(accent.Color.G, foreground.Color.G), Blend(accent.Color.B, foreground.Color.B));
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        _focusTemperatureBrush = (accent.Color, foreground.Color, brush);
        return brush;
    }

    private void DrawFocusTemperatureGuides(DrawingContext dc, Rect plot, int focus, Brush accent)
    {
        var metric = _focusTemperatureMetric!.Value;
        var brush = FocusTemperatureBrush(focus, accent);
        dc.DrawLine(CachedPen(brush, 1), plot.TopRight, plot.BottomRight);
        DrawTemperatureTicks(plot, focus is 2 or 5 ? 5 : TemperatureAxisStep, value =>
        {
            var y = TemperatureY(value, plot);
            dc.DrawLine(CachedPen(brush, 1), new Point(plot.Right, y), new Point(plot.Right + 5, y));
            Label(dc, GaugeReadoutText.Temperature(value, TemperatureUnit), new Point(plot.Right + 8, y - 7), brush);
        });

        var sample = SelectionInPlot ? _selected! : _samples[^1];
        var activity = sample.IsGap || MetricValue(sample, focus) is not { } percent
            ? Strings.Unavailable : Strings.Format(Strings.PercentInteger, Math.Round(percent));
        var activityText = MetricName(focus) + "  " + activity;
        var temperatureText = FocusTemperatureText(sample, metric);
        var legendLeft = plot.Right - LabelWidth(temperatureText) - 26;
        if (legendLeft < plot.Left + LabelWidth(activityText) + 32)
        {
            temperatureText = GaugeReadoutText.Temperature(TemperatureValue(sample, metric), TemperatureUnit);
            legendLeft = plot.Right - LabelWidth(temperatureText) - 26;
        }
        dc.DrawLine(CachedPen(MetricBrush(focus, accent), 1.7), new Point(plot.Left, 10), new Point(plot.Left + 20, 10));
        Label(dc, activityText, new Point(plot.Left + 26, 3), MetricTextBrush(focus, accent));
        if (legendLeft >= plot.Left + LabelWidth(activityText) + 32)
        {
            dc.DrawLine(CachedPen(brush, 1.7, FocusTemperatureDash), new Point(legendLeft, 10), new Point(legendLeft + 20, 10));
            Label(dc, temperatureText, new Point(legendLeft + 26, 3), brush);
        }
    }

    private string FocusTemperatureText(MonitorSample sample, CaptureMetric metric)
    {
        var label = metric == CaptureMetric.CpuTemperature && TemperatureValue(sample, metric).HasValue &&
                    sample.Resources.CpuTemperature is { SensorName.Length: > 0 } cpu
            ? cpu.SensorName : Strings.DetailTemperature;
        return label + "  " + GaugeReadoutText.Temperature(TemperatureValue(sample, metric), TemperatureUnit);
    }

    internal string? FocusTemperatureDescription(MonitorSample? sample) =>
        HasFocusTemperature && sample is not null ? FocusTemperatureText(sample, _focusTemperatureMetric!.Value) : null;

    private string DescribeResourceSample(MonitorSample sample) => ChartType == ResourceChartType.Temperature
        ? DescribeTemperatureSample(sample)
        : DescribeSample(sample) + (FocusTemperatureDescription(sample) is { } temperature ? "\n" + temperature : "");

    private string DescribeTemperatureSample(MonitorSample sample) => sample.Timestamp.ToLocalTime().ToString("T") + "\n" +
        string.Join("\n", TemperatureSeries.Where(item => (VisibleMetrics & (1 << item.Resource)) != 0).Select(item =>
            MetricName(item.Resource) + (item.Metric == CaptureMetric.CpuTemperature && sample.Resources.CpuTemperature is { } cpu
                ? " · " + cpu.SensorName : "") + ": " + GaugeReadoutText.Temperature(TemperatureValue(sample, item.Metric), TemperatureUnit)));
}
