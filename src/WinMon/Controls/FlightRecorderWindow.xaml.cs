using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using WinMon.Monitoring;

namespace WinMon.Controls;

internal partial class FlightRecorderWindow : Window
{
    private MonitorSample[] _samples = [];
    private readonly CaptureMetric[] _metrics = [CaptureMetric.Cpu, CaptureMetric.Memory, CaptureMetric.PrimaryDisk,
        CaptureMetric.SecondaryDisk, CaptureMetric.Network, CaptureMetric.Gpu];
    private int _index;
    private IReadOnlyDictionary<SampleKey, (IReadOnlyList<ProcessUsage> Started, IReadOnlyList<ProcessUsage> Stopped)>? _changes;

    internal event EventHandler? RefreshRequested;

    internal FlightRecorderWindow(IReadOnlyList<MonitorSample> samples,
        IReadOnlyDictionary<SampleKey, (IReadOnlyList<ProcessUsage> Started, IReadOnlyList<ProcessUsage> Stopped)>? changes = null)
    {
        InitializeComponent();
        WindowTheme.Attach(this);
        Graph.VisibleMetrics = 63;
        Graph.MetricLabels = [Strings.CPU, Strings.Memory, Strings.Disk, Strings.Network, Strings.GPU, Strings.Disk];
        Graph.SampleSelected += (_, sample) => Select(Array.IndexOf(_samples, sample));
        Graph.LiveRequested += (_, _) => Select(_samples.Length - 1);
        Update(samples, changes);
    }

    internal void Update(IReadOnlyList<MonitorSample> samples,
        IReadOnlyDictionary<SampleKey, (IReadOnlyList<ProcessUsage> Started, IReadOnlyList<ProcessUsage> Stopped)>? changes = null)
    {
        var selected = Selection is { } previous ? SampleKey.Of(previous) : (SampleKey?)null;
        var metricIndex = Math.Max(0, Metric.SelectedIndex);
        _samples = samples.ToArray();
        _changes = changes;
        Coverage.Text = _samples.Length == 0 ? Strings.FlightRecorderEmpty :
            Strings.Format(Strings.FlightRecorderCoverage, _samples[0].Timestamp.ToLocalTime(),
                _samples[^1].Timestamp.ToLocalTime(), _samples.Length);
        Metric.ItemsSource = _metrics.Select(metric => CaptureReportText.MetricName(metric,
            _samples.LastOrDefault()?.Resources ?? new ResourceSample())).ToArray();
        Metric.SelectedIndex = metricIndex;
        var retained = selected is { } key ? Array.FindIndex(_samples, key.Matches) : -1;
        if (retained >= 0) Select(retained);
        else FindPeak();
    }

    private CaptureMetric SelectedMetric => _metrics[Math.Max(0, Metric.SelectedIndex)];

    internal MonitorSample? Selection => _index >= 0 && _index < _samples.Length ? _samples[_index] : null;

    internal void Select(int index)
    {
        _index = _samples.Length == 0 ? -1 : Math.Clamp(index, 0, _samples.Length - 1);
        PeakButton.IsEnabled = IncidentTimeline.Peak(_samples, SelectedMetric) is not null;
        PreviousButton.IsEnabled = _index > 0;
        NextButton.IsEnabled = _index >= 0 && _index < _samples.Length - 1;
        var sample = Selection;
        var seconds = _samples.Length < 2 ? 1 :
            Math.Max(1, (int)Math.Ceiling(CpuGraph.Moment(_samples[^1]) - CpuGraph.Moment(_samples[0])) + 1);
        Graph.SetData(_samples, seconds, sample);
        if (sample is null)
        {
            SelectedTime.Text = Strings.FlightRecorderEmpty;
            Readings.Text = "";
            Processes.ItemsSource = null;
            Changes.Text = "";
            return;
        }
        SelectedTime.Text = sample.Timestamp.ToLocalTime().ToString("G", CultureInfo.CurrentCulture);
        Readings.Text = string.Join("   ·   ", _metrics.Select(metric =>
            CaptureReportText.MetricName(metric, sample.Resources) + " " +
            (CaptureRules.Value(metric, sample) is { } value ? CaptureReportText.Value(metric, value) : "—")));
        Processes.ItemsSource = IncidentTimeline.Contributors(sample, SelectedMetric).Select(p => new
        {
            p.Name, Pid = p.ProcessId, Cpu = Percent(p.ObservedCpuPercent),
            Memory = p.WorkingSetBytes is { } memory ? TableMetricText.Memory(memory) : "—",
            Io = p.IoBytesPerSecond is { } io ? TableMetricText.IoRate(io) : "—", Gpu = Percent(p.GpuPercent)
        }).ToArray();
        var changes = _changes is not null && _changes.TryGetValue(SampleKey.Of(sample), out var fullChanges)
            ? fullChanges : IncidentTimeline.Changes(_index > 0 ? _samples[_index - 1] : null, sample);
        Changes.Text = changes is { } observed
            ? Strings.Format(Strings.FlightRecorderChangeSummary, Names(observed.Started), Names(observed.Stopped))
            : Strings.FlightRecorderChangesUnavailable;
        if (sample.IsGap || sample.CompactedProcesses > 0 || sample.UnavailableProcesses > 0)
            Changes.Text += Environment.NewLine + Strings.Format(Strings.FlightRecorderPartial,
                sample.CompactedProcesses, sample.UnavailableProcesses);
    }

    private static string Percent(double? value) => value is { } number
        ? number.ToString("F1", CultureInfo.CurrentCulture) + "%" : "—";

    private static string Names(IReadOnlyList<ProcessUsage> processes) => processes.Count == 0 ? "—" :
        string.Join(", ", processes.Select(p => $"{p.Name} ({p.ProcessId})"));

    private void FindPeak()
    {
        if (IncidentTimeline.Peak(_samples, SelectedMetric) is { } peak) Select(Array.IndexOf(_samples, peak));
        else Select(_index);
    }

    private void OnMetricChanged(object sender, SelectionChangedEventArgs e) => FindPeak();
    private void OnRefresh(object sender, RoutedEventArgs e) => RefreshRequested?.Invoke(this, EventArgs.Empty);
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
}
