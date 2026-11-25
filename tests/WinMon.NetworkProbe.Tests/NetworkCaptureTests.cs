using NUnit.Framework;

namespace WinMon.NetworkProbe.Tests;

[TestFixture]
public sealed class NetworkCaptureTests
{
    [TestCase(true, false, true, 30L)]
    [TestCase(true, true, false, 7L)]
    [TestCase(false, false, true, 9L)]
    [TestCase(false, true, false, 11L)]
    [TestCase(true, false, false, 0L)]
    public void Count_CombinesOnlyTheSelectedProtocolFamilyAndDirection(bool tcp, bool ipv6, bool send, long expected)
    {
        var snapshot = new CaptureSnapshot([
            new(42, 100, true, false, true, 10),
            new(43, 200, true, false, true, 20),
            new(42, 100, true, true, false, 7),
            new(42, 100, false, false, true, 9),
            new(42, 100, false, true, false, 11)
        ], 0, 0, 0, 0, 0, 0, 0, 0, 0);
        Assert.That(NetworkCapture.Count(snapshot, tcp, ipv6, send), Is.EqualTo(expected));
    }
}
