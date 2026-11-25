using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using MonCore.Monitoring;
using NUnit.Framework;
using WinMon.Preferences;
using WinMon.Resources;

namespace WinMon.Tests;

[TestFixture, NonParallelizable, Apartment(ApartmentState.STA), Category("Integration")]
[SetCulture("en-US"), SetUICulture("en-US")]
public sealed class PreferencesApplyCharacterizationTests
{
    private readonly Dictionary<object, object?> _resources = [];
    private MainWindow? _window;
    private string _directory = null!;
    private PreferenceStore _store = null!;
    private CultureInfo? _resourceCulture;
    private CultureInfo? _defaultUiCulture;
    private CultureInfo _uiCulture = null!;
    private CultureInfo _formats = null!;

    [SetUp]
    public void SetUp()
    {
        _resourceCulture = Strings.Culture;
        _defaultUiCulture = CultureInfo.DefaultThreadCurrentUICulture;
        _uiCulture = CultureInfo.CurrentUICulture;
        _formats = CultureInfo.CurrentCulture;
        Strings.SetCulture(CultureInfo.GetCultureInfo("en"));
        _directory = Path.Combine(Path.GetTempPath(), "WinMon.PreferencesApply." + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        _store = new PreferenceStore(Path.Combine(_directory, "settings.json"));
        var app = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        foreach (System.Collections.DictionaryEntry entry in app.Resources) _resources.Add(entry.Key, entry.Value);
        foreach (var key in new[] { "AlwaysOnTopButton", "TitleActionButton", "ActionBarCollapseButton", "ChartModeSegment",
                     "ActionBarButton", "ActionBarLastButton", "ActivityTab", "CaptionButton", "RangeChip" })
            if (!app.Resources.Contains(key)) app.Resources[key] = new Style();
        if (!app.Resources.Contains("TitleBarButtonText")) app.Resources["TitleBarButtonText"] = new DataTemplate();
        if (!app.Resources.Contains(typeof(Button))) app.Resources[typeof(Button)] = new Style(typeof(Button));
    }

    [TearDown]
    public void TearDown()
    {
        if (_window is not null)
        {
            foreach (var view in new FrameworkElement[] { _window.UsageView, _window.Processes, _window.ServiceView, _window.ConnectionView })
                view.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
            _window.CloseForFixture();
            _window = null;
        }
        var resources = Application.Current.Resources;
        foreach (var key in resources.Keys.Cast<object>().Where(key => !_resources.ContainsKey(key)).ToArray()) resources.Remove(key);
        foreach (var (key, value) in _resources) resources[key] = value;
        _resources.Clear();
        CultureInfo.CurrentCulture = _formats;
        CultureInfo.CurrentUICulture = _uiCulture;
        CultureInfo.DefaultThreadCurrentUICulture = _defaultUiCulture;
        Strings.SetCulture(_resourceCulture);
        foreach (var child in Directory.GetDirectories(_directory)) Directory.Delete(child, recursive: false);
        foreach (var file in Directory.GetFiles(_directory)) File.Delete(file);
        Directory.Delete(_directory, recursive: false);
    }

    private static MonitorPreferences Preferences() => PreferenceStore.Validate(new MonitorPreferences
    {
        PrimaryDrive = "C:", SecondaryDrive = "D:", Theme = "Dark", Language = "en", RetentionSeconds = 3600,
        ResourceChartType = ResourceChartType.Focus, FocusMetric = 2, ChartRangeSeconds = 1800,
        ChartResourcesCollapsed = false, GroupProcesses = false, PinnedServices = ["snapshot-service"],
        KeepResourceTrends = true, KeepHistoryAcrossRestarts = true
    });

    private MainWindow Window(PreferenceStore? store = null)
    {
        _window = new MainWindow(true, preferenceStore: store ?? _store);
        _window.Loaded -= _window.OnLoaded;
        _window.Preferences = Preferences();
        return _window;
    }

    [TestCase(false), TestCase(true)]
    public void SavePersistsTheConfirmedSnapshotAndAppliesDialogFieldsToCurrentChoices(bool reset) => Run(async () =>
    {
        var window = Window();
        var opened = window.Preferences;
        var current = opened with { GroupProcesses = true, TopProcessCount = 20,
            SelectedActivityTab = 3, PinnedServices = ["live-service"] };
        window.Preferences = current;
        window.Session.History.Resize(7);
        var edited = opened with
        {
            PrimaryDrive = "D:", SecondaryDrive = null, Theme = "Light", Language = "ja",
            RetentionSeconds = 1800, GaugeStyle = GaugeStyle.Needle, GaugePeakWindowSeconds = 300,
            KeepHistoryAcrossRestarts = true, StartWithWindows = true
        };
        // The real dialog applies its reset request to Result before handing it to this coordinator.
        var confirmed = SettingsWindow.ApplySettings(opened, edited, reset);

        await window.ApplyPreferencesAsync(confirmed, reset);

        var persisted = await _store.LoadAsync();
        var live = window.Preferences;
        Assert.Multiple(() =>
        {
            Assert.That(persisted.PrimaryDrive, Is.EqualTo("D:"));
            Assert.That(persisted.SecondaryDrive, Is.Null);
            Assert.That(persisted.Theme, Is.EqualTo("Light"));
            Assert.That(persisted.Language, Is.EqualTo("ja"));
            Assert.That(persisted.GaugeStyle, Is.EqualTo(GaugeStyle.Needle));
            Assert.That(persisted.RetentionSeconds, Is.EqualTo(1800));
            Assert.That(persisted.StartWithWindows, Is.True, "Diagnostic mode persists the flag without registering startup.");
            Assert.That(persisted.GroupProcesses, Is.False);
            Assert.That(persisted.PinnedServices, Is.EqualTo(new[] { "snapshot-service" }));
            Assert.That(live.PrimaryDrive, Is.EqualTo("D:"));
            Assert.That(live.SecondaryDrive, Is.Null);
            Assert.That(live.Theme, Is.EqualTo("Light"));
            Assert.That(live.Language, Is.EqualTo("ja"));
            Assert.That(live.GaugeStyle, Is.EqualTo(GaugeStyle.Needle));
            Assert.That(live.GaugePeakWindowSeconds, Is.EqualTo(300));
            Assert.That(live.GroupProcesses, Is.True);
            Assert.That(live.TopProcessCount, Is.EqualTo(20));
            Assert.That(live.SelectedActivityTab, Is.EqualTo(3));
            Assert.That(live.PinnedServices, Is.EqualTo(new[] { "live-service" }));
            Assert.That(live.ResourceChartType, Is.EqualTo(reset ? ResourceChartType.Overlay : ResourceChartType.Focus));
            Assert.That(live.ChartRangeSeconds, Is.EqualTo(reset ? 120 : 1800));
            Assert.That(persisted.ResourceChartType, Is.EqualTo(live.ResourceChartType));
            Assert.That(window.Session.History.Capacity, Is.EqualTo(1800));
            Assert.That(window.SecondaryDiskMetric.Visibility, Is.EqualTo(Visibility.Collapsed));
            Assert.That(window.SecondaryDiskColumn.Width.Value, Is.Zero);
            Assert.That(window.CpuMetric.GaugeStyle, Is.EqualTo(GaugeStyle.Needle));
            Assert.That(Strings.Culture!.Name, Is.EqualTo("ja"));
            Assert.That(Directory.GetFiles(_directory, ".settings.json.*.tmp"), Is.Empty);
            Assert.That(opened.PrimaryDrive, Is.EqualTo("C:"));
            Assert.That(window.IsVisible, Is.False);
        });
    });

    [TestCase(false), TestCase(true)]
    public void NormalHistoryAndItsTemporaryAreDeletedOnlyWhenRestartHistoryIsDisabled(bool keepHistory) => Run(async () =>
    {
        var window = Window();
        var normal = _store.NormalHistoryPath;
        var temporary = Path.Combine(_directory, "." + Path.GetFileName(normal) + ".owned.tmp");
        var unrelated = Path.Combine(_directory, ".unrelated-owned.tmp");
        File.WriteAllText(normal, "owned normal history");
        File.WriteAllText(temporary, "owned pending history");
        File.WriteAllText(unrelated, "owned unrelated data");

        await window.ApplyPreferencesAsync(window.Preferences with { KeepHistoryAcrossRestarts = keepHistory });

        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(normal), Is.EqualTo(keepHistory));
            Assert.That(File.Exists(temporary), Is.EqualTo(keepHistory));
            Assert.That(File.ReadAllText(unrelated), Is.EqualTo("owned unrelated data"));
            Assert.That(window.Preferences.KeepHistoryAcrossRestarts, Is.EqualTo(keepHistory));
            if (keepHistory) Assert.That(File.ReadAllText(normal), Is.EqualTo("owned normal history"));
        });
    });

    [Test]
    public void ResourceTrendContinuityBreaksExactlyWhenThePreferenceChanges(
        [Values(false, true)] bool wasEnabled, [Values(false, true)] bool enabled) => Run(async () =>
    {
        var window = Window();
        window.Preferences = window.Preferences with { KeepResourceTrends = wasEnabled };
        var now = DateTimeOffset.UtcNow;
        var minute = new DateTimeOffset(now.UtcTicks - now.UtcTicks % TimeSpan.TicksPerMinute, TimeSpan.Zero).AddMinutes(-1);
        var archive = window.ResourceTrendsForFixture;
        MonitorSample Sample(int second, double cpu) => new(minute.AddSeconds(second), cpu, [], 0)
            { IntervalSeconds = 1, MonotonicTimestamp = (1000L + second) * Stopwatch.Frequency };
        archive.Add(Sample(10, 20));

        await window.ApplyPreferencesAsync(window.Preferences with { KeepResourceTrends = enabled });
        archive.Add(Sample(11, 40));

        var value = archive.Snapshot(minute.AddSeconds(12)).Buckets.Single().Values[ResourceTrendMetric.Cpu];
        Assert.Multiple(() =>
        {
            Assert.That(window.ResourceTrendsForFixture, Is.SameAs(archive));
            Assert.That(value.WeightedSum, Is.EqualTo(60));
            Assert.That(value.ObservedSeconds, Is.EqualTo(2));
            Assert.That(value.Break, Is.EqualTo(wasEnabled == enabled ? ResourceTrendBreak.None : ResourceTrendBreak.CollectionRestart));
            Assert.That(window.Preferences.KeepResourceTrends, Is.EqualTo(enabled));
        });
    });

    [TestCase("parent-file"), TestCase("destination-directory")]
    public void SaveFailurePropagatesWithoutPublishingPreferencesOrDeletingHistory(string failure) => Run(async () =>
    {
        var blockedParent = Path.Combine(_directory, "blocked-parent");
        File.WriteAllText(blockedParent, "owned blocker");
        var store = failure == "parent-file"
            ? new PreferenceStore(Path.Combine(blockedParent, "settings.json")) : _store;
        if (failure == "destination-directory") Directory.CreateDirectory(Path.Combine(_directory, "settings.json"));
        var window = Window(store);
        var before = window.Preferences;
        var culture = Strings.Culture;
        var normal = _store.NormalHistoryPath;
        File.WriteAllText(normal, "owned normal history");
        window.Session.History.Resize(7);
        Exception? observed = null;

        try { await window.ApplyPreferencesAsync(before with { Theme = "Light", Language = "ja", KeepHistoryAcrossRestarts = false, RetentionSeconds = 600 }); }
        catch (Exception ex) { observed = ex; }

        Assert.Multiple(() =>
        {
            Assert.That(observed, Is.InstanceOf<IOException>().Or.InstanceOf<UnauthorizedAccessException>());
            Assert.That(window.Preferences, Is.SameAs(before));
            Assert.That(window.Session.History.Capacity, Is.EqualTo(7));
            Assert.That(Strings.Culture, Is.SameAs(culture));
            Assert.That(File.ReadAllText(normal), Is.EqualTo("owned normal history"));
            Assert.That(File.ReadAllText(blockedParent), Is.EqualTo("owned blocker"));
            Assert.That(Directory.GetFiles(_directory, ".settings.json.*.tmp"), Is.Empty);
        });
    });

    [Test]
    public void CancellationBeforeSaveKeepsTheExistingFilePreferencesAndHistory() => Run(async () =>
    {
        var window = Window();
        var before = window.Preferences;
        await _store.SaveAsync(before);
        var file = Path.Combine(_directory, "settings.json");
        var bytes = File.ReadAllBytes(file);
        File.WriteAllText(_store.NormalHistoryPath, "owned normal history");
        window.Lifetime.Cancel();
        OperationCanceledException? observed = null;

        try { await window.ApplyPreferencesAsync(before with { Theme = "Light", KeepHistoryAcrossRestarts = false }); }
        catch (OperationCanceledException ex) { observed = ex; }

        Assert.Multiple(() =>
        {
            Assert.That(observed, Is.Not.Null);
            Assert.That(observed!.CancellationToken, Is.EqualTo(window.Lifetime.Token));
            Assert.That(window.Preferences, Is.SameAs(before));
            Assert.That(File.ReadAllBytes(file), Is.EqualTo(bytes));
            Assert.That(File.ReadAllText(_store.NormalHistoryPath), Is.EqualTo("owned normal history"));
            Assert.That(Directory.GetFiles(_directory, ".settings.json.*.tmp"), Is.Empty);
        });
    });

    [Test]
    public void NullDiagnosticResultRetainsTheStoreArgumentExceptionAndCurrentState() => Run(async () =>
    {
        var window = Window();
        var before = window.Preferences;
        ArgumentNullException? observed = null;
        try { await window.ApplyPreferencesAsync(null!); }
        catch (ArgumentNullException ex) { observed = ex; }
        Assert.Multiple(() =>
        {
            Assert.That(observed, Is.Not.Null);
            Assert.That(observed!.ParamName, Is.EqualTo("preferences"));
            Assert.That(window.Preferences, Is.SameAs(before));
            Assert.That(Directory.GetFileSystemEntries(_directory), Is.Empty);
        });
    });

    private static void Run(Func<Task> action)
    {
        var previous = SynchronizationContext.Current;
        var dispatcher = Dispatcher.CurrentDispatcher;
        try
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            var task = action();
            if (!task.IsCompleted)
            {
                var frame = new DispatcherFrame();
                _ = task.ContinueWith(_ => dispatcher.BeginInvoke(() => frame.Continue = false),
                    CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
                Dispatcher.PushFrame(frame);
            }
            task.GetAwaiter().GetResult();
        }
        finally { SynchronizationContext.SetSynchronizationContext(previous); }
    }
}
