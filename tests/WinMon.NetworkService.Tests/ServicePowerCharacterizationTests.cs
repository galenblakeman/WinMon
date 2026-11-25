using System.Diagnostics;
using System.Reflection;
using System.ServiceProcess;
using NUnit.Framework;
using WinMon.Monitoring;
using WinMon.NetworkService;

namespace WinMon.Tests;

[TestFixture]
[NonParallelizable]
[Category("Integration")]
public sealed class ServicePowerCharacterizationTests
{
    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    public async Task MissingManagerOrLifetimeAcknowledgesPowerWithoutTouchingTheCollector(bool manager, bool lifetime)
    {
        await using var f = new Host(manager, lifetime);
        await f.Subscribe();
        var generation = f.Manager.Snapshot(Host.Sid).Generation;
        Assert.That(f.Power(PowerBroadcastStatus.Suspend), Is.True);
        Assert.That(f.Created.Single().Stops, Is.Zero);
        Assert.That(f.Manager.Snapshot(Host.Sid).Generation, Is.EqualTo(generation));
        Assert.That(f.Manager.HasSubscriptions, Is.True);
    }

    [TestCase(PowerBroadcastStatus.ResumeAutomatic)]
    [TestCase(PowerBroadcastStatus.ResumeSuspend)]
    public async Task SuspendPreservesLeasesAndResumeStartsANewGeneration(PowerBroadcastStatus resume)
    {
        await using var f = new Host();
        await f.Subscribe();
        var generation = f.Manager.Snapshot(Host.Sid).Generation;
        Assert.That(f.Power(PowerBroadcastStatus.Suspend), Is.True);
        Assert.That(f.Power(PowerBroadcastStatus.Suspend), Is.True);
        Assert.That(f.Created.Single().Stops, Is.EqualTo(1));
        Assert.That(f.Manager.HasSubscriptions, Is.True);
        Assert.That(f.Manager.Snapshot(Host.Sid).Quality,
            Is.EqualTo(ProcessNetworkQuality.Unavailable | ProcessNetworkQuality.Discontinuity));
        await f.Subscribe();
        Assert.That(f.Created, Has.Count.EqualTo(1));
        Assert.That(f.Power(resume), Is.True);
        Assert.That(f.Power(resume), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(f.Created, Has.Count.EqualTo(2));
            Assert.That(f.Created[0].Stops, Is.EqualTo(1));
            Assert.That(f.Created[1].Starts, Is.EqualTo(1));
            Assert.That(f.Created[1].Stops, Is.Zero);
            Assert.That(f.Created[1].StartToken, Is.EqualTo(f.Token));
            Assert.That(f.Manager.Snapshot(Host.Sid).Generation, Is.Not.EqualTo(generation));
            Assert.That(f.Manager.HasSubscriptions, Is.True);
        });
    }

    private static IEnumerable<TestCaseData> IgnoredStatuses()
    {
        foreach (var status in Enum.GetValues<PowerBroadcastStatus>())
            if (status is not (PowerBroadcastStatus.Suspend or PowerBroadcastStatus.ResumeAutomatic or PowerBroadcastStatus.ResumeSuspend))
                foreach (var suspended in new[] { false, true })
                    yield return new TestCaseData(status, suspended);
    }

    [TestCaseSource(nameof(IgnoredStatuses))]
    public async Task OtherPowerNotificationsPreserveTheCurrentSuspensionState(PowerBroadcastStatus status, bool suspended)
    {
        await using var f = new Host();
        await f.Subscribe();
        if (suspended) await f.Manager.SetSuspendedAsync(true, CancellationToken.None);
        var before = f.Manager.Snapshot(Host.Sid);
        Assert.That(f.Power(status), Is.True);
        var after = f.Manager.Snapshot(Host.Sid);
        Assert.Multiple(() =>
        {
            Assert.That(f.Created, Has.Count.EqualTo(1));
            Assert.That(f.Created[0].Stops, Is.EqualTo(suspended ? 1 : 0));
            Assert.That(after.Generation, Is.EqualTo(before.Generation));
            Assert.That(after.Quality, Is.EqualTo(before.Quality));
            Assert.That(f.Manager.HasSubscriptions, Is.True);
        });
    }

    [TestCase(PowerBroadcastStatus.Suspend)]
    [TestCase(PowerBroadcastStatus.ResumeAutomatic)]
    [TestCase(PowerBroadcastStatus.ResumeSuspend)]
    public async Task PowerNotificationsWithoutSubscriptionsNeverStartACollector(PowerBroadcastStatus status)
    {
        await using var f = new Host();
        Assert.That(f.Power(status), Is.True);
        Assert.That(f.Created, Is.Empty);
        Assert.That(f.Manager.HasSubscriptions, Is.False);
    }

    [Test]
    public async Task SuspendAcknowledgesBeforeCollectorDisposalCompletes()
    {
        await using var f = new Host();
        await f.Subscribe();
        var previous = f.Created.Single();
        previous.StopGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Assert.That(f.Power(PowerBroadcastStatus.Suspend), Is.True);
        Assert.That(previous.Stops, Is.EqualTo(1));
        Assert.That(previous.StopGate.Task.IsCompleted, Is.False);
        previous.StopGate.SetResult();
        // Acquiring the real manager gate joins the preceding transition; the repeated state is a no-op.
        await f.Manager.SetSuspendedAsync(true, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(f.Manager.Snapshot(Host.Sid).Quality & ProcessNetworkQuality.Unavailable,
            Is.EqualTo(ProcessNetworkQuality.Unavailable));
        Assert.That(f.Manager.HasSubscriptions, Is.True);
    }

    [TestCase(PowerBroadcastStatus.ResumeAutomatic)]
    [TestCase(PowerBroadcastStatus.ResumeSuspend)]
    public async Task ResumeAcknowledgesBeforeReplacementStartupCompletes(PowerBroadcastStatus status)
    {
        await using var f = new Host();
        await f.Subscribe();
        var generation = f.Manager.Snapshot(Host.Sid).Generation;
        await f.Manager.SetSuspendedAsync(true, CancellationToken.None);
        f.NextStartGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Assert.That(f.Power(status), Is.True);
        Assert.That(f.Created, Has.Count.EqualTo(2));
        Assert.That(f.Created[1].Starts, Is.EqualTo(1));
        Assert.That(f.NextStartGate.Task.IsCompleted, Is.False);
        f.NextStartGate.SetResult();
        await f.Manager.SetSuspendedAsync(false, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(f.Manager.Snapshot(Host.Sid).Generation, Is.Not.EqualTo(generation));
        Assert.That(f.Created[1].StartToken, Is.EqualTo(f.Token));
    }

    [Test]
    public async Task OwnedCancellationDoesNotSuspendOrStopTheActiveCollector()
    {
        await using var f = new Host();
        await f.Subscribe();
        var generation = f.Manager.Snapshot(Host.Sid).Generation;
        f.Lifetime.Cancel();
        Assert.That(f.Power(PowerBroadcastStatus.Suspend), Is.True);
        await Host.Apply(f.Manager, true, f.Token);
        Assert.That(f.Created.Single().Stops, Is.Zero);
        Assert.That(f.Manager.Snapshot(Host.Sid).Generation, Is.EqualTo(generation));
        Assert.That(f.Service.ExitCode, Is.Zero);
    }

    [Test]
    public async Task AlreadyDisposedManagerIsAbsorbedDuringPowerNotification()
    {
        await using var f = new Host();
        await f.Subscribe();
        await f.Manager.DisposeAsync();
        Assert.That(f.Power(PowerBroadcastStatus.Suspend), Is.True);
        await Host.Apply(f.Manager, true, f.Token);
        Assert.That(f.Created.Single().Stops, Is.EqualTo(1));
        Assert.That(f.Service.ExitCode, Is.Zero);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task TransitionErrorsAreLoggedWithoutStoppingTheService(bool foreignCancellation)
    {
        await using var f = new Host();
        await f.Subscribe();
        await f.Manager.SetSuspendedAsync(true, CancellationToken.None);
        f.NextStartError = foreignCancellation
            ? new OperationCanceledException("foreign cancellation", new CancellationToken(true))
            : new IOException("collector startup failed");
        using var output = new StringWriter();
        using var listener = new TextWriterTraceListener(output);
        Trace.Listeners.Add(listener);
        try
        {
            await Host.Apply(f.Manager, false, f.Token);
            listener.Flush();
            Assert.That(output.ToString(), Does.Contain("Network collector power transition failed:")
                .And.Contain(f.NextStartError.Message));
            Assert.That(f.Created, Has.Count.EqualTo(2));
            Assert.That(f.Created[1].Stops, Is.EqualTo(1));
            Assert.That(f.Manager.HasSubscriptions, Is.True);
            Assert.That(f.Manager.Snapshot(Host.Sid).Quality & ProcessNetworkQuality.Unavailable,
                Is.EqualTo(ProcessNetworkQuality.Unavailable));
            Assert.That(f.Service.ExitCode, Is.Zero);
        }
        finally { Trace.Listeners.Remove(listener); }
    }

    [Test]
    public async Task ShutdownClearsPowerStateAndDisposesTheActiveCollectorOnlyOnce()
    {
        await using var f = new Host();
        await f.Subscribe();
        f.Stop();
        f.Stop();
        Assert.That(f.Token.IsCancellationRequested, Is.True);
        Assert.That(f.Power(PowerBroadcastStatus.ResumeAutomatic), Is.True);
        Assert.That(f.Created, Has.Count.EqualTo(1));
        Assert.That(f.Created[0].Stops, Is.EqualTo(1));
        Assert.That(f.Service.ExitCode, Is.Zero);
        Assert.Throws<ObjectDisposedException>(() => f.Manager.Snapshot(Host.Sid));
    }

    private sealed class Host : IAsyncDisposable
    {
        private const BindingFlags Members = BindingFlags.Instance | BindingFlags.NonPublic;
        internal const string Sid = "S-1-5-21-power-test";
        internal readonly NetworkMonitoringService Service = new();
        internal readonly CancellationTokenSource Lifetime = new();
        internal readonly CancellationToken Token;
        internal readonly NetworkCollectorManager Manager;
        internal readonly List<Collector> Created = [];
        private readonly List<IAsyncDisposable> _leases = [];
        internal TaskCompletionSource? NextStartGate;
        internal Exception? NextStartError;
        internal readonly Func<PowerBroadcastStatus, bool> Power;
        internal readonly Action Stop;
        internal static readonly Func<NetworkCollectorManager, bool, CancellationToken, Task> Apply =
            typeof(NetworkMonitoringService).GetMethod("ApplyPowerStateAsync", BindingFlags.Static | BindingFlags.NonPublic)!
                .CreateDelegate<Func<NetworkCollectorManager, bool, CancellationToken, Task>>();

        internal Host(bool manager = true, bool lifetime = true)
        {
            Token = Lifetime.Token;
            Manager = new([Sid], () =>
            {
                var collector = new Collector { StartGate = NextStartGate, StartError = NextStartError };
                Created.Add(collector);
                return collector;
            });
            var type = typeof(NetworkMonitoringService);
            type.GetField("_collectors", Members)!.SetValue(Service, manager ? Manager : null);
            type.GetField("_lifetime", Members)!.SetValue(Service, lifetime ? Lifetime : null);
            type.GetField("_run", Members)!.SetValue(Service, Task.CompletedTask);
            Power = type.GetMethod("OnPowerEvent", Members)!.CreateDelegate<Func<PowerBroadcastStatus, bool>>(Service);
            Stop = type.GetMethod("OnStop", Members)!.CreateDelegate<Action>(Service);
        }
        internal async Task Subscribe() => _leases.Add(await Manager.SubscribeAsync(Sid, CancellationToken.None));
        public async ValueTask DisposeAsync()
        {
            foreach (var collector in Created) { collector.StartGate?.TrySetResult(); collector.StopGate?.TrySetResult(); }
            Stop();
            await Manager.DisposeAsync();
            foreach (var lease in _leases) await lease.DisposeAsync();
            Lifetime.Dispose();
            Service.Dispose();
        }
    }

    private sealed class Collector : INetworkCollector
    {
        private readonly Guid _generation = Guid.NewGuid();
        internal int Starts, Stops;
        internal CancellationToken StartToken;
        internal TaskCompletionSource? StartGate, StopGate;
        internal Exception? StartError;
        public bool Failed => false;
        public ProcessNetworkFailure Failure => ProcessNetworkFailure.None;
        public Task StartAsync(CancellationToken cancellationToken)
        {
            Starts++; StartToken = cancellationToken;
            return StartError is { } error ? Task.FromException(error) : StartGate?.Task ?? Task.CompletedTask;
        }
        public ProcessNetworkSnapshot Snapshot(string ownerSid) =>
            new(_generation, 1, 100, 1, 90, 95, ProcessNetworkQuality.Provisional, 0, 0, []);
        public ValueTask DisposeAsync() { Stops++; return StopGate is { } gate ? new(gate.Task) : ValueTask.CompletedTask; }
    }
}
