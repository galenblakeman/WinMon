using System.Text.Json;
using NUnit.Framework;
using WinMon.Mcp;
using WinMon.Monitoring;

namespace WinMon.Tests;

[TestFixture]
public sealed class UsageArchiveToolsTests
{
    [Test]
    public async Task ReadsSavedCalendarTotalsWithoutCollectingOrDisclosingPaths()
    {
        var folder = Path.Combine(Path.GetTempPath(), "WinMon-archive-test-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(folder, "usage-history.json");
        try
        {
            var archive = new UsageArchive(TimeZoneInfo.Utc);
            foreach (var day in new[] { 14, 15 })
                archive.Add(new MonitorSample(new DateTimeOffset(2026, 11, day, 12, 0, 0, TimeSpan.Zero), 10,
                    [new ProcessUsage(42, 123, "worker", 10) { ImagePath = "C:\\private\\secret.exe", IoBytesPerSecond = 20 }], 0)
                    { IntervalSeconds = 1, IntervalCpuTicks = 10_000_000 });
            await UsageArchive.SaveAsync(path, archive.Export());
            var original = await File.ReadAllBytesAsync(path);
            await using var owner = new SamplingOwner(() => throw new AssertionException("Archive reads must not collect"), TimeProvider.System);
            var tools = new DiagnosticTools(owner, readUsageArchive: (_, token) => UsageArchive.LoadAsync(path, token));
            var json = (await tools.GetUsageHistory("2026-11-14", "2026-11-15")).StructuredContent!.Value;
            var data = json.GetProperty("data");
            var portable = (await new DiagnosticTools(owner).GetUsageHistory("2026-11-14", "2026-11-15", filePath: path)).StructuredContent!.Value;
            Assert.That(portable.GetProperty("data").GetRawText(), Is.EqualTo(data.GetRawText()));
            Assert.Multiple(() =>
            {
                Assert.That(json.GetProperty("status").GetString(), Is.EqualTo("ok"));
                Assert.That(data.GetProperty("observedSeconds").GetDouble(), Is.EqualTo(2));
                Assert.That(data.GetProperty("complete").GetBoolean(), Is.False);
                Assert.That(data.GetProperty("apps")[0].GetProperty("dailyLifetimeObservations").GetInt32(), Is.EqualTo(2));
                Assert.That(data.GetProperty("apps")[0].GetProperty("ioBytes").GetDouble(), Is.EqualTo(40));
                Assert.That(json.GetRawText(), Does.Not.Contain("secret").And.Not.Contain("imagePath").And.Not.Contain("running"));
            });
            var filtered = (await tools.GetUsageHistory("2026-11-14", "2026-11-15", filter: "secret.exe")).StructuredContent!.Value;
            Assert.That(filtered.GetProperty("data").GetProperty("apps").GetArrayLength(), Is.Zero);
            var empty = (await tools.GetUsageHistory("2026-10-01", "2026-10-02")).StructuredContent!.Value;
            Assert.That(empty.GetProperty("status").GetString(), Is.EqualTo("notObserved"));
            Assert.That(await File.ReadAllBytesAsync(path), Is.EqualTo(original));
            await File.WriteAllTextAsync(path, "invalid private content");
            var invalid = (await tools.GetUsageHistory("2026-11-14", "2026-11-15")).StructuredContent!.Value;
            Assert.That(invalid.GetProperty("status").GetString(), Is.EqualTo("unavailable"));
            Assert.That(invalid.GetRawText(), Does.Not.Contain("private"));
            await File.WriteAllTextAsync(path, "{}");
            Assert.That((await tools.GetUsageHistory("2026-11-14", "2026-11-15")).StructuredContent!.Value
                .GetProperty("status").GetString(), Is.EqualTo("unavailable"));
            File.Delete(path);
            Assert.That((await tools.GetUsageHistory("2026-11-14", "2026-11-15")).StructuredContent!.Value
                .GetProperty("status").GetString(), Is.EqualTo("notObserved"));
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }

    [TestCase("2026-11-15", "2026-11-14")]
    [TestCase("2026-10-01", "2026-11-15")]
    [TestCase("2026-02-30", "2026-11-15")]
    [TestCase("2026-1-1", "2026-01-02")]
    [TestCase("9999-12-31", "9999-12-31")]
    public async Task RejectsInvalidDatesBeforeReading(string first, string last)
    {
        await using var owner = new SamplingOwner();
        var tools = new DiagnosticTools(owner, readUsageArchive: (_, _) => throw new AssertionException("Invalid request read the archive"));
        Assert.That((await tools.GetUsageHistory(first, last)).IsError, Is.True);
    }

    [Test]
    public async Task CalendarBoundsUseArchiveTimeZoneAndLimitDoesNotDiscardTotals()
    {
        var archive = new UsageArchive(TimeZoneInfo.FindSystemTimeZoneById("Tokyo Standard Time"));
        archive.Add(new MonitorSample(new DateTimeOffset(2026, 11, 15, 0, 0, 1, TimeSpan.FromHours(9)), 10,
            [new ProcessUsage(1, 123, "one", 10), new ProcessUsage(2, 456, "two", 20)], 0)
            { IntervalSeconds = 2, IntervalCpuTicks = 10_000_000 });
        await using var owner = new SamplingOwner(() => throw new AssertionException("Unexpected sampling"), TimeProvider.System);
        var tools = new DiagnosticTools(owner, readUsageArchive: (_, _) => Task.FromResult(archive));
        var result = (await tools.GetUsageHistory("2026-11-15", "2026-11-15", limit: 1)).StructuredContent!.Value;
        var data = result.GetProperty("data");
        Assert.That(result.GetProperty("truncated").GetBoolean(), Is.True);
        Assert.That(data.GetProperty("apps").GetArrayLength(), Is.EqualTo(1));
        Assert.That(data.GetProperty("observedSeconds").GetDouble(), Is.EqualTo(1));
        Assert.That(data.GetProperty("sinceUtc").GetDateTimeOffset(), Is.EqualTo(new DateTimeOffset(2026, 11, 14, 15, 0, 0, TimeSpan.Zero)));
        Assert.That(data.GetProperty("untilUtc").GetDateTimeOffset(), Is.EqualTo(new DateTimeOffset(2026, 11, 15, 15, 0, 0, TimeSpan.Zero)));
        Assert.That((await tools.GetUsageHistory("2026-10-12", "2026-11-15")).IsError, Is.Not.True);
    }

    [Test]
    public async Task ValidatesOptionsHonorsCancellationAndBoundsResponses()
    {
        await using var owner = new SamplingOwner();
        var tools = new DiagnosticTools(owner, readUsageArchive: (_, _) => throw new AssertionException("Unexpected read"));
        Assert.That((await tools.GetUsageHistory("2026-11-01", "2026-11-15", limit: 0)).IsError, Is.True);
        Assert.That((await tools.GetUsageHistory("2026-11-01", "2026-11-15", sort: "bad")).IsError, Is.True);
        foreach (var path in new[] { "", "relative.json", "\\\\server\\share\\usage-history.json", "//server/share/usage-history.json", "\\/server/share/usage-history.json", "\\\\?\\C:\\usage-history.json" })
            Assert.That((await tools.GetUsageHistory("2026-11-01", "2026-11-15", filePath: path)).IsError, Is.True);
        Assert.That((await tools.GetUsageHistory("2026-11-01", "2026-11-15", filter: new string('x', 257))).IsError, Is.True);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await tools.GetUsageHistory("2026-11-01", "2026-11-15", cancellationToken: cancellation.Token));
        var data = new ArchivedUsageResult("UTC", new(2026, 11, 1), new(2026, 11, 15), owner.UtcNow, owner.UtcNow,
            null, null, 1, 1, 0, false, Enumerable.Range(0, 200).Select(_ =>
                new ArchivedUsageEntry(new string('x', 512), new string('y', 512), false, 1, 1, null, 1, null)).ToArray());
        var result = Responses.Result(new DiagnosticResponse(1, owner.UtcNow, "ok", "historical", 0, false, 0, null, data));
        Assert.That(JsonSerializer.SerializeToUtf8Bytes(result, Responses.Json).Length, Is.LessThanOrEqualTo(Responses.MaximumBytes));
        Assert.That(result.StructuredContent!.Value.GetProperty("truncated").GetBoolean(), Is.True);
        Assert.That(result.StructuredContent.Value.GetProperty("data").GetProperty("observedSeconds").GetDouble(), Is.EqualTo(1));
    }
}
