using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using MonCore.Monitoring;
using NUnit.Framework;
using WinMon.Controls;

namespace WinMon.Tests;

[TestFixture, NonParallelizable, Apartment(ApartmentState.STA), Category("Integration")]
public sealed class SamplingSeriesCharacterizationTests
{
    private readonly Dictionary<object, object?> _resources = [];
    private MainWindow _window = null!;

    [SetUp]
    public void SetUp()
    {
        var app = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        foreach (DictionaryEntry entry in app.Resources) _resources.Add(entry.Key, entry.Value);
        foreach (var key in new[] { "AlwaysOnTopButton", "TitleActionButton", "ActionBarCollapseButton", "ChartModeSegment",
                     "ActionBarButton", "ActionBarLastButton", "ActivityTab", "CaptionButton", "RangeChip" })
            if (!app.Resources.Contains(key)) app.Resources[key] = new Style();
        if (!app.Resources.Contains("TitleBarButtonText")) app.Resources["TitleBarButtonText"] = new DataTemplate();
        if (!app.Resources.Contains(typeof(Button))) app.Resources[typeof(Button)] = new Style(typeof(Button));
        _window = new MainWindow(true);
        _window.Loaded -= _window.OnLoaded;
    }

    [TearDown]
    public void TearDown()
    {
        if (_window is not null)
        {
            foreach (var view in new FrameworkElement[] { _window.UsageView, _window.Processes, _window.ServiceView, _window.ConnectionView })
                view.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
            _window.CloseForFixture();
        }
        var resources = Application.Current.Resources;
        foreach (var key in resources.Keys.Cast<object>().Where(key => !_resources.ContainsKey(key)).ToArray())
            resources.Remove(key);
        foreach (var (key, value) in _resources) resources[key] = value;
        _resources.Clear();
    }

    private MetricReadout[] Metrics => [_window.CpuMetric, _window.MemoryMetric, _window.DiskMetric,
        _window.NetworkMetric, _window.GpuMetric, _window.SecondaryDiskMetric];

