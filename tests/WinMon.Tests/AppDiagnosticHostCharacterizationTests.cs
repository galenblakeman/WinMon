using System.Diagnostics;
using System.Text.Json;
using NUnit.Framework;

namespace WinMon.Tests;

[TestFixture, NonParallelizable, Category("Integration")]
public sealed class AppDiagnosticHostCharacterizationTests
{
    [TestCase("gauges"), TestCase("charts"), TestCase("examples"), TestCase("compact")]
    [TestCase("search"), TestCase("snapshots"), TestCase("features"), TestCase("full"), TestCase("all")]
    public async Task DiagnosticHostDirectoryFailureStillClosesItsWindowAndShutsDown(string mode)
    {
        using var outcome = await Run(mode);
        Assert.Multiple(() =>
        {
            Assert.That(outcome.Json.GetProperty("OperationSucceeded").GetBoolean(), Is.False);
            Assert.That(outcome.Json.GetProperty("ErrorType").GetString(), Is.EqualTo(typeof(IOException).FullName));
            Assert.That(outcome.Json.GetProperty("TraceErrorType").GetString(), Is.EqualTo(typeof(IOException).FullName));
            Assert.That(outcome.Json.GetProperty("Obstruction").GetString(), Is.EqualTo("owned output obstruction"));
            Assert.That(outcome.Json.GetProperty("Failure").ValueKind, Is.EqualTo(JsonValueKind.Null));
            Assert.That(outcome.Events, Is.EqualTo(new[] { "host.startup", "diagnostic.error", "window.closed", "app.exit:1" }));
        });
    }

    [Test]
    public async Task DiagnosticHostRecoversItsOutputFolderAndWritesTheOriginalErrorBeforeShutdown()
    {
        using var outcome = await Run("recover");
        Assert.Multiple(() =>
        {
            Assert.That(outcome.Json.GetProperty("OperationSucceeded").GetBoolean(), Is.True);
            Assert.That(outcome.Json.GetProperty("ErrorType").ValueKind, Is.EqualTo(JsonValueKind.Null));
            Assert.That(outcome.Json.GetProperty("Failure").GetString(), Is.EqualTo(outcome.Json.GetProperty("OriginalError").GetString()));
            Assert.That(File.ReadAllText(Path.Combine(outcome.Directory, "output.sentinel")), Is.EqualTo("owned output obstruction"));
            Assert.That(outcome.Events, Is.EqualTo(new[] { "host.startup", "diagnostic.error", "window.closed", "app.exit:1" }));
        });
    }

    [Test]
    public async Task DiagnosticHostReportWriteFailurePreservesTheSentinelAndStillShutsDown()
    {
        using var outcome = await Run("locked");
        Assert.Multiple(() =>
        {
            Assert.That(outcome.Json.GetProperty("OperationSucceeded").GetBoolean(), Is.False);
            Assert.That(outcome.Json.GetProperty("ErrorType").GetString(), Is.EqualTo(typeof(IOException).FullName));
            Assert.That(outcome.Json.GetProperty("SameError").GetBoolean(), Is.False);
            Assert.That(outcome.Json.GetProperty("Failure").GetString(), Is.EqualTo("owned failure report sentinel"));
            Assert.That(outcome.Events, Is.EqualTo(new[] { "host.startup", "diagnostic.error", "window.closed", "app.exit:1" }));
        });
    }

    [Test]
    public async Task ServicesDiagnosticHostDirectoryFailureStillClosesItsWindowAndShutsDown()
    {
        using var outcome = await Run("services");
        Assert.Multiple(() =>
        {
            Assert.That(outcome.Json.GetProperty("OperationSucceeded").GetBoolean(), Is.False);
            Assert.That(outcome.Json.GetProperty("ErrorType").GetString(), Is.EqualTo(typeof(IOException).FullName));
            Assert.That(outcome.Json.GetProperty("TraceErrorType").ValueKind, Is.EqualTo(JsonValueKind.Null));
            Assert.That(outcome.Json.GetProperty("Obstruction").GetString(), Is.EqualTo("owned output obstruction"));
            Assert.That(outcome.Events, Is.EqualTo(new[] { "host.startup", "window.closed", "app.exit:1" }));
        });
    }

    private static async Task<HostOutcome> Run(string mode)
    {
        var directory = Path.Combine(Path.GetTempPath(), "WinMon.DiagnosticsHost-" + Guid.NewGuid().ToString("N"));
        try
        {
            var assembly = typeof(AppDiagnosticHostCharacterizationTests).Assembly.Location;
            var start = new ProcessStartInfo("dotnet")
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
            };
            foreach (var argument in new[]
            {
                "exec", "--runtimeconfig", Path.ChangeExtension(assembly, ".runtimeconfig.json"),
                "--depsfile", Path.ChangeExtension(assembly, ".deps.json"),
                typeof(AppExitHost.Program).Assembly.Location, "--diagnostic-host-fixture", mode, directory
            }) start.ArgumentList.Add(argument);
            using var child = Process.Start(start) ?? throw new InvalidOperationException("Diagnostic fixture did not start");
            var output = child.StandardOutput.ReadToEndAsync();
            var error = child.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            try { await child.WaitForExitAsync(timeout.Token); }
            finally
            {
                if (!child.HasExited)
                {
                    child.Kill(entireProcessTree: true);
                    await child.WaitForExitAsync();
                }
            }
            var text = await output;
            Assert.That(await error, Is.Empty, text);
            Assert.That(child.ExitCode, Is.EqualTo(1), text);
            var document = JsonDocument.Parse(text);
            var json = document.RootElement;
            Assert.Multiple(() =>
            {
                Assert.That(json.GetProperty("ProcessId").GetInt32(), Is.Not.EqualTo(Environment.ProcessId));
                Assert.That(json.GetProperty("AppMvid").GetGuid(), Is.EqualTo(typeof(App).Module.ModuleVersionId));
                Assert.That(json.GetProperty("AppPath").GetString(), Is.EqualTo(typeof(App).Assembly.Location).IgnoreCase);
                Assert.That(json.GetProperty("RunCode").GetInt32(), Is.EqualTo(1));
                Assert.That(json.GetProperty("OperationCompleted").GetBoolean(), Is.True);
                Assert.That(json.GetProperty("WindowClosed").GetBoolean(), Is.True);
                Assert.That(json.GetProperty("RemainingWindows").GetInt32(), Is.Zero);
                Assert.That(json.GetProperty("SamplingStarted").GetBoolean(), Is.False);
                Assert.That(json.GetProperty("ProductStartupSkipped").GetBoolean(), Is.True);
                Assert.That(json.GetProperty("LogAttached").GetBoolean(), Is.False);
                Assert.That(json.GetProperty("InstanceOwned").GetBoolean(), Is.False);
                Assert.That(json.GetProperty("ActivationAttached").GetBoolean(), Is.False);
                Assert.That(json.GetProperty("SettingsWritten").GetBoolean(), Is.False);
            });
            return new HostOutcome(directory, document);
        }
        catch
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
            throw;
        }
    }

    private sealed class HostOutcome(string directory, JsonDocument document) : IDisposable
    {
        internal string Directory => directory;
        internal JsonElement Json => document.RootElement;
        internal IEnumerable<string?> Events => Json.GetProperty("Events").EnumerateArray().Select(value => value.GetString());
        public void Dispose()
        {
            document.Dispose();
            System.IO.Directory.Delete(directory, true);
        }
    }
}
