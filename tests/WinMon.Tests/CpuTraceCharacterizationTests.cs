using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NUnit.Framework;
using WinMon.Controls;
using WinMon.Monitoring;

namespace WinMon.Tests;

[TestFixture]
[NonParallelizable]
[Apartment(ApartmentState.STA)]
[SetCulture("en-US")]
[SetUICulture("en-US")]
public sealed class CpuTraceCharacterizationTests
{
    private static readonly Color[] MetricColors =
        [Colors.Red, Colors.Lime, Colors.Blue, Colors.Yellow, Colors.Magenta, Colors.Cyan];
    private static readonly string[] MetricKeys =
        ["CpuChartAccent", "MemoryChartAccent", "DiskChartAccent", "NetworkChartAccent",
            "GpuChartAccent", "SecondaryDiskChartAccent"];

    private static MonitorSample Sample(int second, double value) =>
        new(DateTimeOffset.UnixEpoch.AddSeconds(second), value, [], 0,
            MonotonicTimestamp: Stopwatch.Frequency * (100 + second))
        {
            Resources = new ResourceSample
            {
                TotalMemoryBytes = 100, UsedMemoryBytes = (ulong)value, DiskActivePercent = value,
                SecondaryDiskActivePercent = value, ReceiveBytesPerSecond = value * 1_250,
                SendBytesPerSecond = 0, GpuPercent = value
            }
        };

    private static CpuGraph Graph(bool grid = true, int width = 240, int height = 160, int visible = 1,
        string? sparklineAccentKey = null)
    {
        var graph = new CpuGraph { ShowGrid = grid, VisibleMetrics = visible, SparklineAccentKey = sparklineAccentKey };
        for (var index = 0; index < MetricKeys.Length; index++)
        {
            var brush = new SolidColorBrush(MetricColors[index]);
            brush.Freeze();
            graph.Resources[MetricKeys[index]] = brush;
        }
        graph.Resources["PlotSurface"] = Brushes.White;
        graph.Resources["Accent"] = Brushes.Red;
        graph.Measure(new Size(width, height));
        graph.Arrange(new Rect(0, 0, width, height));
        return graph;
    }

    private static (GeometryDrawing[] Drawings, byte[] Pixels, int Width) Render(CpuGraph graph)
    {
        graph.UpdateLayout();
        var width = (int)graph.ActualWidth;
        var height = (int)graph.ActualHeight;
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(graph);
        var pixels = new byte[width * height * 4];
        bitmap.CopyPixels(pixels, width * 4, 0);
        return (Drawings(VisualTreeHelper.GetDrawing(graph)).OfType<GeometryDrawing>().ToArray(), pixels, width);
    }

    private static IEnumerable<Drawing> Drawings(Drawing drawing)
    {
        if (drawing is DrawingGroup group)
            foreach (var child in group.Children)
                foreach (var leaf in Drawings(child)) yield return leaf;
        else yield return drawing;
    }

    private static GeometryDrawing[] Strokes(GeometryDrawing[] drawings, int metric = 0) => drawings
        .Where(drawing => drawing.Brush is null && drawing.Pen?.Brush is SolidColorBrush brush &&
            brush.Color == MetricColors[metric]).ToArray();

    private static Point[] Points(GeometryDrawing drawing)
    {
        var path = PathGeometry.CreateFromGeometry(drawing.Geometry);
        Assert.That(path.Figures, Has.Count.EqualTo(1));
        return new[] { path.Figures[0].StartPoint }.Concat(path.Figures[0].Segments.SelectMany(segment => segment switch
        {
            PolyLineSegment line => line.Points.AsEnumerable(),
            LineSegment line => new[] { line.Point }.AsEnumerable(),
            _ => throw new AssertionException("Expected a straight trace.")
        })).ToArray();
    }

    private static void AssertPoints(Point[] actual, params Point[] expected)
    {
        Assert.That(actual, Has.Length.EqualTo(expected.Length));
        for (var index = 0; index < expected.Length; index++)
        {
            Assert.That(actual[index].X, Is.EqualTo(expected[index].X).Within(0.001));
            Assert.That(actual[index].Y, Is.EqualTo(expected[index].Y).Within(0.001));
        }
    }

    private static int RedPixels(byte[] pixels)
    {
        var count = 0;
        for (var offset = 0; offset < pixels.Length; offset += 4)
            if (pixels[offset + 2] > pixels[offset] + 80 && pixels[offset + 2] > pixels[offset + 1] + 80) count++;
        return count;
    }

