using System.Diagnostics;
using NUnit.Framework;
using WinMon.NetworkService;

namespace WinMon.Tests;

[TestFixture]
public sealed class ProcessOwnerProbeTests
{
    [Test]
    public void TryRead_ReturnsCurrentProcessLifetimeAndUserSid()
    {
        using var process = Process.GetCurrentProcess();

        var read = ProcessOwnerProbe.TryRead(process.Id, out var owner);

        Assert.Multiple(() =>
        {
            Assert.That(read, Is.True);
            Assert.That(owner.ProcessId, Is.EqualTo(process.Id));
            Assert.That(owner.CreationTicks, Is.EqualTo(process.StartTime.ToUniversalTime().ToFileTimeUtc()));
            Assert.That(owner.Sid, Is.Not.Empty);
            Assert.That(ProcessOwnerProbe.TryReadCreation(process.Id, out var created), Is.True);
            Assert.That(created, Is.EqualTo(owner.CreationTicks));
        });
    }

    [Test]
    public void TryRead_RejectsInvalidPid()
    {
        Assert.That(ProcessOwnerProbe.TryRead(-1, out _), Is.False);
    }

    [Test]
    public void ClockCalibrationGuard_RejectsWallClockDrift()
    {
        var started = new DateTime(2026, 11, 7, 18, 0, 0, DateTimeKind.Utc);
        const long frequency = 10_000;
        const long offset = 100_000;
        var nowQpc = offset + (3 * frequency);

        Assert.Multiple(() =>
        {
            Assert.That(EtwNetworkCollector.IsClockCalibrationValid(started, offset, frequency, nowQpc, started.AddSeconds(3.5)), Is.True);
            Assert.That(EtwNetworkCollector.IsClockCalibrationValid(started, offset, frequency, nowQpc, started.AddSeconds(4.1)), Is.False);
        });
    }

    [TestCase(5.5, 7, true)]
    [TestCase(9.1, 7, false)]
    [TestCase(1.9, 7, false)]
    [TestCase(3.0, 2, false)]
    public void ClockCalibrationGuard_AllowsSamplingDelayButRejectsDriftOutsideBracket(double utcSeconds, int afterSeconds, bool valid)
    {
        var started = new DateTime(2026, 11, 7, 18, 0, 0, DateTimeKind.Utc);
        const long frequency = 10_000;
        const long offset = 100_000;
        Assert.That(EtwNetworkCollector.IsClockCalibrationValid(started, offset, frequency, offset + 3 * frequency,
            started.AddSeconds(utcSeconds), offset + afterSeconds * frequency), Is.EqualTo(valid));
    }
}
