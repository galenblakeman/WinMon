using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MonCore.Monitoring;
using NUnit.Framework;
using WinMon.Controls;
using WinMon.Preferences;

namespace WinMon.Tests;

[TestFixture, Apartment(ApartmentState.STA)]
public sealed class TemperatureChartTests
{
    private static readonly Guid Generation = Guid.Parse("8ef3cc39-823c-4e76-939d-daa921ff7569");
    private static MonitorSample Sample(int second, double? temperature = 75, Guid? generation = null) => new(
        DateTimeOffset.UnixEpoch.AddSeconds(second), 100, [], 0, false, second * Stopwatch.Frequency)
    {
        Resources = new ResourceSample
        {
            CpuTemperature = temperature is { } value ? new(generation ?? Generation, "cpu", "CPU", "package", "CPU Package", value, "LibreHardwareMonitorLib 0.9.6") : null,
            CpuPerformanceLimitPercent = 62, CpuPerformanceLimitFlags = 2
        }
    };

    private static (CpuGraph Graph, byte[] Pixels) Render(MonitorSample[] samples, TemperatureUnit unit = TemperatureUnit.Celsius, int mask = 1)
    {
        var graph = new CpuGraph { ShowGrid = true, ChartType = ResourceChartType.Temperature, TemperatureUnit = unit, VisibleMetrics = mask };
        graph.Resources["CpuChartAccent"] = Brushes.Red;
        graph.Resources["Muted"] = Brushes.Gray;
        graph.Resources["Line"] = Brushes.Gray;
        graph.Measure(new Size(600, 240));
        graph.Arrange(new Rect(0, 0, 600, 240));
        graph.SetData(samples, 10, null);
        graph.UpdateLayout();
        var bitmap = new RenderTargetBitmap(600, 240, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(graph);
        var pixels = new byte[600 * 240 * 4];
        bitmap.CopyPixels(pixels, 600 * 4, 0);
        return (graph, pixels);
    }

    private static bool RedAt((CpuGraph Graph, byte[] Pixels) rendered, double second, double celsius)
    {
        var plot = rendered.Graph.PlotBounds;
        var x = (int)Math.Round(plot.Right - (10 - second) / 10 * plot.Width);
        var y = (int)Math.Round(plot.Bottom - celsius / 150 * plot.Height);
        for (var dx = -1; dx <= 1; dx++)
        for (var dy = -1; dy <= 1; dy++)
        {
            var index = ((y + dy) * 600 + x + dx) * 4;
            if (rendered.Pixels[index + 2] > 150 && rendered.Pixels[index + 1] < 100 && rendered.Pixels[index] < 100) return true;
        }
        return false;
    }

    [Test]
    public void TemperatureSeriesUsesItsOwnAxisAndUnitChangesOnlyLabels()
    {
        var samples = Enumerable.Range(1, 10).Select(i => Sample(i)).ToArray();
        var celsius = Render(samples);
        var fahrenheit = Render(samples, TemperatureUnit.Fahrenheit);
        Assert.That(RedAt(celsius, 5, 75), Is.True, "75 C must appear halfway up the 0–150 C axis.");
        Assert.That(RedAt(celsius, 5, 100), Is.False, "CPU utilization must not become a temperature series.");
        Assert.That(RedAt(fahrenheit, 5, 75), Is.True, "Changing display units must preserve curve geometry.");
        Assert.That(fahrenheit.Pixels, Is.Not.EqualTo(celsius.Pixels), "The axis labels must change to Fahrenheit.");
    }

    [TestCase("missing")]
    [TestCase("generation")]
    [TestCase("gap")]
    public void TemperatureSeriesBreaksAcrossMissingReadingsAndGenerationChanges(string interruption)
    {
        var other = Guid.NewGuid();
        var samples = Enumerable.Range(1, 10).Select(i => Sample(i,
            interruption == "missing" && i is 5 or 6 ? null : 75,
            interruption == "generation" && i >= 6 ? other : Generation) with
            { IsGap = interruption == "gap" && i is 5 or 6 }).ToArray();
        var rendered = Render(samples);
        Assert.That(RedAt(rendered, 3, 75), Is.True);
        Assert.That(RedAt(rendered, 5.5, 75), Is.False);
        Assert.That(RedAt(rendered, 8, 75), Is.True);
    }

    [Test]
    public void HiddenOrUnavailableTemperatureDoesNotDrawAZeroTrace()
    {
        var samples = Enumerable.Range(1, 10).Select(i => Sample(i, null)).ToArray();
        var unavailable = Render(samples);
        Assert.That(RedAt(unavailable, 5, 0), Is.False);
        var hidden = Render(Enumerable.Range(1, 10).Select(i => Sample(i)).ToArray(), mask: 0);
        Assert.That(RedAt(hidden, 5, 75), Is.False);
        Assert.That(CpuGraph.TemperatureValue(Sample(1) with { IsGap = true }, CaptureMetric.CpuTemperature), Is.Null);
    }

    [Test]
    public void HistoryTemperatureSelectionUsesTemperatureChartAndPreservesIncidentMetric()
    {
        var window = new FlightRecorderWindow([Sample(1), Sample(2, 90)], temperatureChartsEnabled: true);
        try
        {
            var selector = (ComboBox)window.FindName("Metric");
            selector.SelectedIndex = 6;
            var graph = (CpuGraph)window.FindName("Graph");
            Assert.That(graph.ChartType, Is.EqualTo(ResourceChartType.Temperature));
            Assert.That(window.Selection!.Resources.CpuTemperature!.Celsius, Is.EqualTo(90));
            Assert.That(window.Recording().Metric, Is.EqualTo(CaptureMetric.CpuTemperature));
            Assert.That(((TextBlock)window.FindName("Readings")).Text, Does.Contain("90 °C"));
            selector.SelectedIndex = 10;
            Assert.That(window.Recording().Metric, Is.EqualTo(CaptureMetric.CpuPerformanceLimited));
            Assert.That(((TextBlock)window.FindName("Readings")).Text,
                Does.Contain(WinMon.Resources.Strings.PerformanceLimited).And.Contain("62").And.Contain("0x2"));
        }
        finally { window.Close(); }
    }

    [Test]
    public void HistoryHidesTemperatureChartChoicesWhenServiceIsDisabledAndPreservesReadings()
    {
        var window = new FlightRecorderWindow([Sample(1), Sample(2, 90)], temperatureChartsEnabled: true);
        try
        {
            window.UpdateTrends(new(2, DateTimeOffset.UnixEpoch.AddMinutes(1), []));
            var metric = (ComboBox)window.FindName("Metric");
            var trendMetric = (ComboBox)window.FindName("TrendMetric");
            metric.SelectedIndex = trendMetric.SelectedIndex = 6;
            window.ConfigureTemperatureCharts(false);
            Assert.That(metric.Items.Count, Is.EqualTo(10));
            Assert.That(trendMetric.Items.Count, Is.EqualTo(10));
            Assert.That(metric.SelectedIndex, Is.EqualTo(0));
            Assert.That(trendMetric.SelectedIndex, Is.EqualTo(0));
            Assert.That(((CpuGraph)window.FindName("Graph")).ChartType, Is.EqualTo(ResourceChartType.Overlay));
            Assert.That(window.Recording().Observations[1].Sample.Resources.CpuTemperature!.Celsius, Is.EqualTo(90));
            window.ConfigureTemperatureCharts(true);
            Assert.That(metric.Items.Count, Is.EqualTo(14));
            Assert.That(trendMetric.Items.Count, Is.EqualTo(14));
        }
        finally { window.Close(); }
    }

    [Test]
    [SetCulture("en-US"), SetUICulture("en-US")]
    public void CaptureReportPreservesFractionalCelsiusThresholdAndLimitDuration()
    {
        var sample = Sample(1);
        var thermal = new CaptureFired(new(CaptureMetric.CpuTemperature, true, 85.25, 30), sample, sample, 90, [90]);
        Assert.That(CaptureReportText.Rule(thermal), Does.Contain("85.25 °C").And.Contain("30"));
        var limit = thermal with { Rule = new(CaptureMetric.CpuPerformanceLimited, true, 1, 10) };
        Assert.That(CaptureReportText.Rule(limit), Does.Contain(WinMon.Resources.Strings.PerformanceLimited).And.Contain("10"));
        Assert.That(CaptureReportText.AttributesProcesses(CaptureMetric.CpuTemperature), Is.False);
        Assert.That(CaptureReportText.AttributesProcesses(CaptureMetric.GpuThermalLimited), Is.False);
    }

    [Test]
    public async Task TemperatureChartPreferencesSurviveRestartWithIndependentRangeAndUnits()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        try
        {
            var preferences = PreferenceStore.Validate(new MonitorPreferences { RetentionSeconds = 3600, CpuTemperatureMonitoringEnabled = true })
                .SelectChart(ResourceChartType.Temperature) with { ChartRangeSeconds = 600, TemperatureUnit = TemperatureUnit.Fahrenheit };
            await new PreferenceStore(path).SaveAsync(preferences);
            var restored = await new PreferenceStore(path).LoadAsync();
            Assert.That(restored.ResourceChartType, Is.EqualTo(restored.TemperatureChartsEnabled ? ResourceChartType.Temperature : ResourceChartType.Overlay));
            Assert.That(restored.TemperatureUnit, Is.EqualTo(TemperatureUnit.Fahrenheit));
            if (restored.TemperatureChartsEnabled)
                Assert.That(restored.SelectChart(ResourceChartType.Overlay).SelectChart(ResourceChartType.Temperature).EffectiveChartRange, Is.EqualTo(600));
        }
        finally { File.Delete(path); }
    }
}
