using NUnit.Framework;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using WinMon.Monitoring;
using WinMon.NetworkService;

namespace WinMon.Tests;

[TestFixture]
public sealed class NetworkTrafficLedgerTests
{
    private const long Frequency = 10;
    private const string Owner = "S-1-5-21-owner";

    [Test]
    public void Snapshot_UsesDelayedEventTimeWindowRatherThanArrivalTime()
    {
        var ledger = NewLedger();
        Assert.That(ledger.Register(42, 100, Owner, 0), Is.True);
        Assert.That(ledger.Add(42, 35, 100, false, 500), Is.True);

        var snapshot = ledger.Snapshot(Owner, 100);

        Assert.Multiple(() =>
        {
            Assert.That(snapshot.WindowStartQpc, Is.EqualTo(20));
            Assert.That(snapshot.WindowEndQpc, Is.EqualTo(70));
            Assert.That((snapshot.Quality & ProcessNetworkQuality.Provisional) != 0, Is.True);
            Assert.That((snapshot.Quality & ProcessNetworkQuality.WarmingUp) == 0, Is.True);
            Assert.That(snapshot.LateEvents, Is.EqualTo(1));
            Assert.That(snapshot.Processes.Single().ReceiveBytesPerSecond, Is.EqualTo(100));
        });
    }

