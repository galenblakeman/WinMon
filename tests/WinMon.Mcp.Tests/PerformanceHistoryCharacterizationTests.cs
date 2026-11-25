using System.Collections.Concurrent;
using System.Text.Json;
using MonCore.Monitoring;
using NUnit.Framework;

namespace WinMon.Mcp.Tests;

[TestFixture]
public sealed class PerformanceHistoryCharacterizationTests
{
    private static readonly DateTimeOffset Origin = new(2026, 11, 22, 0, 0, 0, TimeSpan.Zero);

    [TestCase("seconds-zero")]
    [TestCase("seconds-negative")]
    [TestCase("seconds-high")]
    [TestCase("top-zero")]
    [TestCase("top-negative")]
    [TestCase("top-high")]
    [TestCase("sort-null")]
    [TestCase("sort-case")]
    [TestCase("sort-unknown")]
    [TestCase("pid-zero")]
    [TestCase("pid-negative")]
    [TestCase("token-missing")]
    [TestCase("domain-missing")]
    [TestCase("token-zero")]
    [TestCase("token-leading-zero")]
    [TestCase("token-negative")]
    [TestCase("token-overflow")]
    [TestCase("domain-case")]
    [TestCase("domain-unknown")]
    [TestCase("orphan-token")]
    [TestCase("orphan-domain")]
    public async Task InvalidOptionsPreserveSynchronousCancellationPriority(string kind)
    {
        var seconds = kind == "seconds-zero" ? 0 : kind == "seconds-negative" ? -1 : kind == "seconds-high" ? 121 : 120;
        var top = kind == "top-zero" ? 0 : kind == "top-negative" ? -1 : kind == "top-high" ? 21 : 5;
        var sort = kind == "sort-null" ? null! : kind == "sort-case" ? "CPU" : kind == "sort-unknown" ? "unknown" : "cpu";
        int? pid = kind.StartsWith("pid-", StringComparison.Ordinal) || kind.StartsWith("token-", StringComparison.Ordinal) || kind.StartsWith("domain-", StringComparison.Ordinal) ? 42 : null;
        string? token = pid is null ? null : "100", domain = pid is null ? null : "WindowsFileTime";
        switch (kind)
        {
            case "pid-zero": pid = 0; break;
            case "pid-negative": pid = -1; break;
            case "token-missing": token = null; break;
            case "domain-missing": domain = null; break;
            case "token-zero": token = "0"; break;
            case "token-leading-zero": token = "0100"; break;
            case "token-negative": token = "-1"; break;
            case "token-overflow": token = "9223372036854775808"; break;
            case "domain-case": domain = "windowsfiletime"; break;
            case "domain-unknown": domain = "unknown"; break;
            case "orphan-token": token = "100"; break;
            case "orphan-domain": domain = "WindowsFileTime"; break;
        }
        await using var owner = new SamplingOwner(() => throw new AssertionException("History must not start sampling."), new Clock());
        var tools = new DiagnosticTools(owner);
        var result = await tools.GetPerformanceHistory(seconds, sort: sort, top: top, pid: pid, expectedStartToken: token, expectedDomain: domain);
        Assert.That(result.IsError, Is.True);
        Assert.That(result.StructuredContent!.Value.GetProperty("status").GetString(), Is.EqualTo("invalidInput"));
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var exception = Assert.Throws<OperationCanceledException>(() => tools.GetPerformanceHistory(seconds, sort: sort, top: top,
            cancellationToken: cancelled.Token, pid: pid, expectedStartToken: token, expectedDomain: domain));
        Assert.That(exception!.CancellationToken, Is.EqualTo(cancelled.Token));
        Assert.That(owner.ReadAfter(0), Is.Empty);
    }

