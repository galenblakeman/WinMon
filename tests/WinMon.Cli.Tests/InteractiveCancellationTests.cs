using NUnit.Framework;
using Spectre.Console;
using WinMon.Cli;
using WinMon.Monitoring;

namespace WinMon.Cli.Tests;

[TestFixture]
public sealed class InteractiveCancellationTests
{
    [Test]
    public void CancelledLoopRestoresAlternateScreenAndCursorWithoutOpeningCollector()
    {
        using var output = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        { Enrichment = new() { UseDefaultEnrichers = false },
            Out = new AnsiConsoleOutput(output), Ansi = AnsiSupport.Yes,
            Interactive = InteractionSupport.Yes
        });
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var exit = InteractiveMonitor.Run(console, () => throw new AssertionException("Must not collect"),
            10, ProcessSort.Cpu, "", cancellation.Token);
        Assert.Multiple(() =>
        {
            Assert.That(exit, Is.EqualTo(130));
            Assert.That(output.ToString().Contains("\u001b[?1049l", StringComparison.Ordinal), Is.True);
            Assert.That(output.ToString().Contains("\u001b[?25h", StringComparison.Ordinal), Is.True);
        });
    }
}
