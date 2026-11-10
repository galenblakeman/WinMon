using System.Globalization;
using NUnit.Framework;
using WinMon.Cli;
using WinMon.Monitoring;
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
}
