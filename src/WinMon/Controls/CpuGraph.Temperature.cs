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
    private (int Seconds, int Metrics) _temperatureAxisContext;
    private double _temperatureAxisEnd;
    private double _temperatureAxisAdjustedAt;
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
        var context = (_seconds, VisibleMetrics);
        var target = VisibleTemperatureBounds();
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

    private (int Minimum, int Maximum)? VisibleTemperatureBounds()
    {
        var minimum = double.PositiveInfinity;
        var maximum = double.NegativeInfinity;
        foreach (var sample in _samples)
        {
            if (!InTemperatureWindow(sample) || sample.IsGap) continue;
            foreach (var (metric, resource) in TemperatureSeries)
            {
                if ((VisibleMetrics & (1 << resource)) == 0 || ThermalMetrics.Value(metric, sample.Resources) is not { } value)
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
        var (minimum, maximum) = TemperatureAxis;
        var span = maximum - minimum;
        var tickStep = span > 120 ? 50 : span > 60 ? 20 : TemperatureAxisStep;
        var minimumTickStep = TemperatureTickSpacing * span / plot.Height;
        tickStep = Math.Max(tickStep, (int)Math.Ceiling(minimumTickStep / TemperatureAxisStep) * TemperatureAxisStep);
        double Y(double celsius) => plot.Bottom - (celsius - minimum) / span * plot.Height;
        void DrawTick(int value)
        {
            var y = Y(value);
            dc.DrawLine(CachedPen(line, .6), new Point(plot.Left, y), new Point(plot.Right, y));
            Label(dc, GaugeReadoutText.Temperature(value, TemperatureUnit), new Point(plot.Left - 6, y - 7), muted, 1);
        }
        DrawTick(minimum);
        for (var value = minimum + tickStep; value <= maximum - minimumTickStep; value += tickStep)
            DrawTick(value);
        DrawTick(maximum);

        var legendX = plot.Left;
        foreach (var (metric, resource) in TemperatureSeries)
        {
            if ((VisibleMetrics & (1 << resource)) == 0) continue;
            var brush = MetricBrush(resource, muted);
            var name = MetricName(resource);
            Label(dc, name, new Point(legendX, 5), brush);
            legendX += LabelWidth(name) + 20;
            _segment.Clear();
            MonitorSample? previous = null;
            foreach (var sample in _samples)
            {
                if (!InTemperatureWindow(sample)) continue;
                var value = TemperatureValue(sample, metric);
                if (value is null || (previous is not null && (Moment(sample) - Moment(previous) > 2.5 ||
                    ThermalMetrics.Identity(metric, sample.Resources) != ThermalMetrics.Identity(metric, previous.Resources))))
                {
                    DrawSegment(dc, _segment, plot.Bottom, brush, true, resource, false, false);
                    _segment.Clear();
                }
                if (value is { } celsius) _segment.Add(new Point(X(sample, plot), Y(celsius)));
                previous = sample;
            }
            DrawSegment(dc, _segment, plot.Bottom, brush, true, resource, false, false);
            _segment.Clear();
            if (_selected is { } selected && SelectionInPlot && InTemperatureWindow(selected) &&
                TemperatureValue(selected, metric) is { } temperature)
                dc.DrawEllipse(Resource("Raised", Brushes.Black), CachedPen(brush, 2),
                    new Point(X(selected, plot), Y(temperature)), 3, 3);
        }
        if (SelectionInPlot)
            dc.DrawLine(CachedPen(muted, 1, DashStyles.Dash), new Point(X(_selected!, plot), plot.Top), new Point(X(_selected!, plot), plot.Bottom));
        Label(dc, Ago(_seconds), new Point(plot.Left, plot.Bottom + 5), muted);
        Label(dc, Strings.Now, new Point(plot.Right, plot.Bottom + 5), muted, 1);
        DrawMarkers(dc, plot);
        DrawSelectedTimestamp(dc, plot, line, muted);
    }

    private string DescribeTemperatureSample(MonitorSample sample) => sample.Timestamp.ToLocalTime().ToString("T") + "\n" +
        string.Join("\n", TemperatureSeries.Where(item => (VisibleMetrics & (1 << item.Resource)) != 0).Select(item =>
            MetricName(item.Resource) + (item.Metric == CaptureMetric.CpuTemperature && sample.Resources.CpuTemperature is { } cpu
                ? " · " + cpu.SensorName : "") + ": " + GaugeReadoutText.Temperature(TemperatureValue(sample, item.Metric), TemperatureUnit)));
}
