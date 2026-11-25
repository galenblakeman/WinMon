using System.Windows;
using System.Windows.Input;
using NUnit.Framework;
using WinMon.Controls;
using WinMon.Monitoring;
using WinMon.Preferences;
using WinMon.Resources;

namespace WinMon.Tests;

[TestFixture, NonParallelizable, Apartment(ApartmentState.STA), SetCulture("en-US"), SetUICulture("en-US")]
public sealed class CpuMouseMoveCharacterizationTests
{
    private static MonitorSample Sample(int second, double cpu = 25, int processors = 2) =>
        new(DateTimeOffset.UnixEpoch.AddSeconds(second), cpu, [], 0)
        {
            LogicalProcessorPercents = Enumerable.Repeat<double?>(cpu, processors).ToArray(),
            Resources = new ResourceSample
            {
                UsedMemoryBytes = 50, TotalMemoryBytes = 100, DiskName = "C:", DiskActivePercent = 40,
                NetworkName = "owned-link", GpuPercent = 75, GpuEngine = "3D"
            }
        };

    private static CpuGraph Graph(ResourceChartType type, IReadOnlyList<MonitorSample> samples,
        MonitorSample? selected = null, double width = 441)
    {
        var graph = new CpuGraph { ShowGrid = true, ChartType = type };
        graph.SetData(samples, 20, selected);
        graph.Measure(new Size(width, 200));
        graph.Arrange(new Rect(0, 0, width, 200));
        graph.SampleSelected += (_, _) => Assert.Fail("Moving the mouse must not select history");
        graph.MarkerSelected += (_, _) => Assert.Fail("Moving the mouse must not select a capture");
        graph.LiveRequested += (_, _) => Assert.Fail("Moving the mouse must not request live mode");
        return graph;
    }

    private static void Move(CpuGraph graph)
    {
        // Detached visuals give a fixed origin without moving or overriding the system pointer.
        Assert.That(PresentationSource.FromVisual(graph), Is.Null);
        var input = new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.MouseMoveEvent };
        Assert.That(input.GetPosition(graph), Is.EqualTo(new Point(0, 0)));
        graph.RaiseEvent(input);
        Assert.That(input.Handled, Is.False);
    }

    private static string ExpectedSample(int second) =>
        $"00:01:{second - 60:00} · Historical interval\n" +
        "CPU 25.0%\nMemory 50.0%\nDisk C: 40.0%\n" +
        "Network owned-link unavailable · unavailable of link\n" +
        "GPU 75.0% · 3D\nClick to hold; top readouts stay live";

    [TestCase(false), TestCase(true)]
    public void MouseMoveWithHiddenGridPreservesTheExistingTooltipAndSelection(bool selected)
    {
        var first = Sample(100);
        var graph = Graph(ResourceChartType.Overlay, [first, Sample(120)], selected ? first : null);
        graph.ShowGrid = false;
        var tooltip = new object();
        graph.ToolTip = tooltip;
        Move(graph);
        Assert.That(graph.ToolTip, Is.SameAs(tooltip));
        Assert.That(graph.SelectionInPlot, Is.EqualTo(selected));
    }

    [TestCase(ResourceChartType.Overlay), TestCase(ResourceChartType.SmallMultiples)]
    [TestCase(ResourceChartType.Heatmap), TestCase(ResourceChartType.Focus), TestCase(ResourceChartType.LogicalProcessors)]
    public void MouseMoveWithSelectedHistoryClearsAStaleTooltipOutsideAProcessorPlot(ResourceChartType type)
    {
        var first = Sample(100);
        var graph = Graph(type, [first, Sample(120)], first);
        graph.ToolTip = "stale tooltip";
        Move(graph);
        Assert.That(graph.ToolTip, Is.Null);
        Assert.That(graph.SelectionInPlot, Is.True);
    }

    [TestCase(false), TestCase(true)]
    public void MouseMoveOverACaptureMarkerTakesPrecedenceOverHistoryTooltipRules(bool selected)
    {
        var first = Sample(100);
        var graph = Graph(ResourceChartType.LogicalProcessors, [first, Sample(120)], selected ? first : null);
        var marker = new ChartMarker(CpuGraph.Moment(first), "CpuChartAccent", "owned capture tooltip", false, new object());
        graph.Markers = [marker];
        Assert.That(graph.MarkerAt(new Point(0, 0)), Is.SameAs(marker), "Place the capture at the chart's actual start moment");
        graph.ToolTip = "stale tooltip";
        Move(graph);
        Assert.That(graph.ToolTip, Is.EqualTo("owned capture tooltip"));
        Assert.That(graph.Markers.Single(), Is.SameAs(marker));
        Assert.That(graph.SelectionInPlot, Is.EqualTo(selected));
    }

    [TestCase(100, true), TestCase(102, true), TestCase(103, false)]
    public void MouseMoveLiveTooltipUsesTheInclusiveTwoSecondNearestSampleBoundary(int firstSecond, bool found)
    {
        var graph = Graph(ResourceChartType.Overlay, [Sample(firstSecond), Sample(120, 99)]);
        graph.ToolTip = "stale tooltip";
        Move(graph);
        var text = ((string)graph.ToolTip).Replace("\r\n", "\n", StringComparison.Ordinal);
        Assert.That(text, Is.EqualTo(found ? ExpectedSample(firstSecond) : Strings.NoSampleInThisInterval));
        Assert.That(graph.SelectionInPlot, Is.False);
    }

    [TestCase(false), TestCase(true)]
    public void MouseMoveWithoutSamplesOrWithADegeneratePlotReplacesTheStaleTooltip(bool degenerate)
    {
        var graph = Graph(ResourceChartType.Overlay, degenerate ? new[] { Sample(100), Sample(120) } : [],
            width: degenerate ? 41 : 441);
        graph.ToolTip = "stale tooltip";
        Move(graph);
        Assert.That(graph.ToolTip, Is.EqualTo(Strings.NoSampleInThisInterval));
    }

    [TestCase(0, 2), TestCase(1, 2), TestCase(1, 0)]
    public void MouseMoveOutsideLogicalProcessorPlotsShowsTheMissingIntervalNotice(int visible, int processors)
    {
        var graph = Graph(ResourceChartType.LogicalProcessors,
            [Sample(100, processors: processors), Sample(120, processors: processors)]);
        graph.VisibleMetrics = visible;
        graph.ToolTip = "stale tooltip";
        Move(graph);
        Assert.That(graph.ToolTip, Is.EqualTo(Strings.NoSampleInThisInterval));
        Assert.That(graph.SelectionInPlot, Is.False);
    }
}
