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
            Task stop;
            try
            {
                await owner.StartAsync(default);
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
        }
    }
}
