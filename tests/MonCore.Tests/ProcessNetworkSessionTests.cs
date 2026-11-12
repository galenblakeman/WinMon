using NUnit.Framework;
using WinMon.Monitoring;

namespace WinMon.Tests;

[TestFixture]
public sealed class ProcessNetworkSessionTests
{
    private static readonly Guid Generation = Guid.Parse("8e9ed9b0-98a0-4bbb-a3ab-079ec745ae5d");

    private static ProcessNetworkSnapshot Snapshot(long sequence, long timestamp, long received, long sent,
        Guid? generation = null, ProcessNetworkQuality quality = ProcessNetworkQuality.Provisional,
        long windowStart = 0, long windowEnd = 0)
    {
        return new ProcessNetworkSnapshot(generation ?? Generation, sequence, timestamp, 10, windowStart,
            windowEnd, quality, 0, 0, [new ProcessNetworkReading(42, 100, received, sent, 11, 12)]);
    }

    private static ProcessNetworkFlowReading Flow(long received, long sent, double? receiveRate = 11,
        double? sendRate = 12, int port = 443)
    {
        return new ProcessNetworkFlowReading(42, 100, TransportProtocol.Tcp, "127.0.0.1:5000",
            $"127.0.0.1:{port}", received, sent, receiveRate, sendRate);
    }

    [Test]
    public void Add_FlowBaselinesAcrossReconnectAndPublishesServiceWindow()
    {
        var session = new ProcessNetworkSession();
        var firstSubscription = Guid.NewGuid();
        var secondSubscription = Guid.NewGuid();
        var first = session.Add(Snapshot(1, 100, 10, 10, windowStart: 40, windowEnd: 90) with
        {
            SubscriptionId = firstSubscription, FlowTrafficSupported = true, Flows = [Flow(100, 200)]
        }, 100);
        var second = session.Add(Snapshot(2, 110, 20, 20, windowStart: 100, windowEnd: 105) with
        {
            SubscriptionId = firstSubscription, FlowTrafficSupported = true, Flows = [Flow(125, 235)]
        }, 110);
        var reconnected = session.Add(Snapshot(1, 120, 30, 30, windowStart: 110, windowEnd: 115) with
        {
            SubscriptionId = secondSubscription, FlowTrafficSupported = true, Flows = [Flow(500, 800)]
        }, 120);
        var resumed = session.Add(Snapshot(2, 130, 40, 40, windowStart: 120, windowEnd: 125) with
        {
            SubscriptionId = secondSubscription, FlowTrafficSupported = true, Flows = [Flow(510, 820)]
        }, 130);

        Assert.Multiple(() =>
        {
            Assert.That(first.Flows.Single().ReceivedBytes, Is.Zero);
            Assert.That(first.Flows.Single().ReceiveBytesPerSecond, Is.Null);
            Assert.That(second.Flows.Single().ReceivedBytes, Is.EqualTo(25));
            Assert.That(reconnected.Flows.Single().ReceivedBytes, Is.EqualTo(25));
            Assert.That(reconnected.Flows.Single().ReceiveBytesPerSecond, Is.Null);
            Assert.That(resumed.Flows.Single().ReceivedBytes, Is.EqualTo(35));
            Assert.That(resumed.Flows.Single().ReceiveBytesPerSecond, Is.EqualTo(11));
            Assert.That(resumed.WindowStartQpc, Is.EqualTo(120));
            Assert.That(resumed.WindowEndQpc, Is.EqualTo(125));
        });
    }

    [Test]
    public void Add_AbsentFlowRetainsTotalsButClearsItsRate()
    {
        var session = new ProcessNetworkSession();
        session.Add(Snapshot(1, 100, 10, 10, windowStart: 40, windowEnd: 90) with
        {
            FlowTrafficSupported = true, Flows = [Flow(10, 20)]
        }, 100);
        session.Add(Snapshot(2, 110, 20, 20, windowStart: 100, windowEnd: 105) with
        {
            FlowTrafficSupported = true, Flows = [Flow(30, 50)]
        }, 110);
        var absent = session.Add(Snapshot(3, 120, 30, 30, windowStart: 110, windowEnd: 115) with
        {
            FlowTrafficSupported = true
        }, 120);

        Assert.Multiple(() =>
        {
            Assert.That(absent.Flows.Single().ReceivedBytes, Is.EqualTo(20));
            Assert.That(absent.Flows.Single().ReceiveBytesPerSecond, Is.Null);
            Assert.That(absent.CurrentFlows, Is.Empty);
        });
    }

