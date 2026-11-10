using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Spectre.Console;
using WinMon.Monitoring;

namespace WinMon.Cli.Tests;

[TestFixture]
public sealed class TerminalColorTests
{
    [Test]
    public void HeaderIncludesTheAssemblyReleaseVersion()
    {
        var version = typeof(InteractiveMonitor).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;
        Assert.That(Plain(Render(ColorSystemSupport.NoColors)), Does.StartWith($"WinMon {version} · "));
    }

    [TestCase(80, 20)]
    [TestCase(80, 24)]
    [TestCase(120, 36)]
    public void SummaryUsesReferenceResourceColorsAndKeepsIdenticalNoColorText(int width, int height)
    {
        var colored = RenderSummary(width, height, ColorSystemSupport.TrueColor);
        var plain = RenderSummary(width, height, ColorSystemSupport.NoColors);
        Assert.Multiple(() =>
        {
            AssertColor("253;108;15", "42.0%");
            AssertColor("17;149;253", height >= 24 ? "50.0%" : "16.0 / 32 GiB");
            AssertColor("252;234;29", "10.0%");
            AssertColor("220;119;251", "12.8 MB/s");
            AssertColor("0;242;195", "76.0%");
            Assert.That(Plain(colored), Is.EqualTo(Plain(plain)));
            Assert.That(plain, Does.Not.Contain("38;"));
            Assert.That(plain, Does.Not.Contain("10.2%"));
            if (width >= 120 && height >= 32)
            {
                AssertColor("253;108;15", "CPU");
                AssertColor("17;149;253", "Memory");
                AssertColor("252;234;29", "Disk (C:)");
                AssertColor("220;119;251", "Network");
                AssertColor("0;242;195", "GPU");
            }
        });
        return;

        void AssertColor(string rgb, string text) => Assert.That(colored,
            Does.Match(Regex.Escape("\u001b[38;2;" + rgb + "m") + "[^\u001b]*" + Regex.Escape(text)));
    }

    [TestCase(ColorSystemSupport.Standard)]
    [TestCase(ColorSystemSupport.EightBit)]
    public void SummaryAdaptsToLimitedColorWithoutChangingText(ColorSystemSupport colors)
    {
        var rendered = RenderSummary(120, 36, colors);
        Assert.That(rendered, Does.Not.Contain("38;2;"));
        Assert.That(Plain(rendered), Is.EqualTo(Plain(RenderSummary(120, 36, ColorSystemSupport.NoColors))));
    }

