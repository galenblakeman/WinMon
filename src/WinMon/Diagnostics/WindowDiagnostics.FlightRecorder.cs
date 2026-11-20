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
        fixture.FlightRecorderButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var recorder = fixture.OwnedWindows.OfType<FlightRecorderWindow>().Single();
        try
        {
            fixture.FlightRecorderButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (((GridView)recorder.Processes.View).Columns.Any(column => column.Header is string header && header.Contains('{')))
                throw new InvalidOperationException("Process column headings must not display unexpanded format placeholders.");
            if (fixture.OwnedWindows.OfType<FlightRecorderWindow>().Count() != 1 ||
                !ReferenceEquals(recorder.Selection, IncidentTimeline.Peak(fixture.Session.History.Samples, CaptureMetric.Cpu)))
                throw new InvalidOperationException("History must open one recorder and select the CPU peak.");
            recorder.Metric.SelectedIndex = 2;
            if (!ReferenceEquals(recorder.Selection,
                    IncidentTimeline.Peak(fixture.Session.History.Samples, CaptureMetric.PrimaryDisk)))
                throw new InvalidOperationException("Changing the resource must select its peak.");
            recorder.Select(0);
            if (recorder.PreviousButton.IsEnabled || !recorder.NextButton.IsEnabled ||
                recorder.Changes.Text != Strings.FlightRecorderChangesUnavailable)
                throw new InvalidOperationException("The first observation must not invent starts or allow stepping backward.");
            recorder.NextButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (!ReferenceEquals(recorder.Selection, fixture.Session.History.Samples[1]))
                throw new InvalidOperationException("Next must select the next recorded observation.");
            recorder.PeakButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var selectedBeforeRefresh = recorder.Selection;
            var coverageBeforeRefresh = recorder.Coverage.Text;
            var last = fixture.Session.History.Samples[^1];
            var incoming = last with
            {
                Timestamp = last.Timestamp.AddSeconds(1),
                MonotonicTimestamp = last.MonotonicTimestamp + System.Diagnostics.Stopwatch.Frequency,
                TotalCpuPercent = 100,
                Resources = last.Resources with { DiskActivePercent = 100 },
                Processes = [.. last.Processes, new ProcessUsage(9000, 9000, "refresh-worker", 5)]
            };
            fixture.Session.Record(incoming, incoming);
            if (!ReferenceEquals(recorder.Selection, selectedBeforeRefresh) || recorder.Coverage.Text != coverageBeforeRefresh)
                throw new InvalidOperationException("History must stay frozen until Refresh is requested.");
            recorder.RefreshButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (!ReferenceEquals(recorder.Selection, selectedBeforeRefresh) || recorder.Metric.SelectedIndex != 2 ||
                recorder.Coverage.Text == coverageBeforeRefresh || fixture.OwnedWindows.OfType<FlightRecorderWindow>().Count() != 1)
                throw new InvalidOperationException("Refresh must load current history in the same window and preserve the resource and retained selection.");
            recorder.PeakButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (!ReferenceEquals(recorder.Selection, incoming) || recorder.Processes.Items.Count != incoming.Processes.Count ||
                !recorder.Changes.Text.Contains("refresh-worker", StringComparison.Ordinal))
                throw new InvalidOperationException("Refreshed peaks, contributors and lifecycle changes must use the latest observations.");
            if (recorder.ComparisonProcesses.Items.Count == 0 ||
                !recorder.ComparisonSummary.Text.Contains(Strings.FlightRecorderBefore, StringComparison.Ordinal) ||
                !recorder.ComparisonSummary.Text.Contains(Strings.FlightRecorderAfter, StringComparison.Ordinal))
                throw new InvalidOperationException("History comparison must show period averages and retained process changes.");
            var comparisonSamples = Enumerable.Range(0, 30).Select(second => incoming with
            {
                Timestamp = incoming.Timestamp.AddSeconds(second),
                MonotonicTimestamp = incoming.MonotonicTimestamp + second * System.Diagnostics.Stopwatch.Frequency,
                TotalCpuPercent = second < 10 ? 10 : second < 20 ? 80 : 20,
                Resources = incoming.Resources with { DiskActivePercent = second < 10 ? 10 : second < 20 ? 90 : 15 },
                Processes = [new ProcessUsage(9000, 9000, "comparison-worker", second < 10 ? 5 : second < 20 ? 70 : 10,
                    100_000_000, second < 10 ? 1_000_000 : second < 20 ? 12_000_000 : 2_000_000)]
            }).ToArray();
            recorder.Update(comparisonSamples);
            recorder.Select(15);
            foreach (var theme in new[] { "Dark", "Light" })
            {
                fixture.Preferences = fixture.Preferences with { Theme = theme };
                fixture.ApplyTheme();
                WindowTheme.Apply(recorder);
                foreach (var width in new[] { 1000d, 620d })
                {
                    recorder.Width = width;
                    recorder.Height = width == 620 ? 540 : 760;
                    recorder.UpdateLayout();
                    var refreshHost = (FrameworkElement)recorder.RefreshButton.Parent;
                    var refreshRight = recorder.RefreshButton.TranslatePoint(
                        new Point(recorder.RefreshButton.ActualWidth, 0), refreshHost).X;
                    if (recorder.ComparisonProcesses.ActualHeight < 40 || recorder.Graph.ActualHeight < 100 ||
                        recorder.PeakButton.TranslatePoint(new Point(recorder.PeakButton.ActualWidth, 0), recorder).X > recorder.ActualWidth ||
                        Math.Abs(refreshRight - refreshHost.ActualWidth) > 1 ||
                        recorder.RefreshButton.TranslatePoint(new Point(), recorder).Y > 75)
                        throw new InvalidOperationException($"History controls and process list must remain usable at minimum size: list={recorder.ComparisonProcesses.ActualHeight}, graph={recorder.Graph.ActualHeight}.");
                    await CaptureServiceWindowAsync(recorder,
                        Path.Combine(output, $"flight-recorder-{theme.ToLowerInvariant()}-{width:0}.png"), cancellationToken);
                    recorder.Views.SelectedIndex = 1;
                    recorder.UpdateLayout();
                    if (recorder.Processes.ActualHeight < 40)
                        throw new InvalidOperationException("Selected-moment contributors must remain available in their tab.");
                    await CaptureServiceWindowAsync(recorder,
                        Path.Combine(output, $"flight-recorder-moment-{theme.ToLowerInvariant()}-{width:0}.png"), cancellationToken);
                    recorder.Views.SelectedIndex = 0;
                }
            }
        }
        finally { recorder.Close(); }

        var empty = new FlightRecorderWindow([]) { Owner = fixture };
        IReadOnlyList<MonitorSample> refreshSamples = [];
        empty.RefreshRequested += (_, _) => empty.Update(refreshSamples);
        try
        {
            empty.Show();
            if (empty.Selection is not null || empty.PeakButton.IsEnabled || empty.NextButton.IsEnabled ||
                empty.PreviousButton.IsEnabled || empty.Coverage.Text != Strings.FlightRecorderEmpty)
                throw new InvalidOperationException("Empty history needs an explanation and disabled navigation.");
            await CaptureServiceWindowAsync(empty, Path.Combine(output, "flight-recorder-empty.png"), cancellationToken);
            refreshSamples = [fixture.Session.History.Samples[^1]];
            empty.RefreshButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (!ReferenceEquals(empty.Selection, refreshSamples[0]) || !empty.PeakButton.IsEnabled || empty.Processes.Items.Count == 0)
                throw new InvalidOperationException("Refresh must populate an initially empty recorder.");
            var replacement = refreshSamples[0] with { Timestamp = refreshSamples[0].Timestamp.AddMinutes(1) };
            refreshSamples = [replacement];
            empty.RefreshButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (!ReferenceEquals(empty.Selection, replacement))
                throw new InvalidOperationException("Refresh must select the current peak when the old observation expired.");
            refreshSamples = [];
            empty.RefreshButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (empty.Selection is not null || empty.Processes.Items.Count != 0 || empty.Readings.Text.Length != 0 ||
                empty.Changes.Text.Length != 0 || empty.ComparisonSummary.Text.Length != 0 || empty.ComparisonProcesses.Items.Count != 0 ||
                empty.PeakButton.IsEnabled || empty.Coverage.Text != Strings.FlightRecorderEmpty)
                throw new InvalidOperationException("Refreshing to empty history must clear previous readings, contributors and changes.");
        }
        finally { empty.Close(); }

        var initial = fixture.Session.History.Samples[0];
        var idle = Enumerable.Range(0, 80).Select(i => new ProcessUsage(8000 + i, 8000 + i, $"fixture-{i}", 0, (ulong)(100 + i))).ToArray();
        var before = initial with { Processes = [.. initial.Processes, .. idle], CompactedProcesses = 0 };
        var after = before with
        {
            Timestamp = before.Timestamp.AddSeconds(1),
            MonotonicTimestamp = before.MonotonicTimestamp + System.Diagnostics.Stopwatch.Frequency,
            Processes = [.. initial.Processes, .. idle.Skip(1), new ProcessUsage(8100, 8100, "fixture-helper", 0, 1)]
        };
        var usage = new UsageHistory();
        usage.Add(before);
        usage.Add(after);
        MonitorSample[] retained = [ProcessRetention.Compact(before), ProcessRetention.Compact(after)];
        if (retained[1].CompactedProcesses == 0)
            throw new InvalidOperationException("The lifecycle fixture must omit idle processes from chart history.");
        var lifecycle = new FlightRecorderWindow(retained, IncidentTimeline.CaptureChanges(retained, usage)) { Owner = fixture };
        try
        {
            lifecycle.Show();
            lifecycle.Select(1);
            if (!lifecycle.Changes.Text.Contains("fixture-0", StringComparison.Ordinal) ||
                !lifecycle.Changes.Text.Contains("fixture-helper", StringComparison.Ordinal))
                throw new InvalidOperationException("Full Usage inventories must reveal starts/stops even when chart processes are compacted.");
            await CaptureServiceWindowAsync(lifecycle, Path.Combine(output, "flight-recorder-process-changes.png"), cancellationToken);
        }
        finally { lifecycle.Close(); }
    }
}
