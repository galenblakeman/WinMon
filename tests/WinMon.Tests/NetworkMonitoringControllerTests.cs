using NUnit.Framework;
using WinMon.Monitoring;

namespace WinMon.Tests;

[TestFixture]
public sealed class NetworkMonitoringControllerTests
{
    [Test]
    [NonParallelizable]
    public async Task ConnectionRecovery_LogsFailureStateAndOutageSummary()
    {
        var folder = Path.Combine(Path.GetTempPath(), "WinMon.NetworkLogTests." + Guid.NewGuid().ToString("N"));
        try
        {
            using (var log = new WinMon.Diagnostics.DiagnosticLog(folder, "App"))
            {
                System.Diagnostics.Trace.Listeners.Add(log);
                var available = false;
                await using var controller = new NetworkMonitoringController(_ => available
                        ? Task.FromResult<INetworkMonitoringConnection>(new FakeConnection()) : throw new IOException("private endpoint"),
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
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }

    [Test]
    public async Task LogicalSessionSurvivesRepairAndOnlyExplicitDisableStartsOver()
    {
        var available = false;
        await using var controller = new NetworkMonitoringController(_ => available
                ? Task.FromResult<INetworkMonitoringConnection>(new FakeConnection()) : throw new IOException(),
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
        await using var controller = new NetworkMonitoringController(_ => Task.FromResult<INetworkMonitoringConnection>(connection),
            (_, _, _) => { setupCalls++; return Task.CompletedTask; }, () => true);
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
            (_, _, _) => { setupCalls++; return Task.CompletedTask; }, () => true);
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
                ? Task.FromResult<INetworkMonitoringConnection>(connection) : throw new IOException(),
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
                ? Task.FromResult<INetworkMonitoringConnection>(connection) : throw new IOException(),
            async (_, verify, token) => { installed = true; await verify(token); committed = true; }, () => true);
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
                ? Task.FromResult<INetworkMonitoringConnection>(connection) : throw new IOException(),
            async (_, verify, token) => { installed = true; await verify(token); cancellation.Cancel(); token.ThrowIfCancellationRequested(); }, () => true);
        var result = await controller.ChangeAsync(true, cancellation.Token);
        Assert.That(result, Is.EqualTo(new NetworkMonitoringChangeResult(false, NetworkMonitoringState.Cancelled)));
        Assert.That(connection.Disposed, Is.True);
    }

    [Test]
    public async Task MissingSignedBundleFailsClosedBeforeSetup()
    {
        await using var controller = new NetworkMonitoringController(_ => throw new IOException(),
            (_, _, _) => throw new AssertionException("Setup must not run"), () => false);
        Assert.That((await controller.ChangeAsync(true, default)).State, Is.EqualTo(NetworkMonitoringState.SetupUnavailable));
    }

    [Test]
    public async Task ConnectionTimeoutAtStartupShowsUnavailableWithNoSetup()
    {
        await using var controller = new NetworkMonitoringController(_ => throw new OperationCanceledException("Internal connection timeout"),
            (_, _, _) => throw new AssertionException("No startup setup"), () => true);
        Assert.That((await controller.RestoreAsync(true, default)).State, Is.EqualTo(NetworkMonitoringState.Unavailable));
    }

    [Test]
    public async Task RestoreProtocolMismatchShowsUpdateRequiredWithoutSetup()
    {
        var setupCalls = 0;
        await using var controller = new NetworkMonitoringController(_ => throw new NetworkServiceProtocolMismatchException(2, 1),
            (_, _, _) => { setupCalls++; return Task.CompletedTask; }, () => true);
        Assert.That((await controller.RestoreAsync(true, default)).State, Is.EqualTo(NetworkMonitoringState.UpdateRequired));
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
            async (_, verify, token) => { setupCalls++; installed = true; await verify(token); }, () => true);
        Assert.That((await controller.ChangeAsync(true, default)).Success, Is.True);
        Assert.That(setupCalls, Is.EqualTo(1));
    }

    [Test]
    public async Task ChangeProtocolMismatchWithoutBundleStaysUpdateRequired()
    {
        await using var controller = new NetworkMonitoringController(_ => throw new NetworkServiceProtocolMismatchException(2, 1),
            (_, _, _) => throw new AssertionException("Setup must not run"), () => false);
        Assert.That((await controller.ChangeAsync(true, default)).State, Is.EqualTo(NetworkMonitoringState.UpdateRequired));
    }

    [Test]
    public async Task PollProtocolMismatchClearsMetricsAndShowsUpdateRequired()
    {
        var connection = new FakeConnection { FailureAfterInitialRead = new NetworkServiceProtocolMismatchException(2, 1) };
        var updateRequired = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var controller = new NetworkMonitoringController(_ => Task.FromResult<INetworkMonitoringConnection>(connection),
            (_, _, _) => Task.CompletedTask, () => true);
        controller.StateChanged += state => { if (state == NetworkMonitoringState.UpdateRequired) updateRequired.TrySetResult(); };
        Assert.That((await controller.ChangeAsync(true, default)).Success, Is.True);
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
        await using var controller = new NetworkMonitoringController(_ => throw new AssertionException("No service access"),
            (_, _, _) => throw new AssertionException("No setup access"), () => true, supported: false);
        Assert.That((await controller.ChangeAsync(true, default)).State, Is.EqualTo(NetworkMonitoringState.StoreUnavailable));
        Assert.That((await controller.RemoveAsync(default)).State, Is.EqualTo(NetworkMonitoringState.StoreUnavailable));
    }

    [TestCase(ProcessNetworkQuality.None, NetworkMonitoringState.Active)]
    [TestCase(ProcessNetworkQuality.WarmingUp, NetworkMonitoringState.WarmingUp)]
    [TestCase(ProcessNetworkQuality.Incomplete, NetworkMonitoringState.Incomplete)]
    [TestCase(ProcessNetworkQuality.Discontinuity, NetworkMonitoringState.Incomplete)]
    [TestCase(ProcessNetworkQuality.Unavailable, NetworkMonitoringState.Unavailable)]
    public void QualityIsNotReportedAsIdle(ProcessNetworkQuality quality, NetworkMonitoringState expected) =>
        Assert.That(NetworkMonitoringController.ToState(Snapshot(quality)), Is.EqualTo(expected));

    private static ProcessNetworkSnapshot Snapshot(ProcessNetworkQuality quality) =>
        new(Guid.NewGuid(), 1, 10, 1, 0, 5, quality, 0, 0, []);

    private sealed class FakeConnection : INetworkMonitoringConnection
    {
        internal bool Subscribed;
        internal bool Disposed;
        internal int Reads;
        internal ProcessNetworkQuality Quality = ProcessNetworkQuality.WarmingUp;
        internal Exception? FailureAfterInitialRead;
        public Task SubscribeAsync(CancellationToken token) { Subscribed = true; return Task.CompletedTask; }
        public Task<ProcessNetworkSnapshot> SnapshotAsync(CancellationToken token)
        {
            Reads++;
            return Reads > 1 && FailureAfterInitialRead is { } exception
                ? Task.FromException<ProcessNetworkSnapshot>(exception) : Task.FromResult(Snapshot(Quality));
        }
        public ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
    }
}
