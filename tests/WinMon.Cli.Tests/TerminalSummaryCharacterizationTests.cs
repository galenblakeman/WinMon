using System.Globalization;
using MonCore.Monitoring;
using NUnit.Framework;
using Spectre.Console;
using Spectre.Console.Rendering;
using WinMon.Resources;

namespace WinMon.Cli.Tests;

[TestFixture, NonParallelizable, SetCulture("en-US"), SetUICulture("en-US")]
public sealed class TerminalSummaryCharacterizationTests
{
    private static MonitorSample Sample(int second = 60) => new(DateTimeOffset.UnixEpoch.AddSeconds(second), 25, [], 0)
    {
        Resources = new ResourceSample
        {
            CpuClockMhz = 3200, UsedMemoryBytes = 4UL << 30, TotalMemoryBytes = 8UL << 30,
            DiskName = "C:", DiskActivePercent = 40, DiskBytesPerSecond = 3000,
            DiskReadBytesPerSecond = 1000, DiskWriteBytesPerSecond = 2000,
            NetworkId = "adapter", NetworkName = "Ethernet", ReceiveBytesPerSecond = 125_000,
            SendBytesPerSecond = 250_000, GpuPercent = 75,
            GpuMemoryUsedBytes = 1UL << 30, GpuMemoryTotalBytes = 4UL << 30
        }
    };

