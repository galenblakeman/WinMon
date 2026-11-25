using System.Windows;
using System.Windows.Controls;
using NUnit.Framework;
using WinMon.Controls;
using WinMon.Diagnostics;
using WinMon.Monitoring;
using WinMon.Preferences;

namespace WinMon.Tests;

public sealed partial class MainWindowWorkflowCharacterizationTests
{
    [Test, SetCulture("en-US")]
    public void ArcReadoutsStayCenteredInFullAndCompactColumns(
        [Values(1000d, 836d)] double width, [Values] bool compact,
        [Values] bool secondaryDrive, [Values("Dark", "Light")] string theme,
        [Values(GaugeStyle.Bowl, GaugeStyle.Needle, GaugeStyle.SegmentedArc)] GaugeStyle style)
    {
        using var fixture = new PinsDiagnosticScope(_directory);
        var window = new MainWindow(true)
        {
            Preferences = new MonitorPreferences
            {
                Theme = theme, GaugeStyle = style, PrimaryDrive = "C:",
                SecondaryDrive = secondaryDrive ? "D:" : null
            }
        };
        window.Session.History.Add(new MonitorSample(DateTimeOffset.UnixEpoch, 50, [], 0)
        {
            Resources = new ResourceSample
            {
                TotalMemoryBytes = 1000, UsedMemoryBytes = 500,
                DiskName = "C:", DiskActivePercent = 50,
                SecondaryDiskName = "D:", SecondaryDiskActivePercent = 50,
                GpuPercent = 50
            }
        });
        window.ApplyTheme();
        window.SetCompactView(compact);
        window.RefreshVisiblePresentation();
        var readouts = window.CompactTiles
            .Where(tile => secondaryDrive || tile.Source != window.SecondaryDiskMetric)
            .Select(tile => compact ? tile.Readout : tile.Source).ToArray();
        foreach (var readout in readouts)
        {
            readout.ShowValue("50", "%");
            readout.ShowGauge(50, 80, 100);
        }
        var visual = (FrameworkElement)window.Content;
        visual.Measure(new Size(width, compact ? MainWindow.CompactHeight : 840));
        visual.Arrange(new Rect(0, 0, width, compact ? MainWindow.CompactHeight : 840));
        visual.UpdateLayout();
        Assert.That(readouts, Has.Length.EqualTo(secondaryDrive ? 6 : 5));
        foreach (var readout in readouts)
        {
            var tile = compact ? (FrameworkElement)readout.Parent : readout;
            var strip = (Grid)tile.Parent;
            var column = Grid.GetColumn(tile);
            var center = strip.ColumnDefinitions.Take(column).Sum(part => part.ActualWidth) +
                         strip.ColumnDefinitions[column].ActualWidth / 2;
            var gaugeCenter = readout.Gauge.TranslatePoint(readout.Gauge.ArcBounds().Center, strip).X;
            var valueCenter = readout.ValueLabel.TranslatePoint(
                new Point(readout.ValueLabel.ActualWidth / 2, 0), strip).X;
            Assert.Multiple(() =>
            {
                Assert.That(readout.ActualWidth, Is.GreaterThan(0), readout.Label);
                Assert.That(gaugeCenter, Is.EqualTo(center).Within(0.5 + 1e-9), readout.Label + " gauge");
                Assert.That(valueCenter, Is.EqualTo(center).Within(0.5 + 1e-9), readout.Label + " value");
            });
        }
        Assert.That(window.SamplingTask, Is.Null);
        Assert.That(window.IsVisible, Is.False);
    }

    // Layout checks must pass before capture observes cancellation; no image should be written.
    [Test, SetCulture("en-US")]
    public void GaugeWorkflowPassesCenteringBeforeCancellationAndCapture()
    {
        using var fixture = new PinsDiagnosticScope(_directory);
        using var stop = new CancellationTokenSource();
        stop.Cancel();
        Assert.Catch<OperationCanceledException>(() =>
            FinishWindowCheck(WindowDiagnostics.CaptureGaugeDiagnosticsAsync(_directory, stop.Token)));
        var window = fixture.CreatedWindow();
        Assert.Multiple(() =>
        {
            Assert.That(window.IsVisible, Is.False);
            Assert.That(window.SamplingTask, Is.Null);
            Assert.That(window.Session.HasLiveSample, Is.False);
            Assert.That(window.Preferences.Theme, Is.EqualTo("Dark"));
            Assert.That(window.Preferences.GaugeStyle, Is.EqualTo(GaugeStyle.Bowl));
            Assert.That(window.Preferences.PrimaryDrive, Is.EqualTo("C:"));
            Assert.That(window.Preferences.SecondaryDrive, Is.EqualTo("D:"));
            Assert.That(Directory.GetFileSystemEntries(_directory), Is.Empty);
        });
        var samples = window.Session.History.Samples;
        Assert.That(samples, Has.Count.EqualTo(120));
        Assert.That(samples.All(sample => sample.Processes.Count == 0), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(samples[94].TotalCpuPercent, Is.EqualTo(77.8).Within(0.000001));
            Assert.That(samples[94].Resources.GpuPercent, Is.EqualTo(69));
            Assert.That(samples[94].Resources.GpuMemoryUsedBytes, Is.Null);
            Assert.That(samples[94].Resources.DiskBytesPerSecond, Is.EqualTo(200_000));
            Assert.That(samples[^1].Resources.GpuMemoryUsedBytes, Is.EqualTo(4UL * 1_073_741_824));
            Assert.That(samples[^1].Resources.DiskBytesPerSecond, Is.EqualTo(900_000));
            Assert.That(samples[^1].Resources.TotalMemoryBytes, Is.EqualTo(64UL * 1_073_741_824));
            Assert.That(samples[^1].Timestamp - samples[0].Timestamp, Is.EqualTo(TimeSpan.FromSeconds(119)));
        });
        var readout = window.CpuMetric;
        var strip = (Grid)readout.Parent;
        var column = Grid.GetColumn(readout);
        var center = strip.ColumnDefinitions.Take(column).Sum(part => part.ActualWidth) +
            strip.ColumnDefinitions[column].ActualWidth / 2;
        var gaugeCenter = readout.Gauge.TranslatePoint(readout.Gauge.ArcBounds().Center, strip).X;
        var valueCenter = readout.ValueLabel.TranslatePoint(new Point(readout.ValueLabel.ActualWidth / 2, 0), strip).X;
        Assert.Multiple(() =>
        {
            Assert.That(gaugeCenter, Is.EqualTo(center).Within(0.5));
            Assert.That(valueCenter, Is.EqualTo(center).Within(0.5));
        });
    }
}
