using NUnit.Framework;
using WinMon.Monitoring;

namespace WinMon.Tests;

public sealed class CpuClockTests
{
    [Test]
    public void Clock_ScalesRatedClockByProcessorPerformance_AboveRatedMaximum()
    {
        // CurrentMhz stuck at the rated clock is what Ryzen 9000 reports; it must not be used.
        WindowsCpuClock.ProcessorPowerInformation[] readings =
            [new() { CurrentMhz = 4300, MaxMhz = 4300 }, new() { CurrentMhz = 4300, MaxMhz = 4300 }];
        Assert.That(WindowsCpuClock.EffectiveMhz(readings, 125), Is.EqualTo(5375));
        Assert.That(WindowsCpuClock.EffectiveMhz(readings, 50), Is.EqualTo(2150));
    }

    [Test]
    public void Clock_AveragesRatedClockAcrossProcessors()
    {
        WindowsCpuClock.ProcessorPowerInformation[] hybrid = [new() { MaxMhz = 4000 }, new() { MaxMhz = 3000 }];
        Assert.That(WindowsCpuClock.EffectiveMhz(hybrid, 50), Is.EqualTo(1750));
    }

    [TestCase(null)]
    [TestCase(0d)]
    [TestCase(-5d)]
    [TestCase(double.NaN)]
    [TestCase(double.PositiveInfinity)]
    public void Clock_MissingOrInvalidPerformanceIsUnavailable_NotRatedClock(double? performance)
    {
        WindowsCpuClock.ProcessorPowerInformation[] readings = [new() { CurrentMhz = 4300, MaxMhz = 4300 }];
        Assert.That(WindowsCpuClock.EffectiveMhz(readings, performance), Is.Null);
    }

    [Test]
    public void Clock_MissingRatedClockIsUnavailable_NotZeroOrPartialAverage()
    {
        Assert.That(WindowsCpuClock.EffectiveMhz([], 100), Is.Null);
        Assert.That(WindowsCpuClock.EffectiveMhz([new() { MaxMhz = 4300 }, new()], 100), Is.Null);
    }

    [Test]
    public void Clock_FirstSampleWithoutPerformanceIsUnavailable()
    {
        Assert.That(new WindowsCpuClock().ReadMhz(null), Is.Null);
    }

    [Test]
    [Category("Integration")]
    public void NativeClock_ReadsWindowsWithoutInvalidValues()
    {
        var mhz = new WindowsCpuClock().ReadMhz(100);
        TestContext.Out.WriteLine($"Windows rated CPU clock: {mhz?.ToString() ?? "unavailable"} MHz");
        Assert.That(mhz, Is.Null.Or.GreaterThan(0));
    }
}
