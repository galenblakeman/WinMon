using System.Collections;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using MonCore.Monitoring;
using NUnit.Framework;

namespace MonCore.Tests;

[TestFixture]
public sealed class MonitorSessionTests
{
    private const int Processes = 60;

    [Test]
    public async Task QueuedNetworkRatesUseCollectionTimeAndRejectAlreadyStaleSnapshots()
    {
        var generation = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var sequence = 0;
        var harness = new Harness(() =>
        {
            var stamp = Stopwatch.GetTimestamp();
            var number = ++sequence;
            if (number == 4) stamp -= Stopwatch.Frequency * 10;
            return new SamplingOptions
            {
                ProcessNetworkSessionId = sessionId,
                ProcessNetwork = number == 2 ? null : new ProcessNetworkSnapshot(generation, number == 1 ? 1 : number - 1, stamp, Stopwatch.Frequency,
                    stamp - 1, stamp, ProcessNetworkQuality.Provisional, 0, 0,
                    [new(42, 100, number * 100, 0, 20, 40)])
            };
        }, Frame(1), Frame(2), Frame(3), Frame(4));
        await harness.RunToEnd();
        await Task.Delay(TimeSpan.FromSeconds(4));
        harness.Drain();

        var captures = harness.Session.Connections.Captures;
        Assert.That(captures[1].Network!.Processes.Single().ReceiveBytesPerSecond, Is.Null);
        Assert.That(captures[2].Network!.Processes.Single().ReceiveBytesPerSecond, Is.EqualTo(20));
        Assert.That(captures[2].Network!.Quality.HasFlag(ProcessNetworkQuality.Unavailable), Is.False);
        Assert.That(captures[3].Network!.Processes.Single().ReceiveBytesPerSecond, Is.Null);
        Assert.That(captures[3].Network!.Quality.HasFlag(ProcessNetworkQuality.Unavailable), Is.True);
    }

    [Test]
    public async Task NetworkTotalsSurviveMissingSnapshotAndCpuCaptureFailure()
    {
        var id = Guid.NewGuid();
        var generation = Guid.NewGuid();
        var raw = new[] { Network(generation, 1, 100), Network(generation, 2, 150) };
        var read = 0;
        var harness = new Harness(
            () => new SamplingOptions
                { ProcessNetwork = read < raw.Length ? raw[read++] : null, ProcessNetworkSessionId = id },
            Frame(1), _ => throw new InvalidOperationException("CPU unavailable"), Frame(3));
        await harness.RunToEnd();
        harness.Drain();
        var network = harness.Session.ProcessNetwork!;
        Assert.Multiple(() =>
        {
            Assert.That(network.Processes.Single().ReceivedBytes, Is.EqualTo(50));
            Assert.That(network.Processes.Single().ReceiveBytesPerSecond, Is.Null);
            Assert.That(network.Quality.HasFlag(ProcessNetworkQuality.Unavailable), Is.True);
        });
    }

    [Test]
    public async Task ExplicitNewNetworkSessionStartsFreshTotals()
    {
        var oldSession = Guid.NewGuid();
        var newSession = Guid.NewGuid();
        var generation = Guid.NewGuid();
        var read = 0;
        var harness = new Harness(() =>
        {
            var number = ++read;
            return new SamplingOptions
            {
                ProcessNetwork = Network(generation, number, number * 100),
                ProcessNetworkSessionId = number < 3 ? oldSession : newSession
            };
        }, Frame(1), Frame(2), Frame(3));
        await harness.RunToEnd();
        harness.Drain();
        Assert.That(harness.Session.ProcessNetwork!.Processes.Single().ReceivedBytes, Is.Zero);
    }

    [Test]
    public async Task ResourceHistoryClockRebasingDoesNotShiftNetworkClock()
    {
        var snapshot = Network(Guid.NewGuid(), 1, 100);
        var harness =
            new Harness(new SamplingOptions { ProcessNetwork = snapshot, ProcessNetworkSessionId = Guid.NewGuid() },
                Frame(1));
        harness.Session.SetHistoryClockOffset(Stopwatch.Frequency * 100);
        await harness.RunToEnd();
        harness.Drain();
        Assert.Multiple(() =>
        {
            Assert.That(harness.Session.ProcessNetwork!.TimestampQpc, Is.EqualTo(snapshot.TimestampQpc));
            Assert.That(harness.Session.ProcessNetwork.Quality.HasFlag(ProcessNetworkQuality.Unavailable), Is.False);
            Assert.That(harness.Session.LatestFull!.MonotonicTimestamp, Is.EqualTo(Stopwatch.Frequency * 101));
        });
    }

