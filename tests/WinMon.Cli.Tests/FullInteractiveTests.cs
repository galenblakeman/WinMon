using System.CommandLine;
using NUnit.Framework;
using Spectre.Console;
using WinMon.Cli;
using WinMon.Monitoring;

namespace WinMon.Cli.Tests;

[TestFixture]
public sealed class FullInteractiveTests
{
    [Test]
    public void DetailsAreBuiltOnlyWhenOpenedAndCachedForThatObservation()
    {
        var calls = 0;
        var cellCalls = 0;
        var row = new TerminalRow("key", () => { cellCalls++; return ["Name"]; }, () => { calls++; return "Details"; });
        Assert.That(calls, Is.Zero);
        Assert.That(cellCalls, Is.Zero);
        Assert.That(row.Cells[0], Is.EqualTo("Name"));
        Assert.That(row.Cells[0], Is.EqualTo("Name"));
        Assert.That(cellCalls, Is.EqualTo(1));
        Assert.That(row.Details, Is.EqualTo("Details"));
        Assert.That(row.Details, Is.EqualTo("Details"));
        Assert.That(calls, Is.EqualTo(1));
        var missing = row with { Details = "Unavailable" };
        Assert.That(missing.Details, Is.EqualTo("Unavailable"));
        Assert.That(row.Details, Is.EqualTo("Details"));
    }

    [TestCase("Microsoft Defender Antimalware Service", 12, "Microsoft D…")]
    [TestCase("中文测试", 5, "中文…")]
    [TestCase("abcdef", 1, "…")]
    [TestCase("abcdef", 0, "")]
    [TestCase("[red]ok[/]", 20, "[red]ok[/]")]
    public void TerminalTextUsesCellWidthsAndPreservesLiteralText(string value, int width, string expected) =>
        Assert.That(TerminalText.Fit(value, width), Is.EqualTo(expected));

    private static MonitorSample Sample(params ProcessUsage[] processes) => new(DateTimeOffset.UtcNow, 25, processes, 0)
        { IntervalSeconds = 1, IntervalCpuTicks = 100_000_000 };
    private static ProcessUsage Process(int pid, long created, string path, double cpu, bool app, ulong memory = 1024) =>
        new(pid, created, "alpha", cpu, memory, 1024) { ImagePath = path, PrivateBytes = memory * 2,
            HasAppWindow = app, ThreadCount = 3, HandleCount = 4, GpuPercent = 2 };
    private static void Key(InteractiveState state, ConsoleKey key, char character = '\0') =>
        state.Handle(new ConsoleKeyInfo(character, key, false, false, false), 3);

    [Test]
    public void GroupingUsesPrivateMemoryAndMatchesAnyMemberPidWithAppOnly()
    {
        var sample = Sample(Process(10, 1, "C:\\alpha.exe", 20, false), Process(11, 2, "C:\\alpha.exe", 1, true));
        var table = ProcessesTab.Build([sample], ProcessSort.Cpu, "11", true, true, true);
        Assert.Multiple(() =>
        {
            Assert.That(table.Rows.Count, Is.EqualTo(1));
            Assert.That(table.Rows[0].Process!.PrivateBytes, Is.EqualTo(4096));
            Assert.That(table.Rows[0].Process!.CpuPercent, Is.EqualTo(21));
            Assert.That(table.Rows[0].Cells[1], Is.EqualTo("×2"));
            Assert.That(table.Rows[0].Details, Does.Contain("10, 11"));
            Assert.That(table.Rows[0].Details, Does.Contain("C:\\alpha.exe"));
        });
    }

    [Test]
    public void FiniteTopOmitsIdleRowsWhileAllIncludesThemAndSnapshotZeroMeansAll()
    {
        var sample = Sample(Process(1, 1, "active", 10, false), Process(2, 2, "idle", 0, false));
        var state = new InteractiveState(ProcessSort.Cpu, "");
        state.Configure(10, false, false);
        state.Observe(sample, null, [sample]);
        Assert.That(InteractiveMonitor.Query(state).Rows.Count, Is.EqualTo(1));
        state.Active.Top = 0;
        Assert.That(InteractiveMonitor.Query(state).Rows.Count, Is.EqualTo(2));
        Assert.That(SnapshotReport.Create(new CapturedFrame(sample), "test", 0, ProcessSort.Cpu, "").Processes.Count, Is.EqualTo(2));
    }

    [Test]
    public void PinUsesLifetimeIdentityAndDoesNotAttachToReusedPid()
    {
        var state = new InteractiveState(ProcessSort.Cpu, "");
        var first = Sample(Process(42, 1, "first", 10, true));
        state.Observe(first, null, [first]);
        state.Present(InteractiveMonitor.Query(state), 3);
        Key(state, ConsoleKey.P);
        Key(state, ConsoleKey.N);
        var reused = Sample(Process(42, 2, "replacement", 20, true));
        state.Observe(reused, null, [reused]);
        var table = state.Present(InteractiveMonitor.Query(state), 3);
        Assert.Multiple(() =>
        {
            Assert.That(table.Rows.Count, Is.EqualTo(1));
            Assert.That(table.Rows[0].Process!.CreationTicks, Is.EqualTo(1));
            Assert.That(table.Rows[0].Process!.ObservedCpuPercent, Is.Null);
        });
    }

