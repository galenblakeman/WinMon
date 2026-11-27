using MonCore.Monitoring;
using NUnit.Framework;

namespace MonCore.Tests;

[TestFixture]
public sealed class NetworkChartScaleTests
{
    // Inputs and results are bits per second; the 1-2-5 step applies to the byte rate shown on the axis.
    [TestCase(0, 800_000)]
    [TestCase(400_000, 800_000)]
    [TestCase(1_200_000, 1_600_000)]
    [TestCase(3_000_000, 4_000_000)]
    [TestCase(7_000_000, 8_000_000)]
    [TestCase(8_000_000, 8_000_000)]
    [TestCase(100_000_000, 160_000_000)]
    [TestCase(180_000_000, 400_000_000)]
    public void Maximum_RoundsPeakUpToOneTwoFiveByteRateStep(double bitsPerSecond, double expected)
    {
        var samples = new[] { Sample(receiveBytesPerSecond: bitsPerSecond / 8, sendBytesPerSecond: 0) };

        Assert.That(NetworkChartScale.Maximum(samples), Is.EqualTo(expected));
    }

    [TestCase(0.3, 0.5)]
    [TestCase(1, 1)]
    [TestCase(1.2, 2)]
    [TestCase(37, 50)]
    [TestCase(600, 1000)]
    public void RoundUp_ReturnsTheNextOneTwoFiveStep(double value, double expected) =>
        Assert.That(NetworkChartScale.RoundUp(value), Is.EqualTo(expected).Within(1e-9));

    [Test]
    public void Maximum_IgnoresGapsAndMissingRates()
    {
        var samples = new[]
        {
            Sample(875_000, 0, isGap: true),
            Sample(null, 1),
            Sample(1, null)
        };

        Assert.That(NetworkChartScale.Maximum(samples), Is.EqualTo(800_000));
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

        Assert.That(NetworkChartScale.Maximum(samples), Is.EqualTo(1_600_000));
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
