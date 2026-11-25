using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using NUnit.Framework;
using WinMon.Mcp;
using WinMon.Monitoring;

namespace WinMon.Tests;

[TestFixture]
public sealed class ProcessQueryCharacterizationTests
{
    private static readonly DateTimeOffset Origin = new(2026, 11, 22, 0, 0, 0, TimeSpan.Zero);

    [TestCase("sort-null")]
    [TestCase("sort-empty")]
    [TestCase("sort-long")]
    [TestCase("sort-numeric")]
    [TestCase("sort-composite")]
    [TestCase("sort-unknown")]
    [TestCase("sort-spaces")]
    [TestCase("filter-null")]
    [TestCase("filter-long")]
    [TestCase("limit-zero")]
    [TestCase("limit-negative")]
    [TestCase("limit-high")]
    public async Task InvalidProcessRequestsPrecedeCancellationAndCollection(string kind)
    {
        var sort = kind switch { "sort-null" => null!, "sort-empty" => "", "sort-long" => "12345678", "sort-numeric" => "0", "sort-composite" => "Cpu,Io", "sort-unknown" => "other", "sort-spaces" => " cpu ", _ => "cpu" };
        var filter = kind == "filter-null" ? null! : kind == "filter-long" ? new string('x', 257) : "";
        var limit = kind == "limit-zero" ? 0 : kind == "limit-negative" ? -1 : kind == "limit-high" ? 201 : 20;
        await using var owner = GuardedOwner(); using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        var result = await new DiagnosticTools(owner).ListProcesses(sort, filter, limit, cancellation.Token);
        Assert.That(result.IsError, Is.True);
        Assert.That(result.StructuredContent!.Value.GetProperty("status").GetString(), Is.EqualTo("invalidInput"));
        Assert.That(owner.ReadAfter(0), Is.Empty);
        Assert.That(owner.TrafficState, Is.EqualTo("NotStarted"));
    }

    [TestCase("cpu")]
    [TestCase("memory")]
    [TestCase("gpu")]
    [TestCase("io")]
    [TestCase("threads")]
    [TestCase("handles")]
    public async Task ValidCancelledProcessRequestsNeverCreateCollectors(string sort)
    {
        await using var owner = GuardedOwner(); using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        var exception = Assert.ThrowsAsync<TaskCanceledException>(async () => await new DiagnosticTools(owner).ListProcesses(sort, new string('x', 256), 200, cancellation.Token));
        Assert.That(exception!.CancellationToken.IsCancellationRequested, Is.True);
        Assert.That(owner.ReadAfter(0), Is.Empty);
    }

    [TestCase("cpu", "30,20,10,5,40")]
    [TestCase("memory", "10,20,30,5,40")]
    [TestCase("gpu", "20,30,10,5,40")]
    [TestCase("io", "20,10,30,5,40")]
    [TestCase("threads", "10,30,20,5,40")]
    [TestCase("handles", "30,10,20,5,40")]
    [TestCase("CPU", "30,20,10,5,40")]
    [TestCase("MEMORY", "10,20,30,5,40")]
    [TestCase("GPU", "20,30,10,5,40")]
    [TestCase("IO", "20,10,30,5,40")]
    [TestCase("THREADS", "10,30,20,5,40")]
    [TestCase("HANDLES", "30,10,20,5,40")]
    [TestCase("hAnDlEs", "30,10,20,5,40")]
    public async Task ProcessSortsKeepIndividualRowsInactiveValuesAndNullMetrics(string sort, string expected)
    {
        await using var harness = await Harness.Create(); await harness.Publish(Rows());
        var json = (await harness.Tools.ListProcesses(sort)).StructuredContent!.Value; var entries = json.GetProperty("data").EnumerateArray().ToArray();
        Assert.That(entries.Select(e => e.GetProperty("identity").GetProperty("pid").GetInt32().ToString(CultureInfo.InvariantCulture)), Is.EqualTo(expected.Split(',')));
        Assert.That(json.GetProperty("returnedCount").GetInt32(), Is.EqualTo(5));
        Assert.That(json.GetProperty("truncated").GetBoolean(), Is.False);
        var missing = entries.Single(e => e.GetProperty("identity").GetProperty("pid").GetInt32() == 40);
        foreach (var field in new[] { "cpuPercent", "workingSetBytes", "privateBytes", "ioBytesPerSecond", "gpuPercent", "threads", "handles" })
        {
            Assert.That(missing.GetProperty(field).ValueKind, Is.EqualTo(JsonValueKind.Null));
            Assert.That(missing.GetProperty("metricAvailability").GetProperty(field).GetBoolean(), Is.False);
        }
        var idle = entries.Single(e => e.GetProperty("identity").GetProperty("pid").GetInt32() == 5);
        Assert.That(idle.GetProperty("cpuPercent").GetDouble(), Is.Zero);
        Assert.That(idle.GetProperty("metricAvailability").GetProperty("cpuPercent").GetBoolean(), Is.True);
        Assert.That(entries.Single(e => e.GetProperty("identity").GetProperty("pid").GetInt32() == 20).GetProperty("privateBytes").GetUInt64(), Is.EqualTo(333));
        Assert.That(json.GetRawText(), Does.Not.Contain("private-path").And.Not.Contain("publisher-secret"));
    }