    [Test]
    public void SelectionFollowsIdentityThroughRankChangesAndDetailsCanClose()
    {
        var state = new InteractiveState(ProcessSort.Cpu, "");
        var a = new TerminalRow("a", ["Alpha"], "Alpha details");
        var b = new TerminalRow("b", ["Beta"], "Beta details");
        state.Present(new TerminalTable(["Name"], [a, b]), 3);
        Key(state, ConsoleKey.DownArrow);
        Assert.That(state.Selected!.Key, Is.EqualTo("b"));
        state.Present(new TerminalTable(["Name"], [b, a]), 3);
        Assert.That(state.Selected!.Key, Is.EqualTo("b"));
        Key(state, ConsoleKey.Enter);
        Assert.That(state.Active.Details, Is.True);
        Key(state, ConsoleKey.RightArrow);
        Assert.That(state.Active.HorizontalOffset, Is.EqualTo(16));
        Key(state, ConsoleKey.Escape);
        Assert.That(state.Active.Details, Is.False);
    }

    [Test]
    public void StartupFilterBelongsOnlyToSelectedTab()
    {
        var state = new InteractiveState(ProcessSort.Cpu, "chrome");
        state.Configure(0, false, false, TerminalTab.Connections);
        Assert.That(state.Filter, Is.EqualTo("chrome"));
        Key(state, ConsoleKey.F1);
        Assert.That(state.Filter, Is.Empty);
        Key(state, ConsoleKey.F3);
        Assert.That(state.Filter, Is.EqualTo("chrome"));
    }

    [Test]
    public void FunctionKeysFollowDesktopVisualTabOrder()
    {
        var state = new InteractiveState(ProcessSort.Cpu, "");
        Key(state, ConsoleKey.F3);
        Assert.That(state.Tab, Is.EqualTo(TerminalTab.Connections));
        Key(state, ConsoleKey.F4);
        Assert.That(state.Tab, Is.EqualTo(TerminalTab.Usage));
    }

    [Test]
    public void EachTabPreservesItsFilterSortAndTopAndEditingDoesNotNavigate()
    {
        var state = new InteractiveState(ProcessSort.Cpu, "process");
        state.Configure(0, true, true);
        Key(state, ConsoleKey.F2);
        Key(state, ConsoleKey.D4, '4');
        state.Active.Filter = "service";
        Key(state, ConsoleKey.T);
        Assert.That(state.Active.Sort, Is.Zero, "Services name sorting switches to CPU for finite Top");
        Key(state, ConsoleKey.F1);
        Assert.Multiple(() =>
        {
            Assert.That(state.Filter, Is.EqualTo("process"));
            Assert.That(state.Active.Grouped, Is.True);
            Assert.That(state.Active.AppsOnly, Is.True);
            Assert.That(state.Active.Top, Is.Zero);
        });
        Key(state, ConsoleKey.Oem2, '/');
        Key(state, ConsoleKey.F4);
        Assert.That(state.Tab, Is.EqualTo(TerminalTab.Processes));
        Key(state, ConsoleKey.Escape);
        Key(state, ConsoleKey.F2);
        Assert.That(state.Filter, Is.EqualTo("service"));
    }

    [Test]
    public void PauseAlsoFreezesSamplingAndTrafficHealth()
    {
        var state = new InteractiveState(ProcessSort.Cpu, "") { NetworkStatus = NetworkTrafficState.Active };
        state.Observe(Sample(), null);
        Key(state, ConsoleKey.Spacebar);
        state.Observe(null, SamplingError.ReadingsUnavailable);
        state.NetworkStatus = NetworkTrafficState.Unavailable;
        Assert.Multiple(() =>
        {
            Assert.That(state.Error, Is.Null);
            Assert.That(state.NetworkStatus, Is.EqualTo(NetworkTrafficState.Active));
            Assert.That(state.LiveNetworkStatus, Is.EqualTo(NetworkTrafficState.Unavailable));
        });
        Key(state, ConsoleKey.Spacebar);
        Assert.That(state.Error, Is.EqualTo(SamplingError.ReadingsUnavailable));
        Assert.That(state.NetworkStatus, Is.EqualTo(NetworkTrafficState.Unavailable));
    }