    [Test]
    public async Task ConnectionsHistoryAlignsWithRebasedResourceSampleAndKeepsAccountedTraffic()
    {
        var network = Network(Guid.NewGuid(), 1, 100);
        var owner = new ProcessUsage(42, 100, "browser", 0) { ImagePath = @"C:\\Apps\\browser.exe" };
        var sample = Frame(1)(new SamplingOptions()).Full with { Processes = [owner] };
        var endpoint = new NetworkConnection(42, 100, TransportProtocol.Tcp,
            new IPEndPoint(IPAddress.Loopback, 4200), new IPEndPoint(IPAddress.Loopback, 443));
        var harness = new Harness(
            new SamplingOptions { ProcessNetwork = network, ProcessNetworkSessionId = Guid.NewGuid() },
            _ => new CapturedFrame(sample)
            {
                Connections = new NetworkConnectionsFrame([endpoint],
                    NetworkAddressFamilyStatus.Success, NetworkAddressFamilyStatus.Success)
            });
        harness.Session.SetHistoryClockOffset(Stopwatch.Frequency * 10);

        await harness.RunToEnd();
        harness.Drain();

        var capture = harness.Session.Connections.Find(Stopwatch.Frequency * 11);
        Assert.Multiple(() =>
        {
            Assert.That(capture, Is.Not.Null);
            Assert.That(capture!.Connections!.Connections, Is.EqualTo(new[] { endpoint }));
            Assert.That(capture.Owners.Single().Identity, Is.EqualTo(owner.Identity));
            Assert.That(capture.Network!.TimestampQpc, Is.EqualTo(network.TimestampQpc));
            Assert.That(harness.Session.Connections.Find(Stopwatch.Frequency), Is.Null);
        });
    }

    [Test]
    public async Task ConnectionsHistoryDoesNotInventAnObservationWhenTheSourceHasNone()
    {
        var harness = new Harness(Frame(1));
        await harness.RunToEnd();
        harness.Drain();
        Assert.That(harness.Session.Connections.Find(Stopwatch.Frequency), Is.Not.Null);
        Assert.That(harness.Session.Connections.Find(Stopwatch.Frequency)!.Available, Is.False);
    }

    [Test]
    public async Task NetworkRetainsExitedOwnersMetadataWithoutMakingThemLiveProcesses()
    {
        var owner = new ProcessUsage(42, 100, "browser", 0) { ImagePath = @"C:\Apps\browser.exe" };
        var generation = Guid.NewGuid();
        var id = Guid.NewGuid();
        var reads = 0;
        var harness = new Harness(
            () => new SamplingOptions
                { ProcessNetwork = Network(generation, ++reads, reads * 100), ProcessNetworkSessionId = id },
            options => new CapturedFrame(Frame(1)(options).Full with { Processes = [owner] }),
            options => new CapturedFrame(Frame(2)(options).Full with { Processes = [] }));
        await harness.RunToEnd();
        harness.Drain();
        Assert.Multiple(() =>
        {
            Assert.That(harness.Session.LatestFull!.Processes, Is.Empty);
            Assert.That(harness.Session.ProcessNetwork!.Owners.Single().Identity, Is.EqualTo(owner.Identity));
            Assert.That(harness.Session.ProcessNetwork.Owners.Single().ImagePath, Is.EqualTo(owner.ImagePath));
            Assert.That(harness.Session.ProcessNetwork.Processes.Single().ReceivedBytes, Is.EqualTo(100));
        });
    }

    [Test]
    public async Task OptionalNetworkFailureDoesNotDiscardOrdinaryCapture()
    {
        var harness = new Harness(new SamplingOptions
        {
            ProcessNetwork = Network(Guid.NewGuid(), 1, 100) with { Processes = new FailingNetworkRows() },
            ProcessNetworkSessionId = Guid.NewGuid()
        }, Frame(1));
        await harness.RunToEnd();
        harness.Drain();
        Assert.Multiple(() =>
        {
            Assert.That(harness.Session.HasLiveSample, Is.True);
            Assert.That(harness.Session.Error, Is.Null);
            Assert.That(harness.Session.History.Samples, Has.Count.EqualTo(1));
            Assert.That(harness.Session.ProcessNetwork!.Quality.HasFlag(ProcessNetworkQuality.Unavailable), Is.True);
        });
    }

    private sealed class FailingNetworkRows : IReadOnlyList<ProcessNetworkReading>
    {
        public int Count => throw new InvalidOperationException("Network provider failed.");
        public ProcessNetworkReading this[int index] => throw new InvalidOperationException();

        public IEnumerator<ProcessNetworkReading> GetEnumerator()
        {
            throw new InvalidOperationException();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }

