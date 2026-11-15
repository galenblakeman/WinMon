using NUnit.Framework;
using WinMon.Mcp;
using WinMon.Monitoring;

namespace WinMon.Tests;

[TestFixture]
public sealed class SamplingOwnerTests
{
    [Test]
    public async Task CurrentWaitsForWarmupAndConcurrentReadersShareOneSerializedSource()
    {
        var source = new Source(blockOn: 2);
        var created = 0;
        await using var owner = new SamplingOwner(() => { Interlocked.Increment(ref created); return source; },
            TimeProvider.System, TimeSpan.FromMilliseconds(5));
        Task<PublishedFrame?>[] readers;
        try
        {
            await owner.StartAsync(default);
            await using var request = await owner.AcquireAsync(default);
            await source.Entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            readers = Enumerable.Range(0, 12).Select(_ => owner.CurrentAsync(default)).ToArray();
            Assert.That(readers.Any(reader => reader.IsCompleted), Is.False, "Readers must wait for warmup.");
        }
        finally { source.Release.TrySetResult(); }
        var frames = await Task.WhenAll(readers).WaitAsync(TimeSpan.FromSeconds(3));

        Assert.Multiple(() =>
        {
            Assert.That(frames, Has.All.Not.Null);
            Assert.That(created, Is.EqualTo(1));
            Assert.That(frames[0]!.Sequence, Is.GreaterThanOrEqualTo(2));
            Assert.That(source.MaxConcurrentCaptures, Is.EqualTo(1));
            Assert.That(source.Captures, Is.GreaterThanOrEqualTo(2));
        });
    }

    [Test]
    public async Task CancellingOneWaiterDoesNotStopSharedSampling()
    {
        var source = new Source();
        await using var owner = new SamplingOwner(() => source, TimeProvider.System, TimeSpan.FromMilliseconds(20));
        await owner.StartAsync(default);
        await using var request = await owner.AcquireAsync(default);
        using var cancelled = new CancellationTokenSource();
        var wait = owner.WaitForChangeAsync(long.MaxValue, cancelled.Token);
        cancelled.Cancel();

        Assert.That(async () => await wait, Throws.InstanceOf<OperationCanceledException>());
        var current = await owner.CurrentAsync(default).WaitAsync(TimeSpan.FromSeconds(3));
        await owner.WaitForChangeAsync(current!.Sequence, default).WaitAsync(TimeSpan.FromSeconds(3));

        Assert.Multiple(() =>
        {
            Assert.That(current, Is.Not.Null);
            Assert.That(owner.ReadAfter(current.Sequence), Is.Not.Empty);
        });
    }

    [Test]
    public async Task RetainsOnlyTheMostRecent120Frames()
    {
        var source = new Source();
        await using var owner = new SamplingOwner(() => source, TimeProvider.System, TimeSpan.FromMilliseconds(1));
        await owner.StartAsync(default);
        await using var request = await owner.AcquireAsync(default);
        await WaitForFrameAsync(owner, frame => frame.Sequence >= 125);

        var frames = owner.ReadAfter(0);

        Assert.Multiple(() =>
        {
            Assert.That(frames, Has.Length.EqualTo(120));
            Assert.That(frames[0].Sequence, Is.EqualTo(frames[^1].Sequence - 119));
        });
    }

    [Test]
    public async Task FailedCapturePublishesSafeUnavailableFrame()
    {
        var source = new Source(throwOn: 3);
        await using var owner = new SamplingOwner(() => source, TimeProvider.System, TimeSpan.FromMilliseconds(5));
        await owner.StartAsync(default);
        await using var request = await owner.AcquireAsync(default);
        var failed = await WaitForFrameAsync(owner, frame => frame.Frame.Error == SamplingError.ReadingsUnavailable);

        Assert.Multiple(() =>
        {
            Assert.That(failed.Frame.Full.IsGap, Is.True);
            Assert.That(failed.Frame.Full.Processes, Is.Empty);
            Assert.That(failed.Frame.Full.TotalCpuPercent, Is.Null);
        });
    }

