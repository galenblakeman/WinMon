using MonCore.Monitoring;
using NUnit.Framework;
using WinMon.Controls;
using WinMon.Resources;

namespace WinMon.Tests;

[TestFixture]
[SetCulture("en-US")]
[SetUICulture("en")]
public sealed class SnapshotTextTests
{
    private static SnapshotReport Report(bool grouped = false)
    {
        return new SnapshotReport(
            new MonitorSample(new DateTimeOffset(2026, 10, 21, 14, 32, 8, TimeSpan.FromHours(-5)), 42.3, [], 1)
            {
                Resources = new ResourceSample
                {
                    UsedMemoryBytes = 8UL << 30, TotalMemoryBytes = 16UL << 30,
                    CachedBytes = 1UL << 30, PagingReadsPerSecond = 12.5, StandbyBytes = 2UL << 30,
                    ModifiedBytes = 3UL << 20, FreeBytes = 4UL << 20, CompressionStoreBytes = 5UL << 20,
                    DiskName = "C:", DiskActivePercent = 0, SecondaryDiskName = "D:", SecondaryDiskActivePercent = 28,
                    ReceiveBytesPerSecond = 1_000_000, SendBytesPerSecond = 0
                }
            },
            [
                new ProcessUsage(123, 1, "browser", 18.2, 100UL << 20, 2_000_000)
                {
                    AppName = "Browser", PrivateBytes = 60UL << 20, MemberCount = grouped ? 2 : 1,
                    MemberIds = grouped ? [123, 456] : [], ThreadCount = 12, HandleCount = 34
                }
            ], grouped, "CPU", "browser", false, "Coverage note", new SnapshotRemainder(3, 4.2, null));
    }

    [Test]
    public void Format_AlwaysIncludesHistoricalSummaryAndTopProcesses()
    {
        var text = SnapshotText.Format(Report());
        Assert.Multiple(() =>
        {
            Assert.That(text, Does.Contain("2026-10-21 14:32:08 -05:00"));
            Assert.That(text, Does.Contain("Capture:").And.Not.Contain("Snapshot"));
            Assert.That(Strings.CopySnapshot, Is.EqualTo("Copy capture"));
            Assert.That(text, Does.Contain("42.3%").And.Contain("8.0 / 16 GiB").And.Contain("50.0%"));
            Assert.That(text, Does.Contain("System cache\t1.1 GB").And.Contain("Paging reads / sec\t12.5/s")
                .And.Contain("Standby\t2.1 GB").And.Contain("Compression store\t5.2 MB"));
            Assert.That(text, Does.Contain("GPU\t—"));
            Assert.That(text, Does.Contain("Disk (C:)\t0.0%").And.Contain("Disk (D:)\t28.0%"));
            Assert.That(text, Does.Contain("↓ 1 MB/s").And.Contain("↑ 0 KB/s"));
            Assert.That(text, Does.Contain("Browser").And.Contain("five-second averages"));
        });
    }

    [Test]
    public void Format_MarksUnavailableMemoryPressureReadingsAsMissing()
    {
        var text = SnapshotText.Format(Report() with
        {
            Sample = Report().Sample with { Resources = new ResourceSample() }
        });

        Assert.That(text, Does.Contain("System cache\t—").And.Contain("Paging reads / sec\t—")
            .And.Contain("Standby\t—").And.Contain("Modified\t—").And.Contain(Strings.DetailFree + "\t—")
            .And.Contain("Compression store\t—"));
    }

    [TestCase(false, "100.0")]
    [TestCase(true, "60.0")]
    public void Processes_PreserveDisplayedOrderGroupingMemoryAndRemainder(bool grouped, string memory)
    {
        var report = Report(grouped);
        var text = SnapshotText.Format(report);
        var row = text.Split(Environment.NewLine).Single(l => l.StartsWith(grouped ? "Browser ×2\t" : "Browser\t"))
            .Split('\t');
        Assert.Multiple(() =>
        {
            Assert.That(row, Has.Length.EqualTo(9));
            Assert.That(row[1], Is.EqualTo(grouped ? "123, 456" : "123"));
            Assert.That(row[2..8], Is.EqualTo(new[] { "18.2", "—", memory, "2.00", "12", "34" }));
            Assert.That(row[8], Is.EqualTo(Strings.ExecutablePathUnavailable));
            Assert.That(text, Does.Contain(grouped ? "Private (MiB)" : "Memory (MiB)"));
            Assert.That(text, Does.Contain("five-second averages").And.Contain("browser").And.Contain("Coverage note"));
            Assert.That(text, Does.Contain(grouped ? "Other executables" : "Other processes"));
            Assert.That(text.IndexOf("Browser", StringComparison.Ordinal),
                Is.LessThan(text.IndexOf("Other", StringComparison.Ordinal)));
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
            Assert.That(row, Has.Length.EqualTo(9));
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
