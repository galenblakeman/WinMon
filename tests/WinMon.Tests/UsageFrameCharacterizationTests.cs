using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using MonCore.Monitoring;
using NUnit.Framework;
using WinMon.Controls;
using WinMon.Preferences;
using WinMon.Resources;

namespace WinMon.Tests;

[TestFixture, NonParallelizable, Apartment(ApartmentState.STA), SetCulture("en-US"), SetUICulture("en-US")]
public sealed class UsageFrameCharacterizationTests
{
    private static readonly DateTimeOffset Start = new(2025, 2, 11, 12, 0, 0, TimeSpan.Zero);
    private static readonly ProcessUsage Process = new(8123, 100, "example.exe", 20, IoBytesPerSecond: 300)
        { ImagePath = @"C:\Apps\example.exe", GpuPercent = 25 };
    private readonly List<object> _addedResources = [];
    private MainWindow _window = null!;
    private string _directory = null!;
    private PreferenceStore _store = null!;

    [SetUp]
    public void SetUp()
    {
        _directory = Path.Combine(Path.GetTempPath(), "WinMon.UsageFrames-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        _store = new PreferenceStore(Path.Combine(_directory, "settings.json"));
        var app = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        foreach (var key in new[] { "AlwaysOnTopButton", "TitleActionButton", "ActionBarCollapseButton", "ChartModeSegment",
                     "ActionBarButton", "ActionBarLastButton", "ActivityTab", "CaptionButton", "RangeChip" })
            if (!app.Resources.Contains(key)) { app.Resources[key] = new Style(); _addedResources.Add(key); }
        if (!app.Resources.Contains("TitleBarButtonText"))
        { app.Resources["TitleBarButtonText"] = new DataTemplate(); _addedResources.Add("TitleBarButtonText"); }
        if (!app.Resources.Contains(typeof(Button)))
        { app.Resources[typeof(Button)] = new Style(typeof(Button)); _addedResources.Add(typeof(Button)); }
        _window = new MainWindow(false, preferenceStore: _store);
        _window.Loaded -= _window.OnLoaded;
        _window.Session.Usage.Reset(Start);
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
        if (_directory is not null && Directory.Exists(_directory))
        {
            Assert.That(Path.GetDirectoryName(Path.GetFullPath(_directory)),
                Is.EqualTo(Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar)));
            Directory.Delete(_directory, true);
        }
    }

    private static MonitorSample Sample(int second, ProcessUsage? process = null) =>
        new(Start.AddSeconds(second), 50, [process ?? Process], 0)
            { IntervalSeconds = 1, IntervalCpuTicks = 10_000_000 };

    private void ArchiveScope(int index, DateTime date)
    {
        _window.UsageView.ArchiveScope.SelectedIndex = index;
        var picker = (DatePicker)_window.UsageView.ArchiveDateHost.ContentTemplate.LoadContent();
        picker.SelectedDate = date;
        Assert.That(_window.UsageView.ArchiveDate, Is.EqualTo(date), "Drive the existing date-picker event, not private state.");
    }

    private static void Flags(UsageFrame frame, bool capturing, bool complete, bool available, bool archived)
    {
        Assert.Multiple(() =>
        {
            Assert.That(frame.Capturing, Is.EqualTo(capturing));
            Assert.That(frame.Complete, Is.EqualTo(complete));
            Assert.That(frame.Available, Is.EqualTo(available));
            Assert.That(frame.Archived, Is.EqualTo(archived));
        });
    }

