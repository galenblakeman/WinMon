using MonCore.Windows.Monitoring;
using NUnit.Framework;

namespace MonCore.Windows.Tests;

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
    public void Clock_HybridCoresUseHighestRatedClock_LikeTaskManagerBaseSpeed()
    {
        // Core Ultra 9 275HX: 8 performance cores at 2,700 MHz, 16 efficient cores at 2,100 MHz; Task Manager base 2.70 GHz.
        var hybrid = Enumerable.Repeat(new WindowsCpuClock.ProcessorPowerInformation { MaxMhz = 2100 }, 16)
            .Concat(Enumerable.Repeat(new WindowsCpuClock.ProcessorPowerInformation { MaxMhz = 2700 }, 8)).ToArray();
        Assert.That(WindowsCpuClock.EffectiveMhz(hybrid, 156), Is.EqualTo(4212).Within(0.001));
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
