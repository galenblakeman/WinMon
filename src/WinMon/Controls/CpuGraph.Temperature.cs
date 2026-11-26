using System.Windows;
using System.Windows.Media;
using MonCore.Monitoring;
using WinMon.Preferences;

namespace WinMon.Controls;

public sealed partial class CpuGraph
{
    public TemperatureUnit TemperatureUnit { get; set; }
    internal const double TemperatureAxisMaximum = 150;
    private static readonly (CaptureMetric Metric, int Resource)[] TemperatureSeries =
    [
        (CaptureMetric.CpuTemperature, 0), (CaptureMetric.GpuTemperature, 4),
        (CaptureMetric.PrimaryDiskTemperature, 2), (CaptureMetric.SecondaryDiskTemperature, 5)
    ];

    internal static double? TemperatureValue(MonitorSample sample, CaptureMetric metric) =>
        sample.IsGap ? null : ThermalMetrics.Value(metric, sample.Resources);

    private void DrawTemperatures(DrawingContext dc, Rect plot, Brush line, Brush muted)
    {
        dc.DrawRectangle(Resource("PlotSurface", Brushes.Transparent), null, plot);
        for (var index = 0; index <= 3; index++)
        {
            var value = index * 50d;
            var y = plot.Bottom - value / TemperatureAxisMaximum * plot.Height;
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
            _segment.Clear();
            MonitorSample? previous = null;
            foreach (var sample in _samples)
            {
                if (Moment(sample) < _end - _seconds) continue;
                var value = TemperatureValue(sample, metric);
                if (value is null || (previous is not null && (Moment(sample) - Moment(previous) > 2.5 ||
                    ThermalMetrics.Identity(metric, sample.Resources) != ThermalMetrics.Identity(metric, previous.Resources))))
                {
                    DrawSegment(dc, _segment, plot.Bottom, brush, true, resource, false, false);
                    _segment.Clear();
                }
                if (value is { } celsius) _segment.Add(new Point(X(sample, plot), plot.Bottom - celsius / TemperatureAxisMaximum * plot.Height));
                previous = sample;
            }
            DrawSegment(dc, _segment, plot.Bottom, brush, true, resource, false, false);
            _segment.Clear();
            if (_selected is { } selected && SelectionInPlot && TemperatureValue(selected, metric) is { } temperature)
                dc.DrawEllipse(Resource("Raised", Brushes.Black), CachedPen(brush, 2),
                    new Point(X(selected, plot), plot.Bottom - temperature / TemperatureAxisMaximum * plot.Height), 3, 3);
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
