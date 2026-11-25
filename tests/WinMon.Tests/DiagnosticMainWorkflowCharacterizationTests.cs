using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Xml.Linq;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MonCore.Monitoring;
using NUnit.Framework;
using WinMon.Diagnostics;

namespace WinMon.Tests;

public sealed partial class MainWindowWorkflowCharacterizationTests
{
    [Test, Category("Integration"), SetCulture("en-US")]
    public void MainDiagnosticPreservesAdvancingSyntheticRecordingAndItsCompleteSmokeReport()
    {
        using var scope = new AutoCaptureDiagnosticScope(_directory,
            window => window.Title == "Owned metric capture diagnostic fixture");
        using var fixture = new ActionDrawerDiagnostic(_directory);
        InstallDesignActionStyles();
        InstallMainDiagnosticTooltipStyle();
        var window = fixture.Window;
        typeof(MainWindow).GetField("_preferencesLoaded", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(window, true);
        window.Width = 1400;
        window.UpdateLayout();
        var originalWindows = Application.Current.Windows.Cast<Window>().ToArray();
        using var recording = new MainDiagnosticRecording(window);
        try
        {
            scope.Run((output, token) => new WindowDiagnostics(window).CaptureDiagnosticAsync(output, token));
            using var report = JsonDocument.Parse(File.ReadAllText(Path.Combine(_directory, "smoke.json")));
            foreach (var name in new[] { "ProcessGpuRanking", "SeriesToggle", "ProcessCountChoicesLiveAndHistorical",
                         "ProcessListScrollAtMinimumSize", "IntegratedTitleBarHitTesting", "ToolTipDarkAndLight",
                         "HistoricalResourceValues", "ChartRangeAndWideLayout", "HistoricalSelection", "SelectionExpiry",
                         "PauseResumeWithGap", "ReturnToLive", "HiddenRecordingAndRestore", "MinimizedRecordingAndRestore" })
                Assert.That(report.RootElement.GetProperty(name).GetBoolean(), Is.True, name);
            Assert.Multiple(() =>
            {
                Assert.That(report.RootElement.GetProperty("NativeSamples").GetInt32(), Is.GreaterThan(3));
                Assert.That(report.RootElement.GetProperty("LastTotalCpu").GetDouble(), Is.EqualTo(7));
                Assert.That(report.RootElement.GetProperty("QuietObservationSeconds").GetDouble(), Is.GreaterThan(14.5));
                Assert.That(window.SelectedSample, Is.Null);
                Assert.That(window.Preferences.Theme, Is.EqualTo("Light"));
                Assert.That(window.WindowState, Is.EqualTo(WindowState.Normal));
                Assert.That(window.IsVisible, Is.True);
                Assert.That(window.SamplingTask, Is.Null, "The Windows sampler must never start.");
                Assert.That(window.Session.HasLiveSample, Is.False, "The window receives explicit synthetic records.");
                Assert.That(recording.Session.HasLiveSample, Is.True, "The owned fake source uses the real sampling queue.");
                Assert.That(recording.BridgeError, Is.Null);
                Assert.That(scope.DispatcherError, Is.Null);
                Assert.That(scope.TimedOut, Is.False);
            });
            foreach (var name in new[] { "features.json", "disks.json", "snapshot.json", "light.png", "range-wide-200.png" })
                Assert.That(File.Exists(Path.Combine(_directory, name)), Is.True, name);
            var images = Directory.GetFiles(_directory, "*.png");
            Assert.That(images.Length, Is.GreaterThan(30));
            foreach (var path in images)
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                var image = BitmapFrame.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                Assert.That(image.PixelWidth, Is.GreaterThan(0));
                Assert.That(image.PixelHeight, Is.GreaterThan(0));
            }
        }
        finally
        {
            recording.Dispose();
            CloseCompactOwnedFixtures(originalWindows);
        }
        Assert.That(recording.SourceDisposed, Is.True);
    }