    [Test]
    public void ContinuousOverlayRetainsEveryOriginalVertexAndPaintsItsTrace()
    {
        var graph = Graph();
        graph.SetData([Sample(0, 25), Sample(1, 50), Sample(2, 75)], 10, null);
        var rendered = Render(graph);
        var stroke = Strokes(rendered.Drawings).Single();
        var plot = graph.PlotBounds;
        AssertPoints(Points(stroke), new Point(plot.Left + plot.Width * 0.8, plot.Top + plot.Height * 0.75),
            new Point(plot.Left + plot.Width * 0.9, plot.Top + plot.Height * 0.5),
            new Point(plot.Right, plot.Top + plot.Height * 0.25));
        Assert.That(stroke.Pen.Thickness, Is.EqualTo(1.7));
        Assert.That(stroke.Pen.DashStyle.Dashes, Is.Empty);
        Assert.That(RedPixels(rendered.Pixels), Is.GreaterThan(0));
    }

    [TestCase("gap")]
    [TestCase("missing")]
    [TestCase("elapsed")]
    public void GapsMissingReadingsAndElapsedBreaksDoNotConnectSeparateTraceSegments(string kind)
    {
        var graph = Graph();
        var broken = Sample(2, 50) with { IsGap = kind == "gap", TotalCpuPercent = kind == "missing" ? null : 50 };
        MonitorSample[] samples = kind == "elapsed"
            ? [Sample(0, 25), Sample(1, 50), Sample(5, 75), Sample(6, 50)]
            : [Sample(0, 25), Sample(1, 50), broken, Sample(3, 75), Sample(4, 50)];
        graph.SetData(samples, 10, null);
        var rendered = Render(graph);
        var strokes = Strokes(rendered.Drawings);
        Assert.That(strokes, Has.Length.EqualTo(2));
        var plot = graph.PlotBounds;
        var firstStart = kind == "elapsed" ? 0.4 : 0.6;
        var firstEnd = kind == "elapsed" ? 0.5 : 0.7;
        AssertPoints(Points(strokes[0]), new Point(plot.Left + plot.Width * firstStart, plot.Top + plot.Height * 0.75),
            new Point(plot.Left + plot.Width * firstEnd, plot.Top + plot.Height * 0.5));
        AssertPoints(Points(strokes[1]), new Point(plot.Left + plot.Width * 0.9, plot.Top + plot.Height * 0.25),
            new Point(plot.Right, plot.Top + plot.Height * 0.5));
        Assert.That(RedPixels(rendered.Pixels), Is.GreaterThan(0));
        var inGap = 0;
        for (var y = (int)plot.Top + 2; y < (int)plot.Bottom - 2; y++)
            for (var x = (int)(plot.Left + plot.Width * firstEnd) + 3; x < (int)(plot.Left + plot.Width * 0.9) - 3; x++)
            {
                var offset = (y * rendered.Width + x) * 4;
                if (rendered.Pixels[offset + 2] > rendered.Pixels[offset] + 80 &&
                    rendered.Pixels[offset + 2] > rendered.Pixels[offset + 1] + 80) inGap++;
            }
        Assert.That(inGap, Is.Zero);
    }

    [Test]
    public void CompactBinningAveragesBothCoordinatesAndClosesTheWashAtItsBaseline()
    {
        var graph = Graph(false, 100, 40);
        graph.SetData([Sample(0, 10), Sample(1, 90), Sample(2, 30)], 600, null);
        var rendered = Render(graph);
        var stroke = Strokes(rendered.Drawings).Single();
        AssertPoints(Points(stroke), new Point(99.75, 20), new Point(100, 28));
        Assert.That(stroke.Pen.Thickness, Is.EqualTo(1.2));
        var fill = rendered.Drawings.Single(drawing => drawing.Brush is SolidColorBrush brush && brush.Color == Colors.Red);
        Assert.That(fill.Brush.Opacity, Is.EqualTo(0.14));
        Assert.That(PathGeometry.CreateFromGeometry(fill.Geometry).Figures.Single().IsClosed, Is.True);
        var area = Points(fill);
        Assert.That(area[0], Is.EqualTo(new Point(99.75, 40)));
        Assert.That(area, Does.Contain(new Point(100, 40)));
        Assert.That(RedPixels(rendered.Pixels), Is.GreaterThan(0));
    }

