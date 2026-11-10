using NUnit.Framework;
using WinMon.Controls;
using WinMon.Resources;

namespace WinMon.Tests;

[SetCulture("en-US")]
public sealed class GaugeReadoutTextTests
{
    [TestCase(4300d, "4.30 GHz")]
    [TestCase(1d, "0.00 GHz")]
    public void CpuClock_FormatsPositiveFiniteMegahertz(double megahertz, string expected)
    {
        Assert.That(GaugeReadoutText.CpuClock(megahertz), Is.EqualTo(expected));
    }

    [TestCase(null)]
    [TestCase(0d)]
    [TestCase(-1d)]
    [TestCase(double.NaN)]
    [TestCase(double.PositiveInfinity)]
    public void CpuClock_UsesUnavailableForMissingOrInvalidReadings(double? megahertz)
    {
        Assert.That(GaugeReadoutText.CpuClock(megahertz), Is.EqualTo(Strings.Unavailable));
    }

    [Test]
    public void Memory_FormatsUsedAndTotalGiB()
    {
        Assert.That(
            GaugeReadoutText.Memory(17_179_869_184, 34_359_738_368),
            Is.EqualTo("16.0 / 32 GiB"));
    }

    [Test]
    public void Memory_UsesUnavailableWhenEitherReadingIsMissing()
    {
        Assert.Multiple(() =>
        {
            Assert.That(GaugeReadoutText.Memory(null, 1), Is.EqualTo(Strings.Unavailable));
            Assert.That(GaugeReadoutText.Memory(1, null), Is.EqualTo(Strings.Unavailable));
        });
    }

    [TestCase(1_500_000d, "1.5 MB/s")]
    [TestCase(0d, "0.0 MB/s")]
    public void DiskRate_FormatsFiniteNonnegativeBytesPerSecond(double bytesPerSecond, string expected)
    {
        Assert.That(GaugeReadoutText.DiskRate(bytesPerSecond), Is.EqualTo(expected));
    }

    [TestCase(null)]
    [TestCase(-1d)]
    [TestCase(double.NaN)]
    [TestCase(double.PositiveInfinity)]
    public void DiskRate_UsesUnavailableForMissingOrInvalidReadings(double? bytesPerSecond)
    {
        Assert.That(GaugeReadoutText.DiskRate(bytesPerSecond), Is.EqualTo(Strings.Unavailable));
    }

    [Test]
    public void GpuMemory_FormatsUsedAndTotalGiB()
    {
        Assert.That(
            GaugeReadoutText.GpuMemory(4_294_967_296, 8_589_934_592),
            Is.EqualTo("4.0 / 8.0 GiB"));
    }

    [Test]
    public void GpuMemory_Uses3DWhenMemoryIsNotAvailable()
    {
        Assert.Multiple(() =>
        {
            Assert.That(GaugeReadoutText.GpuMemory(null, 1), Is.EqualTo(Strings.GaugeGpuEngine3D));
            Assert.That(GaugeReadoutText.GpuMemory(1, null), Is.EqualTo(Strings.GaugeGpuEngine3D));
            Assert.That(GaugeReadoutText.GpuMemory(1, 0), Is.EqualTo(Strings.GaugeGpuEngine3D));
        });
    }
}
