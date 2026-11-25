using System.Diagnostics;
using System.Windows;
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
public sealed class CpuMarkerHitCharacterizationTests
{
    private static MonitorSample Sample(int second) =>
        new(DateTimeOffset.UnixEpoch.AddSeconds(second), 25, [], 0,
            MonotonicTimestamp: Stopwatch.Frequency * second);

    private static ChartMarker Marker(double moment, bool selected = false,
        ChartMarkerPattern pattern = ChartMarkerPattern.Triangle) =>
        new(moment, "CpuChartAccent", "capture", selected, new object(), pattern);

    private static CpuGraph Graph()
    {
        var graph = new CpuGraph { ShowGrid = true, ChartType = ResourceChartType.Overlay };
        graph.SetData([Sample(100), Sample(120)], 20, null);
        Size(graph, 441);
        return graph;
    }

    private static void Size(CpuGraph graph, double width)
    {
        graph.Measure(new Size(width, 200));
        graph.Arrange(new Rect(0, 0, width, 200));
    }

    private static Point Middle(CpuGraph graph) =>
        new(graph.PlotBounds.Left + graph.PlotBounds.Width / 2, graph.PlotBounds.Top);

    [TestCase(false, false, false)]
    [TestCase(true, true, false)]
    [TestCase(true, false, true)]
    public void HiddenGridProcessGraphsAndEmptyMarkersCannotProduceAHit(bool grid, bool process, bool empty)
    {
        var graph = Graph();
        graph.ShowGrid = grid;
        if (process)
            graph.SetData([Sample(100), Sample(120)], 20, null,
                new ProcessUsage(42, 1, "process", 0, 0, 0));
        graph.Markers = empty ? [] : [Marker(110)];

        Assert.That(graph.MarkerAt(Middle(graph)), Is.Null);
    }

    [Test]
    public void RestoringResourceGraphStateReenablesTheSameMarker()
    {
        var graph = Graph();
        var marker = Marker(110);
        graph.Markers = [marker];
        graph.ShowGrid = false;
        Assert.That(graph.MarkerAt(Middle(graph)), Is.Null);
        graph.ShowGrid = true;
        graph.SetData([Sample(100), Sample(120)], 20, null,
            new ProcessUsage(42, 1, "process", 0, 0, 0));
        Assert.That(graph.MarkerAt(Middle(graph)), Is.Null);

        graph.SetData([Sample(100), Sample(120)], 20, null);
        Assert.That(graph.MarkerAt(Middle(graph)), Is.SameAs(marker));
    }

    [TestCase(-8.01, false)]
    [TestCase(-8, true)]
    [TestCase(-7.99, true)]
    [TestCase(0, true)]
    [TestCase(7.99, true)]
    [TestCase(8, true)]
    [TestCase(8.01, false)]
    public void HorizontalToleranceIncludesEightPixelsOnEitherSide(double offset, bool hit)
    {
        var graph = Graph();
        var marker = Marker(110);
        graph.Markers = [marker];
        var point = Middle(graph);
        point.X += offset;

        Assert.That(graph.MarkerAt(point), hit ? Is.SameAs(marker) : Is.Null);
    }

    [TestCase(-100, true)]
    [TestCase(0, true)]
    [TestCase(5.99, true)]
    [TestCase(6, true)]
    [TestCase(6.01, false)]
    public void TopEdgeBandHasAnInclusiveUpperBoundaryAndNoLowerClamp(double offset, bool hit)
    {
        var graph = Graph();
        var marker = Marker(110);
        graph.Markers = [marker];
        var point = Middle(graph);
        point.Y += offset;

        Assert.That(graph.MarkerAt(point), hit ? Is.SameAs(marker) : Is.Null);
    }

    [TestCase(99.875, false)]
    [TestCase(100, true)]
    [TestCase(100.125, true)]
    [TestCase(119.875, true)]
    [TestCase(120, true)]
    [TestCase(120.125, true)]
    [TestCase(120.875, true)]
    [TestCase(121, true)]
    [TestCase(121.125, false)]
    public void TimeWindowIncludesItsStartAndOneSecondBeyondItsEnd(double moment, bool hit)
    {
        var graph = Graph();
        var marker = Marker(moment);
        graph.Markers = [marker];
        // This fixture has a 400-pixel plot and a 20-second window.
        var point = new Point(39 + (moment - 100) * 20, graph.PlotBounds.Top);

        Assert.That(graph.MarkerAt(point), hit ? Is.SameAs(marker) : Is.Null);
    }

