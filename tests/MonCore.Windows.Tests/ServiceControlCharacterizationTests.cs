using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using NUnit.Framework;
using Native = WinMon.Monitoring.WindowsServices;

namespace WinMon.Tests;

[TestFixture]
public sealed class ServiceControlCharacterizationTests
{
    private static readonly string[] RestartTrace =
    [
        "open-manager", "check:manager", "open-service:svc:52", "check:service", "query:1",
        "open-process:42", "creation", "query:2", "exited", "stop", "query:3", "dispose:process",
        "start", "query:4", "dispose:service", "dispose:manager"
    ];

    private static Native.Status Status(uint state, uint pid = 42) => new() { State = state, ProcessId = pid };
    private static Task Execute(Backend backend, string action = "stop", int pid = 42, long token = 100,
        CancellationToken cancellation = default) =>
        Native.ExecuteAsync("svc", action, pid, token, cancellation, backend, backend.Clock);

    [TestCase("stop", 4u, 4u)]
    [TestCase("stop", 7u, 7u)]
    [TestCase("restart", 4u, 4u)]
    [TestCase("restart", 7u, 7u)]
    [TestCase("stop", 4u, 7u)]
    [TestCase("stop", 7u, 4u)]
    [TestCase("restart", 4u, 7u)]
    [TestCase("restart", 7u, 4u)]
    public async Task RunningAndPausedHostsUseExactControlOrderAndDisposeBeforeRestart(string action, uint state, uint recheckedState)
    {
        using var backend = new Backend(Status(state), Status(recheckedState), Status(1), Status(4));
        await Execute(backend, action);
        var expected = action == "restart" ? RestartTrace : RestartTrace.Take(12)
            .Select(entry => entry == "open-service:svc:52" ? "open-service:svc:36" : entry)
            .Concat(new[] { "dispose:service", "dispose:manager" }).ToArray();

        Assert.That(backend.Log, Is.EqualTo(expected));
        AssertDisposed(backend, true);
    }

    [TestCase(0, 0L)]
    [TestCase(-1, -1L)]
    [TestCase(int.MaxValue, long.MinValue)]
    public async Task StartIgnoresExpectedHostIdentityAndRequestsOnlyStartAccess(int pid, long token)
    {
        using var backend = new Backend(Status(1, 0), Status(4));
        await Execute(backend, "start", pid, token);

        Assert.That(backend.Log, Is.EqualTo(new[] { "open-manager", "check:manager", "open-service:svc:20",
            "check:service", "query:1", "start", "query:2", "dispose:service", "dispose:manager" }));
        AssertDisposed(backend, false);
    }

    [TestCase(0u)]
    [TestCase(2u)]
    [TestCase(3u)]
    [TestCase(4u)]
    [TestCase(5u)]
    [TestCase(6u)]
    [TestCase(7u)]
    [TestCase(8u)]
    [TestCase(uint.MaxValue)]
    public void StartRejectsEveryNonStoppedStateBeforeStarting(uint state)
    {
        using var backend = new Backend(Status(state));
        AssertError(Execute(backend, "start"), 1237);
        Assert.That(backend.Log, Is.EqualTo(new[] { "open-manager", "check:manager", "open-service:svc:20",
            "check:service", "query:1", "dispose:service", "dispose:manager" }));
        AssertDisposed(backend, false);
    }

    [TestCase(0u, 42u, 100L)]
    [TestCase(1u, 42u, 100L)]
    [TestCase(2u, 42u, 100L)]
    [TestCase(3u, 42u, 100L)]
    [TestCase(5u, 42u, 100L)]
    [TestCase(6u, 42u, 100L)]
    [TestCase(8u, 42u, 100L)]
    [TestCase(uint.MaxValue, 42u, 100L)]
    [TestCase(4u, 41u, 100L)]
    [TestCase(4u, 43u, 100L)]
    [TestCase(4u, uint.MaxValue, 100L)]
    [TestCase(4u, 42u, 0L)]
    [TestCase(4u, 42u, -1L)]
    [TestCase(4u, 42u, long.MinValue)]
    public void StaleInitialHostIsRejectedBeforeOpeningAProcess(uint state, uint actualPid, long expectedToken)
    {
        using var backend = new Backend(Status(state, actualPid));
        AssertError(Execute(backend, token: expectedToken), 1237);
        Assert.That(backend.Log, Is.EqualTo(new[] { "open-manager", "check:manager", "open-service:svc:36",
            "check:service", "query:1", "dispose:service", "dispose:manager" }));
        AssertDisposed(backend, false);
    }