    [Test]
    public void Add_OlderServiceDistinguishesUnsupportedFlowsFromAnObservedEmptyWindow()
    {
        var session = new ProcessNetworkSession();
        var unsupported = session.Add(Snapshot(1, 100, 10, 10, windowStart: 40, windowEnd: 90), 100);
        var supportedEmpty = session.Add(Snapshot(2, 110, 20, 20, windowStart: 100, windowEnd: 105) with
        {
            FlowTrafficSupported = true
        }, 110);

        Assert.Multiple(() =>
        {
            Assert.That(unsupported.FlowTrafficSupported, Is.False);
            Assert.That(unsupported.Flows, Is.Empty);
            Assert.That(supportedEmpty.FlowTrafficSupported, Is.True);
            Assert.That(supportedEmpty.Flows, Is.Empty);
        });
    }

    [Test]
    public void Add_FlowCapacityAndRetentionDoNotSuppressProcessRatesOrReuseAnExpiredBaseline()
    {
        var session = new ProcessNetworkSession();
        var initial = Enumerable.Range(1, 8192).Select(port => Flow(10, 20, port: port)).ToArray();
        session.Add(Snapshot(1, 100, 10, 10, windowStart: 40, windowEnd: 90) with
        {
            FlowTrafficSupported = true, Flows = initial
        }, 100);
        var capacity = session.Add(Snapshot(2, 110, 20, 20, windowStart: 100, windowEnd: 105) with
        {
            FlowTrafficSupported = true,
            Flows = initial.Append(Flow(30, 40, port: 9000)).ToArray()
        }, 110);
        session.Add(Snapshot(3, 421, 30, 30, windowStart: 410, windowEnd: 415) with
        {
            FlowTrafficSupported = true
        }, 421);
        var reappeared = session.Add(Snapshot(4, 430, 40, 40, windowStart: 420, windowEnd: 425) with
        {
            FlowTrafficSupported = true, Flows = [Flow(500, 600, port: 443)]
        }, 430);

        Assert.Multiple(() =>
        {
            Assert.That(capacity.Processes.Single().ReceiveBytesPerSecond, Is.EqualTo(11));
            Assert.That(capacity.FlowQuality.HasFlag(ProcessNetworkQuality.CapacityExceeded), Is.True);
            Assert.That(reappeared.Flows.Single().ReceivedBytes, Is.Zero);
            Assert.That(reappeared.Flows.Single().ReceiveBytesPerSecond, Is.Null);
        });
    }

    [Test]
    public void Add_BaselinesInitialAndNewLifetimesWithoutPreEnableTraffic()
    {
        var session = new ProcessNetworkSession();
        var first = session.Add(Snapshot(1, 100, 1000, 2000, windowStart: 40, windowEnd: 90), 100);
        var second = session.Add(Snapshot(2, 110, 1025, 2035, windowStart: 50, windowEnd: 100), 110);
        var reused = session.Add(new ProcessNetworkSnapshot(Generation, 3, 120, 10, 60, 110,
            ProcessNetworkQuality.Provisional, 0, 0, [new ProcessNetworkReading(42, 101, 500, 700, 1, 2)]), 120);

        Assert.Multiple(() =>
        {
            Assert.That(first.Processes.Single().ReceivedBytes, Is.Zero);
            Assert.That(first.Processes.Single().ReceiveBytesPerSecond, Is.Null);
            Assert.That(second.Processes.Single().ReceivedBytes, Is.EqualTo(25));
            Assert.That(second.Processes.Single().SentBytes, Is.EqualTo(35));
            Assert.That(reused.Processes.Single(reading => reading.CreationTicks == 101).ReceivedBytes, Is.Zero);
        });
    }

    [Test]
    public void Add_DuplicateDoesNotDoubleCountAndRatesComeFromServiceWindow()
    {
        var session = new ProcessNetworkSession();
        session.Add(Snapshot(1, 100, 10, 20, windowStart: 40, windowEnd: 90), 100);
        var changed = session.Add(Snapshot(2, 110, 30, 50, windowStart: 100, windowEnd: 105), 110);
        var duplicate = session.Add(Snapshot(2, 110, 30, 50, windowStart: 100, windowEnd: 105), 111);

        Assert.Multiple(() =>
        {
            Assert.That(changed.Processes.Single().ReceivedBytes, Is.EqualTo(20));
            Assert.That(duplicate.Processes.Single().ReceivedBytes, Is.EqualTo(20));
            Assert.That(duplicate.Processes.Single().ReceiveBytesPerSecond, Is.EqualTo(11));
        });
    }