    [TestCase("IO")]
    [TestCase("Io")]
    [NonParallelizable]
    public async Task ProcessSortCasingIsIndependentOfTurkishCulture(string sort)
    {
        var prior = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
            await using var harness = await Harness.Create(); await harness.Publish(Rows());
            var json = (await harness.Tools.ListProcesses(sort)).StructuredContent!.Value;
            Assert.That(json.GetProperty("status").GetString(), Is.EqualTo("ok"));
            Assert.That(json.GetProperty("data")[0].GetProperty("identity").GetProperty("pid").GetInt32(), Is.EqualTo(20));
        }
        finally { CultureInfo.CurrentCulture = prior; }
    }

    [TestCase("name", "30")]
    [TestCase("application", "10")]
    [TestCase("product", "20")]
    [TestCase("trim", "30")]
    [TestCase("numeric", "30,20,10,40")]
    [TestCase("empty", "30,20,10,5,40")]
    [TestCase("whitespace", "30,20,10,5,40")]
    [TestCase("blank-boundary", "30,20,10,5,40")]
    [TestCase("text-boundary", "")]
    [TestCase("path", "")]
    [TestCase("publisher", "")]
    [TestCase("mixed-numeric", "")]
    public async Task ProcessSearchTrimsPublicTextAndPreservesRanking(string kind, string expected)
    {
        var filter = kind switch { "name" => "ALPHA", "application" => "FRIENDLY APP", "product" => "SUITE X", "trim" => " alpha ", "numeric" => "0", "whitespace" => " \t ", "blank-boundary" => new string(' ', 256), "text-boundary" => new string('x', 256), "path" => "private-path", "publisher" => "publisher-secret", "mixed-numeric" => "x10", _ => "" };
        await using var harness = await Harness.Create(); await harness.Publish(Rows());
        var json = (await harness.Tools.ListProcesses(filter: filter)).StructuredContent!.Value;
        var entries = json.GetProperty("data").EnumerateArray().ToArray();
        Assert.That(json.GetProperty("status").GetString(), Is.EqualTo("ok"));
        Assert.That(entries.Select(e => e.GetProperty("identity").GetProperty("pid").GetInt32().ToString(CultureInfo.InvariantCulture)), Is.EqualTo(expected.Length == 0 ? [] : expected.Split(',')));
        Assert.That(json.GetProperty("returnedCount").GetInt32(), Is.EqualTo(entries.Length));
        Assert.That(json.GetProperty("truncated").GetBoolean(), Is.False);
    }

    [TestCase(1, 0, false)]
    [TestCase(1, 1, false)]
    [TestCase(1, 2, true)]
    [TestCase(200, 199, false)]
    [TestCase(200, 200, false)]
    [TestCase(200, 201, true)]
    public async Task ProcessLimitsKeepPidTieOrderAndExactTruncation(int limit, int count, bool truncated)
    {
        await using var harness = await Harness.Create(); await harness.Publish(ManyRows(count));
        var json = (await harness.Tools.ListProcesses(limit: limit)).StructuredContent!.Value;
        Assert.That(json.GetProperty("data").EnumerateArray().Select(e => e.GetProperty("identity").GetProperty("pid").GetInt32()), Is.EqualTo(Enumerable.Range(1, Math.Min(limit, count))));
        Assert.That(json.GetProperty("returnedCount").GetInt32(), Is.EqualTo(Math.Min(limit, count)));
        Assert.That(json.GetProperty("truncated").GetBoolean(), Is.EqualTo(truncated));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task LargeProcessNamesRespectTheCompleteProtocolByteBudget(bool escaped)
    {
        await using var harness = await Harness.Create();
        await harness.Publish(ManyRows(201).Select(p => p with { Name = new string(escaped ? '界' : 'n', 512) }).ToArray());
        var result = await harness.Tools.ListProcesses(limit: 200); var json = result.StructuredContent!.Value;
        var entries = json.GetProperty("data").EnumerateArray().ToArray();
        Assert.That(JsonSerializer.SerializeToUtf8Bytes(result, Responses.Json).Length, Is.LessThanOrEqualTo(256 * 1024));
        Assert.That(entries.Length, Is.GreaterThan(0).And.LessThan(200));
        Assert.That(json.GetProperty("returnedCount").GetInt32(), Is.EqualTo(entries.Length));
        Assert.That(json.GetProperty("truncated").GetBoolean(), Is.True);
        Assert.That(entries.Select(e => e.GetProperty("identity").GetProperty("pid").GetInt32()), Is.EqualTo(Enumerable.Range(1, entries.Length)));
        Assert.That(entries.Select(e => e.GetProperty("name").GetString()!.Length), Is.All.EqualTo(512));
    }

    [TestCase(1L, ProcessLifetimeDomain.WindowsFileTime)]
    [TestCase(9007199254740993L, ProcessLifetimeDomain.WindowsFileTime)]
    [TestCase(long.MaxValue, ProcessLifetimeDomain.WindowsFileTime)]
    [TestCase(1L, ProcessLifetimeDomain.UnixMicroseconds)]
    [TestCase(9007199254740993L, ProcessLifetimeDomain.UnixMicroseconds)]
    [TestCase(long.MaxValue, ProcessLifetimeDomain.UnixMicroseconds)]
    public async Task ProcessListKeepsOpaqueTokenBoundariesAndDomains(long token, ProcessLifetimeDomain domain)
    {
        await using var harness = await Harness.Create(); await harness.Publish([Process(1, token, "owner", 1) with { LifetimeDomain = domain }]);
        var identity = (await harness.Tools.ListProcesses()).StructuredContent!.Value.GetProperty("data")[0].GetProperty("identity");
        Assert.That(identity.GetProperty("pid").GetInt32(), Is.EqualTo(1));
        Assert.That(identity.GetProperty("startToken").ValueKind, Is.EqualTo(JsonValueKind.String));
        Assert.That(identity.GetProperty("startToken").GetString(), Is.EqualTo(token.ToString(CultureInfo.InvariantCulture)));
        Assert.That(identity.GetProperty("domain").GetString(), Is.EqualTo(domain.ToString()));
    }

    [TestCase("empty", false)]
    [TestCase("boundary", false)]
    [TestCase("long", true)]
    [TestCase("surrogate", true)]
    [TestCase("control", false)]
    public async Task ProcessNamesKeepTextAndLengthFlagContracts(string kind, bool truncated)
    {
        var name = kind == "empty" ? "" : new string('n', kind is "control" or "surrogate" ? 511 : kind == "boundary" ? 512 : 513) + (kind == "surrogate" ? "😀" : kind == "control" ? "\n" : "");
        var expected = kind is "control" or "surrogate" ? new string('n', 511) : name[..Math.Min(name.Length, 512)];
        await using var harness = await Harness.Create(); await harness.Publish([Process(1, 1, name, 1)]);
        var entry = (await harness.Tools.ListProcesses()).StructuredContent!.Value.GetProperty("data")[0];
        Assert.That(entry.GetProperty("name").GetString(), Is.EqualTo(expected));
        Assert.That(entry.GetProperty("nameTruncated").GetBoolean(), Is.EqualTo(truncated));
    }

    [TestCase(false, false, false, 5)]
    [TestCase(true, false, false, 0)]
    [TestCase(false, true, false, 0)]
    [TestCase(false, true, true, 5)]
    [TestCase(true, true, true, 0)]
    public async Task ProcessWindowHonorsGapsAndIndependentReadings(bool gap, bool missingCpu, bool independent, int count)
    {
        await using var harness = await Harness.Create(); await harness.Publish(Rows(), gap, missingCpu, independent);
        var json = (await harness.Tools.ListProcesses()).StructuredContent!.Value;
        Assert.That(json.GetProperty("status").GetString(), Is.EqualTo("ok"));
        Assert.That(json.GetProperty("isGap").GetBoolean(), Is.EqualTo(gap));
        Assert.That(json.GetProperty("data").GetArrayLength(), Is.EqualTo(count));
        Assert.That(json.GetProperty("returnedCount").GetInt32(), Is.EqualTo(count));
    }

    [Test]
    public async Task ProcessListUsesLatestFrameRatherThanHistoryAverages()
    {
        await using var harness = await Harness.Create();
        await harness.Publish([Process(90, 1, "exited", 99), Process(30, 300, "old-alpha", 99)]);
        await harness.Publish(Rows());
        var json = (await harness.Tools.ListProcesses()).StructuredContent!.Value;
        Assert.That(json.GetProperty("data").EnumerateArray().Select(e => e.GetProperty("identity").GetProperty("pid").GetInt32()), Is.EqualTo(new[] { 30, 20, 10, 5, 40 }));
        Assert.That(json.GetProperty("data")[0].GetProperty("cpuPercent").GetDouble(), Is.EqualTo(30));
        Assert.That(json.GetRawText(), Does.Not.Contain("exited").And.Not.Contain("old-alpha"));
    }

    [Test]
    public async Task StaleProcessPublicationWithholdsRows()
    {
        await using var harness = await Harness.Create(); await harness.Publish(Rows()); harness.Clock.Advance(TimeSpan.FromSeconds(3));
        var json = (await harness.Tools.ListProcesses()).StructuredContent!.Value;
        Assert.That(json.GetProperty("status").GetString(), Is.EqualTo("unavailable"));
        Assert.That(json.GetProperty("data").ValueKind, Is.EqualTo(JsonValueKind.Null));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task MissingOrCancelledProcessWarmupReleasesRequests(bool cancel)
    {
        await using var harness = await Harness.Create(false); using var token = new CancellationTokenSource();
        var request = harness.Tools.ListProcesses(cancellationToken: token.Token);
        await harness.Clock.CurrentCreated.Task.WaitAsync(TimeSpan.FromSeconds(5));
        if (cancel) { token.Cancel(); Assert.That(async () => await request, Throws.InstanceOf<OperationCanceledException>()); }
        else { harness.Clock.Advance(TimeSpan.FromSeconds(5)); var json = (await request.WaitAsync(TimeSpan.FromSeconds(5))).StructuredContent!.Value; Assert.That(json.GetProperty("status").GetString(), Is.EqualTo("unavailable")); Assert.That(json.GetProperty("data").ValueKind, Is.EqualTo(JsonValueKind.Null)); }
        await harness.Publish(Rows()); await harness.Publish(Rows());
        Assert.That((await harness.Tools.ListProcesses()).StructuredContent!.Value.GetProperty("returnedCount").GetInt32(), Is.EqualTo(5));
        await harness.ReleasePin(); await harness.Clock.IdleCreated.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    internal static async Task<(Func<object> Invoke, IAsyncDisposable Owner)> CreateBenchmarkRequest(string scenario)
    {
        if (scenario == "invalid") { var owner = GuardedOwner(); return (() => new DiagnosticTools(owner).ListProcesses(limit: 0).GetAwaiter().GetResult(), owner); }
        var harness = await Harness.Create();
        try
        {
            if (scenario == "stale") harness.Clock.Advance(TimeSpan.FromSeconds(3));
            else await harness.Publish(scenario == "limited" ? ManyRows(201) : Rows());
            harness.Clock.SystemTimers = true;
            return (() => harness.Tools.ListProcesses(scenario == "handles" ? "handles" : scenario == "uppercase" ? "CPU" : "cpu", scenario == "filtered" ? "friendly" : "", 200).GetAwaiter().GetResult(), harness);
        }
        catch { await harness.DisposeAsync(); throw; }
    }

    private static SamplingOwner GuardedOwner() => new(() => throw new AssertionException("Unexpected native collector."), new Clock(), createTraffic: () => throw new AssertionException("Unexpected traffic."));
    private static ProcessUsage Process(int pid, long token, string name, double cpu) => new(pid, token, name, cpu, 1, 1) { ImagePath = @"C:\private-path\shared.exe", Publisher = "publisher-secret", PrivateBytes = 1, GpuPercent = 1, ThreadCount = 1, HandleCount = 1 };
    private static ProcessUsage[] Rows() => [
        Process(30, 300, "alpha", 30) with { WorkingSetBytes = 100, PrivateBytes = 111, IoBytesPerSecond = 10, GpuPercent = 20, ThreadCount = 7, HandleCount = 9 },
        Process(10, 100, "beta", 10) with { AppName = "Friendly App", WorkingSetBytes = 300, PrivateBytes = 222, IoBytesPerSecond = 20, GpuPercent = 10, ThreadCount = 9, HandleCount = 7 },
        Process(20, 200, "gamma", 20) with { ProductName = "Suite X", WorkingSetBytes = 200, PrivateBytes = 333, IoBytesPerSecond = 30, GpuPercent = 30, ThreadCount = 5, HandleCount = 5 },
        Process(40, 400, "unreadable", 999) with { CpuAvailable = false, WorkingSetBytes = null, PrivateBytes = null, IoBytesPerSecond = null, GpuPercent = null, ThreadCount = null, HandleCount = null },
        Process(5, 50, "idle", 0) with { WorkingSetBytes = 0, PrivateBytes = 0, IoBytesPerSecond = 0, GpuPercent = 0, ThreadCount = 0, HandleCount = 0 }
    ];
    private static ProcessUsage[] ManyRows(int count) => Enumerable.Range(1, count).Reverse().Select(i => Process(i, i, "process-" + i, 0.1) with { GpuPercent = 0.1 }).ToArray();

    private sealed class Clock : TimeProvider
    {
        private readonly SamplingOwnerTests.ManualClock _inner = new();
        internal bool SystemTimers { get; set; }
        internal TaskCompletionSource CurrentCreated { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource IdleCreated { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override long TimestampFrequency => _inner.TimestampFrequency;
        public override long GetTimestamp() => _inner.GetTimestamp();
        public override DateTimeOffset GetUtcNow() => Origin.AddTicks(GetTimestamp());
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            if (SystemTimers) return TimeProvider.System.CreateTimer(callback, state, dueTime, period);
            var timer = _inner.CreateTimer(callback, state, dueTime, period);
            if (dueTime == TimeSpan.FromSeconds(5)) CurrentCreated.TrySetResult();
            if (dueTime == TimeSpan.FromSeconds(30)) IdleCreated.TrySetResult();
            return timer;
        }
        internal void Advance(TimeSpan elapsed) => _inner.Advance(elapsed);
    }
    private sealed class Source : IFrameSource
    {
        internal readonly BlockingCollection<CapturedFrame> Frames = new();
        public CapturedFrame Capture(SamplingOptions options) => Frames.Take();
        public void Reset() { }
        // The harness owns the queue until sampling has joined.
        public void Dispose() { }
    }
    private sealed class Harness : IAsyncDisposable
    {
        private readonly Source _source = new();
        private IAsyncDisposable? _lease;
        internal Clock Clock { get; } = new();
        internal SamplingOwner Owner { get; }
        internal DiagnosticTools Tools { get; }
        private Harness() { Owner = new SamplingOwner(() => _source, Clock, TimeSpan.FromMilliseconds(1)); Tools = new DiagnosticTools(Owner); }
        internal static async Task<Harness> Create(bool warm = true)
        {
            var result = new Harness();
            try { result._lease = await result.Owner.AcquireAsync(default); if (warm) { await result.Publish([]); await result.Publish([]); } return result; }
            catch { await result.DisposeAsync(); throw; }
        }
        internal async Task Publish(ProcessUsage[] processes, bool gap = false, bool missingCpu = false, bool independent = false)
        {
            var sequence = Owner.ReadAfter(0).LastOrDefault()?.Sequence ?? 0;
            Clock.Advance(TimeSpan.FromSeconds(1));
            var sample = new MonitorSample(Clock.GetUtcNow(), missingCpu ? null : 100, processes, 0, gap)
                { IntervalSeconds = 1, IntervalCpuTicks = 10_000_000, IndependentProcessReadings = independent };
            _source.Frames.Add(new CapturedFrame(sample));
            await Owner.WaitForChangeAsync(sequence, default).WaitAsync(TimeSpan.FromSeconds(5));
        }
        internal async Task ReleasePin() { if (_lease is not null) { await _lease.DisposeAsync(); _lease = null; } }
        public async ValueTask DisposeAsync()
        {
            var stop = Owner.StopAsync(default); _source.Frames.CompleteAdding(); await stop.WaitAsync(TimeSpan.FromSeconds(5));
            if (_lease is not null) await _lease.DisposeAsync(); await Owner.DisposeAsync();
            _source.Frames.Dispose();
        }
    }
}
