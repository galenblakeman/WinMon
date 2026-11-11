using System.Diagnostics;
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
        var source = new Source();
        var created = 0;
        await using var owner = new SamplingOwner(() => { Interlocked.Increment(ref created); return source; },
            TimeProvider.System, TimeSpan.FromMilliseconds(5));
        await owner.StartAsync(default);

        var readers = Enumerable.Range(0, 12).Select(_ => owner.CurrentAsync(default)).ToArray();
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
        var captureCount = source.Captures;
        await Task.Delay(40);

        Assert.Multiple(() =>
        {
            Assert.That(current, Is.Not.Null);
            Assert.That(source.Captures, Is.GreaterThan(captureCount));
        });
    }

    [Test]
    public async Task RetainsOnlyTheMostRecent120Frames()
    {
        var source = new Source();
        await using var owner = new SamplingOwner(() => source, TimeProvider.System, TimeSpan.FromMilliseconds(1));
        await owner.StartAsync(default);
        await EventuallyAsync(() => source.Captures >= 125);

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
        await EventuallyAsync(() => owner.ReadAfter(0).Any(frame => frame.Frame.Error == SamplingError.ReadingsUnavailable));

        var failed = owner.ReadAfter(0).Single(frame => frame.Frame.Error == SamplingError.ReadingsUnavailable);

        Assert.Multiple(() =>
        {
            Assert.That(failed.Frame.Full.IsGap, Is.True);
            Assert.That(failed.Frame.Full.Processes, Is.Empty);
            Assert.That(failed.Frame.Full.TotalCpuPercent, Is.Null);
        });
    }

    [Test]
    public void FreshnessRejectsFramesOlderThanTwoSeconds()
    {
        var owner = new SamplingOwner(() => new Source(), TimeProvider.System);
        var stale = new PublishedFrame(1, Frame(), owner.Now - TimeProvider.System.TimestampFrequency * 3);

        try
        {
            Assert.That(owner.IsFresh(stale), Is.False);
        }
        finally
        {
            owner.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    [Test]
    public async Task StopAndDisposeReleaseTheSourceExactlyOnceAfterCaptureCompletes()
    {
        var source = new Source(blockFirstCapture: true);
        await using (var owner = new SamplingOwner(() => source, TimeProvider.System, TimeSpan.FromMilliseconds(5)))
        {
            await owner.StartAsync(default);
            await source.Entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            var stop = owner.StopAsync(default);

            Assert.That(stop.IsCompleted, Is.False);
            source.Release.TrySetResult();
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

    private static async Task EventuallyAsync(Func<bool> condition)
    {
        var timeout = Stopwatch.StartNew();
        while (!condition())
        {
            if (timeout.Elapsed > TimeSpan.FromSeconds(3)) Assert.Fail("Condition did not become true.");
            await Task.Delay(5);
        }
    }

    private sealed class Source(int? throwOn = null, bool blockFirstCapture = false) : IFrameSource
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
                if (blockFirstCapture && capture == 1)
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