    [TestCase(0, false)]
    [TestCase(0, true)]
    [TestCase(1, false)]
    [TestCase(1, true)]
    [TestCase(50, false)]
    [TestCase(50, true)]
    public void SamplingChoicesForwardStoredTargetsAndOnlyZeroRequestsAllProcesses(int count, bool networkEnabled)
    {
        _window.Preferences = _window.Preferences with
        {
            PrimaryDrive = @"Q:\", SecondaryDrive = @"R:\", NetworkInterfaceId = "adapter-selected",
            TopProcessCount = count, ProcessNetworkMonitoringEnabled = networkEnabled
        };
        var before = _window.Preferences;
        var options = _window.CurrentSamplingOptions();
        Assert.Multiple(() =>
        {
            Assert.That(options.PrimaryDrive, Is.EqualTo(@"Q:\"));
            Assert.That(options.SecondaryDrive, Is.EqualTo(@"R:\"));
            Assert.That(options.NetworkInterfaceId, Is.EqualTo("adapter-selected"));
            Assert.That(options.RetainAll, Is.EqualTo(count == 0));
            Assert.That(options.Retain, Is.Null);
            Assert.That(options.ProcessNetwork, Is.Null);
            Assert.That(options.ProcessNetworkSessionId, Is.EqualTo(Guid.Empty));
            Assert.That(_window.Preferences, Is.SameAs(before));
            Assert.That(_window.SamplingTask, Is.Null);
            Assert.That(new WindowInteropHelper(_window).Handle, Is.EqualTo(IntPtr.Zero));
        });
    }

    [Test]
    public void AutomaticTargetsKeepTheirSentinelsAndPreviouslyReturnedChoicesRemainAnIndependentSnapshot()
    {
        _window.Preferences = _window.Preferences with
        {
            PrimaryDrive = "", SecondaryDrive = null, NetworkInterfaceId = null, TopProcessCount = 0
        };
        var automatic = _window.CurrentSamplingOptions();
        _window.Preferences = _window.Preferences with
        {
            PrimaryDrive = @"S:\", SecondaryDrive = @"T:\", NetworkInterfaceId = "adapter-next", TopProcessCount = 7
        };
        var selected = _window.CurrentSamplingOptions();
        Assert.Multiple(() =>
        {
            Assert.That(automatic.PrimaryDrive, Is.Empty);
            Assert.That(automatic.SecondaryDrive, Is.Null);
            Assert.That(automatic.NetworkInterfaceId, Is.Null);
            Assert.That(automatic.RetainAll, Is.True);
            Assert.That(selected.PrimaryDrive, Is.EqualTo(@"S:\"));
            Assert.That(selected.SecondaryDrive, Is.EqualTo(@"T:\"));
            Assert.That(selected.NetworkInterfaceId, Is.EqualTo("adapter-next"));
            Assert.That(selected.RetainAll, Is.False);
        });
    }

    [Test]
    public void PublishedPinRetentionSurvivesLaterUnpinningWithoutFollowingAReusedProcessId()
    {
        var pinned = new ProcessUsage(20, 10, "pinned", 0, 123, 0) { ImagePath = @"C:\Apps\pinned.exe" };
        _window.Processes.Pins.Toggle(pinned, false);
        _window.Processes.UpdateRetention();
        var first = _window.CurrentSamplingOptions();
        _window.Processes.Pins.Toggle(pinned, false);
        _window.Processes.UpdateRetention();
        var next = _window.CurrentSamplingOptions();
        Assert.Multiple(() =>
        {
            Assert.That(first.Retain, Is.Not.Null);
            Assert.That(first.Retain!(pinned), Is.True);
            Assert.That(first.Retain(pinned with { CreationTicks = 11 }), Is.False);
            Assert.That(next.Retain, Is.Null);
            Assert.That(first.Retain(pinned), Is.True);
        });
    }

    [TestCase(true, 1, 5, 1, 0)]
    [TestCase(true, 2, 5, 2, 1)]
    [TestCase(true, 4, 5, 4, 2)]
    [TestCase(true, 8, 5, 8, 3)]
    [TestCase(true, 16, 5, 16, 4)]
    [TestCase(true, 32, 0, 32, 5)]
    [TestCase(true, 63, 4, 63, 4)]
    [TestCase(true, 26, 0, 26, 1)]
    [TestCase(true, 0, 3, 0, 3)]
    [TestCase(false, 32, 5, 0, 5)]
    [TestCase(false, 34, 5, 2, 1)]
    public void SeriesEventsPreserveBitMappingAndChooseTheFirstVisibleFocusOnlyWhenNeeded(
        bool secondary, int mask, int focus, int expectedMask, int expectedFocus)
    {
        var metrics = Metrics;
        for (var i = 0; i < metrics.Length; i++) metrics[i].Toggle.IsChecked = (mask & (1 << i)) != 0;
        var before = _window.Preferences with
        {
            SecondaryDrive = secondary ? @"R:\" : null, FocusMetric = focus,
            ShowSecondaryDisk = true, TopProcessCount = 7, NetworkInterfaceId = "unchanged"
        };
        _window.Preferences = before;
        metrics[0].Toggle.RaiseEvent(new RoutedEventArgs(ToggleButton.CheckedEvent));
        var expected = before with
        {
            VisibleMetrics = expectedMask & 15, ShowGpu = (expectedMask & 16) != 0,
            ShowSecondaryDisk = secondary ? (expectedMask & 32) != 0 : before.ShowSecondaryDisk,
            FocusMetric = expectedFocus
        };
        Assert.Multiple(() =>
        {
            Assert.That(_window.Preferences, Is.EqualTo(expected));
            Assert.That(_window.OverallGraph.VisibleMetrics, Is.EqualTo(expectedMask));
            Assert.That(_window.OverallGraph.FocusMetric, Is.EqualTo(expectedFocus));
            Assert.That(new WindowInteropHelper(_window).Handle, Is.EqualTo(IntPtr.Zero));
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void AbsentSecondaryDrivePreservesItsStoredVisibilityAndIgnoresAnIndeterminateToggle(bool storedVisibility)
    {
        foreach (var metric in Metrics) metric.Toggle.IsChecked = false;
        _window.SecondaryDiskMetric.Toggle.IsChecked = true;
        _window.GpuMetric.Toggle.IsChecked = null;
        _window.Preferences = _window.Preferences with
        {
            SecondaryDrive = null, ShowSecondaryDisk = storedVisibility, ShowGpu = true, FocusMetric = 2
        };
        _window.MemoryMetric.Toggle.IsChecked = true;
        Assert.Multiple(() =>
        {
            Assert.That(_window.Preferences.VisibleMetrics, Is.EqualTo(2));
            Assert.That(_window.Preferences.ShowGpu, Is.False);
            Assert.That(_window.Preferences.ShowSecondaryDisk, Is.EqualTo(storedVisibility));
            Assert.That(_window.Preferences.FocusMetric, Is.EqualTo(1));
            Assert.That(_window.OverallGraph.VisibleMetrics, Is.EqualTo(2));
            Assert.That(_window.OverallGraph.FocusMetric, Is.EqualTo(1));
        });
    }

    [Test]
    public void RestoringChartTogglesDoesNotPublishIntermediateSeriesPreferences()
    {
        Assert.That(Updates.UpdateRuntime.RestartHistory, Is.Null);
        foreach (var metric in Metrics) metric.Toggle.IsChecked = false;
        var observed = new List<(int Visible, bool Gpu, int Focus)>();
        RoutedEventHandler observe = (_, _) => observed.Add((_window.Preferences.VisibleMetrics,
            _window.Preferences.ShowGpu, _window.Preferences.FocusMetric));
        foreach (var metric in Metrics) metric.Toggle.Checked += observe;
        try
        {
            _window.StopForFixtureAsync().GetAwaiter().GetResult();
            _window.OnLoaded(_window, new RoutedEventArgs(FrameworkElement.LoadedEvent));
            Assert.Multiple(() =>
            {
                Assert.That(observed, Has.Count.EqualTo(5));
                Assert.That(observed, Is.All.EqualTo((15, true, 0)));
                Assert.That(_window.OverallGraph.VisibleMetrics, Is.EqualTo(31));
                Assert.That(_window.SamplingTask, Is.Null);
                Assert.That(_window.Lifetime.IsCancellationRequested, Is.True);
                Assert.That(new WindowInteropHelper(_window).Handle, Is.EqualTo(IntPtr.Zero));
                Assert.That(_window.IsVisible, Is.False);
            });
        }
        finally
        {
            foreach (var metric in Metrics) metric.Toggle.Checked -= observe;
        }
    }
}
