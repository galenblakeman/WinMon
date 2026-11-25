using WinMon.Monitoring;
using System.Diagnostics;
using System.Text.Json;
using MonCore.Monitoring;
using NUnit.Framework;

namespace WinMon.Mcp.Tests;

[TestFixture]
public sealed class ActivityToolsTests
{
    private sealed class Source : IFrameSource
    {
        internal bool MissingServices;
        internal bool ReusePid;
        private int _captures;
        public CapturedFrame Capture(SamplingOptions options)
        {
            var p = new ProcessUsage(42, ReusePid && Interlocked.Increment(ref _captures) % 2 == 0 ? 456 : 123,
                "worker", 25, 100) { ImagePath = "C:\\private\\secret.exe", PrivateBytes = 200 };
            var sample = new MonitorSample(DateTimeOffset.UtcNow, 25, [p, new ProcessUsage(43, 999, "busier", 90)], 0)
                { IntervalSeconds = 1, IntervalCpuTicks = 10_000_000 };
            return new CapturedFrame(sample, MissingServices ? null : new ServiceFrame(sample,
                [new ServiceGroup("private-key", [new ServiceObservation("worker-service", "Worker service", 4, 42, 0),
                    new ServiceObservation("second", "Second service", 4, 42, 0)], p, [p])]));
        }
        public void Reset() { }
        public void Dispose() { }
    }

    [Test]
    public async Task ServiceAndUsageQueriesAreBoundedPrivateAndSessionScoped()
    {
        await using var owner = new SamplingOwner(() => new Source(), TimeProvider.System, TimeSpan.FromMilliseconds(10));
        await owner.StartAsync(default);
        var tools = new DiagnosticTools(owner);
        var services = (await tools.ListServices()).StructuredContent!.Value;
        var usage = (await tools.GetUsage()).StructuredContent!.Value;
        Assert.Multiple(() =>
        {
            Assert.That(services.GetProperty("data")[0].GetProperty("sharedHost").GetBoolean(), Is.True);
            Assert.That(services.GetProperty("data")[0].GetProperty("processes")[0].GetProperty("startToken").GetString(), Is.EqualTo("123"));
            Assert.That(usage.GetProperty("data").GetProperty("apps")[0].GetProperty("cpuTicks").GetInt64(), Is.GreaterThan(0));
            Assert.That(services.GetRawText(), Does.Not.Contain("secret").And.Not.Contain("private-key"));
            Assert.That(usage.GetRawText(), Does.Not.Contain("secret"));
        });
        Assert.That((await tools.GetUsage(filter: "secret.exe")).StructuredContent!.Value.GetProperty("data").GetProperty("apps").GetArrayLength(), Is.Zero);
        Assert.That((await tools.ListServices(filter: "absent")).StructuredContent!.Value.GetProperty("data").GetArrayLength(), Is.Zero);
        Assert.That((await tools.ListServices(limit: 0)).IsError, Is.True);
        Assert.That((await tools.GetUsage(sort: "bad")).IsError, Is.True);
    }

    [Test]
    public async Task MissingServicesAndHistoricalBoundsStayExplicit()
    {
        await using var owner = new SamplingOwner(() => new Source { MissingServices = true }, TimeProvider.System, TimeSpan.FromMilliseconds(10));
        await owner.StartAsync(default);
        var tools = new DiagnosticTools(owner);
        Assert.That((await tools.ListServices()).StructuredContent!.Value.GetProperty("status").GetString(), Is.EqualTo("unavailable"));
        await owner.StopAsync(default);
        var frames = owner.ReadAfter(0);
        var end = frames[^1].Frame.Full.Timestamp;
        var history = (await tools.GetPerformanceHistory(endUtc: end)).StructuredContent!.Value;
        Assert.That(history.GetProperty("data").GetProperty("points").GetArrayLength(), Is.EqualTo(frames.Length));
        Assert.That(history.GetRawText(), Does.Not.Contain("secret.exe"));
        Assert.That((await tools.GetPerformanceHistory(endUtc: end.AddHours(-1))).StructuredContent!.Value.GetProperty("status").GetString(), Is.EqualTo("notObserved"));
        Assert.That((await tools.GetPerformanceHistory(seconds: 121)).IsError, Is.True);
        Assert.That((await tools.GetPerformanceHistory(endUtc: DateTimeOffset.MinValue)).IsError, Is.True);
        Assert.That((await tools.GetPerformanceHistory(endUtc: new DateTimeOffset(1, 1, 1, 0, 0, 1, TimeSpan.FromHours(-1)))).IsError, Is.Not.True);
    }

