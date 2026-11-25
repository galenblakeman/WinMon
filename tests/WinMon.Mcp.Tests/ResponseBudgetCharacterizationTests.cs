using System.Text.Json;
using ModelContextProtocol.Protocol;
using NUnit.Framework;
using WinMon.Mcp;
using WinMon.Monitoring;

namespace WinMon.Tests;

[TestFixture]
public sealed class ResponseBudgetCharacterizationTests
{
    private static readonly DateTimeOffset Timestamp = new(2026, 11, 22, 1, 2, 3, TimeSpan.Zero);
    private static readonly string[] Shapes = ["usage", "archive", "history", "performance", "connections", "traffic", "array"];

    private static DiagnosticResponse Envelope(object? data, bool truncated = false) =>
        new(7, Timestamp, "ok", "stale", 0.5, true, 3, "retained error", data, truncated, 999);

    private static IEnumerable<TestCaseData> SmallCases()
    {
        foreach (var shape in Shapes) foreach (var count in new[] { 0, 3 }) foreach (var truncated in new[] { false, true })
            yield return new TestCaseData(shape, count, truncated).SetName($"BudgetSmall_{shape}_{count}_{truncated}");
    }

    [TestCaseSource(nameof(SmallCases))]
    public void UnderBudgetPayloadPreservesCallerEnvelope(string shape, int count, bool truncated)
    {
        var response = Envelope(Data(shape, count, 12), truncated);
        var before = JsonSerializer.Serialize(response, Responses.Json);
        var result = Responses.Result(response);
        Assert.That(result.StructuredContent!.Value.GetRawText(), Is.EqualTo(before));
        Assert.That(result.IsError, Is.False);
        AssertWire(result);
        Assert.That(JsonSerializer.Serialize(response, Responses.Json), Is.EqualTo(before));
    }

    private static IEnumerable<TestCaseData> LargeCases()
    {
        foreach (var shape in Shapes) foreach (var count in new[] { 8, 9 })
            yield return new TestCaseData(shape, count).SetName($"BudgetHalf_{shape}_{count}");
    }

    [TestCaseSource(nameof(LargeCases))]
    public void OversizedPayloadHalvesExactlyAndKeepsMetadata(string shape, int count) => AssertTrim(shape, count, 20_000, count / 2);

    [TestCase("usage")]
    [TestCase("archive")]
    [TestCase("history")]
    [TestCase("performance")]
    [TestCase("connections")]
    [TestCase("traffic")]
    [TestCase("array")]
    public void RepeatedHalvingPreservesPrefixOrNewestHistory(string shape) => AssertTrim(shape, 65, 12_000, 8);

    private static void AssertTrim(string shape, int count, int textLength, int expectedCount)
    {
        var response = Envelope(Data(shape, count, textLength));
        var before = JsonSerializer.SerializeToElement(response, Responses.Json);
        Assert.That(Size(Wire(response)), Is.GreaterThan(Responses.MaximumBytes));
        var result = Responses.Result(response);
        var after = result.StructuredContent!.Value;
        Assert.That(after.GetProperty("returnedCount").GetInt32(), Is.EqualTo(expectedCount));
        Assert.That(after.GetProperty("truncated").GetBoolean(), Is.True);
        foreach (var property in before.EnumerateObject().Where(property => property.Name is not ("data" or "returnedCount" or "truncated")))
            Assert.That(after.GetProperty(property.Name).GetRawText(), Is.EqualTo(property.Value.GetRawText()), property.Name);
        var source = Items(shape, before.GetProperty("data")).EnumerateArray().Select(item => item.GetRawText()).ToArray();
        var expected = shape == "history" ? source.TakeLast(expectedCount) : source.Take(expectedCount);
        Assert.That(Items(shape, after.GetProperty("data")).EnumerateArray().Select(item => item.GetRawText()), Is.EqualTo(expected));
        if (shape != "array")
            foreach (var property in before.GetProperty("data").EnumerateObject().Where(property => property.Name != Collection(shape)))
                Assert.That(after.GetProperty("data").GetProperty(property.Name).GetRawText(), Is.EqualTo(property.Value.GetRawText()), property.Name);
        Assert.That(JsonSerializer.SerializeToElement(response, Responses.Json).GetRawText(), Is.EqualTo(before.GetRawText()));
        Assert.That(result.IsError, Is.False);
        AssertWire(result);
    }

