using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WinMon.Monitoring;
using WinMon.Preferences;

namespace WinMon.Controls;

/// <summary>How the Usage tab ranks apps; stored by number in preferences.</summary>
internal enum UsageSort
{
    CpuTime,
    Io,
    GpuTime,
    Processes,
    LastActive
}

/// <summary>The Usage tab's list choices, stored in preferences.</summary>
internal sealed record UsageListSettings(int TopCount, UsageSort Sort, bool AppsOnly);

/// <summary>One refresh of the Usage tab: the ledger's totals and the newest sample's time.</summary>
internal sealed record UsageFrame(
    IReadOnlyList<AppUsage> Apps,
    long MachineBusyTicks,
    long NotAttributedTicks,
    DateTimeOffset Since,
    DateTimeOffset? Latest,
    bool Capturing);

/// <summary>Per-app CPU time, I/O and GPU time since WinMon started; see docs/design/USAGE-TAB.md.</summary>
public partial class UsageView
{
    private readonly ObservableCollection<UsageDisplayRow> _rows = [];
    private readonly Dictionary<string, UsageDisplayRow> _rowsByKey = new(StringComparer.OrdinalIgnoreCase);
    private UsageFrame? _frame;
    private UsageDisplayRow? _contextRow;
    private bool _ready;
    private bool? _wideToolbar;

    internal event Action<UsageListSettings>? SettingsChanged;
    internal event Action<string>? CopyRequested;
    internal event Action? ResetRequested;
    internal event Action<AppUsage>? ShowInProcessesRequested;

    /// <summary>Whether Show in Processes can find this app now; the window owns history and selection.</summary>
    internal Func<AppUsage, bool>? CanShowInProcesses { get; set; }

    public UsageView()
    {
        InitializeComponent();
        foreach (var header in new[] { CpuHeader, IoHeader, GpuHeader, ProcessesHeader, LastActiveHeader })
        {
            header.MouseEnter += (_, _) => header.TextDecorations = TextDecorations.Underline;
            header.MouseLeave += (_, _) => header.TextDecorations = null;
        }

        Rows.ItemsSource = _rows;
        Rows.ContextMenu = new ContextMenu();
        RefreshLabels(new UsageListSettings(20, UsageSort.CpuTime, false));
        _ready = true;
        Strings.Text.PropertyChanged += OnLanguageChanged;
        Unloaded += (_, _) => Strings.Text.PropertyChanged -= OnLanguageChanged;
        Loaded += (_, _) =>
        {
            Strings.Text.PropertyChanged -= OnLanguageChanged;
            Strings.Text.PropertyChanged += OnLanguageChanged;
            ArrangeToolbar();
        };
        SizeChanged += (_, _) => ArrangeToolbar();
        RefreshRows();
    }

    /// <summary>The list choices as the controls show them.</summary>
    internal UsageListSettings Settings => new(
        TopCountChoices.TryGetCount(TopCount.SelectedItem, out var count) ? count : 20,
        (UsageSort)Math.Clamp(Sort.SelectedIndex, 0, 4), AppsOnly.IsChecked == true);

    /// <summary>Show saved list choices without raising <see cref="SettingsChanged"/>.</summary>
    internal void Load(UsageListSettings settings)
    {
        _ready = false;
        RefreshLabels(settings);
        _ready = true;
        RefreshRows();
    }

    internal void Update(UsageFrame frame)
    {
        _frame = frame;
        RefreshRows();
    }

    internal void FocusSearch()
    {
        Search.Focus();
        Search.SelectAll();
    }

    internal bool ClearSearch()
    {
        if (!Search.IsKeyboardFocusWithin || Search.Text.Length == 0) return false;
        Search.Clear();
        return true;
    }

    private void OnLanguageChanged(object? sender, PropertyChangedEventArgs e)
    {
        Load(Settings);
        ArrangeToolbar();
    }

