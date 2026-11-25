using System.Windows;
using MonCore.Monitoring;
using NUnit.Framework;
using WinMon.Controls;
using WinMon.Resources;

namespace WinMon.Tests;

[TestFixture, NonParallelizable, Apartment(ApartmentState.STA), Category("Integration"), SetCulture("en-US"), SetUICulture("en-US")]
public sealed class UsageFrameControlsCharacterizationTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 24, 0, 0, 0, TimeSpan.Zero);
    private UsageView _view = null!;

    [SetUp]
    public void SetUp() => _view = new UsageView();

    [TearDown]
    public void TearDown() => _view.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));

    private static AppUsage App(string key = "sample") =>
        new(key, key, "", "", "", 1, 0, null, 1, 0, true, null);

    private static UsageFrame Frame(bool available = true, bool capturing = false, bool archived = false) =>
        new([App()], TimeSpan.FromSeconds(120).Ticks, TimeSpan.FromSeconds(30).Ticks,
            Start, Start.AddDays(1), capturing, false, available) { Archived = archived, Notice = "frame coverage" };

    private static string Period(bool available, bool archived)
    {
        if (!available) return Strings.UsagePeriodUnavailable + " frame coverage";
        return archived
            ? Strings.Format(Strings.UsageArchivePeriodNote, Start.Date, Start.Date) + " frame coverage"
            : Strings.Format(Strings.UsagePeriodNote, Start.ToLocalTime().DateTime, Start.AddDays(1).ToLocalTime().DateTime) +
              " " + Strings.UsagePeriodPartial + " frame coverage";
    }

    [Test]
    public void BeforeTheFirstFrameSummaryReadingsAreMissingAndTheNoticeIsCollapsed()
    {
        Assert.Multiple(() =>
        {
            Assert.That(_view.Rows.Items, Is.Empty);
            Assert.That(_view.NotAttributedCpu.Text, Is.EqualTo(Strings.MissingReading));
            Assert.That(_view.NotAttributedShare.Text, Is.EqualTo(Strings.MissingReading));
            Assert.That(_view.CopyUsage.IsEnabled, Is.False);
            Assert.That(_view.ArchiveScope.IsEnabled, Is.True);
            Assert.That(_view.ArchiveDateHost.IsEnabled, Is.True);
            Assert.That(_view.ResetUsage.IsEnabled, Is.False);
            Assert.That(_view.Notice.Text, Is.Empty);
            Assert.That(_view.Notice.Visibility, Is.EqualTo(Visibility.Collapsed));
        });
    }

    [TestCase(false, false, false)]
    [TestCase(false, false, true)]
    [TestCase(false, true, false)]
    [TestCase(false, true, true)]
    [TestCase(true, false, false)]
    [TestCase(true, false, true)]
    [TestCase(true, true, false)]
    [TestCase(true, true, true)]
    public void FrameAvailabilityCaptureAndArchiveStateKeepTheirExistingSummaryAndActionRules(bool available, bool capturing, bool archived)
    {
        _view.Update(Frame(available, capturing, archived));
        Assert.Multiple(() =>
        {
            Assert.That(_view.Rows.Items, Has.Count.EqualTo(1));
            Assert.That(_view.NotAttributedCpu.Text, Is.EqualTo(available ? "0:00:30" : Strings.MissingReading));
            Assert.That(_view.NotAttributedShare.Text, Is.EqualTo(available ? Strings.Format(Strings.PercentOneDecimal, 25d) : Strings.MissingReading));
            Assert.That(_view.CopyUsage.IsEnabled, Is.EqualTo(available));
            Assert.That(_view.ArchiveScope.IsEnabled, Is.EqualTo(!capturing || archived));
            Assert.That(_view.ArchiveDateHost.IsEnabled, Is.EqualTo(!capturing || archived));
            Assert.That(_view.ResetUsage.IsEnabled, Is.EqualTo(available && !capturing));
            Assert.That(_view.Notice.Text, Is.EqualTo(capturing ? Period(available, archived) : ""));
            Assert.That(_view.Notice.Visibility, Is.EqualTo(capturing ? Visibility.Visible : Visibility.Collapsed));
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void FilteringToNoRowsChangesOnlyTheNonCapturingNoticeAndKeepsAvailableTotalsCopyable(bool capturing)
    {
        _view.Update(Frame(capturing: capturing));
        var selected = _view.Rows.Items[0];
        _view.Rows.SelectedItem = selected;
        _view.Search.Text = "no matching application";
        Assert.Multiple(() =>
        {
            Assert.That(_view.Rows.Items, Is.Empty);
            Assert.That(_view.Rows.SelectedItem, Is.Null);
            Assert.That(_view.CopyDetails.IsEnabled, Is.False);
            Assert.That(_view.CopyUsage.IsEnabled, Is.True);
            Assert.That(_view.NotAttributedCpu.Text, Is.EqualTo("0:00:30"));
            Assert.That(_view.NotAttributedShare.Text, Is.EqualTo(Strings.Format(Strings.PercentOneDecimal, 25d)));
            Assert.That(_view.Notice.Text, Is.EqualTo(capturing ? Period(true, false) : Strings.UsageNoAppsMatch));
            Assert.That(_view.Notice.Visibility, Is.EqualTo(Visibility.Visible));
        });
        _view.Search.Clear();
        Assert.Multiple(() =>
        {
            Assert.That(_view.Rows.SelectedItem, Is.SameAs(selected));
            Assert.That(_view.Notice.Text, Is.EqualTo(capturing ? Period(true, false) : ""));
            Assert.That(_view.Notice.Visibility, Is.EqualTo(capturing ? Visibility.Visible : Visibility.Collapsed));
        });
    }

    [Test]
    public void AFullLedgerResetClearsVisibleRowsAndSelectedActionsBeforeShowingReplacementData()
    {
        _view.Load(new UsageListSettings(0, UsageSort.CpuTime, false));
        _view.Update(Frame() with { Apps = Enumerable.Range(0, 100).Select(i => App("old-" + i)).ToArray() });
        Assert.That(_view.Rows.Items, Has.Count.EqualTo(100));
        _view.Rows.SelectedItem = _view.Rows.Items[50];
        _view.Update(Frame() with { Apps = [], MachineBusyTicks = 0, NotAttributedTicks = 0 });
        Assert.Multiple(() =>
        {
            Assert.That(_view.Rows.Items, Is.Empty);
            Assert.That(_view.Rows.SelectedItem, Is.Null);
            Assert.That(_view.CopyDetails.IsEnabled, Is.False);
            Assert.That(_view.Properties.IsEnabled, Is.False);
            Assert.That(_view.CopyUsage.IsEnabled, Is.True);
            Assert.That(_view.NotAttributedCpu.Text, Is.EqualTo("0:00:00"));
            Assert.That(_view.NotAttributedShare.Text, Is.EqualTo(Strings.MissingReading));
            Assert.That(_view.Notice.Text, Is.EqualTo(Strings.UsageNoAppsMatch));
            Assert.That(_view.Notice.Visibility, Is.EqualTo(Visibility.Visible));
        });
        _view.Update(Frame() with { Apps = [App("replacement")] });
        Assert.Multiple(() =>
        {
            Assert.That(_view.Rows.Items.Cast<UsageDisplayRow>().Select(row => row.Key), Is.EqualTo(new[] { "replacement" }));
            Assert.That(_view.Rows.SelectedItem, Is.Null);
            Assert.That(_view.CopyDetails.IsEnabled, Is.False);
            Assert.That(_view.NotAttributedCpu.Text, Is.EqualTo("0:00:30"));
            Assert.That(_view.Notice.Text, Is.Empty);
            Assert.That(_view.Notice.Visibility, Is.EqualTo(Visibility.Collapsed));
        });
    }

    [TestCase(0, 0, "0:00:00", null)]
    [TestCase(30, 0, "0:00:30", null)]
    [TestCase(0, 120, "0:00:00", 0d)]
    public void ZeroBusyTimeHasAMissingShareWhileAZeroNumeratorCanStillHaveAValidShare(
        int unattributedSeconds, int busySeconds, string cpu, double? share)
    {
        _view.Update(Frame() with
        {
            MachineBusyTicks = TimeSpan.FromSeconds(busySeconds).Ticks,
            NotAttributedTicks = TimeSpan.FromSeconds(unattributedSeconds).Ticks
        });
        Assert.Multiple(() =>
        {
            Assert.That(_view.NotAttributedCpu.Text, Is.EqualTo(cpu));
            Assert.That(_view.NotAttributedShare.Text, Is.EqualTo(share.HasValue ? Strings.Format(Strings.PercentOneDecimal, share.Value) : Strings.MissingReading));
            Assert.That(_view.CopyUsage.IsEnabled, Is.True);
        });
    }

    [Test]
    public void ReentrantFrameUpdatePreservesTheExistingPropertyNotificationAndReadOrder()
    {
        var changes = new List<bool>();
        var updated = false;
        DependencyPropertyChangedEventHandler observe = (_, e) =>
        {
            changes.Add((bool)e.NewValue);
            if (updated) return;
            updated = true;
            _view.Update(Frame(false, true) with { Apps = [] });
        };
        _view.CopyUsage.IsEnabledChanged += observe;
        try { _view.Update(Frame()); }
        finally { _view.CopyUsage.IsEnabledChanged -= observe; }
        Assert.Multiple(() =>
        {
            Assert.That(changes, Is.EqualTo(new[] { true, false }));
            Assert.That(_view.Rows.Items, Is.Empty);
            Assert.That(_view.NotAttributedCpu.Text, Is.EqualTo(Strings.MissingReading));
            Assert.That(_view.NotAttributedShare.Text, Is.EqualTo(Strings.MissingReading));
            Assert.That(_view.CopyUsage.IsEnabled, Is.False);
            Assert.That(_view.ArchiveScope.IsEnabled, Is.True);
            Assert.That(_view.ArchiveDateHost.IsEnabled, Is.True);
            Assert.That(_view.ResetUsage.IsEnabled, Is.True);
            Assert.That(_view.Notice.Text, Is.EqualTo(Strings.UsageNoAppsMatch));
            Assert.That(_view.Notice.Visibility, Is.EqualTo(Visibility.Visible));
        });
    }
}