    private static ProcessNetworkSnapshot Network(Guid generation, long sequence, long total)
    {
        var stamp = Stopwatch.GetTimestamp();
        return new ProcessNetworkSnapshot(generation, sequence, stamp, Stopwatch.Frequency,
            stamp - Stopwatch.Frequency * 8, stamp - Stopwatch.Frequency * 3, ProcessNetworkQuality.Provisional,
            0, 0, [new ProcessNetworkReading(42, 100, total, total * 2, 20, 40)]);
    }

    [Test]
    public async Task Drain_RecordsQueuedCapturesInOrderWithOneCallback()
    {
        var harness = new Harness(Frame(1), Frame(2), Frame(3));

        await harness.RunToEnd();

        Assert.That(harness.Posted, Has.Count.EqualTo(1), "Captures queued behind one pending drain.");
        harness.Drain();
        var session = harness.Session;
        Assert.Multiple(() =>
        {
            Assert.That(session.History.Samples.Select(s => s.Timestamp.ToUnixTimeSeconds()),
                Is.EqualTo(new[] { 1, 2, 3 }));
            Assert.That(harness.RecordedCount, Is.EqualTo(1));
            Assert.That(session.Latest, Is.SameAs(session.History.Latest));
            Assert.That(session.LatestFull!.Processes, Has.Count.EqualTo(Processes),
                "Live ranking uses the full sample.");
            Assert.That(session.Latest!.Processes, Has.Count.LessThan(Processes),
                "History keeps the compacted sample.");
            Assert.That(session.Services.Frames, Has.Count.EqualTo(3));
            Assert.That(session.Error, Is.Null);
            Assert.That(harness.Source.Disposed, Is.True);
        });
    }

