using NUnit.Framework;
using WinMon.Controls;
using WinMon.Monitoring;

namespace WinMon.Tests;

[TestFixture]
[SetCulture("en-US")]
[SetUICulture("en")]
public sealed class SnapshotTextTests
{
    private static SnapshotReport Report(bool grouped = false) => new(
        new MonitorSample(new DateTimeOffset(2026, 10, 21, 14, 32, 8, TimeSpan.FromHours(-5)), 42.3, [], 1)
        {
            Resources = new ResourceSample { UsedMemoryBytes = 8UL << 30, TotalMemoryBytes = 16UL << 30,
                DiskName = "C:", DiskActivePercent = 0, SecondaryDiskName = "D:", SecondaryDiskActivePercent = 28,
                ReceiveBytesPerSecond = 1_000_000, SendBytesPerSecond = 0 }
        },
        [new ProcessUsage(123, 1, "browser", 18.2, 100UL << 20, 2_000_000)
        {
            AppName = "Browser", PrivateBytes = 60UL << 20, MemberCount = grouped ? 2 : 1,
            MemberIds = grouped ? [123, 456] : [], ThreadCount = 12, HandleCount = 34
        }], grouped, "CPU", "browser", false, "Coverage note", new SnapshotRemainder(3, 4.2, null));

    [Test]
    public void Format_AlwaysIncludesHistoricalSummaryAndTopProcesses()
    {
        var text = SnapshotText.Format(Report());
        Assert.Multiple(() =>
        {
            Assert.That(text, Does.Contain("2026-10-21 14:32:08 -05:00"));
            Assert.That(text, Does.Contain("Capture:").And.Not.Contain("Snapshot"));
            Assert.That(WinMon.Resources.Strings.CopySnapshot, Is.EqualTo("Copy capture"));
            Assert.That(text, Does.Contain("42.3%").And.Contain("8.0 / 16 GiB").And.Contain("50.0%"));
            Assert.That(text, Does.Contain("GPU\t—"));
            Assert.That(text, Does.Contain("Disk (C:)\t0.0%").And.Contain("Disk (D:)\t28.0%"));
            Assert.That(text, Does.Contain("↓ 8 Mbps").And.Contain("↑ 0 Kbps"));
            Assert.That(text, Does.Contain("Browser").And.Contain("five-second averages"));
        });
    }

    [TestCase(false, "100.0")]
    [TestCase(true, "60.0")]
    public void Processes_PreserveDisplayedOrderGroupingMemoryAndRemainder(bool grouped, string memory)
    {
        var report = Report(grouped);
        var text = SnapshotText.Format(report);
        var row = text.Split(Environment.NewLine).Single(l => l.StartsWith(grouped ? "Browser ×2\t" : "Browser\t")).Split('\t');
        Assert.Multiple(() =>
        {
            Assert.That(row, Has.Length.EqualTo(8));
            Assert.That(row[1], Is.EqualTo(grouped ? "123, 456" : "123"));
            Assert.That(row[2..], Is.EqualTo(new[] { "18.2", "—", memory, "2.00", "12", "34" }));
            Assert.That(text, Does.Contain(grouped ? "Private (MiB)" : "Memory (MiB)"));
            Assert.That(text, Does.Contain("five-second averages").And.Contain("browser").And.Contain("Coverage note"));
            Assert.That(text, Does.Contain(grouped ? "Other executables" : "Other processes"));
            Assert.That(text.IndexOf("Browser", StringComparison.Ordinal), Is.LessThan(text.IndexOf("Other", StringComparison.Ordinal)));
        });
    }

    [TestCase("=SUM(A1:A2)")]
    [TestCase(" +1")]
    [TestCase("-1")]
    [TestCase("@name")]
    public void Format_PreservesNamesWithoutExtraRowsOrColumns(string name)
    {
        var report = Report() with { Processes = [new ProcessUsage(1, 1, name + "\tline\r\nnext", 0)] };
        var text = SnapshotText.Format(report);
        var row = text.Split(Environment.NewLine).Single(l => l.StartsWith(name, StringComparison.Ordinal)).Split('\t');
        Assert.Multiple(() =>
        {
            Assert.That(row, Has.Length.EqualTo(8));
            Assert.That(row[0], Is.EqualTo(name + " line  next"));
            Assert.That(row[2], Is.EqualTo("0.0"));
            Assert.That(row[4], Is.EqualTo("—"));
        });
    }

    [Test]
    [SetCulture("de-DE")]
    public void Format_UsesLocalDecimalSeparatorWithoutThousandsSeparators()
    {
        var text = SnapshotText.Format(Report());
        Assert.That(text, Does.Contain("Browser\t123\t18,2\t—\t100,0\t2,00\t12\t34"));
    }
}
