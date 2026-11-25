using System.Net.Sockets;
using System.Text.Json;
using NUnit.Framework;

namespace WinMon.NetworkProbe.Tests;

[TestFixture]
[NonParallelizable]
public sealed class SocketWorkloadTests
{
    [Test]
    [Category("Integration")]
    public async Task Scenarios_TransferTheReportedPayloadAndStopCleanly()
    {
        var input = Console.In;
        var output = Console.Out;
        using var commands = new StringReader(string.Join('\n', SocketWorkload.Scenarios.Append("stop")));
        using var report = new StringWriter();
        try
        {
            Console.SetIn(commands);
            Console.SetOut(report);
            Assert.That(await SocketWorkload.RunAsync(), Is.Zero);
        }
        finally
        {
            Console.SetIn(input);
            Console.SetOut(output);
        }

        var lines = report.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.That(lines[0], Is.EqualTo("ready"));
        var results = lines.Skip(1).Select(line => JsonSerializer.Deserialize<WorkloadResult>(line)!).ToArray();
        Assert.That(results.Select(result => result.Scenario), Is.EqualTo(SocketWorkload.Scenarios));
        foreach (var result in results)
        {
            if (result.Scenario.Contains('6') && !Socket.OSSupportsIPv6)
            {
                Assert.That(result.Skipped, Is.EqualTo("IPv6 unavailable"));
                Assert.That(result.PayloadSent, Is.Zero);
                Assert.That(result.PayloadReceived, Is.Zero);
                continue;
            }

            var minimum = result.Scenario.EndsWith("existing", StringComparison.Ordinal) ||
                          (result.Scenario.StartsWith("tcp", StringComparison.Ordinal) &&
                           result.Scenario.EndsWith("load", StringComparison.Ordinal))
                ? 16 * 1024 * 1024
                : result.Scenario.EndsWith("short", StringComparison.Ordinal) ? 32 * 4096 : 1024 * 1024;
            Assert.Multiple(() =>
            {
                Assert.That(result.Skipped, Is.Null, result.Scenario);
                Assert.That(result.PayloadSent, Is.GreaterThanOrEqualTo(minimum), result.Scenario);
                Assert.That(result.PayloadSent % minimum, Is.Zero, result.Scenario);
                Assert.That(result.PayloadReceived, Is.EqualTo(result.PayloadSent), result.Scenario);
            });
        }
    }

    [Test]
    [Category("Integration")]
    public void UnknownScenario_IsRejected()
    {
        var input = Console.In;
        var output = Console.Out;
        using var commands = new StringReader("unknown\n");
        using var report = new StringWriter();
        try
        {
            Console.SetIn(commands);
            Console.SetOut(report);
            Assert.ThrowsAsync<ArgumentException>(async () => await SocketWorkload.RunAsync());
        }
        finally
        {
            Console.SetIn(input);
            Console.SetOut(output);
        }
    }

    [Test]
    [Category("Integration")]
    public async Task ExistingReport_IsNeverOverwritten()
    {
        var path = Path.GetTempFileName();
        var error = Console.Error;
        using var report = new StringWriter();
        try
        {
            await File.WriteAllTextAsync(path, "original");
            Console.SetError(report);
            Assert.That(await Program.Main(["--output", path]), Is.EqualTo(2));
            Assert.That(await File.ReadAllTextAsync(path), Is.EqualTo("original"));
            Assert.That(report.ToString(), Does.Contain("Refusing to replace"));
        }
        finally
        {
            Console.SetError(error);
            File.Delete(path);
        }
    }

    [Test]
    public async Task MissingArguments_ReportsUsage()
    {
        var error = Console.Error;
        using var report = new StringWriter();
        try
        {
            Console.SetError(report);
            Assert.That(await Program.Main([]), Is.EqualTo(2));
            Assert.That(report.ToString(), Does.Contain("Usage: WinMon.NetworkProbe"));
        }
        finally
        {
            Console.SetError(error);
        }
    }
}