    [Test]
    public void PauseRetainsUsageAndServicesWhileSessionContinues()
    {
        var session = new MonitorSession(() => throw new AssertionException("No native source"), () => new SamplingOptions(), a => a());
        var first = Sample(Process(1, 1, "alpha", 10, true));
        session.Record(first, first, new ServiceFrame(first, []));
        var state = new InteractiveState(ProcessSort.Cpu, "");
        state.ObserveSession(session);
        Key(state, ConsoleKey.Spacebar);
        var frozen = state.Observations;
        var next = Sample(Process(2, 2, "beta", 20, true));
        session.Record(next, next, new ServiceFrame(next, []));
        state.ObserveSession(session);
        Assert.That(state.Observations, Is.SameAs(frozen));
        Assert.That(state.Observations.Services[^1].Sample, Is.SameAs(first));
        Key(state, ConsoleKey.Spacebar);
        Assert.That(state.Observations.Services[^1].Sample, Is.SameAs(next));
    }

    [TestCase("usage", "cpu-time", "0", true)]
    [TestCase("services", "name", "0", true)]
    [TestCase("services", "name", "10", false)]
    [TestCase("connections", "received", "0", true)]
    [TestCase("usage", "tcp", "0", false)]
    public void StartupSortMatchesChosenTab(string tab, string sort, string top, bool valid) =>
        Assert.That(CliCommand.Create().Parse(["watch", "--tab", tab, "--sort", sort, "--top", top]).Errors.Count == 0, Is.EqualTo(valid));

    [TestCase(80)]
    [TestCase(140)]
    public void EveryTabRendersAndDetailsStayLiteral(int width)
    {
        var session = new MonitorSession(() => throw new AssertionException("No native source"), () => new SamplingOptions(), a => a());
        var sample = Sample(Process(42, 1, "C:\\[red]name.exe", 10, true));
        session.Record(sample, sample, new ServiceFrame(sample, []));
        var state = new InteractiveState(ProcessSort.Cpu, "");
        state.ObserveSession(session);
        using var output = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings { Out = new AnsiConsoleOutput(output), Ansi = AnsiSupport.No });
        console.Profile.Width = width;
        foreach (var key in new[] { ConsoleKey.F1, ConsoleKey.F2, ConsoleKey.F3, ConsoleKey.F4 })
        {
            Key(state, key);
            console.Write(InteractiveMonitor.BuildView(state, 0, 3, width, 24));
        }
        Assert.That(output.ToString().Contains('\u001b'), Is.False);
        Assert.That(output.ToString(), Does.Contain("F1").And.Contain("F4"));
    }

    [Test]
    public void DisabledTrafficStatusIsShownOnceForAnAvailableEndpointFrame()
    {
        var session = new MonitorSession(() => throw new AssertionException("No native source"), () => new SamplingOptions(), a => a());
        var sample = Sample();
        session.Record(sample, sample, connections: new NetworkConnectionsFrame([], NetworkAddressFamilyStatus.Success, NetworkAddressFamilyStatus.Success));
        var state = new InteractiveState(ProcessSort.Cpu, "");
        state.Configure(0, false, false, TerminalTab.Connections);
        state.ObserveSession(session);
        using var output = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings { Out = new AnsiConsoleOutput(output), Ansi = AnsiSupport.No });
        console.Profile.Width = 120;
        console.Write(InteractiveMonitor.BuildView(state, 0, 3, 120, 24));
        var label = WinMon.Resources.Strings.NetworkProcessMonitoringDisabled;
        Assert.That(output.ToString().Split(label, StringSplitOptions.None).Length - 1, Is.EqualTo(1));
    }

    [Test]
    public void UnavailableTrafficShowsSetupCommandsAtMinimumWidth()
    {
        var state = new InteractiveState(ProcessSort.Cpu, "");
        state.Configure(0, false, false, TerminalTab.Connections);
        state.Observe(Sample(), null);
        state.NetworkEnabled = true;
        state.NetworkStatus = NetworkTrafficState.Unavailable;
        using var output = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings { Out = new AnsiConsoleOutput(output), Ansi = AnsiSupport.No });
        console.Profile.Width = 80;
        console.Write(InteractiveMonitor.BuildView(state, 0, 3, 80, 24));
        Assert.That(output.ToString(), Does.Contain("winmon-cli network start").And.Contain("winmon-cli network install"));
    }

    [Test]
    public void LongNamesDoNotWrapRowsOrHideNavigation()
    {
        var state = new InteractiveState(ProcessSort.Cpu, "");
        var processes = Enumerable.Range(1, 50).Select(i => Process(i, i, "app" + i, 10, true) with
        { AppName = "Microsoft Defender Antimalware Service with a very long name " + i }).ToArray();
        var sample = Sample(processes);
        state.Observe(sample, null, [sample]);
        using var output = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings { Out = new AnsiConsoleOutput(output), Ansi = AnsiSupport.No });
        console.Profile.Width = 80;
        console.Write(InteractiveMonitor.BuildView(state, 0, 7, 80, 24));
        var lines = output.ToString().Split('\n');
        Assert.Multiple(() =>
        {
            Assert.That(lines.Length, Is.LessThanOrEqualTo(25));
            Assert.That(output.ToString(), Does.Contain("[Q] Quit"));
            Assert.That(output.ToString(), Does.Not.Contain("very long name"));
        });
    }
}
