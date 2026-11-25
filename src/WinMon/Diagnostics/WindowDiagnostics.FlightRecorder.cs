using System.Windows;
using System.Windows.Controls;
using WinMon.Controls;
using WinMon.Monitoring;
using WinMon.Resources;

namespace WinMon.Diagnostics;

internal sealed partial class WindowDiagnostics
{
    private static async Task CaptureFlightRecorderAsync(MainWindow fixture, string output,
        CancellationToken cancellationToken)
    {
        fixture.FlightRecorderEventReader = (start, end, _) => Task.FromResult(new IncidentEventResult(
            [new IncidentEvent(IncidentTimeline.Peak(fixture.Session.History.Samples, CaptureMetric.Cpu)?.Timestamp ?? start,
                "Application Error", 1000, IncidentEventLevel.Error,
                "Synthetic diagnostic crash", CrashSignature: new(CrashKind.Crash, "fixture.exe", "1.0",
                    "fixture.dll", "1.0", 0xC0000005, 0x10)),
                new IncidentEvent(end.AddSeconds(10), "Application Hang", 1002, IncidentEventLevel.Error,
                    "Synthetic diagnostic hang", CrashSignature: new(CrashKind.Hang, "fixture-hang.exe", "1.0"))],
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

        await CaptureHangOnlyFlightRecorderAsync(fixture, output, cancellationToken);
        await CaptureEmptyFlightRecorderAsync(fixture, output, cancellationToken);
        await CaptureLifecycleFlightRecorderAsync(fixture, output, cancellationToken);
    }

    private static async Task CaptureHangOnlyFlightRecorderAsync(MainWindow fixture, string output,
        CancellationToken cancellationToken)
    {
        foreach (var theme in new[] { "Dark", "Light" })
        {
            fixture.Preferences = fixture.Preferences with { Theme = theme };
            fixture.ApplyTheme();
            var recorder = new FlightRecorderWindow(fixture.Session.History.Samples, eventReader: (start, _, _) =>
                Task.FromResult(new IncidentEventResult(
                    [new IncidentEvent(start, "Application Hang", 1002, IncidentEventLevel.Error, "Synthetic diagnostic hang",
                        CrashSignature: new(CrashKind.Hang, "fixture-hang.exe", "1.0"))],
                    [new IncidentEventLogCoverage("Application", IncidentEventCoverageStatus.Complete),
                        new IncidentEventLogCoverage("System", IncidentEventCoverageStatus.Complete)], false))) { Owner = fixture };
            try
            {
                recorder.Show();
                await Task.Yield();
                if (recorder.CrashSummaryGroups.Count != 1 || recorder.Crashes.Items.Count != 1 ||
                    !recorder.Crashes.Items.Cast<object>().Any(item => string.Equals(
                        item.GetType().GetProperty("Failure")?.GetValue(item) as string, Strings.FlightRecorderCrashNotResponding,
                        StringComparison.Ordinal)))
                    throw new InvalidOperationException("A hang-only history must show one Not responding crash summary.");
                await CaptureServiceWindowAsync(recorder, Path.Combine(output, $"flight-recorder-hangs-{theme.ToLowerInvariant()}.png"),
                    cancellationToken);
            }
            finally { recorder.Close(); }
        }
    }
}
