using NUnit.Framework;
using WinMon.Monitoring;

namespace WinMon.Tests;

public sealed class CpuClockTests
{
    [Test]
    public void Clock_UsesCurrentReadingsAcrossProcessors_NotRatedMaximum()
    {
        WindowsCpuClock.ProcessorPowerInformation[] readings =
        [new() { CurrentMhz = 2100, MaxMhz = 4500 }, new() { CurrentMhz = 3300, MaxMhz = 4500 }];
        Assert.That(WindowsCpuClock.AverageMhz(readings), Is.EqualTo(2700));
    }

    [Test]
    public void Clock_MissingReadingIsUnavailable_NotZeroOrPartialAverage()
    {
        Assert.That(WindowsCpuClock.AverageMhz([]), Is.Null);
        Assert.That(WindowsCpuClock.AverageMhz([new() { CurrentMhz = 2100 }, new()]), Is.Null);
    }

    [Test]
    public void NativeClock_ReadsWindowsWithoutInvalidValues()
    {
        var mhz = new WindowsCpuClock().ReadMhz();
        TestContext.Out.WriteLine($"Windows current CPU clock: {mhz?.ToString() ?? "unavailable"} MHz");
        Assert.That(mhz, Is.Null.Or.GreaterThan(0));
    }
}
