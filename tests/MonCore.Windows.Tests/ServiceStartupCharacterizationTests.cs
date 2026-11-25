using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using NUnit.Framework;
using WinMon.Monitoring;
using StartupSession = WinMon.Monitoring.IServiceStartupScope;

namespace WinMon.Tests;

[TestFixture]
public sealed class ServiceStartupCharacterizationTests
{
    [Test]
    public async Task ActualDefaultPreCanceledEntryReturnsCanceledTaskWithoutSynchronousThrow()
    {
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        Task operation = null!;
        Assert.DoesNotThrow(() => operation = NetworkServiceIdentity.EnsureRunningAsync(canceled.Token));
        var error = await Cancellation(operation);
        Assert.That(error.CancellationToken, Is.EqualTo(canceled.Token));
    }

    [Test]
    public async Task PreCancellationPreventsOpeningOrDisposingAService()
    {
        using var s = new Scenario(4);
        s.Cancel.Cancel();
        var error = await Cancellation(s.Run());
        Assert.Multiple(() =>
        {
            Assert.That(error.CancellationToken, Is.EqualTo(s.Cancel.Token));
            Assert.That(s.Log, Is.Empty);
            Assert.That(s.Service.Disposals, Is.Zero);
        });
    }

    [TestCase("access")]
    [TestCase("missing")]
    [TestCase("native")]
    [TestCase("cancel-without-request")]
    public async Task OpenFailuresRemainOnTheReturnedTask(string kind)
    {
        using var s = new Scenario(4);
        Exception failure = kind switch
        {
            "access" => new UnauthorizedAccessException("fixture access"),
            "missing" => new InvalidOperationException("fixture missing"),
            "native" => new Win32Exception(5, "fixture native"),
            _ => new OperationCanceledException(CancellationToken.None)
        };
        s.OpenFailure = failure;
        Task operation = null!;
        Assert.DoesNotThrow(() => operation = s.Run());
        var observed = await Failure(operation);
        Assert.Multiple(() =>
        {
            Assert.That(observed, Is.SameAs(failure));
            Assert.That(operation.IsCanceled, Is.EqualTo(failure is OperationCanceledException));
            Assert.That(s.Log, Is.EqualTo(new[] { "open:21" }));
            Assert.That(s.Service.Disposals, Is.Zero);
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task UntrustedConfigurationWinsBeforeStatusStartAndLaterCancellation(bool cancelOnOpen)
    {
        using var s = new Scenario(4);
        s.Service.Trusted = false;
        if (cancelOnOpen) s.OnOpen = s.Cancel.Cancel;
        var error = await Failure(s.Run());
        Assert.Multiple(() =>
        {
            Assert.That(error, Is.TypeOf<UnauthorizedAccessException>());
            Assert.That(error.Message, Is.EqualTo("Registered network service configuration is not trusted."));
            Assert.That(s.Log, Is.EqualTo(new[] { "open:21", "trust", "dispose" }));
            Assert.That(s.Service.Disposals, Is.EqualTo(1));
        });
    }

    [TestCase("none")]
    [TestCase("open")]
    [TestCase("query")]
    public async Task AlreadyRunningReturnsBeforeStartAndSecondCancellationCheck(string cancelAt)
    {
        using var s = new Scenario(4);
        if (cancelAt == "open") s.OnOpen = s.Cancel.Cancel;
        if (cancelAt == "query") s.Service.OnQuery = _ => s.Cancel.Cancel();
        await s.Run();
        Assert.Multiple(() =>
        {
            Assert.That(s.Log, Is.EqualTo(new[] { "open:21", "trust", "query:1", "dispose" }));
            Assert.That(s.Clock.Created, Is.Zero);
            Assert.That(s.Clock.Reads, Is.Zero);
            Assert.That(s.Service.Disposals, Is.EqualTo(1));
        });
    }

    [TestCase("open")]
    [TestCase("query")]
    public async Task CancellationAfterOpeningStoppedServicePreventsStart(string cancelAt)
    {
        using var s = new Scenario(1);
        if (cancelAt == "open") s.OnOpen = s.Cancel.Cancel;
        else s.Service.OnQuery = _ => s.Cancel.Cancel();
        var error = await Cancellation(s.Run());
        Assert.Multiple(() =>
        {
            Assert.That(error.CancellationToken, Is.EqualTo(s.Cancel.Token));
            Assert.That(s.Log, Is.EqualTo(new[] { "open:21", "trust", "query:1", "dispose" }));
            Assert.That(s.Service.Disposals, Is.EqualTo(1));
        });
    }

    [TestCase(0)]
    [TestCase(5)]
    [TestCase(1053)]
    [TestCase(1060)]
    public async Task FailedStartPreservesNativeErrorIncludingZero(int code)
    {
        using var s = new Scenario(1);
        s.Service.StartSucceeds = false;
        s.Service.StartError = code;
        var error = await Failure(s.Run());
        Assert.Multiple(() =>
        {
            Assert.That(error, Is.TypeOf<Win32Exception>());
            Assert.That(((Win32Exception)error).NativeErrorCode, Is.EqualTo(code));
            Assert.That(error.Message, Is.EqualTo("Cannot start network monitoring service."));
            Assert.That(s.Log, Is.EqualTo(new[] { "open:21", "trust", "query:1", "start", "dispose" }));
            Assert.That(s.Clock.Reads, Is.Zero);
            Assert.That(s.Service.Disposals, Is.EqualTo(1));
        });
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task SuccessfulOrAlreadyRunningStartPollsImmediatelyWithoutDelay(bool succeeds)
    {
        using var s = new Scenario(1, 4);
        s.Service.StartSucceeds = succeeds;
        s.Service.StartError = 1056;
        await s.Run();
        Assert.Multiple(() =>
        {
            Assert.That(s.Log, Is.EqualTo(new[] { "open:21", "trust", "query:1", "start", "query:2", "dispose" }));
            Assert.That(s.Clock.Reads, Is.EqualTo(1));
            Assert.That(s.Clock.Created, Is.Zero);
            Assert.That(s.Service.Disposals, Is.EqualTo(1));
        });
    }

    [TestCase(0u)]
    [TestCase(1u)]
    [TestCase(2u)]
    [TestCase(3u)]
    [TestCase(5u)]
    [TestCase(6u)]
    [TestCase(7u)]
    public async Task NonRunningStatesUseFiftyMillisecondPollingUntilRunning(uint state)
    {
        using var s = new Scenario(state, state, 4);
        var operation = s.Run();
        await s.Clock.NextTimer();
        Assert.Multiple(() =>
        {
            Assert.That(operation.IsCompleted, Is.False);
            Assert.That(s.Service.Disposals, Is.Zero);
            Assert.That(s.Clock.Delays, Is.EqualTo(new[] { TimeSpan.FromMilliseconds(50) }));
        });
        s.Clock.Advance(TimeSpan.FromMilliseconds(49));
        Assert.That(operation.IsCompleted, Is.False);
        s.Clock.Advance(TimeSpan.FromMilliseconds(1));
        await operation.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Multiple(() =>
        {
            Assert.That(s.Service.Queries, Is.EqualTo(3));
            Assert.That(s.Service.Starts, Is.EqualTo(1));
            Assert.That(s.Service.Disposals, Is.EqualTo(1));
            Assert.That(s.Clock.Pending, Is.Zero);
        });
    }

    [TestCase(10000, 1)]
    [TestCase(10001, 1)]
    [TestCase(5000, 2)]
    public async Task ExactTenSecondDeadlineStopsAfterDelayWithoutAnExtraPoll(int advanceMs, int delays)
    {
        using var s = new Scenario(1);
        var operation = s.Run();
        for (var i = 0; i < delays; i++)
        {
            await s.Clock.NextTimer();
            s.Clock.Advance(TimeSpan.FromMilliseconds(advanceMs));
        }
        var error = await Failure(operation.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Multiple(() =>
        {
            Assert.That(error, Is.TypeOf<TimeoutException>());
            Assert.That(error.Message, Is.EqualTo("Network monitoring service did not start in time."));
            Assert.That(s.Service.Queries, Is.EqualTo(1 + delays));
            Assert.That(s.Clock.Created, Is.EqualTo(delays));
            Assert.That(s.Service.Disposals, Is.EqualTo(1));
            Assert.That(s.Clock.Pending, Is.Zero);
        });
    }

    [Test]
    public async Task RunningReplyAtDeadlineStillWinsBeforeDelayOrDeadlineCheck()
    {
        using var s = new Scenario(1, 4);
        s.Service.OnQuery = index => { if (index == 2) s.Clock.Advance(TimeSpan.FromSeconds(10)); };
        await s.Run();
        Assert.Multiple(() =>
        {
            Assert.That(s.Clock.Reads, Is.EqualTo(1));
            Assert.That(s.Clock.Created, Is.Zero);
            Assert.That(s.Service.Queries, Is.EqualTo(2));
            Assert.That(s.Service.Disposals, Is.EqualTo(1));
        });
    }

    [TestCase("trust")]
    [TestCase("initial-query")]
    [TestCase("poll-query")]
    public async Task QueryFailuresRemainFaultedAndCloseTheOwnedHandle(string point)
    {
        using var s = new Scenario(1);
        var failure = new UnauthorizedAccessException("fixture query");
        if (point == "trust") s.Service.TrustFailure = failure;
        else s.Service.OnQuery = index => { if (index == (point == "initial-query" ? 1 : 2)) throw failure; };
        var error = await Failure(s.Run());
        Assert.Multiple(() =>
        {
            Assert.That(error, Is.SameAs(failure));
            Assert.That(s.Service.Starts, Is.EqualTo(point == "poll-query" ? 1 : 0));
            Assert.That(s.Service.Disposals, Is.EqualTo(1));
            Assert.That(s.Clock.Created, Is.Zero);
        });
    }

    [TestCase(true, 0)]
    [TestCase(false, 1056)]
    [TestCase(false, 5)]
    public async Task CancellationDuringStartPreservesNativeFailureAndPollingPrecedence(bool succeeds, int code)
    {
        using var s = new Scenario(1);
        s.Service.StartSucceeds = succeeds;
        s.Service.StartError = code;
        s.Service.OnStart = s.Cancel.Cancel;
        var operation = s.Run();
        var error = await Failure(operation);
        var nativeFailure = !succeeds && code != 1056;
        Assert.Multiple(() =>
        {
            Assert.That(s.Cancel.IsCancellationRequested, Is.True);
            Assert.That(operation.IsCanceled, Is.EqualTo(!nativeFailure));
            Assert.That(error, nativeFailure ? Is.TypeOf<Win32Exception>() : Is.InstanceOf<OperationCanceledException>());
            Assert.That(s.Clock.Reads, Is.EqualTo(nativeFailure ? 0 : 1));
            Assert.That(s.Service.Queries, Is.EqualTo(1));
            Assert.That(s.Clock.Created, Is.Zero);
            Assert.That(s.Service.Disposals, Is.EqualTo(1));
        });
        if (nativeFailure)
        {
            Assert.That(((Win32Exception)error).NativeErrorCode, Is.EqualTo(code));
            Assert.That(error.Message, Is.EqualTo("Cannot start network monitoring service."));
        }
        else Assert.That(((OperationCanceledException)error).CancellationToken, Is.EqualTo(s.Cancel.Token));
    }

    [Test]
    public async Task CancellationDuringDelayClosesTheHandleAndRemovesTheTimer()
    {
        using var s = new Scenario(1);
        var operation = s.Run();
        await s.Clock.NextTimer();
        s.Cancel.Cancel();
        var error = await Cancellation(operation.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Multiple(() =>
        {
            Assert.That(error.CancellationToken, Is.EqualTo(s.Cancel.Token));
            Assert.That(s.Service.Queries, Is.EqualTo(2));
            Assert.That(s.Service.Disposals, Is.EqualTo(1));
            Assert.That(s.Clock.Pending, Is.Zero);
        });
    }

    [TestCase(50, false)]
    [TestCase(10000, true)]
    public async Task CancellationAfterDelayPreservesDeadlinePrecedence(int advanceMs, bool timesOut)
    {
        using var s = new Scenario(1);
        s.Clock.OnRead = index => { if (index == 2) s.Cancel.Cancel(); };
        var operation = s.Run();
        await s.Clock.NextTimer();
        s.Clock.Advance(TimeSpan.FromMilliseconds(advanceMs));
        var error = await Failure(operation.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Multiple(() =>
        {
            Assert.That(s.Cancel.IsCancellationRequested, Is.True);
            Assert.That(error, timesOut ? Is.TypeOf<TimeoutException>() : Is.InstanceOf<OperationCanceledException>());
            Assert.That(operation.IsCanceled, Is.EqualTo(!timesOut));
            Assert.That(s.Service.Queries, Is.EqualTo(2));
            Assert.That(s.Service.Disposals, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task IncompletePollingDoesNotCaptureCallerSynchronizationContext()
    {
        using var s = new Scenario(1, 1, 4);
        var context = new CountingContext();
        var previous = SynchronizationContext.Current;
        Task operation;
        try
        {
            SynchronizationContext.SetSynchronizationContext(context);
            operation = s.Run();
        }
        finally { SynchronizationContext.SetSynchronizationContext(previous); }
        await s.Clock.NextTimer();
        Assert.That(s.Service.Disposals, Is.Zero);
        await Task.Run(() => s.Clock.Advance(TimeSpan.FromMilliseconds(50)));
        await operation.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Multiple(() =>
        {
            Assert.That(context.Posts, Is.Zero);
            Assert.That(s.Service.LastQueryContext, Is.Not.SameAs(context));
            Assert.That(s.Service.Disposals, Is.EqualTo(1));
        });
    }

    private static async Task<Exception> Failure(Task task)
    {
        try { await task; }
        catch (Exception error) { return error; }
        Assert.Fail("The operation must fail.");
        throw new InvalidOperationException();
    }

    private static async Task<OperationCanceledException> Cancellation(Task task)
    {
        var error = await Failure(task);
        Assert.That(task.IsCanceled, Is.True);
        Assert.That(error, Is.InstanceOf<OperationCanceledException>());
        return (OperationCanceledException)error;
    }

    private sealed class Scenario : IDisposable
    {
        private readonly ConcurrentQueue<string> _log = new();
        public Scenario(params uint[] states) => Service = new Session(this, states);
        public ManualClock Clock { get; } = new();
        public CancellationTokenSource Cancel { get; } = new();
        public Session Service { get; }
        public Exception? OpenFailure { get; set; }
        public Action? OnOpen { get; set; }
        public string[] Log => _log.ToArray();
        internal void Record(string entry) => _log.Enqueue(entry);
        public Task Run() => NetworkServiceIdentity.EnsureRunningAsync(Cancel.Token, Open);
        private StartupSession Open(uint access)
        {
            Record("open:" + access);
            if (OpenFailure is { } failure) throw failure;
            OnOpen?.Invoke();
            return Service;
        }
        public void Dispose() { Cancel.Cancel(); Clock.Dispose(); Cancel.Dispose(); }
    }

    private sealed class Session(Scenario scenario, uint[] states) : StartupSession
    {
        public bool Trusted { get; set; } = true;
        public bool StartSucceeds { get; set; } = true;
        public int StartError { get; set; }
        public int Queries { get; private set; }
        public int Starts { get; private set; }
        public int Disposals { get; private set; }
        public Exception? TrustFailure { get; set; }
        public Action<int>? OnQuery { get; set; }
        public Action? OnStart { get; set; }
        public SynchronizationContext? LastQueryContext { get; private set; }
        public TimeProvider Clock => scenario.Clock;
        public bool IsTrusted
        {
            get { scenario.Record("trust"); if (TrustFailure is { } failure) throw failure; return Trusted; }
        }
        public uint State
        {
            get
            {
                var index = ++Queries;
                scenario.Record("query:" + index);
                LastQueryContext = SynchronizationContext.Current;
                OnQuery?.Invoke(index);
                return index <= states.Length ? states[index - 1] : 1;
            }
        }
        public bool TryStart(out int error)
        {
            Starts++;
            scenario.Record("start");
            OnStart?.Invoke();
            error = StartError;
            return StartSucceeds;
        }

        public void Dispose() { Disposals++; scenario.Record("dispose"); }
    }

    private sealed class CountingContext : SynchronizationContext
    {
        public int Posts;
        public override void Post(SendOrPostCallback callback, object? state)
        {
            Interlocked.Increment(ref Posts);
            ThreadPool.QueueUserWorkItem(_ => callback(state));
        }
    }

    private sealed class ManualClock : TimeProvider, IDisposable
    {
        private readonly List<ManualTimer> _timers = [];
        private readonly SemaphoreSlim _created = new(0);
        private long _ticks;
        public int Created { get; private set; }
        public int Reads { get; private set; }
        public List<TimeSpan> Delays { get; } = [];
        public Action<int>? OnRead { get; set; }
        public int Pending { get { lock (_timers) return _timers.Count; } }
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() { var reads = ++Reads; OnRead?.Invoke(reads); return Interlocked.Read(ref _ticks); }
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            Assert.That(period, Is.EqualTo(Timeout.InfiniteTimeSpan));
            var timer = new ManualTimer(this, callback, state);
            timer.Change(dueTime, period);
            lock (_timers) { _timers.Add(timer); Delays.Add(dueTime); Created++; }
            _created.Release();
            return timer;
        }
        public async Task NextTimer() => Assert.That(await _created.WaitAsync(TimeSpan.FromSeconds(5)), Is.True,
            "The operation must register its controlled polling delay.");
        public void Advance(TimeSpan elapsed)
        {
            ManualTimer[] due;
            lock (_timers)
            {
                Interlocked.Add(ref _ticks, elapsed.Ticks);
                due = _timers.Where(timer => timer.Due <= _ticks).ToArray();
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
