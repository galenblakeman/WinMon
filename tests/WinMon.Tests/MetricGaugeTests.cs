using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NUnit.Framework;
using WinMon.Controls;
using WinMon.Monitoring;
using WinMon.Preferences;

namespace WinMon.Tests;

[TestFixture]
public sealed class MetricGaugeTests
{
    [Test]
    public void Peaks_UseLastTwoMinutesAndMatchingDrives()
    {
        var now = DateTimeOffset.UnixEpoch.AddMinutes(5);
        MonitorSample Sample(int secondsAgo, double cpu, string disk, bool gap = false) =>
            new(now.AddSeconds(-secondsAgo), cpu, [], 0, gap)
            {
                Resources = new ResourceSample
                {
                    TotalMemoryBytes = 100, UsedMemoryBytes = (ulong)cpu,
                    DiskName = disk, DiskActivePercent = cpu,
                    SecondaryDiskName = "D:", SecondaryDiskActivePercent = cpu + 1,
                    ReceiveBytesPerSecond = cpu * 1000,
                    SendBytesPerSecond = 100_000,
                    GpuPercent = cpu
                }
            };
        var samples = new[]
        {
            Sample(121, 95, "C:"), Sample(90, 80, "E:"), Sample(60, 99, "C:", true),
            Sample(20, 40, "C:"), Sample(0, 20, "C:")
        };

        var peaks = MainWindow.TwoMinuteGaugePeaks(samples, samples[^1], "C:", "D:");

        Assert.Multiple(() =>
        {
            Assert.That(peaks.Cpu, Is.EqualTo(80));
            Assert.That(peaks.Memory, Is.EqualTo(80));
            Assert.That(peaks.Disk, Is.EqualTo(40));
            Assert.That(peaks.SecondaryDisk, Is.EqualTo(81));
            Assert.That(peaks.Network, Is.EqualTo(640_000));
            Assert.That(peaks.Gpu, Is.EqualTo(80));
        });
    }

