using System.Globalization;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MonCore.Monitoring;
using NUnit.Framework;
using WinMon.Diagnostics;
using WinMon.Monitoring;
using WinMon.Resources;

namespace WinMon.Tests;

public sealed partial class MainWindowWorkflowCharacterizationTests
{
    [Test, Category("Integration"), SetCulture("en-US")]
    public void AutoCaptureDiagnosticPreservesReportsHistorySettingsAndAwaitedShutdown()
    {
        using var scope = new AutoCaptureDiagnosticScope(_directory);
        try
        {
            scope.Run();
            using var report = JsonDocument.Parse(File.ReadAllText(Path.Combine(_directory, "captures.json")));
            Assert.Multiple(() =>
            {
                Assert.That(report.RootElement.GetProperty("Markers").GetArrayLength(), Is.EqualTo(2));
                Assert.That(report.RootElement.GetProperty("Reports").GetArrayLength(), Is.EqualTo(2));
                Assert.That(report.RootElement.GetProperty("ShutdownWaitedForReport").GetBoolean(), Is.True);
                Assert.That(scope.Window!.SamplingTask, Is.Null);
                Assert.That(scope.Window.Session.History.Samples, Has.Count.EqualTo(121));
                Assert.That(scope.Window.IsVisible, Is.False);
                Assert.That(scope.Window.IsClosing, Is.True);
                Assert.That(Application.Current.Windows.Cast<Window>(), Does.Not.Contain(scope.Window));
                Assert.That(scope.DispatcherError, Is.Null);
                Assert.That(scope.TimedOut, Is.False);
            });
            var samples = scope.Window!.Session.History.Samples;
            Assert.Multiple(() =>
            {
                Assert.That(samples[^1].TotalCpuPercent, Is.EqualTo(100));
                Assert.That(samples[^1].Resources.DiskActivePercent, Is.EqualTo(100));
                Assert.That(samples[^1].Processes.Single(process => process.ProcessId == 9000).Name, Is.EqualTo("refresh-worker"));
                Assert.That(samples[^1].Timestamp - samples[^2].Timestamp, Is.EqualTo(TimeSpan.FromSeconds(1)));
                Assert.That(samples[^1].MonotonicTimestamp - samples[^2].MonotonicTimestamp,
                    Is.EqualTo(System.Diagnostics.Stopwatch.Frequency));
            });
            var markers = scope.Window!.CaptureMarkers;
            Assert.That(markers.Select(marker => marker.Fired.Rule.Metric),
                Is.EqualTo(new[] { CaptureMetric.Cpu, CaptureMetric.PrimaryDisk }));
            var root = Path.GetFullPath(_directory) + Path.DirectorySeparatorChar;
            foreach (var marker in markers)
            {
                Assert.That(marker.ReportPath, Is.Not.Null);
                Assert.That(Path.GetFullPath(marker.ReportPath!), Does.StartWith(root).IgnoreCase);
                Assert.That(marker.WriteFailed, Is.False);
                Assert.That(File.ReadAllText(marker.ReportPath!), Does.StartWith(CaptureReports.FirstLine(marker.ReportId)));
                var incidentPath = CaptureReports.IncidentPath(marker.ReportPath!, marker.ReportId.ToString("N"));
                var incident = IncidentRecording.Load(incidentPath);
                var tailSeconds = marker.Fired.Rule.Metric == CaptureMetric.Cpu ? 15 : 5;
                Assert.Multiple(() =>
                {
                    Assert.That(incident.Metric, Is.EqualTo(marker.Fired.Rule.Metric));
                    Assert.That(incident.Observations, Has.Length.EqualTo(31 + tailSeconds));
                    Assert.That(incident.Observations[0].Sample.Timestamp, Is.EqualTo(marker.FiredKey.Timestamp.AddSeconds(-30)));
                    Assert.That(incident.Observations[^1].Sample.Timestamp, Is.EqualTo(marker.FiredKey.Timestamp.AddSeconds(tailSeconds)));
                    Assert.That(incident.Observations[incident.SelectedIndex].Sample.Timestamp, Is.EqualTo(marker.FiredKey.Timestamp));
                    Assert.That(incident.WindowsEvents.Events, Is.Empty);
                });
            }
            Assert.That(File.ReadAllText(markers[1].ReportPath!), Does.Contain(Strings.AutoCaptureIoDisclaimer));
            var images = Directory.GetFiles(_directory, "*.png");
            Assert.That(images.Length, Is.GreaterThan(5));
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
            scope.DrainWrites();
            PreserveAutoCaptureArtifacts();
        }
    }

