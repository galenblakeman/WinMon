using System.Diagnostics;
using System.Reflection;
using System.Security.Principal;
using System.Text.Json;

namespace WinMon.NetworkProbe;

internal static class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    internal static async Task<int> Main(string[] args)
    {
        if (args is ["--worker"])
        {
            try { return await SocketWorkload.RunAsync(); }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Socket workload failed: {ex.GetType().Name}: {ex.Message}");
                return 1;
            }
        }
        if (args is not ["--output", var output])
        {
            Console.Error.WriteLine("Usage: WinMon.NetworkProbe --output <new-report.json>");
            return 2;
        }

        output = Path.GetFullPath(output);
        if (File.Exists(output))
        {
            Console.Error.WriteLine("Refusing to replace an existing report.");
            return 2;
        }
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        ConsoleCancelEventHandler cancel = (_, e) => { e.Cancel = true; lifetime.Cancel(); };
        Console.CancelKeyPress += cancel;
        var elevated = IsElevated();
        string? sessionName = null;
        var phases = new List<PhaseResult>();
        CaptureSnapshot? final = null;
        var stopped = false;
        var exitCode = 1;
        string? error = null;
        try
        {
            using var worker = StartWorker();
            try
            {
                var ready = await worker.StandardOutput.ReadLineAsync(lifetime.Token);
                if (ready != "ready") throw new IOException("Socket workload did not become ready.");
                phases.Add(await IdleAsync("idle-without-trace", lifetime.Token));
                using (var capture = new NetworkCapture(worker.Id, worker.StartTime.ToUniversalTime().ToFileTimeUtc(),
                           worker.StartTime.ToUniversalTime()))
                {
                    sessionName = capture.SessionName;
                    Console.WriteLine($"Session: {sessionName}");
                    capture.Start();
                    await capture.SettleAsync(lifetime.Token);
                    phases.Add(await IdleAsync("idle-with-trace", lifetime.Token));
                    foreach (var scenario in SocketWorkload.Scenarios)
                    {
                        var before = capture.Snapshot();
                        var performance = PerformanceMark.Now();
                        await worker.StandardInput.WriteLineAsync(scenario);
                        var resultLine = await worker.StandardOutput.ReadLineAsync(lifetime.Token)
                            ?? throw new IOException("Socket workload closed unexpectedly.");
                        var workload = JsonSerializer.Deserialize<WorkloadResult>(resultLine)
                            ?? throw new IOException("Socket workload returned no result.");
                        var tcp = scenario.StartsWith("tcp", StringComparison.Ordinal);
                        var ipv6 = scenario.Contains('6');
                        var after = await capture.DrainBytesAsync(before, tcp, ipv6, workload.PayloadSent,
                            workload.PayloadReceived, lifetime.Token);
                        var sent = NetworkCapture.Count(after, tcp, ipv6, true) - NetworkCapture.Count(before, tcp, ipv6, true);
                        var received = NetworkCapture.Count(after, tcp, ipv6, false) - NetworkCapture.Count(before, tcp, ipv6, false);
                        phases.Add(new PhaseResult(scenario, workload, sent, received,
                            after.EventsLost - before.EventsLost, performance.Finish()));
                        Console.WriteLine($"{scenario}: sent {sent}, received {received}, payload {workload.PayloadSent}");
                    }
                    await worker.StandardInput.WriteLineAsync("stop");
                    await worker.WaitForExitAsync(lifetime.Token);
                    if (worker.ExitCode != 0) throw new IOException("Socket workload failed.");
                    await capture.SettleAsync(lifetime.Token);
                    final = capture.Snapshot();
                }
                stopped = !TraceSessionExists(sessionName);
                var traffic = phases.Where(p => p.Workload is { Skipped: null }).ToArray();
                exitCode = stopped && final.EventsLost == 0 && final.PayloadPidMismatches == 0 &&
                    final.UnattributedWorkerEvents == 0 && final.UnsupportedWorkerEvents == 0 && final.PayloadKindsChecked == 8 &&
                    traffic.Length == SocketWorkload.Scenarios.Length &&
                    traffic.All(p => p.ObservedSent == p.Workload!.PayloadSent &&
                                     p.ObservedReceived == p.Workload.PayloadReceived) ? 0 : 1;
            }
            finally
            {
                if (!worker.HasExited)
                {
                    worker.Kill(entireProcessTree: true);
                    await worker.WaitForExitAsync();
                }
            }
        }
        catch (Exception ex)
        {
            error = $"{ex.GetType().Name}: {ex.Message}";
            Console.Error.WriteLine(error);
            if (sessionName is not null) stopped = !TraceSessionExists(sessionName);
        }
        finally
        {
            Console.CancelKeyPress -= cancel;
        }

        var report = new
        {
            SchemaVersion = 1,
            StartedFrom = "ETW prototype; not the production service",
            RecordedAtUtc = DateTimeOffset.UtcNow,
            Runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            Windows = Environment.OSVersion.VersionString,
            TraceEventVersion = typeof(Microsoft.Diagnostics.Tracing.TraceEvent).Assembly.GetName().Version?.ToString(),
            Elevated = elevated,
            SessionName = sessionName,
            SessionStopped = stopped,
            Error = error,
            Phases = phases,
            Final = final,
            CapturePassed = exitCode == 0,
            Limitations = new[]
            {
                "Controlled loopback workload only; no external link, VPN, retransmission or saturation proof.",
                "Rows belong only to the spawned worker; no production ownership authorization is implemented.",
                "CPU and allocations measure the collector process, excluding workload and kernel tracing cost.",
                "Per-phase observations wait up to ten seconds for known workload bytes; not throughput benchmarks.",
                "Byte equality is reported for comparison and is not asserted as a Windows accounting guarantee."
            }
        };
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        await using (var stream = new FileStream(output, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
            await JsonSerializer.SerializeAsync(stream, report, JsonOptions);
        Console.WriteLine($"Report: {output}");
        return exitCode;
    }

    private static bool IsElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static Process StartWorker()
    {
        var executable = Environment.ProcessPath ?? throw new InvalidOperationException("No executable path.");
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true
        };
        if (Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
        start.ArgumentList.Add("--worker");
        var process = Process.Start(start) ?? throw new IOException("Could not start socket workload.");
        process.StandardInput.AutoFlush = true;
        return process;
    }

    private static bool TraceSessionExists(string name) =>
        Microsoft.Diagnostics.Tracing.Session.TraceEventSession.GetActiveSessionNames().Contains(name);

    private static async Task<PhaseResult> IdleAsync(string name, CancellationToken token)
    {
        var mark = PerformanceMark.Now();
        await Task.Delay(TimeSpan.FromSeconds(3), token);
        return new PhaseResult(name, null, 0, 0, 0, mark.Finish());
    }
}

internal sealed record PhaseResult(string Scenario, WorkloadResult? Workload, long ObservedSent,
    long ObservedReceived, long EventsLost, PerformanceReading Performance);

internal sealed record PerformanceReading(double ElapsedSeconds, double CollectorCpuMilliseconds,
    long CollectorAllocatedBytes, long CollectorPrivateBytes);

internal sealed record PerformanceMark(long Timestamp, TimeSpan Cpu, long Allocations)
{
    internal static PerformanceMark Now()
    {
        using var process = Process.GetCurrentProcess();
        return new PerformanceMark(Stopwatch.GetTimestamp(), process.TotalProcessorTime, GC.GetTotalAllocatedBytes());
    }

    internal PerformanceReading Finish()
    {
        using var process = Process.GetCurrentProcess();
        return new PerformanceReading(Stopwatch.GetElapsedTime(Timestamp).TotalSeconds,
            (process.TotalProcessorTime - Cpu).TotalMilliseconds, GC.GetTotalAllocatedBytes() - Allocations,
            process.PrivateMemorySize64);
    }
}
