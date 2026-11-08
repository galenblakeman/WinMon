using NUnit.Framework;
using WinMon.NetworkProbe;

namespace WinMon.Tests;

[TestFixture]
public sealed class NetworkLedgerTests
{
    private static readonly DateTime Started = new(2026, 11, 7, 12, 0, 0, DateTimeKind.Utc);

    [Test]
    public void Add_SeparatesTcpUdpIpFamilyAndDirectionForKnownLifetime()
    {
        var ledger = new NetworkLedger();
        Assert.That(ledger.Start(42, 100, Started), Is.True);

        Assert.Multiple(() =>
        {
            Assert.That(ledger.Add(42, Started.AddSeconds(1), true, false, true, 10), Is.True);
            Assert.That(ledger.Add(42, Started.AddSeconds(1), true, true, false, 20), Is.True);
            Assert.That(ledger.Add(42, Started.AddSeconds(1), false, false, true, 30), Is.True);
        });

        Assert.That(ledger.Snapshot(Started.AddSeconds(1)), Is.EquivalentTo(new[]
        {
            new NetworkLedgerRow(42, 100, true, false, true, 10),
            new NetworkLedgerRow(42, 100, true, true, false, 20),
            new NetworkLedgerRow(42, 100, false, false, true, 30)
        }));
    }

    [Test]
    public void Add_DoesNotAssignUnknownOrPreStartEvents()
    {
        var ledger = new NetworkLedger();
        Assert.That(ledger.Add(42, Started, true, false, true, 10), Is.False);
        Assert.That(ledger.Start(42, 100, Started), Is.True);
        Assert.That(ledger.Add(42, Started.AddTicks(-1), true, false, true, 10), Is.False);
        Assert.That(ledger.Add(42, Started, true, false, true, 0), Is.False);
        Assert.Multiple(() =>
        {
            Assert.That(ledger.Snapshot(Started), Is.Empty);
            Assert.That(ledger.UnknownProcessEvents, Is.EqualTo(1));
            Assert.That(ledger.OutOfLifetimeEvents, Is.EqualTo(1));
            Assert.That(ledger.InvalidEvents, Is.EqualTo(1));
        });
    }

    [Test]
    public void Start_RejectsOutOfOrderReuseAndDoesNotMisattributeOldEvent()
    {
        var ledger = new NetworkLedger();
        Assert.That(ledger.Start(42, 100, Started), Is.True);
        Assert.That(ledger.Start(42, 200, Started.AddSeconds(2)), Is.True);
        Assert.That(ledger.Start(42, 300, Started.AddSeconds(1)), Is.False);
        Assert.That(ledger.Add(42, Started.AddSeconds(1), true, false, true, 10), Is.False);
        Assert.That(ledger.Add(42, Started.AddSeconds(3), true, false, true, 20), Is.True);

        Assert.That(ledger.Snapshot(Started.AddSeconds(3)), Is.EquivalentTo(new[]
        {
            new NetworkLedgerRow(42, 100, true, false, true, 0),
            new NetworkLedgerRow(42, 200, true, false, true, 20)
        }.Where(row => row.Bytes > 0)));
    }

    [Test]
    public void Prune_RetainsExitedLifetimeForGraceThenRemovesIt()
    {
        var ledger = new NetworkLedger(exitedGrace: TimeSpan.FromSeconds(10));
        Assert.That(ledger.Start(42, 100, Started), Is.True);
        Assert.That(ledger.Add(42, Started.AddSeconds(1), true, false, true, 10), Is.True);
        Assert.That(ledger.Stop(42, Started.AddSeconds(2)), Is.True);

        Assert.That(ledger.Snapshot(Started.AddSeconds(12)), Has.Count.EqualTo(1));
        ledger.Prune(Started.AddSeconds(12).AddTicks(1));
        Assert.That(ledger.Snapshot(Started.AddSeconds(13)), Is.Empty);
    }

    [Test]
    public void Stop_DoesNotRemoveNewerLifetimeAndOldStartCannotResurrectAfterExit()
    {
        var ledger = new NetworkLedger();
        Assert.That(ledger.Start(42, 100, Started), Is.True);
        Assert.That(ledger.Start(42, 200, Started.AddSeconds(2)), Is.True);
        Assert.That(ledger.Stop(42, Started.AddSeconds(1)), Is.False);
        Assert.That(ledger.Add(42, Started.AddSeconds(3), true, false, true, 20), Is.True);
        Assert.That(ledger.Stop(42, Started.AddSeconds(4)), Is.True);
        Assert.That(ledger.Start(42, 100, Started), Is.False);
    }

    [Test]
    public void Start_AcceptsDifferentProcessesSeededOutOfTimestampOrder()
    {
        var ledger = new NetworkLedger();
        Assert.That(ledger.Start(42, 100, Started.AddSeconds(2)), Is.True);
        Assert.That(ledger.Start(43, 200, Started), Is.True);
    }

    [Test]
    public void Add_RejectsAmbiguousEventExactlyAtReusedPidStart()
    {
        var ledger = new NetworkLedger();
        Assert.That(ledger.Start(42, 100, Started), Is.True);
        var reusedAt = Started.AddSeconds(2);
        Assert.That(ledger.Start(42, 200, reusedAt), Is.True);
        Assert.That(ledger.Add(42, reusedAt, true, false, true, 10), Is.False);
        Assert.That(ledger.Snapshot(reusedAt), Is.Empty);
        Assert.That(ledger.OutOfLifetimeEvents, Is.EqualTo(1));
    }

    [Test]
    public void Start_RefusesNewLifetimeAtCapacityUntilExitedLifetimeExpires()
    {
        var ledger = new NetworkLedger(maximumLifetimes: 1, exitedGrace: TimeSpan.Zero);
        Assert.That(ledger.Start(42, 100, Started), Is.True);
        Assert.That(ledger.Stop(42, Started.AddSeconds(1)), Is.True);
        Assert.That(ledger.Start(43, 200, Started.AddSeconds(1)), Is.False);
        Assert.That(ledger.Start(43, 200, Started.AddSeconds(2)), Is.True);
        Assert.That(ledger.CapacityRejectedStarts, Is.EqualTo(1));
    }
}