    [Test]
    public void Add_GenerationGapPreservesTotalsAndWaitsForFreshEventWindow()
    {
        var session = new ProcessNetworkSession();
        session.Add(Snapshot(1, 100, 10, 20, windowStart: 40, windowEnd: 90), 100);
        session.Add(Snapshot(2, 110, 30, 50, windowStart: 50, windowEnd: 100), 110);
        var nextGeneration = Guid.NewGuid();
        var restarted = session.Add(Snapshot(1, 120, 500, 800, nextGeneration, windowStart: 50, windowEnd: 110), 120);
        var fresh = session.Add(Snapshot(2, 130, 510, 820, nextGeneration, windowStart: 120, windowEnd: 125), 130);

        Assert.Multiple(() =>
        {
            Assert.That(restarted.Processes.Single().ReceivedBytes, Is.EqualTo(20));
            Assert.That(restarted.Processes.Single().ReceiveBytesPerSecond, Is.Null);
            Assert.That(restarted.Quality.HasFlag(ProcessNetworkQuality.Incomplete), Is.True);
            Assert.That(fresh.Processes.Single().ReceivedBytes, Is.EqualTo(30));
            Assert.That(fresh.Processes.Single().ReceiveBytesPerSecond, Is.Not.Null);
        });
    }

    [Test]
    public void Add_StaleOrCounterRegressionNeverPublishesLiveRates()
    {
        var session = new ProcessNetworkSession();
        session.Add(Snapshot(1, 100, 100, 100, windowStart: 40, windowEnd: 90), 100);
        var stale = session.Add(Snapshot(2, 110, 120, 120, windowStart: 50, windowEnd: 100), 141);
        var regression = session.Add(Snapshot(3, 120, 1, 1, windowStart: 60, windowEnd: 110), 120);

        Assert.Multiple(() =>
        {
            Assert.That(stale.Processes.Single().ReceiveBytesPerSecond, Is.Null);
            Assert.That(stale.Quality.HasFlag(ProcessNetworkQuality.Incomplete), Is.True);
            Assert.That(regression.Processes.Single().ReceivedBytes, Is.EqualTo(20));
            Assert.That(regression.Processes.Single().ReceiveBytesPerSecond, Is.Null);
        });
    }

    [Test]
    public void Reset_ClearsObservedTotals()
    {
        var session = new ProcessNetworkSession();
        session.Add(Snapshot(1, 100, 100, 100, windowStart: 40, windowEnd: 90), 100);
        session.Add(Snapshot(2, 110, 120, 120, windowStart: 50, windowEnd: 100), 110);
        session.Reset();
        var frame = session.Add(Snapshot(1, 120, 500, 500, windowStart: 60, windowEnd: 110), 120);
        Assert.That(frame.Processes.Single().ReceivedBytes, Is.Zero);
    }

    [Test]
    public void Unavailable_KeepsTotalsButClearsLiveRates()
    {
        var session = new ProcessNetworkSession();
        session.Add(Snapshot(1, 100, 10, 20, windowStart: 40, windowEnd: 90), 100);
        session.Add(Snapshot(2, 110, 30, 50, windowStart: 50, windowEnd: 100), 110);
        var unavailable = session.Unavailable(120, 10);

        Assert.Multiple(() =>
        {
            Assert.That(unavailable.Processes.Single().ReceivedBytes, Is.EqualTo(20));
            Assert.That(unavailable.Processes.Single().ReceiveBytesPerSecond, Is.Null);
            Assert.That(unavailable.Quality.HasFlag(ProcessNetworkQuality.Unavailable), Is.True);
        });
    }