    [TestCase(-1)]
    [TestCase(0)]
    [TestCase(1)]
    public void SerializedProtocolBudgetIncludesItsExactBoundary(int delta)
    {
        var target = Responses.MaximumBytes + delta;
        DiagnosticResponse? response = null;
        foreach (var status in new[] { "ok", "busy" })
        {
            var empty = Envelope(new[] { "" }) with { Status = status };
            var padding = target - Size(Wire(empty, status == "busy"));
            if (padding >= 0 && padding % 2 == 0)
            {
                response = empty with { Data = new[] { new string('x', padding / 2) } };
                break;
            }
        }
        Assert.That(response, Is.Not.Null, "Both protocol parity cases must be considered.");
        Assert.That(Size(Wire(response!, response!.Status == "busy")), Is.EqualTo(target));
        var result = Responses.Result(response);
        var json = result.StructuredContent!.Value;
        Assert.That(json.GetProperty("returnedCount").GetInt32(), Is.EqualTo(delta <= 0 ? 999 : 0));
        Assert.That(json.GetProperty("truncated").GetBoolean(), Is.EqualTo(delta > 0));
        Assert.That(json.GetProperty("data").GetArrayLength(), Is.EqualTo(delta <= 0 ? 1 : 0));
        Assert.That(result.IsError, Is.EqualTo(response.Status == "busy"));
        AssertWire(result);
    }

    [TestCase("ok", false)]
    [TestCase("invalidInput", true)]
    [TestCase("busy", true)]
    [TestCase("identityMismatch", true)]
    [TestCase("unavailable", false)]
    [TestCase("notObserved", false)]
    [TestCase("BUSY", false)]
    [TestCase(null, false)]
    public void ErrorFlagRecognizesOnlyExistingStatusCodes(string? status, bool isError)
    {
        var response = Envelope(null) with { Status = status! };
        var result = Responses.Result(response);
        Assert.That(result.IsError, Is.EqualTo(isError));
        Assert.That(result.StructuredContent!.Value.GetRawText(), Is.EqualTo(JsonSerializer.Serialize(response, Responses.Json)));
        AssertWire(result);
    }

    [TestCase("usage")]
    [TestCase("archive")]
    [TestCase("history")]
    [TestCase("performance")]
    [TestCase("connections")]
    [TestCase("traffic")]
    public void NullCollectionsRetainLazyFailureBehavior(string shape)
    {
        object data = Data(shape, 0, 0) switch
        {
            UsageResult value => value with { Apps = null! },
            ArchivedUsageResult value => value with { Apps = null! },
            HistoryResult value => value with { Points = null! },
            PerformanceResult value => value with { Trend = null! },
            ConnectionResult value => value with { Connections = null! },
            TrafficResult value => value with { Processes = null! },
            _ => throw new AssertionException("Unexpected shape.")
        };
        var response = Envelope(data);
        var result = Responses.Result(response);
        Assert.That(result.StructuredContent!.Value.GetProperty("data").GetProperty(Collection(shape)).ValueKind, Is.EqualTo(JsonValueKind.Null));
        Assert.That(result.StructuredContent.Value.GetProperty("returnedCount").GetInt32(), Is.EqualTo(999));
        Assert.Throws<NullReferenceException>(() => Responses.Result(response with { CollectionError = new string('e', 140_000) }));
        AssertWire(result);
    }

