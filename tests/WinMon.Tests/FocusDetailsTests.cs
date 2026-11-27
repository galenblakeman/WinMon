using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
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
public sealed class FocusDetailsTests
{
    private const ulong GiB = 1UL << 30;

    [Test]
    public void MemoryStackSeparatesModifiedFromInUseThenAddsStandbyAndFree()
    {
        var stack = CpuGraph.MemoryStack(new ResourceSample
        {
            TotalMemoryBytes = 16 * GiB, UsedMemoryBytes = 8 * GiB, ModifiedBytes = 1 * GiB,
            StandbyBytes = 6 * GiB, FreeBytes = 2 * GiB
        });
        Assert.That(stack, Is.EqualTo((43.75, 50d, 87.5, 100d)));
    }

    [Test]
    public void MemoryStackNeedsEveryReadingAndClampsInconsistentOnes()
    {
        Assert.Multiple(() =>
        {
            Assert.That(CpuGraph.MemoryStack(new ResourceSample { TotalMemoryBytes = 16, UsedMemoryBytes = 8 }), Is.Null);
            Assert.That(CpuGraph.MemoryStack(new ResourceSample
            {
                TotalMemoryBytes = 0, UsedMemoryBytes = 0, ModifiedBytes = 0, StandbyBytes = 0, FreeBytes = 0
            }), Is.Null);
            // Counters read at slightly different moments can overshoot; the stack stays within the plot.
            Assert.That(CpuGraph.MemoryStack(new ResourceSample
            {
                TotalMemoryBytes = 10, UsedMemoryBytes = 4, ModifiedBytes = 6, StandbyBytes = 5, FreeBytes = 5
            }), Is.EqualTo((0d, 40d, 90d, 100d)));
        });
    }

    [Test]
    public void CommitAndGpuMemoryAreSharesOfTheirLimits()
    {
        Assert.Multiple(() =>
        {
            Assert.That(CpuGraph.CommitPercent(new ResourceSample { CommittedBytes = 12, CommitLimitBytes = 48 }),
                Is.EqualTo(25));
            Assert.That(CpuGraph.CommitPercent(new ResourceSample { CommittedBytes = 12 }), Is.Null);
            Assert.That(CpuGraph.GpuMemoryPercent(new ResourceSample { GpuMemoryUsedBytes = 3, GpuMemoryTotalBytes = 4 }),
                Is.EqualTo(75));
            Assert.That(CpuGraph.GpuMemoryPercent(new ResourceSample { GpuMemoryUsedBytes = 3, GpuMemoryTotalBytes = 0 }),
                Is.Null);
        });
    }

    [TestCase(0.5, "0.5 ms")]
    [TestCase(2, "2 ms")]
    [TestCase(50, "50 ms")]
    public void MillisecondsUseShortNumbers(double value, string expected) =>
        Assert.That(CpuGraph.Milliseconds(value), Is.EqualTo(expected));

    private static MonitorSample Sample(int second, double value) =>
        new(DateTimeOffset.UnixEpoch.AddSeconds(second), value, [], 0,
            MonotonicTimestamp: Stopwatch.Frequency * (100 + second))
        {
            Resources = new ResourceSample
            {
                CpuKernelPercent = value / 2, TotalMemoryBytes = 100, UsedMemoryBytes = 50, ModifiedBytes = 5,
                StandbyBytes = 30, FreeBytes = 20, CommittedBytes = 60, CommitLimitBytes = 120, DiskName = "C:",
                DiskActivePercent = value, DiskReadBytesPerSecond = value * 10_000,
                DiskWriteBytesPerSecond = value * 5_000, DiskResponseSeconds = value / 10_000, GpuPercent = value,
                GpuMemoryUsedBytes = 2, GpuMemoryTotalBytes = 8
            }
        };

    private static GeometryDrawing[] Render(int focus)
    {
        var graph = new CpuGraph
        {
            ShowGrid = true, ChartType = ResourceChartType.Focus, VisibleMetrics = 1 << focus, FocusMetric = focus
        };
        graph.Resources["PlotSurface"] = Brushes.White;
        graph.Measure(new Size(400, 300));
        graph.Arrange(new Rect(0, 0, 400, 300));
        graph.SetData([Sample(0, 20), Sample(1, 40), Sample(2, 60)], 10, null);
        graph.UpdateLayout();
        return Drawings(VisualTreeHelper.GetDrawing(graph)).OfType<GeometryDrawing>()
            .Where(drawing => drawing.Geometry is StreamGeometry).ToArray();
    }

    private static IEnumerable<Drawing> Drawings(Drawing drawing)
    {
        if (drawing is DrawingGroup group)
            foreach (var child in group.Children)
                foreach (var leaf in Drawings(child)) yield return leaf;
        else yield return drawing;
    }

    private static string Describe(GeometryDrawing drawing) => drawing.Brush is { } fill
        ? $"fill {fill.Opacity:0.##}"
        : drawing.Pen.DashStyle.Dashes.Count == 0 ? "line" : $"dash {string.Join(",", drawing.Pen.DashStyle.Dashes)}";

    [Test]
    public void CpuFocusAddsAKernelBandBetweenTheWashAndTheTotalTrace()
    {
        if (SystemParameters.HighContrast) Assert.Ignore("High contrast draws no washes.");
        Assert.That(Render(0).Select(Describe), Is.EqualTo(new[] { "fill 0.08", "fill 0.3", "line" }));
    }

    [Test]
    public void MemoryFocusStacksFourLayersThenCommitThenTheUsedTrace()
    {
        if (SystemParameters.HighContrast) Assert.Ignore("High contrast draws no washes.");
        var dash = string.Join(",", DashStyles.Dash.Dashes);
        Assert.That(Render(1).Select(Describe),
            Is.EqualTo(new[] { "fill 0.05", "fill 0.18", "fill 0.3", "fill 0.15", $"dash {dash}", "line" }));
    }

    [Test]
    public void GpuFocusAddsADottedDedicatedMemoryTrace()
    {
        if (SystemParameters.HighContrast) Assert.Ignore("High contrast draws no washes.");
        var dot = string.Join(",", new[] { 1d, 3d });
        Assert.That(Render(4).Select(Describe), Is.EqualTo(new[] { "fill 0.08", $"dash {dot}", "line" }));
    }

    [Test]
    public void DiskFocusAddsReadWriteAndResponseLanes()
    {
        if (SystemParameters.HighContrast) Assert.Ignore("High contrast draws no washes.");
        var dash = string.Join(",", DashStyles.Dash.Dashes);
        Assert.That(Render(2).Select(Describe), Is.EqualTo(new[] { "fill 0.08", "line", "line", $"dash {dash}", "line" }));
    }
}
