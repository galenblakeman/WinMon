using NUnit.Framework;
using WinMon.Monitoring;

namespace WinMon.Tests;

[TestFixture]
public sealed class NetworkChartScaleTests
{
    [TestCase(0, 1_000_000)]
    [TestCase(400_000, 1_000_000)]
    [TestCase(1_200_000, 2_000_000)]
    [TestCase(3_000_000, 5_000_000)]
    [TestCase(7_000_000, 10_000_000)]
    [TestCase(10_000_000, 10_000_000)]
    [TestCase(180_000_000, 200_000_000)]
    public void Maximum_RoundsPeakUpToOneTwoFiveStep(double bitsPerSecond, double expected)
    {
        var samples = new[] { Sample(receiveBytesPerSecond: bitsPerSecond / 8, sendBytesPerSecond: 0) };

        Assert.That(NetworkChartScale.Maximum(samples), Is.EqualTo(expected));
    }

    [Test]
    public void Maximum_IgnoresGapsAndMissingRates()
    {
        var samples = new[]
        {
            Sample(875_000, 0, isGap: true),
            Sample(null, 1),
            Sample(1, null)
        };

        Assert.That(NetworkChartScale.Maximum(samples), Is.EqualTo(1_000_000));
    }

    [Test]
    public void Maximum_UsesOnlyAvailableFiniteNonnegativeRates()
    {
        var samples = new[]
        {
            Sample(double.NaN, 1),
            Sample(double.PositiveInfinity, 1),
            Sample(-1, 1),
            Sample(187_500, 125_000)
        };

        Assert.That(NetworkChartScale.Maximum(samples), Is.EqualTo(2_000_000));
    }

    [Test]
    public void Maximum_CapsAnExtremeRoundedRateAtDoubleMaximum()
    {
        var samples = new[] { Sample(double.MaxValue / 8, 0) };

        Assert.That(NetworkChartScale.Maximum(samples), Is.EqualTo(double.MaxValue));
    }

    [Test]
    public void BitsPerSecond_UsesBusierDirectionWhenBothDirectionsAreAvailable()
    {
        var bits = NetworkChartScale.BitsPerSecond(new ResourceSample
        {
            ReceiveBytesPerSecond = 100,
            SendBytesPerSecond = 150
        });

        Assert.That(bits, Is.EqualTo(1_200));
    }

    [Test]
    public void BitsPerSecond_ReturnsNullForMissingOrInvalidDirections()
    {
        Assert.Multiple(() =>
        {
            Assert.That(NetworkChartScale.BitsPerSecond(new ResourceSample { ReceiveBytesPerSecond = 1 }), Is.Null);
            Assert.That(NetworkChartScale.BitsPerSecond(new ResourceSample { ReceiveBytesPerSecond = -1, SendBytesPerSecond = 1 }), Is.Null);
            Assert.That(NetworkChartScale.BitsPerSecond(new ResourceSample { ReceiveBytesPerSecond = 1, SendBytesPerSecond = double.NaN }), Is.Null);
        });
    }

    private static MonitorSample Sample(double? receiveBytesPerSecond, double? sendBytesPerSecond, bool isGap = false)
    {
        return new MonitorSample(DateTimeOffset.UnixEpoch, null, [], 0, isGap)
        {
            Resources = new ResourceSample
            {
                ReceiveBytesPerSecond = receiveBytesPerSecond,
                SendBytesPerSecond = sendBytesPerSecond
            }
        };
    }
}
