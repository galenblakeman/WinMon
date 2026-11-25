using System.Text.Json;
using NUnit.Framework;
using WinMon.Mcp;
using WinMon.Monitoring;

namespace WinMon.Tests;

[TestFixture]
public sealed class UsageHistoryCharacterizationTests
{
    [TestCase("first-null")]
    [TestCase("last-null")]
    [TestCase("first-format")]
    [TestCase("last-format")]
    [TestCase("last-maximum")]
    [TestCase("reversed")]
    [TestCase("36-days")]
    [TestCase("sort-null")]
    [TestCase("sort-case")]
    [TestCase("filter-null")]
    [TestCase("filter-long")]
    [TestCase("limit-zero")]
    [TestCase("limit-negative")]
    [TestCase("limit-high")]
    [TestCase("path-empty")]
    [TestCase("path-short")]
    [TestCase("path-long")]
    [TestCase("path-unicode-drive")]
    [TestCase("path-no-colon")]
    [TestCase("path-drive-relative")]
    [TestCase("path-unc")]
    [TestCase("path-device")]
    public async Task InvalidArchiveRequestsNeverReadEvenWhenCancelled(string kind)
    {
        string first = "2026-11-01", last = "2026-11-15", sort = "cpu-time", filter = "";
        string? path = null;
        var limit = 20;
        switch (kind)
        {
            case "first-null": first = null!; break;
            case "last-null": last = null!; break;
            case "first-format": first = "2026-1-01"; break;
            case "last-format": last = "2026-02-30"; break;
            case "last-maximum": first = last = "9999-12-31"; break;
            case "reversed": first = "2026-11-16"; break;
            case "36-days": last = "2026-12-06"; break;
            case "sort-null": sort = null!; break;
            case "sort-case": sort = "CPU-TIME"; break;
            case "filter-null": filter = null!; break;
            case "filter-long": filter = new string('x', 257); break;
            case "limit-zero": limit = 0; break;
            case "limit-negative": limit = -1; break;
            case "limit-high": limit = 201; break;
            case "path-empty": path = ""; break;
            case "path-short": path = "C:"; break;
            case "path-long": path = "C:\\" + new string('x', 1022); break;
            case "path-unicode-drive": path = "é:\\usage.json"; break;
            case "path-no-colon": path = "C/usage.json"; break;
            case "path-drive-relative": path = "C:usage.json"; break;
            case "path-unc": path = @"\\server\share\usage.json"; break;
            case "path-device": path = @"\\?\C:\usage.json"; break;
        }
        await using var owner = Owner();
        var calls = 0;
        var tools = new DiagnosticTools(owner, readUsageArchive: (_, _) =>
        {
            calls++;
            throw new AssertionException("Invalid archive request reached the reader.");
        });
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var result = await tools.GetUsageHistory(first, last, sort, filter, limit, path, cancellation.Token);
        Assert.That(result.IsError, Is.True);
        Assert.That(result.StructuredContent!.Value.GetProperty("status").GetString(), Is.EqualTo("invalidInput"));
        Assert.That(calls, Is.Zero);
    }