    private void RefreshLabels(UsageListSettings settings)
    {
        TopCount.ItemsSource = TopCountChoices.Items;
        TopCount.SelectedItem = TopCountChoices.Selection(TopCountChoices.IsSupported(settings.TopCount)
            ? settings.TopCount
            : 20);
        Sort.ItemsSource = new[]
            { Strings.UsageCpuTime, Strings.IO, Strings.UsageGpuTime, Strings.ProcessesTab, Strings.UsageLastActive };
        Sort.SelectedIndex = Math.Clamp((int)settings.Sort, 0, 4);
        AppsOnly.IsChecked = settings.AppsOnly;
    }

    private void ArrangeToolbar()
    {
        if (!_ready || ActualWidth <= 0) return;
        RankingControls.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        AppsOnly.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var wide = ActualWidth >= RankingControls.DesiredSize.Width + AppsOnly.DesiredSize.Width + 224;
        if (_wideToolbar == wide) return;
        _wideToolbar = wide;
        Grid.SetColumnSpan(RankingControls, wide ? 1 : 3);
        Grid.SetRow(SearchBorder, wide ? 0 : 1);
        Grid.SetColumn(SearchBorder, wide ? 1 : 0);
        Grid.SetColumnSpan(SearchBorder, wide ? 1 : 2);
        Grid.SetRow(AppsOnly, wide ? 0 : 1);
        SearchBorder.Margin = wide ? new Thickness(12, 0, 12, 0) : new Thickness(0, 6, 12, 0);
        AppsOnly.Margin = wide ? new Thickness() : new Thickness(0, 6, 0, 0);
    }

    private void OnSettingsChanged(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        SettingsChanged?.Invoke(Settings);
        RefreshRows();
        ArrangeToolbar();
    }

    private void OnRankHeader(object sender, MouseButtonEventArgs e)
    {
        Sort.SelectedIndex = ReferenceEquals(sender, CpuHeader) ? 0 :
            ReferenceEquals(sender, IoHeader) ? 1 :
            ReferenceEquals(sender, GpuHeader) ? 2 :
            ReferenceEquals(sender, ProcessesHeader) ? 3 : 4;
        e.Handled = true;
    }

    private void OnSearchChanged(object sender, TextChangedEventArgs e)
    {
        if (!_ready) return;
        ClearSearchButton.Visibility = Search.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        RefreshRows();
    }

    private void OnClearSearch(object sender, RoutedEventArgs e)
    {
        Search.Clear();
        Search.Focus();
    }

