using System.Collections.Concurrent;
using System.Text.Json;
using NUnit.Framework;
using WinMon.Mcp;
using WinMon.Monitoring;

namespace WinMon.Tests;

[TestFixture]
public sealed class PerformanceSamplingCharacterizationTests
{
    private static IEnumerable<TestCaseData> Requests()
    {
        (int Duration, int Interval, int? Pid, string? Token, string? Domain, bool Valid)[] cases =
        [
            (0, 1, null, null, null, false), (1, 1, null, null, null, true),
            (60, 1, null, null, null, true), (61, 1, null, null, null, false),
            (10, 0, null, null, null, false), (10, 10, null, null, null, true),
            (60, 10, null, null, null, true), (11, 11, null, null, null, false),
            (3, 2, null, null, null, false), (4, 2, null, null, null, true),
            (int.MinValue, 1, null, null, null, false), (int.MaxValue, 1, null, null, null, false),
            (10, int.MinValue, null, null, null, false), (10, int.MaxValue, null, null, null, false),
            (2, 1, null, "1", null, false), (2, 1, null, null, "WindowsFileTime", false),
            (2, 1, null, "1", "WindowsFileTime", false),
            (2, 1, 0, null, null, false), (2, 1, -1, null, null, false),
            (2, 1, 42, null, null, true), (2, 1, int.MaxValue, null, null, true),
            (2, 1, 42, "1", null, false), (2, 1, 42, null, "WindowsFileTime", false),
            (2, 1, 42, "1", "WindowsFileTime", true), (2, 1, 42, "1", "UnixMicroseconds", true),
            (2, 1, 42, "9223372036854775807", "WindowsFileTime", true),
            (2, 1, 42, "9223372036854775808", "WindowsFileTime", false),
            (2, 1, 42, "0", "WindowsFileTime", false), (2, 1, 42, "-1", "WindowsFileTime", false),
            (2, 1, 42, "01", "WindowsFileTime", false), (2, 1, 42, "+1", "WindowsFileTime", false),
            (2, 1, 42, "1\n", "WindowsFileTime", false), (2, 1, 42, "", "WindowsFileTime", false),
            (2, 1, 42, "1", "windowsfiletime", false), (2, 1, 42, "1", "unknown", false)
        ];
        for (var i = 0; i < cases.Length; i++)
        {
            var c = cases[i];
            yield return new TestCaseData(c.Duration, c.Interval, c.Pid, c.Token, c.Domain, c.Valid).SetName("SamplingRequest_" + i);
        }
    }

    [TestCaseSource(nameof(Requests))]
    public async Task RequestValidationPrecedesCancellationAndNeverCreatesASource(int duration, int interval, int? pid, string? token, string? domain, bool valid)
    {
        var creates = 0;
        await using var owner = new SamplingOwner(() => { creates++; throw new AssertionException("Must not sample."); }, TimeProvider.System);
        var tools = new DiagnosticTools(owner);
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        if (valid)
        {
            var exception = Assert.ThrowsAsync<TaskCanceledException>(async () =>
                await tools.SamplePerformance(duration, interval, pid, token, domain, cancelled.Token));
            Assert.That(exception!.CancellationToken, Is.EqualTo(cancelled.Token));
        }
        else
        {
            var result = await tools.SamplePerformance(duration, interval, pid, token, domain, cancelled.Token);
            var json = result.StructuredContent!.Value;
            Assert.That(json.GetProperty("status").GetString(), Is.EqualTo("invalidInput"));
            Assert.That(json.GetProperty("collectionError").GetString(), Is.EqualTo("invalidInput"));
            Assert.That(json.GetProperty("data").ValueKind, Is.EqualTo(JsonValueKind.Null));
            Assert.That(result.IsError, Is.True);
        }
        Assert.That(creates, Is.Zero);
    }