    [TestCase("seconds-min")]
    [TestCase("seconds-max")]
    [TestCase("top-min")]
    [TestCase("top-max")]
    [TestCase("cpu")]
    [TestCase("memory")]
    [TestCase("gpu")]
    [TestCase("io")]
    [TestCase("threads")]
    [TestCase("handles")]
    [TestCase("windows-pin")]
    [TestCase("unix-pin")]
    [TestCase("maximum-token")]
    public async Task AcceptedOptionsReadEmptyHistoryWithoutStartingSampling(string kind)
    {
        var seconds = kind == "seconds-min" ? 1 : 120;
        var top = kind == "top-min" ? 1 : kind == "top-max" ? 20 : 5;
        var sort = kind is "cpu" or "memory" or "gpu" or "io" or "threads" or "handles" ? kind : "cpu";
        var pinned = kind.EndsWith("-pin", StringComparison.Ordinal) || kind == "maximum-token";
        await using var owner = new SamplingOwner(() => throw new AssertionException("History must not start sampling."), new Clock());
        var tools = new DiagnosticTools(owner);
        var result = await tools.GetPerformanceHistory(seconds, sort: sort, top: top, pid: pinned ? 42 : null,
            expectedStartToken: pinned ? kind == "maximum-token" ? "9223372036854775807" : "100" : null,
            expectedDomain: pinned ? kind == "unix-pin" ? "UnixMicroseconds" : "WindowsFileTime" : null);
        var json = result.StructuredContent!.Value;
        var data = json.GetProperty("data");
        Assert.That(result.IsError, Is.Not.True);
        Assert.That(json.GetProperty("status").GetString(), Is.EqualTo("notObserved"));
        Assert.That(json.GetProperty("returnedCount").GetInt32(), Is.Zero);
        Assert.That(data.GetProperty("points").GetArrayLength(), Is.Zero);
        Assert.That(data.GetProperty("requestedStartUtc").GetDateTimeOffset(), Is.EqualTo(Origin.AddSeconds(-seconds)));
        Assert.That(data.GetProperty("requestedEndUtc").GetDateTimeOffset(), Is.EqualTo(Origin));
        Assert.That(data.GetProperty("retainedStartUtc").ValueKind, Is.EqualTo(JsonValueKind.Null));
        Assert.That(data.GetProperty("retainedEndUtc").ValueKind, Is.EqualTo(JsonValueKind.Null));
        Assert.That(owner.ReadAfter(0), Is.Empty);
    }

    [Test]
    public async Task ValidRequestCancellationThrowsSynchronouslyWithoutStartingSampling()
    {
        await using var owner = new SamplingOwner(() => throw new AssertionException("Cancelled history must not sample."), new Clock());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var tools = new DiagnosticTools(owner);
        var exception = Assert.Throws<OperationCanceledException>(() => tools.GetPerformanceHistory(cancellationToken: cancellation.Token));
        Assert.That(exception!.CancellationToken, Is.EqualTo(cancellation.Token));
        Assert.That(owner.ReadAfter(0), Is.Empty);
    }

    [TestCase(1)]
    [TestCase(120)]
    public async Task EndTimeBoundsUseUtcAndRejectOnlyUnderflow(int seconds)
    {
        await using var owner = new SamplingOwner(() => throw new AssertionException("Unexpected sampling."), new Clock());
        var tools = new DiagnosticTools(owner);
        var minimum = DateTimeOffset.MinValue.AddSeconds(seconds);
        Assert.That((await tools.GetPerformanceHistory(seconds, minimum.AddTicks(-1))).IsError, Is.True);
        var data = (await tools.GetPerformanceHistory(seconds, minimum)).StructuredContent!.Value.GetProperty("data");
        Assert.That(data.GetProperty("requestedStartUtc").GetDateTimeOffset(), Is.EqualTo(DateTimeOffset.MinValue));
        Assert.That(data.GetProperty("requestedEndUtc").GetDateTimeOffset(), Is.EqualTo(minimum));
        var offsetEnd = new DateTimeOffset(1, 1, 1, 0, 0, 1, TimeSpan.FromHours(-1));
        data = (await tools.GetPerformanceHistory(seconds, offsetEnd)).StructuredContent!.Value.GetProperty("data");
        Assert.That(data.GetProperty("requestedEndUtc").GetDateTimeOffset(), Is.EqualTo(offsetEnd.ToUniversalTime()));
        Assert.That(data.GetProperty("requestedEndUtc").GetDateTimeOffset().Offset, Is.EqualTo(TimeSpan.Zero));
        Assert.That((await tools.GetPerformanceHistory(seconds, DateTimeOffset.MaxValue)).IsError, Is.Not.True);
    }