    private void OnScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (e.OriginalSource is not ScrollViewer scroll || scroll.ViewportWidth <= 0) return;
        HeaderScroll.Width = scroll.ViewportWidth;
        ColumnHeaders.Width = Math.Max(ColumnHeaders.MinWidth,
            scroll.ViewportWidth - ColumnHeaders.Margin.Left - ColumnHeaders.Margin.Right);
        NotAttributedRow.Width = ColumnHeaders.Width;
        HeaderScroll.ScrollToHorizontalOffset(e.HorizontalOffset);
    }

    private void UpdateRankHeaders()
    {
        (TextBlock Header, string Label, string Help)[] headers =
        [
            (CpuHeader, Strings.UsageCpuTime, Strings.UsageCpuTimeHelp), (IoHeader, Strings.IO, Strings.UsageIoHelp),
            (GpuHeader, Strings.UsageGpuTime, Strings.UsageGpuTimeHelp),
            (ProcessesHeader, Strings.ProcessesTab, Strings.UsageProcessesHelp),
            (LastActiveHeader, Strings.UsageLastActive, Strings.UsageLastActive)
        ];
        for (var i = 0; i < headers.Length; i++)
        {
            var (header, label, help) = headers[i];
            var active = Sort.SelectedIndex == i;
            header.Text = active ? label + " ▾" : label;
            header.FontWeight = active ? FontWeights.SemiBold : FontWeights.Normal;
            header.ToolTip = help;
            if (header.Tag is bool shown && shown == active) continue;
            header.Tag = active;
            header.SetResourceReference(TextBlock.ForegroundProperty, active ? "Text" : "Muted");
        }
    }

    /// <summary>The apps the list shows, in order, after search, App only and the Top N limit.</summary>
    internal static IReadOnlyList<AppUsage> Visible(IReadOnlyList<AppUsage> apps, UsageListSettings settings,
        string query)
    {
        query = query.Trim();
        var filtered = apps.Where(app => (!settings.AppsOnly || app.IsApp) && Matches(app, query));
        var ordered = settings.Sort switch
        {
            UsageSort.Io => filtered.OrderByDescending(app => app.IoBytes),
            UsageSort.GpuTime => filtered.OrderByDescending(app => app.GpuSeconds ?? -1),
            UsageSort.Processes => filtered.OrderByDescending(app => app.Lifetimes).ThenByDescending(app => app.Running),
            UsageSort.LastActive => filtered.OrderByDescending(app => app.LastActive?.Timestamp ?? DateTimeOffset.MinValue),
            _ => filtered.OrderByDescending(app => app.CpuTicks)
        };
        return [.. TopCountChoices.Limit(ordered.ThenBy(UsageText.DisplayName, StringComparer.CurrentCultureIgnoreCase),
            settings.TopCount)];
    }

    private static bool Matches(AppUsage app, string query)
    {
        return query.Length == 0 ||
               UsageText.DisplayName(app).Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
               app.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
               app.ImagePath.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    private void RefreshRows()
    {
        if (!_ready) return;
        UpdateRankHeaders();
        var frame = _frame;
        IReadOnlyList<AppUsage> visible = frame is null ? [] : Visible(frame.Apps, Settings, Search.Text);
        var desired = new List<UsageDisplayRow>(visible.Count);
        foreach (var app in visible)
        {
            if (!_rowsByKey.TryGetValue(app.Key, out var row))
            {
                _rowsByKey[app.Key] = row = new UsageDisplayRow(app.Key);
                LoadIcon(row, app.ImagePath);
            }

            row.Update(app, frame!.MachineBusyTicks, frame.Latest);
            desired.Add(row);
        }

        // Move rows into place rather than reset the list, as the Services view does.
        for (var i = 0; i < desired.Count; i++)
        {
            if (i < _rows.Count && ReferenceEquals(_rows[i], desired[i])) continue;
            var from = _rows.IndexOf(desired[i]);
            if (from > i) _rows.Move(from, i);
            else _rows.Insert(i, desired[i]);
        }

        while (_rows.Count > desired.Count) _rows.RemoveAt(_rows.Count - 1);
        if (frame is not null && _rowsByKey.Count > frame.Apps.Count * 2 + 64)
        {
            // A reset clears the ledger; drop rows for apps it no longer has.
            var keys = frame.Apps.Select(app => app.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var key in _rowsByKey.Keys.Where(key => !keys.Contains(key)).ToArray()) _rowsByKey.Remove(key);
        }

        Summary.Text = frame is null
            ? Strings.UsageCollecting
            : Strings.Format(Strings.UsageSummary, frame.Since.ToLocalTime().DateTime,
                UsageText.Duration((frame.Latest ?? frame.Since) - frame.Since),
                UsageText.CpuTime(frame.MachineBusyTicks), Environment.ProcessorCount);
        NotAttributedCpu.Text = frame is null ? Strings.MissingReading : UsageText.CpuTime(frame.NotAttributedTicks);
        NotAttributedShare.Text =
            frame is null ? Strings.MissingReading : UsageText.Share(frame.NotAttributedTicks, frame.MachineBusyTicks);
        CopyUsage.IsEnabled = ResetUsage.IsEnabled = frame is not null;
        Notice.Text = frame is null ? "" :
            frame.Capturing ? Strings.UsageLiveNote :
            _rows.Count == 0 ? Strings.UsageNoAppsMatch : "";
        Notice.Visibility = Notice.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private static void LoadIcon(UsageDisplayRow row, string path)
    {
        var task = ProcessIconProvider.GetForPathAsync(path);
        if (task.IsCompletedSuccessfully) row.SetIcon(task.Result);
        else _ = SetWhenLoadedAsync();
        return;

        async Task SetWhenLoadedAsync()
        {
            row.SetIcon(await task);
        }
    }

    private void OnCopyUsage(object sender, RoutedEventArgs e)
    {
        if (_frame is { } frame) CopyRequested?.Invoke(UsageText.Report(frame, Settings, Search.Text));
    }

    private void OnReset(object sender, RoutedEventArgs e)
    {
        ResetRequested?.Invoke();
    }

    private void OnRowRightClick(object sender, MouseButtonEventArgs e)
    {
        _contextRow = null;
        if (e.OriginalSource is not DependencyObject source ||
            ItemsControl.ContainerFromElement(Rows, source) is not ListBoxItem item) return;
        _contextRow = item.DataContext as UsageDisplayRow;
        item.IsSelected = true;
        item.Focus();
    }

    private void OnMenuOpening(object sender, ContextMenuEventArgs e)
    {
        var row = e.OriginalSource is DependencyObject source &&
                  ItemsControl.ContainerFromElement(Rows, source) is ListBoxItem item
            ? item.DataContext as UsageDisplayRow
            : e.CursorLeft < 0
                ? Rows.SelectedItem as UsageDisplayRow
                : _contextRow;
        if (row?.App is not { } app || _frame is not { } frame)
        {
            e.Handled = true;
            return;
        }

        Rows.ContextMenu = CreateMenu(app, frame);
    }

    internal ContextMenu CreateMenu(AppUsage app, UsageFrame frame)
    {
        var menu = new ContextMenu();
        menu.SetResourceReference(BackgroundProperty, "Raised");
        menu.SetResourceReference(ForegroundProperty, "Text");
        var other = app.Key == UsageLedger.OtherAppsKey;
        var canShow = !other && CanShowInProcesses?.Invoke(app) == true;
        var show = Add(Strings.UsageShowInProcesses, ButtonIcon.Process, canShow,
            () => ShowInProcessesRequested?.Invoke(app));
        if (!canShow && !other) ToolTipService.SetShowOnDisabled(show, true);
        if (!canShow && !other) show.ToolTip = Strings.UsageNotInHistory;
        Add(Strings.CopyDetails, ButtonIcon.Copy, true,
            () => CopyRequested?.Invoke(UsageText.Details(app, frame)));
        Add(Strings.OpenFileLocation, ButtonIcon.Folder, app.ImagePath.Length > 0 && !other,
            () => OpenLocation(app.ImagePath));
        return menu;

        MenuItem Add(string label, Geometry icon, bool enabled, Action action)
        {
            var entry = ProcessRow.ActionItem(label, icon);
            entry.IsEnabled = enabled;
            entry.Click += (_, _) =>
            {
                if (enabled) action();
            };
            menu.Items.Add(entry);
            return entry;
        }
    }

    private void OpenLocation(string path)
    {
        try
        {
            if (!File.Exists(path)) throw new IOException(Strings.ProcessFileMissing);
            var start = new System.Diagnostics.ProcessStartInfo(Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"))
                { UseShellExecute = false, Arguments = $"/select,\"{path}\"" };
            System.Diagnostics.Process.Start(start)?.Dispose();
        }
        catch (Exception ex) when (ex is Win32Exception or IOException or UnauthorizedAccessException
                                       or InvalidOperationException)
        {
            MessageDialog.ShowNotice(Window.GetWindow(this), ex.Message, Strings.OpenFileLocation);
        }
    }
}

/// <summary>One app's row; it raises a change only when something it shows changed.</summary>
internal sealed class UsageDisplayRow(string key) : INotifyPropertyChanged
{
    public string Key { get; } = key;
    public string Name { get; private set; } = "";
    public string Detail { get; private set; } = "";
    public string Tooltip { get; private set; } = "";
    public string AccessibleName { get; private set; } = "";
    public string CpuTime { get; private set; } = "";
    public string Share { get; private set; } = "";
    public string Io { get; private set; } = "";
    public string GpuTime { get; private set; } = "";
    public string Processes { get; private set; } = "";
    public string LastActive { get; private set; } = "";
    public ImageSource? Icon { get; private set; }
    internal AppUsage? App { get; private set; }
    public event PropertyChangedEventHandler? PropertyChanged;

    internal void Update(AppUsage app, long machineBusyTicks, DateTimeOffset? latest)
    {
        var shown = Shown();
        App = app;
        Name = UsageText.DisplayName(app);
        var kind = app.IsApp ? Strings.ProcessKindApp : Strings.ProcessKindBackground;
        Detail = app.Key == UsageLedger.OtherAppsKey ? "" :
            app.ImagePath.Length > 0 ? $"{kind} · {app.ImagePath}" : $"{kind} · {app.Name}";
        Tooltip = Detail.Length == 0 ? Name : $"{Name}\n{Detail}";
        CpuTime = UsageText.CpuTime(app.CpuTicks);
        Share = UsageText.Share(app.CpuTicks, machineBusyTicks);
        Io = UsageText.Bytes(app.IoBytes);
        GpuTime = app.GpuSeconds is { } gpu ? UsageText.Duration(TimeSpan.FromSeconds(gpu)) : Strings.MissingReading;
        Processes = UsageText.Processes(app);
        LastActive = UsageText.LastActive(app.LastActive, latest);
        AccessibleName = $"{Name}, {Strings.UsageCpuTime} {CpuTime}, {Strings.UsageShare} {Share}";
        if (Shown() != shown) PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
    }

    internal void SetIcon(BitmapSource icon)
    {
        Icon = icon;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Icon)));
    }

    private (string, string, string, string, string, string, string, string, string, string) Shown()
    {
        return (Name, Detail, Tooltip, AccessibleName, CpuTime, Share, Io, GpuTime, Processes, LastActive);
    }
}

