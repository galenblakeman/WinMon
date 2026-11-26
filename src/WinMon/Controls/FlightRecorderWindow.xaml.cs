using System.Globalization;
using System.Diagnostics;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using System.Windows.Controls.Primitives;
using Microsoft.Win32;
using MonCore.Monitoring;
using WinMon.Monitoring;

namespace WinMon.Controls;

internal partial class FlightRecorderWindow : Window
{
    private MonitorSample[] _samples = [];
    private static readonly CaptureMetric[] AllMetrics = [CaptureMetric.Cpu, CaptureMetric.Memory, CaptureMetric.PrimaryDisk,
        CaptureMetric.SecondaryDisk, CaptureMetric.Network, CaptureMetric.Gpu,
        CaptureMetric.CpuTemperature, CaptureMetric.GpuTemperature, CaptureMetric.PrimaryDiskTemperature, CaptureMetric.SecondaryDiskTemperature,
        CaptureMetric.CpuPerformanceLimited, CaptureMetric.GpuThermalLimited];
    private CaptureMetric[] _metrics = AllMetrics;
    private bool _temperatureChartsEnabled;
    internal WinMon.Preferences.TemperatureUnit TemperatureUnit { get; set; }
    private int _index;
    private bool _fileBusy;
    private readonly Func<DateTimeOffset, DateTimeOffset, CancellationToken, Task<IncidentEventResult>>? _eventReader;
    private CancellationTokenSource? _eventReadCancellation;
    private int _eventReadGeneration;
    private bool _eventReadPending;
    private bool _openedRecording;
    private bool _findingTrendIncident;
    private CaptureMetric? _recordedMetric;
    private bool _loadingRecording;
    private bool _closed;
    private bool _updatingEvents;
    private IncidentEventResult _events = IncidentEventResult.Unknown;
    private IReadOnlyList<IncidentEventMapping> _eventMappings = [];
    private IncidentEventMapping? _selectedEvent;
    private IReadOnlyList<CrashGroup> _crashGroups = [];
    private CrashGroup? _selectedCrashGroup;
    private bool _updatingCrashes;
    private IReadOnlyDictionary<SampleKey, ConnectionCapture> _traffic = new Dictionary<SampleKey, ConnectionCapture>();
    private IReadOnlyDictionary<SampleKey, (IReadOnlyList<ProcessUsage> Started, IReadOnlyList<ProcessUsage> Stopped)>? _changes;
    private ResourceTrendArchiveState? _trends;
    private ResourceTrendBucket[] _trendBuckets = [];
    private ResourceTrendBucket? _trendSelection;
    private string? _trendNotice;
    private bool _updatingTrends;
    private static readonly ResourceTrendMetric[] AllTrendMetrics = [ResourceTrendMetric.Cpu, ResourceTrendMetric.MemoryBytes,
        ResourceTrendMetric.PrimaryDiskActive, ResourceTrendMetric.SecondaryDiskActive, ResourceTrendMetric.Receive,
        ResourceTrendMetric.Gpu, ResourceTrendMetric.CpuTemperature, ResourceTrendMetric.GpuTemperature,
        ResourceTrendMetric.PrimaryDiskTemperature, ResourceTrendMetric.SecondaryDiskTemperature,
        ResourceTrendMetric.CpuPerformanceLimited, ResourceTrendMetric.GpuThermalLimited];

    private ResourceTrendMetric[] _trendMetrics = AllTrendMetrics;

    internal event EventHandler? RefreshRequested;
    internal event Action<Task>? SaveStarted;
    internal event Action<Task>? SaveFinished;
    internal event Action<bool, double>? EventsSidebarChanged;
    internal Func<string>? CaptureFolder { get; set; }
    internal bool EventsCollapsed { get; private set; }
    private double _eventsRatio = 0.5;

    internal void ConfigureEventsSidebar(bool collapsed, double ratio)
    {
        _eventsRatio = double.IsFinite(ratio) ? Math.Clamp(ratio, 0.1, 2) : 0.5;
        var moveFocus = collapsed && (EventsBody.IsKeyboardFocusWithin || EventsDivider.IsKeyboardFocusWithin);
        EventsCollapsed = collapsed;
        EventsBody.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;
        EventsHeading.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;
        CrashHeading.Visibility = !collapsed && _crashGroups.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        EventsRail.Visibility = collapsed ? Visibility.Visible : Visibility.Collapsed;
        EventsDivider.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;
        EventsColumn.MinWidth = collapsed ? 44 : 180;
        EventsColumn.Width = collapsed ? new GridLength(44) : new GridLength(_eventsRatio, GridUnitType.Star);
        EventsDividerColumn.Width = new GridLength(collapsed ? 0 : 10);
        HistoryDetailsColumn.Width = new GridLength(1, GridUnitType.Star);
        var label = collapsed ? Strings.FlightRecorderExpandEvents : Strings.FlightRecorderCollapseEvents;
        EventsToggle.ToolTip = label;
        AutomationProperties.SetName(EventsToggle, label);
        ButtonIcon.SetData(EventsToggle, collapsed ? ButtonIcon.ChevronLeft : ButtonIcon.ChevronRight);
        EventsToggle.SetExpanded(!collapsed);
        if (moveFocus) EventsToggle.Focus();
    }

    private void OnToggleEvents(object sender, RoutedEventArgs e)
    {
        ConfigureEventsSidebar(!EventsCollapsed, _eventsRatio);
        EventsSidebarChanged?.Invoke(EventsCollapsed, _eventsRatio);
    }

    private void OnEventsResized(object sender, DragCompletedEventArgs e)
    {
        RememberEventsRatio();
    }

