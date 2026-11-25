using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MonCore.Monitoring;
using NUnit.Framework;
using WinMon.Controls;

namespace WinMon.Tests;

[TestFixture, Apartment(ApartmentState.STA)]
public sealed class ResourceTrendGraphTests
{
    [TestCase(1000, ResourceTrendMetric.Cpu)]
    [TestCase(3000, ResourceTrendMetric.Cpu)]
    [TestCase(3000, ResourceTrendMetric.Send)]
    public void WideGraph_ConnectsConsecutiveMinutes(int width, ResourceTrendMetric metric)
    {
        var pixels = Render(width, metric, false);
        var columns = ColoredColumns(pixels, width, 35, width - 10);
        Assert.That(columns, Is.GreaterThan((width - 45) * (metric == ResourceTrendMetric.Send ? 0.15 : 0.95)),
            "Unused pixels between consecutive minutes must not break a continuous trace.");
    }

    [TestCase(1000, ResourceTrendMetric.Cpu)]
    [TestCase(3000, ResourceTrendMetric.Cpu)]
    [TestCase(3000, ResourceTrendMetric.Send)]
    public void WideGraph_PreservesRealCollectionGaps(int width, ResourceTrendMetric metric)
    {
        var pixels = Render(width, metric, true);
        var gapStart = 34 + (int)(730 / 1440d * (width - 42));
        var gapEnd = 34 + (int)(770 / 1440d * (width - 42));
        Assert.That(ColoredColumns(pixels, width, gapStart, gapEnd), Is.Zero);
        Assert.That(ColoredColumns(pixels, width, 35, width - 10), Is.GreaterThan((width - 45) * 0.1),
            "Both sides must still contain a trace; an empty graph must not pass the gap check.");
    }

    private static byte[] Render(int width, ResourceTrendMetric metric, bool gap)
    {
        var start = DateTimeOffset.UnixEpoch;
        var buckets = Enumerable.Range(0, 1440).Where(minute => !gap || minute is < 720 or >= 780)
            .Select(minute => new ResourceTrendBucket(start.AddMinutes(minute),
                new Dictionary<ResourceTrendMetric, ResourceTrendValue>
                {
                    [metric] = new(3000, 60, 50, 50, "", ResourceTrendBreak.None)
                })).ToArray();
        var graph = new ResourceTrendGraph();
        graph.Resources["PlotSurface"] = Brushes.Black;
        graph.Resources["Accent"] = Brushes.Red;
        graph.Resources["NetworkAccent"] = Brushes.Red;
        graph.SetData(buckets, metric == ResourceTrendMetric.Send ? ResourceTrendMetric.Receive : metric,
            start, start.AddDays(1), null);
        graph.Measure(new Size(width, 200));
        graph.Arrange(new Rect(0, 0, width, 200));
        graph.UpdateLayout();
        var bitmap = new RenderTargetBitmap(width, 200, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(graph);
        var pixels = new byte[width * 200 * 4];
        bitmap.CopyPixels(pixels, width * 4, 0);
        return pixels;
    }

    private static int ColoredColumns(byte[] pixels, int width, int start, int end)
    {
        var count = 0;
        for (var x = start; x < end; x++)
            for (var y = 6; y <= 10; y++)
            {
                var offset = (y * width + x) * 4;
                if (pixels[offset + 2] <= 150 || pixels[offset] >= 80 || pixels[offset + 1] >= 80) continue;
                count++;
                break;
            }
        return count;
    }
}
