using System.Globalization;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using WinMon.Monitoring;

namespace WinMon.Controls;

internal partial class FlightRecorderWindow : Window
{
    private MonitorSample[] _samples = [];
    private readonly CaptureMetric[] _metrics = [CaptureMetric.Cpu, CaptureMetric.Memory, CaptureMetric.PrimaryDisk,
        CaptureMetric.SecondaryDisk, CaptureMetric.Network, CaptureMetric.Gpu];
    private int _index;
    private bool _fileBusy;
    private IReadOnlyDictionary<SampleKey, ConnectionCapture> _traffic = new Dictionary<SampleKey, ConnectionCapture>();
    private IReadOnlyDictionary<SampleKey, (IReadOnlyList<ProcessUsage> Started, IReadOnlyList<ProcessUsage> Stopped)>? _changes;

    internal event EventHandler? RefreshRequested;
    internal event Action<Task>? SaveStarted;
    internal event Action<Task>? SaveFinished;

    internal FlightRecorderWindow(IReadOnlyList<MonitorSample> samples,
        IReadOnlyDictionary<SampleKey, (IReadOnlyList<ProcessUsage> Started, IReadOnlyList<ProcessUsage> Stopped)>? changes = null,
        IReadOnlyList<ConnectionCapture>? traffic = null)
    {
        InitializeComponent();
        WindowTheme.Attach(this);
        Closing += (_, args) => args.Cancel = _fileBusy;
        Graph.VisibleMetrics = 63;
        Graph.MetricLabels = [Strings.CPU, Strings.Memory, Strings.Disk, Strings.Network, Strings.GPU, Strings.Disk];
        Graph.SampleSelected += (_, sample) => Select(Array.IndexOf(_samples, sample));
        Graph.LiveRequested += (_, _) => Select(_samples.Length - 1);
        Update(samples, changes, traffic);
    }

    internal void Update(IReadOnlyList<MonitorSample> samples,
        IReadOnlyDictionary<SampleKey, (IReadOnlyList<ProcessUsage> Started, IReadOnlyList<ProcessUsage> Stopped)>? changes = null,
        IReadOnlyList<ConnectionCapture>? traffic = null)
    {
        var selected = Selection is { } previous ? SampleKey.Of(previous) : (SampleKey?)null;
        var metricIndex = Math.Max(0, Metric.SelectedIndex);
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
        SaveButton.IsEnabled = !_fileBusy && sample is not null;
        var seconds = _samples.Length < 2 ? 1 :
            Math.Max(1, (int)Math.Ceiling(CpuGraph.Moment(_samples[^1]) - CpuGraph.Moment(_samples[0])) + 1);
        Graph.SetData(_samples, seconds, sample);
        if (sample is null)
        {
            SelectedTime.Text = Strings.FlightRecorderEmpty;
            Readings.Text = "";
            Processes.ItemsSource = null;
            Changes.Text = "";
            ComparisonSummary.Text = "";
            ComparisonProcesses.ItemsSource = null;
            return;
        }
        SelectedTime.Text = sample.Timestamp.ToLocalTime().ToString("G", CultureInfo.CurrentCulture);
        Readings.Text = string.Join("   ·   ", _metrics.Select(metric =>
            CaptureReportText.MetricName(metric, sample.Resources) + " " +
            (CaptureRules.Value(metric, sample) is { } value ? CaptureReportText.Value(metric, value) : "—")));
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
        Processes.ItemsSource = IncidentTimeline.Contributors(sample, SelectedMetric, traffic).Select(p => new
        {
            p.Name, Pid = p.ProcessId, Cpu = Percent(p.ObservedCpuPercent),
            Memory = p.WorkingSetBytes is { } memory ? TableMetricText.Memory(memory) : "—",
            Io = p.IoBytesPerSecond is { } io ? TableMetricText.IoRate(io) : "—", Gpu = Percent(p.GpuPercent),
            Network = rates.GetValueOrDefault(p.Identity) is { } rate ? TableMetricText.IoRate(rate) : "—"
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

    private string Reading(IncidentTimeline.Reading reading, bool process) => reading.Average is { } value
        ? Strings.Format(Strings.FlightRecorderObservedMean, Value(value, process), reading.Observations) : "—";

    private string Value(double value, bool process) => !process ? CaptureReportText.Value(SelectedMetric, value) : SelectedMetric switch
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

    internal IncidentRecording Recording() => new(IncidentRecording.CurrentVersion, Stopwatch.Frequency,
        _samples.Select((sample, index) =>
        {
            var capture = _traffic.GetValueOrDefault(SampleKey.Of(sample));
            var changes = _changes is not null && _changes.TryGetValue(SampleKey.Of(sample), out var known)
                ? known : IncidentTimeline.Changes(index > 0 ? _samples[index - 1] : null, sample);
            return new IncidentObservation(sample, capture?.Network is { } network
                ? network with { Flows = [], CurrentFlows = null } : null, capture?.Owners ?? [],
                changes?.Started, changes?.Stopped);
        }).ToArray(), SelectedMetric, _index);

    internal void OpenRecording(IncidentRecording recording, string name)
    {
        var observations = recording.Observations;
        var changes = observations.Where(frame => frame.Started is not null && frame.Stopped is not null)
            .ToDictionary(frame => SampleKey.Of(frame.Sample), frame => (frame.Started!, frame.Stopped!));
        Update(observations.Select(frame => frame.Sample).ToArray(), changes,
            observations.Select(frame => new ConnectionCapture(frame.Sample, null, frame.Owners, frame.Network)).ToArray());
        Metric.SelectedIndex = Array.IndexOf(_metrics, recording.Metric);
        Select(recording.SelectedIndex);
        Source.Text = Strings.Format(Strings.FlightRecorderOpened, name);
    }

    // WPF event handlers await file work so sampling and painting remain responsive.
    private async void OnSave(object sender, RoutedEventArgs e)
    {
        try
        {
            var dialog = new SaveFileDialog { Title = Strings.FlightRecorderSave, Filter = Strings.FlightRecorderFilter,
                DefaultExt = ".wmincident", AddExtension = true, OverwritePrompt = true,
                FileName = $"WinMon-{DateTime.Now:yyyyMMdd-HHmmss}.wmincident" };
            if (dialog.ShowDialog(this) != true) return;
            await SaveRecordingAsync(dialog.FileName);
        }
        catch (Exception ex) { FileError(ex); }
    }

    internal async Task SaveRecordingAsync(string path)
    {
        var recording = Recording();
        SetFileBusy(true);
        var write = Task.Run(() => recording.Save(path));
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
}
