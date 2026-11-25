using System.Text.Json;
using ModelContextProtocol.Protocol;
using MonCore.Monitoring;
using MonCore.Windows.Monitoring;
using NUnit.Framework;

namespace WinMon.Mcp.Tests;

[TestFixture]
public sealed class CrashToolsTests
{
    [Test]
    public async Task ReturnsHistoricalGroupsSanitizesTextAndAppliesLimit()
    {
        await using var owner = new SamplingOwner(() => throw new AssertionException("Sampling must not start"), TimeProvider.System);
        var other = Event("Other.exe", "1.0", "other.dll", 0xE0434352) with { Timestamp = DateTimeOffset.UnixEpoch };
        var marked = Event("[red]app.exe", "1.0", "module.dll", 0xC0000005) with { Timestamp = DateTimeOffset.UnixEpoch.AddSeconds(1) };
        var tools = new DiagnosticTools(owner, readCrashes: (_, _, _) => Result(other, marked));

        var json = Json(await tools.ListCrashes(limit: 1));
        Assert.Multiple(() =>
        {
            Assert.That(json.GetProperty("status").GetString(), Is.EqualTo("ok"));
            Assert.That(json.GetProperty("freshness").GetString(), Is.EqualTo("historical"));
            Assert.That(json.GetProperty("truncated").GetBoolean(), Is.True);
            Assert.That(json.GetProperty("returnedCount").GetInt32(), Is.EqualTo(1));
            Assert.That(json.GetProperty("data").GetProperty("groups")[0].GetProperty("application").GetString(), Is.EqualTo("[red]app.exe"));
            Assert.That(json.GetProperty("data").GetProperty("groups")[0].GetProperty("exceptionCode").GetString(), Is.EqualTo("0xC0000005"));
        });
    }

    [Test]
    public async Task FilterAndCoverageAreReturnedWithoutEventMessages()
    {
        await using var owner = new SamplingOwner(() => throw new AssertionException("Sampling must not start"), TimeProvider.System);
        var result = Result(Event("Example.exe", "1.0", "example.dll", 0xC0000005));
        var group = CrashGroups.Build(result.Events).Single();
        Assert.That(CrashGroupObservation.From(group).Application, Is.EqualTo("Example.exe"));
        var tools = new DiagnosticTools(owner, readCrashes: (_, _, _) => result);
        var json = Json(await tools.ListCrashes(filter: "example"));
        Assert.Multiple(() =>
        {
            Assert.That(json.GetProperty("data").GetProperty("groups").GetArrayLength(), Is.EqualTo(1));
            Assert.That(json.GetProperty("data").GetProperty("coverage").GetProperty("applicationLogStatus").GetString(), Is.EqualTo("Complete"));
            Assert.That(json.GetRawText(), Does.Not.Contain("private event message"));
        });
    }

    [TestCase(0, "", 50)]
    [TestCase(91, "", 50)]
    [TestCase(30, "", 0)]
    [TestCase(30, "", 101)]
    public async Task InvalidInputDoesNotRead(int days, string filter, int limit)
    {
        await using var owner = new SamplingOwner(() => throw new AssertionException("Sampling must not start"), TimeProvider.System);
        var tools = new DiagnosticTools(owner, readCrashes: (_, _, _) => throw new AssertionException("Must not read"));
        Assert.That(Json(await tools.ListCrashes(days, filter, limit)).GetProperty("status").GetString(), Is.EqualTo("invalidInput"));
    }

    [Test]
    public async Task AcceptsExactDayBoundsAndReportsReaderFailureAsUnavailable()
    {
        await using var owner = new SamplingOwner(() => throw new AssertionException("Sampling must not start"), TimeProvider.System);
        var tools = new DiagnosticTools(owner, readCrashes: (_, _, _) => Result());
        Assert.That(Json(await tools.ListCrashes(days: 1)).GetProperty("status").GetString(), Is.EqualTo("ok"));
        Assert.That(Json(await tools.ListCrashes(days: 90)).GetProperty("status").GetString(), Is.EqualTo("ok"));
        var unavailable = new DiagnosticTools(owner, readCrashes: (_, _, _) => throw new InvalidOperationException());
        Assert.That(Json(await unavailable.ListCrashes()).GetProperty("status").GetString(), Is.EqualTo("unavailable"));
    }