    [Test]
    public async Task WindowSelectionKeepsExclusiveStartInclusiveEndAndRetainedBounds()
    {
        await using var harness = await Harness.Create();
        for (var second = 1; second <= 3; second++)
            await harness.Publish(Origin.AddSeconds(second).ToOffset(TimeSpan.FromHours(3)), Processes());
        var json = (await harness.Tools.GetPerformanceHistory(2, Origin.AddSeconds(3))).StructuredContent!.Value;
        var data = json.GetProperty("data");
        var points = data.GetProperty("points").EnumerateArray().ToArray();
        Assert.That(points.Select(point => point.GetProperty("timestampUtc").GetDateTimeOffset()),
            Is.EqualTo(new[] { Origin.AddSeconds(2), Origin.AddSeconds(3) }));
        Assert.That(points.Select(point => point.GetProperty("timestampUtc").GetDateTimeOffset().Offset), Is.All.EqualTo(TimeSpan.Zero));
        Assert.That(data.GetProperty("retainedStartUtc").GetDateTimeOffset(), Is.EqualTo(Origin.AddSeconds(-10)));
        Assert.That(data.GetProperty("retainedEndUtc").GetDateTimeOffset().Offset, Is.EqualTo(TimeSpan.FromHours(3)));
        Assert.That(data.GetProperty("requestedStartUtc").GetDateTimeOffset(), Is.EqualTo(Origin.AddSeconds(1)));
        Assert.That(json.GetProperty("returnedCount").GetInt32(), Is.EqualTo(2));
        var clipped = (await harness.Tools.GetPerformanceHistory(1, Origin.AddSeconds(2).ToOffset(TimeSpan.FromHours(-3)))).StructuredContent!.Value;
        Assert.That(clipped.GetProperty("data").GetProperty("points").GetArrayLength(), Is.EqualTo(1));
        Assert.That(clipped.GetProperty("data").GetProperty("points")[0].GetProperty("timestampUtc").GetDateTimeOffset(), Is.EqualTo(Origin.AddSeconds(2)));
    }

    [TestCase("cpu", 1)]
    [TestCase("memory", 2)]
    [TestCase("gpu", 3)]
    [TestCase("io", 2)]
    [TestCase("threads", 3)]
    [TestCase("handles", 1)]
    public async Task EachSortRanksIndividualLifetimesIncludingInactiveRows(string sort, int expectedPid)
    {
        await using var harness = await Harness.Create();
        await harness.Publish(Origin.AddSeconds(1), Processes());
        var json = (await harness.Tools.GetPerformanceHistory(1, Origin.AddSeconds(1), sort, 1)).StructuredContent!.Value;
        var point = json.GetProperty("data").GetProperty("points")[0];
        Assert.That(point.GetProperty("processes").GetArrayLength(), Is.EqualTo(1));
        Assert.That(point.GetProperty("processes")[0].GetProperty("identity").GetProperty("pid").GetInt32(), Is.EqualTo(expectedPid));
        Assert.That(point.GetProperty("resources").GetProperty("diskName").GetString(), Is.EqualTo("history-disk"));
        Assert.That(point.GetProperty("intervalSeconds").GetDouble(), Is.EqualTo(0.5));
        Assert.That(json.GetRawText(), Does.Not.Contain("private-secret").And.Not.Contain("imagePath"));
    }