    private void OnEventsResizeKeyUp(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key is System.Windows.Input.Key.Left or System.Windows.Input.Key.Right)
            RememberEventsRatio();
    }

    private void RememberEventsRatio()
    {
        if (EventsCollapsed || HistoryDetailsColumn.ActualWidth <= 0) return;
        _eventsRatio = Math.Clamp(EventsColumn.ActualWidth / HistoryDetailsColumn.ActualWidth, 0.1, 2);
        EventsSidebarChanged?.Invoke(false, _eventsRatio);
    }

    internal FlightRecorderWindow(IReadOnlyList<MonitorSample> samples,
        IReadOnlyDictionary<SampleKey, (IReadOnlyList<ProcessUsage> Started, IReadOnlyList<ProcessUsage> Stopped)>? changes = null,
        IReadOnlyList<ConnectionCapture>? traffic = null,
        Func<DateTimeOffset, DateTimeOffset, CancellationToken, Task<IncidentEventResult>>? eventReader = null,
        bool temperatureChartsEnabled = false)
    {
        _temperatureChartsEnabled = temperatureChartsEnabled;
        _metrics = DetailedMetrics();
        _trendMetrics = AllTrendMetrics.Where(metric => temperatureChartsEnabled || !ResourceTrendArchive.IsTemperature(metric)).ToArray();
        InitializeComponent();
        ConfigureEventsSidebar(false, 0.5);
        WindowTheme.Attach(this);
        Closing += (_, args) => args.Cancel = _fileBusy;
        Closed += (_, _) => { _closed = true; InvalidateEventRead(); };
        _eventReader = eventReader;
        Graph.VisibleMetrics = 63;
        Graph.MetricLabels = [Strings.CPU, Strings.Memory, Strings.Disk, Strings.Network, Strings.GPU, Strings.Disk];
        Graph.SampleSelected += (_, sample) => Select(Array.IndexOf(_samples, sample));
        Graph.LiveRequested += (_, _) => Select(_samples.Length - 1);
        Graph.MarkerSelected += (_, marker) => SelectEvent(marker.Tag as IncidentEventMapping);
        TrendGraph.BucketSelected += (_, bucket) => SelectTrend(bucket);
        HistoryMode.SelectedIndex = 0;
        Update(samples, changes, traffic);
    }

    internal void ConfigureTemperatureCharts(bool enabled)
    {
        if (_temperatureChartsEnabled == enabled) return;
        var metric = SelectedMetric;
        var trendMetric = SelectedTrendMetric;
        var selectedIndex = _index;
        _temperatureChartsEnabled = enabled;
        _metrics = DetailedMetrics();
        _trendMetrics = AllTrendMetrics.Where(item => enabled || !ResourceTrendArchive.IsTemperature(item)).ToArray();
        Metric.ItemsSource = _metrics.Select(item => CaptureReportText.MetricName(item, Selection?.Resources ?? new ResourceSample())).ToArray();
        Metric.SelectedIndex = Math.Max(0, Array.IndexOf(_metrics, metric));
        _updatingTrends = true;
        TrendMetric.ItemsSource = _trendMetrics.Select(TrendMetricName).ToArray();
        TrendMetric.SelectedIndex = Math.Max(0, Array.IndexOf(_trendMetrics, trendMetric));
        _updatingTrends = false;
        UpdateTrendPresentation();
        Select(selectedIndex);
    }

    internal void Update(IReadOnlyList<MonitorSample> samples,
        IReadOnlyDictionary<SampleKey, (IReadOnlyList<ProcessUsage> Started, IReadOnlyList<ProcessUsage> Stopped)>? changes = null,
        IReadOnlyList<ConnectionCapture>? traffic = null)
    {
        _openedRecording = false;
        InvalidateEventRead();
        var selected = Selection is { } previous ? SampleKey.Of(previous) : (SampleKey?)null;
        var metric = SelectedMetric;
        if (!_loadingRecording) _recordedMetric = null;
        _metrics = DetailedMetrics();
        _samples = samples.ToArray();
        _changes = changes;
        var keys = _samples.Select(SampleKey.Of).ToHashSet();
        _traffic = (traffic ?? []).Where(frame => keys.Contains(SampleKey.Of(frame.Sample)))
            .ToDictionary(frame => SampleKey.Of(frame.Sample));
        Source.Text = "";
        Coverage.Text = _samples.Length == 0 ? Strings.FlightRecorderEmpty :
            Strings.Format(Strings.FlightRecorderCoverage, _samples[0].Timestamp.ToLocalTime(),
                _samples[^1].Timestamp.ToLocalTime(), _samples.Length);
        Metric.ItemsSource = _metrics.Select(metric => CaptureReportText.MetricName(metric,
            _samples.LastOrDefault()?.Resources ?? new ResourceSample())).ToArray();
        Metric.SelectedIndex = Math.Max(0, Array.IndexOf(_metrics, metric));
        var retained = selected is { } key ? Array.FindIndex(_samples, key.Matches) : -1;
        if (retained >= 0) Select(retained);
        else FindPeak();
        BeginEventRead();
    }

    private CaptureMetric SelectedMetric => _metrics[Math.Clamp(Metric.SelectedIndex, 0, _metrics.Length - 1)];

    private CaptureMetric[] DetailedMetrics() => AllMetrics.Where(metric =>
        _temperatureChartsEnabled || !ThermalMetrics.IsTemperature(metric) || metric == _recordedMetric).ToArray();

    internal MonitorSample? Selection => _index >= 0 && _index < _samples.Length ? _samples[_index] : null;
    internal void SelectTrendMinuteForFixture(DateTimeOffset startUtc)
    {
        var index = Array.FindIndex(_trendBuckets, bucket => bucket.StartUtc == startUtc);
        if (index >= 0) TrendGraph.Select(index);
    }

    /// <summary>Replaces the frozen archive snapshot shown in Longer trends; it never changes detailed history.</summary>
    internal void UpdateTrends(ResourceTrendArchiveState? trends, string? notice = null)
    {
        var selectedStart = _trendSelection?.StartUtc;
        var rangeIndex = Math.Max(0, TrendRange.SelectedIndex);
        var metricIndex = Math.Max(0, TrendMetric.SelectedIndex);
        _trends = trends;
        _trendNotice = notice;
        _updatingTrends = true;
        _trendSelection = selectedStart is { } start ? trends?.Buckets.FirstOrDefault(bucket => bucket.StartUtc == start)
            ?? new ResourceTrendBucket(start, new Dictionary<ResourceTrendMetric, ResourceTrendValue>()) : null;
        TrendRange.ItemsSource = new[] { Strings.FlightRecorderTrend24Hours, Strings.FlightRecorderTrend7Days };
        TrendMetric.ItemsSource = _trendMetrics.Select(TrendMetricName).ToArray();
        TrendRange.SelectedIndex = rangeIndex;
        TrendMetric.SelectedIndex = metricIndex;
        _updatingTrends = false;
        UpdateTrendPresentation();
    }

    internal void Select(int index)
    {
        _index = _samples.Length == 0 ? -1 : Math.Clamp(index, 0, _samples.Length - 1);
        PeakButton.IsEnabled = IncidentTimeline.Peak(_samples, SelectedMetric) is not null;
        PreviousButton.IsEnabled = _index > 0;
        NextButton.IsEnabled = _index >= 0 && _index < _samples.Length - 1;
        var sample = Selection;
        SaveButton.IsEnabled = !_fileBusy && sample is not null;
        var seconds = _samples.Length < 2 ? 1 :
            Math.Max(1, (int)Math.Ceiling(CpuGraph.Moment(_samples[^1]) - CpuGraph.Moment(_samples[0])) + 1);
        Graph.ChartType = ThermalMetrics.IsTemperature(SelectedMetric) ? WinMon.Preferences.ResourceChartType.Temperature : WinMon.Preferences.ResourceChartType.Overlay;
        Graph.TemperatureUnit = TemperatureUnit;
        Graph.SetData(_samples, seconds, sample);
        if (sample is null)
        {
            SelectedTime.Text = Strings.FlightRecorderEmpty;
            Readings.Text = "";
            Processes.ItemsSource = null;
            Changes.Text = "";
            ComparisonSummary.Text = "";
            ComparisonProcesses.ItemsSource = null;
            UpdateEventPresentation();
            return;
        }
        SelectedTime.Text = sample.Timestamp.ToLocalTime().ToString("G", CultureInfo.CurrentCulture);
        Readings.Text = string.Join("   ·   ", AllMetrics.OrderByDescending(metric => metric == SelectedMetric).Select(metric =>
            CaptureReportText.MetricName(metric, sample.Resources) + " " +
            HistoricalValue(metric, sample)));
        var comparison = IncidentTimeline.Compare(_samples, sample, SelectedMetric, _traffic);
        ComparisonSummary.Text = CaptureReportText.MetricName(SelectedMetric, sample.Resources) + ": " +
            string.Join("   ·   ", new[] { (Strings.FlightRecorderBefore, comparison.Before),
                (Strings.FlightRecorderDuring, comparison.During), (Strings.FlightRecorderAfter, comparison.After) }
                .Select(period => period.Item1 + " " + Reading(period.Item2, false)));
        ComparisonProcesses.ItemsSource = comparison.Processes.Select(row => new
        {
            row.Process.Name, Pid = row.Process.ProcessId,
            Before = Reading(row.Before, true), During = Reading(row.During, true), After = Reading(row.After, true),
            Change = row.Change is { } delta ? (delta > 0 ? "+" : delta < 0 ? "−" : "") + Value(Math.Abs(delta), true) : "—"
        }).ToArray();
        var traffic = _traffic.GetValueOrDefault(SampleKey.Of(sample));
        var rates = IncidentTimeline.ProcessReadings(sample, CaptureMetric.Network, traffic)
            .ToDictionary(row => row.Process.Identity, row => row.Value);
        var contributors = IncidentTimeline.Contributors(sample, SelectedMetric, traffic).ToList();
        var contributorRows = contributors.Select(p => new ProcessContributor(p, p.Name, p.ProcessId,
            Percent(p.ObservedCpuPercent), p.WorkingSetBytes is { } memory ? TableMetricText.Memory(memory) : "—",
            p.IoBytesPerSecond is { } io ? TableMetricText.IoRate(io) : "—", Percent(p.GpuPercent),
            rates.GetValueOrDefault(p.Identity) is { } rate ? TableMetricText.IoRate(rate) : "—")).ToList();
        if (_selectedEvent?.LinkedProcess is { } linked && !contributors.Any(p => p.Identity == linked.Identity))
            contributorRows.Add(new ProcessContributor(linked, linked.Name, linked.ProcessId, "—", "—", "—", "—", "—"));
        Processes.ItemsSource = contributorRows;
        var changes = _changes is not null && _changes.TryGetValue(SampleKey.Of(sample), out var fullChanges)
            ? fullChanges : IncidentTimeline.Changes(_index > 0 ? _samples[_index - 1] : null, sample);
        Changes.Text = changes is { } observed
            ? Strings.Format(Strings.FlightRecorderChangeSummary, Names(observed.Started), Names(observed.Stopped))
            : Strings.FlightRecorderChangesUnavailable;
        if (sample.IsGap || sample.CompactedProcesses > 0 || sample.UnavailableProcesses > 0)
            Changes.Text += Environment.NewLine + Strings.Format(Strings.FlightRecorderPartial,
                sample.CompactedProcesses, sample.UnavailableProcesses);
        UpdateEventPresentation();
    }

    private string HistoricalValue(CaptureMetric metric, MonitorSample sample)
    {
        if (CaptureRules.Value(metric, sample) is not { } value) return "—";
        if (ThermalMetrics.IsTemperature(metric)) return GaugeReadoutText.Temperature(value, TemperatureUnit);
        var text = CaptureReportText.Value(metric, value);
        if (metric == CaptureMetric.CpuPerformanceLimited && sample.Resources.CpuPerformanceLimitPercent is { } limit)
            text += " (" + Strings.DetailPerformanceLimit + " " + Percent(limit) +
                (sample.Resources.CpuPerformanceLimitFlags is { } flags ? " · 0x" + flags.ToString("X", CultureInfo.InvariantCulture) : "") + ")";
        return text;
    }

    private static string Percent(double? value) => value is { } number
        ? number.ToString("F1", CultureInfo.CurrentCulture) + "%" : "—";

    private string Reading(IncidentTimeline.Reading reading, bool process) => reading.Average is { } value
        ? Strings.Format(Strings.FlightRecorderObservedMean, Value(value, process), reading.Observations) : "—";

    private string Value(double value, bool process) => !process ? ThermalMetrics.IsTemperature(SelectedMetric)
        ? GaugeReadoutText.Temperature(value, TemperatureUnit) : ThermalMetrics.IsThermal(SelectedMetric)
            ? Percent(value * 100) : CaptureReportText.Value(SelectedMetric, value) : SelectedMetric switch
    {
        CaptureMetric.Memory => TableMetricText.Memory((ulong)value),
        CaptureMetric.PrimaryDisk or CaptureMetric.SecondaryDisk or CaptureMetric.Network => TableMetricText.IoRate(value),
        _ => Percent(value)
    };

    private static string Names(IReadOnlyList<ProcessUsage> processes) => processes.Count == 0 ? "—" :
        string.Join(", ", processes.Select(p => $"{p.Name} ({p.ProcessId})"));

    private void FindPeak()
    {
        if (IncidentTimeline.Peak(_samples, SelectedMetric) is { } peak) Select(Array.IndexOf(_samples, peak));
        else Select(_index);
    }

    private void OnMetricChanged(object sender, SelectionChangedEventArgs e) => FindPeak();
    private void OnRefresh(object sender, RoutedEventArgs e) => RefreshRequested?.Invoke(this, EventArgs.Empty);

    private void OnHistoryModeChanged(object sender, SelectionChangedEventArgs e)
    {
        var trends = HistoryMode.SelectedIndex == 1;
        DetailTools.Visibility = Graph.Visibility = DetailSelection.Visibility = DetailPanels.Visibility = trends
            ? Visibility.Collapsed : Visibility.Visible;
        SaveButton.Visibility = OpenButton.Visibility = Coverage.Visibility = Source.Visibility = Changes.Visibility = trends
            ? Visibility.Collapsed : Visibility.Visible;
        TrendSurface.Visibility = trends ? Visibility.Visible : Visibility.Collapsed;
        if (trends) UpdateTrendPresentation();
    }

    private ResourceTrendMetric SelectedTrendMetric => _trendMetrics[Math.Clamp(TrendMetric.SelectedIndex, 0, _trendMetrics.Length - 1)];
    private TimeSpan SelectedTrendRange => TrendRange.SelectedIndex == 1 ? TimeSpan.FromDays(7) : TimeSpan.FromHours(24);

    private void OnTrendRangeChanged(object sender, SelectionChangedEventArgs e) { if (!_updatingTrends) UpdateTrendPresentation(); }
    private void OnTrendMetricChanged(object sender, SelectionChangedEventArgs e) { if (!_updatingTrends) UpdateTrendPresentation(); }
    private void OnTrendPrevious(object sender, RoutedEventArgs e) => MoveTrend(-1);
    private void OnTrendNext(object sender, RoutedEventArgs e) => MoveTrend(1);

    private void MoveTrend(int direction)
    {
        if (_trendBuckets.Length == 0) return;
        var index = _trendSelection is null ? direction < 0 ? _trendBuckets.Length - 1 : 0 :
            Math.Clamp(Array.IndexOf(_trendBuckets, _trendSelection) + direction, 0, _trendBuckets.Length - 1);
        TrendGraph.Select(index);
    }

    private void UpdateTrendPresentation()
    {
        TrendLegend.Text = SelectedTrendMetric == ResourceTrendMetric.Receive
            ? Strings.FlightRecorderTrendNetworkLegend + " · " + Strings.FlightRecorderTrendPeakLegend
            : ResourceTrendArchive.IsThermal(SelectedTrendMetric) ? Strings.ThermalTrendPeaks : Strings.FlightRecorderTrendPeakLegend;
        if (_trends is null)
        {
            TrendCoverage.Text = _trendNotice ?? Strings.FlightRecorderTrendEmpty;
            TrendSelection.Text = "";
            TrendPrevious.IsEnabled = TrendNext.IsEnabled = OpenTrendDetail.IsEnabled = false;
            TrendGraph.SetData([], SelectedTrendMetric, DateTimeOffset.UtcNow - SelectedTrendRange, DateTimeOffset.UtcNow, null);
            return;
        }
        var end = _trends.EndUtc;
        var start = end - SelectedTrendRange;
        var retained = _trends.Buckets.Where(bucket => bucket.StartUtc < end && bucket.StartUtc.AddMinutes(1) > start)
            .ToDictionary(bucket => bucket.StartUtc);
        if (retained.Count == 0) _trendBuckets = [];
        else
        {
            var first = Minute(start);
            var last = Minute(end);
            _trendBuckets = Enumerable.Range(0, (int)(last - first).TotalMinutes + 1).Select(offset =>
            {
                var minute = first.AddMinutes(offset);
                return retained.GetValueOrDefault(minute) ?? new ResourceTrendBucket(minute,
                    new Dictionary<ResourceTrendMetric, ResourceTrendValue>());
            }).ToArray();
        }
        if (_trendSelection is { } selection)
            _trendSelection = _trendBuckets.MinBy(bucket => Math.Abs((bucket.StartUtc - selection.StartUtc).Ticks));
        var observed = _trendBuckets.Where(bucket => bucket.Values.TryGetValue(SelectedTrendMetric, out var value) && value.Average is not null)
            .Sum(bucket => bucket.Values[SelectedTrendMetric].ObservedSeconds);
        var coverage = _trendBuckets.Length == 0 ? Strings.FlightRecorderTrendEmpty :
            Strings.Format(Strings.FlightRecorderTrendCoverage, start.ToLocalTime(), end.ToLocalTime(),
                "≈" + Math.Min(SelectedTrendRange.TotalMinutes, observed / 60).ToString("N1", CultureInfo.CurrentCulture),
                SelectedTrendRange.TotalMinutes);
        TrendCoverage.Text = _trendNotice is { Length: > 0 } ? coverage + Environment.NewLine + _trendNotice : coverage;
        TrendGraph.TemperatureUnit = TemperatureUnit;
        TrendGraph.SetData(_trendBuckets, SelectedTrendMetric, start, end, _trendSelection);
        if (_trendSelection is null && _trendBuckets.Length > 0) TrendGraph.Select(_trendBuckets.Length - 1);
        else UpdateTrendSelection();
    }

    private void SelectTrend(ResourceTrendBucket bucket)
    {
        _trendSelection = bucket;
        UpdateTrendSelection();
    }

    private void UpdateTrendSelection()
    {
        if (_trendSelection is not { } bucket)
        {
            TrendSelection.Text = "";
            TrendPrevious.IsEnabled = TrendNext.IsEnabled = OpenTrendDetail.IsEnabled = false;
            return;
        }
        var value = bucket.Values.TryGetValue(SelectedTrendMetric, out var known) ? known : null;
        TrendSelection.Text = value?.Average is { } average
            ? Strings.Format(Strings.FlightRecorderTrendSelected, TrendMinuteTime(bucket), TrendValue(average), value.ObservedSeconds) +
              TrendSupplement(bucket) + (value.DeviceLabel.Length > 0 ? " · " + Strings.Format(Strings.FlightRecorderTrendDevice, value.DeviceLabel) : "")
              + (value.Break != ResourceTrendBreak.None ? " · " + TrendBreakText(value.Break) : "")
              + (bucket.StartUtc.AddMinutes(1) > _trends?.EndUtc ? " · " + Strings.FlightRecorderTrendInProgress : "")
            : Strings.Format(Strings.FlightRecorderTrendSelected, TrendMinuteTime(bucket), TrendBreakText(value?.Break), 0);
        TrendSelection.Text += TrendPeakText(value);
        if (SelectedTrendMetric == ResourceTrendMetric.Receive)
        {
            var upload = bucket.Values.GetValueOrDefault(ResourceTrendMetric.Send);
            TrendSelection.Text = Strings.FlightRecorderTrendReceive + ": " + TrendSelection.Text + Environment.NewLine +
                Strings.FlightRecorderTrendSend + ": " + Strings.Format(Strings.FlightRecorderTrendSelected,
                    TrendMinuteTime(bucket), upload?.Average is { } rate ? TableMetricText.IoRate(rate) : TrendBreakText(upload?.Break),
                    upload?.Average is not null ? upload.ObservedSeconds : 0) + TrendPeakText(upload);
        }
        var index = Array.IndexOf(_trendBuckets, bucket);
        TrendPrevious.IsEnabled = index > 0;
        TrendNext.IsEnabled = index >= 0 && index < _trendBuckets.Length - 1;
        var hasDetail = NearestDetailedSample(bucket) is not null;
        var canLookUp = CaptureFolder is not null && KnownPeak(bucket) is not null;
        OpenTrendDetail.Content = !hasDetail && canLookUp ? Strings.FlightRecorderTrendFindIncident : Strings.FlightRecorderOpenDetailed;
        OpenTrendDetail.IsEnabled = !_findingTrendIncident && (hasDetail || canLookUp);
    }

    private string TrendPeakText(ResourceTrendValue? value) => value is { Average: not null, PeakUtcTicks: { } ticks }
        ? " · " + Strings.Format(Strings.FlightRecorderTrendPeak, TrendValue(value.Maximum), new DateTimeOffset(ticks, TimeSpan.Zero).ToLocalTime())
        : "";

    private static string TrendMinuteTime(ResourceTrendBucket bucket) => bucket.StartUtc.ToLocalTime().ToString("G", CultureInfo.CurrentCulture)
        + " – " + bucket.StartUtc.AddMinutes(1).ToLocalTime().ToString("T", CultureInfo.CurrentCulture)
        + " (" + bucket.StartUtc.ToLocalTime().ToString("zzz", CultureInfo.CurrentCulture) + ")";

    private async void OnOpenTrendDetail(object sender, RoutedEventArgs e)
    {
        if (_trendSelection is not { } bucket || _findingTrendIncident) return;
        if (NearestDetailedSample(bucket) is { } sample)
        {
            HistoryMode.SelectedIndex = 0;
            Metric.SelectedIndex = Math.Max(0, Array.IndexOf(_metrics, DetailMetric(SelectedTrendMetric)));
            Select(Array.IndexOf(_samples, sample));
            return;
        }
        if (KnownPeak(bucket) is not { } peak || CaptureFolder?.Invoke() is not { } folder) return;
        var metric = SelectedTrendMetric;
        _findingTrendIncident = true;
        OpenTrendDetail.IsEnabled = false;
        var missing = false;
        try
        {
            var match = await Task.Run(() => FindTrendIncident(folder, peak.Metric, peak.Value));
            if (_closed || _fileBusy || HistoryMode.SelectedIndex != 1 || _trendSelection != bucket || SelectedTrendMetric != metric) return;
            if (match is { } found) OpenRecording(found.Recording, found.Name);
            else missing = true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            if (!_closed) FileError(ex);
        }
        finally
        {
            _findingTrendIncident = false;
            if (!_closed) UpdateTrendSelection();
        }
        if (missing) TrendSelection.Text += Environment.NewLine + Strings.FlightRecorderTrendIncidentMissing;
    }

    private (ResourceTrendMetric Metric, ResourceTrendValue Value)? KnownPeak(ResourceTrendBucket bucket)
    {
        var metric = SelectedTrendMetric;
        var value = bucket.Values.GetValueOrDefault(metric);
        if (metric == ResourceTrendMetric.Receive && bucket.Values.GetValueOrDefault(ResourceTrendMetric.Send) is
            { Average: not null, PeakUtcTicks: not null } upload &&
            (value is not { Average: not null, PeakUtcTicks: not null } || upload.Maximum > value.Maximum))
        {
            metric = ResourceTrendMetric.Send;
            value = upload;
        }
        return value is { Average: not null, PeakUtcTicks: not null } ? (metric, value) : null;
    }

    private MonitorSample? NearestDetailedSample(ResourceTrendBucket bucket)
    {
        if (_openedRecording) return null;
        if (KnownPeak(bucket) is { } peak)
            return _samples.FirstOrDefault(sample => IsTrendPeak(sample, peak.Metric, peak.Value));
        var end = bucket.StartUtc.AddMinutes(1);
        return _samples.Where(sample => !sample.IsGap && sample.Timestamp >= bucket.StartUtc && sample.Timestamp < end &&
                (TrendSampleValue(sample, SelectedTrendMetric) is not null ||
                 (SelectedTrendMetric == ResourceTrendMetric.Receive && sample.Resources.SendBytesPerSecond is not null)))
            .MinBy(sample => Math.Abs((sample.Timestamp.ToUniversalTime() - bucket.StartUtc.AddSeconds(30)).Ticks));
    }

    private static bool IsTrendPeak(MonitorSample sample, ResourceTrendMetric metric, ResourceTrendValue peak) =>
        !sample.IsGap && sample.Timestamp.UtcTicks == peak.PeakUtcTicks && TrendSampleValue(sample, metric) == peak.Maximum;

    internal static (IncidentRecording Recording, string Name)? FindTrendIncident(string folder, ResourceTrendMetric metric,
        ResourceTrendValue peak)
    {
        if (peak.PeakUtcTicks is not { } ticks) return null;
        foreach (var path in CaptureReports.FindIncidentsCovering(folder, ticks).Take(CaptureReports.Keep))
        {
            try
            {
                var recording = IncidentRecording.Load(path);
                var index = Array.FindIndex(recording.Observations, item => IsTrendPeak(item.Sample, metric, peak));
                if (index >= 0) return (recording with { Metric = DetailMetric(metric), SelectedIndex = index }, Path.GetFileName(path));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or JsonException or OverflowException)
            {
                Trace.TraceWarning("WinMon could not read a saved incident for a trend peak: {0}", ex.Message);
            }
        }
        return null;
    }

    private static CaptureMetric DetailMetric(ResourceTrendMetric metric) => metric switch
    {
        ResourceTrendMetric.MemoryBytes => CaptureMetric.Memory,
        ResourceTrendMetric.PrimaryDiskActive => CaptureMetric.PrimaryDisk,
        ResourceTrendMetric.SecondaryDiskActive => CaptureMetric.SecondaryDisk,
        ResourceTrendMetric.Receive or ResourceTrendMetric.Send => CaptureMetric.Network,
        _ => Enum.TryParse<CaptureMetric>(metric.ToString(), out var capture) ? capture : CaptureMetric.Cpu
    };

    private string TrendMetricName(ResourceTrendMetric metric) => metric switch
    {
        ResourceTrendMetric.Cpu => Strings.CPU,
        ResourceTrendMetric.MemoryBytes => Strings.Memory,
        ResourceTrendMetric.PrimaryDiskActive => Strings.FlightRecorderTrendPrimaryDisk,
        ResourceTrendMetric.SecondaryDiskActive => Strings.FlightRecorderTrendSecondaryDisk,
        ResourceTrendMetric.Receive => Strings.Network,
        ResourceTrendMetric.Gpu => Strings.GPU,
        _ => Enum.TryParse<CaptureMetric>(metric.ToString(), out var capture) && ThermalMetrics.IsThermal(capture)
            ? CaptureReportText.MetricName(capture, new ResourceSample()) : Strings.Unavailable
    };

    private string TrendValue(double value) => ResourceTrendArchive.IsTemperature(SelectedTrendMetric)
        ? GaugeReadoutText.Temperature(value, TemperatureUnit)
        : SelectedTrendMetric is ResourceTrendMetric.CpuPerformanceLimited or ResourceTrendMetric.GpuThermalLimited
            ? value > 0 ? Strings.SettingsOn : Strings.SettingsOff : SelectedTrendMetric switch
    {
        ResourceTrendMetric.MemoryBytes => TableMetricText.Memory((ulong)value),
        ResourceTrendMetric.Receive or ResourceTrendMetric.Send or ResourceTrendMetric.PrimaryDiskRead or
            ResourceTrendMetric.PrimaryDiskWrite or ResourceTrendMetric.SecondaryDiskRead or ResourceTrendMetric.SecondaryDiskWrite =>
            TableMetricText.IoRate(value),
        _ => value.ToString("F1", CultureInfo.CurrentCulture) + "%"
    };

    private string TrendBreakText(ResourceTrendBreak? value) => value switch
    {
        ResourceTrendBreak.ResourceChanged => Strings.FlightRecorderTrendDeviceChanged,
        ResourceTrendBreak.ClockChanged => Strings.FlightRecorderTrendClockChanged,
        ResourceTrendBreak.CollectionRestart => Strings.FlightRecorderTrendRestart,
        _ => Strings.FlightRecorderTrendNoReading
    };

    private string TrendSupplement(ResourceTrendBucket bucket)
    {
        if (SelectedTrendMetric == ResourceTrendMetric.MemoryBytes && bucket.Values.TryGetValue(ResourceTrendMetric.MemoryPercent, out var memory) &&
            memory.Average is { } percent) return " · " + percent.ToString("F1", CultureInfo.CurrentCulture) + "%";
        var reads = SelectedTrendMetric == ResourceTrendMetric.PrimaryDiskActive ? ResourceTrendMetric.PrimaryDiskRead :
            SelectedTrendMetric == ResourceTrendMetric.SecondaryDiskActive ? ResourceTrendMetric.SecondaryDiskRead : (ResourceTrendMetric?)null;
        var writes = SelectedTrendMetric == ResourceTrendMetric.PrimaryDiskActive ? ResourceTrendMetric.PrimaryDiskWrite :
            SelectedTrendMetric == ResourceTrendMetric.SecondaryDiskActive ? ResourceTrendMetric.SecondaryDiskWrite : (ResourceTrendMetric?)null;
        return reads is { } read && writes is { } write && bucket.Values.GetValueOrDefault(read)?.Average is { } received &&
               bucket.Values.GetValueOrDefault(write)?.Average is { } writeRate
            ? " · ↓" + TableMetricText.IoRate(received) + " · ↑" + TableMetricText.IoRate(writeRate) : "";
    }

    private static double? TrendSampleValue(MonitorSample sample, ResourceTrendMetric metric) => metric switch
    {
        ResourceTrendMetric.Cpu => sample.TotalCpuPercent,
        ResourceTrendMetric.MemoryBytes => sample.Resources.UsedMemoryBytes,
        ResourceTrendMetric.PrimaryDiskActive => sample.Resources.DiskActivePercent,
        ResourceTrendMetric.SecondaryDiskActive => sample.Resources.SecondaryDiskActivePercent,
        ResourceTrendMetric.Receive => sample.Resources.ReceiveBytesPerSecond,
        ResourceTrendMetric.Send => sample.Resources.SendBytesPerSecond,
        ResourceTrendMetric.Gpu => sample.Resources.GpuPercent,
        _ => Enum.TryParse<CaptureMetric>(metric.ToString(), out var capture) ? ThermalMetrics.Value(capture, sample.Resources) : null
    };

    private static DateTimeOffset Minute(DateTimeOffset value) => new(value.UtcDateTime.AddTicks(
        -(value.UtcDateTime.Ticks % TimeSpan.TicksPerMinute)));

    internal IncidentRecording Recording(int secondsBeforeAndAfter = 0)
    {
        var first = 0;
        var end = _samples.Length;
        var events = _eventReadPending ? IncidentEventResult.Unknown : _events;
        if (secondsBeforeAndAfter > 0 && Selection is { } selected)
        {
            first = _index;
            end = _index + 1;
            while (first > 0 && WithinSaveRange(_samples[first - 1], selected, secondsBeforeAndAfter)) first--;
            while (end < _samples.Length && WithinSaveRange(_samples[end], selected, secondsBeforeAndAfter)) end++;

            // Event logs use wall time, even when the observations are ordered by the monotonic clock.
            var samples = _samples.AsSpan(first, end - first);
            var earliest = selected.Timestamp;
            var latest = earliest;
            foreach (var sample in samples)
            {
                if (sample.Timestamp < earliest) earliest = sample.Timestamp;
                if (sample.Timestamp > latest) latest = sample.Timestamp;
            }
            events = events with { Events = events.Events.Where(item => item.Timestamp >= earliest && item.Timestamp <= latest).ToArray() };
        }

        var observations = new IncidentObservation[end - first];
        for (var index = first; index < end; index++)
        {
            var sample = _samples[index];
            var capture = _traffic.GetValueOrDefault(SampleKey.Of(sample));
            var changes = _changes is not null && _changes.TryGetValue(SampleKey.Of(sample), out var known)
                ? known : IncidentTimeline.Changes(index > 0 ? _samples[index - 1] : null, sample);
            observations[index - first] = new IncidentObservation(sample, capture?.Network is { } network
                ? network with { Flows = [], CurrentFlows = null } : null, capture?.Owners ?? [],
                changes?.Started, changes?.Stopped);
        }
        return new IncidentRecording(IncidentRecording.CurrentVersion, Stopwatch.Frequency,
            observations, SelectedMetric, _index - first) { WindowsEvents = events };
    }

    private static bool WithinSaveRange(MonitorSample sample, MonitorSample selected, int seconds) =>
        sample.MonotonicTimestamp > 0 && selected.MonotonicTimestamp > 0
            ? Math.Abs(sample.MonotonicTimestamp - selected.MonotonicTimestamp) <= seconds * Stopwatch.Frequency
            : Math.Abs((sample.Timestamp - selected.Timestamp).Ticks) <= seconds * TimeSpan.TicksPerSecond;

    internal void OpenRecording(IncidentRecording recording, string name)
    {
        InvalidateEventRead();
        _loadingRecording = true;
        _recordedMetric = recording.Metric;
        var observations = recording.Observations;
        var changes = observations.Where(frame => frame.Started is not null && frame.Stopped is not null)
            .ToDictionary(frame => SampleKey.Of(frame.Sample), frame => (frame.Started!, frame.Stopped!));
        Update(observations.Select(frame => frame.Sample).ToArray(), changes,
            observations.Select(frame => new ConnectionCapture(frame.Sample, null, frame.Owners, frame.Network)).ToArray());
        _loadingRecording = false;
        _openedRecording = true;
        HistoryMode.SelectedIndex = 0;
        Metric.SelectedIndex = Math.Max(0, Array.IndexOf(_metrics, recording.Metric));
        Select(recording.SelectedIndex);
        Source.Text = Strings.Format(Strings.FlightRecorderOpened, name);
        ApplyEvents(recording.WindowsEvents);
    }

    // WPF event handlers await file work so sampling and painting remain responsive.
    private async void OnSave(object sender, RoutedEventArgs e)
    {
        try
        {
            if (Selection is null) return;
            var dialog = new FlightRecorderSaveDialog(SelectedTime.Text) { Owner = this };
            if (dialog.ShowDialog() != true || dialog.Choice is not { } choice) return;
            await SaveRecordingAsync(choice.Path, choice.SecondsBeforeAndAfter, choice.Overwrite);
        }
        catch (Exception ex) { FileError(ex); }
    }

    internal async Task SaveRecordingAsync(string path, int secondsBeforeAndAfter, bool overwrite = false)
    {
        var recording = Recording(secondsBeforeAndAfter);
        SetFileBusy(true);
        var write = Task.Run(() => recording.Save(path, overwrite));
        SaveStarted?.Invoke(write);
        try { await write; }
        finally
        {
            SaveFinished?.Invoke(write);
            SetFileBusy(false);
        }
    }

    private async void OnOpen(object sender, RoutedEventArgs e)
    {
        try
        {
            var dialog = new OpenFileDialog { Title = Strings.FlightRecorderOpen, Filter = Strings.FlightRecorderFilter };
            if (CaptureFolder?.Invoke() is { } folder && Directory.Exists(folder)) dialog.InitialDirectory = folder;
            if (dialog.ShowDialog(this) != true) return;
            await OpenRecordingAsync(dialog.FileName);
        }
        catch (Exception ex) { FileError(ex); }
    }

    internal async Task OpenRecordingAsync(string path)
    {
        SetFileBusy(true);
        try
        {
            var recording = await Task.Run(() => IncidentRecording.Load(path));
            OpenRecording(recording, Path.GetFileName(path));
        }
        finally { SetFileBusy(false); }
    }

    private void SetFileBusy(bool busy)
    {
        _fileBusy = busy;
        OpenButton.IsEnabled = RefreshButton.IsEnabled = !busy;
        SaveButton.IsEnabled = !busy && Selection is not null;
    }

    private void FileError(Exception error)
    {
        if (Owner is MainWindow { IsClosing: true }) return;
        MessageDialog.ShowNotice(this, Strings.Format(Strings.FlightRecorderFileError, error.Message), Strings.FlightRecorderTitle);
    }
    private void OnPeak(object sender, RoutedEventArgs e) => FindPeak();
    private void OnPrevious(object sender, RoutedEventArgs e) => Select(_index - 1);
    private void OnNext(object sender, RoutedEventArgs e) => Select(_index + 1);
    private void OnClose(object sender, RoutedEventArgs e) => Close();
    private void OnKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != System.Windows.Input.Key.Escape) return;
        e.Handled = true;
        Close();
    }

    internal IReadOnlyList<IncidentEventMapping> EventMappings => _eventMappings;
    internal IReadOnlyList<CrashGroup> CrashSummaryGroups => _crashGroups;
    internal bool EventReadPending => _eventReadPending;
    internal string EventCoverageTextValue => EventCoverageText.Text;
    internal void SelectEventForFixture(IncidentEventMapping item) => SelectEvent(item);

    private void BeginEventRead()
    {
        if (_eventReader is null || _openedRecording || _loadingRecording || _samples.Length == 0) { ApplyEvents(IncidentEventResult.Unknown); return; }
        var start = _samples.Min(sample => sample.Timestamp).ToUniversalTime();
        var end = _samples.Max(sample => sample.Timestamp).ToUniversalTime();
        var generation = ++_eventReadGeneration;
        var cancellation = new CancellationTokenSource();
        _eventReadCancellation = cancellation;
        _eventReadPending = true;
        ApplyEvents(IncidentEventResult.Unknown);
        _ = ReadEventsAsync(start, end, generation, cancellation);
    }

    private async Task ReadEventsAsync(DateTimeOffset start, DateTimeOffset end, int generation, CancellationTokenSource cancellation)
    {
        Task<IncidentEventResult>? read = null;
        try
        {
            read = _eventReader!(start, end, cancellation.Token);
            var result = await read.WaitAsync(TimeSpan.FromSeconds(5), cancellation.Token);
            if (generation != _eventReadGeneration || cancellation.IsCancellationRequested || _openedRecording || _closed) return;
            _eventReadPending = false;
            ApplyEvents(result);
        }
        catch (TimeoutException)
        {
            cancellation.Cancel();
            if (generation != _eventReadGeneration || _openedRecording || _closed) return;
            _eventReadPending = false;
            ApplyEvents(new IncidentEventResult([], [new("Application", IncidentEventCoverageStatus.TimedOut),
                new("System", IncidentEventCoverageStatus.TimedOut)], false));
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch
        {
            if (generation != _eventReadGeneration || cancellation.IsCancellationRequested || _openedRecording || _closed) return;
            _eventReadPending = false;
            ApplyEvents(new IncidentEventResult([], [new("Application", IncidentEventCoverageStatus.Unreadable),
                new("System", IncidentEventCoverageStatus.Unreadable)], false));
        }
        finally
        {
            if (ReferenceEquals(_eventReadCancellation, cancellation)) _eventReadCancellation = null;
            if (read is { IsCompleted: false })
                _ = read.ContinueWith(completed =>
                {
                    _ = completed.Exception;
                    cancellation.Dispose();
                }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            else cancellation.Dispose();
        }
    }

    private void InvalidateEventRead()
    {
        ++_eventReadGeneration;
        _eventReadCancellation?.Cancel();
        _eventReadCancellation = null;
        _eventReadPending = false;
    }

    private void ApplyEvents(IncidentEventResult result)
    {
        _events = result;
        var retained = (_changes?.Values.SelectMany(change => change.Started.Concat(change.Stopped)) ?? Enumerable.Empty<ProcessUsage>())
            .Concat(_traffic.Values.SelectMany(capture => capture.Owners));
        _eventMappings = IncidentEventMapper.Map(result, _samples, retained);
        if (_selectedEvent is { } selected && !_eventMappings.Any(item => item.Event == selected.Event)) _selectedEvent = null;
        _crashGroups = CrashGroups.Build(result.Events);
        if (_selectedCrashGroup is { } selectedGroup)
            _selectedCrashGroup = _crashGroups.FirstOrDefault(group => group.Key == selectedGroup.Key);
        RefreshMarkerSelection();
        UpdateEventPresentation();
    }

    private void UpdateEventPresentation()
    {
        var selected = Selection;
        var moment = selected is null ? double.NaN : CpuGraph.Moment(selected);
        var rows = double.IsNaN(moment) ? Array.Empty<EventRow>() : _eventMappings.Where(item => item.Moment >= moment - 15 && item.Moment < moment + 15)
            .Select(item => new EventRow(item, EventSummary(item), EventMessage(item.Event))).ToArray();
        IReadOnlyList<EventRow> displayed = rows.Length == 0 && IsComplete(_events)
            ? [new EventRow(null, Strings.FlightRecorderEventNoEvents, "")]
            : rows;
        _updatingEvents = true;
        try
        {
            Events.ItemsSource = displayed;
            EventsCount.Text = rows.Length.ToString(CultureInfo.CurrentCulture);
            Events.SelectedItem = _selectedEvent is null ? null :
                displayed.FirstOrDefault(row => row.Mapping?.Event == _selectedEvent.Event);
        }
        finally { _updatingEvents = false; }
        UpdateCrashPresentation();
        EventCoverageText.Text = FormatEventCoverage(_events);
        SelectedEvent.Text = _selectedEvent is null ? "" : EventTooltip(_selectedEvent);
        if (_selectedEvent?.LinkedProcess is { } linked)
        {
            var row = (Processes.ItemsSource as IEnumerable<ProcessContributor>)?.FirstOrDefault(item => item.Process.Identity == linked.Identity);
            if (row is not null)
            {
                Processes.SelectedItem = row;
                Processes.ScrollIntoView(row);
            }
        }
    }

    private void SelectEvent(IncidentEventMapping? item)
    {
        if (item is null) return;
        _selectedEvent = item;
        if (item.LinkedProcess is not null) Views.SelectedIndex = 1;
        // Select refreshes the event list; the mappings themselves are unchanged, so they are not rebuilt.
        Select(item.ObservationIndex ?? _index);
        RefreshMarkerSelection();
    }

    private void OnEventSelected(object sender, SelectionChangedEventArgs e)
    {
        if (!_updatingEvents && Events.SelectedItem is EventRow { Mapping: { } item }) SelectEvent(item);
    }

    private void UpdateCrashPresentation()
    {
        var rows = _crashGroups.Select(group =>
        {
            var failure = CrashFailure(group);
            var details = Strings.Format(Strings.FlightRecorderCrashDetails, group.Count, group.LastTime.ToLocalTime());
            var summary = string.Join(" · ", group.Key.Application, failure, details);
            return new CrashRow(group, group.Key.Application, failure, details, summary, summary,
                Strings.Format(Strings.FlightRecorderCrashCompactDetails, group.Count, group.LastTime.ToLocalTime()));
        }).ToArray();
        _updatingCrashes = true;
        try
        {
            Crashes.ItemsSource = rows;
            Crashes.Visibility = rows.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
            CrashHeading.Visibility = rows.Length == 0 || EventsCollapsed ? Visibility.Collapsed : Visibility.Visible;
            Crashes.SelectedItem = _selectedCrashGroup is null ? null : rows.FirstOrDefault(row => row.Group.Key == _selectedCrashGroup.Key);
        }
        finally { _updatingCrashes = false; }
    }

    private static string CrashFailure(CrashGroup group)
    {
        if (group.Key.Kind == CrashKind.Hang) return Strings.FlightRecorderCrashNotResponding;
        var code = group.Key.ExceptionCode is { } exception ? "0x" + exception.ToString("X8", CultureInfo.InvariantCulture) : "";
        var named = ExceptionName(group.Key.ExceptionCode);
        var exceptionText = named is null ? code : code + " (" + named + ")";
        return string.Join(" · ", new[] { group.Key.Module, exceptionText }.Where(text => !string.IsNullOrEmpty(text)));
    }

    private static string? ExceptionName(uint? code) => code switch
    {
        0xC0000005 => Strings.FlightRecorderCrashAccessViolation,
        0xC0000409 => Strings.FlightRecorderCrashStackBufferOverrun,
        0xE0434352 => Strings.FlightRecorderCrashUnhandledDotNetException,
        _ => null
    };

    private void OnCrashSelected(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingCrashes) return;
        if (Crashes.SelectedItem is not CrashRow row)
        {
            _selectedCrashGroup = null;
            _selectedEvent = null;
            RefreshMarkerSelection();
            UpdateEventPresentation();
            return;
        }
        _selectedCrashGroup = row.Group;
        var item = _eventMappings.LastOrDefault(mapping => row.Group.Events.Any(member => ReferenceEquals(member, mapping.Event)));
        if (item is not null) SelectEvent(item);
        else RefreshMarkerSelection();
    }

    private static ChartMarkerPattern EventPattern(IncidentEventLevel level) => level switch
    {
        IncidentEventLevel.Error => ChartMarkerPattern.Diamond,
        IncidentEventLevel.Warning => ChartMarkerPattern.Triangle,
        _ => ChartMarkerPattern.Square
    };
    private static string EventBrush(IncidentEventLevel level) => level switch
    {
        IncidentEventLevel.Error => "DiskAccent", IncidentEventLevel.Warning => "SecondaryDiskAccent", _ => "NetworkAccent"
    };
    private static string EventLevel(IncidentEventLevel level) => level switch
    {
        IncidentEventLevel.Error => Strings.FlightRecorderEventError, IncidentEventLevel.Warning => Strings.FlightRecorderEventWarning,
        IncidentEventLevel.Information => Strings.FlightRecorderEventInformation, _ => Strings.FlightRecorderEventUnknownLevel
    };
    private static string EventSummary(IncidentEventMapping item) => $"{item.Event.Timestamp.ToLocalTime():G} · {EventLevel(item.Event.Level)} · {item.Event.Provider} ({item.Event.EventId})";
    private static string EventMessage(IncidentEvent item) => item.Message.Length == 0 ? Strings.MissingReading : item.Message;
    private static string EventTooltip(IncidentEventMapping item) => EventSummary(item) + Environment.NewLine +
        (item.ObservationIndex is null || item.Approximate ? string.Join(" · ",
            new[] { item.ObservationIndex is null ? Strings.FlightRecorderEventBetweenObservations : "",
                item.Approximate ? Strings.FlightRecorderEventApproximate : "" }.Where(text => text.Length > 0)) + " · " : "") + EventMessage(item.Event);
    private void RefreshMarkerSelection()
    {
        Graph.Markers = _eventMappings.OrderBy(item => item.Moment).Select(item => new ChartMarker(item.Moment, EventBrush(item.Event.Level), EventTooltip(item),
            item.Event == _selectedEvent?.Event || _selectedCrashGroup?.Events.Any(member => ReferenceEquals(member, item.Event)) == true,
            item, EventPattern(item.Event.Level))).ToArray();
        Graph.InvalidateVisual();
    }
    private string FormatEventCoverage(IncidentEventResult result)
    {
        if (_eventReadPending) return Strings.Format(Strings.FlightRecorderEventCoverage, Strings.FlightRecorderEventReading);
        var detail = string.Join(Environment.NewLine, result.Coverage.Select(item => Strings.Format(Strings.FlightRecorderEventLogCoverage,
            item.Log, CoverageStatus(item.Status))));
        if (result.LimitReached) detail += Environment.NewLine + Strings.Format(Strings.FlightRecorderEventLimit, result.Events.Count);
        return detail;
    }

    private static bool IsComplete(IncidentEventResult result) => result.Coverage.Count > 0 &&
        result.Coverage.All(item => item.Status == IncidentEventCoverageStatus.Complete);
    private static string CoverageStatus(IncidentEventCoverageStatus status) => status switch
    {
        IncidentEventCoverageStatus.Complete => Strings.FlightRecorderEventComplete,
        IncidentEventCoverageStatus.Unreadable => Strings.FlightRecorderEventUnreadable,
        IncidentEventCoverageStatus.TimedOut => Strings.FlightRecorderEventTimedOut,
        _ => Strings.Unknown
    };

    private sealed record ProcessContributor(ProcessUsage Process, string Name, int Pid, string Cpu, string Memory, string Io, string Gpu, string Network);
    private sealed record EventRow(IncidentEventMapping? Mapping, string Summary, string Message);
    private sealed record CrashRow(CrashGroup Group, string Application, string Failure, string Details, string Summary,
        string AccessibleName, string CountText);
}