/// <summary>Text for the Usage tab and its copied reports.</summary>
internal static class UsageText
{
    internal static string DisplayName(AppUsage app)
    {
        return app.Key == UsageLedger.OtherAppsKey ? Strings.UsageOtherApps :
            app.AppName.Length > 0 ? app.AppName : app.Name;
    }

    /// <summary>Processor time as hours:minutes:seconds; hours keep counting past a day.</summary>
    internal static string CpuTime(long ticks)
    {
        return Duration(TimeSpan.FromTicks(Math.Max(0, ticks)));
    }

    internal static string Duration(TimeSpan time)
    {
        var whole = TimeSpan.FromSeconds(Math.Floor(Math.Max(0, time.TotalSeconds)));
        return string.Create(CultureInfo.CurrentCulture,
            $"{(long)whole.TotalHours}:{whole.Minutes:00}:{whole.Seconds:00}");
    }

    internal static string Share(long part, long whole)
    {
        return whole > 0 ? Strings.Format(Strings.PercentOneDecimal, 100d * part / whole) : Strings.MissingReading;
    }

    /// <summary>Decimal units, matching the MB/s used for rates elsewhere.</summary>
    internal static string Bytes(double bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var unit = 0;
        while (bytes >= 1000 && unit < units.Length - 1)
        {
            bytes /= 1000;
            unit++;
        }

        return (unit == 0 ? bytes.ToString("N0", CultureInfo.CurrentCulture) :
            bytes.ToString("N1", CultureInfo.CurrentCulture)) + " " + units[unit];
    }

