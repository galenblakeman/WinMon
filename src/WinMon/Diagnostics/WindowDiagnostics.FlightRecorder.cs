using System.Windows;
using System.Windows.Controls;
using WinMon.Controls;
using WinMon.Monitoring;

namespace WinMon.Diagnostics;

internal sealed partial class WindowDiagnostics
{
    private static async Task CaptureFlightRecorderAsync(MainWindow fixture, string output,
        CancellationToken cancellationToken)
    {
        fixture.FlightRecorderEventReader = (start, end, _) => Task.FromResult(new IncidentEventResult(
            [new IncidentEvent(IncidentTimeline.Peak(fixture.Session.History.Samples, CaptureMetric.Cpu)?.Timestamp ?? start,
                "fixture-provider", 42, IncidentEventLevel.Warning,
                "Synthetic diagnostic event"),
                new IncidentEvent(end.AddSeconds(10), "fixture-provider", 43, IncidentEventLevel.Error,
                    "Synthetic event between observations")],
            [new IncidentEventLogCoverage("Application", IncidentEventCoverageStatus.Complete),
                new IncidentEventLogCoverage("System", IncidentEventCoverageStatus.Unreadable)], true));
        fixture.FlightRecorderButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var recorder = fixture.OwnedWindows.OfType<FlightRecorderWindow>().Single();
        try
        {
            await VerifyFlightRecorderNavigationAsync(fixture, recorder);
            var incoming = VerifyFlightRecorderRefresh(fixture, recorder);
            await VerifyFlightRecorderIncidentAsync(recorder, incoming, output);
            await CaptureFlightRecorderLayoutsAsync(fixture, recorder, output, cancellationToken);
            recorder.RefreshButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (recorder.Source.Text.Length != 0 || recorder.Metric.SelectedIndex != 4 ||
                !fixture.Session.History.Samples.Any(sample => ReferenceEquals(sample, recorder.Selection)))
                throw new InvalidOperationException("Refresh must leave the loaded incident and return to retained live history.");
        }
        finally { recorder.Close(); fixture.FlightRecorderEventReader = null; }

        await CaptureEmptyFlightRecorderAsync(fixture, output, cancellationToken);
        await CaptureLifecycleFlightRecorderAsync(fixture, output, cancellationToken);
    }
}
