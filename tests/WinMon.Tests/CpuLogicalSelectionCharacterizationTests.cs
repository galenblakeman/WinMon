using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NUnit.Framework;
using WinMon.Controls;
using WinMon.Monitoring;
using WinMon.Preferences;

namespace WinMon.Tests;

[TestFixture]
[NonParallelizable]
[Apartment(ApartmentState.STA)]
[SetCulture("en-US")]
[SetUICulture("en-US")]
public sealed class CpuLogicalSelectionCharacterizationTests
{
    private static MonitorSample Sample(int second, int processors) =>
        new(DateTimeOffset.UnixEpoch.AddSeconds(second), 25, [], 0,
            MonotonicTimestamp: Stopwatch.Frequency * (100 + second))
        {
            LogicalProcessorPercents = Enumerable.Range(0, processors).Select(index => (double?)(10 + index)).ToArray()
        };

    private static CpuGraph Graph(int width = 836, int height = 360)
    {
        var graph = new CpuGraph
        {
            ShowGrid = true, ChartType = ResourceChartType.LogicalProcessors, VisibleMetrics = 1
        };
        graph.Resources["PlotSurface"] = Brushes.White;
        graph.Resources["CpuChartAccent"] = Brushes.Lime;
        graph.Resources["Muted"] = Brushes.Magenta;
        graph.Resources["Line"] = Brushes.LightGray;
        graph.Resources["Text"] = Brushes.Black;
        Size(graph, width, height);
        return graph;
    }

    private static void Size(CpuGraph graph, int width, int height)
    {
        graph.Measure(new Size(width, height));
        graph.Arrange(new Rect(0, 0, width, height));
    }

    private static void SetSelected(CpuGraph graph, int processors = 4, int second = 50)
    {
        var first = Sample(40, processors);
        var middle = Sample(50, processors);
        var latest = Sample(60, processors);
        graph.SetData([first, middle, latest], 20, second == 40 ? first : second == 60 ? latest : middle);
    }

    private static (LineGeometry[] Cursors, byte[] Pixels, int Width, int Height) Render(CpuGraph graph)
    {
        graph.UpdateLayout();
        var width = (int)graph.ActualWidth;
        var height = (int)graph.ActualHeight;
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(graph);
        var cursors = Drawings(VisualTreeHelper.GetDrawing(graph)).OfType<GeometryDrawing>()
            .Where(drawing => drawing.Geometry is LineGeometry && drawing.Pen?.Brush is SolidColorBrush brush &&
                brush.Color == Colors.Magenta && drawing.Pen.DashStyle.Dashes.Count > 0)
            .Select(drawing => (LineGeometry)drawing.Geometry).ToArray();
        var pixels = new byte[width * height * 4];
        bitmap.CopyPixels(pixels, width * 4, 0);
        return (cursors, pixels, width, height);
    }

    private static IEnumerable<Drawing> Drawings(Drawing drawing)
    {
        if (drawing is DrawingGroup group)
            foreach (var child in group.Children)
                foreach (var leaf in Drawings(child)) yield return leaf;
        else yield return drawing;
    }

    private static void AssertCursors(CpuGraph graph, int processors, double fraction = 0.5)
    {
        var rendered = Render(graph);
        Assert.That(rendered.Cursors, Has.Length.EqualTo(processors));
        for (var index = 0; index < processors; index++)
        {
            var plot = graph.LogicalProcessorPlotBounds(index);
            Assert.That(plot, Is.Not.Null);
            var bounds = plot!.Value;
            var cursor = rendered.Cursors[index];
            var x = bounds.Left + bounds.Width * fraction;
            Assert.That(cursor.StartPoint.X, Is.EqualTo(x).Within(0.001));
            Assert.That(cursor.EndPoint.X, Is.EqualTo(x).Within(0.001));
            Assert.That(cursor.StartPoint.Y, Is.EqualTo(bounds.Top).Within(0.001));
            Assert.That(cursor.EndPoint.Y, Is.EqualTo(bounds.Bottom).Within(0.001));
            var colored = 0;
            for (var y = Math.Max(0, (int)bounds.Top); y <= Math.Min(rendered.Height - 1, (int)Math.Ceiling(bounds.Bottom)); y++)
                for (var column = Math.Max(0, (int)x - 2); column <= Math.Min(rendered.Width - 1, (int)x + 2); column++)
                {
                    var offset = (y * rendered.Width + column) * 4;
                    if (rendered.Pixels[offset] > rendered.Pixels[offset + 1] + 80 &&
                        rendered.Pixels[offset + 2] > rendered.Pixels[offset + 1] + 80) colored++;
                }
            Assert.That(colored, Is.GreaterThan(0), "Each processor cursor must also reach its rasterized plot.");
        }
        Assert.That(graph.LogicalProcessorPlotBounds(processors), Is.Null);
    }

