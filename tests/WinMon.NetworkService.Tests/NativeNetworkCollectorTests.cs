using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text.Json;
using Microsoft.Diagnostics.Tracing.Session;
using NUnit.Framework;
using WinMon.Monitoring;
using WinMon.NetworkService;

namespace WinMon.Tests;

[TestFixture]
public sealed class NativeNetworkCollectorTests
{
    [Test]
    [Category("Integration")]
    [Category("Elevated")]
    [Explicit("Runs a bounded elevated ETW experiment on an approved Windows test machine")]
    public async Task Collector_AccountsForControlledWorkerAndStopsItsSession()
    {
        using var identity = WindowsIdentity.GetCurrent();
        Assert.That(new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator), Is.True,
            "Run this explicit test elevated.");
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "WinMon.sln"))) root = root.Parent;
        Assert.That(root, Is.Not.Null);
        var probe = Path.Combine(root!.FullName,
            "artifacts/network-probe/bin/WinMon.NetworkProbe/release/WinMon.NetworkProbe.exe");
        Assert.That(File.Exists(probe), Is.True, "Build the network prototype into artifacts/network-probe first.");
        var start = new ProcessStartInfo(probe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true
        };
        start.ArgumentList.Add("--worker");
        using var worker = Process.Start(start)!;
        worker.StandardInput.AutoFlush = true;
        using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var sid = identity.User!.Value;
        await using var collector = new EtwNetworkCollector([sid]);
        var results = new List<object>();
        var mark = Stopwatch.GetTimestamp();
        using var current = Process.GetCurrentProcess();
        var cpuBefore = current.TotalProcessorTime;
        long expectedSent = 0;
        long expectedReceived = 0;
        double idleWithoutTraceCpu = 0;
        double idleWithTraceCpu = 0;
        try
        {
            Assert.That(await worker.StandardOutput.ReadLineAsync(limit.Token), Is.EqualTo("ready"));
            var idleCpu = current.TotalProcessorTime;
            await Task.Delay(TimeSpan.FromSeconds(2), limit.Token);
            idleWithoutTraceCpu = (current.TotalProcessorTime - idleCpu).TotalMilliseconds;
            await collector.StartAsync(limit.Token);
            await Task.Delay(TimeSpan.FromSeconds(1), limit.Token);
            idleCpu = current.TotalProcessorTime;
            await Task.Delay(TimeSpan.FromSeconds(2), limit.Token);
            idleWithTraceCpu = (current.TotalProcessorTime - idleCpu).TotalMilliseconds;
            foreach (var scenario in new[]
                     {
                         "tcp4-existing", "tcp6-existing", "tcp4-short", "tcp6-short", "udp4", "udp6",
                         "tcp4-load", "tcp6-load", "udp4-load", "udp6-load"
                     })
            {
                await worker.StandardInput.WriteLineAsync(scenario);
                var line = await worker.StandardOutput.ReadLineAsync(limit.Token);
                using var result = JsonDocument.Parse(line ?? throw new IOException("Workload closed unexpectedly."));
                Assert.That(result.RootElement.GetProperty("Skipped").ValueKind, Is.EqualTo(JsonValueKind.Null));
                expectedSent += result.RootElement.GetProperty("PayloadSent").GetInt64();
                expectedReceived += result.RootElement.GetProperty("PayloadReceived").GetInt64();
                var deadline = Stopwatch.GetTimestamp();
                ProcessNetworkSnapshot snapshot;
                ProcessNetworkReading? row;
                do
                {
                    await Task.Delay(250, limit.Token);
                    snapshot = collector.Snapshot(sid);
                    row = snapshot.Processes.SingleOrDefault(p => p.ProcessId == worker.Id);
                    if (row?.SentBytes >= expectedSent && row.ReceivedBytes >= expectedReceived) break;
                } while (Stopwatch.GetElapsedTime(deadline).TotalSeconds < 12);

                Assert.That(collector.Failed, Is.False, scenario);
                Assert.That(row, Is.Not.Null, scenario);
                TestContext.Progress.WriteLine(
                    $"{scenario}: expected {expectedSent}/{expectedReceived}, observed {row!.SentBytes}/{row.ReceivedBytes}; ETW lost {snapshot.EventsLost}, queue dropped {snapshot.DroppedEvents}, quality {snapshot.Quality}");
                Assert.Multiple(() =>
                {
                    Assert.That(row!.SentBytes, Is.EqualTo(expectedSent), scenario);
                    Assert.That(row.ReceivedBytes, Is.EqualTo(expectedReceived), scenario);
                    Assert.That(snapshot.EventsLost, Is.Zero, scenario);
                    Assert.That(snapshot.Quality.HasFlag(ProcessNetworkQuality.Provisional), Is.True, scenario);
                });
                var flows = snapshot.Flows.Where(flow => flow.ProcessId == worker.Id).ToArray();
                Assert.Multiple(() =>
                {
                    Assert.That(snapshot.FlowTrafficSupported, Is.True, scenario);
                    Assert.That(flows.Sum(flow => flow.SentBytes), Is.EqualTo(row.SentBytes), scenario);
                    Assert.That(flows.Sum(flow => flow.ReceivedBytes), Is.EqualTo(row.ReceivedBytes), scenario);
                    Assert.That(flows.Any(flow => flow.Protocol == (scenario.StartsWith("tcp", StringComparison.Ordinal)
                        ? TransportProtocol.Tcp : TransportProtocol.Udp) &&
                        flow.LocalEndpoint.Contains(scenario.Contains('6') ? "::1" : "127.0.0.1", StringComparison.Ordinal)),
                        Is.True, scenario);
                });
                if (scenario == "udp6-load")
                {
                    Assert.That(row!.ReceiveBytesPerSecond, Is.Not.Null,
                        "Known traffic rates must recover after startup uncertainty.");
                    Assert.That(row.SendBytesPerSecond, Is.Not.Null);
                    Assert.That(flows.Sum(flow => flow.ReceiveBytesPerSecond), Is.EqualTo(row.ReceiveBytesPerSecond));
                    Assert.That(flows.Sum(flow => flow.SendBytesPerSecond), Is.EqualTo(row.SendBytesPerSecond));
                }

                results.Add(new
                {
                    Scenario = scenario, Sent = row!.SentBytes, Received = row.ReceivedBytes,
                    row.ReceiveBytesPerSecond, row.SendBytesPerSecond, snapshot.WindowStartQpc, snapshot.WindowEndQpc,
                    snapshot.Quality, snapshot.LateEvents
                });
            }

            await worker.StandardInput.WriteLineAsync("stop");
            await worker.WaitForExitAsync(limit.Token);
            Assert.That(worker.ExitCode, Is.Zero);
            using (var ownedSession = TraceEventSession.GetActiveSession(collector.SessionName!))
            {
                ownedSession.Stop();
            }

            var stoppedAt = Stopwatch.GetTimestamp();
            while (!collector.Failed && Stopwatch.GetElapsedTime(stoppedAt).TotalSeconds < 3)
                await Task.Delay(50, limit.Token);
            Assert.That(collector.Failed, Is.True,
                "An externally stopped session must not become idle-looking live data.");
            Assert.That(collector.Snapshot(sid).Quality.HasFlag(ProcessNetworkQuality.Unavailable), Is.True);
        }
        finally
        {
            if (!worker.HasExited)
            {
                worker.Kill(true);
                await worker.WaitForExitAsync();
            }

            await collector.DisposeAsync();
        }

        Assert.That(TraceEventSession.GetActiveSessionNames(), Does.Not.Contain(collector.SessionName));
        current.Refresh();
        var directory = Path.Combine(root.FullName, "artifacts/network-service");
        Directory.CreateDirectory(directory);
        var report = Path.Combine(directory, $"native-collector-{Guid.NewGuid():N}.json");
        File.WriteAllText(report, JsonSerializer.Serialize(new
        {
            Runtime = RuntimeInformation.FrameworkDescription,
            Windows = Environment.OSVersion.VersionString,
            Results = results,
            IdleIntervalSeconds = 2,
            IdleWithoutTraceCpuMilliseconds = idleWithoutTraceCpu,
            IdleWithTraceCpuMilliseconds = idleWithTraceCpu,
            ElapsedSeconds = Stopwatch.GetElapsedTime(mark).TotalSeconds,
            CollectorAndTestHostCpuMilliseconds = (current.TotalProcessorTime - cpuBefore).TotalMilliseconds,
            TestHostPrivateBytes = current.PrivateMemorySize64,
            SessionStopped = true
        }, new JsonSerializerOptions { WriteIndented = true }));
        TestContext.Progress.WriteLine($"Native collector report: {report}");
        TestContext.AddTestAttachment(report);
    }
}
