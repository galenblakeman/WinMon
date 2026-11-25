using System.Windows;
using System.Windows.Controls;
using NUnit.Framework;
using WinMon.Controls;
using WinMon.Monitoring;
using WinMon.Resources;

namespace WinMon.Tests;

[TestFixture, NonParallelizable, Apartment(ApartmentState.STA), Category("Integration"), SetCulture("en-US"), SetUICulture("en-US")]
public sealed class UsageMenuCharacterizationTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 24, 14, 0, 0, TimeSpan.Zero);
    private UsageView _view = null!;

    [SetUp]
    public void SetUp() => _view = new UsageView();

    [TearDown]
    public void TearDown() => _view.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));

    private static AppUsage App() => new("original-key", "original-process", @"C:\Apps\original.exe",
        "Original app", "Original publisher", TimeSpan.FromSeconds(90).Ticks, 0, 12.75, 3, 2, true, null);

    private static UsageFrame Frame(AppUsage app, bool archived = false) =>
        new([app], TimeSpan.FromSeconds(300).Ticks, 0, Start, Start.AddMinutes(5), true, false)
        { Archived = archived, Notice = "original coverage" };

    private static MenuItem[] Entries(ContextMenu menu) => menu.Items.Cast<MenuItem>().ToArray();
    private static void Click(MenuItem item) => item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));

    [TestCase(false, false, "Missing")]
    [TestCase(false, false, "Denied")]
    [TestCase(false, false, "Allowed")]
    [TestCase(false, true, "Missing")]
    [TestCase(false, true, "Denied")]
    [TestCase(false, true, "Allowed")]
    [TestCase(true, false, "Missing")]
    [TestCase(true, false, "Denied")]
    [TestCase(true, false, "Allowed")]
    [TestCase(true, true, "Missing")]
    [TestCase(true, true, "Denied")]
    [TestCase(true, true, "Allowed")]
    public void MenuEligibilityUsesTheAggregateIdentityPathAndOptionalNavigationDecision(bool other, bool path, string decision)
    {
        var app = App() with { Key = other ? UsageLedger.OtherAppsKey : "original-key", ImagePath = path ? App().ImagePath : "" };
        var queries = 0;
        if (decision != "Missing") _view.CanShowInProcesses = candidate =>
        {
            queries++;
            Assert.That(candidate, Is.SameAs(app));
            return decision == "Allowed";
        };
        AppUsage? navigated = null;
        _view.ShowInProcessesRequested += candidate => navigated = candidate;
        var menu = _view.CreateMenu(app, Frame(app));
        var entries = Entries(menu);
        var canShow = !other && decision == "Allowed";
        Assert.Multiple(() =>
        {
            Assert.That(entries.Select(item => item.Header), Is.EqualTo(new[]
                { Strings.UsageShowInProcesses, Strings.CopyDetails, Strings.OpenFileLocation }));
            Assert.That(entries.Select(item => item.IsEnabled), Is.EqualTo(new[] { canShow, true, path && !other }));
            Assert.That(queries, Is.EqualTo(!other && decision != "Missing" ? 1 : 0));
            Assert.That(entries[0].ToolTip, Is.EqualTo(!other && !canShow ? Strings.UsageNotInHistory : null));
            if (!other && !canShow) Assert.That(ToolTipService.GetShowOnDisabled(entries[0]), Is.True);
            Assert.That(menu.IsOpen, Is.False);
            Assert.That(entries.Select(item => ((System.Windows.Shapes.Path)item.Icon).Data),
                Is.EqualTo(new[] { ButtonIcon.Process, ButtonIcon.Copy, ButtonIcon.Folder }));
        });
        Click(entries[0]);
        Assert.That(navigated, canShow ? Is.SameAs(app) : Is.Null);
    }

    [Test]
    public void AnAllowedMenuKeepsItsOriginalAppAndDecisionAfterTheViewAndEligibilityChange()
    {
        var app = App();
        var queries = 0;
        _view.CanShowInProcesses = _ => { queries++; return true; };
        var entries = Entries(_view.CreateMenu(app, Frame(app)));
        _view.Update(new UsageFrame([], 1, 0, Start.AddDays(1), Start.AddDays(2), false));
        _view.CanShowInProcesses = _ => { queries++; return false; };
        var received = new List<AppUsage>();
        _view.ShowInProcessesRequested += received.Add;
        Click(entries[0]);
        Assert.Multiple(() =>
        {
            Assert.That(received, Has.Count.EqualTo(1));
            Assert.That(received[0], Is.SameAs(app));
            Assert.That(queries, Is.EqualTo(1));
        });
    }

    [Test]
    public void ARefusedMenuStaysDisabledWhenANewMenuWouldAllowNavigation()
    {
        var app = App();
        _view.CanShowInProcesses = _ => false;
        var refused = Entries(_view.CreateMenu(app, Frame(app)));
        _view.CanShowInProcesses = _ => true;
        var allowed = Entries(_view.CreateMenu(app, Frame(app)));
        var received = new List<AppUsage>();
        _view.ShowInProcessesRequested += received.Add;
        Click(refused[0]);
        Assert.That(received, Is.Empty);
        Click(allowed[0]);
        Assert.Multiple(() =>
        {
            Assert.That(refused[0].IsEnabled, Is.False);
            Assert.That(allowed[0].IsEnabled, Is.True);
            Assert.That(received, Has.Count.EqualTo(1));
            Assert.That(received[0], Is.SameAs(app));
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void CopyUsesTheOriginalAppAndPeriodIncludingItsArchivedRunningPolicyAfterARefresh(bool archived)
    {
        var app = App();
        var frame = Frame(app, archived);
        var entries = Entries(_view.CreateMenu(app, frame));
        _view.Update(new UsageFrame([], 1, 0, Start.AddDays(1), Start.AddDays(2), false));
        string? copied = null;
        _view.CopyRequested += text => copied = text;
        Click(entries[1]);
        var period = archived
            ? Strings.Format(Strings.UsageArchivePeriodNote, Start.Date, Start.AddMinutes(5).AddTicks(-1).Date) + " original coverage"
            : Strings.Format(Strings.UsagePeriodNote, Start.ToLocalTime().DateTime, Start.AddMinutes(5).ToLocalTime().DateTime) +
              " " + Strings.UsagePeriodPartial + " original coverage";
        var expected = string.Join(Environment.NewLine, new[]
        {
            "Original app", @"C:\Apps\original.exe", "Original publisher", Strings.ProcessKindApp,
            Strings.Format(Strings.UsageReportSince, Start.ToLocalTime().DateTime), period,
            $"{Strings.UsageCpuTime}: 0:01:30 ({Strings.Format(Strings.PercentOneDecimal, 30d)})",
            $"{Strings.IO}: 0 B", $"{Strings.UsageGpuTime}: 0:00:12",
            $"{Strings.ProcessesTab}: 3 ({(archived ? Strings.MissingReading : "2")})",
            $"{Strings.UsageLastActive}: {Strings.MissingReading}", ""
        });
        Assert.That(copied, Is.EqualTo(expected));
    }

    [Test]
    public void AggregateRowStillCopiesItsPublicLabelAndMissingReadingsWithoutQueryingNavigation()
    {
        var app = new AppUsage(UsageLedger.OtherAppsKey, "internal-aggregation-name", "", "", "", 0, 0, null, 0, 0, false, null);
        _view.CanShowInProcesses = _ => throw new InvalidOperationException("Aggregate navigation must not be queried");
        var entries = Entries(_view.CreateMenu(app, new UsageFrame([app], 0, 0, Start, Start, false)));
        string? copied = null;
        _view.CopyRequested += text => copied = text;
        Click(entries[1]);
        var expected = string.Join(Environment.NewLine, new[]
        {
            Strings.UsageOtherApps, Strings.ProcessKindBackground,
            Strings.Format(Strings.UsageReportSince, Start.ToLocalTime().DateTime),
            $"{Strings.UsageCpuTime}: 0:00:00 ({Strings.MissingReading})", $"{Strings.IO}: 0 B",
            $"{Strings.UsageGpuTime}: {Strings.MissingReading}", $"{Strings.ProcessesTab}: 0 (0)",
            $"{Strings.UsageLastActive}: {Strings.MissingReading}", ""
        });
        Assert.That(copied, Is.EqualTo(expected).And.Not.Contains("internal-aggregation-name"));
    }

    [Test]
    public void CallbacksUseTheSubscribersPresentAtClickTimeAndTolerateTheirAbsence()
    {
        var app = App();
        _view.CanShowInProcesses = _ => true;
        var entries = Entries(_view.CreateMenu(app, Frame(app)));
        Assert.DoesNotThrow(() => { Click(entries[0]); Click(entries[1]); });
        var shown = 0;
        var copied = 0;
        Action<AppUsage> show = candidate => { Assert.That(candidate, Is.SameAs(app)); shown++; };
        Action<string> copy = text => { Assert.That(text, Does.StartWith("Original app" + Environment.NewLine)); copied++; };
        _view.ShowInProcessesRequested += show;
        _view.CopyRequested += copy;
        Click(entries[0]);
        Click(entries[1]);
        _view.ShowInProcessesRequested -= show;
        _view.CopyRequested -= copy;
        Assert.DoesNotThrow(() => { Click(entries[0]); Click(entries[1]); });
        Assert.Multiple(() =>
        {
            Assert.That(shown, Is.EqualTo(1));
            Assert.That(copied, Is.EqualTo(1));
        });
    }

    [Test]
    public void EligibilityExceptionsPropagateWithoutReplacingTheViewsExistingMenu()
    {
        var before = _view.Rows.ContextMenu;
        var failure = new InvalidOperationException("owned eligibility failure");
        _view.CanShowInProcesses = _ => throw failure;
        var app = App();
        Assert.That(Assert.Throws<InvalidOperationException>(() => _view.CreateMenu(app, Frame(app))), Is.SameAs(failure));
        Assert.That(_view.Rows.ContextMenu, Is.SameAs(before));
    }

    [TestCase("Navigation")]
    [TestCase("Copy")]
    public void DestinationExceptionsPropagateWithoutInvokingTheOtherAction(string destination)
    {
        var app = App();
        _view.CanShowInProcesses = _ => true;
        var entries = Entries(_view.CreateMenu(app, Frame(app)));
        var failure = new InvalidOperationException("owned destination failure");
        var otherCalls = 0;
        _view.ShowInProcessesRequested += _ => { if (destination == "Navigation") throw failure; otherCalls++; };
        _view.CopyRequested += _ => { if (destination == "Copy") throw failure; otherCalls++; };
        Assert.That(Assert.Throws<InvalidOperationException>(() => Click(entries[destination == "Navigation" ? 0 : 1])), Is.SameAs(failure));
        Assert.That(otherCalls, Is.Zero);
    }
}