    [TestCase("disk", "diskName", "disk-original")]
    [TestCase("network", "networkId", "network")]
    [TestCase("gpu", "gpuId", "gpu")]
    public async Task ControlledFramesPreserveWeightedBucketsAndTargetMetadata(string target, string property, string original)
    {
        await using var harness = await Harness.Create();
        var request = harness.Tools.SamplePerformance(2, 1);
        await harness.Clock.TimerCreated.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await harness.Publish(10, "changed", target: target);
        await harness.Publish(30, original, target: target);
        var response = (await request.WaitAsync(TimeSpan.FromSeconds(5))).StructuredContent!.Value;
        var data = response.GetProperty("data");
        Assert.That(response.GetProperty("status").GetString(), Is.EqualTo("ok"));
        Assert.That(data.GetProperty("actualDurationSeconds").GetDouble(), Is.EqualTo(2));
        Assert.That(data.GetProperty("targetsChanged").GetBoolean(), Is.True);
        Assert.That(data.GetProperty("startingTargets").GetProperty(property).GetString(), Is.EqualTo(original));
        Assert.That(data.GetProperty("endingTargets").GetProperty(property).GetString(), Is.EqualTo(original));
        var cpu = data.GetProperty("metrics").GetProperty("cpuPercent");
        Assert.That(cpu.GetProperty("average").GetDouble(), Is.EqualTo(20));
        Assert.That(cpu.GetProperty("validSampleCount").GetInt32(), Is.EqualTo(2));
        Assert.That(cpu.GetProperty("coverage").GetDouble(), Is.EqualTo(1));
        Assert.That(data.GetProperty("trend").EnumerateArray().Select(bucket => bucket.GetProperty("metrics").GetProperty("cpuPercent").GetProperty("average").GetDouble()), Is.EqualTo(new[] { 10d, 30d }));
    }

    [Test]
    public async Task GapDoesNotEndPinnedLifetimeButReusedPidDoes()
    {
        await using var harness = await Harness.Create();
        var request = harness.Tools.SamplePerformance(4, 1, 42, "100", "WindowsFileTime");
        await harness.Clock.TimerCreated.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await harness.Publish(90, gap: true, process: false);
        Assert.That(request.IsCompleted, Is.False);
        await harness.Publish(99, processToken: 101, error: SamplingError.CpuUnavailable);
        var response = (await request.WaitAsync(TimeSpan.FromSeconds(5))).StructuredContent!.Value;
        var data = response.GetProperty("data");
        Assert.That(response.GetProperty("status").GetString(), Is.EqualTo("notObserved"));
        Assert.That(data.GetProperty("identity").GetProperty("startToken").GetString(), Is.EqualTo("100"));
        Assert.That(data.GetProperty("actualDurationSeconds").GetDouble(), Is.EqualTo(2));
        Assert.That(data.GetProperty("metrics").GetProperty("cpuPercent").GetProperty("available").GetBoolean(), Is.False);
        Assert.That(data.GetProperty("metrics").GetProperty("processCpuPercent").GetProperty("available").GetBoolean(), Is.False);
        Assert.That(data.GetProperty("collectionErrors").EnumerateArray().Select(value => value.GetString()), Is.EqualTo(new[] { "CpuUnavailable" }));
    }

    [Test]
    public async Task CollectionErrorsRemainSortedDistinctAndDoNotReplaceSuccessfulStatus()
    {
        await using var harness = await Harness.Create();
        var request = harness.Tools.SamplePerformance(4, 1);
        await harness.Clock.TimerCreated.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await harness.Publish(10, error: SamplingError.ReadingsUnavailable);
        await harness.Publish(10, error: SamplingError.CpuUnavailable);
        await harness.Publish(10, error: SamplingError.ReadingsUnavailable);
        await harness.Publish(10);
        var response = (await request.WaitAsync(TimeSpan.FromSeconds(5))).StructuredContent!.Value;
        Assert.That(response.GetProperty("status").GetString(), Is.EqualTo("ok"));
        Assert.That(response.GetProperty("data").GetProperty("collectionErrors").EnumerateArray().Select(value => value.GetString()),
            Is.EqualTo(new[] { "CpuUnavailable", "ReadingsUnavailable" }));
    }

    [Test]
    public async Task InitialDomainMismatchRemainsAnErrorAndReleasesTheSlot()
    {
        await using var harness = await Harness.Create();
        for (var i = 0; i < 5; i++)
        {
            var response = await harness.Tools.SamplePerformance(2, 1, 42, "100", "UnixMicroseconds");
            Assert.That(response.IsError, Is.True);
            Assert.That(response.StructuredContent!.Value.GetProperty("status").GetString(), Is.EqualTo("identityMismatch"));
        }
    }

