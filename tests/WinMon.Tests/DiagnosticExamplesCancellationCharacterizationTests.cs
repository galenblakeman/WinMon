using System.Globalization;
using System.Windows;
using NUnit.Framework;
using WinMon.Diagnostics;
using WinMon.Preferences;
using WinMon.Resources;

namespace WinMon.Tests;

public sealed partial class MainWindowWorkflowCharacterizationTests
{
    [Test, Category("Integration")]
    public void ExampleWorkflowDiagnosticPreCancellationPreservesEnglishSelectionAndClosesItsFixture()
    {
        using var scope = new ExampleWorkflowScope(_directory);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Catch<OperationCanceledException>(() => FinishWindowCheck(WindowDiagnostics.CaptureExamplesAsync(_directory, cancellation.Token)));
        scope.AssertClosedWithoutImages();
    }

    [Test, Category("Integration")]
    public void ExampleWorkflowDiagnosticCancellationPreservesIllustrativeHistoryAndDisposesTheShownFixture()
    {
        using var scope = new ExampleWorkflowScope(_directory);
        using var cancellation = new CancellationTokenSource();
        var capture = WindowDiagnostics.CaptureExamplesAsync(_directory, cancellation.Token);
        try
        {
            Assert.That(capture.IsCompleted, Is.False, "The first owned Draw must reach its asynchronous capture boundary");
            var window = scope.SourceWindow();
            var closed = false;
            window.Closed += (_, _) => closed = true;
            Assert.That(window.IsVisible, Is.True);
            Assert.That(window.Width, Is.EqualTo(1000));
            Assert.That(window.Height, Is.EqualTo(940));
            Assert.That(window.Preferences.Theme, Is.EqualTo("Dark"));
            Assert.That(window.Preferences.PrimaryDrive, Is.EqualTo("C:"));
            Assert.That(window.Preferences.SecondaryDrive, Is.EqualTo("D:"));
            var samples = window.Session.History.Samples;
            Assert.That(samples, Has.Count.EqualTo(120));
            Assert.That(samples.All(sample => sample.Processes.Count == 7), Is.True);
            Assert.That(samples[92].Processes.Single(process => process.ProcessId == 12000).CpuPercent,
                Is.EqualTo(56.7).Within(1e-10));
            Assert.That(samples[^1].Processes.Single(process => process.ProcessId == 12000).CpuPercent,
                Is.InRange(8.7, 10));
            Assert.That(samples[^1].Timestamp, Is.EqualTo(new DateTimeOffset(2026, 11, 19, 10, 24, 0, TimeSpan.FromHours(-6))));
            Assert.That(samples[92].Resources.DiskActivePercent, Is.EqualTo(66.4).Within(1e-10));
            Assert.That(samples[^1].Resources.NetworkId, Is.EqualTo("fixture"));
            Assert.That(window.SamplingTask, Is.Null);
            Assert.That(window.Session.HasLiveSample, Is.False);
            cancellation.Cancel();
            Assert.Catch<OperationCanceledException>(() => FinishWindowCheck(capture));
            Assert.That(closed, Is.True);
            Assert.That(window.SnapshotCopiedTimer.IsEnabled, Is.False);
            Assert.Throws<ObjectDisposedException>(() => _ = window.Lifetime.Token);
            scope.AssertClosedWithoutImages();
        }
        finally
        {
            cancellation.Cancel();
            if (!capture.IsCompleted) FinishWindowCheck(capture.ContinueWith(_ => { }, TaskScheduler.Default));
            _ = capture.Exception;
        }
    }

    [Test, Category("Integration")]
    public void ExampleWorkflowDiagnosticNullOutputPreservesArgumentFailureAndClosesItsFixture()
    {
        using var scope = new ExampleWorkflowScope(_directory);
        var error = Assert.Throws<ArgumentNullException>(() => FinishWindowCheck(WindowDiagnostics.CaptureExamplesAsync(null!, default)));
        Assert.That(error!.ParamName, Is.EqualTo("path1"));
        scope.AssertClosedWithoutImages();
    }

    private sealed class ExampleWorkflowScope : IDisposable
    {
        private readonly UpdateUi _ui;
        private readonly string _directory;
        private readonly CultureInfo? _resources = Strings.Culture;
        private readonly CultureInfo? _defaultUi = CultureInfo.DefaultThreadCurrentUICulture;
        private readonly CultureInfo _currentUi = CultureInfo.CurrentUICulture;
        private readonly Window[] _existing;
        internal ExampleWorkflowScope(string directory)
        {
            _directory = directory;
            _ui = new UpdateUi(directory, true);
            Directory.CreateDirectory(directory);
            _ui.Window.ApplyTheme(false, false);
            // Draw shows the source-owned window before applying its own theme. Supply
            // the normal initial caption resources that App.xaml provides in the app.
            foreach (var key in new[] { "TitleBarSurface", "TitleText", "Surface", "Text" })
                Application.Current.Resources[key] = _ui.Window.FindResource(key);
            AppLanguages.Apply("ja");
            _existing = Application.Current.Windows.Cast<Window>().ToArray();
        }
        internal MainWindow SourceWindow() => Application.Current.Windows.OfType<MainWindow>().Single(window => !_existing.Contains(window));
        internal void AssertClosedWithoutImages()
        {
            Assert.That(Application.Current.Windows.Cast<Window>().Except(_existing), Is.Empty);
            Assert.That(Strings.Culture?.Name, Is.EqualTo("en"), "The original example workflow retains its English resource choice on cancellation");
            Assert.That(Directory.GetFiles(_directory, "*.png"), Is.Empty);
            Assert.That(Directory.GetFiles(_directory, "*.json"), Is.Empty);
        }
        public void Dispose()
        {
            try { _ui.Dispose(); }
            finally
            {
                CultureInfo.CurrentUICulture = _currentUi;
                CultureInfo.DefaultThreadCurrentUICulture = _defaultUi;
                Strings.SetCulture(_resources);
            }
        }
    }
}