    [TestCase(-1, false)]
    [TestCase(0, true)]
    [TestCase(1999, true)]
    [TestCase(2000, true)]
    [TestCase(2001, false)]
    public async Task FreshnessAcceptsOnlyFramesFromTheLastTwoSeconds(int ageMilliseconds, bool expected)
    {
        var clock = new FixedClock();
        await using var owner = new SamplingOwner(() => new Source(), clock);
        var frame = new PublishedFrame(1, Frame(), owner.Now - ageMilliseconds);

        Assert.That(owner.IsFresh(frame), Is.EqualTo(expected));
    }

    [Test]
    public async Task StopAndDisposeReleaseTheSourceExactlyOnceAfterCaptureCompletes()
    {
        var source = new Source(blockOn: 1);
        await using (var owner = new SamplingOwner(() => source, TimeProvider.System, TimeSpan.FromMilliseconds(5)))
        {
            await owner.StartAsync(default);
            await using var request = await owner.AcquireAsync(default);
            Task stop;
            try
            {
                await source.Entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
                stop = owner.StopAsync(default);
                Assert.That(stop.IsCompleted, Is.False);
            }
            finally { source.Release.TrySetResult(); }
            await stop.WaitAsync(TimeSpan.FromSeconds(3));
        }

        Assert.Multiple(() =>
        {
            Assert.That(source.DisposeCount, Is.EqualTo(1));
            Assert.That(source.DisposedDuringCapture, Is.False);
        });
    }

    [Test]
    public async Task StartupHistoryAndCancelledRequestsDoNotCreateACollector()
    {
        var created = 0;
        await using var owner = new SamplingOwner(() => { created++; return new Source(); }, TimeProvider.System);
        await owner.StartAsync(default);
        await new DiagnosticTools(owner).GetPerformanceHistory();
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        Assert.ThrowsAsync<OperationCanceledException>(async () => await owner.CurrentAsync(cancelled.Token));
        await Task.Delay(30);
        Assert.That(created, Is.Zero);
    }

    [Test]
    public async Task ShutdownCancelsReadersWithoutWaitingForABlockedCaptureToFinish()
    {
        var source = new Source(blockOn: 1);
        await using var owner = new SamplingOwner(() => source, TimeProvider.System);
        var current = owner.CurrentAsync(default);
        Task stop;
        try
        {
            await source.Entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            stop = owner.StopAsync(default);
            Assert.That(async () => await current.WaitAsync(TimeSpan.FromSeconds(1)),
                Throws.InstanceOf<OperationCanceledException>());
            Assert.That(stop.IsCompleted, Is.False);
        }
        finally { source.Release.TrySetResult(); }
        await stop.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.That(source.DisposedDuringCapture, Is.False);
    }

    [Test]
    public async Task IdleReleasesCollectorAndNextRequestWarmsUpANewSource()
    {
        var sources = new List<Source>();
        await using var owner = new SamplingOwner(() =>
        {
            var source = new Source();
            sources.Add(source);
            return source;
        }, TimeProvider.System, TimeSpan.FromMilliseconds(5), idleTimeout: TimeSpan.FromMilliseconds(40));
        var first = await owner.CurrentAsync(default);
        await sources[0].Disposed.Task.WaitAsync(TimeSpan.FromSeconds(3));
        var stoppedSequence = owner.ReadAfter(0)[^1].Sequence;
        var captures = sources[0].Captures;
        await Task.Delay(80);
        Assert.That(sources[0].Captures, Is.EqualTo(captures));
        var second = await owner.CurrentAsync(default);
        Assert.Multiple(() =>
        {
            Assert.That(first, Is.Not.Null);
            Assert.That(second!.Sequence, Is.GreaterThanOrEqualTo(stoppedSequence + 2));
            Assert.That(sources, Has.Count.EqualTo(2));
            Assert.That(sources[0].DisposeCount, Is.EqualTo(1));
            Assert.That(sources[1].Captures, Is.GreaterThanOrEqualTo(2));
            Assert.That(sources[0].DisposedDuringCapture, Is.False);
        });
    }