    [TestCase(99L)]
    [TestCase(101L)]
    [TestCase(long.MaxValue)]
    public void MismatchedCreationTimeDisposesTheProcessWithoutRequeryOrControl(long expectedToken)
    {
        using var backend = new Backend(Status(4));
        AssertError(Execute(backend, token: expectedToken), 1237);
        Assert.That(backend.Log, Is.EqualTo(new[] { "open-manager", "check:manager", "open-service:svc:36",
            "check:service", "query:1", "open-process:42", "creation", "dispose:process", "dispose:service", "dispose:manager" }));
        AssertDisposed(backend, true);
    }

    [TestCase(-1, uint.MaxValue)]
    [TestCase(int.MinValue, 2147483648u)]
    public void UnsignedNativePidCannotMatchANegativeExpectedPid(int expectedPid, uint actualPid)
    {
        using var backend = new Backend(Status(4, actualPid));
        AssertError(Execute(backend, pid: expectedPid), 1237);
        Assert.That(backend.Log.Any(entry => entry.StartsWith("open-process:", StringComparison.Ordinal)), Is.False);
        AssertDisposed(backend, false);
    }

    [Test]
    public async Task FirstPositiveCreationTokenCanAuthorizeTheExactHost()
    {
        using var backend = new Backend(Status(4), Status(4), Status(1));
        backend.Process.Token = 1;
        await Execute(backend, token: 1);
        Assert.That(backend.Log, Does.Contain("stop"));
        AssertDisposed(backend, true);
    }

    [TestCase(1u, 42u, false)]
    [TestCase(4u, 43u, false)]
    [TestCase(4u, 42u, true)]
    public void HostRestartOrExitDuringValidationPreventsStop(uint state, uint pid, bool exited)
    {
        using var backend = new Backend(Status(4), Status(state, pid));
        backend.Process.Exited = exited;
        AssertError(Execute(backend), 1237);
        Assert.That(backend.Log, Is.EqualTo(RestartTrace.Take(9)
            .Select(entry => entry == "open-service:svc:52" ? "open-service:svc:36" : entry)
            .Concat(new[] { "dispose:process", "dispose:service", "dispose:manager" })));
        AssertDisposed(backend, true);
    }

    [Test]
    public void HasExitedFailurePrecedesTheStaleRequeryDecision()
    {
        using var backend = new Backend(Status(4), Status(1));
        backend.FailureAt = "exited";
        Assert.That(Assert.ThrowsAsync<Win32Exception>(async () => await Execute(backend)), Is.SameAs(backend.Failure));
        Assert.That(backend.Log, Does.Not.Contain("stop"));
        AssertDisposed(backend, true);
    }

    private static IEnumerable<TestCaseData> FailureStages() => RestartTrace
        .Where(stage => !stage.StartsWith("dispose:", StringComparison.Ordinal))
        .Select(stage => new TestCaseData(stage).SetName("NativeFailurePreservesOwnership_" + stage.Replace(':', '_')));

    [TestCaseSource(nameof(FailureStages))]
    public void NativeFailurePreservesOwnership(string stage)
    {
        using var backend = new Backend(Status(4), Status(4), Status(1), Status(4)) { FailureAt = stage };
        var error = Assert.ThrowsAsync<Win32Exception>(async () => await Execute(backend, "restart"));
        var index = Array.IndexOf(RestartTrace, stage);
        var cleanup = new List<string>();
        if (index > 5 && index < 11) cleanup.Add("dispose:process");
        if (index > 2) cleanup.Add("dispose:service");
        if (index > 0) cleanup.Add("dispose:manager");
        Assert.Multiple(() =>
        {
            Assert.That(error, Is.SameAs(backend.Failure));
            Assert.That(error!.NativeErrorCode, Is.EqualTo(5));
            Assert.That(backend.Log, Is.EqualTo(RestartTrace.Take(index + 1).Concat(cleanup)));
            Assert.That(backend.Manager.Disposals, Is.EqualTo(index > 0 ? 1 : 0));
            Assert.That(backend.Service.Disposals, Is.EqualTo(index > 2 ? 1 : 0));
            Assert.That(backend.Process.Disposals, Is.EqualTo(index > 5 ? 1 : 0));
        });
    }