    [Test]
    public async Task StaleInitialPublicationReturnsNoHistoricalData()
    {
        await using var harness = await Harness.Create();
        harness.Clock.Advance(TimeSpan.FromSeconds(3));
        var response = (await harness.Tools.SamplePerformance(2, 1)).StructuredContent!.Value;
        Assert.That(response.GetProperty("status").GetString(), Is.EqualTo("unavailable"));
        Assert.That(response.GetProperty("freshness").GetString(), Is.EqualTo("stale"));
        Assert.That(response.GetProperty("data").ValueKind, Is.EqualTo(JsonValueKind.Null));
    }

    [Test]
    public async Task MissingInitialPublicationTimesOutWithoutStartingTheCollectionWindow()
    {
        await using var harness = await Harness.Create(warm: false);
        var request = harness.Tools.SamplePerformance(2, 1);
        await harness.Clock.CurrentCreated.Task.WaitAsync(TimeSpan.FromSeconds(5));
        harness.Clock.Advance(TimeSpan.FromSeconds(5));
        var response = (await request.WaitAsync(TimeSpan.FromSeconds(5))).StructuredContent!.Value;
        Assert.That(response.GetProperty("status").GetString(), Is.EqualTo("unavailable"));
        Assert.That(response.GetProperty("freshness").GetString(), Is.EqualTo("unavailable"));
        Assert.That(response.GetProperty("data").ValueKind, Is.EqualTo(JsonValueKind.Null));
        Assert.That(harness.Clock.TimerCreated.Task.IsCompleted, Is.False);
    }

    [TestCase(false)] [TestCase(true)]
    public async Task InitialIdentityRejectionsReleaseTheSamplingSlot(bool mismatch)
    {
        await using var harness = await Harness.Create();
        for (var i = 0; i < 5; i++)
        {
            var result = await harness.Tools.SamplePerformance(2, 1, mismatch ? 42 : 999,
                mismatch ? "101" : null, mismatch ? "WindowsFileTime" : null);
            Assert.That(result.StructuredContent!.Value.GetProperty("status").GetString(), Is.EqualTo(mismatch ? "identityMismatch" : "notObserved"));
            Assert.That(result.StructuredContent!.Value.GetProperty("data").ValueKind, Is.EqualTo(JsonValueKind.Null));
        }
    }

    [Test]
    public async Task DeadlineReturnsHistoricalIncompleteDataAtItsExactBoundary()
    {
        await using var harness = await Harness.Create();
        var request = harness.Tools.SamplePerformance(2, 1);
        await harness.Clock.TimerCreated.Task.WaitAsync(TimeSpan.FromSeconds(5));
        harness.Clock.Advance(TimeSpan.FromSeconds(4) - TimeSpan.FromTicks(1));
        Assert.That(request.IsCompleted, Is.False);
        harness.Clock.Advance(TimeSpan.FromTicks(1));
        var response = (await request.WaitAsync(TimeSpan.FromSeconds(5))).StructuredContent!.Value;
        Assert.That(response.GetProperty("status").GetString(), Is.EqualTo("incomplete"));
        Assert.That(response.GetProperty("freshness").GetString(), Is.EqualTo("stale"));
        Assert.That(response.GetProperty("data").GetProperty("actualDurationSeconds").GetDouble(), Is.EqualTo(2));
        Assert.That(response.GetProperty("data").GetProperty("trend").GetArrayLength(), Is.EqualTo(2));
        Assert.That(response.GetProperty("data").GetProperty("metrics").GetProperty("cpuPercent").GetProperty("available").GetBoolean(), Is.False);
    }

    [Test]
    public async Task CallerCancellationPropagatesInsteadOfReturningDeadlineData()
    {
        await using var harness = await Harness.Create();
        using var cancelled = new CancellationTokenSource();
        var request = harness.Tools.SamplePerformance(2, 1, cancellationToken: cancelled.Token);
        await harness.Clock.TimerCreated.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancelled.Cancel();
        Assert.CatchAsync<OperationCanceledException>(async () => await request.WaitAsync(TimeSpan.FromSeconds(5)));
        for (var i = 0; i < 5; i++)
            Assert.That((await harness.Tools.SamplePerformance(2, 1, 999)).StructuredContent!.Value.GetProperty("status").GetString(), Is.EqualTo("notObserved"));
    }

