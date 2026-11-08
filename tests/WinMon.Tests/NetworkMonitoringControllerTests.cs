using System.Diagnostics;
using NUnit.Framework;
using WinMon.Diagnostics;
using WinMon.Monitoring;
using WinMon.Preferences;

namespace WinMon.Tests;

[TestFixture]
public sealed class NetworkMonitoringControllerTests
{
    [TestCase(false)]
    [TestCase(true)]
    public async Task InstallerEnablesPreferenceOnlyAfterSuccessfulSetup(bool cancel)
    {
        var folder = Path.Combine(Path.GetTempPath(), "WinMon.InstallerTests." + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new PreferenceStore(Path.Combine(folder, "settings.json"));
            await store.SaveAsync(new MonitorPreferences { TopProcessCount = 20 });
            var installed = false;
            await using var controller = new NetworkMonitoringController(_ => installed
                    ? Task.FromResult<INetworkMonitoringConnection>(new FakeConnection())
                    : throw new IOException("Service not installed"),
                async (operation, verify, token) =>
                {
                    Assert.That(operation, Is.EqualTo("install"));
                    if (cancel) throw new System.ComponentModel.Win32Exception(1223);
                    installed = true;
                    await verify(token);
                }, () => true, supported: true);

            var result = await App.InstallNetworkServiceAsync(controller, store);
            var saved = await store.LoadAsync();
            Assert.Multiple(() =>
            {
                Assert.That(result, Is.EqualTo(cancel ? 1 : 0));
                Assert.That(saved.ProcessNetworkMonitoringEnabled, Is.EqualTo(!cancel));
                Assert.That(saved.TopProcessCount, Is.EqualTo(20));
            });
        }
        finally
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
        }
    }

    [Test]
    [NonParallelizable]
    public async Task ConnectionRecovery_LogsFailureStateAndOutageSummary()
    {
        var folder = Path.Combine(Path.GetTempPath(), "WinMon.NetworkLogTests." + Guid.NewGuid().ToString("N"));
        try
        {
            using (var log = new DiagnosticLog(folder, "App"))
            {
                Trace.Listeners.Add(log);
                var available = false;
                await using var controller = new NetworkMonitoringController(_ => available
                        ? Task.FromResult<INetworkMonitoringConnection>(new FakeConnection())
                        : throw new IOException("private endpoint"),
                    (_, _, _) => throw new AssertionException("No setup needed"), () => true);
                await controller.RestoreAsync(true, default);
                available = true;
                await controller.ChangeAsync(true, default);
            }

            var text = File.ReadAllText(Path.Combine(folder, "winmon.log"));
            Assert.That(text, Does.Contain("Network.ConnectionFailed: IOException"));
            Assert.That(text, Does.Contain("Network.State from=Enabling to=Unavailable"));
            Assert.That(text, Does.Contain("Network.Connected attempts=1 outageMs="));
            Assert.That(text, Does.Not.Contain("private endpoint"));
        }
        finally
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
        }
    }

    [Test]
    [NonParallelizable]
    public async Task SnapshotDiagnostics_RecordCoverageAndCollectorFailureWithoutProcessMetadata()
    {
        var folder = Path.Combine(Path.GetTempPath(), "WinMon.NetworkQualityTests." + Guid.NewGuid().ToString("N"));
        try
        {
            using (var log = new DiagnosticLog(folder, "App"))
            {
                Trace.Listeners.Add(log);
                var connection = new DiagnosticConnection { Unavailable = true };
                await using var controller = new NetworkMonitoringController(
                    _ => Task.FromResult<INetworkMonitoringConnection>(connection),
                    (_, _, _) => throw new AssertionException("No setup needed"), () => true);
                await controller.RestoreAsync(true, default);
                connection.Unavailable = false;
                await controller.ChangeAsync(true, default);
            }

            var text = File.ReadAllText(Path.Combine(folder, "winmon.log"));
            Assert.That(text, Does.Contain("reasons=NativeLoss"));
            Assert.That(text, Does.Contain("failure=ClockDiscontinuity"));
            Assert.That(text, Does.Contain("eventsLost=3 lateEvents=4 droppedEvents=5"));
            Assert.That(text, Does.Contain("windowMs=5000 lagMs=3000"));
            Assert.That(text, Does.Not.Contain("987654"));
        }
        finally
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
        }
    }

    private sealed class DiagnosticConnection : INetworkMonitoringConnection
    {
        internal bool Unavailable { get; set; }

        public Task SubscribeAsync(CancellationToken token)
        {
            return Task.CompletedTask;
        }

        public Task<ProcessNetworkSnapshot> SnapshotAsync(CancellationToken token)
        {
            return Task.FromResult(
                new ProcessNetworkSnapshot(Guid.NewGuid(), 1, 110, 10, 30, 80,
                    ProcessNetworkQuality.Incomplete |
                    (Unavailable ? ProcessNetworkQuality.Unavailable : ProcessNetworkQuality.Provisional),
                    3, 4, [new ProcessNetworkReading(987654, 987654, 123, 456, 10, 20)])
                {
                    IncompleteReasons = ProcessNetworkIncompleteReason.NativeLoss, DroppedEvents = 5,
                    Failure = Unavailable ? ProcessNetworkFailure.ClockDiscontinuity : ProcessNetworkFailure.None
                });
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }

    [Test]
    public async Task LogicalSessionSurvivesRepairAndOnlyExplicitDisableStartsOver()
    {
        var available = false;
        await using var controller = new NetworkMonitoringController(_ => available
                ? Task.FromResult<INetworkMonitoringConnection>(new FakeConnection())
                : throw new IOException(),
            (_, _, _) => throw new AssertionException("No setup needed"), () => true);
        await controller.RestoreAsync(true, default);
        var initial = controller.Sample.SessionId;
        Assert.That(initial, Is.Not.EqualTo(Guid.Empty));
        available = true;
        Assert.That((await controller.ChangeAsync(true, default)).Success, Is.True);
        Assert.That(controller.Sample.SessionId, Is.EqualTo(initial));
        await controller.ChangeAsync(false, default);
        Assert.That(controller.Sample.SessionId, Is.EqualTo(Guid.Empty));
        await controller.ChangeAsync(true, default);
        Assert.That(controller.Sample.SessionId, Is.Not.EqualTo(initial));
    }

    [Test]
    public async Task AuthorizedServiceEnablesWithoutSetupAndDisableReleasesIt()
    {
        var connection = new FakeConnection();
        var setupCalls = 0;
        await using var controller = new NetworkMonitoringController(
            _ => Task.FromResult<INetworkMonitoringConnection>(connection),
            (_, _, _) =>
            {
                setupCalls++;
                return Task.CompletedTask;
            }, () => true);
        Assert.That((await controller.ChangeAsync(true, default)).Success, Is.True);
        Assert.That(connection.Subscribed, Is.True);
        Assert.That(controller.State, Is.EqualTo(NetworkMonitoringState.WarmingUp));
        Assert.That((await controller.ChangeAsync(false, default)).Success, Is.True);
        Assert.That(connection.Disposed, Is.True);
        Assert.That(controller.LatestSnapshot, Is.Null);
        Assert.That(setupCalls, Is.Zero);
    }

    [Test]
    public async Task StartupFailureNeverInvokesSetup()
    {
        var setupCalls = 0;
        await using var controller = new NetworkMonitoringController(_ => throw new IOException(),
            (_, _, _) =>
            {
                setupCalls++;
                return Task.CompletedTask;
            }, () => true);
        Assert.That((await controller.RestoreAsync(true, default)).Success, Is.False);
        Assert.That(controller.State, Is.EqualTo(NetworkMonitoringState.Unavailable));
        Assert.That(setupCalls, Is.Zero);
    }

    [Test]
    public async Task SetupCommitsOnlyAfterOriginalClientSubscribesAndReadsHealth()
    {
        var installed = false;
        var committed = false;
        var connection = new FakeConnection();
        await using var controller = new NetworkMonitoringController(_ => installed
                ? Task.FromResult<INetworkMonitoringConnection>(connection)
                : throw new IOException(),
            async (operation, verify, token) =>
            {
                Assert.That(operation, Is.EqualTo("install"));
                installed = true;
                await verify(token);
                Assert.That(connection.Subscribed, Is.True);
                Assert.That(connection.Reads, Is.EqualTo(1));
                committed = true;
            }, () => true);
        Assert.That((await controller.ChangeAsync(true, default)).Success, Is.True);
        Assert.That(committed, Is.True);
    }

    [Test]
    public async Task UnavailableHealthRejectsSetupAndDisposesSubscription()
    {
        var installed = false;
        var committed = false;
        var connection = new FakeConnection { Quality = ProcessNetworkQuality.Unavailable };
        await using var controller = new NetworkMonitoringController(_ => installed
                ? Task.FromResult<INetworkMonitoringConnection>(connection)
                : throw new IOException(),
            async (_, verify, token) =>
            {
                installed = true;
                await verify(token);
                committed = true;
            }, () => true);
        Assert.That((await controller.ChangeAsync(true, default)).Success, Is.False);
        Assert.That(committed, Is.False);
        Assert.That(connection.Disposed, Is.True);
    }

    [Test]
    public async Task CancellationAfterHealthDoesNotLeaveCollectionRunning()
    {
        using var cancellation = new CancellationTokenSource();
        var installed = false;
        var connection = new FakeConnection();
        await using var controller = new NetworkMonitoringController(_ => installed
                ? Task.FromResult<INetworkMonitoringConnection>(connection)
                : throw new IOException(),
            async (_, verify, token) =>
            {
                installed = true;
                await verify(token);
                cancellation.Cancel();
                token.ThrowIfCancellationRequested();
            }, () => true);
        var result = await controller.ChangeAsync(true, cancellation.Token);
        Assert.That(result, Is.EqualTo(new NetworkMonitoringChangeResult(false, NetworkMonitoringState.Cancelled)));
        Assert.That(connection.Disposed, Is.True);
    }

    [Test]
    public async Task MissingSignedBundleFailsClosedBeforeSetup()
    {
        await using var controller = new NetworkMonitoringController(_ => throw new IOException(),
            (_, _, _) => throw new AssertionException("Setup must not run"), () => false);
        Assert.That((await controller.ChangeAsync(true, default)).State,
            Is.EqualTo(NetworkMonitoringState.SetupUnavailable));
    }

    [Test]
    public async Task ConnectionTimeoutAtStartupShowsUnavailableWithNoSetup()
    {
        await using var controller = new NetworkMonitoringController(
            _ => throw new OperationCanceledException("Internal connection timeout"),
            (_, _, _) => throw new AssertionException("No startup setup"), () => true);
        Assert.That((await controller.RestoreAsync(true, default)).State,
            Is.EqualTo(NetworkMonitoringState.Unavailable));
    }

    [Test]
    public async Task RestoreProtocolMismatchShowsUpdateRequiredWithoutSetup()
    {
        var setupCalls = 0;
        await using var controller = new NetworkMonitoringController(
            _ => throw new NetworkServiceProtocolMismatchException(2, 1),
            (_, _, _) =>
            {
                setupCalls++;
                return Task.CompletedTask;
            }, () => true);
        Assert.That((await controller.RestoreAsync(true, default)).State,
            Is.EqualTo(NetworkMonitoringState.UpdateRequired));
        Assert.That(setupCalls, Is.Zero);
    }

    [Test]
    public async Task ChangeProtocolMismatchUsesSignedRepairWhenAvailable()
    {
        var installed = false;
        var setupCalls = 0;
        var connection = new FakeConnection();
        await using var controller = new NetworkMonitoringController(_ => installed
                ? Task.FromResult<INetworkMonitoringConnection>(connection)
                : throw new NetworkServiceProtocolMismatchException(2, 1),
            async (_, verify, token) =>
            {
                setupCalls++;
                installed = true;
                await verify(token);
            }, () => true);
        Assert.That((await controller.ChangeAsync(true, default)).Success, Is.True);
        Assert.That(setupCalls, Is.EqualTo(1));
    }

    [Test]
    public async Task ChangeProtocolMismatchWithoutBundleStaysUpdateRequired()
    {
        await using var controller = new NetworkMonitoringController(
            _ => throw new NetworkServiceProtocolMismatchException(2, 1),
            (_, _, _) => throw new AssertionException("Setup must not run"), () => false);
        Assert.That((await controller.ChangeAsync(true, default)).State,
            Is.EqualTo(NetworkMonitoringState.UpdateRequired));
    }

    [Test]
    public async Task PollProtocolMismatchClearsMetricsAndShowsUpdateRequired()
    {
        var delay = new PollDelay();
        var connection = new FakeConnection
            { FailureAfterInitialRead = new NetworkServiceProtocolMismatchException(2, 1) };
        var updateRequired = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var controller = new NetworkMonitoringController(
            _ => Task.FromResult<INetworkMonitoringConnection>(connection),
            (_, _, _) => Task.CompletedTask, () => true, delay: delay.Wait);
        controller.StateChanged += state =>
        {
            if (state == NetworkMonitoringState.UpdateRequired) updateRequired.TrySetResult();
        };
        Assert.That((await controller.ChangeAsync(true, default)).Success, Is.True);
        (await delay.Next()).Complete();
        await updateRequired.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Multiple(() =>
        {
            Assert.That(controller.State, Is.EqualTo(NetworkMonitoringState.UpdateRequired));
            Assert.That(controller.LatestSnapshot, Is.Null);
        });
    }

    [Test]
    public async Task StoreDoesNotAccessServiceOrSetup()
    {
        await using var controller = new NetworkMonitoringController(
            _ => throw new AssertionException("No service access"),
            (_, _, _) => throw new AssertionException("No setup access"), () => true, false);
        Assert.That((await controller.ChangeAsync(true, default)).State,
            Is.EqualTo(NetworkMonitoringState.StoreUnavailable));
        Assert.That((await controller.RemoveAsync(default)).State, Is.EqualTo(NetworkMonitoringState.StoreUnavailable));
    }

    [TestCase(null)]
    [TestCase(typeof(IOException))]
    [TestCase(typeof(OperationCanceledException))]
    public async Task InterruptedMonitoringReconnectsWithoutSetupAndPreservesSession(Type? failureType)
    {
        var delay = new PollDelay();
        var failed = new FakeConnection
        {
            QualityAfterInitialRead = ProcessNetworkQuality.Unavailable,
            FailureAfterInitialRead = failureType is null ? null : (Exception)Activator.CreateInstance(failureType)!
        };
        var healthy = new FakeConnection { Quality = ProcessNetworkQuality.None };
        var connections = 0;
        var unavailable = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var recovered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var controller = new NetworkMonitoringController(_ =>
        {
            if (Interlocked.Increment(ref connections) == 1)
                return Task.FromResult<INetworkMonitoringConnection>(failed);
            Assert.That(failed.Disposed, Is.True);
            return Task.FromResult<INetworkMonitoringConnection>(healthy);
        }, (_, _, _) => throw new AssertionException("Recovery must not run setup"), () => true, delay: delay.Wait);
        controller.StateChanged += state =>
        {
            if (state == NetworkMonitoringState.Unavailable) unavailable.TrySetResult();
            if (state == NetworkMonitoringState.Active) recovered.TrySetResult();
        };
        await controller.ChangeAsync(true, default);
        var session = controller.Sample.SessionId;
        (await delay.Next()).Complete();
        await unavailable.Task.WaitAsync(TimeSpan.FromSeconds(4));
        Assert.That(controller.LatestSnapshot, Is.Null);
        (await delay.Next()).Complete();
        await recovered.Task.WaitAsync(TimeSpan.FromSeconds(9));
        Assert.Multiple(() =>
        {
            Assert.That(controller.Sample.SessionId, Is.EqualTo(session));
            Assert.That(controller.LatestSnapshot, Is.Not.Null);
            Assert.That(healthy.Subscribed, Is.True);
            Assert.That(connections, Is.EqualTo(2));
        });
    }

    [Test]
    public async Task StartupRecoveryKeepsRetryingUntilServiceReturns()
    {
        var delay = new PollDelay();
        var attempts = 0;
        var recovered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var controller = new NetworkMonitoringController(_ => Interlocked.Increment(ref attempts) < 3
                ? throw new IOException()
                : Task.FromResult<INetworkMonitoringConnection>(new FakeConnection()),
            (_, _, _) => throw new AssertionException("Recovery must not run setup"), () => true, delay: delay.Wait);
        controller.StateChanged += state =>
        {
            if (state == NetworkMonitoringState.WarmingUp) recovered.TrySetResult();
        };
        await controller.RestoreAsync(true, default);
        var session = controller.Sample.SessionId;
        (await delay.Next()).Complete();
        (await delay.Next()).Complete();
        await recovered.Task.WaitAsync(TimeSpan.FromSeconds(15));
        Assert.That(controller.Sample.SessionId, Is.EqualTo(session));
        Assert.That(attempts, Is.EqualTo(3));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task DisableOrShutdownCancelsAnInFlightRecovery(bool shutdown)
    {
        var delay = new PollDelay();
        var attempts = 0;
        var reconnecting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var controller = new NetworkMonitoringController(async token =>
        {
            if (Interlocked.Increment(ref attempts) == 1) throw new IOException();
            reconnecting.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
            }
            catch (OperationCanceledException)
            {
                cancelled.TrySetResult();
                throw;
            }

            throw new AssertionException("Recovery must be cancelled");
        }, (_, _, _) => throw new AssertionException("No setup"), () => true, delay: delay.Wait);
        try
        {
            await controller.RestoreAsync(true, default);
            (await delay.Next()).Complete();
            await reconnecting.Task.WaitAsync(TimeSpan.FromSeconds(9));
            if (shutdown)
            {
                await controller.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(3));
            }
            else
            {
                await controller.ChangeAsync(false, default).WaitAsync(TimeSpan.FromSeconds(3));
                Assert.That(controller.State, Is.EqualTo(NetworkMonitoringState.Disabled));
                Assert.That(controller.Sample.SessionId, Is.EqualTo(Guid.Empty));
            }

            Assert.That(cancelled.Task.IsCompletedSuccessfully, Is.True);
            Assert.That(controller.LatestSnapshot, Is.Null);
        }
        finally
        {
            if (!shutdown) await controller.DisposeAsync();
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task DisableOrShutdownCancelsPendingRetry(bool shutdown)
    {
        var delay = new PollDelay();
        var attempts = 0;
        var controller = new NetworkMonitoringController(_ =>
        {
            Interlocked.Increment(ref attempts);
            throw new IOException();
        }, (_, _, _) => throw new AssertionException("No setup"), () => true, delay: delay.Wait);
        try
        {
            await controller.RestoreAsync(true, default);
            var retry = await delay.Next();
            Assert.That(retry.Interval, Is.EqualTo(TimeSpan.FromSeconds(5)));
            if (shutdown)
                await controller.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(3));
            else
                await controller.ChangeAsync(false, default).WaitAsync(TimeSpan.FromSeconds(3));
            Assert.Multiple(() =>
            {
                Assert.That(retry.Token.IsCancellationRequested, Is.True);
                Assert.That(attempts, Is.EqualTo(1));
                Assert.That(controller.LatestSnapshot, Is.Null);
            });
        }
        finally
        {
            if (!shutdown) await controller.DisposeAsync();
        }
    }

    [Test]
    public async Task RepeatedOutagesDisposeEachConnectionAndKeepTheLogicalSession()
    {
        var delay = new PollDelay();
        var connections = new[]
        {
            new FakeConnection { FailureAfterInitialRead = new IOException() },
            new FakeConnection { QualityAfterInitialRead = ProcessNetworkQuality.Unavailable },
            new FakeConnection { Quality = ProcessNetworkQuality.None }
        };
        var opened = 0;
        await using var controller = new NetworkMonitoringController(
            _ => Task.FromResult<INetworkMonitoringConnection>(connections[opened++]),
            (_, _, _) => throw new AssertionException("Recovery must not run setup"), () => true, delay: delay.Wait);
        await controller.RestoreAsync(true, default);
        var session = controller.Sample.SessionId;
        for (var index = 0; index < 2; index++)
        {
            var poll = await delay.Next();
            Assert.That(poll.Interval, Is.EqualTo(TimeSpan.FromSeconds(1)));
            poll.Complete();
            var retry = await delay.Next();
            Assert.Multiple(() =>
            {
                Assert.That(retry.Interval, Is.EqualTo(TimeSpan.FromSeconds(5)));
                Assert.That(controller.State, Is.EqualTo(NetworkMonitoringState.Unavailable));
                Assert.That(controller.LatestSnapshot, Is.Null);
                Assert.That(connections[index].Disposed, Is.True);
            });
            retry.Complete();
        }

        await delay.Next();
        Assert.Multiple(() =>
        {
            Assert.That(controller.State, Is.EqualTo(NetworkMonitoringState.Active));
            Assert.That(controller.Sample.SessionId, Is.EqualTo(session));
            Assert.That(opened, Is.EqualTo(3));
        });
        await controller.ChangeAsync(false, default);
        Assert.That(connections[2].Disposed, Is.True);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task LateRecoveryConnectionIsDisposedAfterDisableOrShutdown(bool shutdown)
    {
        var delay = new PollDelay();
        var reconnecting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var late = new TaskCompletionSource<INetworkMonitoringConnection>(TaskCreationOptions.RunContinuationsAsynchronously);
        var connection = new FakeConnection();
        var attempts = 0;
        var controller = new NetworkMonitoringController(async token =>
        {
            if (++attempts == 1) throw new IOException();
            using var registration = token.Register(() => cancelled.TrySetResult());
            reconnecting.SetResult();
            return await late.Task;
        }, (_, _, _) => throw new AssertionException("No setup"), () => true, delay: delay.Wait);
        Task? stop = null;
        try
        {
            await controller.RestoreAsync(true, default);
            (await delay.Next()).Complete();
            await reconnecting.Task.WaitAsync(TimeSpan.FromSeconds(3));
            stop = shutdown ? controller.DisposeAsync().AsTask() : controller.ChangeAsync(false, default);
            await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(3));
            late.SetResult(connection);
            await stop.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.Multiple(() =>
            {
                Assert.That(connection.Disposed, Is.True);
                Assert.That(controller.LatestSnapshot, Is.Null);
                Assert.That(attempts, Is.EqualTo(2));
                if (!shutdown)
                {
                    Assert.That(controller.State, Is.EqualTo(NetworkMonitoringState.Disabled));
                    Assert.That(controller.Sample.SessionId, Is.EqualTo(Guid.Empty));
                }
            });
        }
        finally
        {
            late.TrySetResult(connection);
            if (shutdown && stop is not null) await stop.WaitAsync(TimeSpan.FromSeconds(3));
            else await controller.DisposeAsync();
        }
    }

    private sealed class PollDelay
    {
        private readonly System.Threading.Channels.Channel<Step> _steps =
            System.Threading.Channels.Channel.CreateUnbounded<Step>();

        internal Task Wait(TimeSpan interval, CancellationToken token)
        {
            var step = new Step(interval, token);
            _steps.Writer.TryWrite(step);
            return step.Completion.Task.WaitAsync(token);
        }

        internal Task<Step> Next() => _steps.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(3));

        internal sealed record Step(TimeSpan Interval, CancellationToken Token)
        {
            internal TaskCompletionSource Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
            internal void Complete() => Completion.SetResult();
        }
    }

    [TestCase(ProcessNetworkQuality.None, NetworkMonitoringState.Active)]
    [TestCase(ProcessNetworkQuality.WarmingUp, NetworkMonitoringState.WarmingUp)]
    [TestCase(ProcessNetworkQuality.Incomplete, NetworkMonitoringState.Incomplete)]
    [TestCase(ProcessNetworkQuality.Discontinuity, NetworkMonitoringState.Incomplete)]
    [TestCase(ProcessNetworkQuality.Unavailable, NetworkMonitoringState.Unavailable)]
    public void QualityIsNotReportedAsIdle(ProcessNetworkQuality quality, NetworkMonitoringState expected)
    {
        Assert.That(NetworkMonitoringController.ToState(Snapshot(quality)), Is.EqualTo(expected));
    }

    private static ProcessNetworkSnapshot Snapshot(ProcessNetworkQuality quality)
    {
        return new ProcessNetworkSnapshot(Guid.NewGuid(), 1, 10, 1, 0, 5, quality, 0, 0, []);
    }

    private sealed class FakeConnection : INetworkMonitoringConnection
    {
        internal bool Subscribed;
        internal bool Disposed;
        internal int Reads;
        internal ProcessNetworkQuality Quality = ProcessNetworkQuality.WarmingUp;
        internal ProcessNetworkQuality? QualityAfterInitialRead;
        internal Exception? FailureAfterInitialRead;

        public Task SubscribeAsync(CancellationToken token)
        {
            Subscribed = true;
            return Task.CompletedTask;
        }

        public Task<ProcessNetworkSnapshot> SnapshotAsync(CancellationToken token)
        {
            Reads++;
            return Reads > 1 && FailureAfterInitialRead is { } exception
                ? Task.FromException<ProcessNetworkSnapshot>(exception)
                : Task.FromResult(Snapshot(Reads > 1 ? QualityAfterInitialRead ?? Quality : Quality));
        }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }
}