    [Test]
    public async Task PinnedHistoryPreservesGapsAndRejectsPidReuseAndOtherDomains()
    {
        await using var harness = await Harness.Create();
        var pinned = new ProcessUsage(42, 100, "pinned", 0) { ImagePath = @"C:\private-secret\pinned.exe" };
        var busy = new ProcessUsage(7, 70, "busy", 90);
        await harness.Publish(Origin.AddSeconds(1), [pinned, busy]);
        await harness.Publish(Origin.AddSeconds(2), [], gap: true, error: SamplingError.ReadingsUnavailable);
        await harness.Publish(Origin.AddSeconds(3), [pinned with { CreationTicks = 101 }, busy]);
        await harness.Publish(Origin.AddSeconds(4), [pinned with { LifetimeDomain = ProcessLifetimeDomain.UnixMicroseconds }]);
        await harness.Publish(Origin.AddSeconds(5), [pinned, pinned with { Name = "duplicate identity" }, busy]);
        var json = (await harness.Tools.GetPerformanceHistory(5, Origin.AddSeconds(5), top: 1, pid: 42,
            expectedStartToken: "100", expectedDomain: "WindowsFileTime")).StructuredContent!.Value;
        var points = json.GetProperty("data").GetProperty("points").EnumerateArray().ToArray();
        Assert.That(points.Select(point => point.GetProperty("processes").GetArrayLength()), Is.EqualTo(new[] { 1, 0, 0, 0, 2 }));
        Assert.That(points[1].GetProperty("isGap").GetBoolean(), Is.True);
        Assert.That(points[1].GetProperty("collectionError").GetString(), Is.EqualTo(nameof(SamplingError.ReadingsUnavailable)));
        Assert.That(json.GetProperty("status").GetString(), Is.EqualTo("ok"));
        Assert.That(json.GetProperty("returnedCount").GetInt32(), Is.EqualTo(5));
        Assert.That(json.GetRawText(), Does.Not.Contain("private-secret"));
        var absent = (await harness.Tools.GetPerformanceHistory(5, Origin.AddSeconds(5), pid: 42,
            expectedStartToken: "999", expectedDomain: "WindowsFileTime")).StructuredContent!.Value;
        Assert.That(absent.GetProperty("status").GetString(), Is.EqualTo("notObserved"));
        Assert.That(absent.GetProperty("returnedCount").GetInt32(), Is.EqualTo(5));
        Assert.That(absent.GetProperty("data").GetProperty("points").EnumerateArray().Select(point => point.GetProperty("processes").GetArrayLength()), Is.All.Zero);
    }

    [Test]
    public async Task UnpinnedGapAndStaleHistoryRetainResourcesAndExplicitStatus()
    {
        await using var harness = await Harness.Create();
        await harness.Publish(Origin.AddSeconds(1), [], gap: true, error: SamplingError.ReadingsUnavailable);
        harness.AdvanceClock();
        harness.AdvanceClock();
        harness.AdvanceClock();
        var json = (await harness.Tools.GetPerformanceHistory(1, Origin.AddSeconds(1))).StructuredContent!.Value;
        var point = json.GetProperty("data").GetProperty("points")[0];
        Assert.That(json.GetProperty("status").GetString(), Is.EqualTo("ok"));
        Assert.That(json.GetProperty("freshness").GetString(), Is.EqualTo("stale"));
        Assert.That(json.GetProperty("returnedCount").GetInt32(), Is.EqualTo(1));
        Assert.That(point.GetProperty("isGap").GetBoolean(), Is.True);
        Assert.That(point.GetProperty("collectionError").GetString(), Is.EqualTo(nameof(SamplingError.ReadingsUnavailable)));
        Assert.That(point.GetProperty("processes").GetArrayLength(), Is.Zero);
        Assert.That(point.GetProperty("resources").GetProperty("diskName").GetString(), Is.EqualTo("history-disk"));
        var absent = (await harness.Tools.GetPerformanceHistory(1, Origin.AddHours(-1))).StructuredContent!.Value;
        Assert.That(absent.GetProperty("status").GetString(), Is.EqualTo("notObserved"));
        Assert.That(absent.GetProperty("returnedCount").GetInt32(), Is.Zero);
        Assert.That(absent.GetProperty("data").GetProperty("points").GetArrayLength(), Is.Zero);
        Assert.That(absent.GetProperty("data").GetProperty("retainedEndUtc").GetDateTimeOffset(), Is.EqualTo(Origin.AddSeconds(1)));
    }

    [Test]
    public async Task RetainedHistoryDropsOnlyTheOldestFramesAtTheCapacityBoundary()
    {
        await using var harness = await Harness.Create();
        for (var second = 1; second <= 122; second++) await harness.Publish(Origin.AddSeconds(second), []);
        var json = (await harness.Tools.GetPerformanceHistory(120, Origin.AddSeconds(122))).StructuredContent!.Value;
        var data = json.GetProperty("data");
        var points = data.GetProperty("points").EnumerateArray().ToArray();
        Assert.That(points, Has.Length.EqualTo(120));
        Assert.That(points[0].GetProperty("timestampUtc").GetDateTimeOffset(), Is.EqualTo(Origin.AddSeconds(3)));
        Assert.That(points[^1].GetProperty("timestampUtc").GetDateTimeOffset(), Is.EqualTo(Origin.AddSeconds(122)));
        Assert.That(data.GetProperty("retainedStartUtc").GetDateTimeOffset(), Is.EqualTo(Origin.AddSeconds(3)));
        Assert.That(data.GetProperty("retainedEndUtc").GetDateTimeOffset(), Is.EqualTo(Origin.AddSeconds(122)));
        Assert.That(json.GetProperty("returnedCount").GetInt32(), Is.EqualTo(120));
        Assert.That(harness.Owner.ReadAfter(0), Has.Length.EqualTo(120));
    }