    internal static string Processes(AppUsage app)
    {
        return string.Create(CultureInfo.CurrentCulture, $"{app.Lifetimes:N0} ({app.Running:N0})");
    }

    /// <summary>"now" within five seconds of the newest sample, else the local time, with the date if not today.</summary>
    internal static string LastActive(SampleKey? active, DateTimeOffset? latest)
    {
        if (active is not { } key) return Strings.MissingReading;
        if (latest is { } now && (now - key.Timestamp).TotalSeconds <= 5) return Strings.UsageNow;
        var local = key.Timestamp.ToLocalTime();
        return local.Date == DateTimeOffset.Now.Date
            ? local.ToString("T", CultureInfo.CurrentCulture)
            : local.ToString("g", CultureInfo.CurrentCulture);
    }

    internal static string Details(AppUsage app, UsageFrame frame)
    {
        var text = new StringBuilder();
        text.AppendLine(DisplayName(app));
        if (app.ImagePath.Length > 0) text.AppendLine(app.ImagePath);
        if (app.Publisher.Length > 0) text.AppendLine(app.Publisher);
        text.AppendLine(app.IsApp ? Strings.ProcessKindApp : Strings.ProcessKindBackground);
        text.AppendLine(Strings.Format(Strings.UsageReportSince, frame.Since.ToLocalTime().DateTime));
        text.AppendLine($"{Strings.UsageCpuTime}: {CpuTime(app.CpuTicks)} ({Share(app.CpuTicks, frame.MachineBusyTicks)})");
        text.AppendLine($"{Strings.IO}: {Bytes(app.IoBytes)}");
        text.AppendLine(
            $"{Strings.UsageGpuTime}: {(app.GpuSeconds is { } gpu ? Duration(TimeSpan.FromSeconds(gpu)) : Strings.MissingReading)}");
        text.AppendLine($"{Strings.ProcessesTab}: {Processes(app)}");
        text.AppendLine($"{Strings.UsageLastActive}: {LastActive(app.LastActive, frame.Latest)}");
        return text.ToString();
    }