    internal static async Task<JsonElement> BenchmarkRequest(string mode)
    {
        if (mode == "invalid")
        {
            await using var owner = new SamplingOwner(() => throw new InvalidOperationException("Unexpected collection."), new Clock());
            return (await new DiagnosticTools(owner).SamplePerformance(0)).StructuredContent!.Value;
        }
        await using var harness = await Harness.Create();
        var process = mode != "system";
        var request = harness.Tools.SamplePerformance(2, 1, process ? 42 : null,
            process ? "100" : null, process ? "WindowsFileTime" : null);
        await harness.Clock.TimerCreated.Task.ConfigureAwait(false);
        await harness.Publish(10).ConfigureAwait(false);
        await harness.Publish(30, processToken: mode == "exit" ? 101 : 100).ConfigureAwait(false);
        return (await request.ConfigureAwait(false)).StructuredContent!.Value;
    }

    private sealed class Clock : TimeProvider
    {
        private readonly SamplingOwnerTests.ManualClock _inner = new();
        internal readonly TaskCompletionSource TimerCreated = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource CurrentCreated = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override long TimestampFrequency => _inner.TimestampFrequency;
        public override long GetTimestamp() => _inner.GetTimestamp();
        public override DateTimeOffset GetUtcNow() => new DateTimeOffset(2026, 11, 22, 0, 0, 0, TimeSpan.Zero).AddTicks(GetTimestamp());
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = _inner.CreateTimer(callback, state, dueTime, period);
            if (dueTime == TimeSpan.FromSeconds(4) || dueTime == TimeSpan.FromSeconds(6)) TimerCreated.TrySetResult();
            if (dueTime == TimeSpan.FromSeconds(5)) CurrentCreated.TrySetResult();
            return timer;
        }
        internal void Advance(TimeSpan elapsed) => _inner.Advance(elapsed);
    }

    private sealed class Source : IFrameSource
    {
        internal readonly BlockingCollection<CapturedFrame> Frames = new();
        public CapturedFrame Capture(SamplingOptions options) => Frames.Take();
        public void Reset() { }
        public void Dispose() => Frames.Dispose();
    }

    private sealed class Harness : IAsyncDisposable
    {
        private readonly Source _source = new();
        private IAsyncDisposable? _lease;
        internal Clock Clock { get; } = new();
        internal SamplingOwner Owner { get; }
        internal DiagnosticTools Tools { get; }
        private Harness()
        {
            Owner = new SamplingOwner(() => _source, Clock, TimeSpan.FromMilliseconds(1));
            Tools = new DiagnosticTools(Owner);
        }
        internal static async Task<Harness> Create(bool warm = true)
        {
            var result = new Harness();
            try
            {
                result._lease = await result.Owner.AcquireAsync(default);
                if (warm)
                {
                    result._source.Frames.Add(result.Frame(5));
                    result._source.Frames.Add(result.Frame(5));
                    await result.Owner.CurrentAsync(default).WaitAsync(TimeSpan.FromSeconds(5));
                }
                return result;
            }
            catch { await result.DisposeAsync(); throw; }
        }
        private CapturedFrame Frame(double cpu, string name = "disk-original", bool gap = false, bool process = true, long processToken = 100,
            string target = "disk", SamplingError? error = null) =>
            new(new MonitorSample(Clock.GetUtcNow(), cpu, process ? [new ProcessUsage(42, processToken, "fixture", 20)] : [], 1, gap)
            { IntervalSeconds = 1, Resources = new ResourceSample { DiskName = target == "disk" ? name : "disk-original",
                NetworkId = target == "network" ? name : "network", GpuId = target == "gpu" ? name : "gpu" } }, Error: error);
        internal async Task Publish(double cpu, string name = "disk-original", bool gap = false, bool process = true, long processToken = 100,
            string target = "disk", SamplingError? error = null)
        {
            var sequence = Owner.ReadAfter(0).Last().Sequence;
            Clock.Advance(TimeSpan.FromSeconds(1));
            _source.Frames.Add(Frame(cpu, name, gap, process, processToken, target, error));
            await Owner.WaitForChangeAsync(sequence, default).WaitAsync(TimeSpan.FromSeconds(5));
        }
        public async ValueTask DisposeAsync()
        {
            var stop = Owner.StopAsync(default);
            _source.Frames.CompleteAdding();
            await stop.WaitAsync(TimeSpan.FromSeconds(5));
            if (_lease is not null) await _lease.DisposeAsync();
            await Owner.DisposeAsync();
        }
    }
}
