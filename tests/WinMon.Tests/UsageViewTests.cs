using System.Globalization;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using NUnit.Framework;
using WinMon.Controls;
using WinMon.Monitoring;
using WinMon.Resources;

namespace WinMon.Tests;

[TestFixture]
public sealed class UsageViewTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-24T14:00:00Z");

    [Test]
    public void Visible_RanksByTheChosenColumnThenName()
    {
        AppUsage[] apps =
        [
            App("b", cpu: 100, io: 5, gpu: null, lifetimes: 1),
            App("a", cpu: 100, io: 9, gpu: 2, lifetimes: 3),
            App("c", cpu: 300, io: 1, gpu: 1, lifetimes: 2)
        ];

        Assert.Multiple(() =>
        {
            Assert.That(Names(UsageSort.CpuTime), Is.EqualTo(new[] { "c", "a", "b" }), "Ties fall back to the name.");
            Assert.That(Names(UsageSort.Io), Is.EqualTo(new[] { "a", "b", "c" }));
            Assert.That(Names(UsageSort.GpuTime), Is.EqualTo(new[] { "a", "c", "b" }), "No GPU reading ranks last.");
            Assert.That(Names(UsageSort.Processes), Is.EqualTo(new[] { "a", "c", "b" }));
        });

        string[] Names(UsageSort sort)
        {
            return [.. UsageView.Visible(apps, new UsageListSettings(0, sort, false), "").Select(app => app.Name)];
        }
    }

    [Test]
    public void Visible_FiltersBeforeTheTopLimit()
    {
        AppUsage[] apps =
        [
            App("tool", cpu: 900) with { ImagePath = @"C:\Tools\tool.exe" },
            App("chrome", cpu: 500, isApp: true) with { AppName = "Google Chrome" },
            App("code", cpu: 400, isApp: true) with { ImagePath = @"C:\Apps\code.exe" },
            App("edge", cpu: 300, isApp: true)
        ];

        Assert.Multiple(() =>
        {
            Assert.That(UsageView.Visible(apps, new UsageListSettings(2, UsageSort.CpuTime, true), "")
                .Select(app => app.Name), Is.EqualTo(new[] { "chrome", "code" }));
            Assert.That(UsageView.Visible(apps, new UsageListSettings(5, UsageSort.CpuTime, false), "google")
                .Select(app => app.Name), Is.EqualTo(new[] { "chrome" }), "Search covers the description.");
            Assert.That(UsageView.Visible(apps, new UsageListSettings(5, UsageSort.CpuTime, false), @"c:\apps")
                .Select(app => app.Name), Is.EqualTo(new[] { "code" }), "Search covers the path.");
        });
    }

    [Test]
    public void Text_FormatsTotalsWithoutDroppingHoursPastADay()
    {
        var culture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        try
        {
            Assert.Multiple(() =>
            {
                Assert.That(UsageText.CpuTime(TimeSpan.FromHours(27.5).Ticks), Is.EqualTo("27:30:00"));
                Assert.That(UsageText.CpuTime(-5), Is.EqualTo("0:00:00"));
                Assert.That(UsageText.Bytes(999), Is.EqualTo("999 B"));
                Assert.That(UsageText.Bytes(4_800_000_000), Is.EqualTo("4.8 GB"));
                Assert.That(UsageText.Processes(App("a") with { Lifetimes = 41, Running = 18 }), Is.EqualTo("41 (18)"));
                Assert.That(UsageText.Share(1, 0), Is.EqualTo(Strings.MissingReading));
            });
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
        }
    }

    [Test]
    public void LastActive_IsNowWithinFiveSecondsOfTheNewestSample()
    {
        Assert.Multiple(() =>
        {
            Assert.That(UsageText.LastActive(new SampleKey(1, Now.AddSeconds(-4)), Now), Is.EqualTo(Strings.UsageNow));
            Assert.That(UsageText.LastActive(new SampleKey(1, Now.AddSeconds(-30)), Now), Is.Not.EqualTo(Strings.UsageNow));
            Assert.That(UsageText.LastActive(null, Now), Is.EqualTo(Strings.MissingReading));
        });
    }

    [Test]
    public void Report_ListsTheVisibleAppsAndNotAttributedTime()
    {
        var frame = new UsageFrame([App("a", cpu: 600), App("b", cpu: 200)], 1_000, 200, Now.AddHours(-1), Now, false);

        var report = UsageText.Report(frame, new UsageListSettings(1, UsageSort.CpuTime, false), "");

        Assert.Multiple(() =>
        {
            Assert.That(report, Does.Contain("\na\t"));
            Assert.That(report, Does.Not.Contain("\nb\t"), "The Top limit applies to the report too.");
            Assert.That(report, Does.Contain(Strings.UsageNotAttributed + "\t"));
        });
    }

    [Test]
    public void DisplayName_LabelsTheSharedRow()
    {
        Assert.That(UsageText.DisplayName(App(UsageLedger.OtherAppsKey) with { Name = "" }),
            Is.EqualTo(Strings.UsageOtherApps));
    }

    [Test, Apartment(ApartmentState.STA)]
    public void ActionBarTracksSelectedAppAcrossRefreshAndFilter()
    {
        var view = new UsageView { CanShowInProcesses = _ => true };
        var first = App("first", cpu: 200) with { ImagePath = @"C:\Apps\first.exe" };
        var second = App("second", cpu: 100) with { ImagePath = @"C:\Apps\second.exe" };
        view.Update(new UsageFrame([first, second], 300, 0, Now, Now, false));
        Assert.That(view.CopyDetails.IsEnabled, Is.False);

        view.Rows.SelectedItem = view.Rows.Items.Cast<UsageDisplayRow>().Single(row => row.Key == first.Key);
        string? propertiesName = null;
        string? propertiesDetails = null;
        string? copied = null;
        view.PropertiesRequested += (name, details) =>
        {
            propertiesName = name;
            propertiesDetails = details;
        };
        view.CopyRequested += details => copied = details;
        view.Update(new UsageFrame([first with { CpuTicks = 50 }, second with { CpuTicks = 300 }],
            350, 0, Now, Now, false));
        view.Properties.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        view.CopyDetails.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Multiple(() =>
        {
            Assert.That((view.Rows.SelectedItem as UsageDisplayRow)?.Key, Is.EqualTo(first.Key));
            Assert.That(view.SelectionName.Text, Is.EqualTo("first"));
            Assert.That(propertiesName, Is.EqualTo("first"));
            Assert.That(propertiesDetails, Does.Contain("first"));
            Assert.That(copied, Does.Contain("first"));
            Assert.That(view.OpenFileLocation.IsEnabled, Is.True);
        });

        view.ActionsHidden = true;
        Assert.That(view.ActionBar.Visibility, Is.EqualTo(Visibility.Collapsed));
        view.Search.Text = "second";
        Assert.Multiple(() =>
        {
            Assert.That(view.Rows.SelectedItem, Is.Null);
            Assert.That(view.CopyDetails.IsEnabled, Is.False);
            Assert.That(view.Properties.IsEnabled, Is.False);
            Assert.That(view.OpenFileLocation.IsEnabled, Is.False);
            Assert.That(view.ActionBar.Visibility, Is.EqualTo(Visibility.Collapsed));
        });
        view.ActionsHidden = false;
        Assert.That(view.ActionBar.Visibility, Is.EqualTo(Visibility.Visible));
    }

    private static AppUsage App(string name, long cpu = 0, double io = 0, double? gpu = null, int lifetimes = 1,
        bool isApp = false)
    {
        return new AppUsage(name, name, "", "", "", cpu, io, gpu, lifetimes, 0, isApp, null);
    }
}
