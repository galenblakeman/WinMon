using Spectre.Console;
using Spectre.Console.Rendering;
using WinMon.Controls;
using WinMon.Monitoring;
using WinMon.Resources;
using static WinMon.Cli.SnapshotReport;

namespace WinMon.Cli;

internal static class TerminalCharts
{
    internal const int HistorySeconds = 60;

    internal static bool Wide(int width, int height) => width >= 120 && height >= 32;
    internal static int SummaryHeight(int width, int height) => Wide(width, height) ? 7 : height >= 24 ? 5 : 3;
    internal static int PageSize(int width, int height, TerminalTab tab) => Math.Max(1,
        height - 16 - (SummaryHeight(width, height) - 3));

    internal static string Gauge(double? value, int width, bool unicode)
    {
        if (value is not { } number || !double.IsFinite(number)) return TerminalText.Fit(Strings.Unavailable, width);
        var filled = (int)Math.Floor(Math.Clamp(number, 0, 100) / 100 * width);
        return new string(unicode ? '█' : '#', filled) + new string(unicode ? '░' : '-', width - filled);
    }

    internal static string Sparkline(IReadOnlyList<MonitorSample> samples, IReadOnlyList<double?> values,
        double maximum, int width, bool unicode, double minimum = 0)
    {
        var result = new char[width];
        Array.Fill(result, ' ');
        if (samples.Count == 0 || maximum <= minimum || !double.IsFinite(maximum)) return new string(result);
        var end = ProcessRanking.Moment(samples[^1]);
        var sums = new double[width];
        var counts = new int[width];
        var gaps = new bool[width];
        double? previous = null;
        var previousBucket = -1;
        for (var i = 0; i < samples.Count; i++)
        {
            var moment = ProcessRanking.Moment(samples[i]);
            if (moment < end - HistorySeconds || moment > end) continue;
            var bucket = Math.Min(width - 1, (int)((moment - end + HistorySeconds) / HistorySeconds * width));
            if (previous is { } prior && moment - prior > 2.5)
                for (var j = previousBucket + 1; j < bucket; j++) gaps[j] = true;
            if (samples[i].IsGap || values[i] is not { } value || !double.IsFinite(value)) gaps[bucket] = true;
            else { sums[bucket] += Math.Max(0, value); counts[bucket]++; }
            previous = moment;
            previousBucket = bucket;
        }
        for (var i = 0; i < width; i++)
        {
            if (gaps[i] || counts[i] == 0) continue;
            var average = sums[i] / counts[i];
            var level = average == 0 ? 0 : (int)Math.Clamp(Math.Round((average - minimum) / (maximum - minimum) * 8), 1, 8);
            result[i] = (unicode ? "─▁▂▃▄▅▆▇█" : "_.:-=+*#@")[level];
        }
        return new string(result);
    }

