using NUnit.Framework;
using WinMon.Controls;

namespace WinMon.Tests;

[SetCulture("en-US")]
public sealed class TableMetricTextTests
{
    [TestCase(0UL, "0 B")]
    [TestCase(1023UL, "1,023 B")]
    [TestCase(1536UL, "1.5 KiB")]
    [TestCase(1_048_576UL, "1.0 MiB")]
    [TestCase(1_572_864UL, "1.5 MiB")]
    [TestCase(1_610_612_736UL, "1.5 GiB")]
    [TestCase(1_649_267_441_664UL, "1.5 TiB")]
    public void Memory_ChoosesBinaryUnit(ulong bytes, string expected) =>
        Assert.That(TableMetricText.Memory(bytes), Is.EqualTo(expected));

    [TestCase(0d, "0 B/s")]
    [TestCase(999d, "999 B/s")]
    [TestCase(1_500d, "1.5 KB/s")]
    [TestCase(4_000_000d, "4.0 MB/s")]
    [TestCase(1_500_000_000d, "1.5 GB/s")]
    [TestCase(1_500_000_000_000d, "1.5 TB/s")]
    public void IoRate_ChoosesDecimalUnit(double bytesPerSecond, string expected) =>
        Assert.That(TableMetricText.IoRate(bytesPerSecond), Is.EqualTo(expected));
}
