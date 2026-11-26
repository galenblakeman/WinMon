using MonCore.Monitoring;
using NUnit.Framework;

namespace MonCore.Tests;

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

    [TestCase(false)]
    [TestCase(true)]
    public void Add_OneFlowsBaselineDoesNotSuppressOtherFlows(bool resetCounter)
    {
        var session = new ProcessNetworkSession();
        session.Add(Snapshot(1, 100, 10, 10, windowStart: 40, windowEnd: 90) with
        { FlowTrafficSupported = true, Flows = [Flow(100, 200)] }, 100);
        session.Add(Snapshot(2, 110, 20, 20, windowStart: 100, windowEnd: 105) with
        { FlowTrafficSupported = true, Flows = [Flow(125, 235)] }, 110);
        var frame = session.Add(Snapshot(3, 120, 30, 30, windowStart: 110, windowEnd: 115) with
        {
            FlowTrafficSupported = true,
            Flows = resetCounter ? [Flow(1, 2), Flow(50, 60, port: 444)] : [Flow(150, 270), Flow(50, 60, port: 444)]
        }, 120);
        var establishedPort = resetCounter ? 444 : 443;
        if (resetCounter)
        {
            // Make both flows established, then reset only one counter.
            session.Add(Snapshot(4, 130, 40, 40, windowStart: 120, windowEnd: 125) with
            { FlowTrafficSupported = true, Flows = [Flow(10, 20), Flow(60, 70, port: 444)] }, 130);
            frame = session.Add(Snapshot(5, 140, 50, 50, windowStart: 130, windowEnd: 135) with
            { FlowTrafficSupported = true, Flows = [Flow(1, 2), Flow(70, 80, port: 444)] }, 140);
        }
        Assert.Multiple(() =>
        {
            Assert.That(frame.Flows.Single(f => f.RemoteEndpoint.EndsWith(":" + establishedPort, StringComparison.Ordinal)).ReceiveBytesPerSecond, Is.EqualTo(11));
            Assert.That(frame.Flows.Single(f => !f.RemoteEndpoint.EndsWith(":" + establishedPort, StringComparison.Ordinal)).ReceiveBytesPerSecond, Is.Null);
            Assert.That(frame.Processes.Single().ReceiveBytesPerSecond, Is.EqualTo(11));
            Assert.That(frame.FlowQuality.HasFlag(ProcessNetworkQuality.Incomplete), Is.True);
        });
        var replaySequence = resetCounter ? 5 : 3;
        var replayTimestamp = resetCounter ? 140 : 120;
        var replay = session.Add(Snapshot(replaySequence, replayTimestamp, 50, 50,
            windowStart: replayTimestamp - 10, windowEnd: replayTimestamp - 5) with
        { FlowTrafficSupported = true, Flows = [Flow(1, 2), Flow(70, 80, port: 444)] }, replayTimestamp);
        Assert.That(replay.FlowQuality.HasFlag(ProcessNetworkQuality.Incomplete), Is.True);
        Assert.That(replay.Flows.Single(f => f.RemoteEndpoint.EndsWith(":" + establishedPort, StringComparison.Ordinal)).ReceiveBytesPerSecond, Is.EqualTo(11));
        var timestamp = resetCounter ? 150 : 130;
        var resumed = session.Add(Snapshot(resetCounter ? 6 : 4, timestamp, 60, 60,
            windowStart: timestamp - 10, windowEnd: timestamp - 5) with
        { FlowTrafficSupported = true, Flows = [Flow(300, 500), Flow(300, 500, port: 444)] }, timestamp);
        Assert.That(resumed.Flows.Select(f => f.ReceiveBytesPerSecond), Is.All.EqualTo(11));
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

    [TestCase("frequency", false)]
    [TestCase("timestamp", false)]
    [TestCase("window-start", false)]
    [TestCase("window-order", false)]
    [TestCase("future-window", false)]
    [TestCase("processes", false)]
    [TestCase("frequency", true)]
    [TestCase("timestamp", true)]
    [TestCase("window-start", true)]
    [TestCase("window-order", true)]
    [TestCase("future-window", true)]
    [TestCase("processes", true)]
    public void Add_InvalidHeaderUsesAvailableClockAndPreservesRecovery(string field, bool seeded)
    {
        var session = new ProcessNetworkSession();
        if (seeded)
        {
            session.Add(Snapshot(1, 100, 10, 20), 100);
            session.Add(Snapshot(2, 110, 30, 50, windowStart: 100, windowEnd: 105), 110);
        }
        var incoming = Snapshot(3, 120, 999, 999, windowStart: 110, windowEnd: 115);
        incoming = field switch
        {
            "frequency" => incoming with { Frequency = 0 },
            "timestamp" => incoming with { TimestampQpc = -1 },
            "window-start" => incoming with { WindowStartQpc = -1 },
            "window-order" => incoming with { WindowEndQpc = 109 },
            "future-window" => incoming with { WindowEndQpc = 121 },
            "processes" => incoming with { Processes = null! },
            _ => throw new ArgumentOutOfRangeException(nameof(field))
        };
        var invalid = session.Add(incoming, 120);
        var recovered = session.Add(Snapshot(3, 130, 40, 60, windowStart: 120, windowEnd: 125), 130);
        Assert.Multiple(() =>
        {
            Assert.That(invalid.Frequency, Is.EqualTo(field == "frequency" && !seeded ? 1 : 10));
            Assert.That(invalid.Quality, Is.EqualTo(ProcessNetworkQuality.Unavailable | ProcessNetworkQuality.Incomplete));
            Assert.That(invalid.Processes.Select(p => p.ReceivedBytes), Is.EqualTo(seeded ? new long[] { 20 } : []));
            Assert.That(invalid.Processes.All(p => p.ReceiveBytesPerSecond is null && p.SendBytesPerSecond is null), Is.True);
            Assert.That(recovered.Processes.Single().ReceivedBytes, Is.EqualTo(seeded ? 30 : 0));
            Assert.That(recovered.Processes.Single().ReceiveBytesPerSecond, Is.EqualTo(seeded ? 11 : (double?)null));
        });
    }

    [TestCase("null")]
    [TestCase("pid-zero")]
    [TestCase("pid-negative")]
    [TestCase("creation-zero")]
    [TestCase("creation-negative")]
    [TestCase("received-negative")]
    [TestCase("sent-negative")]
    public void Add_InvalidProcessReadingDoesNotDiscardOtherReadings(string field)
    {
        var session = new ProcessNetworkSession();
        session.Add(Snapshot(1, 100, 10, 20), 100);
        var reading = new ProcessNetworkReading(50, 200, 100, 200, 1, 2);
        var invalid = field switch
        {
            "null" => null!,
            "pid-zero" => reading with { ProcessId = 0 },
            "pid-negative" => reading with { ProcessId = -1 },
            "creation-zero" => reading with { CreationTicks = 0 },
            "creation-negative" => reading with { CreationTicks = -1 },
            "received-negative" => reading with { ReceivedBytes = -1 },
            "sent-negative" => reading with { SentBytes = -1 },
            _ => throw new ArgumentOutOfRangeException(nameof(field))
        };
        var good = Snapshot(2, 110, 30, 50, windowStart: 100, windowEnd: 105);
        var result = session.Add(good with { Processes = [invalid, good.Processes[0]] }, 110);
        Assert.Multiple(() =>
        {
            Assert.That(result.Processes.Single(), Is.EqualTo(new ProcessNetworkReading(42, 100, 20, 30, 11, 12)));
            Assert.That(result.CurrentLifetimes, Has.Count.EqualTo(1));
            Assert.That(result.Quality, Is.EqualTo(ProcessNetworkQuality.Provisional | ProcessNetworkQuality.Incomplete));
        });
    }

    [TestCase("older-timestamp")]
    [TestCase("older-sequence")]
    [TestCase("duplicate")]
    public void Add_ReplayDoesNotCommitCountersOrStreamIdentity(string kind)
    {
        var session = new ProcessNetworkSession();
        var subscription = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
        ProcessNetworkSnapshot WithFlow(long sequence, long timestamp, long received) =>
            Snapshot(sequence, timestamp, received, received, windowStart: timestamp - 10, windowEnd: timestamp - 5) with
            { SubscriptionId = subscription, FlowTrafficSupported = true, Flows = [Flow(received, received)] };
        session.Add(WithFlow(1, 100, 10), 100);
        session.Add(WithFlow(2, 110, 30), 110);
        var replay = kind == "older-timestamp"
            ? WithFlow(99, 109, 999) with { Generation = Guid.Empty, SubscriptionId = Guid.Empty }
            : WithFlow(kind == "duplicate" ? 2 : 1, 120, 999);
        var ignored = session.Add(replay, 120);
        var next = session.Add(WithFlow(3, 130, 40), 130);
        Assert.Multiple(() =>
        {
            Assert.That(ignored.Processes.Single().ReceivedBytes, Is.EqualTo(20));
            Assert.That(ignored.Flows.Single().ReceivedBytes, Is.EqualTo(20));
            Assert.That(ignored.Processes.Single().ReceiveBytesPerSecond, Is.EqualTo(kind == "duplicate" ? 11 : (double?)null));
            Assert.That(ignored.CurrentLifetimes, Has.Count.EqualTo(kind == "duplicate" ? 1 : 0));
            Assert.That(next.Processes.Single().ReceivedBytes, Is.EqualTo(30));
            Assert.That(next.Flows.Single().ReceivedBytes, Is.EqualTo(30));
            Assert.That(next.Processes.Single().ReceiveBytesPerSecond, Is.EqualTo(11));
            Assert.That(next.Quality, Is.EqualTo(ProcessNetworkQuality.Provisional));
        });
    }

    [TestCase("clock")]
    [TestCase("discontinuity")]
    [TestCase("dropped")]
    [TestCase("loss-decreased")]
    public void Add_InterruptionRetainsTotalsAndRecoversAtTheExactWindowBoundary(string kind)
    {
        var session = new ProcessNetworkSession();
        var lost = kind == "loss-decreased" ? 1 : 0;
        session.Add(Snapshot(1, 100, 10, 20) with { EventsLost = lost }, 100);
        session.Add(Snapshot(2, 110, 30, 50, windowStart: 100, windowEnd: 105) with { EventsLost = lost }, 110);
        var transition = Snapshot(3, 120, 40, 60, windowStart: 110, windowEnd: 115);
        transition = kind switch
        {
            "clock" => transition with { Frequency = 20 },
            "discontinuity" => transition with { Quality = ProcessNetworkQuality.Provisional | ProcessNetworkQuality.Discontinuity },
            "dropped" => transition with { DroppedEvents = 1 },
            "loss-decreased" => transition,
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
        var interrupted = session.Add(transition, 120);
        var resumed = session.Add(transition with { Sequence = 4, TimestampQpc = 130, WindowStartQpc = 120,
            WindowEndQpc = 125, Processes = [new(42, 100, 50, 70, 11, 12)] }, 130);
        Assert.Multiple(() =>
        {
            Assert.That(interrupted.Processes.Single().ReceivedBytes, Is.EqualTo(30));
            Assert.That(interrupted.Processes.Single().ReceiveBytesPerSecond, Is.Null);
            Assert.That(resumed.Processes.Single().ReceivedBytes, Is.EqualTo(40));
            Assert.That(resumed.Processes.Single().ReceiveBytesPerSecond, Is.EqualTo(11));
            Assert.That(resumed.Quality.HasFlag(ProcessNetworkQuality.Incomplete), Is.True);
        });
    }

    [Test]
    public void Add_EmptySubscriptionDoesNotRebaseKnownCounters()
    {
        var session = new ProcessNetworkSession();
        session.Add(Snapshot(1, 100, 10, 20), 100);
        session.Add(Snapshot(2, 110, 30, 50, windowStart: 100, windowEnd: 105) with { SubscriptionId = Generation }, 110);
        var frame = session.Add(Snapshot(3, 120, 40, 60, windowStart: 110, windowEnd: 115), 120);
        Assert.That(frame.Processes.Single(), Is.EqualTo(new ProcessNetworkReading(42, 100, 30, 40, 11, 12)));
        Assert.That(frame.Quality, Is.EqualTo(ProcessNetworkQuality.Provisional));
    }

    [Test]
    public void Add_RejectsNullAndNegativeArrivalWithoutChangingTheBaseline()
    {
        var session = new ProcessNetworkSession();
        session.Add(Snapshot(1, 100, 10, 20), 100);
        Assert.That(() => session.Add(null!, 100), Throws.ArgumentNullException.With.Property("ParamName").EqualTo("snapshot"));
        Assert.That(() => session.Add(Snapshot(2, 110, 999, 999), -1),
            Throws.TypeOf<ArgumentOutOfRangeException>().With.Property("ParamName").EqualTo("nowQpc"));
        var result = session.Add(Snapshot(2, 110, 30, 50, windowStart: 100, windowEnd: 105), 110);
        Assert.That(result.Processes.Single(), Is.EqualTo(new ProcessNetworkReading(42, 100, 20, 30, 11, 12)));
    }

    [TestCase("null")]
    [TestCase("pid")]
    [TestCase("creation")]
    [TestCase("protocol")]
    [TestCase("local")]
    [TestCase("remote")]
    [TestCase("received")]
    [TestCase("sent")]
    public void Add_InvalidFlowDoesNotSuppressValidProcessOrFlowRates(string field)
    {
        var session = new ProcessNetworkSession();
        session.Add(Snapshot(1, 100, 10, 20) with { FlowTrafficSupported = true, Flows = [Flow(100, 200)] }, 100);
        var bad = Flow(100, 200, port: 999);
        bad = field switch
        {
            "null" => null!,
            "pid" => bad with { ProcessId = 0 },
            "creation" => bad with { CreationTicks = 0 },
            "protocol" => bad with { Protocol = (TransportProtocol)999 },
            "local" => bad with { LocalEndpoint = "" },
            "remote" => bad with { RemoteEndpoint = "" },
            "received" => bad with { ReceivedBytes = -1 },
            "sent" => bad with { SentBytes = -1 },
            _ => throw new ArgumentOutOfRangeException(nameof(field))
        };
        var frame = session.Add(Snapshot(2, 110, 30, 50, windowStart: 100, windowEnd: 105) with
            { FlowTrafficSupported = true, Flows = [bad, Flow(125, 235)] }, 110);
        var next = session.Add(Snapshot(3, 120, 40, 60, windowStart: 110, windowEnd: 115) with
            { FlowTrafficSupported = true, Flows = [Flow(135, 255)] }, 120);
        Assert.Multiple(() =>
        {
            Assert.That(frame.Flows.Single(), Is.EqualTo(Flow(25, 35)));
            Assert.That(frame.Processes.Single().ReceiveBytesPerSecond, Is.EqualTo(11));
            Assert.That(frame.Quality, Is.EqualTo(ProcessNetworkQuality.Provisional));
            Assert.That(frame.FlowQuality, Is.EqualTo(ProcessNetworkQuality.Incomplete));
            // Current identities validate endpoints, not cumulative counters.
            Assert.That(frame.CurrentFlows, Has.Count.EqualTo(field is "received" or "sent" ? 2 : 1));
            Assert.That(next.Flows.Single(), Is.EqualTo(Flow(35, 55)));
            Assert.That(next.FlowQuality, Is.EqualTo(ProcessNetworkQuality.None));
        });
    }

    [TestCase(true)]
    [TestCase(false)]
    public void Add_NullFlowEndpointPreservesExistingExceptionAndRecovery(bool local)
    {
        var session = new ProcessNetworkSession();
        session.Add(Snapshot(1, 100, 10, 20) with { FlowTrafficSupported = true, Flows = [Flow(100, 200)] }, 100);
        var bad = local ? Flow(125, 235) with { LocalEndpoint = null! } : Flow(125, 235) with { RemoteEndpoint = null! };
        var snapshot = Snapshot(2, 110, 30, 50, windowStart: 100, windowEnd: 105) with
            { FlowTrafficSupported = true, Flows = [bad] };
        Assert.That(() => session.Add(snapshot, 110), Throws.TypeOf<NullReferenceException>());
        var recovered = session.Add(snapshot with { Flows = [Flow(125, 235)] }, 110);
        Assert.That(recovered.Flows.Single(), Is.EqualTo(Flow(25, 35)));
        Assert.That(recovered.Processes.Single(), Is.EqualTo(new ProcessNetworkReading(42, 100, 20, 30, 11, 12)));
    }

    [TestCase(true)]
    [TestCase(false)]
    public void Add_FlowCounterRegressionRebasesBothDirectionsWithoutPoisoningProcessQuality(bool received)
    {
        var session = new ProcessNetworkSession();
        session.Add(Snapshot(1, 100, 10, 20) with { FlowTrafficSupported = true, Flows = [Flow(100, 200)] }, 100);
        session.Add(Snapshot(2, 110, 30, 50, windowStart: 100, windowEnd: 105) with
            { FlowTrafficSupported = true, Flows = [Flow(125, 235)] }, 110);
        var baseline = received ? Flow(1, 250) : Flow(140, 1);
        var regressed = session.Add(Snapshot(3, 120, 40, 60, windowStart: 110, windowEnd: 115) with
            { FlowTrafficSupported = true, Flows = [baseline] }, 120);
        var resumed = session.Add(Snapshot(4, 130, 50, 70, windowStart: 120, windowEnd: 125) with
            { FlowTrafficSupported = true, Flows = [baseline with
                { ReceivedBytes = baseline.ReceivedBytes + 10, SentBytes = baseline.SentBytes + 20 }] }, 130);
        Assert.Multiple(() =>
        {
            Assert.That(regressed.Flows.Single(), Is.EqualTo(Flow(25, 35, null, null)));
            Assert.That(regressed.FlowQuality, Is.EqualTo(ProcessNetworkQuality.Incomplete));
            Assert.That(regressed.Quality, Is.EqualTo(ProcessNetworkQuality.Provisional));
            Assert.That(regressed.Processes.Single().ReceiveBytesPerSecond, Is.EqualTo(11));
            Assert.That(resumed.Flows.Single(), Is.EqualTo(Flow(35, 55)));
            Assert.That(resumed.FlowQuality, Is.EqualTo(ProcessNetworkQuality.None));
        });
    }

    [TestCase(true)]
    [TestCase(false)]
    public void Add_FlowTotalsSaturateAcrossGenerationsWithoutSuppressingFreshRates(bool received)
    {
        var session = new ProcessNetworkSession();
        var maximum = received ? Flow(long.MaxValue, 0) : Flow(0, long.MaxValue);
        session.Add(Snapshot(1, 100, 10, 20) with { FlowTrafficSupported = true, Flows = [Flow(0, 0)] }, 100);
        session.Add(Snapshot(2, 110, 30, 50, windowStart: 100, windowEnd: 105) with
            { FlowTrafficSupported = true, Flows = [maximum] }, 110);
        session.Add(Snapshot(1, 120, 40, 60, Guid.Empty, windowStart: 110, windowEnd: 115) with
            { FlowTrafficSupported = true, Flows = [Flow(0, 0)] }, 120);
        var clipped = session.Add(Snapshot(2, 130, 50, 70, Guid.Empty, windowStart: 120, windowEnd: 125) with
            { FlowTrafficSupported = true, Flows = [received ? Flow(1, 0) : Flow(0, 1)] }, 130);
        Assert.That(clipped.Flows.Single(), Is.EqualTo(maximum));
        Assert.That(clipped.FlowQuality, Is.EqualTo(ProcessNetworkQuality.Incomplete | ProcessNetworkQuality.CapacityExceeded));
    }

    [TestCase(410, 400)]
    [TestCase(411, 0)]
    public void Add_FlowRetentionKeepsTheExactBoundaryAndExpiresOnlyAfterIt(long absentAt, long expectedTotal)
    {
        var session = new ProcessNetworkSession();
        session.Add(Snapshot(1, 100, 10, 20) with { FlowTrafficSupported = true, Flows = [Flow(100, 200)] }, 100);
        session.Add(Snapshot(2, 110, 30, 50, windowStart: 100, windowEnd: 105) with
            { FlowTrafficSupported = true, Flows = [Flow(125, 235)] }, 110);
        var absent = session.Add(Snapshot(3, absentAt, 40, 60, windowStart: absentAt - 10, windowEnd: absentAt - 5) with
            { FlowTrafficSupported = true }, absentAt);
        var resumed = session.Add(Snapshot(4, 420, 50, 70, windowStart: 410, windowEnd: 415) with
            { FlowTrafficSupported = true, Flows = [Flow(500, 600)] }, 420);
        Assert.That(absent.Flows, Has.Count.EqualTo(absentAt == 410 ? 1 : 0));
        Assert.That(resumed.Flows.Single().ReceivedBytes, Is.EqualTo(expectedTotal));
        Assert.That(resumed.Flows.Single().ReceiveBytesPerSecond, Is.EqualTo(absentAt == 410 ? 11 : (double?)null));
    }

    [TestCase(true)]
    [TestCase(false)]
    public void Add_MissingOrUnsupportedFlowsDoNotExpireCachedBaselines(bool supported)
    {
        var session = new ProcessNetworkSession();
        session.Add(Snapshot(1, 100, 10, 20) with { FlowTrafficSupported = true, Flows = [Flow(100, 200)] }, 100);
        var missing = session.Add(Snapshot(2, 411, 30, 50, windowStart: 400, windowEnd: 405) with
            { FlowTrafficSupported = supported, Flows = null! }, 411);
        var resumed = session.Add(Snapshot(3, 420, 40, 60, windowStart: 410, windowEnd: 415) with
            { FlowTrafficSupported = true, Flows = [Flow(500, 600)] }, 420);
        Assert.That(missing.Flows, Is.Empty);
        Assert.That(missing.CurrentFlows, Is.Empty);
        Assert.That(resumed.Flows.Single(), Is.EqualTo(Flow(400, 400)));
    }

    [TestCase(ProcessNetworkQuality.None, ProcessNetworkQuality.None, true, true)]
    [TestCase(ProcessNetworkQuality.Unavailable, ProcessNetworkQuality.None, false, false)]
    [TestCase(ProcessNetworkQuality.WarmingUp, ProcessNetworkQuality.None, false, false)]
    [TestCase(ProcessNetworkQuality.Incomplete, ProcessNetworkQuality.None, true, true)]
    [TestCase(ProcessNetworkQuality.CapacityExceeded, ProcessNetworkQuality.None, true, true)]
    [TestCase(ProcessNetworkQuality.Discontinuity, ProcessNetworkQuality.None, true, true)]
    [TestCase(ProcessNetworkQuality.None, ProcessNetworkQuality.Unavailable, true, false)]
    [TestCase(ProcessNetworkQuality.None, ProcessNetworkQuality.WarmingUp, true, false)]
    [TestCase(ProcessNetworkQuality.None, ProcessNetworkQuality.Incomplete, true, true)]
    [TestCase(ProcessNetworkQuality.None, ProcessNetworkQuality.CapacityExceeded, true, true)]
    public void Add_ProcessAndFlowQualityGateRatesIndependently(ProcessNetworkQuality processFlag,
        ProcessNetworkQuality flowFlag, bool processRates, bool flowRates)
    {
        var session = new ProcessNetworkSession();
        var source = Snapshot(1, 100, 10, 20, quality: ProcessNetworkQuality.Provisional | processFlag) with
            { FlowTrafficSupported = true, FlowQuality = flowFlag, Flows = [Flow(100, 200)] };
        session.Add(source, 100);
        var frame = session.Add(source with { Sequence = 2, TimestampQpc = 110, WindowStartQpc = 100, WindowEndQpc = 105,
            Processes = [new(42, 100, 30, 50, 11, 12)], Flows = [Flow(125, 235)] }, 110);
        Assert.Multiple(() =>
        {
            Assert.That(frame.Processes.Single(), Is.EqualTo(new ProcessNetworkReading(42, 100, 20, 30,
                processRates ? 11 : null, processRates ? 12 : null)));
            Assert.That(frame.Flows.Single(), Is.EqualTo(Flow(25, 35, flowRates ? 11 : null, flowRates ? 12 : null)));
            Assert.That(frame.FlowQuality, Is.EqualTo(flowFlag | (flowRates ? ProcessNetworkQuality.None : ProcessNetworkQuality.Incomplete)));
            Assert.That(frame.CurrentLifetimes, Has.Count.EqualTo(1));
            Assert.That(frame.CurrentFlows, Has.Count.EqualTo(1));
        });
    }

    [TestCase(109, false)]
    [TestCase(140, true)]
    [TestCase(141, false)]
    public void Add_StalenessUsesTheInclusiveServiceClockBoundaryWithoutPoisoningRecovery(long now, bool live)
    {
        var session = new ProcessNetworkSession();
        session.Add(Snapshot(1, 100, 10, 20) with { FlowTrafficSupported = true, Flows = [Flow(100, 200)] }, 100);
        var frame = session.Add(Snapshot(2, 110, 30, 50, windowStart: 100, windowEnd: 105) with
            { FlowTrafficSupported = true, Flows = [Flow(125, 235)] }, now);
        var next = session.Add(Snapshot(3, 150, 40, 60, windowStart: 140, windowEnd: 145) with
            { FlowTrafficSupported = true, Flows = [Flow(135, 255)] }, 150);
        Assert.Multiple(() =>
        {
            Assert.That(frame.Processes.Single().ReceiveBytesPerSecond, Is.EqualTo(live ? 11 : (double?)null));
            Assert.That(frame.Flows.Single(), Is.EqualTo(Flow(25, 35, live ? 11 : null, live ? 12 : null)));
            Assert.That(frame.Quality, Is.EqualTo(live ? ProcessNetworkQuality.Provisional :
                ProcessNetworkQuality.Provisional | ProcessNetworkQuality.Unavailable | ProcessNetworkQuality.Incomplete));
            Assert.That(frame.FlowQuality, Is.EqualTo(live ? ProcessNetworkQuality.None :
                ProcessNetworkQuality.Unavailable | ProcessNetworkQuality.Incomplete));
            Assert.That(next.FlowQuality, Is.EqualTo(ProcessNetworkQuality.None));
            Assert.That(next.Quality, Is.EqualTo(ProcessNetworkQuality.Provisional));
            Assert.That(next.Flows.Single(), Is.EqualTo(Flow(35, 55)));
        });
    }

    [Test]
    public void Add_UnsupportedFlowsHideTheirQualityWithoutSuppressingProcessRates()
    {
        var session = new ProcessNetworkSession();
        session.Add(Snapshot(1, 100, 10, 20), 100);
        var frame = session.Add(Snapshot(2, 110, 30, 50, windowStart: 100, windowEnd: 105) with
            { FlowQuality = ProcessNetworkQuality.Unavailable | ProcessNetworkQuality.WarmingUp }, 110);
        Assert.That(frame.Processes.Single().ReceiveBytesPerSecond, Is.EqualTo(11));
        Assert.That(frame.FlowQuality, Is.EqualTo(ProcessNetworkQuality.None));
        Assert.That(frame.FlowTrafficSupported, Is.False);
        Assert.That(frame.Flows, Is.Empty);
    }

    [Test]
    public void Add_OverflowingFlowRetentionClockPreservesExistingException()
    {
        var session = new ProcessNetworkSession();
        var source = Snapshot(1, 100, 10, 20) with { Frequency = long.MaxValue, FlowTrafficSupported = true };
        Assert.That(() => session.Add(source, 100), Throws.TypeOf<OverflowException>());
    }
}