    [Test]
    public void EmptyLiveSessionKeepsItsStartAndDoesNotInventALatestObservation()
    {
        var frame = _window.CurrentUsageFrame();
        Flags(frame, false, true, true, false);
        Assert.That(frame.Apps, Is.Empty);
        Assert.That(frame.MachineBusyTicks, Is.Zero);
        Assert.That(frame.NotAttributedTicks, Is.Zero);
        Assert.That(frame.Since, Is.EqualTo(Start));
        Assert.That(frame.Latest, Is.Null);
        Assert.That(frame.Notice, Is.Null);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void LiveFramesKeepLedgerTotalsAndTheLatestRecordedTimestamp(bool full)
    {
        var sample = Sample(1);
        _window.Session.Record(sample, full ? sample : null);
        var frame = _window.CurrentUsageFrame();
        Flags(frame, false, true, true, false);
        Assert.That(frame.Latest, Is.EqualTo(sample.Timestamp));
        Assert.That(frame.Since, Is.EqualTo(Start));
        Assert.That(frame.MachineBusyTicks, Is.EqualTo(full ? 5_000_000 : 0));
        Assert.That(frame.NotAttributedTicks, Is.EqualTo(full ? 3_000_000 : 0));
        Assert.That(frame.Apps, Has.Count.EqualTo(full ? 1 : 0));
        if (full)
        {
            var app = frame.Apps.Single();
            Assert.That(app.Key, Is.EqualTo(Process.ImagePath));
            Assert.That(app.CpuTicks, Is.EqualTo(2_000_000));
            Assert.That(app.IoBytes, Is.EqualTo(300));
            Assert.That(app.GpuSeconds, Is.EqualTo(.25));
            Assert.That(app.Running, Is.EqualTo(1));
        }
        Assert.That(frame.Notice, Is.Null);
    }

    [TestCase(1, false)]
    [TestCase(120, true)]
    public void HistoricalFramesEndAtTheSelectedSampleAndReportRetainedCompleteness(int count, bool complete)
    {
        MonitorSample selected = null!;
        for (var second = 1; second <= count; second++)
        { selected = Sample(second); _window.Session.Record(selected, selected); }
        var later = Sample(count + 1, Process with { ImagePath = @"C:\Later\other.exe" });
        _window.Session.Record(later, later);
        _window.SelectedSample = selected;
        _window.Preferences = _window.Preferences with { ChartRangeSeconds = 120 };
        var frame = _window.CurrentUsageFrame();
        Flags(frame, true, complete, true, false);
        Assert.That(frame.Since, Is.EqualTo(Start));
        Assert.That(frame.Latest, Is.EqualTo(selected.Timestamp));
        Assert.That(frame.MachineBusyTicks, Is.EqualTo(count * 5_000_000L));
        Assert.That(frame.NotAttributedTicks, Is.EqualTo(count * 3_000_000L));
        Assert.That(frame.Apps, Has.Count.EqualTo(1));
        Assert.That(frame.Apps.Single().Key, Is.EqualTo(Process.ImagePath));
        Assert.That(frame.Apps.Single().CpuTicks, Is.EqualTo(count * 2_000_000L));
        Assert.That(frame.Notice, Is.Null);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void MissingHistoricalUsageDoesNotSubstituteLiveTotals(bool retained)
    {
        var selected = Sample(1);
        if (retained) _window.Session.Record(selected);
        var latest = Sample(2);
        _window.Session.Record(latest, latest);
        _window.SelectedSample = selected;
        var frame = _window.CurrentUsageFrame();
        Flags(frame, true, false, false, false);
        Assert.That(frame.Apps, Is.Empty);
        Assert.That(frame.MachineBusyTicks, Is.Zero);
        Assert.That(frame.NotAttributedTicks, Is.Zero);
        Assert.That(frame.Since, Is.EqualTo(selected.Timestamp));
        Assert.That(frame.Latest, Is.EqualTo(selected.Timestamp));
    }

    [TestCase(1, 1)]
    [TestCase(2, 7)]
    public void ArchivedFramesUseTheSelectedInclusiveCalendarPeriod(int scope, int days)
    {
        var archive = new UsageArchive(TimeZoneInfo.Utc);
        for (var day = -7; day <= 1; day++) archive.Add(Sample(1) with { Timestamp = Start.AddDays(day) });
        _window.DailyUsageArchive = archive;
        ArchiveScope(scope, Start.Date);
        var frame = _window.CurrentUsageFrame();
        Flags(frame, true, false, true, true);
        Assert.That(frame.Since, Is.EqualTo(new DateTimeOffset(Start.Date.AddDays(1 - days), TimeSpan.Zero)));
        Assert.That(frame.Latest, Is.EqualTo(new DateTimeOffset(Start.Date.AddDays(1), TimeSpan.Zero)));
        Assert.That(frame.MachineBusyTicks, Is.EqualTo(days * 5_000_000L));
        Assert.That(frame.NotAttributedTicks, Is.EqualTo(days * 3_000_000L));
        var app = frame.Apps.Single();
        Assert.That(app.CpuTicks, Is.EqualTo(days * 2_000_000L));
        Assert.That(app.IoBytes, Is.EqualTo(days * 300));
        Assert.That(app.GpuSeconds, Is.EqualTo(days * .25));
        Assert.That(app.Running, Is.Zero);
        Assert.That(frame.Notice, Is.EqualTo(Strings.Format(Strings.UsageArchiveObserved, days) + " (UTC)"));
    }

    [Test]
    public void AnUnavailableArchiveKeepsZeroTotalsAndItsExistingEmptyZoneNotice()
    {
        ArchiveScope(1, Start.Date);
        var frame = _window.CurrentUsageFrame();
        Flags(frame, true, false, false, true);
        Assert.That(frame.Apps, Is.Empty);
        Assert.That(frame.MachineBusyTicks, Is.Zero);
        Assert.That(frame.NotAttributedTicks, Is.Zero);
        Assert.That(frame.Latest, Is.Null);
        Assert.That(frame.Notice, Is.EqualTo(Strings.Format(Strings.UsageArchiveObserved, 0) + " ()"));
    }

    [Test]
    public void ASelectedSampleTakesPriorityOverAnArchivedCalendarScope()
    {
        var archive = new UsageArchive(TimeZoneInfo.Utc);
        archive.Add(Sample(1) with { Timestamp = Start.AddDays(-1) });
        _window.DailyUsageArchive = archive;
        ArchiveScope(2, Start.Date);
        var sample = Sample(1);
        _window.Session.Record(sample, sample);
        _window.SelectedSample = sample;
        var frame = _window.CurrentUsageFrame();
        Flags(frame, true, false, true, false);
        Assert.That(frame.MachineBusyTicks, Is.EqualTo(5_000_000));
        Assert.That(frame.Since, Is.EqualTo(Start));
        Assert.That(frame.Latest, Is.EqualTo(sample.Timestamp));
        Assert.That(frame.Notice, Is.Null);
    }

    [TestCase(1, 1)]
    [TestCase(2, 7)]
    public void AnAvailableEmptyArchiveKeepsCalendarBoundsAndAvailability(int scope, int days)
    {
        _window.DailyUsageArchive = new UsageArchive(TimeZoneInfo.Utc);
        ArchiveScope(scope, Start.Date);
        var frame = _window.CurrentUsageFrame();
        Flags(frame, true, false, true, true);
        Assert.That(frame.Apps, Is.Empty);
        Assert.That(frame.MachineBusyTicks, Is.Zero);
        Assert.That(frame.NotAttributedTicks, Is.Zero);
        Assert.That(frame.Since, Is.EqualTo(new DateTimeOffset(Start.Date.AddDays(1 - days), TimeSpan.Zero)));
        Assert.That(frame.Latest, Is.EqualTo(new DateTimeOffset(Start.Date.AddDays(1), TimeSpan.Zero)));
        Assert.That(frame.Notice, Is.EqualTo(Strings.Format(Strings.UsageArchiveObserved, 0) + " (UTC)"));
    }

    [Test]
    public void ArchivedBoundsAndNoticeUseTheArchivesOwnTimeZone()
    {
        var offset = TimeSpan.FromHours(2);
        var zone = TimeZoneInfo.CreateCustomTimeZone("UsageFrameFixtureEast", offset, "Fixture East", "Fixture East");
        var archive = new UsageArchive(zone);
        archive.Add(Sample(1));
        _window.DailyUsageArchive = archive;
        ArchiveScope(1, Start.Date);
        var frame = _window.CurrentUsageFrame();
        Flags(frame, true, false, true, true);
        Assert.That(frame.Since, Is.EqualTo(new DateTimeOffset(Start.Date, offset)));
        Assert.That(frame.Latest, Is.EqualTo(new DateTimeOffset(Start.Date.AddDays(1), offset)));
        Assert.That(frame.Notice, Is.EqualTo(Strings.Format(Strings.UsageArchiveObserved, 1) + " (UsageFrameFixtureEast)"));
    }

    [Test]
    public void ReturningFromArchiveScopeRestoresTheLiveLedgerWithoutMixingPeriods()
    {
        foreach (var second in new[] { 1, 2 }) { var sample = Sample(second); _window.Session.Record(sample, sample); }
        var archive = new UsageArchive(TimeZoneInfo.Utc);
        archive.Add(Sample(1, Process with { ImagePath = @"C:\Archive\archived.exe" }));
        _window.DailyUsageArchive = archive;
        ArchiveScope(1, Start.Date);
        Assert.That(_window.CurrentUsageFrame().Apps.Single().Key, Is.EqualTo(@"C:\Archive\archived.exe"));
        _window.UsageView.ArchiveScope.SelectedIndex = 0;
        var frame = _window.CurrentUsageFrame();
        Flags(frame, false, true, true, false);
        Assert.That(frame.Apps, Has.Count.EqualTo(1));
        Assert.That(frame.Apps.Single().Key, Is.EqualTo(Process.ImagePath));
        Assert.That(frame.MachineBusyTicks, Is.EqualTo(10_000_000));
        Assert.That(frame.NotAttributedTicks, Is.EqualTo(6_000_000));
        Assert.That(frame.Since, Is.EqualTo(Start));
        Assert.That(frame.Latest, Is.EqualTo(Start.AddSeconds(2)));
        Assert.That(frame.Notice, Is.Null);
    }

    [Test]
    public void AValueEqualSelectedSampleRetainsTheExistingHistoryMembershipSemantics()
    {
        var original = Sample(1);
        _window.Session.Record(original, original);
        _window.SelectedSample = original with { };
        Assert.That(_window.SelectedSample, Is.Not.SameAs(original));
        var frame = _window.CurrentUsageFrame();
        Flags(frame, true, false, true, false);
        Assert.That(frame.MachineBusyTicks, Is.EqualTo(5_000_000));
        Assert.That(frame.Latest, Is.EqualTo(original.Timestamp));
        Assert.That(frame.Apps.Single().Key, Is.EqualTo(Process.ImagePath));
    }

    [Test, Category("Integration")]
    public void AnArchiveLoadFailureKeepsTheErrorNoticeWithoutDiscardingAnAvailablePeriod() => Run(async () =>
    {
        await File.WriteAllTextAsync(_store.UsageArchivePath, "invalid archive");
        await _window.LoadUsageArchiveForFixtureAsync();
        var archive = new UsageArchive(TimeZoneInfo.Utc);
        archive.Add(Sample(1));
        _window.DailyUsageArchive = archive;
        ArchiveScope(1, Start.Date);
        var frame = _window.CurrentUsageFrame();
        Flags(frame, true, false, true, true);
        Assert.That(frame.Apps, Has.Count.EqualTo(1));
        Assert.That(frame.Notice, Is.EqualTo(Strings.UsageArchiveError));
        Assert.That(await File.ReadAllTextAsync(_store.UsageArchivePath), Is.EqualTo("invalid archive"));
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