    [Test]
    public async Task LifetimeHistoryBypassesTopRankingNeverFollowsPidReuseAndPreservesEmptyPoints()
    {
        await using var owner = new SamplingOwner(() => new Source { ReusePid = true }, TimeProvider.System, TimeSpan.FromMilliseconds(10));
        var tools = new DiagnosticTools(owner);
        await tools.GetSystemSnapshot(default);
        await owner.StopAsync(default);
        var frames = owner.ReadAfter(0);
        Assert.That(frames.SelectMany(f => f.Frame.Full.Processes).Where(p => p.ProcessId == 42)
            .Select(p => p.CreationTicks).Distinct().Count(), Is.EqualTo(2));
        var history = (await tools.GetPerformanceHistory(top: 1, pid: 42, expectedStartToken: "123",
            expectedDomain: "WindowsFileTime")).StructuredContent!.Value;
        var points = history.GetProperty("data").GetProperty("points").EnumerateArray().ToArray();
        Assert.That(points.Length, Is.EqualTo(frames.Length));
        Assert.That(points.Any(p => p.GetProperty("processes").GetArrayLength() == 0), Is.True);
        var processes = points.SelectMany(p => p.GetProperty("processes").EnumerateArray()).ToArray();
        Assert.That(processes, Is.Not.Empty);
        Assert.That(processes.Select(p => p.GetProperty("identity").GetProperty("startToken").GetString()), Is.All.EqualTo("123"));
        Assert.That(history.GetRawText(), Does.Not.Contain("secret.exe"));
        var absent = (await tools.GetPerformanceHistory(pid: 42, expectedStartToken: "789",
            expectedDomain: "WindowsFileTime")).StructuredContent!.Value;
        Assert.That(absent.GetProperty("status").GetString(), Is.EqualTo("notObserved"));
        var wrongDomain = (await tools.GetPerformanceHistory(pid: 42, expectedStartToken: "123",
            expectedDomain: "UnixMicroseconds")).StructuredContent!.Value;
        Assert.That(wrongDomain.GetProperty("status").GetString(), Is.EqualTo("notObserved"));
        Assert.That((await tools.GetPerformanceHistory(pid: 42)).IsError, Is.True);
        Assert.That((await tools.GetPerformanceHistory(expectedStartToken: "123", expectedDomain: "WindowsFileTime")).IsError, Is.True);
        Assert.That((await tools.GetPerformanceHistory(pid: 42, expectedStartToken: "123")).IsError, Is.True);
    }

    [Test]
    public async Task ReportsRequireExplicitDisclosureAndRejectTraversalAndForeignFiles()
    {
        var folder = Path.Combine(Path.GetTempPath(), "WinMon-capture-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var path = CaptureReports.Write(folder, DateTimeOffset.Now, CaptureMetric.Cpu, CaptureReports.FirstLine(Guid.NewGuid()) + "\nprivate endpoint\n" + new string('x', 9000));
            File.WriteAllText(Path.Combine(folder, "foreign.txt"), "secret");
            await using var owner = new SamplingOwner(() => new Source(), TimeProvider.System);
            var tools = new DiagnosticTools(owner);
            var files = (await tools.ListCaptureReports(folder)).StructuredContent!.Value.GetProperty("data");
            Assert.That(files.GetArrayLength(), Is.EqualTo(1));
            Assert.That((await tools.ReadCaptureReport(folder, Path.GetFileName(path))).IsError, Is.True);
            Assert.That((await tools.ReadCaptureReport(folder, "..\\foreign.txt", true)).IsError, Is.True);
            Assert.That((await tools.ReadCaptureReport(folder, "foreign.txt", true)).StructuredContent!.Value.GetProperty("status").GetString(), Is.EqualTo("unavailable"));
            var report = (await tools.ReadCaptureReport(folder, Path.GetFileName(path), true)).StructuredContent!.Value;
            Assert.That(report.GetProperty("truncated").GetBoolean(), Is.True);
            Assert.That(report.GetProperty("data").GetProperty("text").GetString(), Does.Contain("private endpoint"));
        }
        finally { Directory.Delete(folder, true); }
    }