    internal static IRenderable Summary(MonitorSample sample, IReadOnlyList<MonitorSample> history,
        int width, int height, bool unicode)
    {
        var r = sample.Resources;
        var separator = unicode ? " · " : " | ";
        var disk = $"{Strings.Disk} {SafeText(r.DiskName)} {Percent(r.DiskActivePercent)}{separator}" +
            $"{Strings.ReadWriteIO} {Rate(r.DiskReadBytesPerSecond)} / {Rate(r.DiskWriteBytesPerSecond)}";
        var network = $"{Strings.Network} {SafeText(r.NetworkName)} RX {NetworkRate(r.ReceiveBytesPerSecond)} / TX {NetworkRate(r.SendBytesPerSecond)}";
        var memory = $"{Strings.Memory} {GaugeReadoutText.Memory(r.UsedMemoryBytes, r.TotalMemoryBytes)}";
        var download = DirectionRate(r.ReceiveBytesPerSecond, unicode ? "↓" : "RX ");
        if (height < 24)
            return new Rows(Line(width, ($"{Strings.CPU} {Percent(sample.TotalCpuPercent)}", TerminalChrome.Cpu),
                (separator, null), (memory, TerminalChrome.Memory), (separator, null), ($"{Strings.Network} {download}", TerminalChrome.Network),
                (separator, null), ($"{Strings.GPU} {Percent(r.GpuPercent)}", TerminalChrome.Gpu)),
                Line(width, (disk, TerminalChrome.Disk)), Line(width, (network, TerminalChrome.Network)));
        var visible = history.Where(s => ProcessRanking.Moment(s) >= ProcessRanking.Moment(sample) - HistorySeconds).ToArray();
        var networkScale = NetworkChartScale.Maximum(visible.Where(s => SameAdapter(s.Resources, r)));
        var networkLevel = DownloadLevel(r.ReceiveBytesPerSecond, networkScale);
        if (!Wide(width, height))
        {
            var diskLabel = $"{Strings.Disk} {SafeText(r.DiskName)}";
            var labelWidth = Math.Min(16, new[] { Strings.CPU, Strings.Memory, diskLabel, Strings.Network, Strings.GPU }
                .Max(label => new Segment(label).CellCount()));
            IRenderable Row(string label, double? value, string details, Style style, string? display = null) =>
                Line(width, ($"{Pad(label, labelWidth)} [{Gauge(value, 10, unicode)}] " +
                    $"{display ?? Pad(Percent(value), 6)} {details}", style));
            return new Rows(
                Row(Strings.CPU, sample.TotalCpuPercent, GaugeReadoutText.CpuClock(r.CpuClockMhz), TerminalChrome.Cpu),
                Row(Strings.Memory, r.MemoryPercent, GaugeReadoutText.Memory(r.UsedMemoryBytes, r.TotalMemoryBytes), TerminalChrome.Memory),
                Row(diskLabel, r.DiskActivePercent,
                    $"{Strings.ReadWriteIO} {Rate(r.DiskReadBytesPerSecond)} / {Rate(r.DiskWriteBytesPerSecond)}", TerminalChrome.Disk),
                Row(Strings.Network, networkLevel,
                    $"{DirectionRate(r.SendBytesPerSecond, unicode ? "↑" : "TX ")} {SafeText(r.NetworkName)}",
                    TerminalChrome.Network, download),
                Row(Strings.GPU, r.GpuPercent, GaugeReadoutText.GpuMemory(r.GpuMemoryUsedBytes, r.GpuMemoryTotalBytes), TerminalChrome.Gpu));
        }

        // Memory barely moves on a 0-100 scale, so its history zooms to the range it covered.
        var memoryRange = visible.Select(s => s.Resources.MemoryPercent).OfType<double>().Where(double.IsFinite).ToArray();
        var memoryLow = memoryRange.Length == 0 ? 0 : Math.Min(95, Math.Floor(memoryRange.Min()));
        var memoryHigh = memoryRange.Length == 0 ? 100 : Math.Min(100, Math.Max(memoryLow + 5, Math.Ceiling(memoryRange.Max())));
        var panelWidth = (width - 4) / 5;
        var inner = panelWidth - 2;
        var labels = new[] { Strings.CPU, Strings.Memory, Strings.Format(Strings.DiskLabel, SafeText(r.DiskName)), Strings.Network, Strings.GPU };
        var current = new[] { sample.TotalCpuPercent, r.MemoryPercent, r.DiskActivePercent, networkLevel, r.GpuPercent };
        var values = new[] { Percent(current[0]), Percent(current[1]), Percent(current[2]), download, Percent(current[4]) };
        var details = new[] { GaugeReadoutText.CpuClock(r.CpuClockMhz), GaugeReadoutText.Memory(r.UsedMemoryBytes, r.TotalMemoryBytes),
            GaugeReadoutText.DiskRate(r.DiskBytesPerSecond), DirectionRate(r.SendBytesPerSecond, unicode ? "↑" : "TX "),
            GaugeReadoutText.GpuMemory(r.GpuMemoryUsedBytes, r.GpuMemoryTotalBytes) };
        var cells = new string[5][];
        for (var i = 0; i < cells.Length; i++)
        {
            var metric = i;
            var series = visible.Select(s => metric switch
            {
                0 => s.TotalCpuPercent, 1 => s.Resources.MemoryPercent,
                2 => s.Resources.DiskName == r.DiskName ? s.Resources.DiskActivePercent : null,
                3 => SameAdapter(s.Resources, r) ? DownloadLevel(s.Resources.ReceiveBytesPerSecond, networkScale) : null,
                _ => s.Resources.GpuPercent
            }).ToArray();
            labels[i] = TerminalText.Fit(labels[i], inner - 2);
            cells[i] = [
                (unicode ? "╭" : "+") + Pad(" " + labels[i] + " ", inner, unicode ? '─' : '-') + (unicode ? "╮" : "+"),
                Box(values[i]), Box(Gauge(current[i], inner - 2, unicode)),
                Box(details[i]),
                Box(i == 1 ? Sparkline(visible, series, memoryHigh, inner - 2, unicode, memoryLow) :
                    Sparkline(visible, series, 100, inner - 2, unicode)),
                Box(i == 3 ? Strings.Format(Strings.CliRateHistory, HistorySeconds, RateText.Short(networkScale)) :
                    i == 1 ? Strings.Format(Strings.CliScaledHistory, HistorySeconds, memoryLow, memoryHigh) :
                    Strings.Format(Strings.CliResourceHistory, HistorySeconds)),
                (unicode ? "╰" : "+") + new string(unicode ? '─' : '-', inner) + (unicode ? "╯" : "+")];
        }
        var styles = new[] { TerminalChrome.Cpu, TerminalChrome.Memory, TerminalChrome.Disk, TerminalChrome.Network, TerminalChrome.Gpu };
        var lines = new List<IRenderable>();
        for (var line = 0; line < 7; line++)
        {
            var parts = new List<(string, Style?)>();
            for (var i = 0; i < cells.Length; i++)
            {
                if (i > 0) parts.Add((" ", null));
                var cell = cells[i][line];
                if (line == 0)
                {
                    parts.Add((cell[..2], null));
                    parts.Add((cell.Substring(2, labels[i].Length), styles[i]));
                    parts.Add((cell[(2 + labels[i].Length)..], null));
                    continue;
                }
                parts.Add((cell[..1], null));
                parts.Add((cell[1..^1], line is 1 or 2 or 4 ? styles[i] : null));
                parts.Add((cell[^1..], null));
            }
            lines.Add(Line(width, parts.ToArray()));
        }
        return new Rows(lines);

        string Box(string text) => (unicode ? "│ " : "| ") + Pad(text, inner - 2) + (unicode ? " │" : " |");
    }

