using System.Globalization;
using NUnit.Framework;
using WinMon.Monitoring;

namespace WinMon.Cli.Tests;

[TestFixture, SetCulture("en-US"), SetUICulture("en")]
public sealed class AdaptiveUnitTests
{
    [TestCase(0L, "0 B")]
    [TestCase(1023L, "1,023 B")]
    [TestCase(1024L, "1.0 KiB")]
    [TestCase(1048576L, "1.0 MiB")]
    [TestCase(1073741824L, "1.0 GiB")]
    [TestCase(1099511627776L, "1.0 TiB")]
    public void MemoryUsesDesktopBinaryUnits(long bytes, string expected) =>
        Assert.That(SnapshotReport.Memory((ulong)bytes), Is.EqualTo(expected));

    [TestCase(0d, "0 B/s")]
    [TestCase(999d, "999 B/s")]
    [TestCase(1000d, "1.0 KB/s")]
    [TestCase(1000000d, "1.0 MB/s")]
    [TestCase(1000000000d, "1.0 GB/s")]
    public void TableRatesUseDesktopDecimalByteUnits(double bytes, string expected) =>
        Assert.That(SnapshotReport.Rate(bytes), Is.EqualTo(expected));

    [TestCase(125000d, "125 KB/s")]
    [TestCase(1500000d, "1.5 MB/s")]
    public void NetworkSummaryMatchesDesktopByteRatesWithoutAnEightfoldError(double bytes, string expected) =>
        Assert.That(SnapshotReport.NetworkRate(bytes), Is.EqualTo(expected));

    [Test]
    public void ProcessCellsAndDetailsCarryTheirUnitsAndJsonKeepsRawBytes()
    {
        var process = new ProcessUsage(42, 1, "Claude", 10, 1048576, 125000)
            { PrivateBytes = 2147483648, ImagePath = "C:\\Claude.exe" };
        var sample = new MonitorSample(DateTimeOffset.UnixEpoch, 10, [process], 0);
        var table = ProcessesTab.Build([sample], ProcessSort.Memory, "", true, false, true);
        var report = SnapshotReport.Create(new CapturedFrame(sample), "test", 0, ProcessSort.Memory, "");
        Assert.Multiple(() =>
        {
            Assert.That(table.Rows.Single().Cells[3], Is.EqualTo("2.0 GiB"));
            Assert.That(table.Rows.Single().Cells[5], Is.EqualTo("125.0 KB/s"));
            Assert.That(table.Rows.Single().Details, Does.Contain("2.0 GiB").And.Contain("1.0 MiB"));
            Assert.That(table.Columns, Has.None.Contains("MiB"));
            Assert.That(report.Processes.Single().WorkingSetBytes, Is.EqualTo(1048576));
            Assert.That(report.Processes.Single().PrivateBytes, Is.EqualTo(2147483648));
            Assert.That(report.Processes.Single().IoBytesPerSecond, Is.EqualTo(125000));
            Assert.That(SnapshotReport.Memory(null), Is.EqualTo(WinMon.Resources.Strings.Unavailable));
            Assert.That(SnapshotReport.Rate(null), Is.EqualTo(WinMon.Resources.Strings.Unavailable));
        });
    }

    [Test]
    public void AdaptiveValuesUseTheCurrentNumberCulture()
    {
        var before = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            Assert.That(SnapshotReport.Memory(1073741824), Is.EqualTo("1,0 GiB"));
            Assert.That(SnapshotReport.Rate(1500), Is.EqualTo("1,5 KB/s"));
        }
        finally { CultureInfo.CurrentCulture = before; }
    }
}