    private static string Render(MonitorSample sample, IReadOnlyList<MonitorSample> history,
        int width, int height, bool unicode)
    {
        using var output = new StringWriter(CultureInfo.InvariantCulture);
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Enrichment = new() { UseDefaultEnrichers = false }, Out = new AnsiConsoleOutput(output),
            Ansi = AnsiSupport.No, ColorSystem = ColorSystemSupport.NoColors
        });
        console.Profile.Width = width;
        console.Write(TerminalCharts.Summary(sample, history, width, height, unicode));
        return output.ToString().Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    private static string[] Lines(string text) => text.TrimEnd('\n').Split('\n');

    [TestCase(119, 23, false, 3)]
    [TestCase(120, 23, true, 3)]
    [TestCase(119, 24, true, 5)]
    [TestCase(119, 24, false, 5)]
    [TestCase(120, 24, true, 7)]
    [TestCase(120, 24, false, 7)]
    [TestCase(124, 36, true, 7)]
    [TestCase(125, 36, false, 7)]
    public void LayoutThresholdsKeepExactRowCountsAndBoundedCellWidths(int width, int height, bool unicode, int rows)
    {
        var sample = Sample();
        var lines = Lines(Render(sample, [sample], width, height, unicode));
        Assert.Multiple(() =>
        {
            Assert.That(lines, Has.Length.EqualTo(rows));
            Assert.That(lines.All(line => new Segment(line).CellCount() <= width), Is.True);
            if (rows == 7)
            {
                Assert.That(new Segment(lines[0]).CellCount(), Is.EqualTo(width - (width - 4) % 5));
                Assert.That(lines[0].Count(c => c == (unicode ? '╭' : '+')), Is.EqualTo(unicode ? 5 : 10));
                Assert.That(lines[6].Count(c => c == (unicode ? '╰' : '+')), Is.EqualTo(unicode ? 5 : 10));
            }
        });
    }

    [TestCase(true)]
    [TestCase(false)]
    public void WideRowsKeepPrimaryReadingsGaugeLengthsAndSeparateHistoryLabels(bool unicode)
    {
        var sample = Sample();
        var lines = Lines(Render(sample, [sample], 120, 24, unicode));
        var border = unicode ? '│' : '|';
        string[] Cells(int row) => lines[row].Split(border)
            .Where((_, index) => index % 2 == 1).Select(cell => cell.Trim()).ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(Cells(1), Is.EqualTo(new[] { "25.0%", "50.0%", "40.0%", unicode ? "↓125 KB/s" : "RX 125 KB/s", "75.0%" }));
            Assert.That(Cells(2).Select(cell => cell.Length), Is.All.EqualTo(19));
            Assert.That(Cells(2).All(cell => cell.All(c => unicode ? c is '█' or '░' : c is '#' or '-')), Is.True);
            Assert.That(Cells(3)[0], Is.EqualTo("3.20 GHz"));
            Assert.That(Cells(3)[1], Is.EqualTo("4.0 / 8 GiB"));
            Assert.That(Cells(3)[3], Is.EqualTo(unicode ? "↑250 KB/s" : "TX 250 KB/s"));
            Assert.That(Cells(4).All(cell => cell.Length == 1), Is.True);
            Assert.That(Cells(5)[0], Is.EqualTo(Strings.Format(Strings.CliResourceHistory, 60)));
            Assert.That(Cells(5)[1], Is.EqualTo(Strings.Format(Strings.CliScaledHistory, 60, 50, 55)));
            Assert.That(Cells(5)[3], Is.EqualTo(Strings.Format(Strings.CliRateHistory, 60, "250KB/s")));
        });
    }

    [TestCase(23)]
    [TestCase(24)]
    public void ShortLayoutsRetainDownloadUploadAndLiteralUntrustedNames(int height)
    {
        var sample = Sample() with { Resources = Sample().Resources with
        { DiskName = "[red]C:\u001b\n", NetworkName = "[bold]Ethernet\u001b\r\n" } };
        var text = Render(sample, [sample], 119, height, false);
        Assert.Multiple(() =>
        {
            Assert.That(text, Does.Contain("[red]C:").And.Contain("[bold]Ethernet"));
            Assert.That(text, Does.Contain("RX 125 KB/s").And.Contain("TX 250 KB/s"));
            Assert.That(text, Does.Not.Contain('\u001b').And.Not.Contain('\r'));
            Assert.That(Lines(text), Has.Length.EqualTo(height < 24 ? 3 : 5));
            Assert.That(sample.Resources.DiskName, Does.Contain('\u001b'));
        });
    }

    [Test]
    public void ShortLayoutDoesNotReadHistoryButTallLayoutsRejectNullHistory()
    {
        var sample = Sample();
        Assert.That(Render(sample, null!, 120, 23, true), Is.EqualTo(Render(sample, [], 120, 23, true)));
        var error = Assert.Throws<ArgumentNullException>(() => Render(sample, null!, 120, 24, true));
        Assert.That(error!.ParamName, Is.EqualTo("source"));
    }

    [Test]
    public void HistoryWindowIncludesTheExactBoundaryAndRetainsTheExistingFutureScalePolicy()
    {
        var sample = Sample();
        var peak = Sample(0) with { Resources = sample.Resources with { SendBytesPerSecond = 1_000_000_000 } };
        var ordinary = Render(sample, [sample], 120, 24, true);
        Assert.Multiple(() =>
        {
            Assert.That(Render(sample, [peak with { Timestamp = DateTimeOffset.UnixEpoch.AddSeconds(-1) }, sample], 120, 24, true), Is.EqualTo(ordinary));
            Assert.That(Render(sample, [peak, sample], 120, 24, true), Is.Not.EqualTo(ordinary));
            Assert.That(Render(sample, [sample, peak with { Timestamp = DateTimeOffset.UnixEpoch.AddSeconds(61) }], 120, 24, true), Is.Not.EqualTo(ordinary));
            Assert.That(Render(sample, [peak with { Resources = peak.Resources with { NetworkId = "other" } }, sample], 120, 24, true), Does.Contain("250KB/s"));
        });
    }

    [TestCase(true)]
    [TestCase(false)]
    public void MissingValuesAndRepeatedCallsPreserveInputsAndDoNotRetainAnotherSummary(bool unicode)
    {
        var sample = Sample();
        var missing = sample with { Resources = new ResourceSample() };
        var history = new[] { sample };
        var original = history.ToArray();
        var expected = Render(sample, history, 120, 24, unicode);
        var unavailable = Render(missing, [], 120, 24, unicode);
        Assert.Multiple(() =>
        {
            Assert.That(unavailable, Does.Contain(Strings.MissingReading));
            Assert.That(Render(sample, history, 120, 24, unicode), Is.EqualTo(expected));
            Assert.That(Render(missing, [], 120, 24, unicode), Is.EqualTo(unavailable));
            Assert.That(history, Is.EqualTo(original));
            Assert.That(history[0], Is.SameAs(sample));
            Assert.That(sample.Resources.NetworkName, Is.EqualTo("Ethernet"));
        });
    }
}
