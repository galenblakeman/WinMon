using System.Globalization;
using System.Text.Json;
using NUnit.Framework;
using WinMon.NetworkProbe;

namespace WinMon.Tests;

[TestFixture, NonParallelizable, Category("Integration")]
public sealed class WorkerEntryCharacterizationTests
{
    [TestCase("stop"), TestCase("eof"), TestCase("stop-with-trailing")]
    public async Task WorkerEntryAnnouncesReadinessAndStopsOnTheExactTerminalCommand(string terminal)
    {
        var commands = terminal switch
        {
            "stop" => "stop\n",
            "eof" => "",
            "stop-with-trailing" => "stop\nunknown\n",
            _ => throw new AssertionException("Unknown terminal case")
        };

        var result = await RunOwnedWorker(commands);

        Assert.That(result.ExitCode, Is.Zero);
        Assert.That(result.Output, Is.EqualTo("ready" + Environment.NewLine));
        Assert.That(result.Error, Is.Empty);
        Assert.That(result.RemainingInput, Is.EqualTo(terminal == "stop-with-trailing" ? "unknown\n" : ""));
    }

    [TestCase("unknown"), TestCase("uppercase-stop"), TestCase("blank"), TestCase("trailing-space")]
    public async Task WorkerEntryConvertsInvalidProtocolCommandsToTheExistingErrorAndExitCode(string command)
    {
        var commands = command switch
        {
            "unknown" => "unknown\n",
            "uppercase-stop" => "STOP\n",
            "blank" => "\n",
            "trailing-space" => "tcp4-short \n",
            _ => throw new AssertionException("Unknown invalid command case")
        };

        var result = await RunOwnedWorker(commands);

        Assert.That(result.ExitCode, Is.EqualTo(1));
        Assert.That(result.Output, Is.EqualTo("ready" + Environment.NewLine));
        Assert.That(result.Error, Is.EqualTo("Socket workload failed: ArgumentException: Unknown workload scenario." + Environment.NewLine));
        Assert.That(result.RemainingInput, Is.Empty);
    }

    [Test]
    public async Task WorkerEntryRunsOneShortTcpScenarioAndReportsItsExactOwnedPayload()
    {
        var result = await RunOwnedWorker("tcp4-short\nstop\n");

        Assert.That(result.ExitCode, Is.Zero);
        Assert.That(result.Error, Is.Empty);
        Assert.That(result.RemainingInput, Is.Empty);
        var lines = result.Output.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.That(lines, Has.Length.EqualTo(2));
        Assert.That(lines[0], Is.EqualTo("ready"));
        var workload = JsonSerializer.Deserialize<WorkloadResult>(lines[1])!;
        Assert.That(workload.Scenario, Is.EqualTo("tcp4-short"));
        Assert.That(workload.PayloadSent, Is.EqualTo(131072));
        Assert.That(workload.PayloadReceived, Is.EqualTo(131072));
        Assert.That(workload.Skipped, Is.Null);
    }

    private sealed record WorkerResult(int ExitCode, string Output, string Error, string RemainingInput);

    private static async Task<WorkerResult> RunOwnedWorker(string commands)
    {
        var originalInput = Console.In;
        var originalOutput = Console.Out;
        var originalError = Console.Error;
        using var input = new StringReader(commands);
        using var output = new StringWriter(CultureInfo.InvariantCulture);
        using var error = new StringWriter(CultureInfo.InvariantCulture);
        int exitCode;
        try
        {
            Console.SetIn(input);
            Console.SetOut(output);
            Console.SetError(error);
            exitCode = await Program.Main(["--worker"]);
        }
        finally
        {
            Console.SetIn(originalInput);
            Console.SetOut(originalOutput);
            Console.SetError(originalError);
        }
        Assert.That(Console.In, Is.SameAs(originalInput));
        Assert.That(Console.Out, Is.SameAs(originalOutput));
        Assert.That(Console.Error, Is.SameAs(originalError));
        return new WorkerResult(exitCode, output.ToString(), error.ToString(), input.ReadToEnd());
    }
}
