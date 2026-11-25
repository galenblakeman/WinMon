using System.Diagnostics;
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
public sealed class CpuGuideCharacterizationTests
{
    private static CpuGraph Graph(ResourceChartType type = ResourceChartType.Overlay, int visible = 1, int focus = 0)
    {
        var graph = new CpuGraph { ShowGrid = true, ChartType = type, VisibleMetrics = visible, FocusMetric = focus };
        graph.Resources["Line"] = Brushes.Blue;
        graph.Resources["Muted"] = Brushes.Black;
        graph.Resources["PlotSurface"] = Brushes.White;
        graph.Resources["CpuChartAccent"] = Brushes.Red;
        graph.Resources["NetworkChartAccent"] = Brushes.Red;
        Size(graph, 240, 160);
        Update(graph);
        return graph;
    }

    private static void Size(CpuGraph graph, int width, int height)
    {
        graph.Measure(new Size(width, height));
        graph.Arrange(new Rect(0, 0, width, height));
    }

    private static void Update(CpuGraph graph)
    {
        var samples = Enumerable.Range(0, 3).Select(second => new MonitorSample(
            DateTimeOffset.UnixEpoch.AddSeconds(second), 50, [], 0,
            MonotonicTimestamp: (100 + second) * Stopwatch.Frequency)
        {
            Resources = new ResourceSample { ReceiveBytesPerSecond = 125_000, SendBytesPerSecond = 0 }
        }).ToArray();
        graph.SetData(samples, 120, null);
    }

    private static (LineGeometry[] Horizontal, LineGeometry[] Vertical, byte[] Pixels) Render(CpuGraph graph)
    {
        graph.UpdateLayout();
        var width = (int)graph.ActualWidth;
        var height = (int)graph.ActualHeight;
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(graph);
        var drawings = Drawings(VisualTreeHelper.GetDrawing(graph)).OfType<GeometryDrawing>()
            .Where(drawing => drawing.Geometry is LineGeometry && drawing.Pen?.Brush is SolidColorBrush brush &&
                brush.Color == Colors.Blue).ToArray();
        var horizontal = drawings.Where(drawing => drawing.Pen.Thickness == 0.7)
            .Select(drawing => (LineGeometry)drawing.Geometry).ToArray();
        var vertical = drawings.Where(drawing => drawing.Pen.Thickness == 0.5)
            .Select(drawing => (LineGeometry)drawing.Geometry).ToArray();
        var pixels = new byte[width * height * 4];
        bitmap.CopyPixels(pixels, width * 4, 0);
        return (horizontal, vertical, pixels);
    }

    private static IEnumerable<Drawing> Drawings(Drawing drawing)
    {
        if (drawing is DrawingGroup group)
            foreach (var child in group.Children)
                foreach (var leaf in Drawings(child)) yield return leaf;
        else yield return drawing;
    }

    private static void AssertGuides(CpuGraph graph, int horizontalCount, double heightFraction)
    {
        var rendered = Render(graph);
        var plot = graph.PlotBounds;
        Assert.That(rendered.Horizontal, Has.Length.EqualTo(horizontalCount));
        Assert.That(rendered.Vertical, Has.Length.EqualTo(5));
        for (var index = 0; index < horizontalCount; index++)
        {
            var line = rendered.Horizontal[index];
            var y = plot.Top + plot.Height * heightFraction * index / 4;
            AssertPoint(line.StartPoint, plot.Left, y);
            AssertPoint(line.EndPoint, plot.Right, y);
        }
        for (var index = 0; index < 5; index++)
        {
            var line = rendered.Vertical[index];
            var x = plot.Left + plot.Width * index / 4;
            AssertPoint(line.StartPoint, x, plot.Top);
            AssertPoint(line.EndPoint, x, plot.Bottom);
        }
        Assert.That(Colored(rendered.Pixels, true), Is.GreaterThan(0), "The grid must also reach raster output.");
    }

    private static void AssertPoint(Point point, double x, double y)
    {
        Assert.That(point.X, Is.EqualTo(x).Within(0.001));
        Assert.That(point.Y, Is.EqualTo(y).Within(0.001));
    }

    private static int Colored(byte[] pixels, bool blue)
    {
        var count = 0;
        for (var offset = 0; offset < pixels.Length; offset += 4)
        {
            var primary = pixels[offset + (blue ? 0 : 2)];
            if (primary > pixels[offset + 1] + 40 && primary > pixels[offset + (blue ? 2 : 0)] + 40) count++;
        }
        return count;
    }

    [TestCase(ResourceChartType.Overlay, 1, 0, 5, 1)]
    [TestCase(ResourceChartType.Focus, 1, 0, 5, 0.55)]
    [TestCase(ResourceChartType.Focus, 8, 3, 5, 0.55)]
    [TestCase(ResourceChartType.Focus, 1, 3, 5, 0.55)]
    [TestCase(ResourceChartType.SmallMultiples, 1, 0, 0, 1)]
    [TestCase(ResourceChartType.Heatmap, 1, 0, 0, 1)]
    public void EachModeKeepsItsGlobalValueAndTimeGuides(ResourceChartType type, int visible, int focus,
        int horizontal, double heightFraction) => AssertGuides(Graph(type, visible, focus), horizontal, heightFraction);

    [Test]
    public void HidingTheGridRemovesAxesWhileKeepingTheCpuTrace()
    {
        var graph = Graph();
        AssertGuides(graph, 5, 1);
        graph.ShowGrid = false;
        Update(graph);
        var rendered = Render(graph);
        Assert.That(rendered.Horizontal, Is.Empty);
        Assert.That(rendered.Vertical, Is.Empty);
        Assert.That(Colored(rendered.Pixels, true), Is.Zero);
        Assert.That(Colored(rendered.Pixels, false), Is.GreaterThan(0));
    }

    [TestCase(1, 160)]
    [TestCase(240, 1)]
    public void ViewsBelowTheMinimumDimensionRenderNoGuidesOrTrace(int width, int height)
    {
        var graph = Graph();
        AssertGuides(graph, 5, 1);
        Size(graph, width, height);
        Update(graph);
        var rendered = Render(graph);
        Assert.That(rendered.Horizontal, Is.Empty);
        Assert.That(rendered.Vertical, Is.Empty);
        Assert.That(Colored(rendered.Pixels, true), Is.Zero);
        Assert.That(Colored(rendered.Pixels, false), Is.Zero);
    }

    [Test]
    public void ChangingModesReplacesTheAxesWithoutMutatingEarlierDrawings()
    {
        var graph = Graph();
        AssertGuides(graph, 5, 1);
        var original = Render(graph).Horizontal[4];
        var start = original.StartPoint;
        var end = original.EndPoint;
        foreach (var mode in new[] { ResourceChartType.Focus, ResourceChartType.SmallMultiples, ResourceChartType.Heatmap })
        {
            graph.ChartType = mode;
            Update(graph);
            AssertGuides(graph, mode == ResourceChartType.Focus ? 5 : 0, mode == ResourceChartType.Focus ? 0.55 : 1);
        }
        Assert.That(original.StartPoint, Is.EqualTo(start));
        Assert.That(original.EndPoint, Is.EqualTo(end));
    }
}
