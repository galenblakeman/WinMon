using System.Diagnostics;
using NUnit.Framework;

namespace WinMon.SensorService.Tests;

[TestFixture]
public sealed class SensorCollectorManagerTests
{
    private const string User = "S-1-5-21-1-2-3-1001";

    [Test]
    public async Task CompletedFailure_RemainsFailedWhenCleanupResourcesAreDisposedAndStopIsRequested()
    {
        var provider = new FakeProvider { OpenFailure = new InvalidOperationException("open failed") };
        var worker = new SensorCollectorWorker(provider, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2));
        await worker.Completion.WaitAsync(TimeSpan.FromSeconds(2));
        var failed = worker.Snapshot;
        worker.DisposeCompleted();
        Assert.That(() => worker.RequestStop(), Throws.Nothing);
        Assert.That(worker.Snapshot, Is.SameAs(failed));
        Assert.That(worker.Snapshot.Quality, Is.EqualTo(SensorCollectorQuality.Failed));
    }

    [Test]
    public async Task Leases_ShareOneWorkerAndCloseAfterTheIdleGrace()
    {
        var provider = new FakeProvider();
        await using var manager = Manager(() => provider, TimeSpan.Zero);
        var first = await manager.SubscribeAsync(User, CancellationToken.None);
        var second = await manager.SubscribeAsync(User, CancellationToken.None);
        await provider.ReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await first.DisposeAsync();
        Assert.That(provider.Disposed, Is.False);
        await second.DisposeAsync();
        await provider.Closed.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Multiple(() =>
        {
            Assert.That(provider.Opens, Is.EqualTo(1));
            Assert.That(provider.MaxConcurrentCalls, Is.EqualTo(1));
            Assert.That(provider.Disposed, Is.True);
        });
    }

    [Test]
    public async Task Watchdog_MarksFailureButNeverReplacesABlockedNativeCall()
    {
        var blocked = new FakeProvider { BlockRead = true };
        var replacement = new FakeProvider();
        var providers = new Queue<FakeProvider>([blocked, replacement]);
        await using var manager = Manager(providers.Dequeue, TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(30),
            TimeSpan.Zero);
        await using var first = await manager.SubscribeAsync(User, CancellationToken.None);
        await blocked.ReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await Task.Delay(80);
        await using var second = await manager.SubscribeAsync(User, CancellationToken.None);
        Assert.Multiple(() =>
        {
            Assert.That(manager.Snapshot(User).Quality, Is.EqualTo(SensorCollectorQuality.Failed));
            Assert.That(manager.Snapshot(User).FailureReason, Does.Contain("update timed out"));
            Assert.That(providers, Has.Count.EqualTo(1), "The hung provider still owns native resources.");
        });
        blocked.ReleaseRead();
        await replacement.ReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.That(replacement.Opens, Is.EqualTo(1));
    }

    [Test]
    public async Task Suspend_InvalidatesSnapshotAndWaitsForTheOldWorkerBeforeResume()
    {
        var blocked = new FakeProvider { BlockRead = true };
        var replacement = new FakeProvider();
        var providers = new Queue<FakeProvider>([blocked, replacement]);
        await using var manager = Manager(providers.Dequeue, TimeSpan.FromMilliseconds(10), TimeSpan.FromSeconds(2), TimeSpan.Zero);
        await using var lease = await manager.SubscribeAsync(User, CancellationToken.None);
        await blocked.ReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await manager.SetSuspendedAsync(true, CancellationToken.None);
        await manager.SetSuspendedAsync(false, CancellationToken.None);
        Assert.Multiple(() =>
        {
            Assert.That(manager.Snapshot(User).Quality, Is.Not.EqualTo(SensorCollectorQuality.Complete));
            Assert.That(providers, Has.Count.EqualTo(1));
        });
        blocked.ReleaseRead();
        await replacement.ReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.That(replacement.Opens, Is.EqualTo(1));
    }

    [Test]
    public async Task UnauthorizedSid_CannotLeaseOrRead()
    {
        await using var manager = Manager(() => new FakeProvider(), TimeSpan.Zero);
        Assert.ThrowsAsync<UnauthorizedAccessException>(async () =>
            await manager.SubscribeAsync("S-1-5-21-1-2-3-1002", CancellationToken.None));
        Assert.That(() => manager.Snapshot("S-1-5-21-1-2-3-1002"), Throws.TypeOf<UnauthorizedAccessException>());
    }

    [Test]
    public async Task SynchronousNativeRead_DoesNotBlockSubscriptionOrReplaceTheLiveWorker()
    {
        using var release = new ManualResetEventSlim();
        var blocked = new SynchronousBlockingProvider(release);
        var replacement = new FakeProvider();
        var providers = new Queue<ISensorProvider>([blocked, replacement]);
        await using var manager = Manager(providers.Dequeue, TimeSpan.Zero, TimeSpan.FromMilliseconds(30), TimeSpan.Zero);
        await using var first = await manager.SubscribeAsync(User, CancellationToken.None);
        await blocked.ReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        var second = await manager.SubscribeAsync(User, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(1));
        await using (second)
        {
            await Task.Delay(80);
            Assert.That(manager.Snapshot(User).Quality, Is.EqualTo(SensorCollectorQuality.Failed));
            Assert.That(providers, Has.Count.EqualTo(1));
        }

        release.Set();
        await replacement.ReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Test]
    public async Task SynchronousNativeClose_IsTimedButTheWorkerRemainsOwnerUntilItReturns()
    {
        using var release = new ManualResetEventSlim();
        var provider = new CloseBlockingProvider(release);
        await using var worker = new SensorCollectorWorker(provider, TimeSpan.Zero, TimeSpan.FromMilliseconds(30));
        await provider.ReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        worker.RequestStop();
        await provider.CloseStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await Task.Delay(80);
        Assert.Multiple(() =>
        {
            Assert.That(worker.Snapshot.Quality, Is.EqualTo(SensorCollectorQuality.Failed));
            Assert.That(worker.IsCompleted, Is.False);
        });
        release.Set();
        await worker.Completion.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Test]
    public async Task TimedOutWorkerThatReturnsAfterRecoveryBudget_RemainsFailedUntilServiceRestart()
    {
        var blocked = new FakeProvider { BlockRead = true };
        var replacement = new FakeProvider();
        var providers = new Queue<FakeProvider>([blocked, replacement]);
        await using var manager = new SensorCollectorManager([User], providers.Dequeue, TimeSpan.Zero,
            TimeSpan.FromMilliseconds(5), TimeSpan.FromMilliseconds(30), TimeSpan.Zero, TimeSpan.FromMilliseconds(20));
        await using var first = await manager.SubscribeAsync(User, CancellationToken.None);
        await blocked.ReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await Task.Delay(80); // Exceeds watchdog and recovery windows while the provider is still live.
        blocked.ReleaseRead();
        await blocked.Closed.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await Task.Delay(40);
        await using var next = await manager.SubscribeAsync(User, CancellationToken.None);
        Assert.Multiple(() =>
        {
            Assert.That(manager.Snapshot(User).Quality, Is.EqualTo(SensorCollectorQuality.Failed));
            Assert.That(providers, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public async Task OpenFailureWithUncertainClose_LatchesServiceUntilRestart()
    {
        var failed = new FakeProvider { OpenFailure = new InvalidOperationException("native open failed"), CloseFailure = new InvalidOperationException("native close failed") };
        var replacement = new FakeProvider();
        var providers = new Queue<FakeProvider>([failed, replacement]);
        await using var manager = Manager(providers.Dequeue, TimeSpan.Zero, TimeSpan.FromMilliseconds(30), TimeSpan.Zero);
        await using var first = await manager.SubscribeAsync(User, CancellationToken.None);
        await failed.Closed.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await Task.Delay(50);
        await using var next = await manager.SubscribeAsync(User, CancellationToken.None);
        Assert.Multiple(() =>
        {
            Assert.That(manager.Snapshot(User).Quality, Is.EqualTo(SensorCollectorQuality.Failed));
            Assert.That(providers, Has.Count.EqualTo(1));
        });
    }

    private static SensorCollectorManager Manager(Func<ISensorProvider> create, TimeSpan idleGrace,
        TimeSpan? budget = null, TimeSpan? retry = null) =>
        new([User], create, idleGrace, TimeSpan.FromMilliseconds(5), budget ?? TimeSpan.FromSeconds(1), retry ?? TimeSpan.Zero);

    private sealed class FakeProvider : ISensorProvider
    {
        private int _active;
        private readonly TaskCompletionSource<SensorReading?> _blocked = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource ReadStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource Closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal bool BlockRead;
        internal bool Disposed;
        internal int Opens;
        internal int MaxConcurrentCalls;
        internal Exception? OpenFailure;
        internal Exception? CloseFailure;

        public Task OpenAsync(CancellationToken token)
        {
            Opens++;
            if (OpenFailure is { } failure) throw failure;
            return Task.CompletedTask;
        }

        public async Task<SensorReading?> ReadCpuTemperatureAsync(CancellationToken token)
        {
            var active = Interlocked.Increment(ref _active);
            MaxConcurrentCalls = Math.Max(MaxConcurrentCalls, active);
            try
            {
                ReadStarted.TrySetResult();
                if (BlockRead) return await _blocked.Task.ConfigureAwait(false);
                return CurrentReading();
            }
            finally
            {
                Interlocked.Decrement(ref _active);
            }
        }

        internal void ReleaseRead()
        {
            BlockRead = false;
            _blocked.TrySetResult(CurrentReading());
        }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            Closed.TrySetResult();
            if (CloseFailure is { } failure) throw failure;
            return ValueTask.CompletedTask;
        }

        private static SensorReading CurrentReading() =>
            new("cpu/0", "CPU", "package", "CPU Package", 51, "LibreHardwareMonitorLib 0.9.6", Stopwatch.GetTimestamp());
    }

    private sealed class SynchronousBlockingProvider(ManualResetEventSlim release) : ISensorProvider
    {
        internal readonly TaskCompletionSource ReadStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task OpenAsync(CancellationToken token) => Task.CompletedTask;

        public Task<SensorReading?> ReadCpuTemperatureAsync(CancellationToken token)
        {
            ReadStarted.TrySetResult();
            release.Wait(); // Models a blocking native LHM/PawnIO call before it can return a Task.
            return Task.FromResult<SensorReading?>(new SensorReading("cpu/0", "CPU", "package", "CPU Package", 51,
                "test", Stopwatch.GetTimestamp()));
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class CloseBlockingProvider(ManualResetEventSlim release) : ISensorProvider
    {
        internal readonly TaskCompletionSource ReadStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource CloseStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task OpenAsync(CancellationToken token) => Task.CompletedTask;

        public Task<SensorReading?> ReadCpuTemperatureAsync(CancellationToken token)
        {
            ReadStarted.TrySetResult();
            return Task.FromResult<SensorReading?>(new SensorReading("cpu/0", "CPU", "package", "CPU Package", 51,
                "test", Stopwatch.GetTimestamp()));
        }

        public ValueTask DisposeAsync()
        {
            CloseStarted.TrySetResult();
            release.Wait(); // Models a synchronous native Close call that ignores cancellation.
            return ValueTask.CompletedTask;
        }
    }
}
