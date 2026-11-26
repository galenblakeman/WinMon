using System.Windows;
using MonCore.Monitoring;
using MonCore.Windows.Monitoring;
using WinMon.Controls;

namespace WinMon;

public partial class MainWindow
{
    private FlightRecorderWindow? _flightRecorder;

    private void OnFlightRecorder(object sender, RoutedEventArgs e)
    {
        if (_flightRecorder is { } existing)
        {
            if (existing.WindowState == WindowState.Minimized) existing.WindowState = WindowState.Normal;
            existing.Activate();
            return;
        }
        var samples = Session.History.Samples;
        var eventReader = FlightRecorderEventReader ?? ((DateTimeOffset start, DateTimeOffset end, CancellationToken cancellation) => Task.Run(() =>
            new WindowsHistoryEventReader().Read(start, end, cancellation), cancellation));
        var recorder = new FlightRecorderWindow(samples, IncidentTimeline.CaptureChanges(samples, Session.UsageHistory),
            Session.Connections.Captures, eventReader) { Owner = this };
        recorder.ConfigureEventsSidebar(_preferences.HistoryEventsCollapsed, _preferences.HistoryEventsRatio);
        recorder.EventsSidebarChanged += (collapsed, ratio) => _preferences = _preferences with
        {
            HistoryEventsCollapsed = collapsed,
            HistoryEventsRatio = ratio
        };
        recorder.UpdateTrends(_resourceTrends.Snapshot(DateTimeOffset.UtcNow), ResourceTrendNotice);
        recorder.SaveStarted += write => ExportWrites.Add(write);
        recorder.SaveFinished += write => ExportWrites.Remove(write);
        recorder.RefreshRequested += (_, _) =>
        {
            var latest = Session.History.Samples;
            recorder.Update(latest, IncidentTimeline.CaptureChanges(latest, Session.UsageHistory), Session.Connections.Captures);
            RefreshResourceTrends();
        };
        recorder.Closed += (_, _) => _flightRecorder = null;
        _flightRecorder = recorder;
        recorder.Show();
    }
}