    [Test]
    public void CompactSamplesCollapsedIntoOneBinRetainTheExistingMissingStrokeBehavior()
    {
        MonitorSample[] samples = [Sample(0, 10), Sample(1, 90), Sample(2, 30)];
        var control = Graph(false, 100, 40);
        control.SetData(samples, 600, null);
        Assert.That(RedPixels(Render(control).Pixels), Is.GreaterThan(0));
        var graph = Graph(false, 101, 40);
        graph.SetData(samples, 600, null);
        var rendered = Render(graph);
        var stroke = Strokes(rendered.Drawings).Single();
        Assert.That(PathGeometry.CreateFromGeometry(stroke.Geometry).Figures.SelectMany(figure => figure.Segments), Is.Empty);
        Assert.That(RedPixels(rendered.Pixels), Is.Zero);
    }

    [Test]
    public void AStandaloneReadingPaintsADotAndUnavailableReplacementRemovesIt()
    {
        var graph = Graph(false, 100, 40);
        var sample = Sample(0, 50);
        graph.SetData([sample], 10, null);
        var rendered = Render(graph);
        var dot = rendered.Drawings.Single(drawing => drawing.Geometry is EllipseGeometry &&
            drawing.Brush is SolidColorBrush brush && brush.Color == Colors.Red);
        Assert.That(((EllipseGeometry)dot.Geometry).Center, Is.EqualTo(new Point(100, 20)));
        Assert.That(RedPixels(rendered.Pixels), Is.GreaterThan(0));
        graph.SetData([sample with { TotalCpuPercent = null }], 10, null);
        Assert.That(RedPixels(Render(graph).Pixels), Is.Zero);
    }

    [TestCase(0, "")]
    [TestCase(1, "2,2")]
    [TestCase(2, "0,2")]
    [TestCase(3, "2,2,0,2")]
    [TestCase(4, "2,2,0,2,0,2")]
    [TestCase(5, "6,2")]
    public void SeriesPatternsKeepTheMetricSpecificDashStyles(int metric, string expected)
    {
        var graph = Graph(visible: 63);
        graph.UseSeriesPatterns = true;
        graph.SetData([Sample(0, 25), Sample(1, 50), Sample(2, 75)], 10, null);
        var stroke = Strokes(Render(graph).Drawings, metric).Single();
        Assert.That(string.Join(",", stroke.Pen.DashStyle.Dashes), Is.EqualTo(expected));
        Assert.That(Points(stroke), Has.Length.EqualTo(3));
    }

    [Test]
    public void TheSecondaryDiskTraceRemainsDashedWithoutOptingIntoSeriesPatterns()
    {
        var graph = Graph(visible: 32);
        graph.SetData([Sample(0, 25), Sample(1, 50), Sample(2, 75)], 10, null);
        var stroke = Strokes(Render(graph).Drawings, 5).Single();
        Assert.That(stroke.Pen.DashStyle.Dashes, Is.Not.Empty);
        Assert.That(Points(stroke), Has.Length.EqualTo(3));
    }

    [TestCase(0, "")]
    [TestCase(4, "2,2,0,2,0,2")]
    public void TilePatternsApplyTheMetricStyleEvenWithoutTheGrid(int metric, string expected)
    {
        var graph = Graph(false);
        graph.TileMetric = metric;
        graph.UseSeriesPatterns = true;
        graph.SetData([Sample(0, 25), Sample(1, 50), Sample(2, 75)], 10, null);
        var stroke = Strokes(Render(graph).Drawings, metric).Single();
        Assert.That(string.Join(",", stroke.Pen.DashStyle.Dashes), Is.EqualTo(expected));
        Assert.That(stroke.Pen.Thickness, Is.EqualTo(1.2));
        Assert.That(Points(stroke), Has.Length.EqualTo(3));
    }

    private static Color SetMetricResource(CpuGraph graph, int metric, string resource)
    {
        graph.Resources["Accent"] = Brushes.Orange;
        graph.Resources["Raised"] = Brushes.Black;
        if (resource == "absent") graph.Resources.Remove(MetricKeys[metric]);
        if (resource == "wrong-type") graph.Resources[MetricKeys[metric]] = "not a brush";
        return resource == "brush" ? MetricColors[metric] : Colors.Orange;
    }