    [TestCase("null-response")]
    [TestCase("cycle")]
    [TestCase("nonfinite")]
    [TestCase("delegate")]
    public void SerializationFailuresKeepTheirExceptionTypes(string kind)
    {
        var cycle = new object?[1]; cycle[0] = cycle;
        var response = kind == "null-response" ? null! : Envelope(kind switch
        {
            "cycle" => cycle,
            "nonfinite" => (object)double.NaN,
            _ => (Action)(() => { })
        });
        if (kind == "null-response") Assert.Throws<NullReferenceException>(() => Responses.Result(response));
        else if (kind == "cycle") Assert.Throws<JsonException>(() => Responses.Result(response));
        else if (kind == "nonfinite") Assert.Throws<ArgumentException>(() => Responses.Result(response));
        else Assert.Throws<NotSupportedException>(() => Responses.Result(response));
    }

    [Test]
    public void UnsupportedOversizedPayloadIsDiscardedWithoutChangingEnvelope()
    {
        var response = Envelope(new { privatePayload = new string('x', 140_000) });
        var result = Responses.Result(response);
        var json = result.StructuredContent!.Value;
        Assert.That(json.GetProperty("data").ValueKind, Is.EqualTo(JsonValueKind.Null));
        Assert.That(json.GetProperty("returnedCount").GetInt32(), Is.Zero);
        Assert.That(json.GetProperty("truncated").GetBoolean(), Is.True);
        Assert.That(json.GetProperty("collectionError").GetString(), Is.EqualTo("retained error"));
        Assert.That(json.GetProperty("status").GetString(), Is.EqualTo("ok"));
        AssertWire(result);
    }

    [Test]
    public void ValueArraysKeepBoxedPrefixValuesAndTheOriginalArray()
    {
        var values = Enumerable.Range(0, 50_000).ToArray();
        var result = Responses.Result(Envelope(values));
        var json = result.StructuredContent!.Value;
        Assert.That(json.GetProperty("returnedCount").GetInt32(), Is.EqualTo(12_500));
        Assert.That(json.GetProperty("data").EnumerateArray().Select(value => value.GetInt32()), Is.EqualTo(Enumerable.Range(0, 12_500)));
        Assert.That(values, Is.EqualTo(Enumerable.Range(0, 50_000)));
        AssertWire(result);
    }

    [Test]
    public void MultidimensionalArraysKeepTheirSerializationFailure()
    {
        Assert.Throws<NotSupportedException>(() => Responses.Result(Envelope(new int[2, 2])));
    }

    [Test]
    public void EscapedPayloadIsBoundedBySerializedBytes()
    {
        var text = string.Concat(Enumerable.Repeat("\u0000\u000c\"\\汉😀", 4_000));
        var response = Envelope(Enumerable.Range(0, 9).Select(i => new Row(i, text)).ToArray());
        Assert.That(Size(Wire(response)), Is.GreaterThan(Responses.MaximumBytes));
        var result = Responses.Result(response);
        var json = result.StructuredContent!.Value;
        Assert.That(json.GetProperty("truncated").GetBoolean(), Is.True);
        Assert.That(json.GetProperty("returnedCount").GetInt32(), Is.EqualTo(json.GetProperty("data").GetArrayLength()));
        Assert.That(json.GetProperty("data").GetArrayLength(), Is.LessThan(9));
        AssertWire(result);
    }