    [Test]
    public void Add_ReconnectRebasesSameGenerationWithoutLosingTotals()
    {
        var session = new ProcessNetworkSession();
        var firstSubscription = Guid.NewGuid();
        var secondSubscription = Guid.NewGuid();
        session.Add(
            Snapshot(1, 100, 100, 100, windowStart: 40, windowEnd: 90) with { SubscriptionId = firstSubscription },
            100);
        session.Add(
            Snapshot(2, 110, 120, 130, windowStart: 50, windowEnd: 100) with { SubscriptionId = firstSubscription },
            110);
        var reconnected = session.Add(Snapshot(1, 120, 500, 800, windowStart: 60, windowEnd: 110) with
        {
            SubscriptionId = secondSubscription
        }, 120);
        var fresh = session.Add(Snapshot(2, 130, 510, 820, windowStart: 120, windowEnd: 125) with
        {
            SubscriptionId = secondSubscription
        }, 130);

        Assert.Multiple(() =>
        {
            Assert.That(reconnected.Processes.Single().ReceivedBytes, Is.EqualTo(20));
            Assert.That(reconnected.Processes.Single().ReceiveBytesPerSecond, Is.Null);
            Assert.That(fresh.Processes.Single().ReceivedBytes, Is.EqualTo(30));
            Assert.That(fresh.Processes.Single().ReceiveBytesPerSecond, Is.Not.Null);
        });
    }

    [Test]
    public void Add_StickyIncompleteDoesNotSuppressKnownFreshServiceRates()
    {
        var session = new ProcessNetworkSession();
        session.Add(Snapshot(1, 100, 10, 10, quality: ProcessNetworkQuality.Incomplete, windowStart: 40, windowEnd: 90),
            100);
        var frame = session.Add(Snapshot(2, 110, 20, 20, quality: ProcessNetworkQuality.Incomplete,
            windowStart: 100, windowEnd: 105), 110);

        Assert.Multiple(() =>
        {
            Assert.That(frame.Quality.HasFlag(ProcessNetworkQuality.Incomplete), Is.True);
            Assert.That(frame.Processes.Single().ReceiveBytesPerSecond, Is.EqualTo(11));
        });
    }

    [Test]
    public void Add_PrunesLiveRatesButRetainsTotalsForAbsentLifetimes()
    {
        var session = new ProcessNetworkSession();
        session.Add(Snapshot(1, 100, 10, 10, windowStart: 40, windowEnd: 90), 100);
        session.Add(Snapshot(2, 110, 20, 20, windowStart: 100, windowEnd: 105), 110);
        var noLongerListed = new ProcessNetworkSnapshot(Generation, 3, 120, 10, 110, 115,
            ProcessNetworkQuality.Provisional, 0, 0, []);
        var frame = session.Add(noLongerListed, 120);

        Assert.Multiple(() =>
        {
            Assert.That(frame.Processes.Single().ReceivedBytes, Is.EqualTo(10));
            Assert.That(frame.Processes.Single().ReceiveBytesPerSecond, Is.Null);
        });

        var duplicate = session.Add(noLongerListed, 121);
        Assert.That(duplicate.Processes.Single().ReceiveBytesPerSecond, Is.Null);
    }

    [Test]
    public void Add_GapAndLossKeepTotalsIncompleteButResumeAfterFreshServiceWindows()
    {
        var session = new ProcessNetworkSession();
        session.Add(Snapshot(1, 100, 10, 10, windowStart: 40, windowEnd: 90), 100);
        var gap = session.Add(Snapshot(3, 120, 30, 30, windowStart: 100, windowEnd: 115), 120);
        var afterGap = session.Add(Snapshot(4, 130, 40, 40, windowStart: 120, windowEnd: 125), 130);
        var loss = session.Add(Snapshot(5, 140, 50, 50, windowStart: 130, windowEnd: 135) with { EventsLost = 1 }, 140);
        var afterLoss = session.Add(Snapshot(6, 150, 60, 60, windowStart: 140, windowEnd: 145) with { EventsLost = 1 },
            150);

        Assert.Multiple(() =>
        {
            Assert.That(gap.Processes.Single().ReceivedBytes, Is.EqualTo(20));
            Assert.That(gap.Processes.Single().ReceiveBytesPerSecond, Is.Null);
            Assert.That(afterGap.Processes.Single().ReceiveBytesPerSecond, Is.Not.Null);
            Assert.That(loss.Processes.Single().ReceiveBytesPerSecond, Is.Null);
            Assert.That(afterLoss.Processes.Single().ReceiveBytesPerSecond, Is.Not.Null);
            Assert.That(afterLoss.Quality.HasFlag(ProcessNetworkQuality.Incomplete), Is.True);
        });
    }