    [TestCase("null"), TestCase("empty"), TestCase("whitespace"), TestCase("file"), Category("Integration")]
    public void MainDiagnosticInvalidOutputFailsBeforeChangingItsWindow(string kind)
    {
        using var ui = new UpdateUi(_directory, true);
        Directory.CreateDirectory(_directory);
        var path = kind == "null" ? null : kind == "empty" ? "" : kind == "whitespace" ? " \t" : Path.Combine(_directory, "owned-output");
        if (kind == "file") File.WriteAllText(path!, "owned smoke output sentinel");
        var preferences = ui.Window.Preferences;
        var operation = () => FinishWindowCheck(new WindowDiagnostics(ui.Window).CaptureDiagnosticAsync(path!, default));
        if (kind == "null") Assert.Throws<ArgumentNullException>(operation);
        else if (kind is "empty" or "whitespace") Assert.Throws<ArgumentException>(operation);
        else
        {
            Assert.Throws<IOException>(operation);
            Assert.That(File.ReadAllText(path!), Is.EqualTo("owned smoke output sentinel"));
        }
        Assert.That(ui.Window.Preferences, Is.SameAs(preferences));
        Assert.That(ui.Window.SelectedSample, Is.Null);
        Assert.That(ui.Window.SamplingTask, Is.Null);
        Assert.That(Directory.GetFiles(_directory, "*.png"), Is.Empty);
    }

    [Test, Category("Integration")]
    public void MainDiagnosticPreCancellationCreatesItsOutputAndRetainsTheCallerTokenAndSelection()
    {
        using var fixture = new MetricCaptureDiagnostic(_directory);
        using var stop = new CancellationTokenSource();
        stop.Cancel();
        var window = fixture.Window;
        var selected = window.Session.History.Latest;
        window.SelectedSample = selected;
        window.RefreshVisiblePresentation();
        var path = Path.Combine(_directory, "owned-cancelled-output");
        var error = Assert.Catch<OperationCanceledException>(() => FinishWindowCheck(
            new WindowDiagnostics(window).CaptureDiagnosticAsync(path, stop.Token)));
        Assert.That(error, Is.TypeOf<TaskCanceledException>());
        Assert.That(error!.CancellationToken, Is.EqualTo(stop.Token));
        Assert.That(Directory.Exists(path), Is.True);
        Assert.That(Directory.GetFiles(path), Is.Empty);
        Assert.That(window.SelectedSample, Is.SameAs(selected));
        Assert.That(window.SamplingTask, Is.Null);
        Assert.That(window.Width, Is.EqualTo(1000));
    }

    [TestCase(0, false), TestCase(2, false), TestCase(3, true), Category("Integration")]
    public void MainDiagnosticRejectsEmptyShortOrUnavailableCpuRecordingAfterItsOriginalWait(int count, bool missingCpu)
    {
        using var ui = new UpdateUi(_directory, true);
        for (var i = 0; i < count; i++)
            ui.Window.Session.Record(new MonitorSample(DateTimeOffset.UnixEpoch.AddSeconds(i),
                missingCpu && i == count - 1 ? null : 7d, [], 0));
        var error = Assert.Throws<InvalidOperationException>(() => FinishWindowCheck(
            new WindowDiagnostics(ui.Window).CaptureDiagnosticAsync(_directory, default)));
        Assert.That(error!.Message, Is.EqualTo("Native CPU samples did not arrive in the live window."));
        Assert.That(ui.Window.Session.History.Samples, Has.Count.EqualTo(count));
        Assert.That(ui.Window.SelectedSample, Is.Null);
        Assert.That(ui.Window.SamplingTask, Is.Null);
        Assert.That(Directory.Exists(_directory), Is.True);
        Assert.That(Directory.GetFiles(_directory), Is.Empty);
    }

