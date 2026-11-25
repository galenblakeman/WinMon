using System.Windows;
using System.Windows.Media;
using NUnit.Framework;

namespace WinMon.Tests;

[TestFixture, NonParallelizable, Apartment(ApartmentState.STA)]
[SetCulture("en-US"), SetUICulture("en-US")]
public sealed class ResourceTrendRenderCharacterizationTests
{
    private static PreparedTrendRender Render(string name)
    {
        var prepared = new PreparedTrendRender(name);
        prepared.Render();
        return prepared;
    }

    private static GeometryDrawing Primary(PreparedTrendRender prepared) => prepared.Shapes.Single(shape => shape.Pen?.Thickness == 1.5);
    private static GeometryDrawing Secondary(PreparedTrendRender prepared) => prepared.Shapes.Single(shape => shape.Pen?.Thickness == 1.2);
    private static GeometryDrawing[] Washes(PreparedTrendRender prepared) => prepared.Shapes
        .Where(shape => shape.Brush is SolidColorBrush { Color.A: 56 }).ToArray();
    private static void PointIs(Point actual, double x, double y)
    {
        Assert.That(actual.X, Is.EqualTo(x).Within(1e-8));
        Assert.That(actual.Y, Is.EqualTo(y).Within(1e-8));
    }

    [TestCase("small-width", 0)]
    [TestCase("small-height", 0)]
    [TestCase("minimum", 6)]
    public void PlotBoundsKeepTheExistingMinimumDrawingThreshold(string name, int shapes) =>
        Assert.That(Render(name).Shapes, Has.Count.EqualTo(shapes));

    [TestCase("empty")]
    [TestCase("missing")]
    [TestCase("zero-seconds")]
    [TestCase("resource-change")]
    [TestCase("clock-change")]
    public void EmptyAndUnavailableMetricsKeepAxesWithoutInventingASeries(string name)
    {
        var prepared = Render(name);
        Assert.That(prepared.Shapes, Has.Count.EqualTo(6));
        Assert.That(prepared.Shapes[0].Geometry.Bounds, Is.EqualTo(new Rect(34, 8, 200, 100)));
        Assert.That(prepared.Glyphs.Count, Is.GreaterThanOrEqualTo(2));
    }

    [Test]
    public void PrimaryTraceKeepsPointOrderScaleAndOriginalSolidPen()
    {
        var prepared = Render("primary");
        var trace = Primary(prepared);
        var points = PreparedTrendRender.Points(trace.Geometry);
        Assert.That(points, Has.Length.EqualTo(5));
        for (var index = 0; index < points.Length; index++) PointIs(points[index], 34 + index * 40, 8 + 100 * (1 - (10 + index * 20) / 90d));
        Assert.That(((SolidColorBrush)trace.Pen.Brush).Color, Is.EqualTo(Colors.Red));
        Assert.That(trace.Pen.DashStyle.Dashes, Is.Empty);
    }

    [Test]
    public void CollapsedColumnsUseWeightedMeansAndTheRangeOfMinuteAverages()
    {
        var prepared = Render("weighted-column");
        var points = PreparedTrendRender.Points(Primary(prepared).Geometry);
        Assert.That(points, Has.Length.EqualTo(1));
        PointIs(points[0], 34, 8 + 100 * (1 - (10 * 10 + 90 * 50) / 60d / 90));
        if (!SystemParameters.HighContrast)
        {
            var wash = Washes(prepared).Single();
            Assert.That(wash.Geometry.Bounds.X, Is.EqualTo(34));
            Assert.That(wash.Geometry.Bounds.Y, Is.EqualTo(8));
            Assert.That(wash.Geometry.Bounds.Height, Is.EqualTo(100 * (1 - 10 / 90d)).Within(1e-8));
        }
    }

    [TestCase("partial-before", 1)]
    [TestCase("partial-at", 0)]
    [TestCase("partial-after", 0)]
    public void PartialCoverageKeepsTheExisting59Point999SecondBoundary(string name, int dottedRanges)
    {
        var prepared = Render(name);
        Assert.That(prepared.Shapes.Count(shape => shape.Pen?.Thickness == 1 &&
            shape.Pen.DashStyle.Dashes.SequenceEqual(DashStyles.Dot.Dashes)), Is.EqualTo(dottedRanges));
    }

    [TestCase("primary", 1)]
    [TestCase("break", 2)]
    [TestCase("gap", 2)]
    public void ExplicitBreaksAndRealTimeGapsKeepSeparateFigures(string name, int figures) =>
        Assert.That(PathGeometry.CreateFromGeometry(Primary(Render(name)).Geometry).Figures, Has.Count.EqualTo(figures));

    [Test]
    public void AGapWithinOneScreenColumnOmitsItsRangeAndTrace()
    {
        var prepared = Render("collapsed-gap");
        Assert.That(PreparedTrendRender.Points(Primary(prepared).Geometry), Is.Empty);
        Assert.That(Washes(prepared), Is.Empty);
    }

