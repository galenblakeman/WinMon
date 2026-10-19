using System.Windows;
using System.Windows.Controls;
using WinMon.Monitoring;

namespace WinMon.Controls;

/// <summary>CPU-ranked process with resident memory and interval read/write I/O.</summary>
public sealed class ProcessRow : Grid
{
    private readonly TextBlock name = new() { FontSize = 14, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock pid = new() { FontSize = 10 };
    private readonly TextBlock value = Cell();
    private readonly TextBlock memory = Cell();
    private readonly TextBlock io = Cell();
    private readonly Image icon = new() { Width = 18, Height = 18, Margin = new Thickness(0, 0, 8, 0), Source = ProcessIconProvider.Fallback };
    private readonly CpuGraph graph = new() { Height = 22, Margin = new Thickness(10, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
    private (int, long)? identity;

    public ProcessRow()
    {
        Height = 36;
        var separator = new Border { BorderThickness = new Thickness(0, 0, 0, 1), IsHitTestVisible = false };
        separator.SetResourceReference(Border.BorderBrushProperty, "Line");
        SetColumnSpan(separator, 5); Children.Add(separator);
        ColumnDefinitions.Add(new ColumnDefinition());
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(76) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(66) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(94) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
        stack.Children.Add(name); stack.Children.Add(pid);
        var process = new DockPanel();
        DockPanel.SetDock(icon, Dock.Left);
        process.Children.Add(icon); process.Children.Add(stack); Children.Add(process);
        Children.Add(graph); SetColumn(graph, 1);
        Children.Add(value); SetColumn(value, 2);
        Children.Add(memory); SetColumn(memory, 3);
        Children.Add(io); SetColumn(io, 4);
        foreach (var text in new[] { name, value, memory, io }) text.SetResourceReference(TextBlock.ForegroundProperty, "Text");
        pid.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        memory.ToolTip = "Resident working set, including shared pages. Does not sum to total physical memory used.";
        io.ToolTip = "Read/write I/O includes file, network and device operations; not disk-only throughput.";
    }

    public void Update(ProcessUsage usage, IReadOnlyList<CpuSample> samples, int seconds, bool expanded)
        => Update(usage, samples, seconds, expanded, null);

    internal void Update(ProcessUsage usage, IReadOnlyList<CpuSample> samples, int seconds, bool expanded,
        ProcessGraphData? data)
    {
        ArgumentNullException.ThrowIfNull(usage);
        name.Text = usage.Name;
        name.ToolTip = $"{usage.Name} · PID {usage.ProcessId}";
        pid.Text = $"PID {usage.ProcessId}";
        pid.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
        value.Text = $"{usage.CpuPercent:0.0}%";
        if (data is null) graph.SetData(samples, seconds, null, usage);
        else graph.SetProcessData(data, seconds, usage);
        ToolTip = $"{usage.Name} · PID {usage.ProcessId}\nCPU history · last {seconds / 60d:0.#} min\n0–{graph.ScaleMaximum:0.0}% of total machine CPU · same scale for every row";
        var nextIdentity = (usage.ProcessId, usage.CreationTicks);
        if (identity != nextIdentity)
        {
            identity = nextIdentity;
            icon.Source = ProcessIconProvider.Fallback;
            _ = LoadIconAsync(usage, nextIdentity);
        }
        memory.Text = usage.WorkingSetBytes is ulong bytes ? $"{bytes / 1_048_576d:N0} MiB" : "—";
        io.Text = usage.IoBytesPerSecond is double rate ? $"{rate / 1_000_000:0.00}" : "—";
        System.Windows.Automation.AutomationProperties.SetName(this, $"{usage.Name}, process {usage.ProcessId}, {value.Text} of total CPU averaged over 5 seconds, memory {memory.Text}, read/write I/O {io.Text} MB per second");
    }

    /// <summary>Show the measured processes ranked below the visible rows as one muted summary line.</summary>
    /// <param name="count">Measured processes outside the visible rows.</param>
    /// <param name="cpuPercent">Their summed CPU as a percentage of total machine capacity.</param>
    /// <param name="ioBytesPerSecond">Their summed read/write I/O, if any was readable.</param>
    internal void ShowRemainder(int count, double cpuPercent, double? ioBytesPerSecond)
    {
        icon.Visibility = Visibility.Hidden;
        graph.Visibility = Visibility.Hidden;
        pid.Visibility = Visibility.Collapsed;
        foreach (var text in new[] { name, value, io }) text.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        name.Text = $"Other processes ({count:N0})";
        name.ToolTip = null;
        value.Text = $"{cpuPercent:0.0}%";
        memory.Text = "";
        io.Text = ioBytesPerSecond is double rate ? $"{rate / 1_000_000:0.00}" : "—";
        ToolTip = "Sum of the measured processes outside the list above. Memory is omitted because working sets share pages.";
        System.Windows.Automation.AutomationProperties.SetName(this, $"{count} other processes, {value.Text} of total CPU combined, averaged over 5 seconds");
    }

    private async Task LoadIconAsync(ProcessUsage usage, (int, long) requestedIdentity)
    {
        var source = await ProcessIconProvider.GetAsync(usage);
        // Ranking can replace this row while the executable icon is being read.
        if (identity == requestedIdentity) icon.Source = source;
    }

    private static TextBlock Cell() => new() { FontSize = 12, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
}
