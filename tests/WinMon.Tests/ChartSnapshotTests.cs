using System.Threading;
using System.Diagnostics;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using NUnit.Framework;
using WinMon.Controls;
using WinMon.Monitoring;
using WinMon.Preferences;

namespace WinMon.Tests;

[TestFixture]
[Apartment(ApartmentState.STA)]
public sealed class ChartSnapshotTests
{
    [Test]
    public void AutomationPeer_ExposesAttachedChartNameWithoutChildren()
    {
        var graph = new CpuGraph();
        AutomationProperties.SetName(graph, "CPU · 16 logical processors");

        var peer = UIElementAutomationPeer.CreatePeerForElement(graph)!;

        Assert.Multiple(() =>
        {
            Assert.That(peer.GetName(), Is.EqualTo("CPU · 16 logical processors"));
            Assert.That(peer.GetChildren(), Is.Null);
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void SelectLatest_UsesSameRecordedSampleAsChartClick_AndCentersAnchorVertically(bool gap)
    {
        var first = new MonitorSample(DateTimeOffset.UtcNow, 10, [], 0);
        var latest = first with { Timestamp = first.Timestamp.AddSeconds(1), TotalCpuPercent = gap ? null : 70, IsGap = gap };
        var graph = new CpuGraph { ShowGrid = true };
        graph.Measure(new Size(600, 240));
        graph.Arrange(new Rect(0, 0, 600, 240));
        graph.SetData([first, latest], 120, null);
        MonitorSample? selected = null;
        graph.SampleSelected += (_, sample) => selected = sample;
        var plot = graph.PlotBounds;
        graph.SelectAt(new Point(plot.Right, plot.Top));
        var clicked = selected;

        graph.SelectLatest();
        graph.SetData([first, latest], 120, selected);

        Assert.Multiple(() =>
        {
            Assert.That(selected, Is.SameAs(latest).And.SameAs(clicked));
            Assert.That(graph.SelectionAnchor.X, Is.EqualTo(plot.Right));
            Assert.That(graph.SelectionAnchor.Y, Is.EqualTo(plot.Top + plot.Height / 2));
        });
    }

    [TestCase(ResourceChartType.SmallMultiples)]
    [TestCase(ResourceChartType.Heatmap)]
    [TestCase(ResourceChartType.Focus)]
    public void PlotBounds_VisibleLaneLabels_ExpandSharedRail(ResourceChartType chartType)
    {
        var graph = new CpuGraph
        {
            ShowGrid = true,
            ChartType = chartType,
            VisibleMetrics = 1,
            MetricLabels = ["CPU", "A much longer localized memory label"]
        };
        graph.Measure(new Size(600, 240));
        graph.Arrange(new Rect(0, 0, 600, 240));
        var cpuOnly = graph.PlotBounds;

        graph.VisibleMetrics = 3;
        var withMemory = graph.PlotBounds;

        Assert.Multiple(() =>
        {
            Assert.That(cpuOnly.Left, Is.LessThan(105));
            Assert.That(withMemory.Left, Is.GreaterThan(cpuOnly.Left));
            Assert.That(withMemory.Right, Is.EqualTo(cpuOnly.Right));
        });
    }

    [Test]
    public void SelectLatest_AfterRecordingAdvances_SelectsNewSampleWithoutChangingOldOne()
    {
        var original = new MonitorSample(DateTimeOffset.UtcNow, 10, [], 0);
        var next = original with { Timestamp = original.Timestamp.AddSeconds(1), TotalCpuPercent = 50 };
        var graph = new CpuGraph { ShowGrid = true };
        MonitorSample? selected = null;
        graph.SampleSelected += (_, sample) => selected = sample;
        graph.SetData([original], 120, null);
        graph.SelectLatest();
        var held = selected;
        graph.SetData([original, next], 120, held);
        Assert.That(selected, Is.SameAs(original));

        graph.SelectLatest();
        Assert.That(selected, Is.SameAs(next));
        Assert.That(held, Is.SameAs(original));
    }

    [TestCase(true)]
    [TestCase(false)]
    public void SelectLatest_WithoutSamplesOrOnSparkline_DoesNotSelect(bool showGrid)
    {
        var graph = new CpuGraph { ShowGrid = showGrid };
        var selections = 0;
        graph.SampleSelected += (_, _) => selections++;
        graph.SelectLatest();
        if (!showGrid)
        {
            graph.SetData([new MonitorSample(DateTimeOffset.UtcNow, null, [], 0, true)], 120, null);
            graph.SelectLatest();
        }
        Assert.That(selections, Is.Zero);
    }

    [Test]
    public void LogicalProcessorChart_ClicksWithinEveryColumnUseThatCellTimeline()
    {
        var first = new MonitorSample(DateTimeOffset.UnixEpoch, 10, [], 0, false, Stopwatch.Frequency)
            { LogicalProcessorPercents = [1, 2, 3, 4, 5] };
        var latest = first with
        {
            Timestamp = first.Timestamp.AddSeconds(120), MonotonicTimestamp = Stopwatch.Frequency * 121,
            LogicalProcessorPercents = [11, 12, 13, 14, 15]
        };
        var graph = new CpuGraph { ShowGrid = true, ChartType = ResourceChartType.LogicalProcessors };
        graph.Measure(new Size(600, 240));
        graph.Arrange(new Rect(0, 0, 600, 240));
        graph.SetData([first, latest], 120, null);
        Assert.That(graph.LogicalProcessorPlotBounds(0)!.Value.Top, Is.EqualTo(graph.PlotBounds.Top + 17));
        MonitorSample? selected = null;
        graph.SampleSelected += (_, sample) => selected = sample;

        for (var processor = 0; processor < 4; processor++)
        {
            var cell = graph.LogicalProcessorPlotBounds(processor)!.Value;
            graph.SelectAt(new Point(cell.Left + 1, cell.Top + cell.Height / 2));
            Assert.That(selected, Is.SameAs(first), $"CPU {processor}");
        }
    }

    [Test]
    public void LogicalProcessorChart_WithoutReadingsDoesNotSelectFromItsUnavailableBody()
    {
        var graph = new CpuGraph { ShowGrid = true, ChartType = ResourceChartType.LogicalProcessors };
        graph.Measure(new Size(600, 240));
        graph.Arrange(new Rect(0, 0, 600, 240));
        graph.SetData([new MonitorSample(DateTimeOffset.UtcNow, null, [], 0, true)], 120, null);
        var selections = 0;
        graph.SampleSelected += (_, _) => selections++;

        var plot = graph.PlotBounds;
        graph.SelectAt(new Point(plot.Left + plot.Width / 2, plot.Top + plot.Height / 2));

        Assert.That(selections, Is.Zero);
    }

    [Test]
    public void LogicalProcessorChart_SixtyFourProcessorsUseVisibleDenseSparklinesInShortChart()
    {
        var sample = new MonitorSample(DateTimeOffset.UtcNow, 50, [], 0)
        {
            LogicalProcessorPercents = Enumerable.Range(0, 64).Select(index => (double?)index).ToArray()
        };
        var graph = new CpuGraph { ShowGrid = true, ChartType = ResourceChartType.LogicalProcessors };
        graph.Measure(new Size(836, 220));
        graph.Arrange(new Rect(0, 0, 836, 220));
        graph.SetData([sample], 120, null);

        Assert.Multiple(() =>
        {
            Assert.That(graph.LogicalProcessorPlotBounds(0), Is.Not.Null);
            Assert.That(graph.LogicalProcessorPlotBounds(63), Is.Not.Null);
            Assert.That(graph.LogicalProcessorPlotBounds(0)!.Value.Height, Is.GreaterThanOrEqualTo(2));
            Assert.That(graph.LogicalProcessorPlotBounds(63)!.Value.Height, Is.GreaterThanOrEqualTo(2));
        });
    }
}