    [TestCase("receive", 2)]
    [TestCase("secondary-only", 0)]
    public void ReceiveUsesTheSendTraceAndOneSharedMaximum(string name, int primaryPoints)
    {
        var prepared = Render(name);
        var primary = PreparedTrendRender.Points(Primary(prepared).Geometry);
        var secondary = PreparedTrendRender.Points(Secondary(prepared).Geometry);
        Assert.That(primary, Has.Length.EqualTo(primaryPoints));
        Assert.That(secondary, Has.Length.EqualTo(2));
        PointIs(secondary[0], 34, 58);
        PointIs(secondary[1], 74, 8);
        if (primaryPoints > 0) { PointIs(primary[0], 34, 103); PointIs(primary[1], 74, 93); }
        Assert.That(((SolidColorBrush)Secondary(prepared).Pen.Brush).Color, Is.EqualTo(Colors.Lime));
        Assert.That(Secondary(prepared).Pen.DashStyle.Dashes, Is.EqualTo(DashStyles.Dot.Dashes));
    }

    [Test]
    public void SendDoesNotAddAReceiveOrSecondaryTrace()
    {
        var prepared = Render("send");
        Assert.That(prepared.Shapes.Any(shape => shape.Pen?.Thickness == 1.2), Is.False);
        var points = PreparedTrendRender.Points(Primary(prepared).Geometry);
        PointIs(points[0], 34, 8 + 100 * (1 - 10 / 30d));
        PointIs(points[1], 74, 8);
    }

    [TestCase("selection", 114)]
    [TestCase("clipped-time", 234)]
    [TestCase("stale-selection", -1)]
    public void SelectionUsesClampedTimeAndIgnoresAStaleBucket(string name, int x)
    {
        var prepared = Render(name);
        var markers = prepared.Shapes.Where(shape => shape.Pen?.Thickness == 1 &&
            shape.Pen.DashStyle.Dashes.SequenceEqual(DashStyles.Dash.Dashes)).ToArray();
        Assert.That(markers, Has.Length.EqualTo(x < 0 ? 0 : 1));
        if (x >= 0)
        {
            var points = PreparedTrendRender.Points(markers[0].Geometry);
            PointIs(points[0], x, 8); PointIs(points[1], x, 108);
        }
    }

    [Test]
    public void NonSolidAccentKeepsTheExistingRangeWashFallback()
    {
        var prepared = Render("gradient");
        Assert.That(Primary(prepared).Pen.Brush, Is.TypeOf<LinearGradientBrush>());
        if (!SystemParameters.HighContrast)
        {
            Assert.That(Washes(prepared), Has.Length.EqualTo(5));
            Assert.That(Washes(prepared).Select(shape => ((SolidColorBrush)shape.Brush).Color),
                Is.All.EqualTo(Color.FromArgb(56, 30, 144, 255)));
        }
        else Assert.That(Washes(prepared), Is.Empty);
    }

    [Test]
    public void WrongTypedResourcesKeepTheOriginalBrushFallbacks()
    {
        var prepared = Render("fallback");
        Assert.That(((SolidColorBrush)Primary(prepared).Pen.Brush).Color, Is.EqualTo(Colors.DodgerBlue));
        Assert.That(((SolidColorBrush)prepared.Shapes[0].Brush).Color, Is.EqualTo(Colors.Transparent));
        Assert.That(prepared.Shapes.Count(shape => shape.Pen?.Thickness == .6 &&
            shape.Pen.Brush is SolidColorBrush brush && brush.Color == Colors.DimGray), Is.EqualTo(5));
    }

    [TestCase("clipped-time")]
    [TestCase("normalized-range")]
    public void TimeColumnsClampBothEndsAndNormalizeANonpositiveRange(string name)
    {
        var points = PreparedTrendRender.Points(Primary(Render(name)).Geometry);
        Assert.That(points, Has.Length.EqualTo(2));
        Assert.That(points[0].X, Is.EqualTo(34));
        Assert.That(points[1].X, Is.EqualTo(234));
    }

    [Test]
    public void ZeroValuesUseTheExistingUnitFloorWithoutDividingByZero()
    {
        var points = PreparedTrendRender.Points(Primary(Render("zero-values")).Geometry);
        Assert.That(points, Has.Length.EqualTo(2));
        PointIs(points[0], 34, 108); PointIs(points[1], 74, 108);
    }

    [TestCaseSource(typeof(PreparedTrendRender), nameof(PreparedTrendRender.Cases))]
    public void RepeatedPreparedRendersKeepExactPixelsDrawingAndSelectionState(string name)
    {
        var prepared = new PreparedTrendRender(name);
        var first = prepared.Snapshot(prepared.Render());
        var second = prepared.Snapshot(prepared.Render());
        Assert.That(second, Is.EqualTo(first));
        Assert.That(prepared.SelectionEvents, Is.Zero);
        Assert.That(prepared.Glyphs.Count, Is.GreaterThanOrEqualTo(name is "small-width" or "small-height" ? 0 : 2));
    }
}
