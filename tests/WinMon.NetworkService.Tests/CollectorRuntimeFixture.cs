using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.Diagnostics.Tracing;
using WinMon.NetworkService;
using ProcessOwner = WinMon.NetworkService.ProcessOwner;

namespace WinMon.Tests;

internal sealed class CollectorTestClock : TimeProvider, IDisposable
{
    private readonly List<ManualTimer> _timers = [];
    private readonly SemaphoreSlim _created = new(0);
    internal static readonly DateTime Epoch = new(2026, 11, 21, 12, 0, 0, DateTimeKind.Utc);
    internal long Now = 10 * Stopwatch.Frequency;
    internal DateTimeOffset Utc = new(Epoch.AddSeconds(10));
    internal readonly List<string> Reads = [];
    internal readonly Queue<long> Timestamps = [];
    internal Action? OnTimestamp, OnUtc;
    internal int Created { get; private set; }
    internal int Pending { get { lock (_timers) return _timers.Count; } }
    public override long TimestampFrequency => Stopwatch.Frequency;
    public override long GetTimestamp()
    {
        Reads.Add("qpc"); OnTimestamp?.Invoke();
        return Timestamps.TryDequeue(out var next) ? next : Now;
    }
    public override DateTimeOffset GetUtcNow() { Reads.Add("utc"); OnUtc?.Invoke(); return Utc; }
    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new ManualTimer(this, callback, state);
        timer.Change(dueTime, period);
        lock (_timers) { _timers.Add(timer); Created++; }
        _created.Release();
        return timer;
    }
    internal void At(int seconds) { Now = seconds * Stopwatch.Frequency; Utc = new(Epoch.AddSeconds(seconds)); }
    internal Task NextTimer() => _created.WaitAsync().WaitAsync(TimeSpan.FromSeconds(5));
    internal void AdvanceQpc(long elapsed)
    {
        List<ManualTimer> due;
        lock (_timers)
        {
            Now += elapsed; Utc = Utc.AddSeconds(elapsed / (double)TimestampFrequency);
            due = _timers.Where(timer => timer.Due <= Now).ToList();
            foreach (var timer in due) timer.Due = long.MaxValue;
        }
        foreach (var timer in due) timer.Fire();
    }
    public void Dispose()
    {
        lock (_timers) foreach (var timer in _timers.ToArray()) timer.Dispose();
        _created.Dispose();
    }
    private sealed class ManualTimer(CollectorTestClock clock, TimerCallback callback, object? state) : ITimer
    {
        internal long Due = long.MaxValue;
        private bool _disposed;
        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            if (period != Timeout.InfiniteTimeSpan) throw new NotSupportedException("Only one-shot timers are used by the collector.");
            lock (clock._timers)
            {
                if (_disposed) return false;
                Due = dueTime == Timeout.InfiniteTimeSpan ? long.MaxValue : clock.Now + (long)Math.Round(dueTime.TotalSeconds * clock.TimestampFrequency);
                return true;
            }
        }
        internal void Fire() { lock (clock._timers) if (!_disposed) callback(state); }
        public void Dispose() { lock (clock._timers) { _disposed = true; clock._timers.Remove(this); } }
        public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
    }
}

internal sealed class CollectorRuntimeFixture : IAsyncDisposable
{
    internal const string Owner = "S-1-5-21-owner";
    internal const string Other = "S-1-5-21-other";
    internal static long Tick(int seconds) => seconds * Stopwatch.Frequency;
    internal static long Birth(int seconds) => CollectorTestClock.Epoch.AddSeconds(seconds).ToFileTimeUtc();
    internal readonly CollectorTestClock Clock = new();
    internal readonly Probe Native = new();
    internal readonly EtwNetworkCollector Collector;
    internal readonly EtwCallbackFixture Queue;
    internal readonly CollectorOwnershipState State;
    internal readonly Action Reconcile;

    internal CollectorRuntimeFixture(bool source = true, bool ledger = true)
    {
        Collector = new([Owner, Other], Native, Clock);
        Queue = new(Collector);
        State = new(Collector, CollectorTestClock.Epoch, offset: 0, attachLedger: ledger);
        Reconcile = typeof(EtwNetworkCollector).GetMethod("Reconcile", BindingFlags.NonPublic | BindingFlags.Instance)!
            .CreateDelegate<Action>(Collector);
        if (source)
        {
            // Snapshot checks source availability only; this marker owns no native session or handle.
            var marker = (ETWTraceEventSource)RuntimeHelpers.GetUninitializedObject(typeof(ETWTraceEventSource));
            GC.SuppressFinalize(marker);
            State.Set("_source", marker);
        }
    }

    internal bool Register(int pid = 42) => Collector.ResolveAndRegister(pid, Tick(2), Tick(3), false);
    public async ValueTask DisposeAsync()
    {
        try { await Queue.DisposeAsync(); }
        finally { Clock.Dispose(); }
    }

    internal sealed class Probe : EtwNetworkCollector.IProcessOwnerProbe
    {
        internal Func<int, ProcessOwner?> OwnerRead = pid => new(pid, Birth(1), Owner);
        internal Func<int, long?> CreationRead = _ => Birth(1);
        internal long FailedCreationValue;
        internal readonly List<string> Reads = [];
        internal Action? OnOwner, OnCreation;
        public bool TryRead(int pid, out ProcessOwner owner)
        {
            Reads.Add("owner:" + pid); OnOwner?.Invoke(); owner = OwnerRead(pid)!; return owner is not null;
        }
        public bool TryReadCreation(int pid, out long creation)
        {
            Reads.Add("creation:" + pid); OnCreation?.Invoke(); var read = CreationRead(pid);
            creation = read ?? FailedCreationValue; return read.HasValue;
        }
    }
}
