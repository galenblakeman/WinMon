using System.CommandLine;
using System.Globalization;
using System.Text.Json;
using NUnit.Framework;
using WinMon.Cli;
using WinMon.Monitoring;

namespace WinMon.Cli.Tests;

[TestFixture]
public sealed class SnapshotTests
{
    private static CapturedFrame Frame => new(new MonitorSample(DateTimeOffset.UtcNow, 25,
    [
        new ProcessUsage(42, 1, "alpha", 20, 10),
        new ProcessUsage(43, 2, "beta", 5, 100) { CpuAvailable = false }
    ], 2) { IntervalSeconds = 1.2, Resources = new ResourceSample { UsedMemoryBytes = 1024 } });

    [TestCase("--top", "-1")]
    [TestCase("--top", "1001")]
    [TestCase("--top", "no")]
    [TestCase("--sort", "bad")]
    [TestCase("--format", "csv")]
    [TestCase("--unknown", "x")]
    public void InvalidOptionsAreRejectedBeforeCollecting(string option, string value)
    {
        var command = CliCommand.Create(() => throw new AssertionException("Must not collect"));
        Assert.That(command.Parse(["snapshot", option, value]).Errors, Is.Not.Empty);
    }

    [Test]
    public void RankingFiltersBeforeLimitingAndPreservesUnavailableValues()
    {
        var report = SnapshotReport.Create(Frame, "test", 1, ProcessSort.Memory, "BETA");
        Assert.Multiple(() =>
        {
            Assert.That(report.Processes.Single().Pid, Is.EqualTo(43));
            Assert.That(report.Processes[0].CpuPercent, Is.Null);
            Assert.That(report.Processes[0].PrivateBytes, Is.Null);
            Assert.That(report.IntervalSeconds, Is.EqualTo(1.2));
            Assert.That(report.MemoryTotalBytes, Is.Null);
        });
        Assert.That(SnapshotReport.Create(Frame, "test", 1, ProcessSort.Cpu, "").Processes[0].Pid,
            Is.EqualTo(42));
    }

    [Test]
    public async Task JsonCommandTakesBaselineAndMeasurementAndDisposesSource()
    {
        using var output = new StringWriter(CultureInfo.InvariantCulture);
        using var error = new StringWriter();
        var source = new FakeSource();
        var exit = await CliCommand.Create(() => source, output, error)
            .Parse(["snapshot", "--format", "json", "--top", "1"]).InvokeAsync();
        using var json = JsonDocument.Parse(output.ToString());
        Assert.Multiple(() =>
        {
            Assert.That(exit, Is.Zero);
            Assert.That(source.Captures, Is.EqualTo(2));
            Assert.That(source.Disposed, Is.True);
            Assert.That(error.ToString(), Is.Empty);
            Assert.That(json.RootElement.GetProperty("schemaVersion").GetInt32(), Is.EqualTo(1));
            Assert.That(json.RootElement.GetProperty("processes").GetArrayLength(), Is.EqualTo(1));
            Assert.That(json.RootElement.GetProperty("memoryTotalBytes").ValueKind, Is.EqualTo(JsonValueKind.Null));
        });
    }

    [Test]
    public async Task CancellationDisposesCollectorWithoutWritingAPartialReport()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        var source = new FakeSource();
        var exit = await CliCommand.Create(() => source, output, error).Parse(["snapshot"])
            .InvokeAsync(cancellationToken: cancellation.Token);
        Assert.Multiple(() =>
        {
            Assert.That(exit, Is.EqualTo(130));
            Assert.That(source.Disposed, Is.True);
            Assert.That(source.Captures, Is.EqualTo(1));
            Assert.That(output.ToString(), Is.Empty);
            Assert.That(error.ToString(), Is.Empty);
        });
    }

    [Test]
    public async Task FailureReturnsNonzeroWithoutPollutingStandardOutput()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var exit = await CliCommand.Create(() => throw new IOException("native\u001b[2J failure"), output, error)
            .Parse(["snapshot", "--format", "json"]).InvokeAsync();
        Assert.Multiple(() =>
        {
            Assert.That(exit, Is.EqualTo(1));
            Assert.That(output.ToString(), Is.Empty);
            Assert.That(error.ToString(), Does.Contain("failure"));
            Assert.That(error.ToString().Contains('\u001b'), Is.False);
        });
    }

    [Test]
    public async Task TableTreatsNamesAsTextAndRemovesTerminalControls()
    {
        using var output = new StringWriter();
        var frame = Frame with { Full = Frame.Full with
        {
            Processes = [new ProcessUsage(1, 1, "[red]test[/]\u001b\n\u202e", 2)]
        } };
        var exit = await CliCommand.Create(() => new FakeSource(frame), output)
            .Parse(["snapshot", "--no-color"]).InvokeAsync();
        Assert.Multiple(() =>
        {
            Assert.That(exit, Is.Zero);
            Assert.That(output.ToString(), Does.Contain("[red]test[/]"));
            Assert.That(output.ToString().Contains('\u001b'), Is.False);
            Assert.That(output.ToString().Contains('\u202e'), Is.False);
        });
    }

    [Test]
    public void TranslatedHelpResourcesAreAvailableWithoutWpf()
    {
        var previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("de");
            Assert.That(CliCommand.Create().Subcommands.Single(c => c.Name == "snapshot").Description,
                Does.Contain("Windows-Ressourcen"));
        }
        finally { CultureInfo.CurrentUICulture = previous; }
    }

    private sealed class FakeSource(CapturedFrame? frame = null) : IFrameSource
    {
        public int Captures { get; private set; }
        public bool Disposed { get; private set; }
        public CapturedFrame Capture(SamplingOptions options) { Captures++; return frame ?? Frame; }
        public void Reset() { }
        public void Dispose() => Disposed = true;
    }
}