    private static void AssertColorNear((GeometryDrawing[] Drawings, byte[] Pixels, int Width) rendered,
        Point point, Color color)
    {
        var count = 0;
        for (var y = (int)point.Y - 5; y <= (int)point.Y + 5; y++)
            for (var x = (int)point.X - 5; x <= (int)point.X + 5; x++)
            {
                var offset = (y * rendered.Width + x) * 4;
                // Gridless tiles are transparent; compare premultiplied channels at the rendered alpha.
                var alpha = rendered.Pixels[offset + 3];
                if (alpha > 30 && Math.Abs(rendered.Pixels[offset] - color.B * alpha / 255d) <= 2 &&
                    Math.Abs(rendered.Pixels[offset + 1] - color.G * alpha / 255d) <= 2 &&
                    Math.Abs(rendered.Pixels[offset + 2] - color.R * alpha / 255d) <= 2) count++;
            }
        Assert.That(count, Is.GreaterThan(0), "The retained geometry must also paint the expected color.");
    }

    [Test, Combinatorial]
    public void TileTracesKeepEachMetricBrushAndItsFallback(
        [Values(0, 1, 2, 3, 4, 5)] int metric, [Values("brush", "absent", "wrong-type")] string resource)
    {
        var graph = Graph(false);
        graph.TileMetric = metric;
        var expected = SetMetricResource(graph, metric, resource);
        graph.SetData([Sample(0, 25), Sample(1, 50), Sample(2, 75)], 10, null);
        var rendered = Render(graph);
        var stroke = rendered.Drawings.Single(drawing => drawing.Brush is null && drawing.Geometry is StreamGeometry);
        var plot = graph.PlotBounds;
        var middle = new Point(plot.Left + plot.Width * 0.9, plot.Top + plot.Height * 0.5);
        Assert.That(((SolidColorBrush)stroke.Pen.Brush).Color, Is.EqualTo(expected));
        Assert.That(stroke.Pen.Thickness, Is.EqualTo(1.2));
        AssertPoints(Points(stroke), new Point(plot.Left + plot.Width * 0.8, plot.Top + plot.Height * 0.75),
            middle, new Point(plot.Right, plot.Top + plot.Height * 0.25));
        AssertColorNear(rendered, middle, expected);
    }

    [Test, Combinatorial]
    public void OverlaySelectionOutlinesKeepEachMetricBrushAndItsFallback(
        [Values(0, 1, 2, 3, 4, 5)] int metric, [Values("brush", "absent", "wrong-type")] string resource)
    {
        var graph = Graph(visible: 1 << metric);
        var expected = SetMetricResource(graph, metric, resource);
        var selected = Sample(1, 50);
        graph.SetData([Sample(0, 25), selected, Sample(2, 75)], 10, selected);
        var rendered = Render(graph);
        var marker = rendered.Drawings.Single(drawing => drawing.Geometry is EllipseGeometry && drawing.Pen is not null);
        var ellipse = (EllipseGeometry)marker.Geometry;
        var plot = graph.PlotBounds;
        Assert.That(((SolidColorBrush)marker.Pen.Brush).Color, Is.EqualTo(expected));
        Assert.That(((SolidColorBrush)marker.Brush).Color, Is.EqualTo(Colors.Black));
        Assert.That(marker.Pen.Thickness, Is.EqualTo(2));
        Assert.That(ellipse.RadiusX, Is.EqualTo(3.5));
        Assert.That(ellipse.RadiusY, Is.EqualTo(3.5));
        Assert.That(ellipse.Center.X, Is.EqualTo(plot.Left + plot.Width * 0.9).Within(0.001));
        Assert.That(ellipse.Center.Y, Is.EqualTo(plot.Top + plot.Height * 0.5).Within(0.001));
        AssertColorNear(rendered, ellipse.Center, expected);
    }

    [TestCase(-1)]
    [TestCase(6)]
    public void InvalidTileMetricsLeaveTheTraceEmptyAfterAValidTile(int metric)
    {
        var graph = Graph(false);
        graph.TileMetric = 5;
        MonitorSample[] samples = [Sample(0, 25), Sample(1, 50), Sample(2, 75)];
        graph.SetData(samples, 10, null);
        Assert.That(Strokes(Render(graph).Drawings, 5), Has.Length.EqualTo(1));
        graph.TileMetric = metric;
        graph.SetData(samples, 10, null);
        Assert.That(Render(graph).Drawings.Any(drawing => drawing.Geometry is StreamGeometry), Is.False);
    }

