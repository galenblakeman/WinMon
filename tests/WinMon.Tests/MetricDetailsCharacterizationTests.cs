using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using MonCore.Monitoring;
using NUnit.Framework;
using WinMon.Controls;
using WinMon.Resources;

namespace WinMon.Tests;

[TestFixture, NonParallelizable, Apartment(ApartmentState.STA), SetCulture("en-US"), SetUICulture("en-US")]
public sealed class MetricDetailsCharacterizationTests
{
    private const ulong GiB = 1_073_741_824;
    private static readonly DateTimeOffset Start = new(2025, 2, 11, 12, 0, 0, TimeSpan.Zero);
    private readonly List<object> _addedResources = [];
    private MainWindow _window = null!;
    private static string Dash => Strings.MissingReading;

    [SetUp]
    public void SetUp()
    {
        var app = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        foreach (var key in new[] { "AlwaysOnTopButton", "TitleActionButton", "ActionBarCollapseButton", "ChartModeSegment",
                     "ActionBarButton", "ActionBarLastButton", "ActivityTab", "CaptionButton", "RangeChip" })
            if (!app.Resources.Contains(key)) { app.Resources[key] = new Style(); _addedResources.Add(key); }
        if (!app.Resources.Contains("TitleBarButtonText"))
        { app.Resources["TitleBarButtonText"] = new DataTemplate(); _addedResources.Add("TitleBarButtonText"); }
        if (!app.Resources.Contains(typeof(Button)))
        { app.Resources[typeof(Button)] = new Style(typeof(Button)); _addedResources.Add(typeof(Button)); }
        _window = new MainWindow(true);
        _window.Loaded -= _window.OnLoaded;
        _window.Preferences = _window.Preferences with { PrimaryDrive = "C:", SecondaryDrive = "D:" };
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
        foreach (var key in _addedResources) Application.Current.Resources.Remove(key);
        _addedResources.Clear();
    }

    private MetricReadout Metric(string name) => name switch
    {
        "cpu" => _window.CpuMetric, "memory" => _window.MemoryMetric, "disk" => _window.DiskMetric,
        "secondary" => _window.SecondaryDiskMetric, "network" => _window.NetworkMetric, "gpu" => _window.GpuMetric,
        _ => throw new ArgumentOutOfRangeException(nameof(name))
    };

    private static MetricDetails Details(MetricReadout metric) => (MetricDetails)metric.ToolTip;
    private static string[] Values(MetricReadout metric) => Details(metric).Rows.Select(row => row.Value).ToArray();
    private static ResourceSample Filled => new()
    {
        CpuClockMhz = 3250, CpuKernelPercent = 12.5, ProcessCount = 1247, ThreadCount = 18903, HandleCount = 612880,
        UsedMemoryBytes = 3 * GiB, TotalMemoryBytes = 8 * GiB, AvailableMemoryBytes = 5 * GiB,
        CommittedBytes = 4 * GiB, CommitLimitBytes = 12 * GiB, CachedBytes = GiB, PagingReadsPerSecond = 2.4,
        StandbyBytes = 2 * GiB, ModifiedBytes = GiB / 2, FreeBytes = GiB / 4, CompressionStoreBytes = GiB / 8,
        PagedPoolBytes = GiB / 16, NonPagedPoolBytes = GiB / 32,
        DiskName = "C:", DiskActivePercent = 12.5, DiskReadBytesPerSecond = 1_260_000,
        DiskWriteBytesPerSecond = 2_500_000, DiskResponseSeconds = .0025, DiskQueueLength = 1.4,
        DiskFreeBytes = 3 * GiB, DiskTotalBytes = 10 * GiB,
        SecondaryDiskName = "D:", SecondaryDiskActivePercent = 25,
        SecondaryDiskReadBytesPerSecond = 5_000_000, SecondaryDiskWriteBytesPerSecond = 7_000_000,
        SecondaryDiskResponseSeconds = .004, SecondaryDiskQueueLength = 2.4,
        SecondaryDiskFreeBytes = 7 * GiB, SecondaryDiskTotalBytes = 20 * GiB,
        NetworkName = "fixture adapter", LinkBitsPerSecond = 1_000_000_000,
        ReceiveBytesPerSecond = 25_000_000, SendBytesPerSecond = 10_000_000,
        ReceivedBytesSinceStart = 2 * GiB, SentBytesSinceStart = GiB / 2,
        GpuName = "fixture GPU", GpuEngine = "3D", GpuMemoryUsedBytes = 2 * GiB,
        GpuMemoryTotalBytes = 6 * GiB, GpuSharedMemoryUsedBytes = GiB / 4
    };

