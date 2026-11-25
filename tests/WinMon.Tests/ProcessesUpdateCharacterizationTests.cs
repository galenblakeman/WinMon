using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using NUnit.Framework;
using WinMon.Controls;
using WinMon.Monitoring;
using WinMon.Preferences;
using WinMon.Resources;

namespace WinMon.Tests;

[TestFixture, NonParallelizable, Apartment(ApartmentState.STA), SetCulture("en-US"), SetUICulture("en-US")]
public sealed class ProcessesUpdateCharacterizationTests
{
    private static readonly DateTimeOffset Start = DateTimeOffset.UnixEpoch.AddDays(1);
    private readonly List<object> _addedResources = [];
    private ProcessesView _view = null!;

    [SetUp]
    public void SetUp()
    {
        var app = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        foreach (var key in new[] { "RangeChip", "ActionBarCollapseButton", "ActionBarButton", "ActionBarLastButton" })
            if (!app.Resources.Contains(key)) { app.Resources[key] = new Style(); _addedResources.Add(key); }
        _view = new ProcessesView();
        foreach (var (key, brush) in new[] { ("Surface", Brushes.White), ("Text", Brushes.Black),
                     ("Muted", Brushes.Gray), ("SectionHeaderSurface", Brushes.Gold), ("ProcessPinAccent", Brushes.Red) })
            _view.Resources[key] = brush;
        _view.ProcessActions.EnablePriorityActions(_ => false, _ => null, (_, _) => Assert.Fail("No priority operation."));
        _view.ProcessActions.EnableControlActions(_ => false, _ => false, _ => false, _ => false,
            _ => EfficiencyModeState.Unavailable);
    }

    [TearDown]
    public void TearDown()
    {
        _view?.Close();
        _view?.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
        foreach (var key in _addedResources) Application.Current.Resources.Remove(key);
        _addedResources.Clear();
    }

    private static ProcessUsage Process(int index, double cpu = 10) =>
        new(int.MaxValue - index, 100 + index, $"fixture-{index}", cpu, (ulong)(1_000_000 - index * 1000), 1_000_000)
        {
            PrivateBytes = (ulong)(2_000_000 - index * 1000), ThreadCount = 20 + index,
            HandleCount = 100 + index, GpuPercent = 5 + index, HasAppWindow = true,
            ImagePath = $@"C:\Fixtures\fixture-{index}.exe"
        };

    private static MonitorSample Sample(int second, params ProcessUsage[] processes) =>
        new(Start.AddSeconds(second), 90, processes, 0) { IntervalSeconds = 1, IntervalCpuTicks = TimeSpan.TicksPerSecond };

    private static ProcessListFrame Frame(MonitorSample sample, ProcessListSettings? settings = null) =>
        new([sample], sample, sample, null, sample, sample, false, null, true,
            settings ?? new(5, ProcessSort.Cpu, false, false, false));

    private T Control<T>(string name) where T : FrameworkElement => (T)_view.FindName(name);
    private static string Text(string key) => Strings.ResourceManager.GetString(key, CultureInfo.CurrentUICulture)!;
    private ProcessRow[] VisibleRows => _view.Rows.Where(row => row.Visibility == Visibility.Visible).ToArray();
    private ProcessRow[] VisiblePins => _view.PinnedRows.Where(row => row.Visibility == Visibility.Visible).ToArray();
    private static TextBlock Cell(ProcessRow row, int column) => row.Children.OfType<TextBlock>()
        .Single(text => Grid.GetColumn(text) == column);
    private static string RowName(ProcessRow row) => row.Children.OfType<Panel>().Single()
        .Children.OfType<StackPanel>().Single().Children.OfType<TextBlock>().First().Text;