    [Test]
    public async Task FailedCapture_ResetsSourceAndReportsReadingsUnavailable()
    {
        var harness = new Harness(Frame(1), _ => throw new InvalidOperationException("Native read failed."));

        await harness.RunToEnd();
        harness.Drain();

        Assert.Multiple(() =>
        {
            Assert.That(harness.Session.Error, Is.EqualTo(SamplingError.ReadingsUnavailable));
            Assert.That(harness.Source.Resets, Is.EqualTo(1));
            Assert.That(harness.Session.History.Samples, Has.Count.EqualTo(1));
            Assert.That(harness.RecordedCount, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task SourceThatCannotStart_IsReportedAndRetried()
    {
        var harness = new Harness { FailedCreates = 1 };

        await harness.RunToEnd();
        harness.Drain();

        Assert.Multiple(() =>
        {
            Assert.That(harness.Session.Error, Is.EqualTo(SamplingError.ReadingsUnavailable));
            Assert.That(harness.Creates, Is.EqualTo(2), "The next tick creates the source again.");
            Assert.That(harness.RecordedCount, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task SourceThatCannotReset_IsReplaced()
    {
        var harness = new Harness(_ => throw new InvalidOperationException("Native read failed."), Frame(2));
        harness.Source.FailReset = true;

        await harness.RunToEnd();
        harness.Drain();

        Assert.Multiple(() =>
        {
            Assert.That(harness.Creates, Is.EqualTo(2));
            Assert.That(harness.Session.Error, Is.Null);
            Assert.That(harness.Session.History.Samples, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public async Task CapturesStayOnOneDedicatedThreadWithElevatedPriority()
    {
        var threads = new List<int>();
        var poolThreads = new List<bool>();
        var priorities = new List<ThreadPriority>();
        CapturedFrame Capture(SamplingOptions options)
        {
            threads.Add(Environment.CurrentManagedThreadId);
            poolThreads.Add(Thread.CurrentThread.IsThreadPoolThread);
            priorities.Add(Thread.CurrentThread.Priority);
            return Frame(threads.Count)(options);
        }

        var harness = new Harness(Capture, Capture, Capture);
        await harness.RunToEnd();
        harness.Drain();
        Assert.Multiple(() =>
        {
            Assert.That(threads, Has.Count.EqualTo(3));
            Assert.That(threads.Distinct().Count(), Is.EqualTo(1));
            Assert.That(poolThreads, Is.All.False);
            if (OperatingSystem.IsWindows())
                Assert.That(priorities, Is.All.EqualTo(ThreadPriority.AboveNormal));
            Assert.That(harness.Source.Disposed, Is.True);
            Assert.That(harness.Session.History.Samples, Has.Count.EqualTo(3));
        });
    }

    [Test]
    public async Task CancellationInterruptsTheWaitBetweenCaptures()
    {
        using var cancellation = new CancellationTokenSource();
        using var captured = new ManualResetEventSlim();
        Thread? worker = null;
        var source = new FakeSource([options =>
        {
            worker = Thread.CurrentThread;
            captured.Set();
            return Frame(1)(options);
        }], cancellation);
        var session = new MonitorSession(() => source, () => new(), _ => { }, TimeSpan.FromMinutes(1));
        var run = session.Run(cancellation.Token);
        try
        {
            Assert.That(captured.Wait(TimeSpan.FromSeconds(5)), Is.True);
            Assert.That(SpinWait.SpinUntil(() =>
                (worker!.ThreadState & System.Threading.ThreadState.WaitSleepJoin) != 0,
                TimeSpan.FromSeconds(5)), Is.True, "Sampling did not enter its wait.");
        }
        finally
        {
            cancellation.Cancel();
            await run.WaitAsync(TimeSpan.FromSeconds(5));
        }

        Assert.That(source.Disposed, Is.True);
        Assert.That(source.Options, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task SlowCaptureDoesNotAccumulateCatchUpTicks()
    {
        using var cancellation = new CancellationTokenSource();
        var moments = new List<long>();
        CapturedFrame Capture(SamplingOptions options)
        {
            moments.Add(Stopwatch.GetTimestamp());
            if (moments.Count == 1) Thread.Sleep(150);
            return Frame(moments.Count)(options);
        }

        var source = new FakeSource([Capture, Capture, Capture], cancellation);
        var session = new MonitorSession(() => source, () => new(), _ => { }, TimeSpan.FromMilliseconds(50));
        await session.Run(cancellation.Token).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.That(moments, Has.Count.EqualTo(3));
        Assert.That(Stopwatch.GetElapsedTime(moments[1], moments[2]).TotalMilliseconds,
            Is.GreaterThanOrEqualTo(40), "Missed deadlines caused a burst of catch-up captures.");
    }

    [Test]
    public void Run_CancelledBeforeItStarts_CompletesWithoutThrowing()
    {
        var harness = new Harness(Frame(1));
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        Assert.That(async () => await harness.Session.Run(cancelled.Token).WaitAsync(TimeSpan.FromSeconds(10)),
            Throws.Nothing);
    }

    [Test]
    public async Task SuccessfulCapture_ClearsAnEarlierFailure()
    {
        var harness = new Harness(_ => throw new InvalidOperationException("Native read failed."), Frame(2));

        await harness.RunToEnd();
        harness.Drain();

        Assert.Multiple(() =>
        {
            Assert.That(harness.Session.Error, Is.Null);
            Assert.That(harness.Session.History.Samples, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public async Task PartialFailure_RecordsTheSampleWithItsError()
    {
        var harness = new Harness(options => Frame(1)(options) with { Error = SamplingError.CpuUnavailable });

        await harness.RunToEnd();
        harness.Drain();

        Assert.Multiple(() =>
        {
            Assert.That(harness.Session.Error, Is.EqualTo(SamplingError.CpuUnavailable));
            Assert.That(harness.Session.History.Samples, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public async Task Close_DropsQueuedCapturesWithoutRaisingRecorded()
    {
        var harness = new Harness(Frame(1), Frame(2));

        await harness.RunToEnd();
        harness.Session.Close();
        harness.Drain();

        Assert.Multiple(() =>
        {
            Assert.That(harness.Session.History.Samples, Is.Empty);
            Assert.That(harness.Session.Latest, Is.Null);
            Assert.That(harness.RecordedCount, Is.Zero);
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task CancellationDuringCapture_DisposesSourceAndCloseDiscardsTheLateFrame(bool close)
    {
        using var cancellation = new CancellationTokenSource();
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var posted = new ConcurrentQueue<Action>();
        var source = new FakeSource([options =>
        {
            entered.SetResult();
            if (!release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("Capture was not released.");
            return Frame(1)(options);
        }], cancellation);
        var session = new MonitorSession(() => source, () => new SamplingOptions(), posted.Enqueue);
        var recorded = 0;
        session.Recorded += () => recorded++;
        var run = session.Run(cancellation.Token);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            if (close) session.Close();
            cancellation.Cancel();
        }
        finally
        {
            cancellation.Cancel();
            release.Set();
            await run.WaitAsync(TimeSpan.FromSeconds(3));
        }

        while (posted.TryDequeue(out var drain)) drain();
        Assert.Multiple(() =>
        {
            Assert.That(source.Disposed, Is.True);
            Assert.That(session.History.Samples, Has.Count.EqualTo(close ? 0 : 1));
            Assert.That(recorded, Is.EqualTo(close ? 0 : 1));
            Assert.That(session.Error, Is.Null);
        });
    }

    [Test]
    public async Task Options_AreReadForEachCaptureAndApplied()
    {
        var options = new SamplingOptions("C:", "D:", process => process.ProcessId == 0);
        var harness = new Harness(options, Frame(1), Frame(2));

        await harness.RunToEnd();
        harness.Drain();

        Assert.Multiple(() =>
        {
            Assert.That(harness.OptionReads, Is.EqualTo(3), "Two captures and the read that ended the run.");
            Assert.That(harness.Source.Options, Has.All.SameAs(options));
            Assert.That(harness.Session.Latest!.Processes.Select(p => p.ProcessId), Does.Contain(0));
            Assert.That(harness.Session.Latest.Processes.Select(p => p.ProcessId), Does.Not.Contain(1));
        });
    }

    [Test]
    public async Task RetainAll_KeepsEveryProcessInHistory()
    {
        var harness = new Harness(new SamplingOptions(RetainAll: true), Frame(1));

        await harness.RunToEnd();
        harness.Drain();

        Assert.That(harness.Session.Latest!.Processes, Has.Count.EqualTo(Processes));
    }

    [Test]
    public void Record_ReusesUnchangedConnectionOwnersAcrossCaptures()
    {
        var harness = new Harness();
        var owner = new ProcessUsage(42, 100, "browser", 5) { ImagePath = @"C:\Appsrowser.exe" };
        var endpoint = new NetworkConnection(42, 100, TransportProtocol.Tcp,
            new IPEndPoint(IPAddress.Loopback, 4200), new IPEndPoint(IPAddress.Loopback, 443));
        var connections = new NetworkConnectionsFrame([endpoint],
            NetworkAddressFamilyStatus.Success, NetworkAddressFamilyStatus.Success);
        var full = Frame(1)(new SamplingOptions()).Full;

        foreach (var (second, process) in new[]
                 {
                     (1, owner), (2, owner with { CpuPercent = 9 }), (3, owner with { AppName = "Browser" })
                 })
        {
            var sample = full with
            {
                Processes = [process], Timestamp = full.Timestamp.AddSeconds(second),
                MonotonicTimestamp = Stopwatch.Frequency * second
            };
            harness.Session.Record(sample, sample, connections: connections);
        }

        var owners = harness.Session.Connections.Captures.Select(capture => capture.Owners.Single()).ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(owners, Has.Length.EqualTo(3));
            Assert.That(owners[0].CpuPercent, Is.Zero);
            Assert.That(owners[1], Is.SameAs(owners[0]));
            Assert.That(owners[2], Is.Not.SameAs(owners[1]));
            Assert.That(owners[2].AppName, Is.EqualTo("Browser"));
        });
    }

    [Test]
    public void Record_AddsAFrameWithoutRaisingRecorded()
    {
        var harness = new Harness();
        var full = Frame(1)(new SamplingOptions()).Full;
        var compacted = ProcessRetention.Compact(full);

        harness.Session.Record(compacted, full, new ServiceFrame(full, []));

        Assert.Multiple(() =>
        {
            Assert.That(harness.Session.Latest, Is.SameAs(compacted));
            Assert.That(harness.Session.LatestFull, Is.SameAs(full));
            Assert.That(harness.Session.History.Samples, Has.Count.EqualTo(1));
            Assert.That(harness.Session.Services.Frames, Has.Count.EqualTo(1));
            Assert.That(harness.RecordedCount, Is.Zero);
            Assert.That(harness.Posted, Is.Empty);
        });
    }

    [Test]
    public async Task HasLiveSample_IsSetByADrainButNotByARestoredSample()
    {
        var harness = new Harness(Frame(2));
        harness.Session.Record(ProcessRetention.Compact(Frame(1)(new SamplingOptions()).Full));
        var restoredOnly = harness.Session.HasLiveSample;

        await harness.RunToEnd();
        harness.Drain();

        Assert.Multiple(() =>
        {
            Assert.That(restoredOnly, Is.False,
                "A restored sample sets Latest but cannot prove a new version healthy.");
            Assert.That(harness.Session.HasLiveSample, Is.True);
        });
    }

    [Test]
    public async Task LiveSamples_UseTheRestoredHistoryClockOffset()
    {
        var harness = new Harness(Frame(2));
        harness.Session.SetHistoryClockOffset(Stopwatch.Frequency * 600);
        MonitorSample? observed = null;
        harness.Session.LiveSampleRecorded += sample => observed = sample;

        await harness.RunToEnd();
        harness.Drain();

        Assert.Multiple(() =>
        {
            Assert.That(harness.Session.Latest!.MonotonicTimestamp, Is.EqualTo(Stopwatch.Frequency * 602));
            Assert.That(harness.Session.LatestFull!.MonotonicTimestamp,
                Is.EqualTo(harness.Session.Latest.MonotonicTimestamp));
            Assert.That(harness.Session.Services.Frames[0].Sample.MonotonicTimestamp,
                Is.EqualTo(harness.Session.Latest.MonotonicTimestamp));
            Assert.That(observed!.MonotonicTimestamp, Is.EqualTo(harness.Session.Latest.MonotonicTimestamp));
        });
    }

    [Test]
    public async Task HasLiveSample_StaysFalseWhenTheFirstCaptureFails()
    {
        var harness = new Harness(_ => throw new InvalidOperationException("Native read failed."));
        harness.Session.Record(ProcessRetention.Compact(Frame(1)(new SamplingOptions()).Full));

        await harness.RunToEnd();
        harness.Drain();

        Assert.Multiple(() =>
        {
            Assert.That(harness.Session.Latest, Is.Not.Null);
            Assert.That(harness.Session.HasLiveSample, Is.False);
        });
    }

    // Error names, since the enum is internal and NUnit test methods are public.
    [TestCase(null, true)]
    [TestCase(nameof(SamplingError.CpuUnsupported), true)]
    [TestCase(nameof(SamplingError.CpuUnavailable), false)]
    public async Task IsHealthy_AcceptsOnlyCpuThatThisMachineCannotReport(string? error, bool healthy)
    {
        // Without this, an update to a machine with several processor groups would always roll back.
        SamplingError? sampled = error is null ? null : Enum.Parse<SamplingError>(error);
        var harness = new Harness(options => Frame(1)(options) with { Error = sampled });

        await harness.RunToEnd();
        harness.Drain();

        Assert.That(harness.Session.IsHealthy, Is.EqualTo(healthy));
    }

    [Test]
    public async Task IsHealthy_NeedsALiveCapture()
    {
        var harness = new Harness(_ => throw new InvalidOperationException("Native read failed."));
        harness.Session.Record(ProcessRetention.Compact(Frame(1)(new SamplingOptions()).Full));

        await harness.RunToEnd();
        harness.Drain();

        Assert.That(harness.Session.IsHealthy, Is.False, "Restored history and a failed capture prove nothing.");
    }

    [Test]
    public async Task LiveSampleRecorded_RaisesOncePerDrainedFrameInOrder()
    {
        var harness = new Harness(Frame(1), Frame(2), Frame(3));
        var seen = new List<long>();
        harness.Session.LiveSampleRecorded += full => seen.Add(full.Timestamp.ToUnixTimeSeconds());
        harness.Session.Record(ProcessRetention.Compact(Frame(9)(new SamplingOptions()).Full));

        await harness.RunToEnd();
        harness.Drain();

        Assert.That(seen, Is.EqualTo(new long[] { 1, 2, 3 }), "One drain of three frames; restored samples excluded.");
    }

    [Test]
    public void Record_UsagePeriodsUseFullSamplesAndNeverInventRestoredObservations()
    {
        var session = new MonitorSession(() => throw new InvalidOperationException(), () => new(),
            action => action(), retainUsageHistory: true);
        var first = Frame(1)(new SamplingOptions()).Full;
        var second = Frame(2)(new SamplingOptions()).Full;
        session.Record(ProcessRetention.Compact(first));
        session.Record(ProcessRetention.Compact(second), second);
        Assert.Multiple(() =>
        {
            Assert.That(session.UsageHistory.Query(first, 120), Is.Null);
            Assert.That(session.UsageHistory.Query(second, 120)!.Apps.Sum(app => app.Running), Is.EqualTo(Processes));
            Assert.That(session.UsageHistory.Query(second, 120)!.Complete, Is.False);
        });
    }

    [Test]
    public void Record_FeedsUsageOnlyFromAFullSample()
    {
        var harness = new Harness();
        var first = Frame(1)(new SamplingOptions()).Full;
        var second = Frame(2)(new SamplingOptions()).Full;

        harness.Session.Record(ProcessRetention.Compact(first));
        harness.Session.Record(ProcessRetention.Compact(second), second);

        Assert.Multiple(() =>
        {
            Assert.That(harness.Session.History.Samples, Has.Count.EqualTo(2));
            Assert.That(harness.Session.Usage.Apps.Sum(app => app.Lifetimes), Is.EqualTo(Processes),
                "A compacted-only frame, as restored after an update, is not counted.");
            Assert.That(harness.Session.Usage.Apps.Sum(app => app.Running), Is.EqualTo(Processes));
        });
    }

    [TestCase("old", "", true)]
    [TestCase("", "", false)]
    [TestCase("", "new", false)]
    [TestCase("old", "new", false)]
    [TestCase("old", "  ", false)]
    public async Task NetworkOwnerPathFallbackRetainsTheWholePreviousMetadataObject(string oldPath, string newPath, bool keepOld)
    {
        var old = new ProcessUsage(42, 100, "old-name", 1) { ImagePath = oldPath, PrivateBytes = 10 };
        var current = old with { Name = "new-name", ImagePath = newPath, CpuPercent = 2, PrivateBytes = 20 };
        var generation = Guid.NewGuid();
        var session = Guid.NewGuid();
        var reads = 0;
        var harness = new Harness(() => new SamplingOptions
            { ProcessNetwork = Network(generation, ++reads, reads * 100), ProcessNetworkSessionId = session },
            options => new CapturedFrame(Frame(1)(options).Full with { Processes = [old] }),
            options => new CapturedFrame(Frame(2)(options).Full with { Processes = [current] }));
        await harness.RunToEnd();
        harness.Drain();
        Assert.Multiple(() =>
        {
            Assert.That(harness.Session.ProcessNetwork!.Owners.Single(), Is.SameAs(keepOld ? old : current));
            Assert.That(harness.Session.LatestFull!.Processes.Single(), Is.SameAs(current));
            Assert.That(harness.Session.Connections.Captures[0].Network!.Owners.Single(), Is.SameAs(old));
            Assert.That(harness.Session.Connections.Captures[0].Network!.Owners,
                Is.Not.SameAs(harness.Session.ProcessNetwork.Owners));
        });
    }

    [TestCase(ProcessLifetimeDomain.WindowsFileTime, true)]
    [TestCase(ProcessLifetimeDomain.UnixMicroseconds, false)]
    public async Task NetworkOwnersRequireWindowsLifetimeDomainEvenWhenPidAndTokenMatch(ProcessLifetimeDomain domain, bool attached)
    {
        var owner = new ProcessUsage(42, 100, "worker", 0) { LifetimeDomain = domain, ImagePath = "known" };
        var harness = new Harness(new SamplingOptions
            { ProcessNetwork = Network(Guid.NewGuid(), 1, 100), ProcessNetworkSessionId = Guid.NewGuid() },
            options => new CapturedFrame(Frame(1)(options).Full with { Processes = [owner] }));
        await harness.RunToEnd();
        harness.Drain();
        Assert.That(harness.Session.ProcessNetwork!.Owners, Is.EqualTo(attached ? new[] { owner } : []));
        Assert.That(harness.Session.LatestFull!.Processes.Single(), Is.SameAs(owner));
    }

    [TestCase(8191, false, true)]
    [TestCase(8192, false, true)]
    [TestCase(8193, true, false)]
    public async Task NetworkOwnerMetadataCapacityIsInclusiveAndReportsRejectedMetadata(int count, bool exceeded, bool attached)
    {
        var owners = Enumerable.Range(1, count).Select(id => new ProcessUsage(id, id, "worker" + id, 0)).ToArray();
        var snapshot = Network(Guid.NewGuid(), 1, 100) with { Processes = [new(count, count, 100, 200, 20, 40)] };
        var harness = new Harness(new SamplingOptions { ProcessNetwork = snapshot, ProcessNetworkSessionId = Guid.NewGuid() },
            options => new CapturedFrame(Frame(1)(options).Full with { Processes = owners }));
        await harness.RunToEnd();
        harness.Drain();
        var network = harness.Session.ProcessNetwork!;
        Assert.That(network.Owners, Is.EqualTo(attached ? new[] { owners[^1] } : []));
        Assert.That(network.Quality.HasFlag(ProcessNetworkQuality.CapacityExceeded), Is.EqualTo(exceeded));
        if (exceeded) Assert.That(network.Quality.HasFlag(ProcessNetworkQuality.Incomplete), Is.True);
        Assert.That(harness.Session.Error, Is.Null);
        Assert.That(harness.Session.LatestFull!.Processes, Has.Count.EqualTo(count));
    }

    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public async Task NetworkOwnerCacheClearsOnClientSessionChangeRatherThanCollectorGeneration(bool changeSession, bool changeGeneration)
    {
        var owner = new ProcessUsage(42, 100, "worker", 0) { ImagePath = "known" };
        var firstSession = Guid.NewGuid();
        var nextSession = Guid.NewGuid();
        var firstGeneration = Guid.NewGuid();
        var nextGeneration = Guid.NewGuid();
        var reads = 0;
        var harness = new Harness(() =>
        {
            var read = ++reads;
            return new SamplingOptions
            {
                ProcessNetworkSessionId = changeSession && read > 1 ? nextSession : firstSession,
                ProcessNetwork = Network(changeGeneration && read > 1 ? nextGeneration : firstGeneration, read, read * 100)
            };
        }, options => new CapturedFrame(Frame(1)(options).Full with { Processes = [owner] }),
            options => new CapturedFrame(Frame(2)(options).Full with { Processes = [] }));
        await harness.RunToEnd();
        harness.Drain();
        Assert.That(harness.Session.ProcessNetwork!.Owners, Is.EqualTo(changeSession ? Array.Empty<ProcessUsage>() : [owner]));
        Assert.That(harness.Session.Connections.Captures[0].Network!.Owners.Single(), Is.SameAs(owner));
        Assert.That(harness.Session.LatestFull!.Processes, Is.Empty);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task NetworkOwnerMetadataIncludesFlowOnlyLifetimesAndDeduplicatesSharedReadings(bool includeProcess)
    {
        var owner = new ProcessUsage(42, 100, "worker", 0) { ImagePath = "known" };
        var generation = Guid.NewGuid();
        var session = Guid.NewGuid();
        var reads = 0;
        var harness = new Harness(() =>
        {
            var read = ++reads;
            var snapshot = Network(generation, read, read * 100);
            return new SamplingOptions
            {
                ProcessNetworkSessionId = session,
                ProcessNetwork = snapshot with
                {
                    Processes = includeProcess ? snapshot.Processes : [], FlowTrafficSupported = true,
                    Flows = [new(42, 100, TransportProtocol.Tcp, "127.0.0.1:5000", "127.0.0.1:443", read * 100, 0, 20, 0),
                        new(42, 100, TransportProtocol.Udp, "127.0.0.1:5001", "127.0.0.1:53", read * 100, 0, 20, 0)]
                }
            };
        }, options => new CapturedFrame(Frame(1)(options).Full with { Processes = [owner] }),
            options => new CapturedFrame(Frame(2)(options).Full with { Processes = [] }));
        await harness.RunToEnd();
        harness.Drain();
        Assert.That(harness.Session.ProcessNetwork!.Owners.Single(), Is.SameAs(owner));
        Assert.That(harness.Session.ProcessNetwork.Flows, Has.Count.EqualTo(2));
        Assert.That(harness.Session.ProcessNetwork.Processes, Has.Count.EqualTo(includeProcess ? 1 : 0));
        Assert.That(harness.Session.LatestFull!.Processes, Is.Empty);
    }

    // Working sets rise with the PID, so compaction drops the ten smallest idle processes.
    private static Func<SamplingOptions, CapturedFrame> Frame(int second)
    {
        return _ =>
        {
            var processes = Enumerable.Range(0, Processes)
                .Select(i => new ProcessUsage(i, i + 1, $"fixture-{i}", 0, (ulong)(i + 1) * 1000))
                .ToArray();
            var sample = new MonitorSample(DateTimeOffset.UnixEpoch.AddSeconds(second), 10, processes, 0, false,
                Stopwatch.Frequency * second);
            return new CapturedFrame(sample, new ServiceFrame(sample, []));
        };
    }

    private sealed class Harness
    {
        private readonly CancellationTokenSource _cancellation = new();
        private int _optionReads;
        private int _creates;

        internal Harness(params Func<SamplingOptions, CapturedFrame>[] script) : this(new SamplingOptions(), script)
        {
        }

        internal Harness(SamplingOptions options, params Func<SamplingOptions, CapturedFrame>[] script) : this(
            () => options, script)
        {
        }

        internal Harness(Func<SamplingOptions> options, params Func<SamplingOptions, CapturedFrame>[] script)
        {
            Source = new FakeSource(script, _cancellation);
            Session = new MonitorSession(() => Interlocked.Increment(ref _creates) <= FailedCreates
                ? throw new InvalidOperationException("Sampling could not start.")
                : Source, () =>
            {
                Interlocked.Increment(ref _optionReads);
                return options();
            }, Posted.Enqueue, TimeSpan.FromMilliseconds(1));
            Session.Recorded += () => RecordedCount++;
        }

        internal MonitorSession Session { get; }
        internal FakeSource Source { get; }
        internal ConcurrentQueue<Action> Posted { get; } = new();
        internal int RecordedCount { get; private set; }
        internal int OptionReads => Volatile.Read(ref _optionReads);
        internal int Creates => Volatile.Read(ref _creates);

        /// <summary>How many times creating the source throws before it succeeds.</summary>
        internal int FailedCreates { get; init; }

        internal async Task RunToEnd()
        {
            await Session.Run(_cancellation.Token).WaitAsync(TimeSpan.FromSeconds(10));
        }

        internal void Drain()
        {
            while (Posted.TryDequeue(out var action)) action();
        }
    }

    // Plays its script, one entry per capture, then ends the run the way cancellation does.
    private sealed class FakeSource(Func<SamplingOptions, CapturedFrame>[] script, CancellationTokenSource end)
        : IFrameSource
    {
        private int _next;

        internal List<SamplingOptions> Options { get; } = [];
        internal int Resets { get; private set; }
        internal bool Disposed { get; private set; }
        internal bool FailReset { get; set; }

        public CapturedFrame Capture(SamplingOptions options)
        {
            if (_next == script.Length)
            {
                end.Cancel();
                end.Token.ThrowIfCancellationRequested();
            }

            Options.Add(options);
            return script[_next++](options);
        }

        public void Reset()
        {
            Resets++;
            if (FailReset) throw new InvalidOperationException("Baselines could not be dropped.");
        }

        public void Dispose()
        {
            Disposed = true;
        }
    }
}
