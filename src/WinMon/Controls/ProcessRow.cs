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
    private ProcessUsage? _usage;
    private Func<ProcessUsage, bool>? _canEnd;
    private Action<ProcessUsage>? _end;

    internal void EnableProcessActions(Func<ProcessUsage, bool> canEnd, Action<ProcessUsage> end)
    {
        // A real background lets right-click and keyboard context menus work across the row.
        Background = System.Windows.Media.Brushes.Transparent;
        Focusable = true;
        _canEnd = canEnd;
        _end = end;
        ContextMenu = new ContextMenu();
        ContextMenu.SetResourceReference(BackgroundProperty, "Raised");
        ContextMenu.SetResourceReference(Control.ForegroundProperty, "Text");
        ContextMenuOpening += (_, e) =>
        {
            e.Handled = !PrepareProcessMenu();
        };
    }

    internal bool PrepareProcessMenu()
    {
        if (ContextMenu is null) return false;
        ContextMenu.Items.Clear();
        if (_usage is not { } target || _canEnd is null || _end is null) return false;
        // Capture now: live ranking may replace this row while the menu is open.
        var item = new MenuItem { Header = Strings.Format(Strings.EndPID, target.Name, target.ProcessId), IsEnabled = _canEnd(target),
            ToolTip = Strings.AvailableForLiveIndividualProcessesEndingAProcessCanLose };
        item.Click += (_, _) => _end(target);
        ContextMenu.Items.Add(item);
        return true;
    }

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
        _memory.ToolTip = Strings.ResidentWorkingSetIncludingSharedPagesDoesNotSumTo;
        _io.ToolTip = Strings.ReadWriteIOIncludesFileNetworkAndDeviceOperations;
    }

    internal void Update(ProcessUsage usage, IReadOnlyList<CpuSample> samples, int seconds, bool showPid,
        ProcessGraphData? data, bool grouped = false)
    {
        ArgumentNullException.ThrowIfNull(usage);
        _io.ToolTip = Strings.ReadWriteIOIncludesFileNetworkAndDeviceOperations;
        _usage = usage;
        var members = usage.MemberCount > 1;
        var name = !string.IsNullOrWhiteSpace(usage.AppName) ? usage.AppName
            : !string.IsNullOrWhiteSpace(usage.ProductName) ? usage.ProductName : usage.Name;
        _name.Text = members ? $"{name} ×{usage.MemberCount}" : name;
        var tooltipName = name == usage.Name ? _name.Text : $"{_name.Text} ({usage.Name})";
        var path = usage.ImagePath.Length > 0 ? usage.ImagePath : Strings.ExecutablePathUnavailable;
        _name.ToolTip = $"{tooltipName} · {(grouped ? MemberList(usage) : Strings.Format(Strings.PID, usage.ProcessId))}\n{path}";
        _pid.Text = members ? Strings.Format(Strings.Processes, usage.MemberCount) : Strings.Format(Strings.PID, usage.ProcessId);
        _pid.Visibility = showPid ? Visibility.Visible : Visibility.Collapsed;
        _value.Text = Strings.Format(Strings.PercentOneDecimal, usage.CpuPercent);
        _gpu.Text = usage.GpuPercent is { } percent ? Strings.Format(Strings.PercentOneDecimal, percent) : Strings.MissingReading;
        _gpu.ToolTip = usage.GpuPercent is not null
            ? Strings.Format(Strings.BusiestEnginePerIntervalAveragedOverReadableIntervalsInThe, usage.GpuEngine, usage.GpuId)
            : Strings.GPUUtilizationUnavailableForThisProcessNoValidCounterOr;
        if (data is null) _graph.SetData(samples, seconds, null, usage);
        else _graph.SetProcessData(data, seconds, usage);
        var scale = _graph.ScaleMaximum;
        var history = (data?.Sort ?? ProcessSort.Cpu) switch
        {
            ProcessSort.Gpu => Strings.Format(Strings.History0GPUUtilization, grouped ? Strings.BusiestMemberGpuTitle : Strings.GPU, scale),
            ProcessSort.Memory => Strings.Format(Strings.History0MiB, grouped ? Strings.SummedPrivateMemoryTitle : Strings.ResidentMemory, scale / 1_048_576d),
            ProcessSort.Io => Strings.Format(Strings.History0MBS, grouped ? Strings.SummedReadWriteIO : Strings.ReadWriteIO, scale / 1_000_000d),
            _ => Strings.Format(Strings.History0OfTotalMachineCPU, grouped ? Strings.SummedCpuTitle : Strings.CPU, scale)
        };
        ToolTip = Strings.Format(Strings.LastMinSameScaleForEveryRow, tooltipName, grouped ? MemberList(usage) : Strings.Format(Strings.PID, usage.ProcessId), path, history, seconds / 60d);
        var nextIdentity = (usage.ProcessId, usage.CreationTicks);
        if (_identity != nextIdentity)
        {
            _identity = nextIdentity;
            _icon.Source = ProcessIconProvider.Fallback;
            _ = LoadIconAsync(usage, nextIdentity);
        }
        // Working sets share pages, so a group shows summed private bytes instead.
        _memory.Text = (grouped ? usage.PrivateBytes : usage.WorkingSetBytes) is { } bytes ? Strings.Format(Strings.MiB, bytes / 1_048_576d) : Strings.MissingReading;
        _memory.ToolTip = grouped
            ? Strings.PrivateBytesSummedAcrossThisExecutableSCurrentProcesses
            : Strings.ResidentWorkingSetIncludingSharedPagesDoesNotSumTo;
        _io.Text = usage.IoBytesPerSecond is { } rate ? $"{rate / 1_000_000:0.00}" : Strings.MissingReading;
        System.Windows.Automation.AutomationProperties.SetName(this, Strings.Format(Strings.OfTotalCPUAveragedOver5SecondsGPUAveragedOver, name, members ? Strings.Format(Strings.Processes, usage.MemberCount) : Strings.Format(Strings.Process, usage.ProcessId), _value.Text, _gpu.Text, _memory.Text, _io.Text));
    }

    /// <summary>Show the measured processes ranked below the visible rows as one muted summary line.</summary>
    /// <param name="count">Measured processes outside the visible rows.</param>
    /// <param name="cpuPercent">Their summed CPU as a percentage of total machine capacity.</param>
    /// <param name="ioBytesPerSecond">Their summed read/write I/O, if any was readable.</param>
    /// <param name="grouped">Whether the count represents executable groups.</param>
    internal void ShowRemainder(int count, double cpuPercent, double? ioBytesPerSecond, bool grouped = false)
    {
        _icon.Visibility = Visibility.Hidden;
        _graph.Visibility = Visibility.Hidden;
        _pid.Visibility = Visibility.Collapsed;
        foreach (var text in new[] { _name, _value, _io }) text.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        _name.Text = grouped ? Strings.Format(Strings.OtherExecutables, count) : Strings.Format(Strings.OtherProcesses, count);
        _name.ToolTip = null;
        _value.Text = Strings.Format(Strings.PercentOneDecimal, cpuPercent);
        _memory.Text = "";
        _gpu.Text = Strings.MissingReading;
        _gpu.ToolTip = Strings.GPUPercentagesRepresentDifferentEnginesAndCannotBeSummed;
        _io.Text = ioBytesPerSecond is { } rate ? $"{rate / 1_000_000:0.00}" : Strings.MissingReading;
        ToolTip = Strings.RemainingProcessesInTheCurrentRankingCPUAndIO;
        System.Windows.Automation.AutomationProperties.SetName(this, Strings.Format(Strings.OtherOfTotalCPUCombinedAveragedOver5Seconds, count, grouped ? Strings.Executables : Strings.ProcessesLowercase, _value.Text));
    }

    private static string MemberList(ProcessUsage usage)
    {
        if (usage.MemberIds.Count <= 1) return Strings.Format(Strings.PID, usage.ProcessId);
        var shown = string.Join(", ", usage.MemberIds.Take(12));
        return usage.MemberIds.Count > 12 ? Strings.Format(Strings.PIDsMore, shown, usage.MemberIds.Count - 12) : Strings.Format(Strings.PIDs, shown);
    }

    private async Task LoadIconAsync(ProcessUsage usage, (int, long) requestedIdentity)
    {
        var source = await ProcessIconProvider.GetAsync(usage);
        // Ranking can replace this row while the executable icon is being read.
        if (_identity == requestedIdentity) _icon.Source = source;
    }

    private static TextBlock Cell() => new() { FontSize = 12, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
}
