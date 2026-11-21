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
            fixture.FlightRecorderButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Task.Yield();
            if (recorder.Events.Items.Count == 0 || recorder.Graph.Markers.Count == 0 ||
                !recorder.EventCoverageTextValue.Contains("Application", StringComparison.Ordinal) ||
                !recorder.EventCoverageTextValue.Contains(Strings.FlightRecorderEventUnreadable, StringComparison.Ordinal) ||
                !recorder.EventCoverageTextValue.Contains(Strings.Format(Strings.FlightRecorderEventLimit, 2), StringComparison.Ordinal))
                throw new InvalidOperationException("History events must use the synthetic reader, show coverage, and add timeline markers.");
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
                Resources = incoming.Resources with { DiskActivePercent = second < 10 ? 10 : second < 20 ? 90 : 15,
                    LinkBitsPerSecond = 100_000_000, ReceiveBytesPerSecond = second < 10 ? 100_000 : second < 20 ? 800_000 : 120_000,
                    SendBytesPerSecond = 20_000 },
                Processes = [new ProcessUsage(9000, 9000, "comparison-worker", second < 10 ? 5 : second < 20 ? 70 : 10,
                    100_000_000, second < 10 ? 1_000_000 : second < 20 ? 12_000_000 : 2_000_000)]
            }).ToArray();
            recorder.Update(comparisonSamples);
            recorder.Select(15);
            var trafficOwner = new ProcessUsage(9200, 9200, "incident-download", 0)
                { ImagePath = @"C:\fixture\incident-download.exe" };
            var traffic = comparisonSamples.Select(sample => new ConnectionCapture(sample, null, [trafficOwner],
                new ProcessNetworkFrame(sample.MonotonicTimestamp, System.Diagnostics.Stopwatch.Frequency,
                    ProcessNetworkQuality.Provisional, [new(9200, 9200, 1000, 2000,
                        sample == comparisonSamples[15] ? 800_000 : 100_000, 20_000)])
                { Owners = [trafficOwner] })).ToArray();
            recorder.Update(comparisonSamples, traffic: traffic);
            recorder.Metric.SelectedIndex = 4;
            recorder.Select(15);
            var incidentPath = Path.Combine(output, "history-roundtrip.wmincident");
            var saveStarted = false;
            var saveFinished = false;
            recorder.SaveStarted += _ =>
            {
                saveStarted = true;
                if (recorder.SaveButton.IsEnabled || recorder.OpenButton.IsEnabled || recorder.RefreshButton.IsEnabled)
                    throw new InvalidOperationException("Incident file controls must be disabled during save.");
            };
            recorder.SaveFinished += _ => saveFinished = true;
            await recorder.SaveRecordingAsync(incidentPath);
            if (!saveStarted || !saveFinished)
                throw new InvalidOperationException("Incident saves must participate in the owner's export shutdown tracking.");
            recorder.Update([]);
            await recorder.OpenRecordingAsync(incidentPath);
            if (recorder.Selection?.Timestamp != comparisonSamples[15].Timestamp || recorder.Metric.SelectedIndex != 4 ||
                !recorder.Source.Text.Contains("history-roundtrip.wmincident", StringComparison.Ordinal) ||
                recorder.Processes.Items[0].GetType().GetProperty("Name")?.GetValue(recorder.Processes.Items[0]) as string != "incident-download" ||
                recorder.ComparisonProcesses.Items.Count == 0 || !recorder.SaveButton.IsEnabled)
                throw new InvalidOperationException("Saved incidents must reopen at the selected network observation with actual traffic leaders and comparison data.");
            var loadedSelection = recorder.Selection;
            var loadedSource = recorder.Source.Text;
            var invalidPath = Path.Combine(output, "invalid.wmincident");
            File.WriteAllText(invalidPath, "invalid incident");
            try
            {
                await recorder.OpenRecordingAsync(invalidPath);
                throw new InvalidOperationException("Invalid incident input must be rejected.");
            }
            catch (InvalidDataException) { }
            if (!ReferenceEquals(recorder.Selection, loadedSelection) || recorder.Source.Text != loadedSource ||
                !recorder.SaveButton.IsEnabled || !recorder.OpenButton.IsEnabled || !recorder.RefreshButton.IsEnabled)
                throw new InvalidOperationException("A failed open must preserve the displayed incident and restore its controls.");
            recorder.SelectEventForFixture(recorder.EventMappings.Single(item => item.ObservationIndex is null));
            if (!recorder.SelectedEvent.Text.Contains(Strings.FlightRecorderEventBetweenObservations, StringComparison.Ordinal))
                throw new InvalidOperationException("A between-observation event must keep its selected details visible.");
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
                    if (recorder.ComparisonProcesses.ActualHeight < 40 || recorder.Events.ActualHeight < 40 || recorder.Graph.ActualHeight < 100 ||
                        recorder.PeakButton.TranslatePoint(new Point(recorder.PeakButton.ActualWidth, 0), recorder).X > recorder.ActualWidth ||
                        Math.Abs(refreshRight - refreshHost.ActualWidth) > 1 ||
                        recorder.RefreshButton.TranslatePoint(new Point(), recorder).Y > 75)
                    {
                        await CaptureServiceWindowAsync(recorder, Path.Combine(output, "flight-recorder-layout-failure.png"), cancellationToken);
                        throw new InvalidOperationException($"History controls and process list must remain usable at minimum size: list={recorder.ComparisonProcesses.ActualHeight}, events={recorder.Events.ActualHeight}, graph={recorder.Graph.ActualHeight}, rows={string.Join(',', ((Grid)recorder.Content).RowDefinitions.Select(row => row.ActualHeight))}.");
                    }
                    await CaptureServiceWindowAsync(recorder,
                        Path.Combine(output, $"flight-recorder-{theme.ToLowerInvariant()}-{width:0}.png"), cancellationToken);
                    recorder.Views.SelectedIndex = 1;
                    recorder.UpdateLayout();
                    if (recorder.Processes.ActualHeight < 40)
                        throw new InvalidOperationException("Selected-moment contributors must remain available in their tab.");
                    await CaptureServiceWindowAsync(recorder,
                        Path.Combine(output, $"flight-recorder-moment-{theme.ToLowerInvariant()}-{width:0}.png"), cancellationToken);
                    recorder.Views.SelectedIndex = 0;
                    var trendNow = DateTimeOffset.UtcNow;
                    var trendEnd = new DateTimeOffset(trendNow.UtcDateTime.AddTicks(
                        -(trendNow.UtcDateTime.Ticks % TimeSpan.TicksPerMinute)));
                    var trendBuckets = Enumerable.Range(0, ResourceTrendArchive.RetainedMinutes).Select(index =>
                    {
                        var value = 20 + index % 55;
                        return new ResourceTrendBucket(trendEnd.AddMinutes(index - ResourceTrendArchive.RetainedMinutes + 1),
                            index is 3_600 or 7_200 ? new Dictionary<ResourceTrendMetric, ResourceTrendValue>() :
                            new Dictionary<ResourceTrendMetric, ResourceTrendValue>
                            {
                                [ResourceTrendMetric.Cpu] = new(value * 60, 60, value - 2, value + 2, "", ResourceTrendBreak.None),
                                [ResourceTrendMetric.MemoryBytes] = new((8 + index / 630d) * 1_073_741_824 * 60, 60,
                                    0, 32d * 1_073_741_824, "", ResourceTrendBreak.None),
                                [ResourceTrendMetric.MemoryPercent] = new((25 + index / 201.6) * 60, 60, 0, 100, "", ResourceTrendBreak.None),
                                [ResourceTrendMetric.Receive] = new(index * 100_000d, 60, index * 1_500, index * 1_800, "Ethernet", ResourceTrendBreak.None),
                                [ResourceTrendMetric.Send] = new(index * 50_000d, 60, index * 700, index * 900, "Ethernet", ResourceTrendBreak.None)
                            });
                    }).ToArray();
                    var trend = new ResourceTrendArchiveState(1, trendEnd, trendBuckets);
                    recorder.UpdateTrends(trend);
                    recorder.HistoryMode.SelectedIndex = 1;
                    recorder.TrendRange.SelectedIndex = 1;
                    recorder.UpdateLayout();
                    recorder.SelectTrendMinuteForFixture(trendEnd.AddMinutes(-1));
                    if (recorder.TrendGraph.ActualHeight < 100 || recorder.TrendSelection.Text.Length == 0 ||
                        recorder.TrendSelection.Text.Contains(Strings.FlightRecorderTrendNoReading, StringComparison.Ordinal))
                        throw new InvalidOperationException("Longer trends must render its selected minute and preserve chart space.");
                    await CaptureServiceWindowAsync(recorder,
                        Path.Combine(output, $"flight-recorder-trends-{theme.ToLowerInvariant()}-{width:0}.png"), cancellationToken);
                    foreach (var metric in new[] { 1, 4 })
                    {
                        recorder.TrendMetric.SelectedIndex = metric;
                        recorder.UpdateLayout();
                        await CaptureServiceWindowAsync(recorder,
                            Path.Combine(output, $"flight-recorder-trends-{(metric == 1 ? "memory" : "network")}-{theme.ToLowerInvariant()}-{width:0}.png"), cancellationToken);
                    }
                    recorder.TrendMetric.SelectedIndex = 0;
                    recorder.HistoryMode.SelectedIndex = 0;
                }
                recorder.Width = 3000;
                recorder.Height = 760;
                recorder.HistoryMode.SelectedIndex = 1;
                recorder.TrendRange.SelectedIndex = 0;
                recorder.TrendMetric.SelectedIndex = 1;
                recorder.UpdateLayout();
                await CaptureVisualAsync(recorder,
                    Path.Combine(output, $"flight-recorder-trends-24-hour-wide-{theme.ToLowerInvariant()}-3000-content.png"), 1, cancellationToken);
                recorder.TrendMetric.SelectedIndex = 0;
                recorder.HistoryMode.SelectedIndex = 0;
            }
            recorder.RefreshButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (recorder.Source.Text.Length != 0 || recorder.Metric.SelectedIndex != 4 ||
                !fixture.Session.History.Samples.Any(sample => ReferenceEquals(sample, recorder.Selection)))
                throw new InvalidOperationException("Refresh must leave the loaded incident and return to retained live history.");
        }
        finally { recorder.Close(); fixture.FlightRecorderEventReader = null; }

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