    [Test, Category("Integration"), SetCulture("en-US")]
    public void AutoCaptureDiagnosticPreCancellationStillWritesTriggeredReportsAndClosesBeforeShowing()
    {
        using var scope = new AutoCaptureDiagnosticScope(_directory);
        scope.Cancel();
        try
        {
            var exception = Assert.Catch<OperationCanceledException>(() => scope.Run());
            scope.DrainWrites();
            Assert.Multiple(() =>
            {
                Assert.That(exception!.CancellationToken, Is.EqualTo(scope.Token));
                Assert.That(scope.Window, Is.Not.Null);
                Assert.That(scope.Window!.IsVisible, Is.False);
                Assert.That(Application.Current.Windows.Cast<Window>(), Does.Not.Contain(scope.Window));
                Assert.That(scope.Window.SamplingTask, Is.Null);
                Assert.That(scope.Window.Session.History.Samples, Has.Count.EqualTo(120));
                Assert.That(Directory.GetFiles(_directory, "*.png"), Is.Empty);
                Assert.That(File.Exists(Path.Combine(_directory, "captures.json")), Is.False);
                Assert.That(scope.DispatcherError, Is.Null);
                Assert.That(scope.TimedOut, Is.False);
            });
            AssertAutoCaptureReports(scope.Window!);
        }
        finally { scope.DrainWrites(); PreserveAutoCaptureArtifacts("pre-canceled"); }
    }

    [Test, Category("Integration"), SetCulture("en-US")]
    public void AutoCaptureDiagnosticLockedToolbarPreservesSentinelAndTriggeredReports()
    {
        using var scope = new AutoCaptureDiagnosticScope(_directory);
        var path = Path.Combine(_directory, "captures-toolbar-narrow.png");
        File.WriteAllText(path, "owned toolbar sentinel");
        using var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        try
        {
            AssertLockedPngWriteFailure(scope.Run);
            scope.DrainWrites();
            Assert.Multiple(() =>
            {
                Assert.That(File.ReadAllText(path), Is.EqualTo("owned toolbar sentinel"));
                Assert.That(scope.Window!.IsVisible, Is.False);
                Assert.That(Application.Current.Windows.Cast<Window>(), Does.Not.Contain(scope.Window));
                Assert.That(scope.Window.SamplingTask, Is.Null);
                Assert.That(scope.Window.Session.History.Samples, Has.Count.EqualTo(120));
                Assert.That(File.Exists(Path.Combine(_directory, "captures.json")), Is.False);
                Assert.That(Directory.GetFiles(_directory, "*.png"), Has.Length.EqualTo(1));
                Assert.That(scope.DispatcherError, Is.Null);
                Assert.That(scope.TimedOut, Is.False);
            });
            AssertAutoCaptureReports(scope.Window!);
        }
        finally { scope.DrainWrites(); PreserveAutoCaptureArtifacts("locked-toolbar"); }
    }