    [Test]
    public void Add_InvalidSnapshotRetainsTotalsAsUnavailable()
    {
        var session = new ProcessNetworkSession();
        session.Add(Snapshot(1, 100, 10, 10, windowStart: 40, windowEnd: 90), 100);
        session.Add(Snapshot(2, 110, 30, 30, windowStart: 100, windowEnd: 105), 110);
        var unavailable = session.Add(Snapshot(3, 120, 40, 40, windowStart: 110, windowEnd: 115) with { Frequency = 0 },
            120);

        Assert.Multiple(() =>
        {
            Assert.That(unavailable.Processes.Single().ReceivedBytes, Is.EqualTo(20));
            Assert.That(unavailable.Processes.Single().ReceiveBytesPerSecond, Is.Null);
            Assert.That(unavailable.Quality.HasFlag(ProcessNetworkQuality.Unavailable), Is.True);
        });
    }

    [Test]
    public void Add_CapacityRejectsNewLifetimeWithoutDiscardingRetainedTotalsAndResetRecovers()
    {
        var session = new ProcessNetworkSession();
        var initial = Enumerable.Range(1, 8192).Select(id => new ProcessNetworkReading(id, id, 0, 0, 1, 1)).ToArray();
        session.Add(
            new ProcessNetworkSnapshot(Generation, 1, 100, 10, 40, 90, ProcessNetworkQuality.Provisional, 0, 0,
                initial), 100);
        session.Add(new ProcessNetworkSnapshot(Generation, 2, 110, 10, 100, 105, ProcessNetworkQuality.Provisional, 0,
            0,
            [new ProcessNetworkReading(1, 1, 10, 0, 1, 1)]), 110);
        var rejected = session.Add(new ProcessNetworkSnapshot(Generation, 3, 120, 10, 110, 115,
            ProcessNetworkQuality.Provisional, 0, 0,
            [new ProcessNetworkReading(8193, 8193, 1, 1, 1, 1)]), 120);
        session.Reset();
        var afterReset = session.Add(new ProcessNetworkSnapshot(Generation, 1, 130, 10, 120, 125,
            ProcessNetworkQuality.Provisional, 0, 0, [new ProcessNetworkReading(8193, 8193, 1, 1, 1, 1)]), 130);

        Assert.Multiple(() =>
        {
            Assert.That(rejected.Processes.Single(reading => reading.ProcessId == 1).ReceivedBytes, Is.EqualTo(10));
            Assert.That(rejected.Processes.Any(reading => reading.ProcessId == 8193), Is.False);
            Assert.That(rejected.Quality.HasFlag(ProcessNetworkQuality.CapacityExceeded), Is.True);
            Assert.That(rejected.Quality.HasFlag(ProcessNetworkQuality.Incomplete), Is.True);
            Assert.That(afterReset.Processes.Single().ProcessId, Is.EqualTo(8193));
        });
    }

    [Test]
    public void Add_ClippedSessionTotalIsIncompleteAndCapacityExceeded()
    {
        var session = new ProcessNetworkSession();
        session.Add(Snapshot(1, 100, 0, 0, windowStart: 40, windowEnd: 90), 100);
        session.Add(Snapshot(2, 110, long.MaxValue, 0, windowStart: 100, windowEnd: 105), 110);
        var nextGeneration = Guid.NewGuid();
        session.Add(Snapshot(1, 120, 0, 0, nextGeneration, windowStart: 110, windowEnd: 115), 120);
        var clipped = session.Add(Snapshot(2, 130, 1, 0, nextGeneration, windowStart: 120, windowEnd: 125), 130);

        Assert.Multiple(() =>
        {
            Assert.That(clipped.Processes.Single().ReceivedBytes, Is.EqualTo(long.MaxValue));
            Assert.That(clipped.Quality.HasFlag(ProcessNetworkQuality.Incomplete), Is.True);
            Assert.That(clipped.Quality.HasFlag(ProcessNetworkQuality.CapacityExceeded), Is.True);
        });
    }

    [Test]
    public void Add_ZeroLengthWindowDoesNotPublishAServiceRate()
    {
        var session = new ProcessNetworkSession();
        session.Add(Snapshot(1, 100, 0, 0, windowStart: 40, windowEnd: 90), 100);
        var frame = session.Add(Snapshot(2, 110, 10, 10, windowStart: 110, windowEnd: 110), 110);

        Assert.Multiple(() =>
        {
            Assert.That(frame.Processes.Single().ReceiveBytesPerSecond, Is.Null);
            Assert.That(frame.Quality.HasFlag(ProcessNetworkQuality.Incomplete), Is.True);
        });
    }
}
