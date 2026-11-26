using System.Globalization;
using MonCore.Monitoring;
using NUnit.Framework;
using WinMon.Resources;

namespace WinMon.Cli.Tests;

[TestFixture]
public sealed class UsageTabTests
{
    [Test]
    public void Build_UsesDesktopMetricsAndExactBusyTimeForShare()
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        try
        {
            var since = new DateTimeOffset(2026, 11, 9, 12, 0, 0, TimeSpan.Zero);
            var active = new SampleKey(1, since.AddDays(-1).AddMinutes(5));
            var app = new AppUsage("alpha.exe", "alpha.exe", @"C:\Apps\alpha.exe", "Alpha", "Contoso",
                TimeSpan.FromMinutes(2).Ticks, 4_800_000_000, 90, 4, 2, true, active);

            var table = UsageTab.Build([app], since, TimeSpan.FromMinutes(4).Ticks, since, 0, "", false, 0);

            Assert.Multiple(() =>
            {
                Assert.That(table.Columns, Is.EqualTo([
                    Strings.UsageApp, Strings.UsageCpuTime, Strings.UsageShare, Strings.IO, Strings.UsageGpuTime,
                    Strings.ProcessesTab, Strings.UsageLastActive
                ]));
                Assert.That(table.Rows, Has.Count.EqualTo(1));
                Assert.That(table.Rows[0].Key, Is.EqualTo("alpha.exe"));
                Assert.That(table.Rows[0].Cells, Is.EqualTo(["Alpha", "0:02:00", "50.0%", "4.8 GB", "0:01:30",
                    "4 (2)", active.Timestamp.ToLocalTime().ToString("g", CultureInfo.InvariantCulture)]));
                Assert.That(table.Rows[0].Details, Does.Contain(@"C:\Apps\alpha.exe"));
                Assert.That(table.Rows[0].Details, Does.Contain("Contoso"));
                Assert.That(table.Rows[0].Details,
                    Does.Contain(string.Format(CultureInfo.CurrentCulture, Strings.UsageReportSince,
                        since.ToLocalTime().DateTime)));
            });
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Test]
    public void Build_AppliesSharedFilteringSortingAndTop()
    {
        AppUsage[] apps =
        [
            new("a", "a", "", "", "", 10, 100, null, 1, 0, false, null),
            new("b", "b", "", "", "", 20, 300, null, 1, 0, true, null),
            new("c", "c", @"C:\Apps\c.exe", "", "", 30, 200, null, 1, 0, true, null)
        ];

        var table = UsageTab.Build(apps, DateTimeOffset.UtcNow, 100, null, 1, @"c:\apps", false, 1);

        Assert.That(table.Rows.Select(row => row.Key), Is.EqualTo(["c"]));
    }

    [Test]
    public void Build_UsesTheLatestSampleForTheDesktopNowReading()
    {
        var latest = new DateTimeOffset(2026, 11, 9, 12, 0, 0, TimeSpan.Zero);
        var app = new AppUsage("a", "a", "", "", "", 0, 0, null, 1, 1, false,
            new SampleKey(1, latest.AddSeconds(-5)));

        var table = UsageTab.Build([app], latest, 1, latest, 0, "", false, 0);

        Assert.That(table.Rows.Single().Cells[6], Is.EqualTo(Strings.UsageNow));
    }

    [TestCase(1)]
    [TestCase(5)]
    [TestCase(6)]
    public void ArchivedUsageShowsRecordedDateTimeInCellsAndDetails(int secondsBeforeMidnight)
    {
        var ending = new DateTimeOffset(2026, 11, 2, 0, 0, 0, TimeSpan.Zero);
        var active = ending.AddSeconds(-secondsBeforeMidnight);
        var app = new AppUsage("worker", "worker", "", "", "", 1, 0, null, 1, 0, false,
            new SampleKey(1, active));

        var row = UsageTab.Build([app], ending.AddDays(-1), 1, ending, 0, "", false, 0, archived: true).Rows.Single();

        var expected = active.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);
        Assert.Multiple(() =>
        {
            Assert.That(row.Cells[6], Is.EqualTo(expected));
            Assert.That(row.Details, Does.EndWith($"{Strings.UsageLastActive}: {expected}"));
        });
    }

    [Test]
    public void ArchivedUsageIncludesTheDateEvenForToday()
    {
        var active = DateTimeOffset.Now;
        var app = new AppUsage("worker", "worker", "", "", "", 1, 0, null, 1, 0, false,
            new SampleKey(1, active));

        var row = UsageTab.Build([app], active.AddHours(-1), 1, active.AddMinutes(1), 0, "", false, 0, archived: true).Rows.Single();

        var expected = active.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);
        Assert.That(row.Cells[6], Is.EqualTo(expected));
        Assert.That(row.Details, Does.EndWith($"{Strings.UsageLastActive}: {expected}"));
    }

    [Test]
    public void ArchivedUsageKeepsMissingActivityUnavailable()
    {
        var app = new AppUsage("worker", "worker", "", "", "", 0, 0, null, 1, 0, false, null);

        var row = UsageTab.Build([app], DateTimeOffset.UnixEpoch, 1, DateTimeOffset.UnixEpoch, 0, "", false, 0, archived: true).Rows.Single();

        Assert.That(row.Cells[6], Is.EqualTo(Strings.MissingReading));
        Assert.That(row.Details, Does.EndWith($"{Strings.UsageLastActive}: {Strings.MissingReading}"));
    }
}
