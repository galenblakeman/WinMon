using System.Collections.Concurrent;
using ModelContextProtocol.Client;
using NUnit.Framework;

namespace WinMon.Tests;

[TestFixture, Category("Integration")]
public sealed class StdioTests
{
    [Test]
    public async Task RealServerInitializesDiscoversOnlyReadOnlyToolsAndInvokesSnapshotOverStdio()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        var errors = new ConcurrentQueue<string>();
        var executable = Environment.GetEnvironmentVariable("WINMON_MCP_TEST_EXE");
        var assembly = typeof(WinMon.Mcp.SamplingOwner).Assembly.Location;
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
            "get_system_snapshot", "list_processes", "get_process_details", "sample_performance", "get_network_connections"
        }));
        Assert.That(tools.All(tool => tool.ProtocolTool.Annotations?.ReadOnlyHint == true), Is.True);
        Assert.That(tools.All(tool => tool.ProtocolTool.OutputSchema is not null), Is.True);
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
    }
}
