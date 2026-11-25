using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MonCore.Monitoring;
using NUnit.Framework;
using WinMon.Controls;
using WinMon.Preferences;

namespace WinMon.Tests;

[TestFixture]
[NonParallelizable]
[Apartment(ApartmentState.STA)]
[SetCulture("en-US")]
[SetUICulture("en-US")]
public sealed class CpuAlternativeSelectionCharacterizationTests
{
    private const int Width = 800;
    private const int Height = 360;
    private static readonly Color[] ColorsByMetric =
        [Colors.Red, Colors.Lime, Colors.Blue, Colors.Yellow, Colors.Magenta, Colors.Cyan];

    private static MonitorSample Sample(int second = 15) =>
        new(DateTimeOffset.UnixEpoch.AddSeconds(second), 25, [], 0)
        {
            Resources = new ResourceSample
            {
                TotalMemoryBytes = 100, UsedMemoryBytes = 50, DiskActivePercent = 75,
                SecondaryDiskActivePercent = 10, ReceiveBytesPerSecond = 62_500,
                SendBytesPerSecond = 31_250, GpuPercent = 80
            }
        };

    private static CpuGraph Graph(ResourceChartType type, int visible = 63, int focus = 0)
    {
        var graph = new CpuGraph { ShowGrid = true, ChartType = type, VisibleMetrics = visible, FocusMetric = focus };
        string[] keys = ["CpuChartAccent", "MemoryChartAccent", "DiskChartAccent", "NetworkChartAccent",
            "GpuChartAccent", "SecondaryDiskChartAccent"];
        foreach (var index in Enumerable.Range(0, keys.Length))
        {
            var brush = new SolidColorBrush(ColorsByMetric[index]);
            brush.Freeze();
            graph.Resources[keys[index]] = brush;
        }
        graph.Resources["PlotSurface"] = Brushes.White;
        graph.Resources["Raised"] = Brushes.Black;
        graph.Measure(new Size(Width, Height));
        graph.Arrange(new Rect(0, 0, Width, Height));
        return graph;
    }

    private static void SetSelected(CpuGraph graph, MonitorSample selected) =>
        graph.SetData([Sample(10), selected, Sample(20)], 10, selected);

