using System.Windows;
using WinMon.Controls;
using WinMon.Monitoring;

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
        var recorder = new FlightRecorderWindow(samples, IncidentTimeline.CaptureChanges(samples, Session.UsageHistory)) { Owner = this };
        recorder.RefreshRequested += (_, _) =>
        {
            var latest = Session.History.Samples;
            recorder.Update(latest, IncidentTimeline.CaptureChanges(latest, Session.UsageHistory));
        };
        recorder.Closed += (_, _) => _flightRecorder = null;
        _flightRecorder = recorder;
        recorder.Show();
    }
}
