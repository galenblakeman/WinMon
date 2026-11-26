using System.Windows;
using System.Windows.Controls;
using MonCore.Monitoring;
using WinMon.Controls;

namespace WinMon.Diagnostics;

internal sealed partial class WindowDiagnostics
{
    private static async Task CaptureHistorySidebarContentAsync(FlightRecorderWindow recorder, string path,
        CancellationToken cancellationToken)
    {
        var content = (FrameworkElement)recorder.Content;
        content.UpdateLayout();
        await content.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.Render, cancellationToken);
        // A visual brush isolates the client content from the root's margin and desktop occlusion.
        var drawing = new System.Windows.Media.DrawingVisual();
        using (var context = drawing.RenderOpen())
            context.DrawRectangle(new System.Windows.Media.VisualBrush(content)
                {
                    ViewboxUnits = System.Windows.Media.BrushMappingMode.Absolute,
                    Viewbox = new Rect(content.Margin.Left, content.Margin.Top, content.ActualWidth, content.ActualHeight)
                }, null,
                new Rect(0, 0, content.ActualWidth, content.ActualHeight));
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)Math.Ceiling(content.ActualWidth),
            (int)Math.Ceiling(content.ActualHeight), 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        bitmap.Render(drawing);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        using var buffer = new MemoryStream();
        encoder.Save(buffer);
        await File.WriteAllBytesAsync(path, buffer.ToArray(), cancellationToken);
    }

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

        VerifyHistorySidebarPreference(fixture);

        await CaptureHangOnlyFlightRecorderAsync(fixture, output, cancellationToken);
        await CaptureEmptyFlightRecorderAsync(fixture, output, cancellationToken);
        await CaptureLifecycleFlightRecorderAsync(fixture, output, cancellationToken);
    }

    private static void VerifyHistorySidebarPreference(MainWindow fixture)
    {
        var original = fixture.Preferences;
        FlightRecorderWindow? recorder = null;
        try
        {
            fixture.FlightRecorderButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            recorder = fixture.OwnedWindows.OfType<FlightRecorderWindow>().Single();
            recorder.ConfigureEventsSidebar(false, 0.75);
            recorder.UpdateLayout();
            recorder.EventsDivider.RaiseEvent(new System.Windows.Controls.Primitives.DragCompletedEventArgs(0, 0, false));
            var ratio = fixture.Preferences.HistoryEventsRatio;
            if (Math.Abs(ratio - 0.75) > 0.01)
                throw new InvalidOperationException("Resizing History events must update its saved width ratio.");
            recorder.EventsToggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            recorder.Close();
            fixture.FlightRecorderButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            recorder = fixture.OwnedWindows.OfType<FlightRecorderWindow>().Single();
            if (!recorder.EventsCollapsed)
                throw new InvalidOperationException("Reopening History must restore the collapsed events rail.");
            recorder.EventsToggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            recorder.UpdateLayout();
            if (Math.Abs(recorder.EventsColumn.ActualWidth / recorder.HistoryDetailsColumn.ActualWidth - ratio) > 0.01)
                throw new InvalidOperationException("Reopening History must restore its expanded events width.");
        }
        finally
        {
            recorder?.Close();
            fixture.Preferences = original;
        }
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