    private static IEnumerable<TestCaseData> Headers()
    {
        foreach (var grouped in new[] { false, true })
        foreach (var (sort, history, header, label) in new[]
                 {
                     (ProcessSort.Cpu, grouped ? nameof(Strings.SummedCPU) : nameof(Strings.CPU), "CpuHeader", nameof(Strings.CPU)),
                     (ProcessSort.Gpu, grouped ? nameof(Strings.BusiestMemberGPU) : nameof(Strings.GPU), "GpuHeader", nameof(Strings.GPU)),
                     (ProcessSort.Memory, grouped ? nameof(Strings.SummedPrivateMemory) : nameof(Strings.MemoryLowercase), "MemoryHeader", nameof(Strings.Memory)),
                     (ProcessSort.Io, grouped ? nameof(Strings.SummedIO) : nameof(Strings.IO), "IoHeader", nameof(Strings.IO)),
                     (ProcessSort.Threads, nameof(Strings.Threads), "ThreadsHeader", nameof(Strings.Threads)),
                     (ProcessSort.Handles, nameof(Strings.Handles), "HandlesHeader", nameof(Strings.Handles))
                 })
            yield return new TestCaseData(sort, grouped, history, header, label)
                .SetName($"HistoryHeaderKeepsSortAndGroupingMeaning_{sort}_{grouped}");
    }

    [TestCaseSource(nameof(Headers))]
    public void HistoryHeaderKeepsSortAndGroupingMeaning(ProcessSort sort, bool grouped, string historyKey,
        string headerName, string labelKey)
    {
        var process = Process(1);
        var result = _view.Update(Frame(Sample(1, process), new(5, sort, grouped, false, false)));
        Assert.Multiple(() =>
        {
            Assert.That(result, Is.Null);
            Assert.That(Control<TextBlock>("ProcessHistoryHeader").ToolTip,
                Is.EqualTo(Strings.Format(Strings.HistoryOnOneScaleSharedByEveryRowHoverA, Text(historyKey))));
            Assert.That(Control<TextBlock>(headerName).Text, Is.EqualTo(Text(labelKey) + " ▾"));
            Assert.That(Control<TextBlock>(headerName).FontWeight, Is.EqualTo(FontWeights.SemiBold));
            Assert.That(VisibleRows.Single().Identity, Is.EqualTo(ProcessRow.IdentityOf(process, grouped)));
            Assert.That(Control<TextBlock>("SelectionLabel").Visibility, Is.EqualTo(Visibility.Collapsed));
        });
    }

    [Test]
    public void HistoricalFrameUsesCaptureSettingsWithoutPublishingLiveSettings()
    {
        var sample = Sample(1, Process(1));
        var live = Frame(sample, new(0, ProcessSort.Memory, false, false, true));
        _view.Update(live);
        var notifications = new List<ProcessListSettings>();
        _view.SettingsChanged += notifications.Add;
        try
        {
            var report = _view.Update(live with { Selected = sample })!;
            Assert.Multiple(() =>
            {
                Assert.That(report.Grouped, Is.True);
                Assert.That(report.TopCount, Is.EqualTo(10));
                Assert.That(_view.ProcessCountSelector.SelectedItem, Is.EqualTo(10));
                Assert.That(_view.GroupedChip.IsChecked, Is.True);
                Assert.That(_view.PinnedOnlyCheckBox.IsChecked, Is.False);
                Assert.That(new[] { _view.ProcessCountSelector.IsEnabled, _view.GroupedChip.IsEnabled,
                    _view.IndividualChip.IsEnabled, _view.PinnedOnlyCheckBox.IsEnabled }, Is.All.False);
                Assert.That(notifications, Is.Empty);
            });
            _view.Update(live);
            Assert.Multiple(() =>
            {
                Assert.That(_view.ProcessCountSelector.SelectedItem, Is.SameAs(TopCountChoices.All));
                Assert.That(_view.IndividualChip.IsChecked, Is.True);
                Assert.That(_view.PinnedOnlyCheckBox.IsChecked, Is.True);
                Assert.That(_view.ProcessCountSelector.IsEnabled, Is.True);
                Assert.That(notifications, Is.Empty);
            });
        }
        finally { _view.SettingsChanged -= notifications.Add; }
    }