    [Test]
    public void Add_AttributesDelayedEventToExitedLifetimeWithoutReusingPid()
    {
        var ledger = NewLedger();
        Assert.That(ledger.Register(42, 100, Owner, 0), Is.True);
        ledger.End(42, 50);
        Assert.That(ledger.Register(42, 200, Owner, 50), Is.True);

        Assert.Multiple(() =>
        {
            Assert.That(ledger.Add(42, 49, 100, true, 10), Is.True);
            Assert.That(ledger.Add(42, 50, 100, true, 10), Is.False);
            Assert.That(ledger.Add(42, 60, 100, true, 20), Is.True);
        });

        var rows = ledger.Snapshot(Owner, 100).Processes.OrderBy(row => row.CreationTicks).ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(rows, Has.Length.EqualTo(2));
            Assert.That(rows[0], Is.EqualTo(new ProcessNetworkReading(42, 100, 0, 10, null, null)));
            Assert.That(rows[1], Is.EqualTo(new ProcessNetworkReading(42, 200, 0, 20, null, null)));
        });
    }

    [Test]
    public void Snapshot_FiltersOtherOwners()
    {
        var ledger = NewLedger();
        Assert.That(ledger.Register(42, 100, Owner, 0), Is.True);
        Assert.That(ledger.Register(43, 200, "S-1-5-21-other", 0), Is.True);
        Assert.That(ledger.Add(42, 30, 100, false, 10), Is.True);
        Assert.That(ledger.Add(43, 30, 100, false, 20), Is.True);

        var snapshot = ledger.Snapshot(Owner, 100);

        Assert.That(snapshot.Processes.Select(row => row.ProcessId), Is.EquivalentTo(new[] { 42 }));
    }

    [Test]
    public void Snapshot_ReportsEndpointPairTrafficWithoutChangingProcessTotals()
    {
        var ledger = NewLedger();
        Assert.That(ledger.Register(42, 100, Owner, 0), Is.True);
        Assert.That(ledger.Add(42, 35, 100, true, 500, TransportProtocol.Tcp,
            "127.0.0.1:4000", "127.0.0.1:443"), Is.True);
        Assert.That(ledger.Add(42, 36, 100, false, 250, TransportProtocol.Tcp,
            "127.0.0.1:4000", "127.0.0.1:443"), Is.True);

        var snapshot = ledger.Snapshot(Owner, 100);

        Assert.Multiple(() =>
        {
            Assert.That(snapshot.Processes.Single(), Is.EqualTo(new ProcessNetworkReading(42, 100, 250, 500, 50, 100)));
            Assert.That(snapshot.Flows.Single(), Is.EqualTo(new ProcessNetworkFlowReading(42, 100,
                TransportProtocol.Tcp, "127.0.0.1:4000", "127.0.0.1:443", 250, 500, 50, 100)));
        });
    }

    [Test]
    public void Snapshot_FiltersFlowsByOwner()
    {
        var ledger = NewLedger();
        Assert.That(ledger.Register(42, 100, Owner, 0), Is.True);
        Assert.That(ledger.Register(43, 200, "S-1-5-21-other", 0), Is.True);
        Assert.That(ledger.Add(42, 30, 100, true, 10, TransportProtocol.Udp, "a:1", "b:2"), Is.True);
        Assert.That(ledger.Add(43, 30, 100, true, 20, TransportProtocol.Udp, "a:3", "b:4"), Is.True);

        var snapshot = ledger.Snapshot(Owner, 100);

        Assert.That(snapshot.Flows.Select(flow => flow.ProcessId), Is.EquivalentTo(new[] { 42 }));
    }

    [Test]
    public void Snapshot_MarksOnlyFlowCoverageWhenEndpointCapacityIsExceeded()
    {
        var ledger = NewLedger();
        Assert.That(ledger.Register(42, 100, Owner, 0), Is.True);
        for (var index = 0; index <= 8192; index++)
            Assert.That(ledger.Add(42, 30, 100, true, 1, TransportProtocol.Udp,
                $"local:{index}", "remote:1"), Is.True);

        var snapshot = ledger.Snapshot(Owner, 100);

        Assert.Multiple(() =>
        {
            Assert.That(snapshot.Processes.Single().SentBytes, Is.EqualTo(8193));
            Assert.That(snapshot.Flows, Has.Count.EqualTo(8192));
            Assert.That(snapshot.FlowQuality.HasFlag(ProcessNetworkQuality.CapacityExceeded), Is.True);
            Assert.That(snapshot.Quality.HasFlag(ProcessNetworkQuality.CapacityExceeded), Is.False);
            Assert.That(JsonSerializer.SerializeToUtf8Bytes(snapshot).Length,
                Is.LessThan(NetworkServiceProtocol.MaximumMessageBytes));
        });
    }

    [Test]
    [Explicit("Records bounded endpoint-flow aggregation overhead for the performance history")]
    [Category("Integration")]
    public void Benchmark_FlowAggregationWritesComparableMeasurements()
    {
        const int events = 40_000;
        _ = Measure(false);
        _ = Measure(true);
        var baseline = new List<Measurement>();
        var flows = new List<Measurement>();
        for (var run = 0; run < 4; run++)
        {
            baseline.Add(Measure(false));
            flows.Add(Measure(true));
        }
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "WinMon.sln"))) root = root.Parent;
        Assert.That(root, Is.Not.Null);
        var directory = Path.Combine(root!.FullName, "artifacts", "network-flow-benchmark");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "ledger.json");
        File.WriteAllText(path, JsonSerializer.Serialize(new { Events = events, Baseline = baseline, Flows = flows },
            new JsonSerializerOptions { WriteIndented = true }));

        Assert.Multiple(() =>
        {
            Assert.That(baseline.All(result => result.Rows == 0), Is.True);
            Assert.That(flows.All(result => result.Rows == 8192), Is.True);
            Assert.That(flows.All(result => result.SnapshotBytes < NetworkServiceProtocol.MaximumMessageBytes), Is.True);
        });

        return;

        static Measurement Measure(bool withFlows)
        {
            var ledger = NewLedger();
            Assert.That(ledger.Register(42, 100, Owner, 0), Is.True);
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            var allocated = GC.GetAllocatedBytesForCurrentThread();
            var watch = Stopwatch.StartNew();
            var accepted = true;
            for (var index = 0; index < events; index++)
                accepted &= ledger.Add(42, 30 + index % 20, 100, index % 2 == 0, 1,
                    withFlows ? TransportProtocol.Udp : null,
                    withFlows ? $"127.0.0.1:{10000 + index % 10000}" : "",
                    withFlows ? "127.0.0.1:443" : "");
            var snapshot = ledger.Snapshot(Owner, 200);
            watch.Stop();
            var wire = Stopwatch.StartNew();
            var fitted = NetworkPipeServer.FitSnapshot(snapshot);
            wire.Stop();
            Assert.That(accepted, Is.True);
            return new Measurement(snapshot.Flows.Count, GC.GetAllocatedBytesForCurrentThread() - allocated,
                watch.Elapsed.TotalMilliseconds, wire.Elapsed.TotalMilliseconds,
                JsonSerializer.SerializeToUtf8Bytes(fitted).Length);
        }
    }

    private sealed record Measurement(int Rows, long AllocatedBytes, double ElapsedMilliseconds,
        double WireMilliseconds, int SnapshotBytes);

    [Test]
    public void Snapshot_KeepsRatesUnavailableUntilLossWindowHasAgedOut()
    {
        var ledger = NewLedger();
        Assert.That(ledger.Register(42, 100, Owner, 0), Is.True);
        ledger.ReportLoss(3, 100);

        var affected = ledger.Snapshot(Owner, 150);
        var recovered = ledger.Snapshot(Owner, 190);

        Assert.Multiple(() =>
        {
            Assert.That((affected.Quality & ProcessNetworkQuality.Incomplete) != 0, Is.True);
            Assert.That(affected.Processes, Is.Empty);
            Assert.That(affected.WindowStartQpc, Is.LessThanOrEqualTo(100));
            Assert.That(recovered.WindowStartQpc, Is.GreaterThan(100));
            Assert.That((recovered.Quality & ProcessNetworkQuality.WarmingUp) == 0, Is.True);
        });
    }

    [Test]
    public void ReportLoss_LowerCounterStartsNewGenerationAndMarksDiscontinuity()
    {
        var ledger = NewLedger();
        ledger.ReportLoss(3, 100);
        var before = ledger.Snapshot(Owner, 150);
        ledger.ReportLoss(1, 160);
        var after = ledger.Snapshot(Owner, 170);

        Assert.Multiple(() =>
        {
            Assert.That(after.Generation, Is.Not.EqualTo(before.Generation));
            Assert.That((after.Quality & ProcessNetworkQuality.Discontinuity) != 0, Is.True);
            Assert.That(after.EventsLost, Is.EqualTo(1));
            Assert.That(after.IncompleteReasons, Is.EqualTo(ProcessNetworkIncompleteReason.NativeLoss));
        });
    }

    [Test]
    public void Snapshot_BackwardsClockStartsNewGenerationAndSuppressesRates()
    {
        var ledger = NewLedger();
        Assert.That(ledger.Register(42, 100, Owner, 0), Is.True);
        Assert.That(ledger.Add(42, 30, 100, false, 100), Is.True);
        var before = ledger.Snapshot(Owner, 100);
        var after = ledger.Snapshot(Owner, 90);

        Assert.Multiple(() =>
        {
            Assert.That(after.Generation, Is.Not.EqualTo(before.Generation));
            Assert.That((after.Quality & ProcessNetworkQuality.Discontinuity) != 0, Is.True);
            Assert.That((after.Quality & ProcessNetworkQuality.WarmingUp) != 0, Is.True);
            Assert.That(after.Processes, Is.Empty);
        });
    }

    [Test]
    public void HasLifetime_AcceptsRetainedExitedLifetimeButExcludesReuseBoundary()
    {
        var ledger = NewLedger();
        Assert.That(ledger.Register(42, 100, Owner, 0), Is.True);
        ledger.End(42, 50);
        Assert.That(ledger.Register(42, 200, Owner, 50), Is.True);

        Assert.Multiple(() =>
        {
            Assert.That(ledger.HasLifetime(42, 49), Is.True);
            Assert.That(ledger.HasExitedLifetime(42, 49), Is.True);
            Assert.That(ledger.HasLifetime(42, 50), Is.False);
            Assert.That(ledger.HasExitedLifetime(42, 50), Is.False);
            Assert.That(ledger.HasLifetime(42, 51), Is.True);
            Assert.That(ledger.HasExitedLifetime(42, 51), Is.False);
        });
    }

    [Test]
    public void Register_ExpiredProcessHistoryRolloverKeepsLiveTrafficRatesAvailable()
    {
        var ledger = new NetworkTrafficLedger(Frequency, 0, 2);
        Assert.That(ledger.Register(42, 100, Owner, 0), Is.True);
        Assert.That(ledger.Register(43, 200, Owner, 1), Is.True);
        ledger.End(43, 2);
        var generation = ledger.Snapshot(Owner, 100).Generation;

        for (var index = 1; index <= 8; index++)
        {
            var now = index * 400L;
            var processId = 43 + index;
            Assert.That(ledger.Register(processId, 200 + index, Owner, now), Is.True);
            ledger.End(processId, now + 1);
            Assert.That(ledger.Add(42, now - 40, now, false, 500), Is.True);
            Assert.That(ledger.Add(42, now - 40, now, true, 200), Is.True);

            var snapshot = ledger.Snapshot(Owner, now + 1);
            Assert.Multiple(() =>
            {
                Assert.That(snapshot.Generation, Is.EqualTo(generation));
                Assert.That(snapshot.Quality, Is.EqualTo(ProcessNetworkQuality.Provisional));
                Assert.That(snapshot.IncompleteReasons, Is.EqualTo(ProcessNetworkIncompleteReason.None));
                Assert.That(snapshot.Processes.Single(), Is.EqualTo(new ProcessNetworkReading(
                    42, 100, index * 500, index * 200, 100, 40)));
            });
        }
    }

    [Test]
    public void Register_CapacityFailureDoesNotReplaceExistingLifetime()
    {
        var ledger = new NetworkTrafficLedger(Frequency, 0, 1);
        Assert.That(ledger.Register(42, 100, Owner, 0), Is.True);
        ledger.End(42, 10);
        Assert.That(ledger.Register(43, 200, Owner, 20), Is.False);
        Assert.That(ledger.Add(42, 5, 30, false, 20), Is.True);

        var snapshot = ledger.Snapshot(Owner, 90);
        Assert.Multiple(() =>
        {
            Assert.That((snapshot.Quality & ProcessNetworkQuality.CapacityExceeded) != 0, Is.True);
            Assert.That((snapshot.Quality & ProcessNetworkQuality.Incomplete) != 0, Is.True);
            Assert.That(snapshot.Quality.HasFlag(ProcessNetworkQuality.WarmingUp), Is.True);
            Assert.That(snapshot.IncompleteReasons, Is.EqualTo(ProcessNetworkIncompleteReason.CapacityExceeded));
            Assert.That(snapshot.Processes.Single().ProcessId, Is.EqualTo(42));
            Assert.That(snapshot.Processes.Single().ReceiveBytesPerSecond, Is.Null);
        });
    }

    [Test]
    public void Add_TooOldEventIsDroppedAndMarksReadingIncomplete()
    {
        var ledger = NewLedger();
        Assert.That(ledger.Register(42, 100, Owner, 0), Is.True);
        Assert.That(ledger.Add(42, 10, 311, false, 20), Is.False);

        var snapshot = ledger.Snapshot(Owner, 320);
        Assert.That((snapshot.Quality & ProcessNetworkQuality.Incomplete) != 0, Is.True);
    }

    [Test]
    public void HasLifetime_ReturnsFalseAfterRetainedLifetimeIsPruned()
    {
        var ledger = NewLedger();
        Assert.That(ledger.Register(42, 100, Owner, 0), Is.True);
        ledger.End(42, 10);

        _ = ledger.Snapshot(Owner, 311);

        Assert.That(ledger.HasLifetime(42, 5), Is.False);
        Assert.That(ledger.HasExitedLifetime(42, 5), Is.False);
    }

    [Test]
    public void HasLifetime_ReturnsFalseAfterClockDiscontinuityResetsGeneration()
    {
        var ledger = NewLedger();
        Assert.That(ledger.Register(42, 100, Owner, 0), Is.True);
        Assert.That(ledger.Add(42, 30, 100, false, 10), Is.True);
        _ = ledger.Snapshot(Owner, 100);

        _ = ledger.Snapshot(Owner, 90);

        Assert.That(ledger.HasLifetime(42, 30), Is.False);
    }

    private static NetworkTrafficLedger NewLedger()
    {
        return new NetworkTrafficLedger(Frequency, 0);
    }

    [Test]
    public void DuplicateRegistration_RecoversRatesAfterFreshWindowButKeepsCoverageReason()
    {
        var ledger = NewLedger();
        Assert.That(ledger.Register(42, 100, Owner, 0), Is.True);
        Assert.That(ledger.Register(42, 100, Owner, 10), Is.False);
        Assert.That(ledger.Add(42, 40, 45, false, 50), Is.True);
        var snapshot = ledger.Snapshot(Owner, 110);
        Assert.Multiple(() =>
        {
            Assert.That(snapshot.Quality.HasFlag(ProcessNetworkQuality.WarmingUp), Is.False);
            Assert.That(snapshot.Quality.HasFlag(ProcessNetworkQuality.Incomplete), Is.True);
            Assert.That(snapshot.IncompleteReasons, Is.EqualTo(ProcessNetworkIncompleteReason.AmbiguousLifetime));
            Assert.That(snapshot.Processes.Single().ReceiveBytesPerSecond, Is.EqualTo(10));
        });
    }
}