    [Test]
    public async Task DefaultsAndMaximumLimitArePassedToTheReaderWithoutStartingSampling()
    {
        await using var owner = new SamplingOwner(() => throw new AssertionException("Sampling must not start"), TimeProvider.System);
        TimeSpan? range = null;
        var groups = Enumerable.Range(0, 101).Select(index => Event($"app{index}.exe", "1.0", $"module{index}.dll", 0xC0000005)).ToArray();
        var tools = new DiagnosticTools(owner, readCrashes: (start, end, _) =>
        {
            range = end - start;
            return Result(groups);
        });
        var defaultResponse = Json(await tools.ListCrashes());
        var maximumResponse = Json(await tools.ListCrashes(limit: 100));
        Assert.Multiple(() =>
        {
            Assert.That(range, Is.EqualTo(TimeSpan.FromDays(30)));
            Assert.That(defaultResponse.GetProperty("data").GetProperty("days").GetInt32(), Is.EqualTo(30));
            Assert.That(defaultResponse.GetProperty("returnedCount").GetInt32(), Is.EqualTo(50));
            Assert.That(maximumResponse.GetProperty("returnedCount").GetInt32(), Is.EqualTo(100));
            Assert.That(maximumResponse.GetProperty("truncated").GetBoolean(), Is.True);
        });
    }

    [Test]
    public async Task CancellationPropagatesFromTheReader()
    {
        await using var owner = new SamplingOwner(() => throw new AssertionException("Sampling must not start"), TimeProvider.System);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var tools = new DiagnosticTools(owner, readCrashes: (_, _, token) =>
        {
            token.ThrowIfCancellationRequested();
            return Result();
        });
        Assert.That(async () => await tools.ListCrashes(cancellationToken: cancellation.Token), Throws.InstanceOf<OperationCanceledException>());
    }

    [Test]
    public void ResponseBudgetTrimsGroupsButPreservesCrashCoverage()
    {
        var groups = Enumerable.Range(0, 100).Select(index => new CrashGroupObservation("Crash", new string('a', 512), false,
            new string('m', 512), false, "0xC0000005", "Access violation", "0x1", 1, DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch, Enumerable.Range(0, 100).Select(version => new CrashVersionObservation(new string('v', 512), null, 1)).ToArray())).ToArray();
        var response = Responses.Result(new DiagnosticResponse(1, DateTimeOffset.UtcNow, "ok", "historical", 0, true, 0, null,
            new CrashListResult(30, new CrashCoverageObservation("Complete", false, DateTimeOffset.UnixEpoch), groups), ReturnedCount: groups.Length));
        var json = Json(response);
        Assert.Multiple(() =>
        {
            Assert.That(JsonSerializer.SerializeToUtf8Bytes(response, Responses.Json).Length, Is.LessThanOrEqualTo(Responses.MaximumBytes));
            Assert.That(json.GetProperty("truncated").GetBoolean(), Is.True);
            Assert.That(json.GetProperty("data").GetProperty("coverage").GetProperty("applicationLogStatus").GetString(), Is.EqualTo("Complete"));
        });
    }

    [Test]
    public void ResponseBudgetRetainsOneGroupWhileTrimmingItsVersionBreakdown()
    {
        var versions = Enumerable.Range(0, 2_000).Select(version => new CrashVersionObservation(new string('v', 512),
            new string('m', 512), 1)).ToArray();
        var group = new CrashGroupObservation("Crash", "example.exe", false, "example.dll", false, "0xC0000005",
            "Access violation", "0x1", versions.Length, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, versions);
        var response = Responses.Result(new DiagnosticResponse(1, DateTimeOffset.UtcNow, "ok", "historical", 0, true, 0, null,
            new CrashListResult(30, new CrashCoverageObservation("Complete", false, null), [group]), ReturnedCount: 1));
        var json = Json(response);
        Assert.Multiple(() =>
        {
            Assert.That(JsonSerializer.SerializeToUtf8Bytes(response, Responses.Json).Length, Is.LessThanOrEqualTo(Responses.MaximumBytes));
            Assert.That(json.GetProperty("data").GetProperty("groups").GetArrayLength(), Is.EqualTo(1));
            Assert.That(json.GetProperty("data").GetProperty("groups")[0].GetProperty("versions").GetArrayLength(), Is.LessThan(versions.Length));
        });
    }

    private static CrashEventResult Result(params IncidentEvent[] events) => new(events,
        new IncidentEventLogCoverage("Application", IncidentEventCoverageStatus.Complete), false,
        DateTimeOffset.UnixEpoch);

    private static IncidentEvent Event(string application, string version, string module, uint code) => new(DateTimeOffset.UtcNow,
        "Application Error", 1000, IncidentEventLevel.Error, "private event message", CrashSignature: new(CrashKind.Crash,
            application, version, module, "1.0", code, 1));

    private static JsonElement Json(CallToolResult result) => result.StructuredContent!.Value;
}
