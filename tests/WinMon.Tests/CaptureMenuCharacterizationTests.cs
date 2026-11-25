using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using NUnit.Framework;
using WinMon.Controls;
using WinMon.Monitoring;
using WinMon.Resources;
using System.Windows.Threading;
using System.Windows.Media;
using ShapePath = System.Windows.Shapes.Path;

namespace WinMon.Tests;

[TestFixture, NonParallelizable, Apartment(ApartmentState.STA), Category("Integration")]
public sealed class CaptureMenuCharacterizationTests
{
    private readonly Dictionary<object, object?> _resources = [];
    private MainWindow _window = null!;
    private string? _temporaryFolder;
    private SynchronizationContext? _previousContext;

    [SetUp]
    public void SetUp()
    {
        var app = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        foreach (System.Collections.DictionaryEntry entry in app.Resources) _resources.Add(entry.Key, entry.Value);
        foreach (var key in new[] { "AlwaysOnTopButton", "TitleActionButton", "ActionBarCollapseButton", "ChartModeSegment",
                     "ActionBarButton", "ActionBarLastButton", "ActivityTab", "CaptionButton", "RangeChip" })
            if (!app.Resources.Contains(key)) app.Resources[key] = new Style();
        if (!app.Resources.Contains("TitleBarButtonText"))
            app.Resources["TitleBarButtonText"] = new DataTemplate();
        if (!app.Resources.Contains(typeof(Button)))
            app.Resources[typeof(Button)] = new Style(typeof(Button));
        _window = new MainWindow(true);
        _window.Loaded -= _window.OnLoaded;
        _window.ApplyTheme(false, false);
        _previousContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new System.Windows.Threading.DispatcherSynchronizationContext());
        _temporaryFolder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "WinMon.CaptureMenu." + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(_temporaryFolder);
        _window.Preferences = _window.Preferences with { AutoCapture = _window.Preferences.AutoCapture with { Folder = _temporaryFolder } };
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
        SynchronizationContext.SetSynchronizationContext(_previousContext);
        if (_temporaryFolder is not null)
        {
            System.IO.Directory.Delete(_temporaryFolder, recursive: true);
            _temporaryFolder = null;
        }
    }

    private ContextMenu ReadMenu(int expectedCount = 0)
    {
        var menu = _window.CreateCapturesMenu();
        var open = (MenuItem)menu.Items[menu.Items.Count - 2];
        var expected = Strings.Format(Strings.AutoCaptureOpenFolderCount, expectedCount);
        if (!Equals(open.Header, expected))
        {
            var descriptor = System.ComponentModel.DependencyPropertyDescriptor.FromProperty(
                HeaderedItemsControl.HeaderProperty, typeof(MenuItem))
                ?? throw new AssertionException("Menu header property descriptor must exist");
            var frame = new DispatcherFrame();
            var timedOut = false;
            EventHandler changed = (_, _) => { if (Equals(open.Header, expected)) frame.Continue = false; };
            var deadline = new DispatcherTimer(DispatcherPriority.Send) { Interval = TimeSpan.FromSeconds(10) };
            deadline.Tick += (_, _) => { timedOut = true; frame.Continue = false; };
            descriptor.AddValueChanged(open, changed);
            deadline.Start();
            try { if (!Equals(open.Header, expected)) Dispatcher.PushFrame(frame); }
            finally { deadline.Stop(); descriptor.RemoveValueChanged(open, changed); }
            Assert.That(timedOut, Is.False, "Owned report count did not complete on its dispatcher");
        }
        Assert.That(open.Header, Is.EqualTo(expected));
        return menu;
    }

    private static MonitorSample Sample(int second) => new(DateTimeOffset.UnixEpoch.AddSeconds(second), 20, [], 0)
    {
        MonotonicTimestamp = (1000L + second) * System.Diagnostics.Stopwatch.Frequency,
        IntervalSeconds = 1
    };

    private static MainWindow.CaptureMarker Marker(int fired, int peak, string rule, CaptureMetric metric = CaptureMetric.Cpu) =>
        new(new CaptureFired(new CaptureRuleSetting(metric, true, 90, 1), Sample(fired), Sample(peak), 97, [97]),
            new Guid(fired, 0, 0, new byte[8]), rule);

    private void ClickRetainedMarker(MenuItem item, string expectedRule)
    {
        Assert.That(item.Header, Is.TypeOf<StackPanel>(), "Only a capture entry may be invoked");
        Assert.That(((TextBlock)((StackPanel)item.Header).Children[0]).Text, Is.EqualTo(expectedRule));
        var before = Application.Current.Windows.Cast<Window>().ToHashSet();
        var unexpected = new List<Window>();
        DispatcherHookEventHandler guard = (_, _) =>
        {
            foreach (var window in Application.Current.Windows.Cast<Window>()
                         .Where(window => !before.Contains(window) && window.IsVisible).ToArray())
            {
                unexpected.Add(window);
                window.Close();
            }
        };
        _window.Dispatcher.Hooks.OperationCompleted += guard;
        try { item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent, item)); }
        finally { _window.Dispatcher.Hooks.OperationCompleted -= guard; }
        Assert.That(unexpected, Is.Empty, "Retained peaks must not open a fallback dialog");
    }

    [Test]
    public void EmptyMenuKeepsWatcherRulesOrderAndUsesLatestOrDefaultResourceLabels(
        [Values("Off", "Empty", "Cpu", "Mixed")] string mode, [Values(false, true)] bool history)
    {
        if (history) _window.Session.Record(Sample(30) with { Resources = new ResourceSample { DiskName = "R:" } });
        CaptureRuleSetting[] rules = mode switch
        {
            "Empty" => [],
            "Cpu" => [new(CaptureMetric.Cpu, true, 90, 1)],
            _ => [new(CaptureMetric.Gpu, true, 90, 1), new(CaptureMetric.Cpu, true, 90, 1),
                new(CaptureMetric.Memory, false, 90, 1), new(CaptureMetric.PrimaryDisk, true, 90, 1)]
        };
        _window.Preferences = _window.Preferences with
        {
            AutoCapture = _window.Preferences.AutoCapture with { Enabled = mode != "Off", Rules = rules }
        };
        var preferences = _window.Preferences;
        var watched = mode switch
        {
            "Off" => Strings.AutoCaptureOff,
            "Empty" => "",
            "Cpu" => Strings.CPU,
            _ => string.Join(", ", Strings.GPU, Strings.CPU, history ? Strings.Format(Strings.DiskLabel, "R:") : Strings.Disk)
        };

        var menu = ReadMenu();

        var watcher = (MenuItem)menu.Items[0];
        var empty = (MenuItem)menu.Items[1];
        Assert.Multiple(() =>
        {
            Assert.That(menu.Items.Count, Is.EqualTo(5));
            Assert.That(watcher.Header, Is.EqualTo(Strings.Format(Strings.AutoCaptureWatching, watched)));
            Assert.That(watcher.IsEnabled, Is.False);
            Assert.That(watcher.FontWeight, Is.EqualTo(FontWeights.SemiBold));
            Assert.That(empty.Header, Is.EqualTo(Strings.AutoCaptureNoneYet));
            Assert.That(empty.IsEnabled, Is.False);
            Assert.That(menu.Items[2], Is.TypeOf<Separator>());
            Assert.That(((MenuItem)menu.Items[4]).Header, Is.EqualTo(Strings.AutoCaptureSettings));
            Assert.That(((MenuItem)menu.Items[3]).IsEnabled, Is.True);
            Assert.That(((MenuItem)menu.Items[4]).IsEnabled, Is.True);
            Assert.That(menu.Background, Is.SameAs(_window.FindResource("Raised")));
            Assert.That(menu.Foreground, Is.SameAs(_window.FindResource("Text")));
            Assert.That(_window.Preferences, Is.SameAs(preferences));
            Assert.That(_window.CaptureMarkers, Is.Empty);
            Assert.That(_window.IsVisible, Is.False);
            Assert.That(new WindowInteropHelper(_window).Handle, Is.EqualTo(IntPtr.Zero));
        });
    }

    [TestCase(1)]
    [TestCase(3)]
    public void CaptureEntriesAreNewestFirstWithResourceIconsAndAccessibleDetails(int count)
    {
        _window.Session.Record(Sample(45));
        var markers = Enumerable.Range(1, count).Select(index => Marker(index * 10, index * 10 - 1,
            "rule " + index, index == 2 ? CaptureMetric.Memory : CaptureMetric.Cpu)).ToArray();
        foreach (var marker in markers) _window.AddCaptureMarkerForFixture(marker);

        var menu = ReadMenu();

        Assert.That(menu.Items.Count, Is.EqualTo(count + 4));
        for (var index = 0; index < count; index++)
        {
            var marker = markers[count - index - 1];
            var item = (MenuItem)menu.Items[index + 1];
            var header = (StackPanel)item.Header;
            var detail = Strings.Format(Strings.AutoCaptureMenuDetail, marker.FiredKey.Timestamp,
                Strings.Format(Strings.PercentOneDecimal, 97), UsageText.Duration(TimeSpan.FromSeconds(45 - (count - index) * 10)));
            var icon = (ShapePath)item.Icon;
            Assert.Multiple(() =>
            {
                Assert.That(((TextBlock)header.Children[0]).Text, Is.EqualTo(marker.Rule));
                Assert.That(((TextBlock)header.Children[1]).Text, Is.EqualTo(detail));
                Assert.That(((TextBlock)header.Children[1]).FontSize, Is.EqualTo(11));
                Assert.That(((TextBlock)header.Children[1]).Foreground, Is.SameAs(_window.FindResource("Muted")));
                Assert.That(AutomationProperties.GetName(item), Is.EqualTo(marker.Rule + ", " + detail));
                Assert.That(item.IsEnabled, Is.True);
                Assert.That(icon.Width, Is.EqualTo(10));
                Assert.That(icon.Height, Is.EqualTo(8));
                Assert.That(icon.Stretch, Is.EqualTo(Stretch.Fill));
                Assert.That(icon.Data.GetArea(), Is.EqualTo(40).Within(0.000001));
                Assert.That(icon.Fill, Is.SameAs(_window.FindResource(marker.Fired.Rule.Metric == CaptureMetric.Memory ? "MemoryAccent" : "CpuAccent")));
            });
        }
        Assert.That(menu.Items[count + 1], Is.TypeOf<Separator>());
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(3)]
    public void ReportCountReadsOwnedReportsWithoutChangingTheirContents(int count)
    {
        for (var index = 0; index < count; index++)
            System.IO.File.WriteAllText(System.IO.Path.Combine(_temporaryFolder!,
                CaptureReports.FileName(DateTimeOffset.UnixEpoch.AddSeconds(index), CaptureMetric.Cpu)),
                CaptureReports.FirstLine(Guid.Empty) + "\nowned body");
        var before = System.IO.Directory.GetFiles(_temporaryFolder!).ToDictionary(path => path, System.IO.File.ReadAllText);

        ReadMenu(count);

        Assert.Multiple(() =>
        {
            Assert.That(System.IO.Directory.GetFiles(_temporaryFolder!), Is.EquivalentTo(before.Keys));
            foreach (var entry in before) Assert.That(System.IO.File.ReadAllText(entry.Key), Is.EqualTo(entry.Value));
        });
    }

    [Test]
    public void ReportCountIgnoresUnrelatedInvalidAndNestedFiles()
    {
        var body = CaptureReports.FirstLine(Guid.Empty) + "\nowned";
        System.IO.File.WriteAllText(System.IO.Path.Combine(_temporaryFolder!, CaptureReports.FileName(DateTimeOffset.UnixEpoch, CaptureMetric.Cpu)), body);
        System.IO.File.WriteAllText(System.IO.Path.Combine(_temporaryFolder!, CaptureReports.FileName(DateTimeOffset.UnixEpoch.AddSeconds(1), CaptureMetric.Memory)), body);
        System.IO.File.WriteAllText(System.IO.Path.Combine(_temporaryFolder!, CaptureReports.FileName(DateTimeOffset.UnixEpoch.AddSeconds(2), CaptureMetric.Cpu)), "unrelated first line");
        System.IO.File.WriteAllText(System.IO.Path.Combine(_temporaryFolder!, "unrelated.txt"), body);
        var child = System.IO.Directory.CreateDirectory(System.IO.Path.Combine(_temporaryFolder!, "nested"));
        System.IO.File.WriteAllText(System.IO.Path.Combine(child.FullName, CaptureReports.FileName(DateTimeOffset.UnixEpoch, CaptureMetric.Cpu)), body);

        ReadMenu(2);

        Assert.That(System.IO.Directory.GetFiles(_temporaryFolder!, "*", System.IO.SearchOption.AllDirectories), Has.Length.EqualTo(5));
    }

    [Test]
    public void CountingMissingFolderDoesNotCreateIt()
    {
        var missing = System.IO.Path.Combine(_temporaryFolder!, "not-created");
        _window.Preferences = _window.Preferences with { AutoCapture = _window.Preferences.AutoCapture with { Folder = missing } };

        ReadMenu();

        Assert.That(System.IO.Directory.Exists(missing), Is.False);
    }

    [TestCase(0, 30)]
    [TestCase(1, 20)]
    [TestCase(2, 10)]
    public void EachCaptureCallbackSelectsItsOwnRetainedPeakWithoutOpeningWindows(int index, int expectedSecond)
    {
        _window.Preferences = _window.Preferences with { ChartRangeSeconds = 120 };
        var samples = new[] { Sample(10), Sample(20), Sample(30), Sample(45) };
        foreach (var sample in samples) _window.Session.Record(sample);
        foreach (var second in new[] { 10, 20, 30 }) _window.AddCaptureMarkerForFixture(Marker(second + 1, second, "rule " + second));
        var menu = ReadMenu();

        ClickRetainedMarker((MenuItem)menu.Items[index + 1], "rule " + expectedSecond);

        Assert.Multiple(() =>
        {
            Assert.That(_window.SelectedSample, Is.SameAs(samples.Single(sample => sample.Timestamp == DateTimeOffset.UnixEpoch.AddSeconds(expectedSecond))));
            Assert.That(_window.Preferences.ChartRangeSeconds, Is.EqualTo(120));
            Assert.That(_window.OwnedWindows.Count, Is.Zero);
        });
    }

    [TestCase(120, 120)]
    [TestCase(121, 600)]
    [TestCase(600, 600)]
    [TestCase(601, 1800)]
    [TestCase(3601, 120)]
    public void RetainedPeakUsesTheSmallestAvailableRangeWithoutInventingOne(int age, int expectedRange)
    {
        _window.Session.History.Resize(7200);
        var peak = Sample(10);
        _window.Session.Record(peak);
        _window.Session.Record(Sample(10 + age));
        _window.Preferences = _window.Preferences with { ChartRangeSeconds = 120, RetentionSeconds = 3600 };
        _window.AddCaptureMarkerForFixture(Marker(11, 10, "range rule"));
        var menu = ReadMenu();
        Assert.That(_window.Session.History.Samples, Does.Contain(peak), "The callback must stay on the retained-peak path");

        ClickRetainedMarker((MenuItem)menu.Items[1], "range rule");

        Assert.Multiple(() =>
        {
            Assert.That(_window.SelectedSample, Is.SameAs(peak));
            Assert.That(_window.Preferences.ChartRangeSeconds, Is.EqualTo(expectedRange));
            Assert.That(_window.OwnedWindows.Count, Is.Zero);
        });
    }

    [TestCase(0)]
    [TestCase(999)]
    public void EffectiveShortRangeDoesNotRewriteAnUnrecognizedSavedRange(int saved)
    {
        var peak = Sample(10);
        _window.Session.Record(peak);
        _window.Session.Record(Sample(45));
        _window.Preferences = _window.Preferences with { ChartRangeSeconds = saved };
        _window.AddCaptureMarkerForFixture(Marker(11, 10, "fallback range"));
        var menu = ReadMenu();

        ClickRetainedMarker((MenuItem)menu.Items[1], "fallback range");

        Assert.Multiple(() =>
        {
            Assert.That(_window.SelectedSample, Is.SameAs(peak));
            Assert.That(_window.Preferences.ChartRangeSeconds, Is.EqualTo(saved));
        });
    }
}