    private static MonitorSample Sample(int second, ResourceSample resource, params ProcessUsage[] processes) =>
        new(Start.AddSeconds(second), 35, processes, 0)
        { Resources = resource, IntervalSeconds = 1, IntervalCpuTicks = 10_000_000 };

    private void Record(ResourceSample resource, int second = 0)
    {
        var sample = Sample(second, resource);
        _window.Session.Record(sample, sample);
    }

    private static string[] Expected(string metric) => metric switch
    {
        "cpu" => ["3.25 GHz", "22.5% / 12.5%", "1,247 · 18,903", "612,880", Dash],
        "memory" => ["3.0 GiB / 8.0 GiB", "5.0 GiB", "4.0 GiB / 12.0 GiB", "1.0 GiB", "2.4/s",
            "2.0 GiB", "512.0 MiB", "256.0 MiB", "128.0 MiB", "64.0 MiB / 32.0 MiB", Dash],
        "disk" => ["12.5%", "1.3 MB/s", "2.5 MB/s", "2.5 ms", "1.4", "3.0 GiB / 10.0 GiB"],
        "secondary" => ["25.0%", "5.0 MB/s", "7.0 MB/s", "4.0 ms", "2.4", "7.0 GiB / 20.0 GiB"],
        "network" => ["fixture adapter", "1 Gbps", "20.0%", "↓25 MB/s / ↑10 MB/s", "↓2.0 GiB / ↑512.0 MiB"],
        "gpu" => ["fixture GPU", "3D", "2.0 GiB / 6.0 GiB", "256.0 MiB", Dash],
        _ => throw new ArgumentOutOfRangeException(nameof(metric))
    };

