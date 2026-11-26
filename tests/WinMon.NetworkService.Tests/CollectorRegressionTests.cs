using System.Net;
using MonCore.Monitoring;
using NUnit.Framework;
using static WinMon.NetworkService.Tests.CollectorRuntimeFixture;

namespace WinMon.NetworkService.Tests;

[TestFixture]
public sealed class CollectorRegressionTests
{
    [TestCase("missing")]
    [TestCase("reconcile-reuse")]
    [TestCase("packet-reuse")]
    public async Task InferredExitDoesNotAttributeUnverifiedPacketsToThePreviousAccount(string path)
    {
        await using var f = new CollectorRuntimeFixture();
        Assert.That(f.Register(), Is.True); // Owner verified through second 3.
        f.Native.CreationRead = _ => path == "missing" ? null : Birth(7);
        f.Native.OwnerRead = pid => path == "missing" ? null : new(pid, Birth(7), Other);
        if (path == "packet-reuse")
            Assert.That(f.Collector.ResolveAndRegister(42, Tick(8), Tick(10), true), Is.True);
        else
            f.Reconcile();

        // Lifecycle events for an intervening account's process (seconds 4-6) were missed.
        f.Queue.Network(42, Tick(2), 11, false, TransportProtocol.Tcp,
            IPAddress.Parse("192.0.2.1"), 123, IPAddress.Parse("198.51.100.1"), 456);
        f.Queue.Network(42, Tick(3), 13, false, TransportProtocol.Tcp,
            IPAddress.Parse("192.0.2.1"), 123, IPAddress.Parse("198.51.100.2"), 456);
        f.Queue.Network(42, Tick(5), 17, false, TransportProtocol.Tcp,
            IPAddress.Parse("192.0.2.1"), 123, IPAddress.Parse("198.51.100.3"), 456);
        Assert.That(f.Queue.Batch(), Is.False);

        var snapshot = f.Collector.Snapshot(Owner);
        Assert.Multiple(() =>
        {
            Assert.That(snapshot.Processes.Single().CreationTicks, Is.EqualTo(Birth(1)));
            Assert.That(snapshot.Processes.Single().ReceivedBytes, Is.EqualTo(11));
            Assert.That(snapshot.Flows.Select(flow => flow.RemoteEndpoint), Is.EqualTo(new[] { "198.51.100.1:456" }));
            Assert.That(snapshot.IncompleteReasons.HasFlag(ProcessNetworkIncompleteReason.AmbiguousLifetime), Is.True);
        });

        if (path != "missing")
        {
            f.Clock.At(12); // Expire rejection backoff before observing the verified successor.
            f.Queue.Network(42, Tick(8), 19, true);
            Assert.That(f.Queue.Batch(), Is.False);
            var successor = f.Collector.Snapshot(Other).Processes.Single();
            Assert.That((successor.CreationTicks, successor.SentBytes), Is.EqualTo((Birth(7), 19L)));
        }
    }

    [Test]
    public async Task ExplicitStopRetainsDelayedPacketsWithinTheObservedLifetime()
    {
        await using var f = new CollectorRuntimeFixture();
        Assert.That(f.Register(), Is.True);
        f.Queue.Lifecycle(false, 42, Tick(6));
        f.Queue.Network(42, Tick(5), 23, false);
        Assert.That(f.Queue.Batch(), Is.False);
        Assert.That(f.Collector.Snapshot(Owner).Processes.Single().ReceivedBytes, Is.EqualTo(23));
        Assert.That(f.Native.Reads, Is.EqualTo(new[] { "owner:42" }));
    }

    [Test]
    public async Task BusyQueueReconcilesBeforeTheNextBatch()
    {
        await using var f = new CollectorRuntimeFixture();
        Assert.That(f.Register(), Is.True);
        for (var index = 0; index < 8193; index++) f.Queue.Lifecycle(false, 99, Tick(8));
        var remainingAtReconciliation = new List<int>();
        f.Clock.OnUtc = () => remainingAtReconciliation.Add(f.Queue.Count);
        Assert.That(f.Queue.Complete(), Is.True);

        await f.Queue.Consume().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Multiple(() =>
        {
            Assert.That(remainingAtReconciliation, Is.EqualTo(new[] { 4097 }));
            Assert.That(f.State.Cached(42), Is.EqualTo(new CachedOwner(Birth(1), Tick(1), Tick(10))));
            Assert.That(f.Queue.Count, Is.Zero);
            Assert.That(f.Collector.Failure, Is.EqualTo(ProcessNetworkFailure.None));
        });
    }

    [Test]
    public async Task CancellationStopsAtTheNextBatchBoundary()
    {
        await using var f = new CollectorRuntimeFixture();
        for (var index = 0; index < 8193; index++) f.Queue.Lifecycle(false, 99, Tick(8));
        var reads = 0;
        f.Clock.OnTimestamp = () => { if (++reads == 2) f.Queue.Cancel(); };

        await f.Queue.Consume().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.That(f.Queue.Count, Is.EqualTo(4097));
        Assert.That(f.Collector.Failure, Is.EqualTo(ProcessNetworkFailure.None));
    }
}
