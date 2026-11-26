using System.Diagnostics;
using System.Text.Json;
using MonCore.Monitoring;
using WinMon.Monitoring;

namespace WinMon;

public partial class MainWindow
{
    private const int MaxPendingCaptureIncidents = 20;
    private readonly List<PendingCaptureIncident> _pendingCaptureIncidents = [];
    private bool _captureIncidentErrorShown;

    private sealed record PendingCaptureIncident(CaptureMarker Marker, string Folder, Task<string> Report,
        MonitorSample[] Lead, ConnectionCapture[] Traffic)
    {
        internal double End => Marker.FiredKey.Moment + CaptureReports.IncidentTailSeconds;
    }

    private void QueueCaptureIncident(CaptureMarker marker, IReadOnlyList<MonitorSample> history,
        IReadOnlyList<ConnectionCapture> connections, string folder, Task<string> report)
    {
        if (_pendingCaptureIncidents.Count == MaxPendingCaptureIncidents)
            FinishCaptureIncidents(_pendingCaptureIncidents[0].End);

        var trigger = marker.FiredKey.Moment;
        var start = trigger - CaptureReports.IncidentLeadSeconds;
        var lead = NewestFirst(history).TakeWhile(sample => SampleKey.Of(sample).Moment >= start)
            .Where(sample => SampleKey.Of(sample).Moment < trigger).Take(CaptureReports.IncidentLeadSeconds).Reverse().ToArray();
        var traffic = CaptureTraffic(connections, start, trigger, CaptureReports.IncidentLeadSeconds + 1);
        _pendingCaptureIncidents.Add(new PendingCaptureIncident(marker, folder, report, lead, traffic));
    }

    private void FinishCaptureIncidents(double now)
    {
        while (_pendingCaptureIncidents.Count > 0 && _pendingCaptureIncidents[0].End <= now)
        {
            var pending = _pendingCaptureIncidents[0];
            _pendingCaptureIncidents.RemoveAt(0);
            var trigger = pending.Marker.FiredKey.Moment;
            var tail = NewestFirst(Session.History.Samples)
                .TakeWhile(sample => SampleKey.Of(sample).Moment > trigger)
                .Where(sample => SampleKey.Of(sample).Moment <= pending.End)
                .Reverse().Take(CaptureReports.IncidentTailSeconds).ToArray();
            var traffic = CaptureTraffic(Session.Connections.Captures, Math.BitIncrement(trigger), pending.End,
                CaptureReports.IncidentTailSeconds);
            MonitorSample[] samples = [.. pending.Lead, pending.Marker.Fired.Fired, .. tail];
            var write = Task.Run(() => SaveCaptureIncidentAsync(pending, samples, traffic));
            _ = TrackCaptureIncidentAsync(write);
        }
    }

    private static ConnectionCapture[] CaptureTraffic(IReadOnlyList<ConnectionCapture> captures, double start,
        double end, int limit) => NewestFirst(captures)
        .TakeWhile(capture => SampleKey.Of(capture.Sample).Moment >= start)
        .Where(capture => SampleKey.Of(capture.Sample).Moment <= end && capture.Network is not null)
        .Take(limit).Select(capture => capture with { Connections = null, Network = capture.Network! with
            { Flows = [], CurrentFlows = null } }).ToArray();

    private static IEnumerable<T> NewestFirst<T>(IReadOnlyList<T> items)
    {
        for (var index = items.Count - 1; index >= 0; index--) yield return items[index];
    }

    private static async Task SaveCaptureIncidentAsync(PendingCaptureIncident pending, MonitorSample[] samples,
        ConnectionCapture[] tailTraffic)
    {
        // The report reserves a unique name. Its failure is already handled by the report writer.
        await ((Task)pending.Report).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        if (!pending.Report.IsCompletedSuccessfully) return;
        var report = pending.Report.Result;
        var id = pending.Marker.ReportId.ToString("N");
        try
        {
            if (CaptureReports.ReportId(report) != id) return;
            var path = CaptureReports.IncidentPath(report, id);
            var traffic = pending.Traffic.Concat(tailTraffic).ToDictionary(capture => SampleKey.Of(capture.Sample));
            var observations = samples.Select((sample, index) =>
            {
                var capture = traffic.GetValueOrDefault(SampleKey.Of(sample));
                var changes = IncidentTimeline.Changes(index > 0 ? samples[index - 1] : null, sample);
                return new IncidentObservation(sample, capture?.Network, capture?.Owners ?? [], changes?.Started,
                    changes?.Stopped);
            }).ToArray();
            var metric = pending.Marker.Fired.Rule.Metric switch
            {
                CaptureMetric.ApplicationMemory or CaptureMetric.ServiceMemory => CaptureMetric.Memory,
                CaptureMetric.ApplicationNetwork or CaptureMetric.ServiceNetwork => CaptureMetric.Network,
                CaptureMetric.Process or CaptureMetric.ApplicationCpu or CaptureMetric.ServiceCpu => CaptureMetric.Cpu,
                var resource => resource
            };
            new IncidentRecording(IncidentRecording.CurrentVersion, Stopwatch.Frequency, observations, metric,
                pending.Lead.Length).Save(path, overwrite: false);
            // Another capture may have pruned this report while its incident was being serialized.
            if (CaptureReports.ReportId(report) != id) File.Delete(path);
        }
        finally
        {
            CaptureReports.Prune(pending.Folder);
        }
    }

    private async Task TrackCaptureIncidentAsync(Task write)
    {
        _captureWrites.Add(write);
        try { await write; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or
                                   NotSupportedException or JsonException or InvalidDataException)
        {
            Trace.TraceError("WinMon could not save an automatic incident: {0}", ex);
            if (!_captureIncidentErrorShown && !IsClosing)
            {
                _captureIncidentErrorShown = true;
                ShowCaptureNotice(Strings.Format(Strings.FlightRecorderFileError, ex.Message));
            }
        }
        finally { _captureWrites.Remove(write); }
    }
}