    private sealed record Row(int Index, string Text);
    private static int Size(CallToolResult result) => JsonSerializer.SerializeToUtf8Bytes(result, Responses.Json).Length;
    private static CallToolResult Wire(DiagnosticResponse response, bool isError = false)
    {
        var element = JsonSerializer.SerializeToElement(response, Responses.Json);
        return new CallToolResult { StructuredContent = element, Content = [new TextContentBlock { Text = element.GetRawText() }], IsError = isError };
    }
    private static void AssertWire(CallToolResult result)
    {
        Assert.That(Size(result), Is.LessThanOrEqualTo(Responses.MaximumBytes));
        Assert.That(result.Content, Has.Count.EqualTo(1));
        Assert.That(result.Content[0], Is.TypeOf<TextContentBlock>());
        Assert.That(((TextContentBlock)result.Content[0]).Text, Is.EqualTo(result.StructuredContent!.Value.GetRawText()));
    }
    private static string Collection(string shape) => shape switch
    {
        "usage" or "archive" => "apps", "history" => "points", "performance" => "trend", "connections" => "connections", "traffic" => "processes", _ => ""
    };
    private static JsonElement Items(string shape, JsonElement data) => shape == "array" ? data : data.GetProperty(Collection(shape));
    private static object Data(string shape, int count, int length)
    {
        var text = new string('x', length);
        var date = new DateOnly(2026, 11, 22);
        var metric = new MetricSummary(1, 2, 3, 4, 5, 0.5, "unit");
        var summary = new Dictionary<string, MetricSummary> { ["preserved"] = metric };
        var targets = new PerformanceTargets("disk", "network", "gpu", true);
        return shape switch
        {
            "usage" => new UsageResult(Timestamp, 123, Enumerable.Range(0, count).Select(i => new UsageEntry($"{i}:{text}", "app", true, i, 2, 3, 4, 5, Timestamp)).ToArray()),
            "archive" => new ArchivedUsageResult("UTC", date, date, Timestamp, Timestamp.AddDays(1), date, date, 23, 123, 45, false,
                Enumerable.Range(0, count).Select(i => new ArchivedUsageEntry($"{i}:{text}", "app", true, i, 2, 3, 4, Timestamp)).ToArray()),
            "history" => new HistoryResult(Timestamp, Timestamp.AddSeconds(count), Timestamp.AddHours(-1), Timestamp.AddHours(1),
                Enumerable.Range(0, count).Select(i => new HistoryPoint(Timestamp.AddSeconds(i), 0.5, i % 2 == 0, "frame error",
                    SystemMetrics.From(new MonitorSample(Timestamp, i, [], 0)) with { DiskName = text }, [])).ToArray()),
            "performance" => new PerformanceResult(Timestamp, 60, 59, 1, new ProcessIdentity(7, "123", "WindowsFileTime"), targets, targets, true, summary,
                Enumerable.Range(0, count).Select(i => new PerformanceBucket(i, 1, new Dictionary<string, MetricSummary> { ["value"] = metric with { Unit = text } })).ToArray(), ["retained error"], "limitation"),
            "connections" => new ConnectionResult(null, null, true, true, true,
                Enumerable.Range(0, count).Select(i => new ConnectionObservation(i, null, text, true, "Tcp", "Listen", "127.0.0.1", i, null, null)).ToArray()),
            "traffic" => new TrafficResult("available", ProcessNetworkQuality.None, Guid.Empty, "scope",
                Enumerable.Range(0, count).Select(i => new TrafficProcess(new ProcessIdentity(i, "123", "WindowsFileTime"), text, true, i, 2, 3, 4)).ToArray()),
            "array" => Enumerable.Range(0, count).Select(i => new Row(i, text)).ToArray(),
            _ => throw new ArgumentException("Unknown shape.", nameof(shape))
        };
    }

    internal static Func<object> CreateBenchmarkRequest(string scenario)
    {
        var data = scenario == "small" ? Data("usage", 3, 12) : scenario == "unsupported"
            ? new { payload = new string('x', 140_000) } : Data(scenario, 65, 12_000);
        var response = Envelope(data);
        return () => Responses.Result(response);
    }

    internal static object ProbeIrreducible(string readinessPath)
    {
        var response = Envelope(null) with { Status = new string('x', 140_000) };
        var fixedPoint = response with { Data = null, ReturnedCount = 0, Truncated = true };
        File.WriteAllText(readinessPath, JsonSerializer.Serialize(new { FixedPointBytes = Size(Wire(fixedPoint)), Limit = Responses.MaximumBytes }));
        return Responses.Result(response);
    }
}
