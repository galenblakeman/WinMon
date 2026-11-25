using NUnit.Framework;
using WinMon.NetworkService;

namespace WinMon.Tests;

[TestFixture]
public sealed class CollectorCalibrationBoundaryTests
{
    private static readonly DateTime Epoch = new(2026, 11, 21, 12, 0, 0, DateTimeKind.Utc);

    [TestCase(89_999_999L, false)]
    [TestCase(90_000_000L, true)]
    [TestCase(100_000_000L, true)]
    [TestCase(120_000_000L, true)]
    [TestCase(130_000_000L, true)]
    [TestCase(130_000_001L, false)]
    public void UtcMustStayInsideTheSamplingBracketWithInclusiveOneSecondTolerance(long utcTicks, bool expected)
    {
        Assert.That(EtwNetworkCollector.IsClockCalibrationValid(Epoch, 1000, 1000, 11000,
            Epoch.AddTicks(utcTicks), 13000), Is.EqualTo(expected));
    }

    [TestCase(-1, 11000, 13000)]
    [TestCase(0, 11000, 13000)]
    [TestCase(1000, 999, 13000)]
    [TestCase(1000, 11000, 10999)]
    public void InvalidFrequencyOrReversedTimestampsRejectCalibration(long frequency, long now, long after)
    {
        Assert.That(EtwNetworkCollector.IsClockCalibrationValid(Epoch, 1000, frequency, now,
            Epoch.AddSeconds(10), after), Is.False);
    }

    [TestCase(110_000_000L, true)]
    [TestCase(110_000_001L, false)]
    public void OmittedUpperTimestampUsesTheFirstTimestamp(long utcTicks, bool expected)
    {
        Assert.That(EtwNetworkCollector.IsClockCalibrationValid(Epoch, 1000, 1000, 11000,
            Epoch.AddTicks(utcTicks)), Is.EqualTo(expected));
    }
}
