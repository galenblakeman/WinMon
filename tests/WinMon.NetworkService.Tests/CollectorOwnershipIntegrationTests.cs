using System.Diagnostics;
using System.Security.Principal;
using NUnit.Framework;
using WinMon.Monitoring;
using WinMon.NetworkService;

namespace WinMon.Tests;

[TestFixture]
[Category("Integration")]
public sealed class CollectorOwnershipIntegrationTests
{
    [TestCase(-1, false)]
    [TestCase(0, false)]
    [TestCase(1, true)]
    public async Task DefaultProbeUsesCurrentLifetimeAndRequiresEventsStrictlyAfterCreation(int delta, bool expected)
    {
        using var process = Process.GetCurrentProcess();
        using var identity = WindowsIdentity.GetCurrent();
        var sid = identity.User!.Value;
        var creation = process.StartTime.ToUniversalTime();
        var created = 100 + Stopwatch.Frequency;
        var eventQpc = created + delta;
        var verified = created + 10;
        await using var collector = new EtwNetworkCollector([sid]);
        var state = new CollectorOwnershipState(collector, creation.AddSeconds(-1));

        Assert.That(collector.ResolveAndRegister(process.Id, eventQpc, verified, true), Is.EqualTo(expected));
        Assert.Multiple(() =>
        {
            Assert.That(state.Cached(process.Id), Is.EqualTo(expected
                ? new CachedOwner(creation.ToFileTimeUtc(), created, verified) : null));
            Assert.That(state.Rejected(process.Id), Is.EqualTo(expected
                ? null : new RejectedOwner(verified + Stopwatch.Frequency, false, 0)));
            Assert.That(collector.CoverageReasons, Is.EqualTo(ProcessNetworkIncompleteReason.None));
        });
        if (!expected) return;
        Assert.That(state.Ledger.Add(process.Id, eventQpc, verified, false, 20), Is.True);
        var row = state.Ledger.Snapshot(sid, verified).Processes.Single();
        Assert.Multiple(() =>
        {
            Assert.That(row.ProcessId, Is.EqualTo(process.Id));
            Assert.That(row.CreationTicks, Is.EqualTo(creation.ToFileTimeUtc()));
            Assert.That(row.ReceivedBytes, Is.EqualTo(20));
            Assert.That(row.SentBytes, Is.Zero);
        });
    }

    [Test]
    public async Task DefaultProbeRejectsUnprovisionedOwnerWithoutMarkingMissingTraffic()
    {
        using var process = Process.GetCurrentProcess();
        var creation = process.StartTime.ToUniversalTime();
        var now = 100 + Stopwatch.Frequency + 10;
        await using var collector = new EtwNetworkCollector(["S-1-5-21-not-provisioned"]);
        var state = new CollectorOwnershipState(collector, creation.AddSeconds(-1));
        Assert.That(collector.ResolveAndRegister(process.Id, now, now, true), Is.False);
        Assert.Multiple(() =>
        {
            Assert.That(state.OwnerCount, Is.Zero);
            Assert.That(state.Rejected(process.Id), Is.EqualTo(new RejectedOwner(now + Stopwatch.Frequency, false, creation.ToFileTimeUtc())));
            Assert.That(collector.CoverageReasons, Is.EqualTo(ProcessNetworkIncompleteReason.None));
            Assert.That(state.RateGap, Is.Zero);
            Assert.That(state.Ledger.Add(process.Id, now, now, false, 20), Is.False);
        });
    }

    [Test]
    public async Task DefaultProbeClearsOldRejectionAndRefreshesTheKnownLifetime()
    {
        using var process = Process.GetCurrentProcess();
        using var identity = WindowsIdentity.GetCurrent();
        var sid = identity.User!.Value;
        var creation = process.StartTime.ToUniversalTime();
        var created = 100 + Stopwatch.Frequency;
        var first = created + 10;
        await using var collector = new EtwNetworkCollector([sid]);
        Assert.That(collector.ResolveAndRegister(process.Id, first, first, false), Is.False);
        var state = new CollectorOwnershipState(collector, creation.AddSeconds(-1));
        Assert.That(state.Rejected(process.Id), Is.EqualTo(new RejectedOwner(first + Stopwatch.Frequency, true, 0)));
        Assert.That(collector.ResolveAndRegister(process.Id, first, first, false), Is.True);
        Assert.That(collector.ResolveAndRegister(process.Id, first + 1, first + 5, true), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(state.OwnerCount, Is.EqualTo(1));
            Assert.That(state.RejectedCount, Is.Zero);
            Assert.That(state.Cached(process.Id), Is.EqualTo(new CachedOwner(creation.ToFileTimeUtc(), created, first + 5)));
            Assert.That(collector.CoverageReasons, Is.EqualTo(ProcessNetworkIncompleteReason.None));
        });
    }
}