    [TestCase(1, 40, 0)]
    [TestCase(1, 50, 0.5)]
    [TestCase(1, 60, 1)]
    [TestCase(4, 40, 0)]
    [TestCase(4, 50, 0.5)]
    [TestCase(4, 60, 1)]
    [TestCase(64, 40, 0)]
    [TestCase(64, 50, 0.5)]
    [TestCase(64, 60, 1)]
    public void EachProcessorCursorUsesItsOwnTimelineAtBothBoundariesAndTheMiddle(int processors, int second, double fraction)
    {
        var graph = Graph();
        SetSelected(graph, processors, second);
        AssertCursors(graph, processors, fraction);
    }

    [Test]
    public void DenseSixtyFourProcessorPlotsStillDrawCursorsAtExactlyTwoDipsHigh()
    {
        var graph = Graph(height: 81);
        SetSelected(graph, 64);
        Assert.That(graph.LogicalProcessorPlotBounds(0)!.Value.Height, Is.EqualTo(2));
        AssertCursors(graph, 64);
    }

    [Test]
    public void ProcessorCountUsesTheLargestVisibleObservationAndExcludesOlderCounts()
    {
        var graph = Graph();
        var selected = Sample(50, 2);
        graph.SetData([Sample(0, 64), Sample(40, 4), selected, Sample(60, 3)], 20, selected);
        AssertCursors(graph, 4);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void MissingPerProcessorReadingsAndGapsRetainTheSelectedTimeCursor(bool gap)
    {
        var graph = Graph();
        var selected = Sample(50, 0) with { IsGap = gap, TotalCpuPercent = null };
        graph.SetData([Sample(40, 4), selected, Sample(60, 4)], 20, selected);
        AssertCursors(graph, 4);
    }

    [Test]
    public void HidingCpuRemovesTheCursorsAndShowingItRestoresThem()
    {
        var graph = Graph();
        SetSelected(graph);
        AssertCursors(graph, 4);
        graph.VisibleMetrics = 2;
        SetSelected(graph);
        Assert.That(Render(graph).Cursors, Is.Empty);
        graph.VisibleMetrics = 1;
        SetSelected(graph);
        AssertCursors(graph, 4);
    }

    [TestCase("none")]
    [TestCase("foreign")]
    [TestCase("outside")]
    public void IneligibleSelectionsRemoveCursorsWithoutRemovingCpuTraces(string state)
    {
        var graph = Graph();
        SetSelected(graph);
        AssertCursors(graph, 4);
        var old = Sample(0, 4);
        var selected = Sample(50, 4);
        var choice = state switch { "none" => null, "foreign" => selected with { }, _ => old };
        graph.SetData([old, Sample(40, 4), selected, Sample(60, 4)], 20, choice);
        var rendered = Render(graph);
        Assert.That(rendered.Cursors, Is.Empty);
        var green = 0;
        for (var offset = 0; offset < rendered.Pixels.Length; offset += 4)
            if (rendered.Pixels[offset + 1] > rendered.Pixels[offset] + 80 &&
                rendered.Pixels[offset + 1] > rendered.Pixels[offset + 2] + 80) green++;
        Assert.That(green, Is.GreaterThan(0), "An empty chart must not satisfy the missing-cursor assertion.");
    }

    [Test]
    public void UnavailableProcessorInventoriesRemoveAllCursorsAndCellBounds()
    {
        var graph = Graph();
        SetSelected(graph);
        AssertCursors(graph, 4);
        SetSelected(graph, 0);
        Assert.That(Render(graph).Cursors, Is.Empty);
        Assert.That(graph.LogicalProcessorPlotBounds(0), Is.Null);
    }

    [TestCase(40, 360, true)]
    [TestCase(836, 73, false)]
    public void CellsTooNarrowOrShortForTheirPlotDoNotDrawCursors(int width, int height, bool narrow)
    {
        var graph = Graph();
        SetSelected(graph, 64);
        AssertCursors(graph, 64);
        Size(graph, width, height);
        SetSelected(graph, 64);
        var bounds = graph.LogicalProcessorPlotBounds(0)!.Value;
        Assert.That(narrow ? bounds.Width : bounds.Height, Is.LessThan(2));
        Assert.That(Render(graph).Cursors, Is.Empty);
    }

    [Test]
    public void WallClockChangesDoNotMoveCursorsFromTheirMonotonicTimePosition()
    {
        var graph = Graph();
        var selected = Sample(50, 4) with { Timestamp = DateTimeOffset.UnixEpoch.AddDays(-1) };
        var latest = Sample(60, 4) with { Timestamp = DateTimeOffset.UnixEpoch.AddDays(7) };
        graph.SetData([Sample(40, 4), selected, latest], 20, selected);
        AssertCursors(graph, 4);
    }

    [Test]
    public void ReplacingTheRecordingRebuildsTheCursorSetForItsNewProcessorCount()
    {
        var graph = Graph();
        SetSelected(graph, 4);
        AssertCursors(graph, 4);
        SetSelected(graph, 1);
        AssertCursors(graph, 1);
    }
}