    [Test]
    public void LargeUsageAndHistoryResponsesRespectWireBudget()
    {
        var entries = Enumerable.Range(0, 200).Select(_ => new UsageEntry(new string('x', 512), new string('y', 512), false, 1, 1, null, 1, 1, null)).ToArray();
        var response = Responses.Result(new DiagnosticResponse(1, DateTimeOffset.UtcNow, "ok", "current", 1, false, 0, null,
            new UsageResult(DateTimeOffset.UtcNow, 1, entries)));
        Assert.That(JsonSerializer.SerializeToUtf8Bytes(response, Responses.Json).Length, Is.LessThanOrEqualTo(Responses.MaximumBytes));
        Assert.That(response.StructuredContent!.Value.GetProperty("truncated").GetBoolean(), Is.True);
        var point = new HistoryPoint(DateTimeOffset.UtcNow, 1, false, null,
            SystemMetrics.From(new MonitorSample(DateTimeOffset.UtcNow, 1, [], 0)),
            Enumerable.Range(0, 20).Select(i => ProcessMetrics.From(new ProcessUsage(i + 1, 1, new string('x', 512), 1))).ToArray());
        var points = Enumerable.Range(0, 120).Select(i => point with { TimestampUtc = point.TimestampUtc.AddSeconds(i) }).ToArray();
        var history = Responses.Result(new DiagnosticResponse(1, DateTimeOffset.UtcNow, "ok", "current", 1, false, 0, null,
            new HistoryResult(DateTimeOffset.UtcNow.AddSeconds(-120), DateTimeOffset.UtcNow, null, null, points)));
        Assert.That(JsonSerializer.SerializeToUtf8Bytes(history, Responses.Json).Length, Is.LessThanOrEqualTo(Responses.MaximumBytes));
        Assert.That(history.StructuredContent!.Value.GetProperty("truncated").GetBoolean(), Is.True);
        Assert.That(history.StructuredContent.Value.GetProperty("data").GetProperty("points").EnumerateArray().Last().GetProperty("timestampUtc").GetDateTimeOffset(),
            Is.EqualTo(points[^1].TimestampUtc));
    }

    [Test, Explicit("Focused before/after measurement of publishing immutable Usage totals")]
    public void MeasureUsagePublication()
    {
        var processes = Enumerable.Range(1, 850).Select(i => new ProcessUsage(i, i, "worker" + i, 0.01)
            { ImagePath = "C:\\apps\\worker" + i + ".exe" }).ToArray();
        var sample = new MonitorSample(DateTimeOffset.UtcNow, 10, processes, 0) { IntervalSeconds = 1, IntervalCpuTicks = 100_000_000 };
        object? keep = null;
        foreach (var publish in new[] { false, true })
        {
            var ledger = new UsageLedger(sample.Timestamp);
            for (var i = 0; i < 400; i++) { ledger.Add(sample); if (publish) keep = ledger.Apps; }
            var times = new List<double>();
            var allocations = new List<long>();
            for (var batch = 0; batch < 7; batch++)
            {
                var allocated = GC.GetAllocatedBytesForCurrentThread();
                var watch = Stopwatch.StartNew();
                for (var i = 0; i < 100; i++) { ledger.Add(sample); if (publish) keep = ledger.Apps; }
                watch.Stop();
                times.Add(watch.Elapsed.TotalMilliseconds / 100);
                allocations.Add((GC.GetAllocatedBytesForCurrentThread() - allocated) / 100);
            }
            TestContext.Out.WriteLine($"{(publish ? "After (ledger + immutable snapshot)" : "Before (ledger only)")}: {times.Order().ElementAt(3):F4} ms/tick; {allocations.Order().ElementAt(3)} bytes/tick; 850 apps, median of 7 batches");
        }
        GC.KeepAlive(keep);
    }
}
