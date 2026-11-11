using System.Globalization;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Spectre.Console;
using Spectre.Console.Rendering;
using WinMon.Monitoring;
using WinMon.Resources;

namespace WinMon.Cli.Tests;

[TestFixture]
public sealed class TerminalChartTests
{
    private static MonitorSample Sample(int second, double cpu = 20) => new(DateTimeOffset.UnixEpoch.AddSeconds(second), cpu,
        [new ProcessUsage(42, 1, "spiky", cpu, (ulong)(second + 1) * 1024, second * 1000)
        { GpuPercent = second, ThreadCount = second + 2, HandleCount = second + 3 }], 0)
    {
        IntervalSeconds = 1,
        Resources = new ResourceSample { TotalMemoryBytes = 32UL * 1024 * 1024 * 1024,
            UsedMemoryBytes = 16UL * 1024 * 1024 * 1024, CpuClockMhz = 4300,
            GpuPercent = 76, DiskActivePercent = 10, DiskName = "C:", DiskBytesPerSecond = 200_000,
            NetworkId = "nic-1", NetworkName = "Ethernet", LinkBitsPerSecond = 1_000_000_000,
            ReceiveBytesPerSecond = 12_500_000, SendBytesPerSecond = 1_000_000 }
    };

    private static InteractiveState State()
    {
        var history = Enumerable.Range(0, 61).Select(i => Sample(i, i % 7 * 10)).ToArray();
        var state = new InteractiveState(ProcessSort.Cpu, "");
        state.Observe(history[^1], null, history);
        return state;
    }

    private static string Render(InteractiveState state, int width, int height, bool unicode = true)
    {
        using var output = new StringWriter(CultureInfo.InvariantCulture);
        var console = AnsiConsole.Create(new AnsiConsoleSettings { Enrichment = new() { UseDefaultEnrichers = false }, Out = new AnsiConsoleOutput(output),
            Ansi = AnsiSupport.No, ColorSystem = ColorSystemSupport.NoColors });
        console.Profile.Width = width;
        console.Write(InteractiveMonitor.BuildView(state, 0, 100, width, height, unicode));
        return output.ToString();
    }

    [TestCase(80, 20)]
    [TestCase(80, 24)]
    [TestCase(120, 36)]
    public void SummaryKeepsFiveWideGaugesAndTheCompactResourceOrder(int width, int height)
    {
        var text = Render(State(), width, height);
        var cpu = text.IndexOf(Strings.CPU, StringComparison.Ordinal);
        var memory = text.IndexOf(Strings.Memory, StringComparison.Ordinal);
        var network = text.IndexOf(Strings.Network, StringComparison.Ordinal);
        var gpu = text.IndexOf(Strings.GPU, StringComparison.Ordinal);
        var disk = text.IndexOf(Strings.Disk, StringComparison.Ordinal);
        Assert.Multiple(() =>
        {
            Assert.That(cpu, Is.GreaterThanOrEqualTo(0));
            Assert.That(memory, Is.GreaterThan(cpu));
            Assert.That(network, Is.GreaterThan(memory));
            Assert.That(gpu, Is.GreaterThan(network));
            if (height >= 24)
            {
                Assert.That(disk, Is.GreaterThan(memory));
                Assert.That(network, Is.GreaterThan(disk));
            }
            else Assert.That(disk, Is.GreaterThan(gpu));
        });
    }

