using System.Text.Json;
using System.Windows;
using System.Windows.Media.Imaging;
using NUnit.Framework;
using WinMon.Diagnostics;
using WinMon.Monitoring;

namespace WinMon.Tests;

public sealed partial class MainWindowWorkflowCharacterizationTests
{
    [Test, Category("Integration"), SetCulture("en-US")]
    public void CompactDiagnosticPreservesOwnedPreferenceRoundTripReadoutsAndFixtureReport()
    {
        using var scope = new AutoCaptureDiagnosticScope(_directory,
            window => window.Title == "Owned metric capture diagnostic fixture");
        using var position = new PositionDiagnostic(_directory);
        var originalWindows = Application.Current.Windows.Cast<Window>().ToArray();
        var fullSize = new Size(position.Window.Width, position.Window.Height);
        try
        {
            scope.Run((output, token) => new WindowDiagnostics(position.Window).CaptureCompactDiagnosticsAsync(output, token));
            using var report = JsonDocument.Parse(File.ReadAllText(Path.Combine(_directory, "compact.json")));
            foreach (var name in new[] { "ButtonRoundTrip", "PreferenceRoundTrip", "FullSizeRestored", "ChartTogglePreserved",
                         "OptionalDisk", "MissingReadings", "GapInFixture", "AlwaysNativePeakLabelsAndTooltips", "MemoryPressureDetails" })
                Assert.That(report.RootElement.GetProperty(name).GetBoolean(), Is.True, name);
            Assert.Multiple(() =>
            {
                Assert.That(position.Window.CompactMode, Is.False);
                Assert.That(new Size(position.Window.Width, position.Window.Height), Is.EqualTo(fullSize));
                Assert.That(position.Window.SamplingTask, Is.Null);
                Assert.That(scope.DispatcherError, Is.Null);
                Assert.That(scope.TimedOut, Is.False);
            });
            var fixture = Application.Current.Windows.OfType<MainWindow>().Single(window => !originalWindows.Contains(window));
            Assert.Multiple(() =>
            {
                Assert.That(fixture.IsVisible, Is.False);
                Assert.That(fixture.CompactMode, Is.False);
                Assert.That(fixture.Session.History.Samples, Has.Count.EqualTo(120));
                Assert.That(fixture.Preferences.SecondaryDrive, Is.Null);
                Assert.That(fixture.GpuMetric.Toggle.IsChecked, Is.False);
                Assert.That(fixture.SamplingTask, Is.Null);
            });
            var files = Directory.GetFiles(_directory, "*.png");
            Assert.That(files.Length, Is.GreaterThan(10));
            foreach (var path in files)
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                var image = BitmapFrame.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                Assert.That(image.PixelWidth, Is.GreaterThan(0));
                Assert.That(image.PixelHeight, Is.GreaterThan(0));
            }
        }
        finally
        {
            CloseCompactOwnedFixtures(originalWindows);
        }
    }

    [TestCase(false), TestCase(true), Category("Integration")]
    public void CompactDiagnosticRejectsMissingMemoryMetadataBeforeMovingOrWriting(bool hasSample)
    {
        using var ui = new UpdateUi(_directory, true);
        Directory.CreateDirectory(_directory);
        ui.Window.Left = 30;
        ui.Window.Top = 40;
        if (hasSample) ui.Window.Session.Record(new MonitorSample(DateTimeOffset.UnixEpoch, 7, [], 0));
        var bounds = new Rect(ui.Window.Left, ui.Window.Top, ui.Window.Width, ui.Window.Height);
        var error = Assert.Throws<InvalidOperationException>(() =>
            FinishWindowCheck(new WindowDiagnostics(ui.Window).CaptureCompactDiagnosticsAsync(_directory, default)));
        Assert.Multiple(() =>
        {
            Assert.That(error!.Message, Is.EqualTo("Compact diagnostics require native resource samples."));
            Assert.That(new Rect(ui.Window.Left, ui.Window.Top, ui.Window.Width, ui.Window.Height), Is.EqualTo(bounds));
            Assert.That(ui.Window.CompactMode, Is.False);
            Assert.That(ui.Window.SamplingTask, Is.Null);
            Assert.That(Directory.GetFiles(_directory), Is.Empty);
        });
    }

    [Test, Category("Integration"), SetCulture("en-US")]
    public void CompactDiagnosticPreCancellationPreservesItsMoveAndQueuedRecoveryBeforeCompactMode()
    {
        using var scope = new AutoCaptureDiagnosticScope(_directory,
            window => window.Title == "Owned metric capture diagnostic fixture");
        using var position = new PositionDiagnostic(_directory);
        var original = position.Bounds();
        scope.Cancel();
        var error = Assert.Catch<OperationCanceledException>(() => scope.Run((output, token) =>
            new WindowDiagnostics(position.Window).CaptureCompactDiagnosticsAsync(output, token)));
        position.Flush();
        Assert.Multiple(() =>
        {
            Assert.That(error!.CancellationToken, Is.EqualTo(scope.Token));
            Assert.That(position.Bounds().Left, Is.Not.EqualTo(original.Left));
            Assert.That(position.Timer.IsEnabled, Is.True);
            Assert.That(position.Window.CompactMode, Is.False);
            Assert.That(position.Window.SamplingTask, Is.Null);
            Assert.That(File.Exists(Path.Combine(_directory, "position-recovery.png")), Is.False);
            Assert.That(File.Exists(Path.Combine(_directory, "compact.json")), Is.False);
        });
    }

    [TestCase("gauges-none-dark-1000-100.png", true)]
    [TestCase("compact.json", false)]
    [Category("Integration"), SetCulture("en-US")]
    public void CompactDiagnosticWriteFailurePreservesSentinelAndItsOriginalFixtureState(string name, bool compact)
    {
        using var scope = new AutoCaptureDiagnosticScope(_directory,
            window => window.Title == "Owned metric capture diagnostic fixture");
        using var position = new PositionDiagnostic(_directory);
        var originalWindows = Application.Current.Windows.Cast<Window>().ToArray();
        var path = Path.Combine(_directory, name);
        File.WriteAllText(path, "owned compact sentinel");
        using var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        try
        {
            Assert.Throws<IOException>(() => scope.Run((output, token) =>
                new WindowDiagnostics(position.Window).CaptureCompactDiagnosticsAsync(output, token)));
            var fixture = Application.Current.Windows.OfType<MainWindow>().Single(window => !originalWindows.Contains(window));
            Assert.Multiple(() =>
            {
                Assert.That(File.ReadAllText(path), Is.EqualTo("owned compact sentinel"));
                Assert.That(position.Window.CompactMode, Is.False);
                Assert.That(position.Window.SamplingTask, Is.Null);
                Assert.That(fixture.CompactMode, Is.EqualTo(compact));
                Assert.That(fixture.IsVisible, Is.False);
                Assert.That(fixture.Session.History.Samples, Has.Count.EqualTo(120));
                Assert.That(fixture.SamplingTask, Is.Null);
                Assert.That(scope.DispatcherError, Is.Null);
                Assert.That(scope.TimedOut, Is.False);
            });
        }
        finally { CloseCompactOwnedFixtures(originalWindows); }
    }

    private static void CloseCompactOwnedFixtures(IEnumerable<Window> originalWindows)
    {
        foreach (var window in Application.Current.Windows.OfType<MainWindow>()
                     .Where(window => !originalWindows.Contains(window)).ToArray())
        {
            window.SnapshotCopiedTimer.Stop();
            foreach (var view in new FrameworkElement[] { window.UsageView, window.Processes, window.ServiceView, window.ConnectionView })
                view.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
            window.CloseForFixture();
            window.Lifetime.Dispose();
        }
    }
}
