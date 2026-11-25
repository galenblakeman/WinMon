using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using NUnit.Framework;
using WinMon.Diagnostics;
using WinMon.Monitoring;
using WinMon.Preferences;
using WinMon.Resources;

namespace WinMon.Tests;

public sealed partial class MainWindowWorkflowCharacterizationTests
{
    [TestCase(0), TestCase(1), TestCase(2), Category("Integration"), SetCulture("en-US")]
    public void ChartsWorkflowDiagnosticRejectsInsufficientSamplesAfterExpandingControls(int count)
    {
        using var scope = new ChartsWorkflowScope(_directory, count);
        var exception = Assert.Throws<InvalidOperationException>(scope.Run);
        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Is.EqualTo("Native chart samples did not arrive."));
            Assert.That(scope.Window.Session.History.Samples, Has.Count.EqualTo(count));
            Assert.That(scope.Window.ChartLegend.IsVisible, Is.True);
            Assert.That(scope.Window.Preferences.ChartResourcesCollapsed, Is.False);
            Assert.That(Directory.GetFiles(_directory), Is.Empty);
        });
        scope.AssertOwnerAvailable();
    }

    [Test, Category("Integration"), SetCulture("en-US")]
    public void ChartsWorkflowDiagnosticRejectsInitiallyExpandedControlsWithoutChangingThem()
    {
        using var scope = new ChartsWorkflowScope(_directory);
        scope.Window.ChartResourcesToggle.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        var before = scope.Window.Preferences;
        var exception = Assert.Throws<InvalidOperationException>(scope.Run);
        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Is.EqualTo("Resource controls must start collapsed with a show-controls tooltip."));
            Assert.That(scope.Window.Preferences, Is.SameAs(before));
            Assert.That(scope.Window.ChartLegend.IsVisible, Is.True);
            Assert.That(Directory.GetFiles(_directory), Is.Empty);
        });
        scope.AssertOwnerAvailable();
    }

    [TestCase(false), TestCase(true), Category("Integration"), SetCulture("en-US")]
    public void ChartsWorkflowDiagnosticRejectsMalformedMenuBeforeCapturing(bool separator)
    {
        using var scope = new ChartsWorkflowScope(_directory);
        var selector = scope.Window.ChartTypeSelector;
        var item = selector.Items.Cast<object>().First(value => separator ? value is Separator : value is ComboBoxItem);
        selector.Items.Remove(item);
        var exception = Assert.Throws<InvalidOperationException>(scope.Run);
        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Is.EqualTo("The combined chart menu has the wrong choices."));
            Assert.That(selector.Items.Contains(item), Is.False);
            Assert.That(scope.Window.ChartLegend.IsVisible, Is.True);
            Assert.That(Directory.GetFiles(_directory), Is.Empty);
        });
        scope.AssertOwnerAvailable();
    }

    [Test, Category("Integration"), SetCulture("en-US")]
    public void ChartsWorkflowDiagnosticPreCancellationRestoresThemeAndClosesTooltip()
    {
        using var scope = new ChartsWorkflowScope(_directory);
        var original = scope.Window.Preferences;
        scope.Cancel();
        Assert.Catch<OperationCanceledException>(scope.Run);
        AssertChartsWorkflowFirstCaptureState(scope, original);
        Assert.That(Directory.GetFiles(_directory), Is.Empty);
    }

    [TestCase(0), TestCase(1), TestCase(2), TestCase(3), TestCase(4), TestCase(5)]
    [Category("Integration"), SetCulture("en-US")]
    public void ChartsWorkflowDiagnosticRejectsInvalidLogicalReadingsWithoutReplacingSamples(int partition)
    {
        using var scope = new ChartsWorkflowScope(_directory, logicalPartition: partition);
        var samples = scope.Window.Session.History.Samples.ToArray();
        var exception = Assert.Throws<InvalidOperationException>(scope.Run);
        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Is.EqualTo("Native logical-processor readings did not arrive."));
            Assert.That(scope.Window.Session.History.Samples, Is.EqualTo(samples));
            Assert.That(scope.Window.Preferences.ResourceChartType, Is.EqualTo(ResourceChartType.LogicalProcessors));
            Assert.That(scope.Window.SelectedSample, Is.Null);
            Assert.That(scope.Window.ChartTypeSelector.IsDropDownOpen, Is.False);
            Assert.That(Directory.GetFiles(_directory, "chart-logicalprocessors.png"), Is.Empty);
            Assert.That(Directory.GetFiles(_directory, "chart-legend-narrow-german.png"), Is.Empty);
            Assert.That(((ToolTip)scope.Window.LiveCaption.ToolTip).IsOpen, Is.False);
        });
        scope.AssertOwnerAvailable();
    }

    [Test, Category("Integration"), SetCulture("en-US")]
    public void ChartsWorkflowDiagnosticLockedFirstCapturePreservesSentinelAndClosesTooltip()
    {
        using var scope = new ChartsWorkflowScope(_directory);
        var original = scope.Window.Preferences;
        var path = Path.Combine(_directory, "chart-help-header-dark.png");
        File.WriteAllText(path, "owned chart sentinel");
        using var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        try
        {
            Assert.Throws<IOException>(scope.Run);
            AssertChartsWorkflowFirstCaptureState(scope, original);
            Assert.That(File.ReadAllText(path), Is.EqualTo("owned chart sentinel"));
            Assert.That(Directory.GetFiles(_directory).Select(Path.GetFileName),
                Is.EqualTo(new[] { "chart-help-header-dark.png" }));
        }
        finally { PreserveChartsWorkflowArtifacts("locked-first-capture"); }
    }

    [Test, Category("Integration"), SetCulture("en-US")]
    public void ChartsWorkflowDiagnosticPreservesSamplesAndExercisesChoicesFocusAndGermanLegend()
    {
        using var scope = new ChartsWorkflowScope(_directory);
        var samples = scope.Window.Session.History.Samples.ToArray();
        var width = scope.Window.Width;
        try
        {
            scope.Run();
            var window = scope.Window;
            Assert.Multiple(() =>
            {
                Assert.That(window.Session.History.Samples, Is.EqualTo(samples));
                Assert.That(window.SelectedSample, Is.Null);
                Assert.That(window.SnapshotLayer.Visibility, Is.EqualTo(Visibility.Collapsed));
                Assert.That(window.Preferences.ResourceChartType, Is.EqualTo(ResourceChartType.Overlay));
                Assert.That(window.OverallGraph.ChartType, Is.EqualTo(ResourceChartType.Overlay));
                Assert.That(window.OverallGraph.VisibleMetrics, Is.EqualTo(31));
                Assert.That(window.Preferences.SecondaryDrive, Is.Null);
                Assert.That(window.Preferences.Language, Is.EqualTo("en"));
                Assert.That(window.Width, Is.EqualTo(width));
                Assert.That(window.ChartLegend.IsVisible, Is.True);
                Assert.That(window.ChartTypeSelector.IsDropDownOpen, Is.False);
                Assert.That(window.ChartBody.Visibility, Is.EqualTo(Visibility.Visible));
                Assert.That(((ToolTip)window.LiveCaption.ToolTip).IsOpen, Is.False);
                Assert.That(((ToolTip)window.ChartTypeSelector.ToolTip).IsOpen, Is.False);
            });
            scope.AssertOwnerAvailable();
            var names = Directory.GetFiles(_directory, "*.png").Select(Path.GetFileName).ToArray();
            foreach (var theme in new[] { "dark", "light" })
            foreach (var target in new[] { "header", "selector" })
                Assert.That(names, Does.Contain($"chart-help-{target}-{theme}.png"));
            foreach (var type in Enum.GetValues<ResourceChartType>())
                Assert.That(names, Does.Contain($"chart-{type.ToString().ToLowerInvariant()}.png"));
            foreach (var name in new[] { "chart-menu.png", "chart-focus-network.png", "chart-legend-narrow-german.png" })
                Assert.That(names, Does.Contain(name));
            Assert.That(names, Has.Length.EqualTo(12));
            foreach (var path in Directory.GetFiles(_directory, "*.png"))
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                var frame = BitmapFrame.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                Assert.That(frame.PixelWidth, Is.GreaterThan(0));
                Assert.That(frame.PixelHeight, Is.GreaterThan(0));
            }
        }
        finally { PreserveChartsWorkflowArtifacts("complete"); }
    }

    private static void AssertChartsWorkflowFirstCaptureState(ChartsWorkflowScope scope, MonitorPreferences original)
    {
        Assert.Multiple(() =>
        {
            Assert.That(scope.Window.Preferences, Is.EqualTo(original with { ChartResourcesCollapsed = false }));
            Assert.That(scope.Window.Session.History.Samples, Has.Count.EqualTo(3));
            Assert.That(((ToolTip)scope.Window.LiveCaption.ToolTip).IsOpen, Is.False);
            Assert.That(((ToolTip)scope.Window.ChartTypeSelector.ToolTip).IsOpen, Is.False);
            Assert.That(scope.Window.ChartTypeSelector.IsDropDownOpen, Is.False);
        });
        scope.AssertOwnerAvailable();
    }

    private void PreserveChartsWorkflowArtifacts(string scenario)
    {
        if (TestContext.Parameters.Get("ChartsWorkflowArtifactDirectory") is not { Length: > 0 } artifacts) return;
        artifacts = Path.Combine(artifacts, scenario);
        Directory.CreateDirectory(artifacts);
        foreach (var path in Directory.GetFiles(_directory))
            File.Copy(path, Path.Combine(artifacts, Path.GetFileName(path)), true);
    }

    private sealed class ChartsWorkflowScope : IDisposable
    {
        private readonly UpdateUi _ui;
        private readonly string _directory;
        private readonly CancellationTokenSource _stop = new();
        private readonly CultureInfo? _stringsCulture = Strings.Culture;
        private readonly CultureInfo _culture = CultureInfo.CurrentUICulture;
        private readonly CultureInfo? _defaultCulture = CultureInfo.DefaultThreadCurrentUICulture;
        internal MainWindow Window => _ui.Window;

        internal ChartsWorkflowScope(string directory, int count = 3, int logicalPartition = -1)
        {
            _directory = directory;
            Directory.CreateDirectory(directory);
            _ui = new UpdateUi(directory, true);
            try
            {
                InstallDesignActionStyles();
                InstallServicesWorkflowTitleStyles();
                Assert.That(WinMon.Updates.UpdateRuntime.RestartHistory, Is.Null);
                Window.StopForFixtureAsync().GetAwaiter().GetResult();
                Window.OnLoaded(Window, new RoutedEventArgs(FrameworkElement.LoadedEvent));
                Window.Preferences = Window.Preferences with { Language = "en", Theme = "Dark" };
                Window.ApplyLanguage();
                Window.ApplyTheme(false, false);
                var anchor = DateTimeOffset.UnixEpoch.AddDays(1);
                double?[]? processors = logicalPartition switch
                {
                    0 => null,
                    1 => [],
                    2 => new double?[65],
                    3 => [null, null],
                    4 => [-0.01, 0],
                    5 => [100, 100.01],
                    _ => [null, 0, 25, 100]
                };
                for (var i = 0; i < count; i++)
                    Window.Session.History.Add(new MonitorSample(anchor.AddSeconds(i), 20 + i, [], 0,
                        false, System.Diagnostics.Stopwatch.Frequency * (i + 1))
                    {
                        LogicalProcessorPercents = processors,
                        Resources = new ResourceSample { TotalMemoryBytes = 8UL << 30, UsedMemoryBytes = 4UL << 30 }
                    });
                Window.Width = 1000;
                Window.Height = 840;
                Window.RefreshPresentation();
                Window.Show();
                Window.UpdateLayout();
                AssertOwnerAvailable();
            }
            catch { Dispose(); throw; }
        }

        internal void Cancel() => _stop.Cancel();
        internal void Run()
        {
            _stop.CancelAfter(TimeSpan.FromSeconds(45));
            var bounded = new WindowDiagnostics(Window).CaptureChartDiagnosticsAsync(_directory, _stop.Token)
                .WaitAsync(TimeSpan.FromSeconds(60));
            if (!bounded.IsCompleted)
            {
                var frame = new DispatcherFrame();
                _ = bounded.ContinueWith(_ => Window.Dispatcher.BeginInvoke(() => frame.Continue = false),
                    CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
                Dispatcher.PushFrame(frame);
            }
            bounded.GetAwaiter().GetResult();
        }

        internal void AssertOwnerAvailable()
        {
            Assert.Multiple(() =>
            {
                Assert.That(Window.SamplingTask, Is.Null);
                Assert.That(Window.IsVisible, Is.True);
                Assert.That(Application.Current.Windows.Cast<Window>(), Does.Contain(Window));
                Assert.That(Directory.GetFiles(_directory, "settings.json"), Is.Empty);
            });
        }

        public void Dispose()
        {
            _stop.Cancel();
            try { _ui.Dispose(); }
            finally
            {
                CultureInfo.CurrentUICulture = _culture;
                CultureInfo.DefaultThreadCurrentUICulture = _defaultCulture;
                Strings.SetCulture(_stringsCulture);
                _stop.Dispose();
            }
        }
    }
}
