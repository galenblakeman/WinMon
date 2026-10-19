using System.Windows;
using System.Windows.Controls;
using WinMon.Monitoring;

namespace WinMon.Controls;

/// <summary>Process CPU/GPU utilization with resident memory and interval read/write I/O.</summary>
public sealed class ProcessRow : Grid
{
    private readonly TextBlock _name = new() { FontSize = 14, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock _pid = new() { FontSize = 10 };
    private readonly TextBlock _value = Cell();
    private readonly TextBlock _memory = Cell();
    private readonly TextBlock _io = Cell();
    private readonly TextBlock _gpu = Cell();
    private readonly Image _icon = new() { Width = 18, Height = 18, Margin = new Thickness(0, 0, 8, 0), Source = ProcessIconProvider.Fallback };
    private readonly CpuGraph _graph = new() { Height = 22, Margin = new Thickness(10, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
    private (int, long)? _identity;

    public ProcessRow()
    {
        Height = 36;
        var separator = new Border { BorderThickness = new Thickness(0, 0, 0, 1), IsHitTestVisible = false };
        separator.SetResourceReference(Border.BorderBrushProperty, "Line");
        SetColumnSpan(separator, 6); Children.Add(separator);
        ColumnDefinitions.Add(new ColumnDefinition());
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(55) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(55) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(82) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(75) });
        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
        stack.Children.Add(_name); stack.Children.Add(_pid);
        var process = new Grid { ClipToBounds = true };
        process.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        process.ColumnDefinitions.Add(new ColumnDefinition());
        SetColumn(stack, 1);
        process.Children.Add(_icon); process.Children.Add(stack); Children.Add(process);
        Children.Add(_graph); SetColumn(_graph, 1);
        Children.Add(_value); SetColumn(_value, 2);
        Children.Add(_gpu); SetColumn(_gpu, 3);
        Children.Add(_memory); SetColumn(_memory, 4);
        Children.Add(_io); SetColumn(_io, 5);
        foreach (var text in new[] { _name, _value, _gpu, _memory, _io }) text.SetResourceReference(TextBlock.ForegroundProperty, "Text");
        _pid.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        _memory.ToolTip = "Resident working set, including shared pages. Does not sum to total physical memory used.";
        _io.ToolTip = "Read/write I/O includes file, network and device operations; not disk-only throughput.";
    }

    internal void Update(ProcessUsage usage, IReadOnlyList<CpuSample> samples, int seconds, bool showPid,
        ProcessGraphData? data)
    {
        ArgumentNullException.ThrowIfNull(usage);
        _name.Text = usage.Name;
        _name.ToolTip = $"{usage.Name} · PID {usage.ProcessId}";
        _pid.Text = $"PID {usage.ProcessId}";
        _pid.Visibility = showPid ? Visibility.Visible : Visibility.Collapsed;
        _value.Text = $"{usage.CpuPercent:0.0}%";
        _gpu.Text = usage.GpuPercent is { } percent ? $"{percent:0.0}%" : "—";
        _gpu.ToolTip = usage.GpuPercent is not null
            ? $"Busiest engine per interval, averaged over readable intervals in the last 5 seconds.\nLast measured engine: {usage.GpuEngine} · adapter {usage.GpuId}\nProcess GPU percentages are not additive."
            : "GPU utilization unavailable for this process; no valid counter or lifetime match.";
        if (data is null) _graph.SetData(samples, seconds, null, usage);
        else _graph.SetProcessData(data, seconds, usage);
        ToolTip = $"{usage.Name} · PID {usage.ProcessId}\nCPU history · last {seconds / 60d:0.#} min\n0–{_graph.ScaleMaximum:0.0}% of total machine CPU · same scale for every row";
        var nextIdentity = (usage.ProcessId, usage.CreationTicks);
        if (_identity != nextIdentity)
        {
            _identity = nextIdentity;
            _icon.Source = ProcessIconProvider.Fallback;
            _ = LoadIconAsync(usage, nextIdentity);
        }
        _memory.Text = usage.WorkingSetBytes is { } bytes ? $"{bytes / 1_048_576d:N0} MiB" : "—";
        _io.Text = usage.IoBytesPerSecond is { } rate ? $"{rate / 1_000_000:0.00}" : "—";
        System.Windows.Automation.AutomationProperties.SetName(this, $"{usage.Name}, process {usage.ProcessId}, {_value.Text} of total CPU averaged over 5 seconds, GPU {_gpu.Text} averaged over readable intervals in the last 5 seconds, memory {_memory.Text}, read/write I/O {_io.Text} MB per second");
    }

    /// <summary>Show the measured processes ranked below the visible rows as one muted summary line.</summary>
    /// <param name="count">Measured processes outside the visible rows.</param>
    /// <param name="cpuPercent">Their summed CPU as a percentage of total machine capacity.</param>
    /// <param name="ioBytesPerSecond">Their summed read/write I/O, if any was readable.</param>
    internal void ShowRemainder(int count, double cpuPercent, double? ioBytesPerSecond)
    {
        _icon.Visibility = Visibility.Hidden;
        _graph.Visibility = Visibility.Hidden;
        _pid.Visibility = Visibility.Collapsed;
        foreach (var text in new[] { _name, _value, _io }) text.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        _name.Text = $"Other processes ({count:N0})";
        _name.ToolTip = null;
        _value.Text = $"{cpuPercent:0.0}%";
        _memory.Text = "";
        _gpu.Text = "—";
        _gpu.ToolTip = "GPU percentages represent different engines and cannot be summed.";
        _io.Text = ioBytesPerSecond is { } rate ? $"{rate / 1_000_000:0.00}" : "—";
        ToolTip = "Remaining processes in the current ranking. CPU and I/O are summed; memory shares pages and GPU percentages are not additive.";
        System.Windows.Automation.AutomationProperties.SetName(this, $"{count} other processes, {_value.Text} of total CPU combined, averaged over 5 seconds");
    }

    private async Task LoadIconAsync(ProcessUsage usage, (int, long) requestedIdentity)
    {
        var source = await ProcessIconProvider.GetAsync(usage);
        // Ranking can replace this row while the executable icon is being read.
        if (_identity == requestedIdentity) _icon.Source = source;
    }

    private static TextBlock Cell() => new() { FontSize = 12, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
}
