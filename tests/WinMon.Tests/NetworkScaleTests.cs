using System.Diagnostics;
using NUnit.Framework;
using WinMon.Controls;
using WinMon.Monitoring;

namespace WinMon.Tests;

[TestFixture]
public sealed class NetworkScaleTests
{
    private static MonitorSample Sample(double? rxBytes, double? txBytes, bool gap = false) =>
        new(DateTimeOffset.UtcNow, 10, [], 0, gap, Stopwatch.Frequency)
        {
            Resources = new ResourceSample { ReceiveBytesPerSecond = rxBytes, SendBytesPerSecond = txBytes }
        };

    [TestCase(0, 1_000_000)]
    [TestCase(400_000, 1_000_000)]
    [TestCase(1_200_000, 2_000_000)]
    [TestCase(3_000_000, 5_000_000)]
    [TestCase(7_000_000, 10_000_000)]
    [TestCase(10_000_000, 10_000_000)]
    [TestCase(180_000_000, 200_000_000)]
    public void NetworkScale_RoundsBusierDirectionUpToOneTwoFiveStep(double peakBits, double expected)
    {
        var history = new[] { Sample(peakBits / 8, 0), Sample(0, peakBits / 16) };
        Assert.That(CpuGraph.NetworkScale(history), Is.EqualTo(expected).Within(0.001));
    }

    [Test]
    public void NetworkScale_IgnoresGapsAndUnavailableRates()
    {
        var history = new[] { Sample(100_000_000, 0, gap: true), Sample(null, 1_000), Sample(250_000, 0) };
        Assert.Multiple(() =>
        {
            Assert.That(CpuGraph.NetworkScale(history), Is.EqualTo(2_000_000).Within(0.001));
            Assert.That(CpuGraph.NetworkScale([]), Is.EqualTo(1_000_000).Within(0.001));
            Assert.That(CpuGraph.NetworkBitsPerSecond(history[1].Resources), Is.Null);
        });
    }

    [TestCase(0, "0 Kbps")]
    [TestCase(850_000, "850 Kbps")]
    [TestCase(1_234_000, "1.23 Mbps")]
    [TestCase(12_340_000, "12.3 Mbps")]
    [TestCase(2_500_000_000, "2.5 Gbps")]
    public void RateText_UsesThreeSignificantDigitsAndAdaptiveUnit(double bits, string expected)
    {
        Assert.That(RateText.Format(bits), Is.EqualTo(expected));
    }

    [TestCase(36_300_000, 1_000_000, "↓36.3 ↑1 Mbps")]
    [TestCase(1_010_000, 200_000, "↓1.01 ↑0.2 Mbps")]
    [TestCase(0, 0, "↓0 ↑0 Kbps")]
    public void RateText_PairSharesTheLargerRatesUnit(double rx, double tx, string expected)
    {
        Assert.That(RateText.Pair(rx, tx), Is.EqualTo(expected));
    }
}
