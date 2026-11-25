using MonCore.Monitoring;
using MonCore.Windows.Monitoring;
using NUnit.Framework;

namespace WinMon.Cli.Tests;

[TestFixture]
public sealed class HardwareCommandTests
{
    private static HardwareInventorySnapshot Inventory(bool identifiers) => new(1, DateTimeOffset.UtcNow,
        DateTimeOffset.UtcNow, "ok", identifiers, false,
        new("ok", false, [], new HardwareCpu(["Example CPU"], 1, 2, [])),
        new("ok", false, [], new HardwareMotherboards([])),
        new("ok", false, [], new HardwareMemory(1, 1, 0, null, [])),
        new("ok", false, [], new HardwareGraphics([])),
        new("ok", false, [], new HardwareDisks([])),
        new("ok", false, [], new HardwareVolumes([])),
        new("ok", false, [], new HardwareNetwork([])));

    [Test]
    public async Task JsonHardwareReportUsesTheSharedPrivacyProjection()
    {
        using var output = new StringWriter();
        var includeIdentifiers = false;
        var command = CliCommand.Create(output: output, error: output, readHardware: (included, _) =>
        {
            includeIdentifiers = included;
            return Inventory(included);
        });

        Assert.That(await command.Parse(["hardware", "--format", "json", "--include-identifiers"]).InvokeAsync(), Is.Zero);
        Assert.Multiple(() =>
        {
            Assert.That(includeIdentifiers, Is.True);
            Assert.That(output.ToString(), Does.Contain("\"identifiersIncluded\": true"));
            Assert.That(output.ToString(), Does.Contain("\"schemaVersion\": 1"));
        });
    }

    [Test]
    public async Task TableHardwareReportStatesWhenIdentifiersAreOmitted()
    {
        using var output = new StringWriter();
        var command = CliCommand.Create(output: output, error: output, readHardware: (included, _) => Inventory(included));

        Assert.That(await command.Parse(["hardware"]).InvokeAsync(), Is.Zero);
        Assert.That(output.ToString(), Does.Contain("Example CPU").And.Contain("omitted"));
    }

    [TestCase("partial", 0)]
    [TestCase("unavailable", 1)]
    public async Task JsonReportsAvailabilityEvenWhenNothingCouldBeRead(string status, int expectedExit)
    {
        using var output = new StringWriter();
        var command = CliCommand.Create(output: output, error: output,
            readHardware: (_, _) => Inventory(false) with { Status = status });
        Assert.That(await command.Parse(["hardware", "--format", "json"]).InvokeAsync(), Is.EqualTo(expectedExit));
        Assert.That(output.ToString(), Does.Contain($"\"status\": \"{status}\""));
    }

    [Test]
    public void TabCyclesThroughHardwareAndBackWithoutChangingExistingOrder()
    {
        var state = new InteractiveState(ProcessSort.Cpu, "");
        var tabs = new List<TerminalTab>();
        for (var i = 0; i < 5; i++)
        {
            state.Handle(new ConsoleKeyInfo('\t', ConsoleKey.Tab, false, false, false), 10);
            tabs.Add(state.Tab);
        }
        Assert.That(tabs, Is.EqualTo(new[] { TerminalTab.Services, TerminalTab.Connections,
            TerminalTab.Usage, TerminalTab.Hardware, TerminalTab.Processes }));
    }

    [Test]
    public async Task CancellationDoesNotPublishAnInventory()
    {
        using var output = new StringWriter();
        using var cancellation = new CancellationTokenSource();
        var command = CliCommand.Create(output: output, error: output, readHardware: (_, _) =>
        {
            cancellation.Cancel();
            return Inventory(false);
        });
        Assert.That(await command.Parse(["hardware", "--format", "json"])
            .InvokeAsync(cancellationToken: cancellation.Token), Is.EqualTo(130));
        Assert.That(output.ToString(), Is.Empty);
    }

    [TestCase(80, 20)]
    [TestCase(140, 40)]
    public void HardwareRowsAndDetailsWorkWithoutAnyPerformanceFrame(int width, int height)
    {
        var state = new InteractiveState(ProcessSort.Cpu, "");
        state.Configure(0, false, false, TerminalTab.Hardware);
        state.Hardware = Inventory(false);
        using var output = new StringWriter();
        var console = Spectre.Console.AnsiConsole.Create(new Spectre.Console.AnsiConsoleSettings
        {
            Out = new Spectre.Console.AnsiConsoleOutput(output), Ansi = Spectre.Console.AnsiSupport.No,
            Enrichment = new() { UseDefaultEnrichers = false }
        });
        console.Profile.Width = width;
        console.Write(InteractiveMonitor.BuildView(state, 0, 10, width, height));
        Assert.That(InteractiveMonitor.Query(state), Is.SameAs(InteractiveMonitor.Query(state)), "Unchanged inventory is formatted once.");
        Assert.Multiple(() =>
        {
            Assert.That(state.Active.Rows, Has.Count.EqualTo(7));
            Assert.That(output.ToString(), Does.Contain("[F5]").And.Contain("Motherboard").And.Contain("Network adapters"));
            Assert.That(output.ToString().Split('\n').Length, Is.LessThanOrEqualTo(height + 1));
            Assert.That(output.ToString(), Does.Not.Contain("Sort").And.Not.Contain("Filter"));
        });
        state.Handle(new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false), 10);
        Assert.That(state.Selected!.Details, Does.Contain("Example CPU"));
    }
}