    private static ProcessUsage[] Processes() =>
    [
        new(1, 10, "cpu", 30, 100, 10) { HandleCount = 30, ThreadCount = 10, GpuPercent = 10, ImagePath = @"C:\private-secret\one.exe" },
        new(2, 20, "memory", 0, 300, 30) { HandleCount = 10, ThreadCount = 20, GpuPercent = 20 },
        new(3, 30, "gpu", 10, 200, 20) { HandleCount = 20, ThreadCount = 30, GpuPercent = 30 }
    ];

    internal static async Task<(Func<object> Invoke, IAsyncDisposable Owner)> CreateBenchmarkRequest(string scenario)
    {
        if (scenario is "invalid" or "empty")
        {
            var owner = new SamplingOwner(() => throw new AssertionException("Unexpected sampling."), new Clock());
            var tools = new DiagnosticTools(owner);
            return (() => tools.GetPerformanceHistory(scenario == "invalid" ? 0 : 120).GetAwaiter().GetResult(), owner);
        }
        var harness = await Harness.Create();
        try
        {
            var count = scenario == "retained" ? 120 : 3;
            for (var second = 1; second <= count; second++) await harness.Publish(Origin.AddSeconds(second), Processes());
            return (() => harness.Tools.GetPerformanceHistory(count, Origin.AddSeconds(count), top: 3,
                pid: scenario == "pinned" ? 2 : null, expectedStartToken: scenario == "pinned" ? "20" : null,
                expectedDomain: scenario == "pinned" ? "WindowsFileTime" : null).GetAwaiter().GetResult(), harness);
        }
        catch { await harness.DisposeAsync(); throw; }
    }

    private sealed class Clock : TimeProvider
    {
        private readonly SamplingOwnerTests.ManualClock _inner = new();
        public override long TimestampFrequency => _inner.TimestampFrequency;
        public override long GetTimestamp() => _inner.GetTimestamp();
        public override DateTimeOffset GetUtcNow() => Origin.AddTicks(GetTimestamp());
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) => _inner.CreateTimer(callback, state, dueTime, period);
        internal void Advance() => _inner.Advance(TimeSpan.FromSeconds(1));
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
        private readonly Clock _clock = new();
        private IAsyncDisposable? _lease;
        internal SamplingOwner Owner { get; }
        internal DiagnosticTools Tools { get; }
        private Harness()
        {
            Owner = new SamplingOwner(() => _source, _clock, TimeSpan.FromMilliseconds(1));
            Tools = new DiagnosticTools(Owner);
        }
        internal static async Task<Harness> Create()
        {
            var result = new Harness();
            try
            {
                result._lease = await result.Owner.AcquireAsync(default);
                result._source.Frames.Add(Frame(Origin.AddSeconds(-10), []));
                result._source.Frames.Add(Frame(Origin.AddSeconds(-10), []));
                await result.Owner.CurrentAsync(default).WaitAsync(TimeSpan.FromSeconds(5));
                return result;
            }
            catch { await result.DisposeAsync(); throw; }
        }
        private static CapturedFrame Frame(DateTimeOffset timestamp, ProcessUsage[] processes, bool gap = false, SamplingError? error = null) =>
            new(new MonitorSample(timestamp, 10, processes, 0, gap)
            { IntervalSeconds = 0.5, Resources = new ResourceSample { DiskName = "history-disk" } }, Error: error);
        internal async Task Publish(DateTimeOffset timestamp, ProcessUsage[] processes, bool gap = false, SamplingError? error = null)
        {
            var sequence = Owner.ReadAfter(0).Last().Sequence;
            _clock.Advance();
            _source.Frames.Add(Frame(timestamp, processes, gap, error));
            await Owner.WaitForChangeAsync(sequence, default).WaitAsync(TimeSpan.FromSeconds(5));
        }
        internal void AdvanceClock() => _clock.Advance();
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
