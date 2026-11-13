using System.Diagnostics;
using System.Security.Principal;
using System.Text.Json;
using Microsoft.Diagnostics.Tracing.Session;
using NUnit.Framework;
using WinMon.NetworkProbe;

namespace WinMon.Tests;

[TestFixture]
public sealed class ProbeStartupTests
{
    [Test]
    [Category("Integration")]
    [Explicit("Requires a non-elevated Windows process to check ETW startup failure.")]
    public async Task NonElevatedCapture_ReportsStartupFailureAndStopsItsSession()
    {
        using var identity = WindowsIdentity.GetCurrent();
        Assert.That(new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator), Is.False,
            "This explicit test exercises rejection of non-elevated ETW startup.");
        var reportPath = Path.Combine(Path.GetTempPath(), "WinMon-probe-test-" + Guid.NewGuid().ToString("N") + ".json");
        var start = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        start.ArgumentList.Add(typeof(NetworkLedger).Assembly.Location);
        start.ArgumentList.Add("--output");
        start.ArgumentList.Add(reportPath);
        using var process = Process.Start(start)!;
        using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var output = process.StandardOutput.ReadToEndAsync(limit.Token);
        var errors = process.StandardError.ReadToEndAsync(limit.Token);
        try
        {
            await process.WaitForExitAsync(limit.Token);
            Assert.That(process.ExitCode, Is.EqualTo(1), await errors);
            Assert.That(await output, Does.Contain("Report:"));
            using var document = JsonDocument.Parse(await File.ReadAllTextAsync(reportPath, limit.Token));
            var report = document.RootElement;
            Assert.Multiple(() =>
            {
                Assert.That(report.GetProperty("SchemaVersion").GetInt32(), Is.EqualTo(1));
                Assert.That(report.GetProperty("Elevated").GetBoolean(), Is.False);
                Assert.That(report.GetProperty("CapturePassed").GetBoolean(), Is.False);
                Assert.That(report.GetProperty("SessionStopped").GetBoolean(), Is.True);
                Assert.That(report.GetProperty("Error").GetString(), Does.StartWith("UnauthorizedAccessException:"));
                Assert.That(report.GetProperty("Final").ValueKind, Is.EqualTo(JsonValueKind.Null));
                Assert.That(TraceEventSession.GetActiveSessionNames(),
                    Does.Not.Contain(report.GetProperty("SessionName").GetString()));
            });
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
            }
            File.Delete(reportPath);
        }
    }
}
