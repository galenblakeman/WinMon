using System.Windows;
using System.Windows.Controls;
using WinMon.Monitoring;

namespace WinMon.Controls;

/// <summary>Process CPU/GPU utilization with resident memory and interval read/write I/O.</summary>
public sealed class ProcessRow : Grid
{
    private readonly TextBlock _name = new() { FontSize = 14, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock _pid = new() { FontSize = 10, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock _value = Cell();
    private readonly TextBlock _memory = Cell();
    private readonly TextBlock _io = Cell();
    private readonly TextBlock _gpu = Cell();
    private readonly TextBlock _threads = Cell();
    private readonly TextBlock _handles = Cell();
    private readonly Image _icon = new() { Width = 18, Height = 18, Margin = new Thickness(0, 0, 8, 0), Source = ProcessIconProvider.Fallback };
    private readonly CpuGraph _graph = new() { Height = 22, Margin = new Thickness(10, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
    private (int, long)? _identity;
    private ProcessUsage? _usage;
    private Func<ProcessUsage, bool>? _canEnd;
    private Action<ProcessUsage>? _end;
    private readonly Button _pin = new() { Width = 24, Height = 28, MinHeight = 0, Padding = new Thickness(4),
        VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
    private Func<ProcessUsage, bool, bool>? _isPinned;
    private Action<ProcessUsage, bool>? _togglePin;
    private bool _grouped;
    private bool _unavailable;
    private DateTimeOffset? _sampleAt;
    private string _state = "";
    private Action<ProcessDetailsSnapshot>? _properties;
    private Action<ProcessDetailsSnapshot>? _copyDetails;
    private Action<ProcessUsage>? _openLocation;
    private Action<ProcessUsage>? _dump;
    private Func<ProcessUsage, bool>? _canDump;
    private Func<bool>? _historical;
    private string? _pinForeground;
    private string? _background;
    private string? _nameForeground = "Text";
    private readonly Border _selection = new() { BorderThickness = new Thickness(3, 0, 0, 0), IsHitTestVisible = false, Visibility = Visibility.Collapsed };
    internal event Action<ProcessUsage, bool>? ProcessSelected;
    internal void SetSelected(bool selected) => _selection.Visibility = selected ? Visibility.Visible : Visibility.Collapsed;
    internal bool Represents(Func<ProcessUsage, bool, bool> matches) => _usage is { } usage && matches(usage, _grouped);

    internal void EnablePinning(Func<ProcessUsage, bool, bool> isPinned, Action<ProcessUsage, bool> toggle)
    {
        _isPinned = isPinned;
        _togglePin = toggle;
        ButtonIcon.SetData(_pin, ButtonIcon.Pin);
        _pin.SetResourceReference(StyleProperty, "CaptionButton");
        _pin.Visibility = Visibility.Visible;
        _pin.Click += (_, _) => { if (_usage is { } target) toggle(target, _grouped); };
    }

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

    internal void EnableInspectionActions(Action<ProcessDetailsSnapshot> properties, Action<ProcessDetailsSnapshot> copy,
        Action<ProcessUsage> location, Action<ProcessUsage> dump, Func<ProcessUsage, bool> canDump, Func<bool> historical)
    {
        _properties = properties; _copyDetails = copy; _openLocation = location;
        _dump = dump; _canDump = canDump; _historical = historical;
    }

    internal static MenuItem ActionItem(string header, System.Windows.Media.Geometry geometry)
    {
        var item = new MenuItem { Header = header };
        var icon = new System.Windows.Shapes.Path { Data = geometry, Width = 16, Height = 16,
            Stretch = System.Windows.Media.Stretch.Uniform, StrokeThickness = 1.5,
            StrokeStartLineCap = System.Windows.Media.PenLineCap.Round, StrokeEndLineCap = System.Windows.Media.PenLineCap.Round };
        icon.SetBinding(System.Windows.Shapes.Shape.StrokeProperty,
            new System.Windows.Data.Binding(nameof(MenuItem.Foreground)) { Source = item });
        item.Icon = icon;
        return item;
    }

    internal bool PrepareProcessMenu()
    {
        if (ContextMenu is null) return false;
        ContextMenu.Items.Clear();
        if (_usage is not { } target || _canEnd is null || _end is null) return false;
        // Capture now: live ranking may replace this row while the menu is open.
        var item = ActionItem(target.MemberIds.Count > 0 ? Strings.Format(Strings.EndGroupMenu, target.Name) : Strings.Format(Strings.EndPID, target.Name, target.ProcessId), ButtonIcon.Cancel);
        item.IsEnabled = _canEnd(target);
        item.ToolTip = Strings.AvailableForLiveProcesses;
        if (_properties is not null && _copyDetails is not null && _openLocation is not null && _dump is not null)
        {
            var snapshot = new ProcessDetailsSnapshot(target, _grouped, _sampleAt, _historical?.Invoke() == true, !_unavailable, _state);
            var properties = ActionItem(Strings.ProcessProperties + "…", ButtonIcon.Info);
            properties.Click += (_, _) => _properties(snapshot);
            ContextMenu.Items.Add(properties);
            var location = ActionItem(Strings.OpenFileLocation, ButtonIcon.Folder);
            location.IsEnabled = !string.IsNullOrWhiteSpace(target.ImagePath);
            location.Click += (_, _) => _openLocation(target);
            ContextMenu.Items.Add(location);
            var copy = ActionItem(Strings.CopyDetails, ButtonIcon.Copy);
            copy.Click += (_, _) => _copyDetails(snapshot);
            ContextMenu.Items.Add(copy);
            ContextMenu.Items.Add(new Separator());
        }
        if (_togglePin is not null && _isPinned is not null)
        {
            var grouped = _grouped;
            var pinItem = ActionItem(_isPinned(target, grouped) ? Strings.UnpinProcess : Strings.PinProcess, ButtonIcon.Pin);
            pinItem.Click += (_, _) => _togglePin(target, grouped);
            ContextMenu.Items.Add(pinItem);
        }
        if (_dump is not null)
        {
            var dump = ActionItem(Strings.CreateDumpFile + "…", ButtonIcon.Save);
            dump.IsEnabled = !_unavailable && _canDump?.Invoke(target) == true;
            dump.ToolTip = Strings.ProcessDumpUnavailable;
            dump.Click += (_, _) => _dump(target);
            ContextMenu.Items.Add(dump);
            ContextMenu.Items.Add(new Separator());
        }
        item.IsEnabled &= !_unavailable;
        item.Click += (_, _) => _end(target);
        ContextMenu.Items.Add(item);
        return true;
    }

    public ProcessRow()
    {
        Height = 36;
        PreviewMouseDown += (_, _) => { if (_usage is { } usage) ProcessSelected?.Invoke(usage, _grouped); };
        KeyDown += (_, e) =>
        {
            if (e.Key is System.Windows.Input.Key.Enter or System.Windows.Input.Key.Space && _usage is { } usage)
            { ProcessSelected?.Invoke(usage, _grouped); e.Handled = true; }
        };
        _selection.SetResourceReference(Border.BorderBrushProperty, "Accent");
        _selection.SetResourceReference(Border.BackgroundProperty, "Raised");
        SetColumnSpan(_selection, 8);
        Children.Add(_selection);
        var separator = new Border { BorderThickness = new Thickness(0, 0, 0, 1), IsHitTestVisible = false };
        separator.SetResourceReference(Border.BorderBrushProperty, "Line");
        SetColumnSpan(separator, 8); Children.Add(separator);
        ColumnDefinitions.Add(new ColumnDefinition());
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(55) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(55) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(82) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(75) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(64) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(72) });
        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
        stack.Children.Add(_name); stack.Children.Add(_pid);
        var process = new Grid { ClipToBounds = true };
        process.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        process.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        process.ColumnDefinitions.Add(new ColumnDefinition());
        SetColumn(_icon, 1); SetColumn(stack, 2);
        process.Children.Add(_pin); process.Children.Add(_icon); process.Children.Add(stack); Children.Add(process);
        Children.Add(_graph); SetColumn(_graph, 1);
        Children.Add(_value); SetColumn(_value, 2);
        Children.Add(_gpu); SetColumn(_gpu, 3);
        Children.Add(_memory); SetColumn(_memory, 4);
        Children.Add(_io); SetColumn(_io, 5);
        Children.Add(_threads); SetColumn(_threads, 6);
        Children.Add(_handles); SetColumn(_handles, 7);
        foreach (var text in new[] { _name, _value, _gpu, _memory, _io, _threads, _handles }) text.SetResourceReference(TextBlock.ForegroundProperty, "Text");
        _pid.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        _memory.ToolTip = Strings.ResidentWorkingSetIncludingSharedPagesDoesNotSumTo;
        _io.ToolTip = Strings.ReadWriteIOIncludesFileNetworkAndDeviceOperations;
    }

    internal void Update(ProcessUsage usage, IReadOnlyList<CpuSample> samples, int seconds, bool showPid,
        ProcessGraphData? data, bool grouped = false, PinnedProcessState state = PinnedProcessState.Present)
    {
        ArgumentNullException.ThrowIfNull(usage);
        _io.ToolTip = Strings.ReadWriteIOIncludesFileNetworkAndDeviceOperations;
        _usage = usage;
        _grouped = grouped;
        _unavailable = state != PinnedProcessState.Present;
        _sampleAt = samples.LastOrDefault()?.Timestamp;
        _state = StateText(state);
        var pinned = _isPinned?.Invoke(usage, grouped) == true;
        ButtonIcon.SetData(_pin, pinned ? ButtonIcon.PinFilled : ButtonIcon.Pin);
        ButtonIcon.SetFilled(_pin, pinned);
        _pin.ToolTip = pinned ? Strings.UnpinProcess : Strings.PinProcess;
        System.Windows.Automation.AutomationProperties.SetName(_pin, $"{_pin.ToolTip}: {(string.IsNullOrWhiteSpace(usage.AppName) ? usage.Name : usage.AppName)}");
        SetResourceKey(ref _pinForeground, _pin, Control.ForegroundProperty, pinned ? "ProcessPinAccent" : "Muted");
        SetResourceKey(ref _background, this, BackgroundProperty, pinned ? "SectionHeaderSurface" : "Surface");
        SetResourceKey(ref _nameForeground, _name, TextBlock.ForegroundProperty, _unavailable ? "Muted" : "Text");
        var members = usage.MemberCount > 1;
        var name = !string.IsNullOrWhiteSpace(usage.AppName) ? usage.AppName
            : !string.IsNullOrWhiteSpace(usage.ProductName) ? usage.ProductName : usage.Name;
        _name.Text = members ? $"{name} ×{usage.MemberCount}" : name;
        var tooltipName = name == usage.Name ? _name.Text : $"{_name.Text} ({usage.Name})";
        var path = usage.ImagePath.Length > 0 ? usage.ImagePath : Strings.ExecutablePathUnavailable;
        var publisherAndPath = string.IsNullOrWhiteSpace(usage.Publisher) ? path : $"{usage.Publisher}\n{path}";
        _name.ToolTip = $"{tooltipName} · {(grouped ? MemberList(usage) : Strings.Format(Strings.PID, usage.ProcessId))}\n{publisherAndPath}";
        var kind = usage.HasAppWindow switch { true => Strings.ProcessKindApp, false => Strings.ProcessKindBackground, null => Strings.Unknown };
        _pid.Text = showPid ? $"{(members ? Strings.Format(Strings.Processes, usage.MemberCount) : Strings.Format(Strings.PID, usage.ProcessId))} · {kind}" : kind;
        _pid.Visibility = Visibility.Visible;
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
            ProcessSort.Threads => Strings.Format(Strings.HistoryCount, Strings.Threads, scale),
            ProcessSort.Handles => Strings.Format(Strings.HistoryCount, Strings.Handles, scale),
            _ => Strings.Format(Strings.History0OfTotalMachineCPU, grouped ? Strings.SummedCpuTitle : Strings.CPU, scale)
        };
        ToolTip = Strings.Format(Strings.LastMinSameScaleForEveryRow, tooltipName, grouped ? MemberList(usage) : Strings.Format(Strings.PID, usage.ProcessId), publisherAndPath, history, seconds / 60d);
        var nextIdentity = (usage.ProcessId, usage.CreationTicks);
        if (_identity != nextIdentity)
        {
            _identity = nextIdentity;
            _icon.Source = ProcessIconProvider.Fallback;
            _ = LoadIconAsync(usage, nextIdentity);
        }
        // Working sets share pages, so a group shows summed private bytes instead.
        _memory.Text = (grouped ? usage.PrivateBytes : usage.WorkingSetBytes) is { } bytes ? (bytes / 1_048_576d).ToString("N0", System.Globalization.CultureInfo.CurrentCulture) : Strings.MissingReading;
        _memory.ToolTip = grouped
            ? Strings.PrivateBytesSummedAcrossThisExecutableSCurrentProcesses
            : Strings.ResidentWorkingSetIncludingSharedPagesDoesNotSumTo;
        _io.Text = usage.IoBytesPerSecond is { } rate ? $"{rate / 1_000_000:0.00}" : Strings.MissingReading;
        _threads.Text = usage.ThreadCount?.ToString("N0") ?? Strings.MissingReading;
        _handles.Text = usage.HandleCount?.ToString("N0") ?? Strings.MissingReading;
        _threads.ToolTip = Strings.ProcessThreadsExplanation;
        _handles.ToolTip = Strings.ProcessHandlesExplanation;
        if (_unavailable)
        {
            var status = StateText(state);
            _pid.Text = $"{Strings.Format(Strings.PID, usage.ProcessId)} · {status}";
            foreach (var text in new[] { _value, _gpu, _memory, _io, _threads, _handles }) text.Text = Strings.MissingReading;
            _gpu.ToolTip = status;
        }
        System.Windows.Automation.AutomationProperties.SetName(this, Strings.Format(Strings.OfTotalCPUAveragedOver5SecondsGPUAveragedOver, name, members ? Strings.Format(Strings.Processes, usage.MemberCount) : Strings.Format(Strings.Process, usage.ProcessId), _value.Text, _gpu.Text, _memory.Text, _io.Text) + $" · {kind} · {Strings.Threads}: {_threads.Text} · {Strings.Handles}: {_handles.Text}");
        if (_unavailable) System.Windows.Automation.AutomationProperties.SetName(this,
            System.Windows.Automation.AutomationProperties.GetName(this) + $" · {StateText(state)}");
    }

    // A new resource reference is re-resolved through the element tree, so set one only when the key changes.
    private static void SetResourceKey(ref string? current, FrameworkElement element, DependencyProperty property, string key)
    {
        if (current == key) return;
        current = key;
        element.SetResourceReference(property, key);
    }

    internal static string StateText(PinnedProcessState state) => state switch
    {
        PinnedProcessState.Exited => Strings.ProcessExited,
        PinnedProcessState.NotRecorded => Strings.ProcessNotRecorded,
        PinnedProcessState.Unavailable => Strings.Unavailable,
        _ => ""
    };

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
        foreach (var text in new[] { _value, _io }) text.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        SetResourceKey(ref _nameForeground, _name, TextBlock.ForegroundProperty, "Muted");
        _name.Text = grouped ? Strings.Format(Strings.OtherExecutables, count) : Strings.Format(Strings.OtherProcesses, count);
        _name.ToolTip = null;
        _value.Text = Strings.Format(Strings.PercentOneDecimal, cpuPercent);
        _memory.Text = "";
        _threads.Text = "";
        _handles.Text = "";
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