    [TestCase("flight-recorder-dark-1000.png", TestName = "AutoCaptureDiagnosticLockedRecorderImageClosesRecorderAndSource")]
    [TestCase("flight-recorder-empty.png", TestName = "AutoCaptureDiagnosticLockedEmptyRecorderImageClosesRecorderAndSource")]
    [TestCase("flight-recorder-process-changes.png", TestName = "AutoCaptureDiagnosticLockedLifecycleImageClosesRecorderAndSource")]
    [Category("Integration"), SetCulture("en-US")]
    public void AutoCaptureDiagnosticLockedHistoryImagePreservesSentinelAndClosesOwnedWindows(string image)
    {
        using var scope = new AutoCaptureDiagnosticScope(_directory);
        var path = Path.Combine(_directory, image);
        var contentPath = Path.Combine(_directory, Path.GetFileNameWithoutExtension(image) + "-content.png");
        File.WriteAllText(path, "owned history image sentinel");
        File.WriteAllText(contentPath, "owned history content sentinel");
        using var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var lockedContent = new FileStream(contentPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        try
        {
            AssertLockedPngWriteFailure(scope.Run);
            scope.DrainWrites();
            Assert.Multiple(() =>
            {
                Assert.That(File.ReadAllText(path), Is.EqualTo("owned history image sentinel"));
                Assert.That(File.ReadAllText(contentPath), Is.EqualTo("owned history content sentinel"));
                Assert.That(scope.Window, Is.Not.Null);
                Assert.That(scope.Window!.IsVisible, Is.False);
                Assert.That(Application.Current.Windows.Cast<Window>(), Does.Not.Contain(scope.Window));
                Assert.That(scope.Window.OwnedWindows, Is.Empty);
                Assert.That(scope.Window.FlightRecorderEventReader, Is.Null);
                Assert.That(scope.Window.SamplingTask, Is.Null);
                Assert.That(scope.Window.Session.History.Samples, Has.Count.EqualTo(121));
                Assert.That(File.Exists(Path.Combine(_directory, "captures.json")), Is.False);
                Assert.That(scope.DispatcherError, Is.Null);
                Assert.That(scope.TimedOut, Is.False);
            });
            AssertAutoCaptureReports(scope.Window!);
        }
        finally { scope.DrainWrites(); }
    }

    [TestCase("settings-network-monitoring.png", TestName = "AutoCaptureDiagnosticLockedSettingsImageClosesOwnedDialogAndSource")]
#if !WINMON_STORE
    [TestCase("settings-network-busy.png", TestName = "AutoCaptureDiagnosticLockedBusyNetworkImageCompletesCancellationAndCloses")]
#endif
    [Category("Integration"), SetCulture("en-US")]
    public void AutoCaptureDiagnosticLockedSettingsImagePreservesSentinelsAndCompletesDialogCleanup(string image)
    {
        using var scope = new AutoCaptureDiagnosticScope(_directory);
        var observeBusy = image == "settings-network-busy.png";
        var dispatcher = Dispatcher.CurrentDispatcher;
        var observed = new HashSet<Preferences.SettingsWindow>();
        var busy = new HashSet<Preferences.SettingsWindow>();
        var closedStates = new List<(bool Navigation, bool Save, string Status)>();
        void ObserveSettings(object? sender, DispatcherHookEventArgs e)
        {
            if (!dispatcher.CheckAccess()) return;
            foreach (var settings in Application.Current.Windows.OfType<Preferences.SettingsWindow>())
            {
                if (!ReferenceEquals(settings.Owner, scope.Window)) continue;
                if (observed.Add(settings))
                    settings.Closing += (_, _) =>
                    {
                        if (busy.Contains(settings))
                            closedStates.Add((settings.Navigation.IsEnabled, settings.SaveButton.IsEnabled,
                                settings.ProcessNetworkMonitoringStatus.Text));
                    };
                if (!settings.Navigation.IsEnabled && !settings.SaveButton.IsEnabled) busy.Add(settings);
            }
        }
        var path = Path.Combine(_directory, image);
        var contentPath = Path.Combine(_directory, Path.GetFileNameWithoutExtension(image) + "-content.png");
        File.WriteAllText(path, "owned settings image sentinel");
        File.WriteAllText(contentPath, "owned settings content sentinel");
        using var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var lockedContent = new FileStream(contentPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        try
        {
            if (observeBusy) dispatcher.Hooks.OperationPosted += ObserveSettings;
            AssertLockedPngWriteFailure(scope.Run);
            scope.DrainWrites();
            Assert.Multiple(() =>
            {
                Assert.That(File.ReadAllText(path), Is.EqualTo("owned settings image sentinel"));
                Assert.That(File.ReadAllText(contentPath), Is.EqualTo("owned settings content sentinel"));
                Assert.That(scope.Window, Is.Not.Null);
                Assert.That(scope.Window!.IsVisible, Is.False);
                Assert.That(Application.Current.Windows.Cast<Window>(), Does.Not.Contain(scope.Window));
                Assert.That(scope.Window.OwnedWindows, Is.Empty);
                Assert.That(scope.Window.FlightRecorderEventReader, Is.Null);
                Assert.That(scope.Window.SamplingTask, Is.Null);
                Assert.That(scope.Window.Session.History.Samples, Has.Count.EqualTo(121));
                Assert.That(File.Exists(Path.Combine(_directory, "captures.json")), Is.False);
                Assert.That(scope.DispatcherError, Is.Null);
                Assert.That(scope.TimedOut, Is.False);
            });
            if (observeBusy)
            {
                Assert.That(busy, Has.Count.EqualTo(1));
                Assert.That(closedStates, Is.EqualTo(new[] { (true, true, Strings.NetworkProcessMonitoringCancelled) }));
            }
            AssertAutoCaptureReports(scope.Window!);
        }
        finally
        {
            if (observeBusy) dispatcher.Hooks.OperationPosted -= ObserveSettings;
            scope.DrainWrites();
        }
    }

    [Test, Category("Integration"), SetCulture("en-US")]
    public void AutoCaptureDiagnosticLockedFinalReportPreservesSentinelAfterShutdown()
    {
        using var scope = new AutoCaptureDiagnosticScope(_directory);
        var path = Path.Combine(_directory, "captures.json");
        File.WriteAllText(path, "owned final report sentinel");
        using var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        try
        {
            Assert.Throws<IOException>(() => scope.Run());
            scope.DrainWrites();
            Assert.Multiple(() =>
            {
                Assert.That(File.ReadAllText(path), Is.EqualTo("owned final report sentinel"));
                Assert.That(scope.Window, Is.Not.Null);
                Assert.That(scope.Window!.IsVisible, Is.False);
                Assert.That(scope.Window.IsClosing, Is.True);
                Assert.That(Application.Current.Windows.Cast<Window>(), Does.Not.Contain(scope.Window));
                Assert.That(scope.Window.OwnedWindows, Is.Empty);
                Assert.That(scope.Window.FlightRecorderEventReader, Is.Null);
                Assert.That(scope.Window.SamplingTask, Is.Null);
                Assert.That(scope.Window.Session.History.Samples, Has.Count.EqualTo(121));
                Assert.That(scope.DispatcherError, Is.Null);
                Assert.That(scope.TimedOut, Is.False);
            });
            AssertAutoCaptureReports(scope.Window!);
        }
        finally { scope.DrainWrites(); }
    }

    private static void AssertAutoCaptureReports(MainWindow window)
    {
        Assert.That(window.CaptureMarkers.Select(marker => marker.Fired.Rule.Metric),
            Is.EqualTo(new[] { CaptureMetric.Cpu, CaptureMetric.PrimaryDisk }));
        foreach (var marker in window.CaptureMarkers)
        {
            Assert.That(marker.WriteFailed, Is.False);
            Assert.That(marker.ReportPath, Is.Not.Null);
            Assert.That(File.ReadAllText(marker.ReportPath!), Does.StartWith(CaptureReports.FirstLine(marker.ReportId)));
        }
    }

    private void PreserveAutoCaptureArtifacts(string? scenario = null)
    {
        if (TestContext.Parameters.Get("AutoCaptureArtifactDirectory") is not { Length: > 0 } artifacts) return;
        if (scenario is not null) artifacts = Path.Combine(artifacts, scenario);
        foreach (var path in Directory.GetFiles(_directory, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(artifacts, Path.GetRelativePath(_directory, path));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(path, target, true);
        }
    }

    private sealed class AutoCaptureDiagnosticScope : IDisposable
    {
        private readonly PinsDiagnosticScope _bootstrap;
        private readonly Window[] _existing;
        private readonly CultureInfo? _culture = Strings.Culture;
        private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
        private readonly CancellationTokenSource _stop = new();
        private readonly DispatcherTimer _watchdog;
        private readonly string _directory;
        private readonly Func<MainWindow, bool>? _windowFilter;
        internal MainWindow? Window { get; private set; }
        internal Exception? DispatcherError { get; private set; }
        internal bool TimedOut { get; private set; }
        internal CancellationToken Token => _stop.Token;
        internal void Cancel() => _stop.Cancel();

        internal AutoCaptureDiagnosticScope(string directory, Func<MainWindow, bool>? windowFilter = null)
        {
            _directory = directory;
            _windowFilter = windowFilter;
            _bootstrap = new PinsDiagnosticScope(directory);
            var bootstrap = Application.Current.Windows.OfType<MainWindow>().Single();
            bootstrap.ApplyTheme(false, false);
            foreach (var key in new[] { "TitleBarSurface", "TitleText", "Surface", "Text" })
                Application.Current.Resources[key] = bootstrap.FindResource(key);
            Strings.SetCulture(CultureInfo.GetCultureInfo("en"));
            _existing = Application.Current.Windows.Cast<Window>().ToArray();
            _dispatcher.Hooks.OperationPosted += ObserveWindow;
            AppDomain.CurrentDomain.FirstChanceException += ObserveCancellationWindow;
            _dispatcher.UnhandledException += OnUnhandled;
            _watchdog = new DispatcherTimer(TimeSpan.FromSeconds(90), DispatcherPriority.Send, (_, _) =>
            {
                TimedOut = true;
                _stop.Cancel();
                CloseOwnedDialogs();
            }, _dispatcher);
        }

        private void ObserveWindow(object? sender, DispatcherHookEventArgs e)
        {
            if (_dispatcher.CheckAccess())
                Window ??= Application.Current.Windows.OfType<MainWindow>().FirstOrDefault(IsSourceWindow);
        }
        private bool IsSourceWindow(MainWindow window) => !_existing.Contains(window) && (_windowFilter?.Invoke(window) ?? true);
        private void ObserveCancellationWindow(object? sender, FirstChanceExceptionEventArgs e)
        {
            // A pre-canceled await can throw and close the unshown source window without posting UI work.
            // Observe the same owned window before its finally runs; never change or handle the exception.
            if (Window is null && e.Exception is OperationCanceledException && _dispatcher.CheckAccess())
                Window = Application.Current.Windows.OfType<MainWindow>().FirstOrDefault(IsSourceWindow);
        }
        private void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            DispatcherError ??= e.Exception;
            e.Handled = true;
            _stop.Cancel();
            CloseOwnedDialogs();
        }
        private void CloseOwnedDialogs()
        {
            foreach (Window dialog in Application.Current.Windows.Cast<Window>().Where(window =>
                         !_existing.Contains(window) && window is not MainWindow).ToArray()) dialog.Close();
        }
        internal void Run() => Run(WindowDiagnostics.CaptureAutoCaptureDiagnosticsAsync);
        internal void Run(Func<string, CancellationToken, Task> operation)
        {
            var task = operation(_directory, _stop.Token);
            Wait(task, TimeSpan.FromSeconds(100));
            Assert.That(DispatcherError, Is.Null);
            Assert.That(TimedOut, Is.False);
            Assert.That(Window, Is.Not.Null);
        }
        internal void DrainWrites()
        {
            if (Window is null) return;
            Wait(Window.FinishCaptureWritesForFixtureAsync(), TimeSpan.FromSeconds(10));
        }
        private void Wait(Task task, TimeSpan timeout)
        {
            var bounded = task.WaitAsync(timeout);
            if (!bounded.IsCompleted)
            {
                var frame = new DispatcherFrame();
                _ = bounded.ContinueWith(_ => _dispatcher.BeginInvoke(() => frame.Continue = false),
                    CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
                Dispatcher.PushFrame(frame);
            }
            bounded.GetAwaiter().GetResult();
        }
        public void Dispose()
        {
            _watchdog.Stop();
            _stop.Cancel();
            _dispatcher.Hooks.OperationPosted -= ObserveWindow;
            AppDomain.CurrentDomain.FirstChanceException -= ObserveCancellationWindow;
            _dispatcher.UnhandledException -= OnUnhandled;
            try { CloseOwnedDialogs(); _bootstrap.Dispose(); Window?.Lifetime.Dispose(); }
            finally { _stop.Dispose(); Strings.SetCulture(_culture); }
        }
    }
}
