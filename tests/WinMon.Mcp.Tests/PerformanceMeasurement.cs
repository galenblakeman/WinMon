using System.Diagnostics;
using System.Text.Json;
using ModelContextProtocol.Client;
using NUnit.Framework;

namespace WinMon.Tests;

[TestFixture, Category("Performance")]
public sealed class PerformanceMeasurement
{
    [Test, Explicit("Measures a published server for about four minutes; requires dotnet-counters, dotnet-gcdump and WINMON_MCP_TEST_EXE.")]
    public async Task MeasurePublishedServer()
    {
        var executable = Environment.GetEnvironmentVariable("WINMON_MCP_TEST_EXE")
            ?? throw new InvalidOperationException("Set WINMON_MCP_TEST_EXE to the published server.");
        var directory = Environment.GetEnvironmentVariable("WINMON_MCP_PERF_OUTPUT")
            ?? Path.Combine(TestContext.CurrentContext.WorkDirectory, "mcp-performance");
        Directory.CreateDirectory(directory);
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(6));
        var before = Process.GetProcessesByName("WinMon.Mcp").Select(p => { using (p) return p.Id; }).ToHashSet();
        await using var client = await McpClient.CreateAsync(new StdioClientTransport(new StdioClientTransportOptions
        {
            Command = executable, WorkingDirectory = Path.GetTempPath(),
            StandardErrorLines = _ => { }, ShutdownTimeout = TimeSpan.FromSeconds(5)
        }), cancellationToken: timeout.Token);
        using var server = Process.GetProcessesByName("WinMon.Mcp").Single(p => !before.Contains(p.Id));
        var counterStart = new ProcessStartInfo("dotnet-counters")
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        foreach (var argument in new[] { "collect", "-p", server.Id.ToString(), "--counters", "EventCounters\\System.Runtime",
                     "--format", "csv", "--duration", "00:00:05:30", "-o", Path.Combine(directory, "counters.csv") })
            counterStart.ArgumentList.Add(argument);
        using var counters = Process.Start(counterStart)!;
        var counterOut = counters.StandardOutput.ReadToEndAsync(timeout.Token);
        var counterError = counters.StandardError.ReadToEndAsync(timeout.Token);
        var measurements = new List<object>();
        var timeline = new List<object>();
        var watch = Stopwatch.StartNew();
        string phase = "warmup";
        using var sampling = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
        var memoryTask = Task.Run(async () =>
        {
            while (!sampling.IsCancellationRequested)
            {
                server.Refresh();
                timeline.Add(new { Seconds = watch.Elapsed.TotalSeconds, Phase = phase,
                    server.PrivateMemorySize64, server.WorkingSet64, CpuSeconds = server.TotalProcessorTime.TotalSeconds });
                await Task.Delay(1000, sampling.Token);
            }
        }, sampling.Token);
        try
        {
            await Task.Delay(10000, timeout.Token);
            await Measure("idle", async () => await Task.Delay(20000, timeout.Token));
            await Measure("snapshots", async () =>
            {
                for (var i = 0; i < 40; i++)
                {
                    var elapsed = Stopwatch.StartNew();
                    var result = await client.CallToolAsync("get_system_snapshot", cancellationToken: timeout.Token);
                    Assert.That(result.IsError, Is.Not.True);
                    measurements.Add(new { Name = "snapshotLatency", Milliseconds = elapsed.Elapsed.TotalMilliseconds });
                    await Task.Delay(500, timeout.Token);
                }
            });
            await Measure("fourConcurrentSamples", async () =>
            {
                var calls = Enumerable.Range(0, 4).Select(_ => client.CallToolAsync("sample_performance",
                    new Dictionary<string, object?> { ["durationSeconds"] = 60 }, cancellationToken: timeout.Token).AsTask()).ToArray();
                var results = await Task.WhenAll(calls);
                foreach (var result in results)
                {
                    Assert.That(result.IsError, Is.Not.True);
                    var json = result.StructuredContent!.Value;
                    Assert.That(json.GetProperty("status").GetString(), Is.EqualTo("ok"));
                    measurements.Add(new { Name = "sampleResult", Bytes = JsonSerializer.SerializeToUtf8Bytes(result).Length,
                        Data = json });
                }
            });
            await Measure("steadyRetention", async () => await Task.Delay(60000, timeout.Token));
            await CollectHeap("retained-first");
            await Measure("steadyAfterGc", async () => await Task.Delay(60000, timeout.Token));
            await CollectHeap("retained-second");
        }
        finally
        {
            sampling.Cancel();
            try { await memoryTask; } catch (OperationCanceledException) { }
            // Counter collection ends on process exit; dispose the MCP client before awaiting it.
            await client.DisposeAsync();
            try { await counters.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException) { if (!counters.HasExited) counters.Kill(true); }
            var diagnostics = await counterError;
            await counterOut;
            File.WriteAllText(Path.Combine(directory, "measurements.json"), JsonSerializer.Serialize(new
            {
                Executable = executable, TimestampUtc = DateTimeOffset.UtcNow, Measurements = measurements,
                Timeline = timeline, CountersExitCode = counters.ExitCode, CounterDiagnostics = diagnostics
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
        Assert.That(counters.ExitCode, Is.Zero);

        async Task CollectHeap(string name)
        {
            phase = name;
            var path = Path.Combine(directory, name + ".gcdump");
            await RunGcDump(["collect", "-p", server.Id.ToString(), "-t", "30", "-o", path]);
            var report = await RunGcDump(["report", path]);
            File.WriteAllText(Path.Combine(directory, name + ".txt"), report);
            server.Refresh();
            measurements.Add(new { Name = name, server.PrivateMemorySize64, server.WorkingSet64 });
        }

        async Task<string> RunGcDump(string[] arguments)
        {
            var start = new ProcessStartInfo("dotnet-gcdump")
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true
            };
            foreach (var argument in arguments) start.ArgumentList.Add(argument);
            using var diagnostic = Process.Start(start)!;
            var stdout = diagnostic.StandardOutput.ReadToEndAsync(timeout.Token);
            var stderr = diagnostic.StandardError.ReadToEndAsync(timeout.Token);
            try { await diagnostic.WaitForExitAsync(timeout.Token); }
            catch { if (!diagnostic.HasExited) diagnostic.Kill(true); throw; }
            var output = await stdout;
            Assert.That(diagnostic.ExitCode, Is.Zero, await stderr);
            return output;
        }

        async Task Measure(string name, Func<Task> action)
        {
            phase = name;
            server.Refresh();
            var cpu = server.TotalProcessorTime;
            var elapsed = Stopwatch.StartNew();
            await action();
            server.Refresh();
            measurements.Add(new { Name = name, DurationSeconds = elapsed.Elapsed.TotalSeconds,
                CpuMilliseconds = (server.TotalProcessorTime - cpu).TotalMilliseconds,
                server.PrivateMemorySize64, server.WorkingSet64 });
        }
    }
}