    [TestCase(-3, true)]
    [TestCase(3, false)]
    public void NearestMarkerWinsWhenSeveralMarkersAreWithinTolerance(double offset, bool lowerWins)
    {
        var graph = Graph();
        var lower = Marker(109.75, pattern: ChartMarkerPattern.Diamond);
        var upper = Marker(110.25, true, ChartMarkerPattern.Square);
        graph.Markers = [lower, upper];
        var point = Middle(graph);
        point.X += offset;

        Assert.That(graph.MarkerAt(point), Is.SameAs(lowerWins ? lower : upper));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void IdenticalPositionsPreserveListOrderRegardlessOfSelectionOrShape(bool reverse)
    {
        var graph = Graph();
        var lower = Marker(110, pattern: ChartMarkerPattern.Diamond);
        var upper = Marker(110, true, ChartMarkerPattern.Square);
        graph.Markers = reverse ? [upper, lower] : [lower, upper];

        Assert.That(graph.MarkerAt(Middle(graph)), Is.SameAs(reverse ? upper : lower));
    }

    [Test]
    public void InvisibleAndDistantMarkersDoNotHideTheNearestEligibleMarker()
    {
        var graph = Graph();
        var marker = Marker(110);
        graph.Markers = [Marker(99), Marker(121.125), Marker(109), marker, Marker(111)];

        Assert.That(graph.MarkerAt(Middle(graph)), Is.SameAs(marker));
        Assert.That(graph.MarkerAt(new Point(39 + 15 * 20, graph.PlotBounds.Top)), Is.Null);
    }

    [TestCase(ResourceChartType.Overlay)]
    [TestCase(ResourceChartType.SmallMultiples)]
    [TestCase(ResourceChartType.Heatmap)]
    [TestCase(ResourceChartType.Focus)]
    [TestCase(ResourceChartType.LogicalProcessors)]
    public void HitPositionsFollowTheCurrentChartLayoutAndResize(ResourceChartType type)
    {
        var graph = Graph();
        graph.ChartType = type;
        var marker = Marker(110);
        graph.Markers = [marker];
        var before = Middle(graph);
        Assert.That(graph.MarkerAt(before), Is.SameAs(marker));

        Size(graph, 841);
        Assert.Multiple(() =>
        {
            Assert.That(graph.MarkerAt(before), Is.Null);
            Assert.That(graph.MarkerAt(Middle(graph)), Is.SameAs(marker));
        });
    }

    [Test]
    public void WindowSeriesAndMarkerReplacementImmediatelyChangeHitPositions()
    {
        var graph = Graph();
        var marker = Marker(110);
        graph.Markers = [marker];
        var middle = Middle(graph);
        Assert.That(graph.MarkerAt(middle), Is.SameAs(marker));

        graph.SetData([Sample(100), Sample(120)], 40, null);
        var threeQuarters = new Point(339, graph.PlotBounds.Top);
        Assert.That(graph.MarkerAt(middle), Is.Null);
        Assert.That(graph.MarkerAt(threeQuarters), Is.SameAs(marker));

        graph.SetData([Sample(110), Sample(130)], 20, null);
        var left = new Point(graph.PlotBounds.Left, graph.PlotBounds.Top);
        Assert.That(graph.MarkerAt(threeQuarters), Is.Null);
        Assert.That(graph.MarkerAt(left), Is.SameAs(marker));

        var replacement = Marker(120);
        graph.Markers = [replacement];
        Assert.That(graph.MarkerAt(left), Is.Null);
        Assert.That(graph.MarkerAt(middle), Is.SameAs(replacement));
    }

    [TestCase(-20.125, false)]
    [TestCase(-20, true)]
    [TestCase(0, true)]
    [TestCase(1, true)]
    [TestCase(1.125, false)]
    public void EmptyHistoryUsesZeroAsTheWindowEndRatherThanDisablingMarkerQueries(double moment, bool hit)
    {
        var graph = Graph();
        graph.SetData([], 20, null);
        var marker = Marker(moment);
        graph.Markers = [marker];
        var point = new Point(39 + (moment + 20) * 20, graph.PlotBounds.Top);

        Assert.That(graph.MarkerAt(point), hit ? Is.SameAs(marker) : Is.Null);
    }

    [Test]
    public void QueriesPreserveMarkerIdentitySelectionTooltipAndNotifications()
    {
        var graph = Graph();
        var first = Sample(100);
        graph.SetData([first, Sample(120)], 20, first);
        var marker = Marker(110);
        ChartMarker[] markers = [marker];
        graph.Markers = markers;
        var tooltip = new object();
        graph.ToolTip = tooltip;
        var anchor = graph.SelectionAnchor;
        var notifications = 0;
        graph.MarkerSelected += (_, _) => notifications++;
        graph.SampleSelected += (_, _) => notifications++;
        graph.LiveRequested += (_, _) => notifications++;

        Assert.That(graph.MarkerAt(Middle(graph)), Is.SameAs(marker));
        Assert.That(graph.MarkerAt(Middle(graph)), Is.SameAs(marker));
        Assert.That(graph.MarkerAt(new Point(-200, 100)), Is.Null);
        Assert.Multiple(() =>
        {
            Assert.That(graph.Markers, Is.SameAs(markers));
            Assert.That(graph.Markers.Single().Tag, Is.SameAs(marker.Tag));
            Assert.That(graph.Markers.Single().Selected, Is.False);
            Assert.That(graph.SelectionInPlot, Is.True);
            Assert.That(graph.SelectionAnchor, Is.EqualTo(anchor));
            Assert.That(graph.ToolTip, Is.SameAs(tooltip));
            Assert.That(notifications, Is.Zero);
        });
    }
}