    [Test, Category("Integration")]
    public void MainDiagnosticAcceptsExactlyThreeSamplesButRejectsNonAdvancingRecordingAndRetainsTheSnapshot()
    {
        using var scope = new AutoCaptureDiagnosticScope(_directory,
            candidate => candidate.Title == "Owned metric capture diagnostic fixture");
        using var fixture = new MetricCaptureDiagnostic(_directory);
        var window = fixture.Window;
        typeof(MainWindow).GetField("_preferencesLoaded", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(window, true);
        for (var i = 1; i < 3; i++)
            window.Session.Record(new MonitorSample(DateTimeOffset.UnixEpoch.AddSeconds(i), 7, [], 0));
        window.RefreshVisiblePresentation();
        var latest = window.Session.History.Latest;
        var error = Assert.Throws<InvalidOperationException>(() => scope.Run(
            (output, token) => new WindowDiagnostics(window).CaptureDiagnosticAsync(output, token)));
        Assert.That(error!.Message, Is.EqualTo("Recording must advance while the snapshot stays fixed."));
        Assert.That(window.Session.History.Samples, Has.Count.EqualTo(3));
        Assert.That(window.SelectedSample, Is.SameAs(latest));
        Assert.That(window.SnapshotModeButton.IsChecked, Is.True);
        Assert.That(window.SamplingTask, Is.Null);
        Assert.That(Directory.GetFiles(_directory), Is.Empty);
    }

    private static void InstallMainDiagnosticTooltipStyle()
    {
        var document = XDocument.Load(Path.Combine(TestContext.CurrentContext.TestDirectory,
            "Fixtures", "DiagnosticPins", "App.xaml.xml"));
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        var tooltip = document.Root!.Element(presentation + "Application.Resources")!.Elements(presentation + "Style")
            .Single(style => (string?)style.Attribute("TargetType") == "ToolTip");
        var dictionary = new XElement(presentation + "ResourceDictionary",
            new XAttribute(XNamespace.Xmlns + "x", xaml.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "system", "clr-namespace:System;assembly=System.Runtime"),
            new XElement(tooltip));
        var resources = (ResourceDictionary)XamlReader.Parse(dictionary.ToString());
        Application.Current.Resources[typeof(ToolTip)] = resources[typeof(ToolTip)];
    }
    // Independent fake collector; the window's native factory is deliberately never run.
    private sealed class MainDiagnosticRecording : IDisposable
    {
        private readonly CancellationTokenSource _stop = new();
        private readonly Task _run;
        private readonly MainDiagnosticFrameSource _source = new();
        private bool _disposed;
        internal MonitorSession Session { get; }
        internal Exception? BridgeError { get; private set; }
        internal bool SourceDisposed => _source.Disposed;

        internal MainDiagnosticRecording(MainWindow window)
        {
            Session = new MonitorSession(() => _source, window.CurrentSamplingOptions,
                drain => window.Dispatcher.BeginInvoke(drain, DispatcherPriority.Background));
            Session.LiveSampleRecorded += sample =>
            {
                try
                {
                    window.Session.Record(sample, sample);
                    window.RefreshPresentation();
                }
                catch (Exception error) { BridgeError = error; }
            };
            _run = Session.Run(_stop.Token);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Session.Close();
            _stop.Cancel();
            FinishWindowCheck(_run);
            _stop.Dispose();
        }
    }

    private sealed class MainDiagnosticFrameSource : IFrameSource
    {
        private readonly HashSet<string> _readyDrives = DriveInfo.GetDrives()
            .Where(drive => drive is { IsReady: true, DriveType: DriveType.Fixed })
            .Select(drive => drive.Name.TrimEnd('\\')).ToHashSet(StringComparer.OrdinalIgnoreCase);
        internal bool Disposed { get; private set; }

        public CapturedFrame Capture(SamplingOptions options)
        {
            var process = new ProcessUsage(int.MaxValue - 500, 100, "owned drawer process", 3, 1_048_576)
            { HasAppWindow = true, ThreadCount = 7, HandleCount = 11, GpuPercent = 10 };
            return new CapturedFrame(new MonitorSample(DateTimeOffset.UtcNow, 7, [process], 0,
                false, Stopwatch.GetTimestamp())
            {
                LogicalProcessorPercents = [0, 7, 14, 100],
                Resources = new ResourceSample
                {
                    TotalMemoryBytes = 32UL << 30, UsedMemoryBytes = 16UL << 30,
                    CachedBytes = 5UL << 30, StandbyBytes = 3UL << 30, FreeBytes = 1UL << 30,
                    ModifiedBytes = 768UL << 20, CompressionStoreBytes = 1UL << 30, PagingReadsPerSecond = 18.5,
                    ProcessCount = 7, ThreadCount = 70, HandleCount = 700,
                    DiskName = options.PrimaryDrive ?? "C:", DiskActivePercent = 6.4, DiskBytesPerSecond = 900_000,
                    SecondaryDiskName = options.SecondaryDrive ?? "",
                    SecondaryDiskActivePercent = options.SecondaryDrive is { } name && _readyDrives.Contains(name) ? 3.5 : null,
                    GpuPercent = 10, GpuMemoryUsedBytes = 4UL << 30, GpuMemoryTotalBytes = 8UL << 30,
                    NetworkId = "owned", NetworkName = "Owned synthetic network", LinkBitsPerSecond = 1_000_000_000,
                    ReceiveBytesPerSecond = 955_000, SendBytesPerSecond = 150_000
                }
            });
        }
        public void Reset() { }
        public void Dispose() => Disposed = true;
    }
}