    internal static string Report(UsageFrame frame, UsageListSettings settings, string query)
    {
        var text = new StringBuilder();
        text.AppendLine(Strings.Format(Strings.ProductVersion, AppInfo.Version));
        text.AppendLine(Strings.Format(Strings.UsageReportSince, frame.Since.ToLocalTime().DateTime));
        text.AppendLine(Strings.Format(Strings.UsageReportBusy, CpuTime(frame.MachineBusyTicks),
            Environment.ProcessorCount));
        var sort = settings.Sort switch
        {
            UsageSort.Io => Strings.IO, UsageSort.GpuTime => Strings.UsageGpuTime,
            UsageSort.Processes => Strings.ProcessesTab, UsageSort.LastActive => Strings.UsageLastActive,
            _ => Strings.UsageCpuTime
        };
        text.Append(Strings.Format(Strings.UsageReportView,
            settings.TopCount == 0 ? Strings.AllEntries : settings.TopCount.ToString(CultureInfo.CurrentCulture),
            sort));
        if (settings.AppsOnly) text.Append(" · " + Strings.AppOnly);
        if (query.Trim().Length > 0) text.Append($" · \"{query.Trim()}\"");
        text.AppendLine();
        text.AppendLine();
        text.AppendLine(string.Join('\t', Strings.UsageApp, Strings.UsageCpuTime, Strings.UsageShare, Strings.IO,
            Strings.UsageGpuTime, Strings.ProcessesTab, Strings.UsageLastActive));
        foreach (var app in UsageView.Visible(frame.Apps, settings, query))
            text.AppendLine(string.Join('\t', DisplayName(app), CpuTime(app.CpuTicks),
                Share(app.CpuTicks, frame.MachineBusyTicks), Bytes(app.IoBytes),
                app.GpuSeconds is { } gpu ? Duration(TimeSpan.FromSeconds(gpu)) : Strings.MissingReading,
                Processes(app), LastActive(app.LastActive, frame.Latest)));
        text.AppendLine(string.Join('\t', Strings.UsageNotAttributed, CpuTime(frame.NotAttributedTicks),
            Share(frame.NotAttributedTicks, frame.MachineBusyTicks)));
        return text.ToString();
    }
}