    [Test]
    public void ReplacingMetricAndRaisedResourcesUpdatesSelectionWithoutMutatingEarlierDrawing()
    {
        var graph = Graph();
        SetMetricResource(graph, 0, "brush");
        var selected = Sample(1, 50);
        MonitorSample[] samples = [Sample(0, 25), selected, Sample(2, 75)];
        graph.SetData(samples, 10, selected);
        var original = Render(graph).Drawings.Single(drawing => drawing.Geometry is EllipseGeometry && drawing.Pen is not null);
        graph.Resources["CpuChartAccent"] = Brushes.Purple;
        graph.Resources["Raised"] = Brushes.White;
        graph.SetData(samples, 10, selected);
        var rendered = Render(graph);
        var current = rendered.Drawings.Single(drawing => drawing.Geometry is EllipseGeometry && drawing.Pen is not null);
        Assert.That(((SolidColorBrush)current.Pen.Brush).Color, Is.EqualTo(Colors.Purple));
        Assert.That(((SolidColorBrush)current.Brush).Color, Is.EqualTo(Colors.White));
        Assert.That(((SolidColorBrush)original.Pen.Brush).Color, Is.EqualTo(Colors.Red));
        Assert.That(((SolidColorBrush)original.Brush).Color, Is.EqualTo(Colors.Black));
        Assert.That(current.Geometry.Bounds, Is.EqualTo(original.Geometry.Bounds));
        AssertColorNear(rendered, ((EllipseGeometry)current.Geometry).Center, Colors.Purple);
    }

    [Test]
    public void GrowingAndShrinkingTheSeriesRetainsOnlyCurrentVerticesAndEarlierDrawings()
    {
        var graph = Graph();
        graph.SetData([Sample(0, 25), Sample(1, 50)], 10, null);
        var first = Strokes(Render(graph).Drawings).Single();
        var original = Points(first);
        graph.SetData(Enumerable.Range(0, 6).Select(second => Sample(second, 10 + second * 10)).ToArray(), 10, null);
        Assert.That(Points(Strokes(Render(graph).Drawings).Single()), Has.Length.EqualTo(6));
        graph.SetData([Sample(0, 75), Sample(1, 50), Sample(2, 25)], 10, null);
        Assert.That(Points(Strokes(Render(graph).Drawings).Single()), Has.Length.EqualTo(3));
        AssertPoints(Points(first), original);
    }

    [Test]
    public void OverlayPaintsEveryWashBeforeAnyTraceThenTheCursorAndSelectionOutlines()
    {
        var graph = Graph(visible: 63);
        graph.Resources["Muted"] = Brushes.White;
        var selected = Sample(1, 50);
        graph.SetData([Sample(0, 25), selected, Sample(2, 75)], 10, selected);
        var rendered = Render(graph);
        var layers = rendered.Drawings.Where(drawing => drawing.Geometry is StreamGeometry).ToArray();
        var washes = SystemParameters.HighContrast ? 0 : 6;
        Assert.That(layers.Select(drawing => drawing.Brush is null ? "trace" : "wash"),
            Is.EqualTo(Enumerable.Repeat("wash", washes).Concat(Enumerable.Repeat("trace", 6))));
        Assert.That(layers.Skip(washes).Select(drawing => ((SolidColorBrush)drawing.Pen.Brush).Color),
            Is.EqualTo(MetricColors));
        var cursor = rendered.Drawings.Single(drawing => drawing.Geometry is LineGeometry &&
            drawing.Pen.Thickness == 1 && drawing.Pen.Brush is SolidColorBrush { Color: var color } && color == Colors.White);
        var outlines = rendered.Drawings.Where(drawing => drawing.Geometry is EllipseGeometry && drawing.Pen is not null).ToArray();
        Assert.That(outlines.Select(drawing => ((SolidColorBrush)drawing.Pen.Brush).Color), Is.EqualTo(MetricColors));
        Assert.That(Array.IndexOf(rendered.Drawings, layers[^1]), Is.LessThan(Array.IndexOf(rendered.Drawings, cursor)));
        Assert.That(Array.IndexOf(rendered.Drawings, cursor), Is.LessThan(Array.IndexOf(rendered.Drawings, outlines[0])));
        AssertColorNear(rendered, ((EllipseGeometry)outlines[^1].Geometry).Center, Colors.Cyan);
    }