    private static (Rect Plot, (Color Color, Point Center)[] Markers, byte[] Pixels) Render(CpuGraph graph)
    {
        graph.UpdateLayout();
        var bitmap = new RenderTargetBitmap(Width, Height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(graph);
        var markers = Drawings(VisualTreeHelper.GetDrawing(graph)).OfType<GeometryDrawing>()
            // Isolated trace points are filled ellipses; selection markers have an outline.
            .Where(drawing => drawing.Geometry is EllipseGeometry && drawing.Pen is not null)
            .Select(drawing => (((SolidColorBrush)drawing.Pen.Brush).Color,
                ((EllipseGeometry)drawing.Geometry).Center)).ToArray();
        var pixels = new byte[Width * Height * 4];
        bitmap.CopyPixels(pixels, Width * 4, 0);
        return (graph.PlotBounds, markers, pixels);
    }

    private static IEnumerable<Drawing> Drawings(Drawing drawing)
    {
        if (drawing is DrawingGroup group)
            foreach (var child in group.Children)
                foreach (var leaf in Drawings(child)) yield return leaf;
        else yield return drawing;
    }

    private static void AssertMarkers(
        (Rect Plot, (Color Color, Point Center)[] Markers, byte[] Pixels) rendered,
        params (int Metric, double HeightFraction, double Offset)[] expected)
    {
        Assert.That(rendered.Markers.Select(marker => marker.Color),
            Is.EqualTo(expected.Select(item => ColorsByMetric[item.Metric])));
        for (var index = 0; index < expected.Length; index++)
        {
            var item = expected[index];
            var center = rendered.Markers[index].Center;
            Assert.That(center.X, Is.EqualTo(rendered.Plot.Left + rendered.Plot.Width / 2).Within(0.001));
            Assert.That(center.Y, Is.EqualTo(rendered.Plot.Top + rendered.Plot.Height * item.HeightFraction + item.Offset)
                .Within(0.001));
            var color = ColorsByMetric[item.Metric];
            var colored = 0;
            for (var y = Math.Max(0, (int)center.Y - 5); y <= Math.Min(Height - 1, (int)center.Y + 5); y++)
                for (var x = Math.Max(0, (int)center.X - 5); x <= Math.Min(Width - 1, (int)center.X + 5); x++)
                {
                    var offset = (y * Width + x) * 4;
                    if (rendered.Pixels[offset] == color.B && rendered.Pixels[offset + 1] == color.G &&
                        rendered.Pixels[offset + 2] == color.R && rendered.Pixels[offset + 3] == 255) colored++;
                }
            Assert.That(colored, Is.GreaterThan(0), "The expected metric marker must also reach the raster output.");
        }
    }

    private static void AssertNoMarkersOnPopulatedChart(CpuGraph graph)
    {
        var rendered = Render(graph);
        Assert.That(rendered.Markers, Is.Empty);
        var cpuPixels = 0;
        for (var offset = 0; offset < rendered.Pixels.Length; offset += 4)
            if (rendered.Pixels[offset + 2] > rendered.Pixels[offset + 1] + 40 &&
                rendered.Pixels[offset + 2] > rendered.Pixels[offset] + 40) cpuPixels++;
        Assert.That(cpuPixels, Is.GreaterThan(0), "A missing chart must not satisfy a missing-marker assertion.");
    }

    [Test]
    public void SmallMultiplesKeepMetricColorsAndTheSecondaryDiskBeforeNetworkAndGpu()
    {
        var graph = Graph(ResourceChartType.SmallMultiples);
        SetSelected(graph, Sample());
        AssertMarkers(Render(graph), (0, 0.125, -1.5), (1, 0.25, 0), (2, 0.375, 1.5),
            (5, 0.65, -2.4), (3, 0.75, 0), (4, 13d / 15, 1.8));
    }

    [Test]
    public void FocusEnlargesTheChosenMetricAndKeepsTheOtherMarkersInTheirOwnLanes()
    {
        var graph = Graph(ResourceChartType.Focus, focus: 4);
        SetSelected(graph, Sample());
        AssertMarkers(Render(graph), (0, 0.6515, -1.5), (1, 0.713, 0), (2, 0.7745, 1.5),
            (5, 0.9098, -2.4), (3, 0.959, 0), (4, 0.11, 0));
    }

    [TestCase(0)]
    [TestCase(4)]
    [TestCase(99)]
    public void HiddenOrUnknownFocusFallsBackToTheFirstVisibleMetric(int focus)
    {
        var graph = Graph(ResourceChartType.Focus, visible: 12, focus: focus);
        SetSelected(graph, Sample());
        AssertMarkers(Render(graph), (2, 0.1375, 0), (3, 0.795, 0));
    }

    [TestCase(-20, 0.55)]
    [TestCase(0, 0.55)]
    [TestCase(100, 0)]
    [TestCase(150, 0)]
    public void SingleMetricFocusClampsPercentagesWithoutRequiringOtherLanes(double cpu, double expectedFraction)
    {
        var graph = Graph(ResourceChartType.Focus, visible: 1);
        SetSelected(graph, Sample() with { TotalCpuPercent = cpu });
        AssertMarkers(Render(graph), (0, expectedFraction, 0));
    }

    [Test]
    public void MissingReadingsOmitTheirMarkersWithoutMovingTheAvailableGpuLane()
    {
        var graph = Graph(ResourceChartType.SmallMultiples);
        SetSelected(graph, Sample() with { TotalCpuPercent = null, Resources = new ResourceSample { GpuPercent = 80 } });
        AssertMarkers(Render(graph), (4, 13d / 15, 1.8));
    }

    [Test]
    public void AvailableZeroReadingsStillDrawAllSixMarkers()
    {
        var graph = Graph(ResourceChartType.SmallMultiples);
        SetSelected(graph, Sample() with
        {
            TotalCpuPercent = 0,
            Resources = new ResourceSample
            {
                TotalMemoryBytes = 100, UsedMemoryBytes = 0, DiskActivePercent = 0, SecondaryDiskActivePercent = 0,
                ReceiveBytesPerSecond = 0, SendBytesPerSecond = 0, GpuPercent = 0
            }
        });
        AssertMarkers(Render(graph), (0, 1d / 6, -3), (1, 1d / 3, -3), (2, 0.5, -3),
            (5, 2d / 3, -3), (3, 5d / 6, -3), (4, 1, -3));
    }

    [Test]
    public void NetworkUsesTheBusierDirectionAndVisibleNonGapPeakRatherThanLinkUtilization()
    {
        var graph = Graph(ResourceChartType.Focus, visible: 8, focus: 3);
        var selected = Sample();
        var huge = new ResourceSample { ReceiveBytesPerSecond = 1_000_000_000, SendBytesPerSecond = 1_000_000_000 };
        var peak = Sample(20) with { Resources = new ResourceSample { ReceiveBytesPerSecond = 250_000, SendBytesPerSecond = 0 } };
        graph.SetData([Sample(0) with { Resources = huge }, selected,
            Sample(17) with { IsGap = true, Resources = huge }, peak], 10, selected);
        // Selected500Kbps on a2Mbps scale is25%, leaving75% of the focus lane above its marker.
        AssertMarkers(Render(graph), (3, 0.4125, 0));
    }

    [TestCase(ResourceChartType.Focus, "none")]
    [TestCase(ResourceChartType.Focus, "foreign")]
    [TestCase(ResourceChartType.Focus, "outside")]
    [TestCase(ResourceChartType.Focus, "gap")]
    [TestCase(ResourceChartType.SmallMultiples, "none")]
    [TestCase(ResourceChartType.SmallMultiples, "foreign")]
    [TestCase(ResourceChartType.SmallMultiples, "outside")]
    [TestCase(ResourceChartType.SmallMultiples, "gap")]
    public void IneligibleSelectionsDoNotPaintValueMarkers(ResourceChartType type, string state)
    {
        var graph = Graph(type);
        var old = Sample(0);
        var sample = Sample() with { IsGap = state == "gap" };
        var selected = state switch { "none" => null, "foreign" => sample with { }, "outside" => old, _ => sample };
        graph.SetData([old, sample, Sample(20)], 10, selected);
        AssertNoMarkersOnPopulatedChart(graph);
    }

    [Test]
    public void HeatmapSelectionKeepsTheHeatmapWithoutAddingValueCircles()
    {
        var graph = Graph(ResourceChartType.Heatmap);
        SetSelected(graph, Sample());
        AssertNoMarkersOnPopulatedChart(graph);
    }

    [TestCase(ResourceChartType.Focus)]
    [TestCase(ResourceChartType.SmallMultiples)]
    public void NoVisibleMetricsProducesNoValueMarkers(ResourceChartType type)
    {
        var graph = Graph(type);
        SetSelected(graph, Sample());
        Assert.That(Render(graph).Markers, Has.Length.EqualTo(6));
        graph.VisibleMetrics = 0;
        SetSelected(graph, Sample());
        Assert.That(Render(graph).Markers, Is.Empty);
    }

    [Test]
    public void ReplacementReadingsRemoveOldMarkersAndRepositionTheRemainingGpuMarker()
    {
        var graph = Graph(ResourceChartType.SmallMultiples);
        SetSelected(graph, Sample());
        Assert.That(Render(graph).Markers, Has.Length.EqualTo(6));
        SetSelected(graph, Sample() with { TotalCpuPercent = null, Resources = new ResourceSample { GpuPercent = 20 } });
        AssertMarkers(Render(graph), (4, 29d / 30, -1.8));
    }
}