    [Test]
    public void HistoricalSelectionKeepsAndRestoresTheLiveSelection()
    {
        var first = Process(1); var second = Process(2);
        var sample = Sample(1, first, second);
        var live = Frame(sample);
        _view.Update(live);
        _view.SelectProcess(first, false);
        var liveSelection = _view.Selection;
        _view.Update(live with { Selected = sample });
        Assert.That(_view.Selection, Is.Not.SameAs(liveSelection));
        _view.SelectProcess(second, true);
        Assert.That(_view.Retention!(first), Is.True, "The retained live selection remains protected.");
        _view.Update(live);
        Assert.Multiple(() =>
        {
            Assert.That(_view.Selection, Is.SameAs(liveSelection));
            Assert.That(_view.Selection.Contains(first, false), Is.True);
            Assert.That(_view.Selection.Contains(second, false), Is.False);
            Assert.That(_view.Retention!(first), Is.True);
            Assert.That(_view.Retention!(second), Is.False);
        });
    }

    [TestCase(true)]
    [TestCase(false)]
    public void SelectedHistoryStopsAtTheSameSampleInstance(bool retainedInstance)
    {
        var first = Sample(1, Process(1, 20));
        var future = Sample(2, Process(2, 80));
        var selected = retainedInstance ? first : first with { };
        var report = _view.Update(Frame(future) with { Samples = new[] { first, future }, Selected = selected, Visible = first })!;
        Assert.Multiple(() =>
        {
            Assert.That(selected, Is.EqualTo(first));
            Assert.That(report.Sample, Is.SameAs(first));
            Assert.That(report.Processes.Select(p => p.ImagePath), Is.EqualTo(retainedInstance ? new[] { Process(1).ImagePath } : []));
            Assert.That(Control<TextBlock>("SelectionLabel").Visibility, Is.EqualTo(Visibility.Visible));
            Assert.That(Control<TextBlock>("SelectionLabel").Text,
                Is.EqualTo(Strings.Format(Strings.ProcessesAtSAverage, first.Timestamp, 5, Strings.IdleGroupMembersMayBeOmitted)));
        });
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    public void LiveRankingUsesFullLatestOnlyForTheMatchingCompactedTail(int state)
    {
        var full = Sample(1, Process(1));
        var compacted = full with { Processes = [], CompactedProcesses = 1 };
        var tail = state == 1 ? compacted with { } : compacted;
        MonitorSample[] samples = [tail];
        _view.Update(Frame(compacted) with { Samples = samples, LatestFull = state == 2 ? null : full });
        Assert.Multiple(() =>
        {
            Assert.That(tail, Is.EqualTo(compacted));
            Assert.That(samples[0], Is.SameAs(tail));
            Assert.That(samples[0].Processes, Is.Empty);
            Assert.That(VisibleRows.Select(row => row.Identity), Is.EqualTo(state == 0
                ? new[] { ProcessRow.IdentityOf(Process(1), false) } : []));
        });
    }

    [Test]
    public void EmptyHistoryDoesNotAppendTheFullLatestSample()
    {
        var sample = Sample(1, Process(1));
        _view.Update(Frame(sample) with { Samples = Array.Empty<MonitorSample>() });
        Assert.That(VisibleRows, Is.Empty);
        Assert.That(_view.OtherRow.Visibility, Is.EqualTo(Visibility.Collapsed));
        Assert.That(Control<TextBlock>("EmptyLabel").Visibility, Is.EqualTo(Visibility.Visible));
        Assert.That(Control<TextBlock>("EmptyLabel").Text, Is.EqualTo(Strings.NoMeasurableActiveProcessesInThisWindow));
    }

    [Test]
    public void MissingVisibleSampleKeepsRankedRemainderWithoutCompactedCounts()
    {
        var sample = Sample(1, Enumerable.Range(0, 17).Select(i => Process(i, 17 - i)).ToArray()) with { CompactedProcesses = 7 };
        var result = _view.Update(Frame(sample, new(5, ProcessSort.Memory, false, false, false)) with { Visible = null });
        Assert.Multiple(() =>
        {
            Assert.That(result, Is.Null);
            Assert.That(VisibleRows, Has.Length.EqualTo(5));
            Assert.That(RowName(_view.OtherRow), Is.EqualTo(Strings.Format(Strings.OtherProcesses, 12)));
            Assert.That(Cell(_view.OtherRow, 2).Text, Is.EqualTo("78.0%"));
            Assert.That(Control<TextBlock>("CoverageLabel").Text, Is.Empty);
            Assert.That(Control<TextBlock>("EmptyLabel").Visibility, Is.EqualTo(Visibility.Collapsed));
        });
    }

    private static IEnumerable<TestCaseData> Searches()
    {
        foreach (var apps in new[] { false, true })
        foreach (var (label, query, match) in new[]
                 {
                     ("TrimmedName", "\t SLEEPER \t", true), ("App", "friendly-idle", true),
                     ("Product", "idle-product", true), ("Pid", "2147483646", true),
                     ("PathDoesNotMatch", @"C:\Fixtures\fixture-1.exe", false), ("Missing", "no-match", false)
                 })
            yield return new TestCaseData(query, apps, match && !apps)
                .SetName($"SearchTrimsForFilteringAndIncludesInactiveProcesses_{label}_{apps}");
    }

    [TestCaseSource(nameof(Searches))]
    public void SearchTrimsForFilteringAndIncludesInactiveProcesses(string query, bool appsOnly, bool match)
    {
        var idle = Process(1, 0) with { Name = "sleeper", AppName = "friendly-idle", ProductName = "idle-product", HasAppWindow = false };
        _view.ProcessSearchBox.Text = query;
        _view.Update(Frame(Sample(1, idle, Process(2)), new(5, ProcessSort.Cpu, false, appsOnly, false)));
        Assert.That(VisibleRows.Select(row => row.Identity), Is.EqualTo(match ? new[] { ProcessRow.IdentityOf(idle, false) } : []));
        Assert.That(_view.ProcessSearchBox.Text, Is.EqualTo(query), "Filtering must not rewrite the user's search text.");
    }

    [TestCase(0, 2)]
    [TestCase(5, 1)]
    public void AllRowsIncludesInactiveProcessesWithoutAQuery(int topCount, int expectedCount)
    {
        _view.Update(Frame(Sample(1, Process(1, 0), Process(2)), new(topCount, ProcessSort.Cpu, false, false, false)));
        Assert.That(VisibleRows, Has.Length.EqualTo(expectedCount));
    }

    [Test]
    public void HistoricalSnapshotKeepsTrimmedSearchAndAppsOnly()
    {
        _view.ProcessSearchBox.Text = "  fixture  ";
        var app = Process(1); var background = Process(2) with { HasAppWindow = false };
        var sample = Sample(1, app, background);
        var report = _view.Update(Frame(sample, new(5, ProcessSort.Cpu, false, true, false)) with { Selected = sample })!;
        Assert.Multiple(() =>
        {
            Assert.That(report.Filter, Is.EqualTo("fixture"));
            Assert.That(report.AppsOnly, Is.True);
            Assert.That(report.Processes.Select(p => p.ImagePath), Is.EqualTo(new[] { app.ImagePath }));
        });
    }

    [Test]
    public void PinnedOnlySeparatesPinnedRowsFromRankedAndRemainder([Values(false, true)] bool grouped,
        [Values(false, true)] bool pinnedOnly)
    {
        var pin = Process(1); var other = Process(2);
        _view.ToggleProcessPin(pin, grouped);
        _view.Update(Frame(Sample(1, pin, other), new(5, ProcessSort.Cpu, grouped, false, pinnedOnly)));
        Assert.Multiple(() =>
        {
            Assert.That(VisiblePins.Single().Identity, Is.EqualTo(ProcessRow.IdentityOf(pin, grouped)));
            Assert.That(VisibleRows.Select(row => row.Identity), Is.EqualTo(pinnedOnly ? [] : new[] { ProcessRow.IdentityOf(other, grouped) }));
            Assert.That(_view.OtherRow.Visibility, Is.EqualTo(Visibility.Collapsed));
        });
        _view.ProcessSearchBox.Text = "no-match";
        _view.Update(Frame(Sample(2, pin, other), new(5, ProcessSort.Cpu, grouped, false, pinnedOnly)));
        Assert.That(VisiblePins, Is.Empty);
        Assert.That(Control<TextBlock>("EmptyLabel").Text, Is.EqualTo(pinnedOnly
            ? Strings.NoPinnedProcessesMatch : Strings.NoRankedProcessesMatchYourSearch));
    }

    [TestCase("present", "")]
    [TestCase("exited", nameof(Strings.ProcessExited))]
    [TestCase("reused", nameof(Strings.ProcessExited))]
    [TestCase("gap", nameof(Strings.Unavailable))]
    [TestCase("unread", nameof(Strings.Unavailable))]
    [TestCase("baseline", nameof(Strings.Unavailable))]
    public void PinnedRowsKeepTheirLifetimeAndReadableState(string state, string stateKey)
    {
        var pin = Process(1);
        _view.ToggleProcessPin(pin, false);
        var sample = state switch
        {
            "present" => Sample(1, pin),
            "reused" => Sample(1, pin with { CreationTicks = 999 }),
            "gap" => Sample(1) with { IsGap = true },
            "unread" => Sample(1) with { UnavailableProcesses = 1 },
            "baseline" => Sample(1) with { TotalCpuPercent = null },
            _ => Sample(1)
        };
        _view.Update(Frame(sample));
        var row = VisiblePins.Single();
        Assert.That(row.Identity, Is.EqualTo(ProcessRow.IdentityOf(pin, false)));
        if (stateKey.Length > 0) Assert.That(AutomationProperties.GetName(row), Does.Contain(Text(stateKey)));
        if (state == "present") Assert.That(Cell(row, 2).Text, Is.EqualTo("10.0%"));
        else Assert.That(Cell(row, 2).Text, Is.EqualTo(Strings.MissingReading));
    }

    [TestCase("inside", 2, false, 10, 2, 3)]
    [TestCase("outside", 11, true, 11, 2, 3)]
    [TestCase("absent", 30, true, 11, 2, 3)]
    public void HistoricalTriggerAppearsFirstExactlyOnce(string label, int triggerIndex, bool extra, int count,
        int remainderCount, double remainderCpu)
    {
        var processes = Enumerable.Range(0, 12).Select(i => Process(i, 12 - i)).ToArray();
        var trigger = triggerIndex == 30 ? Process(30, 99) : processes[triggerIndex];
        var sample = Sample(1, processes);
        _view.ToggleProcessPin(processes[0], true);
        var report = _view.Update(Frame(sample) with { Selected = sample, Trigger = trigger })!;
        Assert.Multiple(() =>
        {
            Assert.That(report.Processes, Has.Count.EqualTo(count));
            Assert.That(report.Processes[0].ImagePath, Is.EqualTo(trigger.ImagePath), label);
            Assert.That(report.Processes.Count(p => p.ImagePath == trigger.ImagePath), Is.EqualTo(1));
            Assert.That(report.TriggerIncludedOutsideTop, Is.EqualTo(extra));
            Assert.That(report.Remainder!.Count, Is.EqualTo(remainderCount));
            Assert.That(report.Remainder.CpuPercent, Is.EqualTo(remainderCpu));
            Assert.That(report.ProcessStates, Is.EqualTo(Enumerable.Repeat("", count)));
            Assert.That(VisiblePins, Is.Empty, "Historical pins stay within the ranked capture rows.");
        });
        if (label == "outside")
            // Preserve the existing trigger/remainder double count; the separate defect note records the desired fix.
            Assert.That(report.Processes.Sum(p => p.CpuPercent) + report.Remainder!.CpuPercent, Is.EqualTo(79));
        if (triggerIndex == 30) Assert.That(report.Processes[0], Is.SameAs(trigger));
    }

    [Test]
    public void RankingChangesReuseRowsWithoutRebindingOtherIdentities()
    {
        var first = Process(1, 80); var second = Process(2, 10);
        _view.Update(Frame(Sample(1, first, second)));
        var rows = VisibleRows;
        _view.Update(Frame(Sample(2, first with { CpuPercent = 1 }, second with { CpuPercent = 90 })));
        Assert.Multiple(() =>
        {
            Assert.That(VisibleRows, Is.EqualTo(new[] { rows[1], rows[0] }));
            Assert.That(rows[0].Identity, Is.EqualTo(ProcessRow.IdentityOf(first, false)));
            Assert.That(rows[1].Identity, Is.EqualTo(ProcessRow.IdentityOf(second, false)));
        });
    }

    [TestCase(49)]
    [TestCase(50)]
    [TestCase(51)]
    public void NarrowingAllRowsKeepsAtMostFiftyReusableControls(int count)
    {
        var sample = Sample(1, Enumerable.Range(0, count).Select(i => Process(i, count - i)).ToArray());
        _view.Update(Frame(sample, new(0, ProcessSort.Cpu, false, false, false)));
        var first = VisibleRows[0];
        _view.Update(Frame(sample));
        Assert.Multiple(() =>
        {
            Assert.That(_view.Rows, Has.Count.EqualTo(Math.Min(count, 50)));
            Assert.That(VisibleRows, Has.Length.EqualTo(5));
            Assert.That(VisibleRows[0], Is.SameAs(first));
            Assert.That(_view.Rows.Skip(5).All(row => row.Visibility == Visibility.Collapsed), Is.True);
        });
    }

    [TestCase(ProcessSort.Memory, false, false, false, 19)]
    [TestCase(ProcessSort.Memory, true, false, false, 12)]
    [TestCase(ProcessSort.Cpu, false, false, false, 12)]
    [TestCase(ProcessSort.Memory, false, true, false, 12)]
    [TestCase(ProcessSort.Memory, false, false, true, 12)]
    public void RemainderCountsCompactedMemoryOnlyWithoutOtherFilters(ProcessSort sort, bool grouped,
        bool query, bool appsOnly, int expectedCount)
    {
        var sample = Sample(1, Enumerable.Range(0, 17).Select(i => Process(i, 17 - i)).ToArray()) with { CompactedProcesses = 7 };
        if (query) _view.ProcessSearchBox.Text = "fixture";
        _view.Update(Frame(sample, new(5, sort, grouped, appsOnly, false)));
        Assert.Multiple(() =>
        {
            Assert.That(_view.OtherRow.Visibility, Is.EqualTo(Visibility.Visible));
            Assert.That(RowName(_view.OtherRow), Is.EqualTo(Strings.Format(grouped ? Strings.OtherExecutables : Strings.OtherProcesses, expectedCount)));
            Assert.That(Cell(_view.OtherRow, 2).Text, Is.EqualTo("78.0%"));
            Assert.That(Cell(_view.OtherRow, 5).Text, Is.EqualTo("12.0 MB/s"));
        });
    }

    [TestCase(-1)]
    [TestCase(0)]
    [TestCase(1)]
    public void HistoricalRemainderKeepsMissingIoDistinctFromZero(int reading)
    {
        var sample = Sample(1, Enumerable.Range(0, 17).Select(i => Process(i, 17 - i) with
            { IoBytesPerSecond = reading < 0 ? null : reading * 1_000_000 }).ToArray());
        var report = _view.Update(Frame(sample, new(5, ProcessSort.Memory, false, false, false)) with { Selected = sample })!;
        Assert.Multiple(() =>
        {
            Assert.That(report.Remainder!.Count, Is.EqualTo(7));
            Assert.That(report.Remainder.CpuPercent, Is.EqualTo(28));
            Assert.That(report.Remainder.IoBytesPerSecond, Is.EqualTo(reading < 0 ? (double?)null : reading * 7_000_000));
            Assert.That(Cell(_view.OtherRow, 5).Text, Is.EqualTo(reading < 0 ? Strings.MissingReading : reading == 0 ? "0 B/s" : "7.0 MB/s"));
        });
    }

    private static IEnumerable<TestCaseData> EmptyCases()
    {
        foreach (var (state, expected) in new[]
                 {
                     ("expired", nameof(Strings.ReturnToLiveToInspectCurrentActivity)),
                     ("missing-latest", nameof(Strings.CollectingABaselineOrRecoveringAfterARecordingGap)),
                     ("missing-visible", nameof(Strings.CollectingABaselineOrRecoveringAfterARecordingGap)),
                     ("baseline", nameof(Strings.CollectingABaselineOrRecoveringAfterARecordingGap)),
                     ("baseline-apps-search", nameof(Strings.CollectingABaselineOrRecoveringAfterARecordingGap)),
                     ("apps", nameof(Strings.NoAppsMatchCurrentView)),
                     ("apps-search", nameof(Strings.NoAppsMatchCurrentView)),
                     ("search", nameof(Strings.NoRankedProcessesMatchYourSearch)),
                     ("pinned", nameof(Strings.NoPinnedProcessesMatch)),
                     ("pinned-apps-search", nameof(Strings.NoPinnedProcessesMatch))
                 })
            yield return new TestCaseData(state, ProcessSort.Cpu, expected).SetName($"EmptyFrameKeepsTheOriginalReasonPrecedence_{state}");
        foreach (var (sort, expected) in new[]
                 {
                     (ProcessSort.Cpu, nameof(Strings.NoMeasurableActiveProcessesInThisWindow)),
                     (ProcessSort.Gpu, nameof(Strings.NoMeasurableGPUActivityAmongAccessibleProcessesInThisWindow)),
                     (ProcessSort.Memory, nameof(Strings.NoMemoryReadingsAmongAccessibleProcesses)),
                     (ProcessSort.Io, nameof(Strings.NoMeasurableIOAmongAccessibleProcessesInThisWindow)),
                     (ProcessSort.Threads, nameof(Strings.NoProcessCountReadings)),
                     (ProcessSort.Handles, nameof(Strings.NoProcessCountReadings))
                 })
            yield return new TestCaseData("sort", sort, expected).SetName($"EmptyFrameKeepsTheOriginalReasonPrecedence_{sort}");
    }

    [TestCaseSource(nameof(EmptyCases))]
    public void EmptyFrameKeepsTheOriginalReasonPrecedence(string state, ProcessSort sort, string expectedKey)
    {
        var sample = Sample(1) with { TotalCpuPercent = state.StartsWith("baseline", StringComparison.Ordinal) ? null : 90 };
        if (state.Contains("search", StringComparison.Ordinal)) _view.ProcessSearchBox.Text = "missing";
        var frame = Frame(sample, new(5, sort, false, state.Contains("apps", StringComparison.Ordinal),
            state.Contains("pinned", StringComparison.Ordinal))) with
        {
            Selected = state == "expired" ? sample : null,
            Visible = state is "expired" or "missing-visible" ? null : sample,
            Latest = state == "missing-latest" ? null : sample
        };
        Assert.That(_view.Update(frame), Is.Null);
        Assert.That(Control<TextBlock>("EmptyLabel").Visibility, Is.EqualTo(Visibility.Visible));
        Assert.That(Control<TextBlock>("EmptyLabel").Text, Is.EqualTo(Text(expectedKey)));
    }

    private static IEnumerable<TestCaseData> CoverageCases()
    {
        foreach (var limited in new[] { false, true })
        {
            yield return new TestCaseData(90d, 10d, 0, 80d, limited).SetName($"CoverageKeepsUnreadCpuAndMemoryLimitSeparators_Total_{limited}");
            yield return new TestCaseData(90d, 10d, 2, 80d, limited).SetName($"CoverageKeepsUnreadCpuAndMemoryLimitSeparators_Unread_{limited}");
            yield return new TestCaseData(5d, 10d, 0, 0d, limited).SetName($"CoverageKeepsUnreadCpuAndMemoryLimitSeparators_Clamp_{limited}");
            yield return new TestCaseData(null, 10d, 2, null, limited).SetName($"CoverageKeepsUnreadCpuAndMemoryLimitSeparators_NoTotalUnread_{limited}");
            yield return new TestCaseData(null, 10d, 0, null, limited).SetName($"CoverageKeepsUnreadCpuAndMemoryLimitSeparators_NoTotal_{limited}");
        }
    }

    [TestCaseSource(nameof(CoverageCases))]
    public void CoverageKeepsUnreadCpuAndMemoryLimitSeparators(double? totalCpu, double measuredCpu,
        int unavailable, double? unattributed, bool limited)
    {
        var sample = Sample(1, Process(1, measuredCpu)) with { TotalCpuPercent = totalCpu, UnavailableProcesses = unavailable };
        _view.Update(Frame(sample) with { HistoryMemoryLimited = limited });
        var parts = new List<string>();
        if (unattributed is not null) parts.Add(Strings.Format(Strings.Unattributed, unattributed.Value));
        if (unavailable > 0) parts.Add(Strings.Format(Strings.ProcessesNeedAdministratorAccess, unavailable));
        if (limited) parts.Add(Strings.HistoryShortenedByMemoryLimit);
        var expected = string.Join(" · ", parts);
        Assert.That(Control<TextBlock>("CoverageLabel").Text, Is.EqualTo(expected));
        Assert.That(Control<TextBlock>("CoverageLabel").ToolTip, Is.EqualTo(expected + Strings.UnattributedCPUIsTotalCPUMinusAllMeasuredProcessesKernel));
    }

    [Test]
    public void ThrowingRetentionObserverKeepsTheOriginalPartialTransition()
    {
        var process = Process(1); var sample = Sample(1, process);
        var live = Frame(sample);
        _view.Update(live);
        _view.SelectProcess(process, false);
        var selection = _view.Selection;
        _view.Update(live with { Selected = sample });
        var expected = new InvalidOperationException("retention observer");
        void Throw() => throw expected;
        _view.RetentionChanged += Throw;
        try
        {
            Assert.That(Assert.Throws<InvalidOperationException>(() => _view.Update(live)), Is.SameAs(expected));
            Assert.Multiple(() =>
            {
                Assert.That(_view.Selection, Is.SameAs(selection));
                Assert.That(_view.IsHistorical, Is.True, "Frame assignment happens after the retention notification.");
                Assert.That(_view.ProcessCountSelector.IsEnabled, Is.False);
                Assert.That(_view.Retention!(process), Is.True);
            });
        }
        finally { _view.RetentionChanged -= Throw; }
    }

    [Test]
    public void NullFrameThrowsBeforeSelectionOrRowsChange()
    {
        _view.Update(Frame(Sample(1, Process(1))));
        var selection = _view.Selection;
        var row = VisibleRows.Single();
        var notifications = 0;
        void Observe() => notifications++;
        _view.RetentionChanged += Observe;
        try
        {
            Assert.Throws<NullReferenceException>(() => _view.Update(null!));
            Assert.Multiple(() =>
            {
                Assert.That(_view.Selection, Is.SameAs(selection));
                Assert.That(VisibleRows.Single(), Is.SameAs(row));
                Assert.That(_view.IsHistorical, Is.False);
                Assert.That(notifications, Is.Zero);
            });
        }
        finally { _view.RetentionChanged -= Observe; }
    }
}