    [TestCase("none", false, false)]
    [TestCase("clone", false, false)]
    [TestCase("outside", false, false)]
    [TestCase("gap", true, false)]
    [TestCase("hidden", true, false)]
    [TestCase("missing", true, false)]
    [TestCase("at-window", true, true)]
    [TestCase("latest", true, true)]
    public void SelectionEligibilityKeepsCursorAndOutlineRulesAcrossRedraws(string kind, bool cursorExpected, bool outlineExpected)
    {
        var graph = Graph();
        graph.Resources["Muted"] = Brushes.White;
        var selected = Sample(1, 50);
        MonitorSample[] normal = [Sample(0, 25), selected, Sample(2, 75)];
        graph.SetData(normal, 10, selected);
        var positive = Render(graph);
        Assert.That(positive.Drawings.Count(drawing => drawing.Geometry is EllipseGeometry && drawing.Pen is not null), Is.EqualTo(1));
        AssertColorNear(positive, ((EllipseGeometry)positive.Drawings.Single(drawing => drawing.Geometry is EllipseGeometry && drawing.Pen is not null).Geometry).Center, Colors.Red);
        var candidate = kind switch
        {
            "none" => null, "clone" => selected with { }, "outside" => Sample(-9, 50),
            "gap" => selected with { IsGap = true }, "missing" => selected with { TotalCpuPercent = null },
            "at-window" => Sample(-8, 50), "latest" => normal[^1], _ => selected
        };
        MonitorSample[] history = kind is "outside" or "at-window" ? [candidate!, normal[0], normal[^1]]
            : kind is "gap" or "missing" ? [normal[0], candidate!, normal[^1]] : normal;
        if (kind == "hidden") graph.VisibleMetrics = 0;
        graph.SetData(history, 10, candidate);
        var current = Render(graph);
        var cursors = current.Drawings.Where(drawing => drawing.Geometry is LineGeometry && drawing.Pen.Thickness == 1 &&
            drawing.Pen.Brush is SolidColorBrush { Color: var color } && color == Colors.White).ToArray();
        var outlines = current.Drawings.Where(drawing => drawing.Geometry is EllipseGeometry && drawing.Pen is not null).ToArray();
        Assert.That(cursors.Length, Is.EqualTo(cursorExpected ? 1 : 0));
        Assert.That(outlines.Length, Is.EqualTo(outlineExpected ? 1 : 0));
        if (outlineExpected)
        {
            var expectedX = kind == "latest" ? graph.PlotBounds.Right : graph.PlotBounds.Left;
            Assert.That(((EllipseGeometry)outlines.Single().Geometry).Center.X, Is.EqualTo(expectedX).Within(0.001));
            Assert.That(((SolidColorBrush)outlines.Single().Pen.Brush).Color, Is.EqualTo(Colors.Red));
        }
        graph.VisibleMetrics = 1;
        graph.SetData(normal, 10, selected);
        Assert.That(Render(graph).Drawings.Count(drawing => drawing.Geometry is EllipseGeometry && drawing.Pen is not null), Is.EqualTo(1));
    }

    [TestCase(ProcessSort.Cpu, 0, false)]
    [TestCase(ProcessSort.Memory, 1, false)]
    [TestCase(ProcessSort.Io, 2, false)]
    [TestCase(ProcessSort.Gpu, 4, false)]
    [TestCase(ProcessSort.Threads, 0, false)]
    [TestCase(ProcessSort.Handles, 0, false)]
    [TestCase(ProcessSort.Cpu, 0, true)]
    [TestCase(ProcessSort.Memory, 1, true)]
    [TestCase(ProcessSort.Io, 2, true)]
    [TestCase(ProcessSort.Gpu, 4, true)]
    [TestCase(ProcessSort.Threads, 0, true)]
    [TestCase(ProcessSort.Handles, 0, true)]
    public void ProcessSparklinesKeepTheSortBrushAndExplicitAccentOverride(ProcessSort sort, int metric, bool overridden)
    {
        var graph = Graph(false, sparklineAccentKey: overridden ? "OverrideTrace" : null);
        graph.Resources["OverrideTrace"] = Brushes.Orange;
        var process = new ProcessUsage(42, 100, "worker", 90);
        MonitorSample[] samples = [Sample(0, 90), Sample(1, 90), Sample(2, 90)];
        graph.SetProcessData(new ProcessGraphData(samples, [25, 50, 75], 100, sort), 10, process);
        var rendered = Render(graph);
        var stroke = rendered.Drawings.Single(drawing => drawing.Geometry is StreamGeometry && drawing.Brush is null);
        Assert.That(((SolidColorBrush)stroke.Pen.Brush).Color, Is.EqualTo(overridden ? Colors.Orange : MetricColors[metric]));
        AssertPoints(Points(stroke), new Point(192, 120), new Point(216, 80), new Point(240, 40));
        Assert.That(stroke.Pen.Thickness, Is.EqualTo(1.2));
        AssertColorNear(rendered, new Point(216, 80), overridden ? Colors.Orange : MetricColors[metric]);
    }
}