    [TestCase(true)]
    [TestCase(false)]
    [SetCulture("en-US")]
    public void CompactSummaryKeepsFiveGaugesWithTheirDetails(bool unicode)
    {
        var sample = Sample(60) with { Resources = Sample(60).Resources with
        {
            GpuMemoryUsedBytes = 2UL * 1024 * 1024 * 1024,
            GpuMemoryTotalBytes = 8UL * 1024 * 1024 * 1024,
            DiskReadBytesPerSecond = 279_400, DiskWriteBytesPerSecond = 998_500,
            NetworkName = new string('N', 100)
        } };
        var state = new InteractiveState(ProcessSort.Cpu, "");
        state.Observe(sample, null);
        var lines = Render(state, 80, 24, unicode).Split('\n').Skip(2).Take(5)
            .Select(line => line.TrimEnd('\r')).ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(lines[0], Does.StartWith(Strings.CPU).And.Contain("4.30 GHz"));
            Assert.That(lines[1], Does.StartWith(Strings.Memory).And.Contain("50.0%").And.Contain("16.0 / 32 GiB"));
            Assert.That(lines[2], Does.StartWith(Strings.Disk).And.Contain(SnapshotReport.Rate(279_400))
                .And.Contain(SnapshotReport.Rate(998_500)));
            Assert.That(lines[3], Does.StartWith(Strings.Network).And.Contain("12.5 MB/s").And.Contain("1 MB/s"));
            Assert.That(lines[4], Does.StartWith(Strings.GPU).And.Contain("76.0%")
                .And.Contain(WinMon.Controls.GaugeReadoutText.GpuMemory(sample.Resources.GpuMemoryUsedBytes, sample.Resources.GpuMemoryTotalBytes)));
            Assert.That(lines.All(line => Regex.IsMatch(line, unicode ? @"\[[█░]{10}\]" : @"\[[#-]{10}\]")), Is.True);
            Assert.That(lines.Select(line => line.IndexOf('[')).Distinct().Count(), Is.EqualTo(1));
            Assert.That(lines.All(line => new Segment(line).CellCount() <= 80), Is.True);
        });
    }

    [Test]
    public void ResourceHistoryDoesNotMixAdaptersOrVolumesAndDownloadDoesNotRequireLinkSpeed()
    {
        var first = Sample(0);
        var last = Sample(60) with { Resources = first.Resources with
            { NetworkId = "nic-2", ReceiveBytesPerSecond = 0, SendBytesPerSecond = 0,
                DiskName = "D:", DiskActivePercent = 0 } };
        var state = new InteractiveState(ProcessSort.Cpu, "");
        state.Observe(last, null, [first, last]);
        var lines = Render(state, 120, 36).Split('\n');
        var start = Array.FindIndex(lines, line => line.StartsWith("╭", StringComparison.Ordinal));
        Assert.That(lines[start + 4].Split('│')[5].Trim(), Is.EqualTo("─"));
        Assert.That(lines[start + 4].Split('│')[7].Trim(), Is.EqualTo("─"));

        var unknown = last with { Resources = last.Resources with { LinkBitsPerSecond = null } };
        state.Observe(unknown, null);
        lines = Render(state, 120, 36).Split('\n');
        Assert.That(lines[start + 1].Split('│')[7].Trim(), Is.EqualTo("↓0 KB/s"));

        unknown = unknown with { Resources = unknown.Resources with { ReceiveBytesPerSecond = null } };
        state.Observe(unknown, null);
        lines = Render(state, 120, 36).Split('\n');
        Assert.That(lines[start + 1].Split('│')[7].Trim(), Is.EqualTo(Strings.MissingReading));
    }

    [TestCase(24)]
    [TestCase(36)]
    public void FivePanelsFit120ColumnsWithoutAddingHeight(int height)
    {
        var lines = Render(State(), 120, height).Split('\n');
        var start = Array.FindIndex(lines, line => line.StartsWith("╭", StringComparison.Ordinal));
        Assert.Multiple(() =>
        {
            Assert.That(lines[start].Count(c => c == '╭'), Is.EqualTo(5));
            Assert.That(new Segment(lines[start].TrimEnd('\r')).CellCount(), Is.EqualTo(119));
            Assert.That(lines[start + 2].Split('│').Where((_, index) => index % 2 == 1)
                .All(cell => cell.Trim().Length == 19), Is.True);
            Assert.That(lines[start + 7], Does.StartWith("[B]").And.Contain("Network"));
        });
    }

    [TestCase(119, 24)]
    [TestCase(120, 23)]
    public void PanelsRequireBoth120ColumnsAnd24Rows(int width, int height)
    {
        Assert.That(Render(State(), width, height), Does.Not.Contain("╭"));
    }

    [Test]
    [SetCulture("en-US")]
    public void WideReadoutsUseDesktopPrimaryUnitsAndSecondaryValues()
    {
        var lines = Render(State(), 120, 36).Split('\n');
        var start = Array.FindIndex(lines, line => line.StartsWith("╭", StringComparison.Ordinal));
        var primary = lines[start + 1].Split('│').Where((_, index) => index % 2 == 1).Select(cell => cell.Trim()).ToArray();
        var secondary = lines[start + 3].Split('│').Where((_, index) => index % 2 == 1).Select(cell => cell.Trim()).ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(primary, Is.EqualTo(new[] { "40.0%", "50.0%", "10.0%", "↓12.5 MB/s", "76.0%" }));
            Assert.That(secondary, Is.EqualTo(new[] { "4.30 GHz", "16.0 / 32 GiB", "0.2 MB/s", "↑1 MB/s", "3D" }));
            Assert.That(lines[start + 5], Does.Contain("60s · 0–12.5MB/s"));
        });
    }

    [Test]
    public void NetworkGaugeUsesUploadAwareRateScaleWithoutRequiringLinkSpeed()
    {
        var sample = Sample(60) with { Resources = Sample(60).Resources with
            { LinkBitsPerSecond = null, ReceiveBytesPerSecond = 125_000, SendBytesPerSecond = 375_000 } };
        var state = new InteractiveState(ProcessSort.Cpu, "");
        state.Observe(sample, null);
        var lines = Render(state, 120, 36).Split('\n');
        var start = Array.FindIndex(lines, line => line.StartsWith("╭", StringComparison.Ordinal));
        Assert.Multiple(() =>
        {
            Assert.That(lines[start + 1].Split('│')[7].Trim(), Is.EqualTo("↓125 KB/s"));
            Assert.That(lines[start + 3].Split('│')[7].Trim(), Is.EqualTo("↑375 KB/s"));
            Assert.That(lines[start + 5].Split('│')[7].Trim(), Does.Contain("625KB/s"));
            Assert.That(lines[start + 2].Split('│')[7].Count(c => c == '█'), Is.EqualTo(3));
        });
    }

    [TestCase(-1d)]
    [TestCase(double.NaN)]
    [TestCase(double.PositiveInfinity)]
    public void InvalidDownloadRatesRemainUnavailable(double rate)
    {
        var sample = Sample(60) with { Resources = Sample(60).Resources with { ReceiveBytesPerSecond = rate } };
        var state = new InteractiveState(ProcessSort.Cpu, "");
        state.Observe(sample, null);
        var lines = Render(state, 120, 36).Split('\n');
        var start = Array.FindIndex(lines, line => line.StartsWith("╭", StringComparison.Ordinal));
        Assert.Multiple(() =>
        {
            Assert.That(lines[start + 1].Split('│')[7].Trim(), Is.EqualTo(Strings.MissingReading));
            Assert.That(lines[start + 2].Split('│')[7].Trim(), Is.EqualTo(Strings.Unavailable));
        });
    }

    [Test]
    public void GaugeKeepsZeroFullAndUnavailableDistinctAndSupportsAscii()
    {
        Assert.Multiple(() =>
        {
            Assert.That(TerminalCharts.Gauge(0, 10, true), Is.EqualTo("░░░░░░░░░░"));
            Assert.That(TerminalCharts.Gauge(100, 10, true), Is.EqualTo("██████████"));
            Assert.That(TerminalCharts.Gauge(50, 10, false), Is.EqualTo("#####-----"));
            Assert.That(TerminalCharts.Gauge(null, 30, true), Is.EqualTo(Strings.Unavailable));
            Assert.That(TerminalCharts.Gauge(double.NaN, 30, true), Is.EqualTo(Strings.Unavailable));
        });
    }

    [Test]
    public void SparklineKeepsTimeBucketsGapsAndZeroWithoutStretchingStartup()
    {
        var history = Enumerable.Range(0, 61).Select(i => Sample(i)).ToArray();
        var values = Enumerable.Repeat<double?>(0, 61).ToArray();
        values[30] = null;
        var text = TerminalCharts.Sparkline(history, values, 100, 24, true);
        Assert.Multiple(() =>
        {
            Assert.That(text[0], Is.EqualTo('─'));
            Assert.That(text[12], Is.EqualTo(' '));
            Assert.That(text[^1], Is.EqualTo('─'));
            Assert.That(TerminalCharts.Sparkline([history[^1]], [100d], 100, 24, true), Is.EqualTo(new string(' ', 23) + "█"));
            Assert.That(TerminalCharts.Sparkline([history[^1]], [100d], 100, 24, false), Is.EqualTo(new string(' ', 23) + "@"));
        });
        history[20] = history[20] with { IsGap = true };
        Assert.That(TerminalCharts.Sparkline(history, values, 100, 24, true)[8], Is.EqualTo(' '));
    }

    [Test]
    public void SparklineAveragesWithinBucketsAndLeavesUnrecordedTimeBlank()
    {
        var history = new[] { Sample(0), Sample(1), Sample(60) };
        var text = TerminalCharts.Sparkline(history, [0d, 100d, 100d], 100, 24, true);
        Assert.Multiple(() =>
        {
            Assert.That(text[0], Is.EqualTo('▄'));
            Assert.That(text[1..^1], Is.EqualTo(new string(' ', 22)));
            Assert.That(text[^1], Is.EqualTo('█'));
        });
    }

    [TestCase(80, 20)]
    [TestCase(80, 24)]
    [TestCase(120, 36)]
    [TestCase(160, 45)]
    public void LayoutFitsAndKeepsFooterAndSortVisible(int width, int height)
    {
        var state = State();
        var p = state.DisplayedFrame!.Full.Processes[0];
        var full = Sample(60) with { Processes = Enumerable.Range(1, 100).Select(i => p with { ProcessId = i }).ToArray() };
        state.Observe(full, null);
        var rendered = Render(state, width, height);
        Assert.Multiple(() =>
        {
            Assert.That(rendered.Split('\n').Length, Is.LessThanOrEqualTo(height + 1));
            Assert.That(rendered.Split('\n').All(line => new Segment(line.TrimEnd('\r')).CellCount() <= width), Is.True);
            Assert.That(rendered, Does.Contain("[Q] Quit").And.Contain("↓1 CPU"));
            Assert.That(rendered, Does.Not.Contain(Strings.History).And.Not.Contain("older → newer"));
            Assert.That(rendered.Contains('░'), Is.EqualTo(height >= 24));
        });
    }

    [TestCase('1', ProcessSort.Cpu)]
    [TestCase('2', ProcessSort.Memory)]
    [TestCase('3', ProcessSort.Gpu)]
    [TestCase('4', ProcessSort.Io)]
    [TestCase('5', ProcessSort.Threads)]
    [TestCase('6', ProcessSort.Handles)]
    public void ProcessSortsKeepNumericColumnsWithoutHistory(char key, ProcessSort sort)
    {
        var state = State();
        state.Handle(new ConsoleKeyInfo(key, ConsoleKey.D1, false, false, false), 10);
        var text = Render(state, 120, 36);
        Assert.Multiple(() =>
        {
            Assert.That(text, Does.Not.Contain(Strings.History).And.Not.Contain("older → newer"));
            Assert.That(text, Does.Contain("CPU %").And.Contain("GPU %").And.Contain(Strings.Threads));
            Assert.That(state.Sort, Is.EqualTo(sort));
        });
    }

    [Test]
    public void UnavailableTableMetricsUseDashesWhileGaugeLabelsRemainUnchanged()
    {
        var sample = Sample(60);
        sample = sample with { Processes = [sample.Processes[0] with { GpuPercent = null }],
            Resources = sample.Resources with { GpuPercent = null } };
        var state = new InteractiveState(ProcessSort.Cpu, "");
        state.Observe(sample, null, [sample]);
        var text = Render(state, 120, 36);
        var row = text.Split('\n').Single(line => line.Contains("spiky", StringComparison.Ordinal));
        Assert.Multiple(() =>
        {
            Assert.That(row, Does.Contain(Strings.MissingReading).And.Not.Contain(Strings.Unavailable));
            Assert.That(text, Does.Contain(Strings.Unavailable));
            Assert.That(InteractiveMonitor.Query(state).Rows[0].Cells[4], Is.EqualTo(Strings.Unavailable));
        });
    }

    [TestCase(80)]
    [TestCase(140)]
    public void NumericTableValuesShareTheirColumnsRightEdge(int width)
    {
        var sample = Sample(60);
        sample = sample with { Processes = [sample.Processes[0],
            sample.Processes[0] with { ProcessId = 7, Name = "small", CpuPercent = 2,
                WorkingSetBytes = 1024, GpuPercent = 1, IoBytesPerSecond = 100,
                ThreadCount = 3, HandleCount = 4 }] };
        var state = new InteractiveState(ProcessSort.Cpu, "");
        state.Observe(sample, null, [sample]);
        var lines = Render(state, width, 36).Split('\n');
        var first = lines.Single(line => line.Contains("spiky", StringComparison.Ordinal));
        var second = lines.Single(line => line.Contains("small", StringComparison.Ordinal));
        var rows = InteractiveMonitor.Query(state).Rows;
        var visibleColumns = Math.Min(rows[0].Cells.Length - 1, (width - 32) / 13);
        for (var index = 1; index <= visibleColumns; index++)
        {
            var firstValue = rows[0].Cells[index];
            var secondValue = rows[1].Cells[index];
            Assert.That(first.IndexOf(firstValue, StringComparison.Ordinal) + firstValue.Length,
                Is.EqualTo(second.IndexOf(secondValue, StringComparison.Ordinal) + secondValue.Length),
                $"Column {index} should align its values on the right");
        }
    }

    [TestCase(80, 20, true)]
    [TestCase(80, 24, true)]
    [TestCase(120, 36, true)]
    [TestCase(140, 36, false)]
    public void HiddenColumnsHaveAVisibleHintWithoutAddingRows(int width, int height, bool hidden)
    {
        var state = State();
        state.Active.Filter = new string('a', 128);
        var text = Render(state, width, height);
        Assert.That(text.Contains(Strings.CliColumnsHint, StringComparison.Ordinal), Is.EqualTo(hidden));
        Assert.That(text.Split('\n').Length, Is.LessThanOrEqualTo(height + 1));
        Assert.That(text, Does.Contain("[Q] Quit"));
        state.Handle(new ConsoleKeyInfo('\0', ConsoleKey.RightArrow, false, false, false), 5);
        Assert.That(Render(state, width, height).Contains(Strings.CliColumnsHint, StringComparison.Ordinal), Is.EqualTo(hidden));
    }

    [Test]
    public void FilterLineAppearsOnlyWithAFilterAndTheFooterStaysAtTheBottom()
    {
        var state = State();
        var idle = Render(state, 120, 36);
        state.Active.Filter = "a";
        var filtered = Render(state, 120, 36);
        Assert.Multiple(() =>
        {
            Assert.That(idle, Does.Not.Contain(string.Format(CultureInfo.CurrentCulture, Strings.CliWatchFilter, "")));
            Assert.That(filtered, Does.Contain(string.Format(CultureInfo.CurrentCulture, Strings.CliWatchFilter, "a")));
            foreach (var lines in new[] { idle, filtered }.Select(text => text.TrimEnd().Split('\n')))
            {
                Assert.That(lines.Length, Is.EqualTo(35));
                Assert.That(lines[^1], Does.Contain("[Q] Quit"));
            }
        });
    }

    [Test]
    public void PauseFreezesAllChartValuesAndHistoryUntilResume()
    {
        var state = State();
        var live = Render(state, 120, 36);
        state.Handle(new ConsoleKeyInfo(' ', ConsoleKey.Spacebar, false, false, false), 10);
        var before = Render(state, 120, 36);
        Assert.That(before.Replace("PAUSED", "LIVE", StringComparison.Ordinal), Is.EqualTo(live));
        state.Observe(Sample(61, 99), null, [.. state.DisplayedSamples, Sample(61, 99)]);
        Assert.That(Render(state, 120, 36), Is.EqualTo(before));
        state.Handle(new ConsoleKeyInfo(' ', ConsoleKey.Spacebar, false, false, false), 10);
        Assert.That(Render(state, 120, 36), Is.Not.EqualTo(before));
    }

    [Test]
    public void PageNavigationUsesTheSameViewportAsRendering()
    {
        var state = State();
        var p = Sample(60).Processes[0];
        state.Observe(Sample(60) with { Processes = Enumerable.Range(1, 100).Select(i => p with { ProcessId = i }).ToArray() }, null);
        var page = TerminalCharts.PageSize(120, 36, TerminalTab.Processes);
        Render(state, 120, 36);
        state.Handle(new ConsoleKeyInfo('\0', ConsoleKey.PageDown, false, false, false), page);
        Render(state, 120, 36);
        Assert.Multiple(() =>
        {
            Assert.That(state.Selected!.Key, Is.EqualTo(state.Active.Rows[page].Key));
            Assert.That(state.Offset, Is.EqualTo(1));
        });
    }

    [Test]
    public void WideChartsKeepTheSameTextInColorAndNoColorAndOfferAsciiGlyphs()
    {
        var state = State();
        state.NetworkEnabled = true;
        using var output = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings { Enrichment = new() { UseDefaultEnrichers = false }, Out = new AnsiConsoleOutput(output),
            Ansi = AnsiSupport.Yes, ColorSystem = ColorSystemSupport.TrueColor });
        console.Profile.Width = 120;
        console.Write(InteractiveMonitor.BuildView(state, 0, 100, 120, 36));
        var plain = Regex.Replace(output.ToString(), "\u001b\\[[0-9;]*m", "");
        Assert.That(plain, Is.EqualTo(Render(state, 120, 36)));
        Assert.That(plain, Does.Contain("[✓]").And.Contain("[ ]").And.Not.Contain("☑").And.Not.Contain("☐"));
        Assert.That(output.ToString(), Does.Match("\\x1b\\[[0-9;]*38;2;63;185;80[0-9;]*m(?:\\x1b\\[[0-9;]*m)*\\[✓\\]"));
        var ascii = Render(state, 120, 36, false);
        Assert.That(ascii, Does.Contain("#").And.Not.Contain("░").And.Not.Contain("█").And.Contain("[Q] Quit")
            .And.Contain("[x]").And.Contain("[ ]").And.Not.Contain("✓"));
    }

    [TestCase("de")]
    [TestCase("ja")]
    [TestCase("zh-Hans")]
    public void TranslatedPanelsKeepTheirCellAlignment(string culture)
    {
        var previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
            var rendered = Render(State(), 120, 36);
            Assert.That(rendered.Split('\n').All(line => new Segment(line.TrimEnd('\r')).CellCount() <= 120), Is.True);
        }
        finally { CultureInfo.CurrentUICulture = previous; }
    }
}
