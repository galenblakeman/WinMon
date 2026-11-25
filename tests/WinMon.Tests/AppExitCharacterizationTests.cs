using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using NUnit.Framework;

namespace WinMon.Tests;

[TestFixture, NonParallelizable, Category("Integration")]
public sealed class AppExitCharacterizationTests
{
    private static async Task<(int Code, string Output, string Error)> Run(string mode, int code)
    {
        var assembly = typeof(AppExitCharacterizationTests).Assembly.Location;
        var start = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
        };
        foreach (var argument in new[]
        {
            "exec", "--runtimeconfig", Path.ChangeExtension(assembly, ".runtimeconfig.json"),
            "--depsfile", Path.ChangeExtension(assembly, ".deps.json"),
            typeof(AppExitHost.Program).Assembly.Location, "--app-exit-fixture", mode, code.ToString(CultureInfo.InvariantCulture)
        }) start.ArgumentList.Add(argument);
        using var child = Process.Start(start) ?? throw new InvalidOperationException("Exit fixture did not start");
        var output = child.StandardOutput.ReadToEndAsync();
        var error = child.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        try
        {
            await child.WaitForExitAsync(timeout.Token);
            return (child.ExitCode, await output, await error);
        }
        finally
        {
            if (!child.HasExited)
            {
                child.Kill(entireProcessTree: true);
                await child.WaitForExitAsync();
            }
        }
    }

    private static JsonDocument Outcome((int Code, string Output, string Error) result, int expectedCode)
    {
        Assert.That(result.Code, Is.EqualTo(expectedCode), result.Error + result.Output);
        Assert.That(result.Error, Is.Empty);
        var json = JsonDocument.Parse(result.Output);
        Assert.Multiple(() =>
        {
            Assert.That(json.RootElement.GetProperty("ProcessId").GetInt32(), Is.Not.EqualTo(Environment.ProcessId));
            Assert.That(json.RootElement.GetProperty("AppMvid").GetGuid(), Is.EqualTo(typeof(App).Module.ModuleVersionId));
            Assert.That(json.RootElement.GetProperty("AppPath").GetString(), Is.EqualTo(typeof(App).Assembly.Location).IgnoreCase);
            Assert.That(json.RootElement.GetProperty("WindowCounts").EnumerateArray().Select(value => value.GetInt32()), Is.EqualTo(new[] { 0, 0, 0 }));
            Assert.That(json.RootElement.GetProperty("EventCalls").GetInt32(), Is.EqualTo(1));
            Assert.That(json.RootElement.GetProperty("RunCode").GetInt32(), Is.EqualTo(expectedCode));
        });
        return json;
    }

    [TestCase(0)]
    [TestCase(7)]
    [TestCase(-3)]
    public async Task AppExitPreservesRequestedCodesAndRaisesExitBeforeWritingItsTrace(int code)
    {
        using var json = Outcome(await Run("normal", code), code);
        Assert.That(json.RootElement.GetProperty("Events").EnumerateArray().Select(value => value.GetString()), Is.EqualTo(new[]
        {
            "host.startup", "exit.before", "event.enter:" + code, "event.leave:" + code,
            "trace:App.Exit code=" + code, "exit.after", "run.return:" + code
        }));
        Assert.That(json.RootElement.GetProperty("CaughtType").ValueKind, Is.EqualTo(JsonValueKind.Null));
    }

    [Test]
    public async Task AppExitLogsAndReturnsTheExitCodeChangedByAnExitSubscriber()
    {
        using var json = Outcome(await Run("mutate", 7), 12);
        Assert.That(json.RootElement.GetProperty("Events").EnumerateArray().Select(value => value.GetString()), Is.EqualTo(new[]
        {
            "host.startup", "exit.before", "event.enter:7", "event.leave:12", "trace:App.Exit code=12", "exit.after", "run.return:12"
        }));
    }

    [Test]
    public async Task AppExitDoesNotRaiseAnotherExitWhenASubscriberRequestsShutdownAgain()
    {
        using var json = Outcome(await Run("reenter", 7), 7);
        Assert.That(json.RootElement.GetProperty("Traces").EnumerateArray().Select(value => value.GetString()), Is.EqualTo(new[] { "App.Exit code=7" }));
    }

    [Test]
    public async Task AppExitPropagatesTheOriginalSubscriberExceptionAndSkipsTheSuccessTrace()
    {
        using var json = Outcome(await Run("throw", 7), 71);
        Assert.Multiple(() =>
        {
            Assert.That(json.RootElement.GetProperty("CaughtType").GetString(), Is.EqualTo(typeof(IOException).FullName));
            Assert.That(json.RootElement.GetProperty("CaughtSameException").GetBoolean(), Is.True);
            Assert.That(json.RootElement.GetProperty("Traces").GetArrayLength(), Is.Zero);
            Assert.That(json.RootElement.GetProperty("Events").EnumerateArray().Select(value => value.GetString()), Is.EqualTo(new[]
            { "host.startup", "exit.before", "event.enter:7", "exit.caught", "exit.after", "run.return:71" }));
        });
    }

    [Test]
    public async Task AppExitHostRejectsUnknownModesBeforeStartingAnApplication()
    {
        var result = await Run("unknown", 7);
        Assert.Multiple(() =>
        {
            Assert.That(result.Code, Is.EqualTo(64));
            Assert.That(result.Output, Is.Empty);
            Assert.That(result.Error, Is.Empty);
        });
    }
}
