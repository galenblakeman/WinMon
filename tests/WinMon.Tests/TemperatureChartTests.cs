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

    private static (CpuGraph Graph, byte[] Pixels) Render(MonitorSample[] samples, TemperatureUnit unit = TemperatureUnit.Celsius,
        int mask = 1, int? focus = null, Brush? paletteBrush = null)
    {
        var graph = new CpuGraph
        {
            ShowGrid = true, ChartType = focus.HasValue ? ResourceChartType.Focus : ResourceChartType.Temperature,
            FocusMetric = focus ?? 0, TemperatureUnit = unit, VisibleMetrics = mask
        };
        graph.Resources["CpuChartAccent"] = paletteBrush ?? Brushes.Red;
        graph.Resources["Text"] = Brushes.White;
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

    [TestCase(0, 70, 80)]
    [TestCase(2, 30, 40)]
    [TestCase(4, 80, 90)]
    [TestCase(5, 40, 50)]
    public void FocusTemperatureAxisUsesOnlyTheFocusedSensorInTheVisibleWindow(int focus, int minimum, int maximum)
    {
        var samples = Enumerable.Range(1, 20).Select(second =>
        {
            var sample = Sample(second, second < 10 ? 120 : 75);
            return sample with
            {
                Resources = sample.Resources with
                {
                    DiskName = "C:", DiskTemperatureCelsius = 35,
                    SecondaryDiskName = "D:", SecondaryDiskTemperatureCelsius = 45,
                    GpuId = "selected", GpuThermals = [new("other", "", 120, null, null, null, null),
                        new("selected", "", 85, null, null, null, null)]
                }
            };
        }).ToArray();
        var focusChart = Render(samples, mask: 63, focus: focus).Graph;
        Assert.That(focusChart.TemperatureAxis, Is.EqualTo((minimum, maximum)));
        Assert.That(focusChart.PlotBounds.Right, Is.LessThan(580));
    }

    [TestCase(0)]
    [TestCase(1)]
    public void FocusTemperatureAxisLeavesWhenTheFocusedResourceHasNoVisibleReading(int focus)
    {
        var samples = Enumerable.Range(1, 20).Select(second =>
        {
            var sample = Sample(second, second < 10 ? 75 : null);
            return sample with { Resources = sample.Resources with { DiskName = "C:", DiskTemperatureCelsius = 35 } };
        }).ToArray();
        var graph = Render(samples, mask: 7, focus: focus).Graph;
        Assert.That(graph.TemperatureAxis, Is.EqualTo((0, 150)));
        Assert.That(graph.PlotBounds.Right, Is.EqualTo(598));
    }

    [Test]
    public void FocusTemperatureTraceUsesThePaletteAndRendersAContinuousLine()
    {
        if (SystemParameters.HighContrast) Assert.Ignore("The OS high contrast scheme supplies the chart colors.");
        var samples = Enumerable.Range(1, 10).Select(second => Sample(second) with { TotalCpuPercent = 10 }).ToArray();
        var red = Render(samples, focus: 0, paletteBrush: Brushes.Red);
        var blue = Render(samples, focus: 0, paletteBrush: Brushes.Blue);
        var plot = red.Graph.PlotBounds;
        var y = (int)Math.Floor(plot.Top + plot.Height * 0.55 / 2);
        // A 1-DIP line can straddle two pixel rows, so each column checks a three-row band.
        int ColoredPixels((CpuGraph Graph, byte[] Pixels) render, bool redTint) =>
            Enumerable.Range((int)plot.Left + 120, 160).Count(x => Enumerable.Range(y - 1, 3).Any(row =>
            {
                var index = (row * 600 + x) * 4;
                var dominant = render.Pixels[index + (redTint ? 2 : 0)];
                var other = render.Pixels[index + (redTint ? 0 : 2)];
                return dominant - other > 45;
            }));

        Assert.That(ColoredPixels(red, true), Is.EqualTo(160));
        Assert.That(ColoredPixels(blue, false), Is.EqualTo(160));
        Assert.That(ColoredPixels(red, false), Is.Zero);
    }

    private static bool RedAt((CpuGraph Graph, byte[] Pixels) rendered, double second, double celsius)
    {
        var plot = rendered.Graph.PlotBounds;
        var x = (int)Math.Round(plot.Right - (10 - second) / 10 * plot.Width);
        var (minimum, maximum) = rendered.Graph.TemperatureAxis;
        var y = (int)Math.Round(plot.Bottom - (celsius - minimum) / (maximum - minimum) * plot.Height);
        if (y < 1 || y >= 239) return false;
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
        Assert.That(celsius.Graph.TemperatureAxis, Is.EqualTo((70, 80)));
        Assert.That(fahrenheit.Graph.TemperatureAxis, Is.EqualTo((70, 80)));
        Assert.That(RedAt(celsius, 5, 75), Is.True, "75 C must appear halfway up the 70–80 C axis.");
        Assert.That(RedAt(celsius, 5, 100), Is.False, "CPU utilization must not become a temperature series.");
        Assert.That(RedAt(fahrenheit, 5, 75), Is.True, "Changing display units must preserve curve geometry.");
        Assert.That(fahrenheit.Pixels, Is.Not.EqualTo(celsius.Pixels), "The axis labels must change to Fahrenheit.");
    }

    [Test]
    public void TemperatureAxisFitsVisibleSensorsAndTimeWindowWithRoundedHeadroom()
    {
        var samples = Enumerable.Range(1, 20).Select(i =>
        {
            var sample = Sample(i, i < 10 ? 120 : i == 15 ? 54 : 46);
            return sample with { Resources = sample.Resources with { DiskName = "C:", DiskTemperatureCelsius = 36 } };
        }).ToArray();
        samples[12] = Sample(13, 145) with { IsGap = true };

        var rendered = Render(samples, mask: 1 | 4);
        Assert.That(rendered.Graph.TemperatureAxis, Is.EqualTo((30, 60)));
        rendered.Graph.VisibleMetrics = 4;
        rendered.Graph.SetData(samples, 10, null);
        Assert.That(rendered.Graph.TemperatureAxis, Is.EqualTo((30, 40)));
        rendered.Graph.VisibleMetrics = 1;
        rendered.Graph.SetData(samples, 5, null);
        Assert.That(rendered.Graph.TemperatureAxis, Is.EqualTo((40, 60)));
        rendered.Graph.SetData(samples, 4, null);
        Assert.That(rendered.Graph.TemperatureAxis, Is.EqualTo((40, 50)));
    }

    [Test]
    public void TemperatureAxisExpandsImmediatelyAndContractsInTimedSteps()
    {
        var graph = new CpuGraph { ShowGrid = true, ChartType = ResourceChartType.Temperature, VisibleMetrics = 1 };
        var samples = Enumerable.Range(1, 29).Select(i => Sample(i, i switch { 7 => 92, 8 => 20, _ => 48 })).ToArray();

        graph.SetData(samples[..6], 10, null);
        Assert.That(graph.TemperatureAxis, Is.EqualTo((40, 60)));
        graph.SetData(samples[..7], 10, null);
        Assert.That(graph.TemperatureAxis, Is.EqualTo((40, 100)));
        graph.SetData(samples[..8], 10, null);
        Assert.That(graph.TemperatureAxis, Is.EqualTo((10, 100)));
        graph.SetData(samples[..17], 10, null);
        Assert.That(graph.TemperatureAxis, Is.EqualTo((10, 100)), "Both spikes are still in the window.");
        graph.SetData(samples[..18], 10, null);
        Assert.That(graph.TemperatureAxis, Is.EqualTo((10, 100)));
        graph.SetData(samples[..22], 10, null);
        Assert.That(graph.TemperatureAxis, Is.EqualTo((20, 90)));
        graph.SetData(samples[..22], 10, samples[20]);
        Assert.That(graph.TemperatureAxis, Is.EqualTo((20, 90)), "Redrawing the same window must not advance contraction.");
        graph.SetData(samples[..27], 10, null);
        Assert.That(graph.TemperatureAxis, Is.EqualTo((30, 80)));
        graph.SetData(samples[..6], 10, null);
        Assert.That(graph.TemperatureAxis, Is.EqualTo((40, 60)), "A new earlier view should fit immediately.");
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
        Assert.That(unavailable.Graph.TemperatureAxis, Is.EqualTo((0, 150)));
        Assert.That(RedAt(unavailable, 5, 0), Is.False);
        var hidden = Render(Enumerable.Range(1, 10).Select(i => Sample(i)).ToArray(), mask: 0);
        Assert.That(hidden.Graph.TemperatureAxis, Is.EqualTo((0, 150)));
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
