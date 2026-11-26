using System.Diagnostics;
using System.Collections.Concurrent;
using System.ComponentModel;
using MonCore.Monitoring;
using MonCore.Windows.Monitoring;
using NUnit.Framework;
using WinMon.Monitoring;
using WinMon.Preferences;

namespace WinMon.Tests;

[TestFixture]
public sealed class CpuTemperatureClientTests
{
    [TestCase("success")]
    [TestCase("cancelled")]
    [TestCase("failed")]
    [NUnit.Framework.Category("Integration")]
    public async Task InstallerEnablesSensorsOnlyAfterSuccessfulSetup(string outcome)
    {
        var folder = Path.Combine(Path.GetTempPath(), "WinMon.SensorInstaller." + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new PreferenceStore(Path.Combine(folder, "settings.json"));
            await store.SaveAsync(new MonitorPreferences { TopProcessCount = 20, ProcessNetworkMonitoringEnabled = true });
            var installed = false;
            var setupCalls = 0;
            var connection = new FakeConnection(Complete(Guid.NewGuid(), 1));
            await using var client = new CpuTemperatureClient(_ => installed
                    ? Task.FromResult<ISensorTemperatureConnection>(connection)
                    : throw new IOException("Service not installed"),
                async (operation, verify, token) =>
                {
                    setupCalls++;
                    Assert.That(operation, Is.EqualTo("install"));
                    Assert.That((await store.LoadAsync()).CpuTemperatureMonitoringEnabled, Is.False);
                    if (outcome == "cancelled") throw new Win32Exception(1223);
                    if (outcome == "failed") throw new IOException("Setup failed");
                    installed = true;
                    await verify(token);
                }, () => true, delay: (_, token) => Task.Delay(Timeout.Infinite, token));
            var result = await App.InstallSensorServiceAsync(client, store);
            var saved = await store.LoadAsync();
            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo(outcome == "success" ? 0 : 1));
                Assert.That(saved.CpuTemperatureMonitoringEnabled, Is.EqualTo(outcome == "success"));
                Assert.That(saved.ProcessNetworkMonitoringEnabled, Is.True);
                Assert.That(saved.TopProcessCount, Is.EqualTo(20));
                Assert.That(setupCalls, Is.EqualTo(1));
            });
        }
        finally
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
        }
    }

    [Test]
    public async Task DeclinedInitialEnable_DoesNotLeaveBackgroundMonitoringEnabled()
    {
        var connects = 0;
        var delays = 0;
        await using var client = new CpuTemperatureClient(_ =>
            { connects++; throw new IOException("not installed"); },
            (_, _, _) => Task.FromException(new Win32Exception(1223)), () => true,
            delay: (_, token) => { delays++; return Task.Delay(Timeout.Infinite, token); });
        Assert.That((await client.ChangeAsync(true, CancellationToken.None)).State,
            Is.EqualTo(SensorMonitoringState.Cancelled));
        await client.RemoveAsync(CancellationToken.None);
        Assert.Multiple(() =>
        {
            Assert.That(client.Reading, Is.Null);
            Assert.That(connects, Is.EqualTo(1));
            Assert.That(delays, Is.Zero);
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task FailedOrDeclinedRemoval_ResumesEnabledMonitoringWithoutAnotherSetupPrompt(bool declined)
    {
        var first = new FakeConnection(Complete(Guid.NewGuid(), 1));
        var second = new FakeConnection(Complete(Guid.NewGuid(), 1, name: "resumed"));
        var connections = new Queue<ISensorTemperatureConnection>([first, second]);
        var setupCalls = 0;
        var delay = new ControlledDelay();
        await using var client = new CpuTemperatureClient(_ => Task.FromResult(connections.Dequeue()),
            (operation, _, _) =>
            {
                setupCalls++;
                Assert.That(operation, Is.EqualTo("remove"));
                return Task.FromException(declined ? new Win32Exception(1223) : new IOException("remove failed"));
            }, () => true, delay: delay.WaitAsync);
        await client.ChangeAsync(true, CancellationToken.None);
        var result = await client.RemoveAsync(CancellationToken.None);
        Assert.That(result, Is.EqualTo(new SensorMonitoringChangeResult(false,
            declined ? SensorMonitoringState.Cancelled : SensorMonitoringState.Failed)));
        Assert.That(client.Reading, Is.Null);
        delay.ReleaseNext();
        await WaitForAsync(() => client.State == SensorMonitoringState.Active);
        Assert.Multiple(() =>
        {
            Assert.That(client.Reading?.SensorName, Is.EqualTo("resumed"));
            Assert.That(first.Disposed, Is.True);
            Assert.That(setupCalls, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task Restore_ProtocolMismatchRequiresRepairWithoutSetupOrRetryPolling()
    {
        var setupCalls = 0;
        var delays = 0;
        await using var client = new CpuTemperatureClient(_ =>
                Task.FromException<ISensorTemperatureConnection>(new SensorServiceProtocolMismatchException(2, 1)),
            (_, _, _) => { setupCalls++; return Task.CompletedTask; }, () => true,
            delay: (_, _) => { delays++; return Task.CompletedTask; });
        Assert.That(await client.RestoreAsync(true, CancellationToken.None),
            Is.EqualTo(new SensorMonitoringChangeResult(false, SensorMonitoringState.UpdateRequired)));
        Assert.Multiple(() =>
        {
            Assert.That(client.Reading, Is.Null);
            Assert.That(setupCalls, Is.Zero);
            Assert.That(delays, Is.Zero);
        });
    }

    [Test]
    public async Task Poll_ProtocolMismatchDiscardsReadingAndStopsUntilExplicitRepair()
    {
        var connection = new FakeConnection(Complete(Guid.NewGuid(), 1),
            new SensorServiceProtocolMismatchException(2, 1));
        var delay = new ControlledDelay();
        await using var client = Client(_ => Task.FromResult<ISensorTemperatureConnection>(connection), delay);
        await client.ChangeAsync(true, CancellationToken.None);
        delay.ReleaseNext();
        await WaitForAsync(() => client.State == SensorMonitoringState.UpdateRequired);
        Assert.Multiple(() =>
        {
            Assert.That(client.Reading, Is.Null);
            Assert.That(connection.Disposed, Is.True);
            Assert.That(connection.SnapshotCalls, Is.EqualTo(2));
        });
    }

    [Test]
    public async Task Enable_ConnectsSubscribesAndPublishesTheFreshReading()
    {
        var connection = new FakeConnection(Complete(Guid.NewGuid(), 1));
        var delay = new ControlledDelay();
        await using var client = Client(_ => Task.FromResult<ISensorTemperatureConnection>(connection), delay);

        var result = await client.ChangeAsync(true, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(new SensorMonitoringChangeResult(true, SensorMonitoringState.Active)));
            Assert.That(connection.SubscribeCalls, Is.EqualTo(1));
            Assert.That(client.Reading?.Celsius, Is.EqualTo(52.5));
        });
    }

    [Test]
    public async Task Snapshot_StaleOrPartialReadingIsUnavailableRatherThanStarting()
    {
        var now = Stopwatch.GetTimestamp();
        var stale = Complete(Guid.NewGuid(), 1, now - 6 * Stopwatch.Frequency);
        var partial = stale with { Quality = SensorServiceQuality.Partial, CpuTemperature = null };
        foreach (var snapshot in new[] { stale, partial })
        {
            await using var client = Client(_ => Task.FromResult<ISensorTemperatureConnection>(new FakeConnection(snapshot)), new ControlledDelay());
            var result = await client.ChangeAsync(true, CancellationToken.None);
            Assert.Multiple(() =>
            {
                Assert.That(result.State, Is.EqualTo(SensorMonitoringState.Unavailable));
                Assert.That(client.Reading, Is.Null);
            });
        }
    }

    [Test]
    public async Task Poll_RetiresAnOlderGenerationAfterANewerGenerationArrives()
    {
        var first = Complete(Guid.NewGuid(), 1);
        var newer = Complete(Guid.NewGuid(), 1);
        var oldAgain = Complete(first.Generation, 99, name: "retired");
        var connection = new FakeConnection(first, newer, oldAgain);
        var delay = new ControlledDelay();
        await using var client = Client(_ => Task.FromResult<ISensorTemperatureConnection>(connection), delay);
        await client.ChangeAsync(true, CancellationToken.None);

        delay.ReleaseNext();
        await connection.WaitForSnapshotsAsync(2);
        delay.ReleaseNext();
        await connection.WaitForSnapshotsAsync(3);

        Assert.Multiple(() =>
        {
            Assert.That(client.Reading?.Generation, Is.EqualTo(newer.Generation));
            Assert.That(client.Reading?.SensorName, Is.Not.EqualTo("retired"));
        });
    }

    [Test]
    public async Task Poll_FailureClearsTheCachedReadingAndReconnects()
    {
        var first = new FakeConnection(Complete(Guid.NewGuid(), 1), new IOException("pipe closed"));
        var second = new FakeConnection(Complete(Guid.NewGuid(), 1, name: "reconnected"));
        var connections = new Queue<ISensorTemperatureConnection>([first, second]);
        var delay = new ControlledDelay();
        await using var client = Client(_ => Task.FromResult(connections.Dequeue()), delay);
        await client.ChangeAsync(true, CancellationToken.None);

        delay.ReleaseNext();
        await first.WaitForSnapshotsAsync(2);
        await WaitForAsync(() => client.State == SensorMonitoringState.Unavailable);
        Assert.That(client.Reading, Is.Null);

        delay.ReleaseNext();
        await second.WaitForSnapshotsAsync(1);
        Assert.Multiple(() =>
        {
            Assert.That(client.State, Is.EqualTo(SensorMonitoringState.Active));
            Assert.That(client.Reading?.SensorName, Is.EqualTo("reconnected"));
        });
    }

    [Test]
    public async Task Disable_ClearsTheReadingStopsPollingAndDisposesTheConnection()
    {
        var connection = new FakeConnection(Complete(Guid.NewGuid(), 1));
        var delay = new ControlledDelay();
        await using var client = Client(_ => Task.FromResult<ISensorTemperatureConnection>(connection), delay);
        await client.ChangeAsync(true, CancellationToken.None);

        var result = await client.ChangeAsync(false, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(new SensorMonitoringChangeResult(true, SensorMonitoringState.Disabled)));
            Assert.That(client.Reading, Is.Null);
            Assert.That(connection.Disposed, Is.True);
        });
    }

    [Test]
    public async Task Enable_UsesSetupThenReportsCancellationAndUnsupportedStoreWithoutAReading()
    {
        var setupCalls = 0;
        var connection = new FakeConnection(Complete(Guid.NewGuid(), 1));
        var attempts = 0;
        await using var setupClient = new CpuTemperatureClient(_ =>
        {
            if (attempts++ == 0) throw new IOException("not installed");
            return Task.FromResult<ISensorTemperatureConnection>(connection);
        }, async (operation, continuation, token) => { setupCalls++; Assert.That(operation, Is.EqualTo("install")); await continuation(token); },
            () => true, delay: new ControlledDelay().WaitAsync);
        Assert.That((await setupClient.ChangeAsync(true, CancellationToken.None)).Success, Is.True);
        Assert.That(setupCalls, Is.EqualTo(1));

        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await using var cancelledClient = Client(_ => throw new IOException("not reached"), new ControlledDelay());
        Assert.That(await cancelledClient.ChangeAsync(true, cancelled.Token),
            Is.EqualTo(new SensorMonitoringChangeResult(false, SensorMonitoringState.Cancelled)));

        await using var storeClient = new CpuTemperatureClient(_ => throw new IOException(), (_, _, _) => Task.CompletedTask,
            () => false, supported: false, delay: new ControlledDelay().WaitAsync);
        var storeResult = await storeClient.ChangeAsync(true, CancellationToken.None);
        Assert.Multiple(() =>
        {
            Assert.That(storeResult,
                Is.EqualTo(new SensorMonitoringChangeResult(false, SensorMonitoringState.StoreUnavailable)));
            Assert.That(storeClient.Reading, Is.Null);
        });
    }

    private static CpuTemperatureClient Client(Func<CancellationToken, Task<ISensorTemperatureConnection>> connect,
        ControlledDelay delay) => new(connect, (_, _, _) => Task.CompletedTask, () => false, delay: delay.WaitAsync);

    private static SensorSnapshot Complete(Guid generation, long sequence, long? timestamp = null, string name = "CPU Package")
    {
        var now = timestamp ?? Stopwatch.GetTimestamp();
        return new SensorSnapshot(generation, sequence, now, Stopwatch.Frequency, SensorServiceQuality.Complete,
            new CpuSensorReading("cpu/0", "CPU", "cpu/0/temp", name, 52.5, "LibreHardwareMonitorLib 0.9.6", now));
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        var timeout = Stopwatch.StartNew();
        while (!condition())
        {
            if (timeout.Elapsed > TimeSpan.FromSeconds(5)) throw new TimeoutException("Expected poll state was not reached.");
            await Task.Yield();
        }
    }

    private sealed class ControlledDelay
    {
        private readonly ConcurrentQueue<TaskCompletionSource> _waiters = new();
        public Task WaitAsync(TimeSpan _, CancellationToken token)
        {
            var waiter = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            token.Register(() => waiter.TrySetCanceled(token));
            _waiters.Enqueue(waiter);
            return waiter.Task;
        }
        public void ReleaseNext()
        {
            while (_waiters.TryDequeue(out var waiter))
                if (waiter.TrySetResult()) return;
            throw new InvalidOperationException("No pending delay to release.");
        }
    }

    private sealed class FakeConnection(params object[] replies) : ISensorTemperatureConnection
    {
        private readonly Queue<object> _replies = new(replies);
        private readonly TaskCompletionSource _snapshots = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int SubscribeCalls { get; private set; }
        public int SnapshotCalls { get; private set; }
        public bool Disposed { get; private set; }
        public Task SubscribeAsync(CancellationToken token) { SubscribeCalls++; return Task.CompletedTask; }
        public Task<SensorSnapshot> SnapshotAsync(CancellationToken token)
        {
            SnapshotCalls++;
            if (SnapshotCalls >= 1) _snapshots.TrySetResult();
            var reply = _replies.Dequeue();
            return reply switch { SensorSnapshot snapshot => Task.FromResult(snapshot), Exception error => Task.FromException<SensorSnapshot>(error), _ => throw new AssertionException("Unexpected sensor reply.") };
        }
        public async Task WaitForSnapshotsAsync(int count)
        {
            while (SnapshotCalls < count) { await _snapshots.Task; await Task.Yield(); }
        }
        public ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
    }
}
