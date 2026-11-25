using System.Collections.Concurrent;
using System.Text.Json;
using MonCore.Monitoring;
using NUnit.Framework;

namespace WinMon.Mcp.Tests;

[TestFixture]
public sealed class ServiceQueryCharacterizationTests
{
    private static readonly DateTimeOffset Origin = new(2026, 11, 22, 0, 0, 0, TimeSpan.Zero);

    [TestCase("sort-null")]
    [TestCase("sort-case")]
    [TestCase("sort-unknown")]
    [TestCase("filter-null")]
    [TestCase("filter-long")]
    [TestCase("limit-zero")]
    [TestCase("limit-negative")]
    [TestCase("limit-high")]
    public async Task InvalidServiceRequestsPrecedeCancellationAndNeverCollect(string kind)
    {
        var sort = kind == "sort-null" ? null! : kind == "sort-case" ? "CPU" : kind == "sort-unknown" ? "unknown" : "cpu";
        var filter = kind == "filter-null" ? null! : kind == "filter-long" ? new string('x', 257) : "";
        var limit = kind == "limit-zero" ? 0 : kind == "limit-negative" ? -1 : kind == "limit-high" ? 201 : 20;
        await using var owner = GuardedOwner();
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        var result = await new DiagnosticTools(owner).ListServices(sort, filter, limit, cancellationToken: cancellation.Token);
        Assert.That(result.IsError, Is.True);
        Assert.That(result.StructuredContent!.Value.GetProperty("status").GetString(), Is.EqualTo("invalidInput"));
        Assert.That(owner.ReadAfter(0), Is.Empty);
        Assert.That(owner.TrafficState, Is.EqualTo("NotStarted"));
    }

    [TestCase("cpu")]
    [TestCase("memory")]
    [TestCase("io")]
    [TestCase("name")]
    public async Task ValidServiceCancellationDoesNotCollect(string sort)
    {
        await using var owner = GuardedOwner();
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        var exception = Assert.ThrowsAsync<TaskCanceledException>(async () =>
            await new DiagnosticTools(owner).ListServices(sort, new string('x', 256), 200, cancellationToken: cancellation.Token));
        Assert.That(exception!.CancellationToken.IsCancellationRequested, Is.True);
        Assert.That(owner.ReadAfter(0), Is.Empty);
    }

    [TestCase("cpu", true, "svc-a,svc-c,svc-b")]
    [TestCase("cpu", false, "svc-a,svc-c,svc-b")]
    [TestCase("memory", true, "svc-a,svc-b,svc-c")]
    [TestCase("memory", false, "svc-b,svc-c,svc-a")]
    [TestCase("io", true, "svc-a,svc-b,svc-c")]
    [TestCase("io", false, "svc-b,svc-a,svc-c")]
    [TestCase("name", true, "svc-b,svc-c,svc-a")]
    [TestCase("name", false, "svc-b,svc-c,svc-a")]
    public async Task ServiceSortsRespectWorkerMetricsAndOrdinalNames(string sort, bool workers, string expected)
    {
        await using var harness = await Harness.Create();
        await harness.Publish(Groups());
        var json = (await harness.Tools.ListServices(sort, includeWorkers: workers)).StructuredContent!.Value;
        var entries = json.GetProperty("data").EnumerateArray().ToArray();
        Assert.That(entries.Select(entry => entry.GetProperty("services")[0].GetProperty("name").GetString()), Is.EqualTo(expected.Split(',')));
        Assert.That(entries.Select(entry => entry.GetProperty("includesWorkers").GetBoolean()), Is.All.EqualTo(workers));
        Assert.That(json.GetProperty("returnedCount").GetInt32(), Is.EqualTo(3));
        Assert.That(json.GetProperty("truncated").GetBoolean(), Is.False);
        var shared = entries.Single(entry => entry.GetProperty("services")[0].GetProperty("name").GetString() == "svc-a");
        Assert.That(shared.GetProperty("sharedHost").GetBoolean(), Is.True);
        Assert.That(shared.GetProperty("processes").GetArrayLength(), Is.EqualTo(2));
        Assert.That(shared.GetProperty("host").GetProperty("startToken").GetString(), Is.EqualTo("10"));
        Assert.That(shared.GetProperty("metrics").GetProperty("cpu").GetDouble(), Is.EqualTo(workers ? 70 : 30));
        Assert.That(json.GetRawText(), Does.Not.Contain("private-key").And.Not.Contain("private-path").And.Not.Contain("private-process"));
    }