    [TestCase("start")]
    [TestCase("stop")]
    [TestCase("restart")]
    public void AlreadyCancelledActionsKeepTheirExistingNativeSideEffectOrder(string action)
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        using var backend = action == "start" ? new Backend(Status(1)) : new Backend(Status(4), Status(4), Status(1));
        var task = Execute(backend, action, cancellation: cancellation.Token);
        if (action == "stop") Assert.DoesNotThrowAsync(async () => await task);
        else Assert.That(Assert.ThrowsAsync<OperationCanceledException>(async () => await task)!.CancellationToken, Is.EqualTo(cancellation.Token));

        Assert.Multiple(() =>
        {
            Assert.That(backend.Log, Does.Not.Contain("start"));
            Assert.That(backend.Log.Contains("stop"), Is.EqualTo(action != "start"));
            Assert.That(task.IsCanceled, Is.EqualTo(action != "stop"));
            Assert.That(backend.Clock.Created, Is.Zero);
        });
        AssertDisposed(backend, action != "start");
    }

    [Test]
    public void CancelledPendingStopStillControlsBeforeCancellingItsDelay()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        using var backend = new Backend(Status(4), Status(4), Status(2));
        var error = Assert.ThrowsAsync<TaskCanceledException>(async () => await Execute(backend, cancellation: cancellation.Token));
        Assert.Multiple(() =>
        {
            Assert.That(error!.CancellationToken, Is.EqualTo(cancellation.Token));
            Assert.That(backend.Log, Does.Contain("stop"));
            Assert.That(backend.Queries, Is.EqualTo(3));
            Assert.That(backend.Clock.Created, Is.Zero);
        });
        AssertDisposed(backend, true);
    }

    [Test]
    public async Task RestartHoldsTheProcessDuringTheExactPollingDelayThenDisposesBeforeStart()
    {
        using var backend = new Backend(Status(4), Status(4), Status(2), Status(1), Status(4));
        var task = Execute(backend, "restart");
        await backend.Clock.NextTimer();
        Assert.That(task.IsCompleted, Is.False);
        Assert.That(backend.Process.Disposals, Is.Zero);
        backend.Clock.Advance(TimeSpan.FromMilliseconds(250) - TimeSpan.FromTicks(1));
        Assert.That(task.IsCompleted, Is.False);
        Assert.That(backend.Queries, Is.EqualTo(3));
        backend.Clock.Advance(TimeSpan.FromTicks(1));
        await task;

        Assert.That(backend.Log, Is.EqualTo(RestartTrace.Take(11)
            .Concat(new[] { "query:4", "dispose:process", "start", "query:5", "dispose:service", "dispose:manager" })));
        Assert.That(backend.Clock.Pending, Is.Zero);
        AssertDisposed(backend, true);
    }

    [Test]
    public async Task CancellationDuringAPendingStopDisposesAllOwnedResourcesAndItsTimer()
    {
        using var cancellation = new CancellationTokenSource();
        using var backend = new Backend(Status(4), Status(4), Status(2));
        var task = Execute(backend, cancellation: cancellation.Token);
        await backend.Clock.NextTimer();
        Assert.That(backend.Process.Disposals, Is.Zero);
        cancellation.Cancel();
        Assert.That(Assert.ThrowsAsync<TaskCanceledException>(async () => await task)!.CancellationToken, Is.EqualTo(cancellation.Token));
        Assert.That(backend.Clock.Pending, Is.Zero);
        AssertDisposed(backend, true);
    }

    [Test]
    public async Task NativeQueryFailureAfterSuspensionKeepsItsErrorAndReleasesTheHeldProcess()
    {
        using var backend = new Backend(Status(4), Status(4), Status(2)) { FailureAt = "query:4" };
        var task = Execute(backend);
        await backend.Clock.NextTimer();
        backend.Clock.Advance(TimeSpan.FromMilliseconds(250));
        Assert.That(Assert.ThrowsAsync<Win32Exception>(async () => await task), Is.SameAs(backend.Failure));
        Assert.That(backend.Clock.Pending, Is.Zero);
        AssertDisposed(backend, true);
    }

    [TestCase("start")]
    [TestCase("stop")]
    [TestCase("restart")]
    public async Task TimeoutIsStrictlyAfterThirtySecondsAndOnlyAtAPoll(string action)
    {
        using var backend = action == "start" ? new Backend(Status(1), Status(2)) : new Backend(Status(4), Status(4), Status(2));
        backend.DefaultState = 2;
        var task = Execute(backend, action);
        await backend.Clock.NextTimer();
        backend.Clock.Advance(TimeSpan.FromSeconds(29.75));
        await backend.Clock.NextTimer();
        backend.Clock.Advance(TimeSpan.FromMilliseconds(250) - TimeSpan.FromTicks(1));
        Assert.That(task.IsCompleted, Is.False);
        backend.Clock.Advance(TimeSpan.FromTicks(1));
        await backend.Clock.NextTimer();
        Assert.That(task.IsCompleted, Is.False, "Exactly thirty seconds remains retryable.");
        backend.Clock.Advance(TimeSpan.FromMilliseconds(250));
        AssertError(task, 1053);
        Assert.Multiple(() =>
        {
            Assert.That(backend.Queries, Is.EqualTo(action == "start" ? 5 : 6));
            Assert.That(backend.Log.Count(entry => entry == "stop"), Is.EqualTo(action == "start" ? 0 : 1));
            Assert.That(backend.Log.Count(entry => entry == "start"), Is.EqualTo(action == "start" ? 1 : 0));
            Assert.That(backend.Clock.Pending, Is.Zero);
        });
        AssertDisposed(backend, action != "start");
    }

    [Test]
    public async Task ReachedStateWinsOverAnExpiredDeadlineAndRestartGetsAFreshWaitClock()
    {
        using var backend = new Backend(Status(4), Status(4), Status(2)) { DefaultState = 1 };
        backend.OnStart = () => backend.DefaultState = 2;
        var task = Execute(backend, "restart");
        await backend.Clock.NextTimer();
        backend.Clock.Advance(TimeSpan.FromSeconds(31));
        await backend.Clock.NextTimer();
        Assert.That(task.IsCompleted, Is.False);
        Assert.That(backend.Process.Disposals, Is.EqualTo(1));
        Assert.That(backend.Log, Does.Contain("start"));
        backend.DefaultState = 4;
        backend.Clock.Advance(TimeSpan.FromMilliseconds(250));
        await task;
        Assert.That(backend.Queries, Is.EqualTo(6));
        AssertDisposed(backend, true);
    }

    [Test]
    public async Task ConcurrentOperationsKeepDependenciesTimersAndOwnershipIndependent()
    {
        using var first = new Backend(Status(1), Status(2));
        using var second = new Backend(Status(1), Status(2));
        var a = Execute(first, "start");
        var b = Execute(second, "start");
        await first.Clock.NextTimer();
        await second.Clock.NextTimer();
        first.DefaultState = 4;
        first.Clock.Advance(TimeSpan.FromMilliseconds(250));
        await a;
        Assert.Multiple(() =>
        {
            Assert.That(b.IsCompleted, Is.False);
            Assert.That(second.Queries, Is.EqualTo(2));
            Assert.That(second.Service.Disposals, Is.Zero);
            Assert.That(second.Clock.Pending, Is.EqualTo(1));
        });
        second.DefaultState = 4;
        second.Clock.Advance(TimeSpan.FromMilliseconds(250));
        await b;
        AssertDisposed(first, false);
        AssertDisposed(second, false);
    }

    [TestCase(-1L)]
    [TestCase(0L)]
    [TestCase(1L)]
    public void SystemClockBoundaryConversionMatchesStopwatch(long offset)
    {
        var ticks = checked(30 * Stopwatch.Frequency + offset);
        Assert.Multiple(() =>
        {
            Assert.That(TimeProvider.System.TimestampFrequency, Is.EqualTo(Stopwatch.Frequency));
            Assert.That(TimeProvider.System.GetElapsedTime(0, ticks), Is.EqualTo(Stopwatch.GetElapsedTime(0, ticks)));
        });
    }

    private static void AssertError(Task task, int code) =>
        Assert.That(Assert.ThrowsAsync<Win32Exception>(async () => await task)!.NativeErrorCode, Is.EqualTo(code));

    private static void AssertDisposed(Backend backend, bool process)
    {
        Assert.Multiple(() =>
        {
            Assert.That(backend.Manager.Disposals, Is.EqualTo(1));
            Assert.That(backend.Service.Disposals, Is.EqualTo(1));
            Assert.That(backend.Process.Disposals, Is.EqualTo(process ? 1 : 0));
        });
    }

    private sealed class Backend : Native.IServiceOperations, IDisposable
    {
        private readonly Native.Status[] _statuses;
        private readonly ConcurrentQueue<string> _log = new();
        public Backend(params Native.Status[] statuses)
        {
            _statuses = statuses;
            Manager = new Handle(this, "manager"); Service = new Handle(this, "service"); Process = new Host(this);
        }
        public string[] Log => _log.ToArray();
        public Handle Manager { get; }
        public Handle Service { get; }
        public Host Process { get; }
        public ManualClock Clock { get; } = new();
        public string? FailureAt { get; set; }
        public Win32Exception Failure { get; } = new(5);
        public uint DefaultState { get; set; } = 4;
        public Action? OnStart { get; set; }
        public int Queries { get; private set; }
        public void Record(string entry) { _log.Enqueue(entry); if (FailureAt == entry) throw Failure; }
        public Native.IServiceHandle OpenManager() { Record("open-manager"); return Manager; }
        public Native.IServiceHandle OpenService(Native.IServiceHandle manager, string name, uint access)
        {
            Assert.That(manager, Is.SameAs(Manager)); Record($"open-service:{name}:{access}"); return Service;
        }
        public void CheckHandle(Native.IServiceHandle handle) => Record("check:" + ((Handle)handle).Name);
        public Native.Status Query(Native.IServiceHandle service)
        {
            Assert.That(service, Is.SameAs(Service)); Record("query:" + ++Queries);
            return Queries <= _statuses.Length ? _statuses[Queries - 1] : Status(DefaultState);
        }
        public Native.IServiceProcess OpenProcess(int processId) { Record("open-process:" + processId); return Process; }
        public void Stop(Native.IServiceHandle service) { Assert.That(service, Is.SameAs(Service)); Record("stop"); }
        public void Start(Native.IServiceHandle service) { Assert.That(service, Is.SameAs(Service)); Record("start"); OnStart?.Invoke(); }
        public void Dispose() => Clock.Dispose();
    }

    private sealed class Handle(Backend backend, string name) : Native.IServiceHandle
    {
        public string Name => name;
        public int Disposals { get; private set; }
        public void Dispose() { Disposals++; backend.Record("dispose:" + name); }
    }

    private sealed class Host(Backend backend) : Native.IServiceProcess
    {
        public bool Exited { get; set; }
        public long Token { get; set; } = 100;
        public long CreationTicks { get { backend.Record("creation"); return Token; } }
        public bool HasExited { get { backend.Record("exited"); return Exited; } }
        public int Disposals { get; private set; }
        public void Dispose() { Disposals++; backend.Record("dispose:process"); }
    }

    private sealed class ManualClock : TimeProvider, IDisposable
    {
        private readonly List<ManualTimer> _timers = [];
        private readonly SemaphoreSlim _created = new(0);
        private long _ticks;
        public int Created { get; private set; }
        public int Pending { get { lock (_timers) return _timers.Count; } }
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => Interlocked.Read(ref _ticks);
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            Assert.That(period, Is.EqualTo(Timeout.InfiniteTimeSpan));
            var timer = new ManualTimer(this, callback, state);
            timer.Change(dueTime, period);
            lock (_timers) { _timers.Add(timer); Created++; }
            _created.Release();
            return timer;
        }
        public async Task NextTimer() => Assert.That(await _created.WaitAsync(TimeSpan.FromSeconds(5)), Is.True,
            "The operation must register its next controlled polling delay.");
        public void Advance(TimeSpan elapsed)
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
        public void Dispose() { lock (_timers) foreach (var timer in _timers.ToArray()) timer.Dispose(); _created.Dispose(); }
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
            public void Dispose() { lock (clock._timers) { _disposed = true; clock._timers.Remove(this); } }
            public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        }
    }
}