    private static string RenderSummary(int width, int height, ColorSystemSupport colors)
    {
        var sample = new MonitorSample(DateTimeOffset.UnixEpoch, 42, [], 0)
        {
            Resources = new ResourceSample { TotalMemoryBytes = 32UL * 1024 * 1024 * 1024,
                UsedMemoryBytes = 16UL * 1024 * 1024 * 1024, GpuPercent = 76, DiskActivePercent = 10,
                DiskName = "C:", NetworkName = "Ethernet", LinkBitsPerSecond = 1_000_000_000,
                ReceiveBytesPerSecond = 12_800_000, SendBytesPerSecond = 1_400_000 }
        };
        using var output = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        { Enrichment = new() { UseDefaultEnrichers = false }, Out = new AnsiConsoleOutput(output), Ansi = AnsiSupport.Yes, ColorSystem = colors });
        console.Profile.Width = width;
        console.Write(TerminalCharts.Summary(sample, [sample], width, height, true));
        return output.ToString();
    }

    [Test]
    public void ColorAndMonochromePreserveTheSameControlsAndLiteralProcessText()
    {
        var colored = Render(ColorSystemSupport.TrueColor);
        var monochrome = Render(ColorSystemSupport.NoColors);
        var codes = Regex.Matches(colored, "\u001b\\[([0-9;]*)m")
            .SelectMany(match => match.Groups[1].Value.Split(';')).ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(colored, Does.Contain("38;2;135;149;165"));
            Assert.That(colored, Does.Contain("38;2;0;216;219"));
            Assert.That(colored, Does.Contain("\u001b[38;2;0;216;219m[Q]"));
            Assert.That(colored, Does.Match("\u001b\\[[0-9;]*38;2;0;216;219[0-9;]*m\\[F1\\]"));
            Assert.That(colored, Does.Contain("48;2;38;79;120"));
            Assert.That(colored, Does.Contain("38;2;230;237;243"));
            Assert.That(codes, Does.Contain("1"));
            Assert.That(colored, Does.Contain("38;2;63;185;80"));
            Assert.That(Regex.Replace(colored, "\u001b\\[[0-9;]*m", ""),
                Is.EqualTo(Regex.Replace(monochrome, "\u001b\\[[0-9;]*m", "")));
            Assert.That(Regex.Matches(monochrome, "\u001b\\[([0-9;]*)m")
                .SelectMany(match => match.Groups[1].Value.Split(';')),
                Has.None.Matches<string>(code => code is "31" or "32" or "33" or "36" or "38" or "48" or "96"));
            Assert.That(Plain(monochrome), Does.Contain("[Q] Quit").And.Contain("[?] Help")
                .And.Contain("2 Memory").And.Contain("↓1 CPU").And.Contain("[red]literal"));
        });
    }

    [Test]
    public void SelectionFillsTheWholeRow()
    {
        var row = Render(ColorSystemSupport.TrueColor).Split('\n').Single(line => line.Contains("literal"));
        var unlit = Regex.Replace(row, "\u001b\\[[0-9;]*48;2;38;79;120[0-9;]*m[^\u001b]*", "");
        Assert.That(Plain(unlit).Trim(), Is.Empty);
    }

    [TestCase(TerminalTab.Services, "4 Service")]
    [TestCase(TerminalTab.Connections, "↓3 TCP")]
    [TestCase(TerminalTab.Usage, "↓1 CPU")]
    public void ColumnHeadersCarryTheCurrentTabsSortKeys(TerminalTab tab, string expected)
    {
        Assert.That(Plain(Render(ColorSystemSupport.NoColors, tab)), Does.Contain(expected));
    }

    [TestCase(TerminalTab.Processes)]
    [TestCase(TerminalTab.Services)]
    [TestCase(TerminalTab.Connections)]
    [TestCase(TerminalTab.Usage)]
    public void MonochromeTabsMarkOnlyTheActiveTab(TerminalTab tab)
    {
        var tabs = Plain(Render(ColorSystemSupport.NoColors, tab)).Split('\n')[1];
        Assert.That(tabs, Does.Contain($"> [F{(int)tab + 1}]"));
        Assert.That(tabs.Count(character => character == '>'), Is.EqualTo(1));
    }

    [TestCase(ColorSystemSupport.Standard)]
    [TestCase(ColorSystemSupport.EightBit)]
    public void LimitedColorTerminalsKeepTheSameTextWithoutTrueColorEscapes(ColorSystemSupport colors)
    {
        var rendered = Render(colors);
        Assert.That(Plain(rendered), Is.EqualTo(Plain(Render(ColorSystemSupport.NoColors))));
        Assert.That(rendered, Does.Not.Contain("38;2;").And.Not.Contain("48;2;"));
    }

    [Test]
    public void StyledHelpPreservesSeparateLines()
    {
        var state = new InteractiveState(ProcessSort.Cpu, "");
        state.Handle(new ConsoleKeyInfo('?', ConsoleKey.Oem2, true, false, false), 5);
        using var output = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings { Enrichment = new() { UseDefaultEnrichers = false }, Out = new AnsiConsoleOutput(output), Ansi = AnsiSupport.No });
        console.Profile.Width = 100;
        console.Write(InteractiveMonitor.BuildView(state, 0, 5, 100, 24));
        Assert.That(output.ToString().Split('\n').Length, Is.GreaterThan(8));
        Assert.That(output.ToString(), Does.Contain("[Esc]").And.Contain("[Q]"));
    }

    [TestCase('2', "Memory")]
    [TestCase('3', "GPU")]
    public void ProcessSortKeysMatchTheMemoryAndGpuColumns(char key, string label)
    {
        var text = Plain(Render(ColorSystemSupport.NoColors, sortKey: key));
        Assert.That(text, Does.Contain($"↓{key} {label}"));
    }

    private static string Render(ColorSystemSupport colors, TerminalTab tab = TerminalTab.Processes, char? sortKey = null)
    {
        var state = new InteractiveState(ProcessSort.Cpu, "");
        state.Configure(0, false, false, tab);
        if (sortKey is { } key) state.Handle(new ConsoleKeyInfo(key, ConsoleKey.D1, false, false, false), 5);
        state.Observe(new MonitorSample(DateTimeOffset.UnixEpoch, 10,
            [new ProcessUsage(42, 1, "[red]literal", 10, 1024)], 0), null);
        using var output = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        { Enrichment = new() { UseDefaultEnrichers = false }, Out = new AnsiConsoleOutput(output), Ansi = AnsiSupport.Yes, ColorSystem = colors });
        console.Profile.Width = 140;
        console.Write(InteractiveMonitor.BuildView(state, 0, 5, 140, 24));
        return output.ToString();
    }

    private static string Plain(string text) => Regex.Replace(text, "\u001b\\[[0-9;]*m", "");
}