    private static bool SameAdapter(ResourceSample first, ResourceSample second) =>
        first.NetworkId == second.NetworkId && first.NetworkName == second.NetworkName;

    private static double? DownloadLevel(double? bytes, double maximum) =>
        bytes is >= 0 && double.IsFinite(bytes.Value) ? bytes.Value / (maximum / 800) : null;

    private static string DirectionRate(double? bytes, string direction) =>
        bytes is >= 0 && double.IsFinite(bytes.Value) ? direction + NetworkRate(bytes) : Strings.MissingReading;

    private static Paragraph Line(int width, params (string Text, Style? Style)[] parts)
    {
        var clean = parts.Select(part => (Text: SafeText(part.Text), part.Style)).ToArray();
        var text = TerminalText.Fit(string.Concat(clean.Select(part => part.Text)), width);
        var result = new Paragraph();
        var offset = 0;
        foreach (var part in clean)
        {
            if (offset == text.Length) break;
            var length = Math.Min(part.Text.Length, text.Length - offset);
            result.Append(text.Substring(offset, length), part.Style);
            offset += length;
        }
        return result;
    }

    private static string Pad(string text, int width, char padding = ' ')
    {
        var clipped = TerminalText.Fit(text, width);
        return clipped + new string(padding, Math.Max(0, width - new Segment(clipped).CellCount()));
    }
}