    [Test]
    public void Peaks_UseMonotonicTimeAcrossWallClockChange()
    {
        var earlier = new MonitorSample(DateTimeOffset.UnixEpoch, 70, [], 0,
            MonotonicTimestamp: Stopwatch.Frequency * 100);
        var latest = new MonitorSample(DateTimeOffset.UnixEpoch.AddHours(1), 20, [], 0,
            MonotonicTimestamp: Stopwatch.Frequency * 101);

        var peaks = MainWindow.TwoMinuteGaugePeaks([earlier, latest], latest, "C:", null);

        Assert.That(peaks.Cpu, Is.EqualTo(70));
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void Gauge_NormalizesPhysicalScaleAndHidesUnavailableReading()
    {
        var readout = new MetricReadout();
        readout.ShowGauge(5_000_000, 8_000_000, 10_000_000);
        Assert.Multiple(() =>
        {
            Assert.That(readout.Gauge.Level, Is.EqualTo(0.5));
            Assert.That(readout.Gauge.Peak, Is.EqualTo(0.8));
            Assert.That(readout.Gauge.Visibility, Is.EqualTo(Visibility.Visible));
        });

        readout.ShowGauge(null, 8_000_000, 10_000_000);
        Assert.Multiple(() =>
        {
            Assert.That(readout.Gauge.Level, Is.Null);
            Assert.That(readout.Gauge.Visibility, Is.EqualTo(Visibility.Hidden));
        });
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void NoneStyle_HidesTheGaugeAndGivesTheLabelsTheTile()
    {
        var readout = new MetricReadout { GaugeStyle = GaugeStyle.None };
        readout.ShowGauge(50, 80, 100, "10 Mbps");
        Assert.Multiple(() =>
        {
            Assert.That(readout.Gauge.Visibility, Is.EqualTo(Visibility.Hidden));
            Assert.That(readout.Gauge.Parent, Is.Null);
            Assert.That(readout.ScaleLabel.Visibility, Is.EqualTo(Visibility.Collapsed));
        });
    }

    [TestCase(640e3, "640K")]
    [TestCase(100e6, "100M")]
    [TestCase(2.5e9, "2.5G")]
    public void ScaleLabel_IsShortEnoughToSitUnderASmallArc(double bitsPerSecond, string expected)
    {
        Assert.That(RateText.Short(bitsPerSecond), Is.EqualTo(expected));
    }

    [TestCase(GaugeStyle.Needle, true)]
    [TestCase(GaugeStyle.Bowl, true)]
    [TestCase(GaugeStyle.SegmentedArc, true)]
    [TestCase(GaugeStyle.LeftBar, false)]
    [TestCase(GaugeStyle.BottomBar, false)]
    [TestCase(GaugeStyle.SegmentedBar, false)]
    [Apartment(ApartmentState.STA)]
    public void ScaleLabel_AccompaniesArcsOnly(GaugeStyle style, bool arc)
    {
        var readout = new MetricReadout { GaugeStyle = style };
        readout.ShowGauge(5_000_000, 8_000_000, 10_000_000, "10M");
        var withScale = readout.ScaleLabel.Visibility;
        readout.ShowGauge(50, 80, 100);
        Assert.Multiple(() =>
        {
            Assert.That(readout.ScaleLabel.Text, Is.Empty);
            Assert.That(withScale, Is.EqualTo(arc ? Visibility.Visible : Visibility.Collapsed));
            // Arcs keep the label's space on percentage readouts, so every arc shares one size.
            Assert.That(readout.ScaleLabel.Visibility, Is.EqualTo(arc ? Visibility.Hidden : Visibility.Collapsed));
        });
    }

    [TestCase(180, 0)]
    [TestCase(180, 5)]
    [TestCase(140, 8)]
    [Apartment(ApartmentState.STA)]
    public void ArcCornerLabel_EndsTwelveDipsFromTileEdge(int width, int outerRightMargin)
    {
        var host = new Grid { Width = width };
        var readout = new MetricReadout
        {
            GaugeStyle = GaugeStyle.Bowl,
            Margin = new Thickness(outerRightMargin == 8 ? 10 : 0, 0, outerRightMargin, 0)
        };
        host.Children.Add(readout);
        var window = new Window { Width = width, Content = host, SizeToContent = SizeToContent.Height,
            WindowStyle = WindowStyle.None, ShowInTaskbar = false };
        try
        {
            readout.ShowGauge(50, 80, 100, "Peak 80%", "80%");
            window.Show();
            window.UpdateLayout();
            var right = readout.ScaleLabel.TranslatePoint(new Point(readout.ScaleLabel.ActualWidth, 0), host).X;
            Assert.That(host.ActualWidth - right, Is.EqualTo(12).Within(1));
        }
        finally
        {
            window.Close();
        }
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void Needle_IsTheLargestHalfCircleThatFits([Values(120, 140)] int width)
    {
        var readout = new MetricReadout { Width = width, GaugeStyle = GaugeStyle.Needle };
        var window = new Window { Width = width, Content = readout, SizeToContent = SizeToContent.Height,
            WindowStyle = WindowStyle.None, ShowInTaskbar = false };
        try
        {
            readout.ShowGauge(50, 80, 100);
            window.Show();
            window.UpdateLayout();
            var (_, radiusX, radiusY) = readout.Gauge.ArcBounds();
            Assert.Multiple(() =>
            {
                Assert.That(radiusX, Is.EqualTo(radiusY));
                // The arc beside the text was 48 DIPs wide; a dial is at least 76 across.
                Assert.That(radiusX, Is.GreaterThanOrEqualTo(38));
                // The needle moves the detail line to the tile's tooltip.
                Assert.That(readout.DetailLabel.Parent, Is.Null);
            });
        }
        finally
        {
            window.Close();
        }
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void RadialGauge_ClearsTheResourceName([Values(GaugeStyle.Needle, GaugeStyle.Bowl,
        GaugeStyle.SegmentedArc)] GaugeStyle style)
    {
        var readout = new MetricReadout { Width = 140, GaugeStyle = style, Label = "CPU" };
        var window = new Window { Width = 140, Content = readout, SizeToContent = SizeToContent.Height,
            WindowStyle = WindowStyle.None, ShowInTaskbar = false };
        try
        {
            readout.ShowGauge(50, 80, 100);
            window.Show();
            window.UpdateLayout();
            var nameBottom = readout.Toggle.TranslatePoint(new Point(0, readout.Toggle.ActualHeight), readout).Y;
            var gaugeTop = readout.Gauge.TranslatePoint(new Point(), readout).Y;

            Assert.That(gaugeTop - nameBottom, Is.GreaterThanOrEqualTo(5.5));
        }
        finally
        {
            window.Close();
        }
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void ArcsHoldingTheValue_AreNearlyRoundAndHoldTheWidestRate(
        [Values(GaugeStyle.Bowl, GaugeStyle.SegmentedArc)] GaugeStyle style, [Values(120, 150, 210)] int width)
    {
        var readout = new MetricReadout { Width = width, GaugeStyle = style };
        var window = new Window { Width = width, Content = readout, SizeToContent = SizeToContent.Height,
            WindowStyle = WindowStyle.None, ShowInTaskbar = false };
        try
        {
            readout.ShowValue("↓9.52", "Mbps");
            readout.ShowGauge(50, 80, 100);
            window.Show();
            window.UpdateLayout();
            var (center, radiusX, radiusY) = readout.Gauge.ArcBounds();
            // The digits are the tallest ink; find how far their top rises above the arc's ends.
            var label = readout.ValueLabel;
            var digits = new FormattedText("↓9.52", CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface(label.FontFamily, label.FontStyle, label.FontWeight, label.FontStretch), label.FontSize,
                Brushes.Black, 1);
            var inkTop = digits.Height + digits.OverhangAfter - digits.Extent;
            var rise = center.Y - label.TranslatePoint(new Point(0, inkTop), readout.Gauge).Y;
            // Half the stroke is inside the radius.
            var (insideX, insideY) = (radiusX - 4, radiusY - 4);
            var room = insideX * Math.Sqrt(1 - Math.Pow(rise / insideY, 2));
            Assert.Multiple(() =>
            {
                Assert.That(radiusX / radiusY, Is.InRange(1, MetricGauge.BowlWidening + 1e-9));
                Assert.That(label.DesiredSize.Width / 2, Is.LessThanOrEqualTo(room));
                Assert.That(readout.DetailLabel.Parent, Is.Not.Null, "The detail line stays under the arc.");
            });
        }
        finally
        {
            window.Close();
        }
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void Gauge_SkipsChangesSmallerThanHalfAPixel()
    {
        var readout = new MetricReadout { Width = 200, GaugeStyle = GaugeStyle.BottomBar };
        var window = new Window { Width = 200, Height = 120, Content = readout,
            WindowStyle = WindowStyle.None, ShowInTaskbar = false };
        try
        {
            window.Show();
            readout.ShowGauge(50, 60, 100);
            window.UpdateLayout();
            readout.ShowGauge(50, 60, 100);
            readout.ShowGauge(50.1, 60, 100);
            var steady = readout.Gauge.Level;
            readout.ShowGauge(51, 60, 100);
            Assert.Multiple(() =>
            {
                Assert.That(steady, Is.EqualTo(0.5));
                Assert.That(readout.Gauge.Level, Is.EqualTo(0.51));
            });
        }
        finally
        {
            window.Close();
        }
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void SegmentedGauge_RedrawsASmallChangeThatLightsAnotherSegment(
        [Values(GaugeStyle.SegmentedBar, GaugeStyle.SegmentedArc)] GaugeStyle style)
    {
        var readout = new MetricReadout { Width = 200, GaugeStyle = style };
        var window = new Window { Width = 200, Height = 120, Content = readout,
            WindowStyle = WindowStyle.None, ShowInTaskbar = false };
        try
        {
            window.Show();
            readout.ShowGauge(44.9, 60, 100);
            window.UpdateLayout();
            readout.ShowGauge(44.91, 60, 100);
            var sameSegments = readout.Gauge.Level;
            readout.ShowGauge(45.02, 60, 100);
            Assert.Multiple(() =>
            {
                Assert.That(sameSegments, Is.EqualTo(0.449).Within(1e-12));
                Assert.That(readout.Gauge.Level, Is.EqualTo(0.4502).Within(1e-12), "Four segments become five.");
            });
        }
        finally
        {
            window.Close();
        }
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void EveryStyle_KeepsOneHeightAndTheRateUnitAtTheNarrowWidth([Values] GaugeStyle style)
    {
        var readout = new MetricReadout { Width = 120, GaugeStyle = style, Label = "Network" };
        var window = new Window { Width = 120, Content = readout, SizeToContent = SizeToContent.Height,
            WindowStyle = WindowStyle.None, ShowInTaskbar = false };
        try
        {
            // The widest rate a readout shows, in a tile as narrow as the minimum window gives six resources.
            readout.ShowValue("↓9.52", "Mbps");
            readout.DetailLabel.Text = "↑16.83 Kbps";
            readout.ShowGauge(7_640_000, 9_000_000, 10_000_000, "100M");
            window.Show();
            window.UpdateLayout();
            var availableWidth = readout.ValueLabel.ActualWidth;
            readout.ValueLabel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Assert.Multiple(() =>
            {
                Assert.That(readout.ActualHeight, Is.EqualTo(MetricReadout.LayoutHeight));
                Assert.That(readout.ValueLabel.FontSize, Is.EqualTo(22));
                Assert.That(readout.ValueLabel.Inlines.LastInline.FontSize, Is.EqualTo(13));
                Assert.That(readout.DetailLabel.FontSize, Is.EqualTo(13));
                Assert.That(readout.ValueLabel.DesiredSize.Width, Is.LessThanOrEqualTo(availableWidth));
                Assert.That(readout.Toggle.DesiredSize.Width, Is.LessThanOrEqualTo(readout.Toggle.ActualWidth + 0.5),
                    "The resource name keeps its room beside a scale label.");
                // The fixed height must not cut off anything the style places.
                FrameworkElement[] parts = [readout.Toggle, readout.ValueLabel, readout.DetailLabel, readout.Gauge];
                foreach (var part in parts.Where(part => part.Parent is not null))
                    Assert.That(part.TranslatePoint(new Point(0, part.ActualHeight), readout).Y,
                        Is.LessThanOrEqualTo(MetricReadout.LayoutHeight + 0.5), part.GetType().Name);
            });
        }
        finally
        {
            window.Close();
        }
    }
}