    [Test]
    public async Task SamplingStaysAliveUntilTheLastActiveRequestBecomesIdle()
    {
        var source = new Source();
        await using var owner = new SamplingOwner(() => source, TimeProvider.System,
            TimeSpan.FromMilliseconds(5), idleTimeout: TimeSpan.FromMilliseconds(40));
        await using var first = await owner.AcquireAsync(default);
        await using var second = await owner.AcquireAsync(default);
        await owner.CurrentAsync(default);
        await first.DisposeAsync();
        await Task.Delay(80);
        Assert.That(source.DisposeCount, Is.Zero);
        await second.DisposeAsync();
        await source.Disposed.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.That(source.DisposeCount, Is.EqualTo(1));
    }

    [Test]
    public async Task PerformanceRequestKeepsSamplingBeyondTheIdleTimeout()
    {
        var source = new Source();
        await using var owner = new SamplingOwner(() => source, TimeProvider.System,
            TimeSpan.FromMilliseconds(20), idleTimeout: TimeSpan.FromMilliseconds(40));
        var result = await new DiagnosticTools(owner).SamplePerformance(1).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(result.StructuredContent!.Value.GetProperty("status").GetString(), Is.EqualTo("ok"));
        await source.Disposed.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.That(source.DisposeCount, Is.EqualTo(1));
    }

    [Test]
    public async Task RequestBeforeIdleDeadlineCancelsStopAndReusesCollector()
    {
        var clock = new ManualClock();
        var source = new Source();
        var created = 0;
        await using var owner = new SamplingOwner(() => { created++; return source; }, clock,
            TimeSpan.FromMilliseconds(5));
        var first = await owner.CurrentAsync(default).WaitAsync(TimeSpan.FromSeconds(3));
        clock.Advance(TimeSpan.FromSeconds(29));
        await using var request = await owner.AcquireAsync(default);
        clock.Advance(TimeSpan.FromSeconds(2));
        var next = await WaitForFrameAsync(owner, frame => frame.Sequence > first!.Sequence);
        Assert.Multiple(() =>
        {
            Assert.That(created, Is.EqualTo(1));
            Assert.That(source.DisposeCount, Is.Zero);
            Assert.That(next.Sequence, Is.GreaterThan(first!.Sequence));
        });

        await request.DisposeAsync();
        clock.Advance(TimeSpan.FromSeconds(29));
        Assert.That(source.DisposeCount, Is.Zero, "The replacement idle timer needs a full timeout.");
        clock.Advance(TimeSpan.FromSeconds(1));
        await source.Disposed.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.That(source.DisposeCount, Is.EqualTo(1));
    }

    [Test]
    public async Task WarmupTimeoutReleasesRequestAndLaterRequestRecovers()
    {
        var clock = new ManualClock();
        var blocked = new Source(blockOn: 1);
        var recovered = new Source();
        var created = 0;
        await using var owner = new SamplingOwner(() => ++created == 1 ? blocked : recovered,
            clock, TimeSpan.FromMilliseconds(5));
        try
        {
            var current = owner.CurrentAsync(default);
            await blocked.Entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            clock.Advance(TimeSpan.FromSeconds(4));
            Assert.That(current.IsCompleted, Is.False);
            clock.Advance(TimeSpan.FromSeconds(1));
            Assert.That(await current.WaitAsync(TimeSpan.FromSeconds(3)), Is.Null);
            clock.Advance(TimeSpan.FromSeconds(30));
        }
        finally { blocked.Release.TrySetResult(); }

        await blocked.Disposed.Task.WaitAsync(TimeSpan.FromSeconds(3));
        var stoppedSequence = owner.ReadAfter(0).LastOrDefault()?.Sequence ?? 0;
        var frame = await owner.CurrentAsync(default).WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Multiple(() =>
        {
            Assert.That(created, Is.EqualTo(2));
            Assert.That(frame, Is.Not.Null);
            Assert.That(frame!.Sequence, Is.GreaterThanOrEqualTo(stoppedSequence + 2));
            Assert.That(recovered.Captures, Is.GreaterThanOrEqualTo(2));
            Assert.That(blocked.DisposeCount, Is.EqualTo(1));
            Assert.That(blocked.DisposedDuringCapture, Is.False);
        });
    }