    [TestCase("name", 1)]
    [TestCase("display", 1)]
    [TestCase("secondary", 1)]
    [TestCase("path", 0)]
    [TestCase("key", 0)]
    [TestCase("process", 0)]
    [TestCase("whitespace", 0)]
    public async Task PublicServiceFiltersNeverSearchPathsKeysOrProcessNames(string kind, int expected)
    {
        await using var harness = await Harness.Create(); await harness.Publish(Groups());
        var filter = kind switch { "name" => "SVC-A", "display" => "zUlU", "secondary" => "SECOND TITLE", "path" => "private-path", "key" => "private-key", "process" => "private-process", _ => " Alpha " };
        var json = (await harness.Tools.ListServices(filter: filter)).StructuredContent!.Value;
        Assert.That(json.GetProperty("data").GetArrayLength(), Is.EqualTo(expected));
        Assert.That(json.GetProperty("returnedCount").GetInt32(), Is.EqualTo(expected));
        Assert.That(json.GetProperty("status").GetString(), Is.EqualTo("ok"));
        if (expected == 1) Assert.That(json.GetProperty("data")[0].GetProperty("services")[0].GetProperty("name").GetString(), Is.EqualTo("svc-a"));
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task WorkerSwitchChangesAveragesButKeepsMembership(bool workers)
    {
        await using var harness = await Harness.Create();
        ServiceGroup Group(double hostCpu, double workerCpu, ulong hostMemory, ulong workerMemory, double hostIo, double workerIo)
        {
            var host = Process(1, hostCpu, hostMemory, hostIo);
            return new ServiceGroup("private-key", [new("svc", "service", 4, 1, 0)], host, [host, Process(2, workerCpu, workerMemory, workerIo)]);
        }
        await harness.Publish([Group(10, 20, 11, 22, 100, 200)]);
        await harness.Publish([Group(30, 40, 99, 88, 300, 400)]);
        var entry = (await harness.Tools.ListServices(includeWorkers: workers)).StructuredContent!.Value.GetProperty("data")[0];
        Assert.That(entry.GetProperty("metrics").GetProperty("cpu").GetDouble(), Is.EqualTo(workers ? 50 : 20));
        Assert.That(entry.GetProperty("metrics").GetProperty("io").GetDouble(), Is.EqualTo(workers ? 500 : 200));
        Assert.That(entry.GetProperty("metrics").GetProperty("privateBytes").GetUInt64(), Is.EqualTo(workers ? 187ul : 99ul));
        Assert.That(entry.GetProperty("processes").GetArrayLength(), Is.EqualTo(2));
        Assert.That(entry.GetProperty("includesWorkers").GetBoolean(), Is.EqualTo(workers));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task CurrentServiceAvailabilityWinsOverPriorHistory(bool missing)
    {
        await using var harness = await Harness.Create(); await harness.Publish(Groups());
        await harness.Publish(missing ? null : Groups(), available: false);
        var json = (await harness.Tools.ListServices()).StructuredContent!.Value;
        Assert.That(json.GetProperty("status").GetString(), Is.EqualTo("unavailable"));
        Assert.That(json.GetProperty("data").ValueKind, Is.EqualTo(JsonValueKind.Null));
        Assert.That(json.GetProperty("returnedCount").GetInt32(), Is.Zero);
    }

    [Test]
    public async Task MissingHistoricalServicesBreakTheMetricWindow()
    {
        await using var harness = await Harness.Create();
        await harness.Publish(Groups()); await harness.Publish(null); await harness.Publish(Groups());
        var json = (await harness.Tools.ListServices()).StructuredContent!.Value;
        var entry = json.GetProperty("data").EnumerateArray().Single(item => item.GetProperty("services")[0].GetProperty("name").GetString() == "svc-a");
        Assert.That(entry.GetProperty("metrics").GetProperty("cpu").GetDouble(), Is.EqualTo(70));
        Assert.That(entry.GetProperty("metrics").GetProperty("io").GetDouble(), Is.EqualTo(50));
    }

    [TestCase(200, 200, false)]
    [TestCase(201, 200, true)]
    [TestCase(200, 201, true)]
    [TestCase(201, 201, true)]
    public async Task MemberCapsPreserveOriginalCountsAndMetadata(int services, int processes, bool truncated)
    {
        await using var harness = await Harness.Create();
        var members = Enumerable.Range(1, processes).Select(i => Process(i, 0, 1, 0)).ToArray();
        var names = Enumerable.Range(1, services).Select(i => new ServiceObservation("service-" + i, "display-" + i, 7, i, 0)).ToArray();
        await harness.Publish([new ServiceGroup("private-key", names, members[0], members)]);
        var entry = (await harness.Tools.ListServices()).StructuredContent!.Value.GetProperty("data")[0];
        Assert.That(entry.GetProperty("services").GetArrayLength(), Is.EqualTo(200));
        Assert.That(entry.GetProperty("processes").GetArrayLength(), Is.EqualTo(200));
        Assert.That(entry.GetProperty("services")[199].GetProperty("name").GetString(), Is.EqualTo("service-200"));
        Assert.That(entry.GetProperty("services")[0].GetProperty("state").GetUInt32(), Is.EqualTo(7));
        Assert.That(entry.GetProperty("processes")[199].GetProperty("pid").GetInt32(), Is.EqualTo(200));
        Assert.That(entry.GetProperty("metadataTruncated").GetBoolean(), Is.EqualTo(truncated));
        Assert.That(entry.GetProperty("sharedHost").GetBoolean(), Is.True);
    }

    [TestCase("name", 512, false)]
    [TestCase("name", 513, true)]
    [TestCase("display", 512, false)]
    [TestCase("display", 513, true)]
    [TestCase("both", 513, true)]
    [TestCase("surrogate", 513, true)]
    [TestCase("control", 512, false)]
    public async Task ServiceTextBoundsPreserveIndependentClippingFlags(string field, int length, bool truncated)
    {
        var name = field is "name" or "both" ? new string('n', length) : field == "surrogate" ? new string('n', 511) + "😀" : field == "control" ? new string('n', 511) + "\n" : "name";
        var display = field is "display" or "both" ? new string('d', length) : "display";
        await using var harness = await Harness.Create(); var process = Process(1, 1, 1, 1);
        await harness.Publish([new ServiceGroup("private-key", [new(name, display, 4, 1, 0)], process, [process])]);
        var entry = (await harness.Tools.ListServices()).StructuredContent!.Value.GetProperty("data")[0];
        Assert.That(entry.GetProperty("services")[0].GetProperty("name").GetString(), Is.EqualTo(field is "surrogate" or "control" ? new string('n', 511) : name[..Math.Min(name.Length, 512)]));
        Assert.That(entry.GetProperty("services")[0].GetProperty("displayName").GetString(), Is.EqualTo(display[..Math.Min(display.Length, 512)]));
        Assert.That(entry.GetProperty("metadataTruncated").GetBoolean(), Is.EqualTo(truncated));
    }

    [Test]
    public async Task EmptyGroupsAndNullHostsPreserveNullMetricsAndStableTies()
    {
        await using var harness = await Harness.Create();
        await harness.Publish([new ServiceGroup("private-key-empty", [], null, []), new ServiceGroup("private-key-a", [new("a", "same", 1, 0, 0)], null, []), new ServiceGroup("private-key-b", [new("b", "SAME", 1, 0, 0)], null, [])]);
        var json = (await harness.Tools.ListServices("name", includeWorkers: false)).StructuredContent!.Value;
        var entries = json.GetProperty("data").EnumerateArray().ToArray();
        Assert.That(entries[0].GetProperty("services").GetArrayLength(), Is.Zero);
        Assert.That(entries.Skip(1).Select(entry => entry.GetProperty("services")[0].GetProperty("name").GetString()), Is.EqualTo(new[] { "a", "b" }));
        Assert.That(entries.Select(entry => entry.GetProperty("host").ValueKind), Is.All.EqualTo(JsonValueKind.Null));
        foreach (var entry in entries) foreach (var metric in new[] { "cpu", "privateBytes", "io" }) Assert.That(entry.GetProperty("metrics").GetProperty(metric).ValueKind, Is.EqualTo(JsonValueKind.Null));
    }

    [Test]
    public async Task ServiceLimitsKeepTheFirst200Of201Groups()
    {
        await using var harness = await Harness.Create(); await harness.Publish(ManyGroups());
        var json = (await harness.Tools.ListServices("name", limit: 200)).StructuredContent!.Value;
        Assert.That(json.GetProperty("data").GetArrayLength(), Is.EqualTo(200));
        Assert.That(json.GetProperty("returnedCount").GetInt32(), Is.EqualTo(200));
        Assert.That(json.GetProperty("truncated").GetBoolean(), Is.True);
        var first = (await harness.Tools.ListServices("name", limit: 1)).StructuredContent!.Value;
        Assert.That(first.GetProperty("data")[0].GetProperty("services")[0].GetProperty("displayName").GetString(), Is.EqualTo("service-000"));
        Assert.That(first.GetProperty("returnedCount").GetInt32(), Is.EqualTo(1));
        Assert.That(first.GetProperty("truncated").GetBoolean(), Is.True);
    }

    [Test]
    public async Task StaleServiceDataIsWithheld()
    {
        await using var harness = await Harness.Create(); await harness.Publish(Groups()); harness.Clock.Advance(TimeSpan.FromSeconds(3));
        var json = (await harness.Tools.ListServices()).StructuredContent!.Value;
        Assert.That(json.GetProperty("status").GetString(), Is.EqualTo("unavailable"));
        Assert.That(json.GetProperty("data").ValueKind, Is.EqualTo(JsonValueKind.Null));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task MissingOrCancelledServiceWarmupReleasesRequests(bool cancel)
    {
        await using var harness = await Harness.Create(false); using var token = new CancellationTokenSource();
        var request = harness.Tools.ListServices(cancellationToken: token.Token);
        await harness.Clock.CurrentCreated.Task.WaitAsync(TimeSpan.FromSeconds(5));
        if (cancel) { token.Cancel(); Assert.That(async () => await request, Throws.InstanceOf<OperationCanceledException>()); }
        else { harness.Clock.Advance(TimeSpan.FromSeconds(5)); var json = (await request.WaitAsync(TimeSpan.FromSeconds(5))).StructuredContent!.Value; Assert.That(json.GetProperty("status").GetString(), Is.EqualTo("unavailable")); Assert.That(json.GetProperty("data").ValueKind, Is.EqualTo(JsonValueKind.Null)); }
        await harness.Publish(Groups()); await harness.Publish(Groups());
        Assert.That((await harness.Tools.ListServices()).StructuredContent!.Value.GetProperty("status").GetString(), Is.EqualTo("ok"));
        await harness.ReleasePin(); await harness.Clock.IdleCreated.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    internal static async Task<(Func<object> Invoke, IAsyncDisposable Owner)> CreateBenchmarkRequest(string scenario)
    {
        if (scenario == "invalid")
        {
            var owner = GuardedOwner();
            return (() => new DiagnosticTools(owner).ListServices(limit: 0).GetAwaiter().GetResult(), owner);
        }
        var harness = await Harness.Create();
        try
        {
            if (scenario != "unavailable")
            {
                if (scenario == "limited") await harness.Publish(ManyGroups());
                else if (scenario == "caps")
                {
                    var members = Enumerable.Range(1, 201).Select(i => Process(i, 1, 1, 1)).ToArray();
                    await harness.Publish([new ServiceGroup("key", Enumerable.Range(1, 201).Select(i => new ServiceObservation("service-" + i, "display-" + i, 4, i, 0)).ToArray(), members[0], members)]);
                }
                else for (var i = 0; i < (scenario == "history" ? 5 : 1); i++) await harness.Publish(Groups());
            }
            harness.Clock.SystemTimers = true;
            return (() => harness.Tools.ListServices("memory", limit: 200, includeWorkers: scenario != "host").GetAwaiter().GetResult(), harness);
        }
        catch { await harness.DisposeAsync(); throw; }
    }

    private static SamplingOwner GuardedOwner() => new(() => throw new AssertionException("Unexpected native collector."), new Clock(), createTraffic: () => throw new AssertionException("Unexpected traffic."));
    private static ProcessUsage Process(int id, double cpu, ulong memory, double? io) => new(id, id * 10, "private-process", cpu) { PrivateBytes = memory, IoBytesPerSecond = io, ImagePath = @"C:\private-path\worker.exe" };
    private static ServiceGroup[] Groups()
    {
        var a = Process(1, 30, 100, 10); var b = Process(2, 10, 300, 30); var c = Process(3, 20, 200, null);
        return [new("private-key-a", [new("svc-a", "Zulu", 4, 1, 0), new("second", "Second title", 4, 1, 0)], a, [a, Process(11, 40, 400, 40)]), new("private-key-b", [new("svc-b", "Alpha", 4, 2, 0)], b, [b]), new("private-key-c", [new("svc-c", "Middle", 4, 3, 0)], c, [c])];
    }
    private static ServiceGroup[] ManyGroups() => Enumerable.Range(0, 201).Select(i => { var p = Process(i + 1, 1, 1, 1); return new ServiceGroup("private-key-" + i, [new("service-" + i.ToString("D3"), "service-" + i.ToString("D3"), 4, i + 1, 0)], p, [p]); }).ToArray();

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
            if (SystemTimers) return System.CreateTimer(callback, state, dueTime, period);
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
            try { result._lease = await result.Owner.AcquireAsync(default); if (warm) { await result.Publish(null); await result.Publish(null); } return result; }
            catch { await result.DisposeAsync(); throw; }
        }
        internal async Task Publish(ServiceGroup[]? groups, bool available = true)
        {
            var sequence = Owner.ReadAfter(0).LastOrDefault()?.Sequence ?? 0;
            Clock.Advance(TimeSpan.FromSeconds(1));
            var sample = new MonitorSample(Clock.GetUtcNow(), 100, groups?.SelectMany(group => group.Processes).ToArray() ?? [], 0) { IntervalSeconds = 1, IntervalCpuTicks = 10_000_000 };
            _source.Frames.Add(new CapturedFrame(sample, groups is null ? null : new ServiceFrame(sample, groups, available)));
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
