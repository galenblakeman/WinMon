using System.Diagnostics;
using System.Windows;
using NUnit.Framework;
using WinMon.Controls;
using WinMon.Monitoring;

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
            Assert.That(readout.Gauge.Value, Is.EqualTo(50));
            Assert.That(readout.Gauge.Visibility, Is.EqualTo(Visibility.Visible));
        });

        readout.ShowGauge(null, 8_000_000, 10_000_000);
        Assert.That(readout.Gauge.Visibility, Is.EqualTo(Visibility.Hidden));
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void NarrowReadout_KeepsRateUnitBesideTheWideRail()
    {
        var readout = new MetricReadout { Width = 130 };
        var window = new Window { Width = 130, Height = 110, Content = readout,
            WindowStyle = WindowStyle.None, ShowInTaskbar = false };
        try
        {
            readout.ShowValue("↓7.64", "Mbps");
            window.Show();
            window.UpdateLayout();
            var availableWidth = readout.ValueLabel.ActualWidth;
            readout.ValueLabel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Assert.Multiple(() =>
            {
                Assert.That(readout.ValueLabel.FontSize, Is.EqualTo(22));
                Assert.That(readout.ValueLabel.Inlines.LastInline.FontSize, Is.EqualTo(13));
                Assert.That(readout.DetailLabel.FontSize, Is.EqualTo(13));
                Assert.That(readout.ValueLabel.DesiredSize.Width, Is.LessThanOrEqualTo(availableWidth));
            });
        }
        finally
        {
            window.Close();
        }
    }
}