    private static void AssertUptime(string value, long before, long after)
    {
        var match = Regex.Match(value, @"^(\d+) d (\d{2}):(\d{2})$");
        Assert.That(match.Success, Is.True, value);
        var days = long.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        var hours = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
        var minutes = int.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture);
        Assert.That(hours, Is.InRange(0, 23));
        Assert.That(minutes, Is.InRange(0, 59));
        Assert.That(days * 1440 + hours * 60 + minutes, Is.InRange(before / 60_000, after / 60_000));
    }

    private void AssertRows(string name, string[] expected)
    {
        var values = Values(Metric(name));
        if (name == "cpu") Assert.That(values.Take(5), Is.EqualTo(expected));
        else Assert.That(values, Is.EqualTo(expected));
    }

    [TestCase("cpu"), TestCase("memory"), TestCase("disk"), TestCase("secondary"), TestCase("network"), TestCase("gpu")]
    public void CompleteReadingsKeepTheirRowOrderUnitsAndAccessibleMetadata(string name)
    {
        Record(Filled);
        var metric = Metric(name);
        metric.Label = "fixture " + name;
        metric.ValueLabel.Foreground = Brushes.Tomato;
        var before = Environment.TickCount64;
        _window.OpenDetails(metric);
        var after = Environment.TickCount64;
        AssertRows(name, Expected(name));
        if (name == "cpu") AssertUptime(Values(metric)[5], before, after);
        var details = Details(metric);
        Assert.That(details.Title, Is.EqualTo("fixture " + name));
        Assert.That(details.Accent, Is.SameAs(Brushes.Tomato));
        Assert.That(details.Peak, Is.EqualTo("Peak · last 2 min\n" + Dash));
        Assert.That(AutomationProperties.GetHelpText(metric), Is.EqualTo(details.Definition + "\n" + details.Peak));
        Assert.That(AutomationProperties.GetHelpText(metric.Toggle), Is.EqualTo(AutomationProperties.GetHelpText(metric)));
    }

    [TestCase("cpu"), TestCase("memory"), TestCase("disk"), TestCase("secondary"), TestCase("network"), TestCase("gpu")]
    public void OpeningBeforeTheFirstSampleLeavesUnavailableRowsAndAnObservedUptime(string name)
    {
        var before = Environment.TickCount64;
        _window.OpenDetails(Metric(name));
        var after = Environment.TickCount64;
        var values = Values(Metric(name));
        Assert.That(name == "cpu" ? values.Take(5) : values, Is.All.EqualTo(Dash));
        if (name == "cpu") AssertUptime(values[5], before, after);
    }

    [TestCase("cpu"), TestCase("memory"), TestCase("disk"), TestCase("secondary"), TestCase("network"), TestCase("gpu")]
    public void ANewEmptyReadingClearsPreviouslyDisplayedResourceValues(string name)
    {
        Record(Filled);
        var metric = Metric(name);
        _window.OpenDetails(metric);
        AssertRows(name, Expected(name));
        Record(new ResourceSample(), 1);
        _window.OpenDetails(metric);
        var values = Values(metric);
        Assert.That(name == "cpu" ? values.Take(5) : values, Is.All.EqualTo(Dash));
    }

    private static IEnumerable<TestCaseData> ErrorCases()
    {
        foreach (var metric in new[] { "cpu", "memory", "disk", "secondary", "network", "gpu" })
            foreach (var error in new[] { SamplingError.ReadingsUnavailable, SamplingError.CpuUnavailable,
                         SamplingError.CpuUnsupported, SamplingError.ProcessesUnavailable })
                yield return new TestCaseData(metric, error);
    }

    [TestCaseSource(nameof(ErrorCases))]
    public void OnlyAReadingsFailureHidesOtherwiseAvailableResourceRows(string name, SamplingError error)
    {
        Record(Filled);
        _window.ShownSamplingError = error;
        _window.OpenDetails(Metric(name));
        if (error == SamplingError.ReadingsUnavailable)
        {
            var values = Values(Metric(name));
            Assert.That(name == "cpu" ? values.Take(5) : values, Is.All.EqualTo(Dash));
        }
        else AssertRows(name, Expected(name));
        _window.ShownSamplingError = null;
        _window.OpenDetails(Metric(name));
        AssertRows(name, Expected(name));
    }

    [TestCase(null, null, 35, null)]
    [TestCase(-1d, 12.5, 35, null)]
    [TestCase(0d, 12.5, 35, null)]
    [TestCase(.001, 40d, 35, "0.00 GHz")]
    [TestCase(3250d, null, 35, "3.25 GHz")]
    [TestCase(3250d, 12.5, null, "3.25 GHz")]
    public void CpuDetailsPreserveClockAvailabilityAndUserKernelClamping(double? clock, double? kernel,
        double? total, string? expectedClock)
    {
        var sample = Sample(0, Filled with { CpuClockMhz = clock, CpuKernelPercent = kernel }) with { TotalCpuPercent = total };
        _window.Session.Record(sample, sample);
        _window.OpenDetails(_window.CpuMetric);
        var values = Values(_window.CpuMetric);
        Assert.That(values[0], Is.EqualTo(expectedClock ?? Dash));
        Assert.That(values[1], Is.EqualTo(total is null || kernel is null ? Dash : kernel == 40 ? "0.0% / 40.0%" : "22.5% / 12.5%"));
    }

    [Test]
    public void MemoryDetailsKeepPartialPairsUnavailableAndZeroDistinctFromMissing()
    {
        Record(Filled with { UsedMemoryBytes = null, AvailableMemoryBytes = 0, CommitLimitBytes = null,
            CachedBytes = GiB - 1, StandbyBytes = GiB, ModifiedBytes = null, FreeBytes = 0,
            CompressionStoreBytes = null, NonPagedPoolBytes = null, PagingReadsPerSecond = null });
        _window.OpenDetails(_window.MemoryMetric);
        Assert.That(Values(_window.MemoryMetric), Is.EqualTo(new[] { Dash, "0.0 MiB", Dash, "1024.0 MiB", Dash,
            "1.0 GiB", Dash, "0.0 MiB", Dash, Dash, Dash }));
    }

    [TestCase(false), TestCase(true)]
    public void CpuCountsRequireBothMachineCountsWithoutHidingAnIndependentHandleCount(bool missingProcesses)
    {
        Record(missingProcesses ? Filled with { ProcessCount = null } : Filled with { ThreadCount = null });
        _window.OpenDetails(_window.CpuMetric);
        Assert.That(Values(_window.CpuMetric)[2], Is.EqualTo(Dash));
        Assert.That(Values(_window.CpuMetric)[3], Is.EqualTo("612,880"));
        Record(Filled with { HandleCount = null }, 1);
        _window.OpenDetails(_window.CpuMetric);
        Assert.That(Values(_window.CpuMetric)[2], Is.EqualTo("1,247 · 18,903"));
        Assert.That(Values(_window.CpuMetric)[3], Is.EqualTo(Dash));
    }

    [TestCase("disk", "C:", true), TestCase("disk", "c:", false), TestCase("disk", "Z:", false)]
    [TestCase("secondary", "D:", true), TestCase("secondary", "d:", false), TestCase("secondary", "Z:", false)]
    public void DiskDetailsRequireTheExactConfiguredDriveBeforeUsingItsCounters(string name, string drive, bool matches)
    {
        Record(name == "disk" ? Filled with { DiskName = drive } : Filled with { SecondaryDiskName = drive });
        _window.OpenDetails(Metric(name));
        if (matches) AssertRows(name, Expected(name));
        else Assert.That(Values(Metric(name)), Is.All.EqualTo(Dash));
    }

    [TestCase("disk"), TestCase("secondary")]
    public void DiskDetailsKeepEachPartialCounterIndependentAndRequireBothCapacityValues(string name)
    {
        var resource = name == "disk"
            ? Filled with { DiskActivePercent = null, DiskReadBytesPerSecond = 0, DiskResponseSeconds = null, DiskTotalBytes = null }
            : Filled with { SecondaryDiskActivePercent = null, SecondaryDiskReadBytesPerSecond = 0,
                SecondaryDiskResponseSeconds = null, SecondaryDiskTotalBytes = null };
        Record(resource);
        _window.OpenDetails(Metric(name));
        Assert.That(Values(Metric(name)), Is.EqualTo(new[] { Dash, "0.0 MB/s", name == "disk" ? "2.5 MB/s" : "7.0 MB/s",
            Dash, name == "disk" ? "1.4" : "2.4", Dash }));
    }

    [TestCase("disk"), TestCase("secondary")]
    public void DiskMatchingReadsTheCurrentPreferencesAfterMetadataNotifications(string name)
    {
        Record(Filled);
        var metric = Metric(name);
        metric.Label = "changed disk title";
        var notifications = 0;
        Details(metric).PropertyChanged += (_, args) =>
        {
            if (args.PropertyName != nameof(MetricDetails.Title)) return;
            notifications++;
            _window.Preferences = _window.Preferences with { PrimaryDrive = "Z:", SecondaryDrive = "Z:" };
        };
        _window.OpenDetails(metric);
        Assert.That(notifications, Is.EqualTo(1));
        Assert.That(Values(metric), Is.All.EqualTo(Dash));
    }

    [TestCase("name"), TestCase("link"), TestCase("receive"), TestCase("send"), TestCase("received"), TestCase("sent")]
    public void NetworkDetailsPreserveIndependentNamesLinksAndCompleteRateAndCounterPairs(string missing)
    {
        var resource = missing switch
        {
            "name" => Filled with { NetworkName = "" }, "link" => Filled with { LinkBitsPerSecond = null },
            "receive" => Filled with { ReceiveBytesPerSecond = null }, "send" => Filled with { SendBytesPerSecond = null },
            "received" => Filled with { ReceivedBytesSinceStart = null }, _ => Filled with { SentBytesSinceStart = null }
        };
        Record(resource);
        _window.OpenDetails(_window.NetworkMetric);
        var expected = Expected("network");
        if (missing == "name") expected[0] = Dash;
        if (missing == "link") expected[1] = Dash;
        if (missing is "link" or "receive" or "send") expected[2] = Dash;
        if (missing is "receive" or "send") expected[3] = Dash;
        if (missing is "received" or "sent") expected[4] = Dash;
        Assert.That(Values(_window.NetworkMetric), Is.EqualTo(expected));
    }

    [TestCase(0d, 0d, "0.0%"), TestCase(20_000_000d, 10_000_000d, "100.0%")]
    [TestCase(-1d, -2d, "0.0%")]
    public void NetworkUtilizationKeepsTheFullDuplexMaximumAndClampsTheResult(double receive, double send, string utilization)
    {
        Record(Filled with { LinkBitsPerSecond = 100_000_000, ReceiveBytesPerSecond = receive, SendBytesPerSecond = send });
        _window.OpenDetails(_window.NetworkMetric);
        Assert.That(Values(_window.NetworkMetric)[1], Is.EqualTo("100 Mbps"));
        Assert.That(Values(_window.NetworkMetric)[2], Is.EqualTo(utilization));
    }

    [Test]
    public void GpuDetailsLeaveEmptyNamesAndPartialMemoryUnavailable()
    {
        Record(Filled with { GpuName = "", GpuEngine = "", GpuMemoryTotalBytes = null, GpuSharedMemoryUsedBytes = 0 });
        _window.OpenDetails(_window.GpuMetric);
        Assert.That(Values(_window.GpuMetric), Is.EqualTo(new[] { Dash, Dash, Dash, "0.0 MiB", Dash }));
    }

    [TestCase("cpu", "cpu owner  80.0%"), TestCase("memory", "memory owner  3.0 GiB"), TestCase("gpu", "memory owner  30.0%")]
    public void BusiestRowsUseTheirOwnMetricAndTheFullProcessSample(string name, string expected)
    {
        var cpu = new ProcessUsage(987654, 1, "cpu owner", 80, GiB) { GpuPercent = 5 };
        var memory = new ProcessUsage(987658, 2, "memory owner", 20, 3 * GiB) { GpuPercent = 30 };
        var compacted = Sample(0, Filled, cpu);
        _window.Session.Record(compacted, compacted with { Processes = [cpu, memory] });
        _window.OpenDetails(Metric(name));
        Assert.That(Values(Metric(name))[name == "memory" ? 10 : 4], Is.EqualTo(expected));
    }

    [Test]
    public void HistoricalSelectionDoesNotReplaceTheLiveResourceOrBusiestProcessSource()
    {
        var old = Sample(0, Filled with { UsedMemoryBytes = GiB }, new ProcessUsage(987654, 1, "retained owner", 10, GiB));
        _window.Session.Record(old, old);
        var live = Sample(1, Filled, new ProcessUsage(987658, 2, "live owner", 20, 3 * GiB));
        _window.Session.Record(live, live);
        _window.SelectedSample = old;
        _window.OpenDetails(_window.MemoryMetric);
        Assert.That(Values(_window.MemoryMetric)[0], Is.EqualTo("3.0 GiB / 8.0 GiB"));
        Assert.That(Values(_window.MemoryMetric)[10], Is.EqualTo("live owner  3.0 GiB"));
    }

    [TestCase("cpu"), TestCase("memory"), TestCase("gpu")]
    public void RestoredHistoryWithoutALiveFullSampleDoesNotInventABusiestProcess(string name)
    {
        _window.Session.Record(Sample(0, Filled, new ProcessUsage(987654, 1, "retained only", 80, GiB) { GpuPercent = 30 }));
        _window.OpenDetails(Metric(name));
        Assert.That(Values(Metric(name))[name == "memory" ? 10 : 4], Is.EqualTo(Dash));
    }

    [Test]
    public void ClosingAnotherTooltipCannotCloseTheCurrentOneAndClosedRowsStopRefreshing()
    {
        Record(Filled);
        _window.OpenDetails(_window.MemoryMetric);
        _window.OpenDetails(_window.NetworkMetric);
        _window.CloseDetails(_window.MemoryMetric);
        Record(Filled with { NetworkName = "replacement adapter", UsedMemoryBytes = 4 * GiB }, 1);
        _window.RefreshVisiblePresentation();
        Assert.That(Values(_window.NetworkMetric)[0], Is.EqualTo("replacement adapter"));
        Assert.That(Values(_window.MemoryMetric)[0], Is.EqualTo("3.0 GiB / 8.0 GiB"));
        _window.CloseDetails(_window.NetworkMetric);
        Record(Filled with { NetworkName = "later adapter" }, 2);
        _window.RefreshVisiblePresentation();
        Assert.That(Values(_window.NetworkMetric)[0], Is.EqualTo("replacement adapter"));
    }

    [Test]
    public void IdenticalRefreshesReuseTheRowsWithoutRedundantValueNotifications()
    {
        Record(Filled);
        _window.OpenDetails(_window.MemoryMetric);
        var rows = Details(_window.MemoryMetric).Rows.ToArray();
        var changes = new List<string?>();
        foreach (var row in rows) row.PropertyChanged += (_, args) => changes.Add(args.PropertyName);
        _window.OpenDetails(_window.MemoryMetric);
        Assert.That(Details(_window.MemoryMetric).Rows, Is.EqualTo(rows));
        Assert.That(changes, Is.Empty);
    }

    [Test]
    public void ReentrantTitleChangesKeepTheCapturedResourcesButReadTheCurrentBusiestProcess()
    {
        Record(Filled);
        var metric = _window.MemoryMetric;
        metric.Label = "replacement title";
        var notifications = 0;
        Details(metric).PropertyChanged += (_, args) =>
        {
            if (args.PropertyName != nameof(MetricDetails.Title)) return;
            notifications++;
            var next = Sample(1, Filled with { UsedMemoryBytes = 7 * GiB },
                new ProcessUsage(987658, 2, "new owner", 20, 2 * GiB));
            _window.Session.Record(next, next);
            _window.ShownSamplingError = SamplingError.ReadingsUnavailable;
        };
        _window.OpenDetails(metric);
        Assert.That(notifications, Is.EqualTo(1));
        Assert.That(Values(metric)[0], Is.EqualTo("3.0 GiB / 8.0 GiB"));
        Assert.That(Values(metric)[10], Is.EqualTo("new owner  2.0 GiB"));
    }

    [Test]
    public void ClosingDuringARowNotificationFinishesTheCapturedRowsButSuppressesBusiestLookup()
    {
        var sample = Sample(0, Filled, new ProcessUsage(987654, 1, "owner", 20, GiB));
        _window.Session.Record(sample, sample);
        var metric = _window.MemoryMetric;
        Details(metric).Rows[0].PropertyChanged += (_, _) => _window.CloseDetails(metric);
        _window.OpenDetails(metric);
        Assert.That(Values(metric), Is.EqualTo(Expected("memory")));
        Record(Filled with { UsedMemoryBytes = 7 * GiB }, 1);
        _window.RefreshVisiblePresentation();
        Assert.That(Values(metric)[0], Is.EqualTo("3.0 GiB / 8.0 GiB"));
    }

    private static ResourceSample PeakReading => Filled with
    {
        UsedMemoryBytes = 6 * GiB, DiskActivePercent = 60,
        DiskBytesPerSecond = 3_000_000, DiskReadBytesPerSecond = 1_000_000, DiskWriteBytesPerSecond = 2_000_000,
        SecondaryDiskActivePercent = 50, SecondaryDiskReadBytesPerSecond = 3_000_000,
        SecondaryDiskWriteBytesPerSecond = 4_000_000, SecondaryDiskBytesPerSecond = 7_000_000, NetworkId = "chosen",
        ReceiveBytesPerSecond = 10_000_000, SendBytesPerSecond = 100_000_000, GpuPercent = 70
    };

    private static ResourceSample CurrentReading => PeakReading with
    {
        UsedMemoryBytes = GiB, DiskActivePercent = 10, DiskReadBytesPerSecond = 9_000_000,
        DiskWriteBytesPerSecond = 8_000_000, DiskBytesPerSecond = 17_000_000, SecondaryDiskActivePercent = 10,
        SecondaryDiskReadBytesPerSecond = 7_000_000, SecondaryDiskWriteBytesPerSecond = 6_000_000,
        SecondaryDiskBytesPerSecond = 13_000_000,
        ReceiveBytesPerSecond = 2_500_000, SendBytesPerSecond = 0, GpuPercent = 20,
        GpuMemoryUsedBytes = 3 * GiB
    };

    private MonitorSample PrepareGaugePeak(bool compact, ResourceSample? peakResource = null)
    {
        _window.CompactMode = compact;
        var peak = Sample(0, peakResource ?? PeakReading) with { TotalCpuPercent = 80 };
        var latest = Sample(11, CurrentReading) with { TotalCpuPercent = 20 };
        _window.Session.Record(peak, peak);
        _window.Session.Record(latest, latest);
        _window.RefreshVisiblePresentation();
        return peak;
    }

    private string ExpectedGaugePeak(string name, MonitorSample peak, int age)
    {
        var reading = name switch
        {
            "cpu" => Metric(name).Label + ": 80.0%",
            "memory" => Metric(name).Label + ": 75.0%\nRAM used at utilization peak: 6.0 GiB",
            "disk" => Metric(name).Label + ": 60.0%\nDisk throughput at utilization peak: 3.0 MB/s",
            "secondary" => Metric(name).Label + ": 50.0%\nDisk throughput at utilization peak: 7.0 MB/s",
            "network" => "Download: 10 MB/s\nNetwork scale 0–125 MB/s",
            "gpu" => Metric(name).Label + ": 70.0%\nGPU memory used at utilization peak: 2.0 GiB",
            _ => throw new ArgumentOutOfRangeException(nameof(name))
        };
        return "Peak · last 2 min\n" + reading + "\n" +
               Strings.Format(Strings.GaugePeakAtTime, peak.Timestamp.ToLocalTime(), age);
    }

    [Test]
    public void GaugePeakDetailsUsePeakNativeValuesAndTheActiveView(
        [Values("cpu", "memory", "disk", "secondary", "network", "gpu")] string name,
        [Values(false, true)] bool compact)
    {
        var peak = PrepareGaugePeak(compact);
        var metric = Metric(name);
        if (compact) Assert.That(metric.Gauge.Level, Is.Null, "Only the compact gauge has been updated.");
        _window.OpenDetails(metric);
        var expected = ExpectedGaugePeak(name, peak, 11);
        Assert.That(Details(metric).Peak, Is.EqualTo(expected));
        Assert.That(AutomationProperties.GetHelpText(metric), Is.EqualTo(Details(metric).Definition + "\n" + expected));
        Assert.That(AutomationProperties.GetHelpText(metric.Toggle), Is.EqualTo(AutomationProperties.GetHelpText(metric)));
    }

    private static ResourceSample WithGaugeReading(ResourceSample source, string name, double? value) => name switch
    {
        "cpu" => source,
        "memory" => source with { UsedMemoryBytes = value is { } used ? (ulong)used : null },
        "disk" => source with { DiskActivePercent = value },
        "secondary" => source with { SecondaryDiskActivePercent = value },
        "network" => source with { ReceiveBytesPerSecond = value },
        "gpu" => source with { GpuPercent = value },
        _ => throw new ArgumentOutOfRangeException(nameof(name))
    };

    [Test]
    public void GaugePeakDetailsRefreshBeforeGaugesThenReopenWithCurrentAvailability(
        [Values("cpu", "memory", "disk", "secondary", "network", "gpu")] string name,
        [Values(false, true)] bool compact)
    {
        var peak = PrepareGaugePeak(compact);
        var metric = Metric(name);
        _window.OpenDetails(metric);
        Assert.That(Details(metric).Peak, Is.EqualTo(ExpectedGaugePeak(name, peak, 11)));
        var missing = Sample(12, WithGaugeReading(CurrentReading, name, null)) with
            { TotalCpuPercent = name == "cpu" ? null : 20 };
        _window.Session.Record(missing, missing);
        _window.RefreshVisiblePresentation();
        Assert.That(Details(metric).Peak, Is.EqualTo(ExpectedGaugePeak(name, peak, 12)),
            "An already-open tooltip currently refreshes before the gauge becomes unavailable.");
        _window.OpenDetails(metric);
        Assert.That(Details(metric).Peak, Is.EqualTo("Peak · last 2 min\n" + Dash));

        var recovered = Sample(13, WithGaugeReading(CurrentReading, name, 0)) with
            { TotalCpuPercent = name == "cpu" ? 0 : 20 };
        _window.Session.Record(recovered, recovered);
        _window.RefreshVisiblePresentation();
        Assert.That(Details(metric).Peak, Is.EqualTo("Peak · last 2 min\n" + Dash),
            "The open tooltip still observes the preceding unavailable gauge during refresh.");
        _window.OpenDetails(metric);
        Assert.That(Details(metric).Peak, Is.EqualTo(ExpectedGaugePeak(name, peak, 13)));
        if (compact) Assert.That(metric.Gauge.Level, Is.Null);
    }

    [Test]
    public void GaugePeakDetailsOmitUnavailableNativeValuesAtThePeak(
        [Values("gpu", "disk", "secondary")] string name, [Values(false, true)] bool compact)
    {
        var resources = name switch
        {
            "gpu" => PeakReading with { GpuMemoryUsedBytes = null },
            "disk" => PeakReading with { DiskBytesPerSecond = null },
            "secondary" => PeakReading with { SecondaryDiskBytesPerSecond = null },
            _ => throw new ArgumentOutOfRangeException(nameof(name))
        };
        var peak = PrepareGaugePeak(compact, resources);
        _window.OpenDetails(Metric(name));
        var percent = name switch { "gpu" => "70.0%", "disk" => "60.0%", _ => "50.0%" };
        Assert.That(Details(Metric(name)).Peak, Is.EqualTo("Peak · last 2 min\n" + Metric(name).Label + ": " + percent + "\n" +
            Strings.Format(Strings.GaugePeakAtTime, peak.Timestamp.ToLocalTime(), 11)));
    }

    [Test]
    public void GaugePeakDetailsRequireANonGapPeak([Values(false, true)] bool compact)
    {
        _window.CompactMode = compact;
        var gap = Sample(0, PeakReading) with { TotalCpuPercent = 80, IsGap = true };
        _window.Session.Record(gap, gap);
        _window.RefreshVisiblePresentation();
        if (!compact) Assert.That(_window.CpuMetric.Gauge.Level, Is.Null, "A gauge requires both current and peak readings.");
        _window.OpenDetails(_window.CpuMetric);
        Assert.That(Details(_window.CpuMetric).Peak, Is.EqualTo("Peak · last 2 min\n" + Dash));
    }

    [TestCase(60)]
    [TestCase(120)]
    [TestCase(300)]
    public void GaugePeakDetailsIncludeBoundaryAndReopenAfterExpiration(int seconds)
    {
        _window.Preferences = _window.Preferences with { GaugePeakWindowSeconds = seconds };
        var excluded = Sample(0, PeakReading) with { TotalCpuPercent = 95 };
        var boundary = Sample(1, PeakReading) with { TotalCpuPercent = 80 };
        var latest = Sample(seconds + 1, CurrentReading) with { TotalCpuPercent = 20 };
        foreach (var sample in new[] { excluded, boundary, latest }) _window.Session.Record(sample, sample);
        _window.RefreshVisiblePresentation();
        _window.OpenDetails(_window.CpuMetric);
        var heading = "Peak · last " + seconds / 60 + " min\n";
        Assert.That(Details(_window.CpuMetric).Peak, Is.EqualTo(heading + _window.CpuMetric.Label + ": 80.0%\n" +
            Strings.Format(Strings.GaugePeakAtTime, boundary.Timestamp.ToLocalTime(), seconds)));

        var next = latest with { Timestamp = latest.Timestamp.AddTicks(1) };
        _window.Session.Record(next, next);
        _window.RefreshVisiblePresentation();
        Assert.That(Details(_window.CpuMetric).Peak, Is.EqualTo(heading + _window.CpuMetric.Label + ": 80.0%\n" +
            Strings.Format(Strings.GaugePeakAtTime, boundary.Timestamp.ToLocalTime(), seconds)),
            "The open tooltip observes the old peak before this refresh recomputes the gauge window.");
        _window.OpenDetails(_window.CpuMetric);
        Assert.That(Details(_window.CpuMetric).Peak, Is.EqualTo(heading + _window.CpuMetric.Label + ": 20.0%\n" +
            Strings.Format(Strings.GaugePeakAtTime, next.Timestamp.ToLocalTime(), 0)));
    }

    [Test]
    public void GaugePeakDetailsTruncateAgeAndPreferMonotonicTime([Values(false, true)] bool monotonic)
    {
        var peak = Sample(0, PeakReading) with
            { TotalCpuPercent = 80, MonotonicTimestamp = monotonic ? 100 * Stopwatch.Frequency : 0 };
        var latest = Sample(monotonic ? 60 : 2, CurrentReading) with
        {
            Timestamp = Start.AddSeconds(monotonic ? 60 : 2.75), TotalCpuPercent = 20,
            MonotonicTimestamp = monotonic ? (long)(102.75 * Stopwatch.Frequency) : 0
        };
        _window.Session.Record(peak, peak);
        _window.Session.Record(latest, latest);
        _window.RefreshVisiblePresentation();
        _window.OpenDetails(_window.CpuMetric);
        Assert.That(Details(_window.CpuMetric).Peak, Is.EqualTo(ExpectedGaugePeak("cpu", peak, 2)));
    }
}
