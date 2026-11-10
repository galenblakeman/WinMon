using System.CommandLine;
using System.Globalization;
using NUnit.Framework;
using Spectre.Console;
using WinMon.Cli;
using WinMon.Monitoring;

namespace WinMon.Cli.Tests;

[TestFixture]
public sealed class InteractiveTests
{
    private static MonitorSample Sample(int pid = 42) => new(DateTimeOffset.UtcNow, 25,
        [new ProcessUsage(pid, 1, "[red]alpha[/]\u001b", 2, 1024)], 0) { IntervalSeconds = 1 };

    private static void Key(InteractiveState state, ConsoleKey key, char character = '\0') =>
        state.Handle(new ConsoleKeyInfo(character, key, false, false, false), 5);

    [Test]
    public void PauseKeepsDisplayedFrameWhileNewSamplesArriveAndResumeCatchesUp()
    {
        var state = new InteractiveState(ProcessSort.Cpu, "");
        state.Observe(Sample(1), null);
        Key(state, ConsoleKey.Spacebar);
        state.Observe(Sample(2), null);
        Assert.That(state.DisplayedFrame!.Full.Processes[0].ProcessId, Is.EqualTo(1));
        Key(state, ConsoleKey.Spacebar);
        Assert.That(state.DisplayedFrame!.Full.Processes[0].ProcessId, Is.EqualTo(2));
    }

    [Test]
    public void FilterEditingDoesNotTriggerShortcutsAndSupportsCancelAndClear()
    {
        var state = new InteractiveState(ProcessSort.Cpu, "alpha");
        Key(state, ConsoleKey.Oem2, '/');
        Key(state, ConsoleKey.Q, 'q');
        Key(state, ConsoleKey.D2, '2');
        Assert.Multiple(() =>
        {
            Assert.That(state.Query, Is.EqualTo("alphaq2"));
            Assert.That(state.Quit, Is.False);
            Assert.That(state.Sort, Is.EqualTo(ProcessSort.Cpu));
        });
        Key(state, ConsoleKey.Escape);
        Assert.That(state.Query, Is.EqualTo("alpha"));
        Key(state, ConsoleKey.Oem2, '/');
        Key(state, ConsoleKey.Backspace);
        Key(state, ConsoleKey.Enter);
        Assert.That(state.Query, Is.EqualTo("alph"));
        Key(state, ConsoleKey.Escape);
        Assert.That(state.Query, Is.Empty);
        Key(state, ConsoleKey.Q);
        Assert.That(state.Quit, Is.True);
    }

    [TestCase('1', ProcessSort.Cpu)]
    [TestCase('2', ProcessSort.Memory)]
    [TestCase('3', ProcessSort.Gpu)]
    [TestCase('4', ProcessSort.Io)]
    [TestCase('5', ProcessSort.Threads)]
    [TestCase('6', ProcessSort.Handles)]
    public void SortShortcutResetsScroll(char key, ProcessSort expected)
    {
        var state = new InteractiveState(ProcessSort.Cpu, "");
        Key(state, ConsoleKey.PageDown);
        Assert.That(state.Offset, Is.EqualTo(5));
        Key(state, ConsoleKey.D1, key);
        Assert.Multiple(() =>
        {
            Assert.That(state.Sort, Is.EqualTo(expected));
            Assert.That(state.Offset, Is.Zero);
        });
    }

    [Test]
    public void ScrollClampsAfterListShrinks()
    {
        var state = new InteractiveState(ProcessSort.Cpu, "");
        Key(state, ConsoleKey.UpArrow);
        Assert.That(state.Offset, Is.Zero);
        Key(state, ConsoleKey.PageDown);
        Key(state, ConsoleKey.DownArrow);
        state.ClampOffset(8, 5);
        Assert.That(state.Offset, Is.EqualTo(3));
        state.ClampOffset(0, 5);
        Assert.That(state.Offset, Is.Zero);
    }

    [Test]
    public void FailureKeepsTimestampedLastSampleAndRecoveryClearsError()
    {
        var state = new InteractiveState(ProcessSort.Cpu, "");
        var original = Sample();
        state.Observe(original, null);
        state.Observe(null, SamplingError.ReadingsUnavailable);
        Assert.Multiple(() =>
        {
            Assert.That(state.DisplayedFrame!.Full, Is.SameAs(original));
            Assert.That(state.Error, Is.EqualTo(SamplingError.ReadingsUnavailable));
        });
        state.Observe(Sample(43), null);
        Assert.That(state.Error, Is.Null);
    }

    [Test]
    public void LayoutHandlesWarmingUpSmallTerminalsAndLiteralNames()
    {
        var state = new InteractiveState(ProcessSort.Cpu, "");
        using var output = new StringWriter(CultureInfo.InvariantCulture);
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Out = new AnsiConsoleOutput(output), Ansi = AnsiSupport.No,
            ColorSystem = ColorSystemSupport.NoColors
        });
        console.Profile.Width = 80;
        console.Write(InteractiveMonitor.BuildView(state, 10, 5, 80, 24));
        Assert.That(output.ToString(), Does.Contain("Warming up"));
        output.GetStringBuilder().Clear();
        state.Observe(Sample(), null);
        console.Write(InteractiveMonitor.BuildView(state, 10, 5, 80, 24));
        Assert.Multiple(() =>
        {
            Assert.That(output.ToString(), Does.Contain("[red]alpha[/]").And.Contain("[Q] Quit"));
            Assert.That(output.ToString().Contains('\u001b'), Is.False);
        });
        output.GetStringBuilder().Clear();
        console.Write(InteractiveMonitor.BuildView(state, 10, 5, 40, 12));
        Assert.That(output.ToString(), Does.Contain("Resize"));
    }

    [Test]
    public async Task RedirectedWatchFailsBeforeOpeningCollector()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var exit = await CliCommand.Create(() => throw new AssertionException("Must not collect"), output, error)
            .Parse(["watch"]).InvokeAsync();
        Assert.Multiple(() =>
        {
            Assert.That(exit, Is.EqualTo(1));
            Assert.That(output.ToString(), Is.Empty);
            Assert.That(error.ToString(), Does.Contain("snapshot"));
        });
    }

    [TestCase("--top", "-1")]
    [TestCase("--sort", "bad")]
    [TestCase("--format", "json")]
    public void InvalidWatchOptionsAreRejected(string option, string value) =>
        Assert.That(CliCommand.Create().Parse(["watch", option, value]).Errors, Is.Not.Empty);
}