    private sealed class ManualClock : TimeProvider
    {
        private readonly List<ManualTimer> _timers = [];
        private long _ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => Interlocked.Read(ref _ticks);

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            Assert.That(period, Is.EqualTo(Timeout.InfiniteTimeSpan), "These tests use only one-shot timers.");
            var timer = new ManualTimer(this, callback, state);
            timer.Change(dueTime, period);
            lock (_timers) _timers.Add(timer);
            return timer;
        }

        internal void Advance(TimeSpan elapsed)
        {
            List<ManualTimer> due;
            lock (_timers)
            {
                Interlocked.Add(ref _ticks, elapsed.Ticks);
                due = _timers.Where(timer => timer.Due <= _ticks).ToList();
                foreach (var timer in due) timer.Due = long.MaxValue;
            }
            foreach (var timer in due) timer.Fire();
        }

        private sealed class ManualTimer(ManualClock clock, TimerCallback callback, object? state) : ITimer
        {
            internal long Due = long.MaxValue;
            private bool _disposed;

            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                Assert.That(period, Is.EqualTo(Timeout.InfiniteTimeSpan));
                lock (clock._timers)
                {
                    if (_disposed) return false;
                    Due = dueTime == Timeout.InfiniteTimeSpan ? long.MaxValue : clock._ticks + dueTime.Ticks;
                    return true;
                }
            }

            internal void Fire() => callback(state);
            public void Dispose()
            {
                lock (clock._timers)
                {
                    _disposed = true;
                    clock._timers.Remove(this);
                }
            }
            public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        }
    }

    private static CapturedFrame Frame() => new(new MonitorSample(DateTimeOffset.UtcNow, 10, [], 0)
    {
        IntervalSeconds = 1
    });

    private sealed class FixedClock : TimeProvider
    {
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp() => 10000;
    }

    private static async Task<PublishedFrame> WaitForFrameAsync(SamplingOwner owner, Func<PublishedFrame, bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        long sequence = 0;
        while (true)
        {
            foreach (var frame in owner.ReadAfter(sequence))
            {
                if (condition(frame)) return frame;
                sequence = frame.Sequence;
            }
            await owner.WaitForChangeAsync(sequence, timeout.Token);
        }
    }

    private sealed class Source(int? throwOn = null, int? blockOn = null) : IFrameSource
    {
        private int _active;
        public int Captures { get; private set; }
        public int MaxConcurrentCaptures { get; private set; }
        public int DisposeCount { get; private set; }
        public bool DisposedDuringCapture { get; private set; }
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Disposed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public CapturedFrame Capture(SamplingOptions options)
        {
            var active = Interlocked.Increment(ref _active);
            MaxConcurrentCaptures = Math.Max(MaxConcurrentCaptures, active);
            try
            {
                var capture = ++Captures;
                if (capture == blockOn)
                {
                    Entered.TrySetResult();
                    Release.Task.GetAwaiter().GetResult();
                }
                if (capture == throwOn) throw new InvalidOperationException("expected test failure");
                return Frame();
            }
            finally
            {
                Interlocked.Decrement(ref _active);
            }
        }

        public void Reset() { }

        public void Dispose()
        {
            DisposeCount++;
            DisposedDuringCapture |= Volatile.Read(ref _active) != 0;
            Disposed.TrySetResult();
        }
    }
}
