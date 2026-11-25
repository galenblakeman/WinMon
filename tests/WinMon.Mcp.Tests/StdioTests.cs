using System.Collections.Concurrent;
using ModelContextProtocol.Client;
using NUnit.Framework;

namespace WinMon.Mcp.Tests;

[TestFixture, Category("Integration")]
public sealed class StdioTests
{
    [Test]
    public async Task RealServerInitializesDiscoversOnlyReadOnlyToolsAndInvokesSnapshotOverStdio()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        var errors = new ConcurrentQueue<string>();
        var executable = Environment.GetEnvironmentVariable("WINMON_MCP_TEST_EXE");
        var assembly = typeof(SamplingOwner).Assembly.Location;
        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = "WinMon test",
            Command = executable ?? "dotnet",
            Arguments = executable is null ? ["exec", "--runtimeconfig",
                Path.ChangeExtension(typeof(StdioTests).Assembly.Location, ".runtimeconfig.json"), "--depsfile",
                Path.ChangeExtension(typeof(StdioTests).Assembly.Location, ".deps.json"), assembly] : [],
            WorkingDirectory = Path.GetTempPath(),
            ShutdownTimeout = TimeSpan.FromSeconds(5),
            StandardErrorLines = line => errors.Enqueue(line)
        });
        await using var client = await McpClient.CreateAsync(transport, cancellationToken: timeout.Token);
        var tools = await client.ListToolsAsync(cancellationToken: timeout.Token);
        Assert.That(tools.Select(tool => tool.Name), Is.EquivalentTo(new[]
        {
            "get_system_snapshot", "list_processes", "get_process_details", "sample_performance", "get_network_connections",
            "get_process_network_traffic", "get_hardware_info", "list_crashes", "list_services", "get_usage", "get_performance_history",
            "list_capture_reports", "read_capture_report", "get_usage_history"
        }));
        Assert.That(tools.All(tool => tool.ProtocolTool.Annotations?.ReadOnlyHint == true), Is.True);
        Assert.That(tools.All(tool => tool.ProtocolTool.OutputSchema is not null), Is.True);
        foreach (var name in new[] { "list_services", "get_usage", "get_performance_history" })
        {
            var activity = await client.CallToolAsync(name, cancellationToken: timeout.Token);
            Assert.That(activity.IsError, Is.Not.True, name);
            Assert.That(activity.StructuredContent!.Value.GetProperty("schemaVersion").GetInt32(), Is.EqualTo(1), name);
        }
        var result = await client.CallToolAsync("get_system_snapshot", cancellationToken: timeout.Token);
        var json = result.StructuredContent!.Value;
        Assert.Multiple(() =>
        {
            Assert.That(json.GetProperty("schemaVersion").GetInt32(), Is.EqualTo(1));
            Assert.That(json.GetProperty("freshness").GetString(), Is.AnyOf("current", "stale", "unavailable"));
            Assert.That(json.TryGetProperty("timestampUtc", out _), Is.True);
            Assert.That(json.TryGetProperty("collectionError", out _), Is.True);
            Assert.That(result.IsError, Is.Not.True);
        });
        var invalid = await client.CallToolAsync("list_processes", new Dictionary<string, object?> { ["limit"] = 0 }, cancellationToken: timeout.Token);
        Assert.That(invalid.IsError, Is.True);
        var crashes = await client.CallToolAsync("list_crashes", new Dictionary<string, object?>
        {
            ["days"] = 1, ["limit"] = 1
        }, cancellationToken: timeout.Token);
        Assert.Multiple(() =>
        {
            Assert.That(crashes.IsError, Is.Not.True);
            Assert.That(crashes.StructuredContent!.Value.GetProperty("schemaVersion").GetInt32(), Is.EqualTo(1));
            Assert.That(crashes.StructuredContent.Value.GetProperty("data").GetProperty("coverage").TryGetProperty("oldestRetainedApplicationEvent", out _), Is.True);
            Assert.That(crashes.StructuredContent.Value.GetRawText(), Does.Not.Contain("appPath").And.Not.Contain("modulePath").And.Not.Contain("message"));
        });
        var archive = await client.CallToolAsync("get_usage_history", new Dictionary<string, object?>
        {
            ["startDate"] = "2000-01-01", ["endDate"] = "2000-01-02"
        }, cancellationToken: timeout.Token);
        Assert.That(archive.IsError, Is.Not.True);
        Assert.That(archive.StructuredContent!.Value.GetProperty("freshness").GetString(), Is.EqualTo("historical"));
        var lifetimeHistory = await client.CallToolAsync("get_performance_history", new Dictionary<string, object?>
        {
            ["pid"] = int.MaxValue, ["expectedStartToken"] = "1", ["expectedDomain"] = "WindowsFileTime"
        }, cancellationToken: timeout.Token);
        Assert.That(lifetimeHistory.IsError, Is.Not.True);
        Assert.That(lifetimeHistory.StructuredContent!.Value.GetProperty("status").GetString(), Is.EqualTo("notObserved"));
        var hardware = await client.CallToolAsync("get_hardware_info", cancellationToken: timeout.Token);
        Assert.Multiple(() =>
        {
            Assert.That(hardware.IsError, Is.Not.True);
            Assert.That(hardware.StructuredContent!.Value.GetProperty("schemaVersion").GetInt32(), Is.EqualTo(1));
            Assert.That(hardware.StructuredContent.Value.GetProperty("status").GetString(), Is.AnyOf("ok", "partial", "unavailable"));
        });
        var traffic = await client.CallToolAsync("get_process_network_traffic",
            new Dictionary<string, object?> { ["limit"] = 1 }, cancellationToken: timeout.Token);
        Assert.Multiple(() =>
        {
            Assert.That(traffic.IsError, Is.Not.True);
            Assert.That(traffic.StructuredContent!.Value.GetProperty("returnedCount").GetInt32(), Is.LessThanOrEqualTo(1));
            Assert.That(traffic.StructuredContent.Value.GetRawText(), Does.Not.Contain("localEndpoint").And.Not.Contain("imagePath"));
        });
    }
}