    [TestCase("defaults")]
    [TestCase("35-days")]
    [TestCase("one-day")]
    [TestCase("minimum-limit")]
    [TestCase("maximum-limit")]
    [TestCase("maximum-filter")]
    [TestCase("minimum-path")]
    [TestCase("maximum-path")]
    [TestCase("unnormalized-path")]
    [TestCase("io")]
    [TestCase("gpu-time")]
    [TestCase("processes")]
    [TestCase("last-active")]
    public async Task AcceptedBoundaryOptionsReachOnlyTheArchiveReader(string kind)
    {
        const string first = "2026-11-01";
        var last = kind == "35-days" ? "2026-12-05" : kind == "one-day" ? first : "2026-11-15";
        var limit = kind == "minimum-limit" ? 1 : kind == "maximum-limit" ? 200 : 20;
        var filter = kind == "maximum-filter" ? new string('x', 256) : "";
        var sort = kind is "io" or "gpu-time" or "processes" or "last-active" ? kind : "cpu-time";
        var path = kind switch
        {
            "minimum-path" => "C:\\",
            "maximum-path" => "C:\\" + new string('x', 1021),
            "unnormalized-path" => @"c:\unused\..\usage.json",
            _ => null
        };
        var calls = new List<(string Path, CancellationToken Token)>();
        await using var owner = Owner();
        using var cancellation = new CancellationTokenSource();
        var archive = new UsageArchive(TimeZoneInfo.Utc);
        var tools = new DiagnosticTools(owner, readUsageArchive: (file, token) =>
        {
            calls.Add((file, token));
            return Task.FromResult(archive);
        });
        var result = await tools.GetUsageHistory(first, last, sort, filter, limit, path, cancellation.Token);
        var json = result.StructuredContent!.Value;
        var data = json.GetProperty("data");
        Assert.That(result.IsError, Is.Not.True);
        Assert.That(json.GetProperty("status").GetString(), Is.EqualTo("notObserved"));
        Assert.That(data.GetProperty("startDate").GetString(), Is.EqualTo(first));
        Assert.That(data.GetProperty("endDate").GetString(), Is.EqualTo(last));
        Assert.That(data.GetProperty("apps").GetArrayLength(), Is.Zero);
        Assert.That(data.GetProperty("observedSeconds").GetDouble(), Is.Zero);
        Assert.That(calls, Has.Count.EqualTo(1));
        Assert.That(calls[0].Path, Is.EqualTo(path ?? Path.Combine(Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData), "WinMon", "usage-history.json")));
        Assert.That(calls[0].Token, Is.EqualTo(cancellation.Token));
    }

    [Test]
    public async Task CancellationAfterArchiveReadPropagates()
    {
        await using var owner = Owner();
        using var cancellation = new CancellationTokenSource();
        var calls = 0;
        var tools = new DiagnosticTools(owner, readUsageArchive: (_, token) =>
        {
            Assert.That(token, Is.EqualTo(cancellation.Token));
            calls++;
            cancellation.Cancel();
            return Task.FromResult(new UsageArchive(TimeZoneInfo.Utc));
        });
        var exception = Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await tools.GetUsageHistory("2026-11-01", "2026-11-15", cancellationToken: cancellation.Token));
        Assert.That(exception!.CancellationToken, Is.EqualTo(cancellation.Token));
        Assert.That(calls, Is.EqualTo(1));
    }

    [TestCase("io")]
    [TestCase("invalid-data")]
    [TestCase("access")]
    [TestCase("json")]
    [TestCase("argument")]
    [TestCase("argument-derived")]
    [TestCase("zone-missing")]
    [TestCase("zone-invalid")]
    [TestCase("overflow")]
    [TestCase("unlisted")]
    public async Task ArchiveFailuresAreSanitizedAndUnlistedExceptionsPropagate(string kind)
    {
        const string secret = "private archive content and path";
        Exception failure = kind switch
        {
            "io" => new IOException(secret),
            "invalid-data" => new InvalidDataException(secret),
            "access" => new UnauthorizedAccessException(secret),
            "json" => new JsonException(secret),
            "argument" => new ArgumentException(secret),
            "argument-derived" => new ArgumentOutOfRangeException(secret),
            "zone-missing" => new TimeZoneNotFoundException(secret),
            "zone-invalid" => new InvalidTimeZoneException(secret),
            "overflow" => new OverflowException(secret),
            _ => new InvalidOperationException(secret)
        };
        await using var owner = Owner();
        var calls = 0;
        var tools = new DiagnosticTools(owner, readUsageArchive: (_, _) =>
        {
            calls++;
            return Task.FromException<UsageArchive>(failure);
        });
        if (kind == "unlisted")
        {
            var actual = Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await tools.GetUsageHistory("2026-11-01", "2026-11-15"));
            Assert.That(actual, Is.SameAs(failure));
        }
        else
        {
            var result = await tools.GetUsageHistory("2026-11-01", "2026-11-15");
            var json = result.StructuredContent!.Value;
            Assert.That(json.GetProperty("status").GetString(), Is.EqualTo("unavailable"));
            Assert.That(json.GetProperty("data").ValueKind, Is.EqualTo(JsonValueKind.Null));
            Assert.That(json.GetRawText(), Does.Not.Contain(secret));
        }
        Assert.That(calls, Is.EqualTo(1));
    }

    [TestCase("cpu-time", "gamma worker,beta worker,alpha worker")]
    [TestCase("io", "alpha worker,beta worker,gamma worker")]
    [TestCase("gpu-time", "beta worker,alpha worker,gamma worker")]
    [TestCase("processes", "alpha worker,beta worker,gamma worker")]
    [TestCase("last-active", "alpha worker,beta worker,gamma worker")]
    public async Task PublicSortsKeepExactOrderAndLimitedTotals(string sort, string expected)
    {
        var archive = CreateArchive(3);
        await using var owner = Owner();
        var tools = new DiagnosticTools(owner, readUsageArchive: (_, _) => Task.FromResult(archive));
        var json = (await tools.GetUsageHistory("2026-11-15", "2026-11-15", sort: sort)).StructuredContent!.Value;
        var data = json.GetProperty("data");
        Assert.That(data.GetProperty("apps").EnumerateArray().Select(app => app.GetProperty("name").GetString()), Is.EqualTo(expected.Split(',')));
        Assert.That(json.GetProperty("returnedCount").GetInt32(), Is.EqualTo(3));
        Assert.That(json.GetProperty("truncated").GetBoolean(), Is.False);
        var limited = (await tools.GetUsageHistory("2026-11-15", "2026-11-15", sort: sort, limit: 1)).StructuredContent!.Value;
        var limitedData = limited.GetProperty("data");
        Assert.That(limited.GetProperty("returnedCount").GetInt32(), Is.EqualTo(1));
        Assert.That(limited.GetProperty("truncated").GetBoolean(), Is.True);
        Assert.That(limitedData.GetProperty("apps")[0].GetProperty("name").GetString(), Is.EqualTo(expected.Split(',')[0]));
        foreach (var property in new[] { "observedSeconds", "machineBusyTicks", "notAttributedTicks", "retainedStartDate", "retainedEndDate", "sinceUtc", "untilUtc" })
            Assert.That(limitedData.GetProperty(property).GetRawText(), Is.EqualTo(data.GetProperty(property).GetRawText()), property);
        Assert.That(data.GetProperty("observedSeconds").GetDouble(), Is.EqualTo(1));
        Assert.That(data.GetProperty("machineBusyTicks").GetDouble(), Is.EqualTo(10_000_000));
        Assert.That(data.GetProperty("notAttributedTicks").GetDouble(), Is.EqualTo(4_000_000));
        Assert.That(data.GetProperty("retainedStartDate").GetString(), Is.EqualTo("2026-11-15"));
        Assert.That(data.GetProperty("retainedEndDate").GetString(), Is.EqualTo("2026-11-15"));
    }

    [TestCase("name", 1)]
    [TestCase("app-name", 1)]
    [TestCase("private-path", 0)]
    [TestCase("private-key", 0)]
    [TestCase("whitespace", 0)]
    [TestCase("empty", 3)]
    public async Task FilteringUsesPublicNamesWithoutTrimmingOrExposingPaths(string kind, int count)
    {
        var filter = kind switch { "name" => "ALPHA WORKER", "app-name" => "ALPHA PRODUCT",
            "private-path" => "private-secret", "private-key" => @"C:\private-secret", "whitespace" => " alpha worker ", _ => "" };
        var archive = CreateArchive(3);
        await using var owner = Owner();
        var tools = new DiagnosticTools(owner, readUsageArchive: (_, _) => Task.FromResult(archive));
        var json = (await tools.GetUsageHistory("2026-11-15", "2026-11-15", filter: filter)).StructuredContent!.Value;
        var data = json.GetProperty("data");
        Assert.That(data.GetProperty("apps").GetArrayLength(), Is.EqualTo(count));
        Assert.That(json.GetProperty("returnedCount").GetInt32(), Is.EqualTo(count));
        Assert.That(data.GetProperty("observedSeconds").GetDouble(), Is.EqualTo(1));
        Assert.That(json.GetProperty("status").GetString(), Is.EqualTo("ok"));
        Assert.That(json.GetRawText(), Does.Not.Contain("private-secret").And.Not.Contain("imagePath").And.Not.Contain("publisher"));
        if (count == 1) Assert.That(data.GetProperty("apps")[0].GetProperty("name").GetString(), Is.EqualTo("alpha worker"));
    }

    [TestCase("name", 512, false)]
    [TestCase("name", 513, true)]
    [TestCase("app", 512, false)]
    [TestCase("app", 513, true)]
    [TestCase("both", 513, true)]
    public async Task MetadataBoundsKeepExactTextAndTruncationFlag(string field, int length, bool truncated)
    {
        var name = field is "name" or "both" ? new string('n', length) : "worker";
        var app = field is "app" or "both" ? new string('a', length) : "product";
        var archive = new UsageArchive(TimeZoneInfo.Utc);
        archive.Add(new MonitorSample(new DateTimeOffset(2026, 11, 15, 12, 0, 0, TimeSpan.Zero), 10,
            [new ProcessUsage(1, 100, name, 10) { AppName = app }], 0)
        { IntervalSeconds = 1, IntervalCpuTicks = 10_000_000 });
        await using var owner = Owner();
        var tools = new DiagnosticTools(owner, readUsageArchive: (_, _) => Task.FromResult(archive));
        var entry = (await tools.GetUsageHistory("2026-11-15", "2026-11-15")).StructuredContent!.Value.GetProperty("data").GetProperty("apps")[0];
        Assert.That(entry.GetProperty("name").GetString(), Is.EqualTo(name[..Math.Min(name.Length, 512)]));
        Assert.That(entry.GetProperty("appName").GetString(), Is.EqualTo(app[..Math.Min(app.Length, 512)]));
        Assert.That(entry.GetProperty("metadataTruncated").GetBoolean(), Is.EqualTo(truncated));
        Assert.That(entry.GetProperty("dailyLifetimeObservations").GetInt32(), Is.EqualTo(1));
        Assert.That(entry.GetProperty("cpuTicks").GetInt64(), Is.EqualTo(1_000_000));
    }

    private static UsageArchive CreateArchive(int count)
    {
        var processes = Enumerable.Range(0, count).Select(i => new ProcessUsage(i + 1, 100 + i,
            i switch { 0 => "alpha worker", 1 => "beta worker", 2 => "gamma worker", _ => "worker " + i },
            (i % 3 + 1) * 10)
        {
            AppName = i switch { 0 => "Alpha product", 1 => "Beta product", _ => "" },
            ImagePath = @"C:\private-secret\worker-" + i + ".exe", IoBytesPerSecond = (3 - i % 3) * 10,
            GpuPercent = i % 3 == 2 ? null : (i % 3 + 1) * 10
        }).ToArray();
        var archive = new UsageArchive(TimeZoneInfo.Utc);
        archive.Add(new MonitorSample(new DateTimeOffset(2026, 11, 15, 12, 0, 0, TimeSpan.Zero), 100, processes, 0)
        { IntervalSeconds = 1, IntervalCpuTicks = 10_000_000 });
        return archive;
    }

    private static SamplingOwner Owner() => new(() => throw new AssertionException("Archive reads must not sample."), TimeProvider.System);

    internal static (Func<object> Invoke, IAsyncDisposable Owner) CreateBenchmarkRequest(string scenario)
    {
        var archive = CreateArchive(scenario == "many" ? 500 : scenario == "three" ? 3 : 0);
        var owner = new SamplingOwner(() => throw new AssertionException("Unexpected sampling."), new FixedClock());
        var tools = new DiagnosticTools(owner, readUsageArchive: (_, _) => Task.FromResult(archive));
        return (() => tools.GetUsageHistory("2026-11-15", "2026-11-15", limit: scenario == "invalid" ? 0 : 200,
            filePath: @"C:\fixture\usage.json").GetAwaiter().GetResult(), owner);
    }

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 11, 22, 0, 0, 0, TimeSpan.Zero);
    }
}
