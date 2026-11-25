using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using WinMon.Controls;
using WinMon.Monitoring;

namespace WinMon.Diagnostics;

internal sealed partial class WindowDiagnostics
{
    private static void SeedServicesScenario(MainWindow fixture)
    {
        var tree = new ServiceTree();
        var anchor = DateTimeOffset.UtcNow;
        ServiceObservation[] services =
        [
            new("BuildAgent", "Build Agent", 4, 6240, 1),
            new("WinDefend", "Microsoft Defender Antivirus Service", 4, 3096, 0),
            new("wuauserv", "Windows Update", 4, 4820, 1),
            new("BITS", "Background Intelligent Transfer Service", 4, 4820, 1),
            new("WSearch", "Windows Search", 4, 6340, 1),
            new("Audiosrv", "Windows Audio", 4, 2160, 1),
            new("Spooler", "Print Spooler", 1, 0, 0)
        ];
        for (var i = 0; i < 120; i++)
        {
            var factor = 0.3 + 0.7 * Math.Exp(-Math.Pow((i - 90) / 14d, 2));
            ProcessUsage[] processes =
            [
                ServicesScenarioProcess(6240, 100, 0, "build-agent.exe", 1, 80),
                ServicesScenarioProcess(7312, 200, 6240, "compiler.exe", 8 * factor, 400),
                ServicesScenarioProcess(7460, 300, 6240, "worker.exe", 3 * factor, 160),
                ServicesScenarioProcess(3096, 110, 0, "MsMpEng.exe", 3.2, 286),
                ServicesScenarioProcess(4820, 120, 0, "svchost.exe", 0.8 * factor, 68),
                ServicesScenarioProcess(6340, 130, 0, "SearchIndexer.exe", 1.6, 164),
                ServicesScenarioProcess(2160, 140, 0, "svchost.exe", 0.2, 28)
            ];
            var sample = new MonitorSample(anchor.AddSeconds(i - 119), 28 + factor * 10, processes, 0,
                MonotonicTimestamp: Stopwatch.Frequency * (i + 1))
            {
                Resources = new ResourceSample
                {
                    CpuClockMhz = 4300,
                    UsedMemoryBytes = 21_000_000_000, TotalMemoryBytes = 32UL * 1073741824,
                    DiskName = "C:", DiskActivePercent = 8 + factor * 4, DiskBytesPerSecond = 54_000_000,
                    NetworkId = "fixture", NetworkName = "Ethernet", LinkBitsPerSecond = 1_000_000_000,
                    ReceiveBytesPerSecond = 200_000 + 1_000_000 * factor, SendBytesPerSecond = 130_000 * factor,
                    GpuPercent = 5 + 6 * factor, GpuEngine = "3D", GpuId = "illustrative"
                }
            };
            fixture.Session.History.Add(sample);
            fixture.Session.Services.Add(tree.Capture(sample, new ServiceCatalog(long.MaxValue, services)));
        }
    }

    private static ServiceDisplayRow PrepareServicesScenarioPins(MainWindow fixture)
    {
        fixture.Show();
        fixture.ServicesTab.IsChecked = true;
        if (fixture.ServiceTotalValue.Text != "7" || fixture.ServiceRunningValue.Text != "6")
            throw new InvalidOperationException("Service totals must count services, not host groups.");
        foreach (var group in fixture.Session.Services.Frames[^1].Groups
                     .Where(g => g.Shared || g.Services[0].Name == "BuildAgent"))
            fixture.ServiceView.ExpandGroup(group.Key);
        fixture.ServiceView.Rows.SelectedIndex = 0;
        fixture.ServiceView.RestorePins(["BuildAgent", "Spooler"]);
        fixture.ServiceView.UpdateLayout();
        var agentRow = fixture.ServiceView.Rows.Items.Cast<ServiceDisplayRow>()
            .Single(row => row.Service?.Name == "BuildAgent");
        var agentPin = ServicesScenarioPinButtons(fixture.ServiceView).Single(button => button.DataContext == agentRow);
        agentPin.Focus();
        agentPin.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        fixture.ServiceView.UpdateLayout();
        if (Keyboard.FocusedElement is not Button { DataContext: ServiceDisplayRow focused } ||
            focused != agentRow || agentRow.Pinned)
            throw new InvalidOperationException("Service pin focus must follow its row when ranking moves it.");
        ServicesScenarioPinButtons(fixture.ServiceView).Single(button => button.DataContext == agentRow)
            .RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        fixture.ServiceView.RestorePins(["BuildAgent", "Spooler"]);
        var pinnedHeaders = fixture.ServiceView.Rows.Items.Cast<ServiceDisplayRow>()
            .Where(row => row.Key == row.Group.Key).Take(2).ToArray();
        if (pinnedHeaders[0].Service?.Name != "BuildAgent" || pinnedHeaders[1].Service?.Name != "Spooler" ||
            !pinnedHeaders.All(row => row.PinnedHeader) ||
            fixture.ServiceView.Rows.Items.Cast<ServiceDisplayRow>().Select(row => row.Key).Distinct().Count() !=
            fixture.ServiceView.Rows.Items.Count)
            throw new InvalidOperationException(
                "Pinned service hosts must precede ranked hosts without duplicates.");
        return agentRow;
    }

    private static async Task CaptureServicesScenarioThemesAsync(MainWindow fixture, string output,
        CancellationToken cancellationToken)
    {
        foreach (var theme in new[] { "Dark", "Light" })
        {
            fixture.Preferences = fixture.Preferences with { Theme = theme };
            fixture.ApplyTheme();
            fixture.RefreshVisiblePresentation();
            await CaptureServiceWindowAsync(fixture,
                Path.Combine(output, $"services-{theme.ToLowerInvariant()}.png"), cancellationToken);
            VerifyServicesScenarioToolbar(fixture.ServiceView);
            fixture.SetChartPanelCollapsed(true);
            await CaptureServiceWindowAsync(fixture,
                Path.Combine(output, $"services-pins-{theme.ToLowerInvariant()}.png"), cancellationToken);
            fixture.SetChartPanelCollapsed(false);
            fixture.OnPin(fixture, new RoutedEventArgs());
            try
            {
                if (!fixture.Topmost || fixture.PinButton.Background != fixture.FindResource("SelectedSurface"))
                    throw new InvalidOperationException(
                        "Always on top must show its highlighted enabled background.");
                await CaptureServiceWindowAsync(fixture,
                    Path.Combine(output, $"always-on-top-{theme.ToLowerInvariant()}.png"), cancellationToken);
            }
            finally
            {
                fixture.OnPin(fixture, new RoutedEventArgs());
            }

            if (fixture.Topmost || fixture.PinButton.ReadLocalValue(Control.BackgroundProperty) !=
                DependencyProperty.UnsetValue)
                throw new InvalidOperationException(
                    "Disabling Always on top must restore the title button's default background.");
            fixture.PinButton.Focus();
            fixture.UpdateLayout();
            var buttonBody = (Border)fixture.PinButton.Template.FindName("Body", fixture.PinButton);
            if (buttonBody.BorderBrush is not SolidColorBrush offBorder || offBorder.Color.A != 0)
                throw new InvalidOperationException(
                    "Always on top must have no selected outline when off, even while focused.");
            await CaptureServiceWindowAsync(fixture,
                Path.Combine(output, $"always-on-top-off-focused-{theme.ToLowerInvariant()}.png"),
                cancellationToken);
            if (fixture.ServiceView.Rows.Items[0] is not ServiceDisplayRow target)
                throw new InvalidOperationException("The first service row is missing.");
            var menu = fixture.ServiceView.CreateServiceMenu(target)!;
            menu.PlacementTarget = fixture.ServiceView.Rows;
            menu.Placement = PlacementMode.Relative;
            menu.HorizontalOffset = 80;
            menu.VerticalOffset = 30;
            try
            {
                menu.IsOpen = true;
                await CaptureServiceWindowAsync(fixture,
                    Path.Combine(output, $"services-menu-{theme.ToLowerInvariant()}.png"), cancellationToken);
            }
            finally
            {
                menu.IsOpen = false;
            }
        }
    }

    private static async Task CaptureServicesScenarioFiltersAsync(MainWindow fixture, ServiceDisplayRow agentRow,
        string output, CancellationToken cancellationToken)
    {
        fixture.Width = 836;
        fixture.Height = 840;
        fixture.RefreshVisiblePresentation();
        await CaptureServiceWindowAsync(fixture, Path.Combine(output, "services-narrow.png"), cancellationToken);
        VerifyServicesScenarioToolbar(fixture.ServiceView);
        fixture.ServiceView.PinnedOnly = true;
        await CaptureServiceWindowAsync(fixture, Path.Combine(output, "services-pinned-only.png"),
            cancellationToken);
        if (fixture.ServiceView.Rows.Items.Cast<ServiceDisplayRow>().Any(row =>
                !row.Group.Services.Any(service => fixture.ServiceView.SavedPins.Contains(service.Name))))
            throw new InvalidOperationException("Pinned-only services must hide unpinned hosts.");
        var filteredPin = ServicesScenarioPinButtons(fixture.ServiceView).Single(button => button.DataContext == agentRow);
        filteredPin.Focus();
        filteredPin.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        if (!fixture.ServiceView.PinnedOnlyCheckBox.IsKeyboardFocusWithin)
            throw new InvalidOperationException(
                "Unpinning a hidden row must return focus to the pinned-only filter.");
        fixture.ServiceView.RestorePins(["BuildAgent", "Spooler"]);
        fixture.ServiceView.PinnedOnly = false;
        fixture.ServiceView.Search.Text = "Build";
        await CaptureServiceWindowAsync(fixture, Path.Combine(output, "services-search.png"), cancellationToken);
        fixture.ServiceView.ClearServiceSearchButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        if (fixture.ServiceView.Search.Text.Length != 0 || !fixture.ServiceView.Search.IsKeyboardFocusWithin)
            throw new InvalidOperationException("Service search clear must reset the query and restore focus.");
        // Constrain only the control, keeping the main window's minimum size and caption behavior intact.
        fixture.ServiceView.Width = 600;
        fixture.ServiceView.HorizontalAlignment = HorizontalAlignment.Left;
        await CaptureServiceWindowAsync(fixture, Path.Combine(output, "services-header-wrapped.png"),
            cancellationToken);
        VerifyServicesScenarioToolbar(fixture.ServiceView);
        if (Grid.GetRow(fixture.ServiceView.SearchBorder) != 1)
            throw new InvalidOperationException(
                "Constrained service toolbar must wrap search below the ranking controls.");
        fixture.ServiceView.Width = double.NaN;
        fixture.ServiceView.HorizontalAlignment = HorizontalAlignment.Stretch;
    }

    private static async Task CaptureServicesScenarioHistoryAsync(MainWindow fixture, string output,
        CancellationToken cancellationToken)
    {
        fixture.SelectedSample = fixture.Session.History.Samples[90];
        fixture.RefreshVisiblePresentation();
        if (!ReferenceEquals(fixture.SnapshotReport?.Sample, fixture.SelectedSample))
            throw new InvalidOperationException("Copy capture must include processes while Services is selected.");
        string? copiedCapture = null;
        fixture.CopySnapshot(text => copiedCapture = text);
        if (copiedCapture is null || !copiedCapture.Contains(fixture.ServiceView.CaptureReport(), StringComparison.Ordinal))
            throw new InvalidOperationException("Copy capture must append the selected Services view.");
        await File.WriteAllTextAsync(Path.Combine(output, "services-copied-capture.txt"), copiedCapture, cancellationToken);
        if (fixture.ServiceView.Stop.IsEnabled || fixture.ServiceView.Restart.IsEnabled ||
            fixture.ServiceView.Start.IsEnabled)
            throw new InvalidOperationException("Historical service actions must be disabled.");
        await CaptureServiceWindowAsync(fixture, Path.Combine(output, "services-snapshot.png"), cancellationToken);
        var lastRecorded = fixture.Session.History.Samples[^1];
        var withoutServices = lastRecorded with
        {
            Timestamp = lastRecorded.Timestamp.AddSeconds(1),
            MonotonicTimestamp = lastRecorded.MonotonicTimestamp + Stopwatch.Frequency
        };
        fixture.Session.Record(withoutServices);
        fixture.SelectedSample = withoutServices;
        fixture.RefreshVisiblePresentation();
        if (!fixture.CaptureCoverage.Text.Contains(Strings.HistorySelectedUnavailable, StringComparison.Ordinal))
            throw new InvalidOperationException("Missing service observations need an explicit coverage notice.");
        await CaptureServiceWindowAsync(fixture, Path.Combine(output, "services-history-unavailable.png"), cancellationToken);
    }

    private static async Task CaptureServicesScenarioDialogsAsync(MainWindow fixture, string output,
        CancellationToken cancellationToken)
    {
        foreach (var theme in new[] { "Dark", "Light" })
        {
            fixture.Preferences = fixture.Preferences with { Theme = theme };
            fixture.ApplyTheme();
            foreach (var confirmation in new[] { true, false })
            {
                var message = confirmation
                    ? Strings.Format(Strings.ConfirmServiceAction, Strings.StopService, "Example Service")
                    : Strings.TheClipboardIsUnavailableTryCopyAgain;
                var dialog = new MessageDialog(confirmation ? Strings.Services : Strings.CopyDetails,
                    message, confirmation ? Strings.StopService : null) { Owner = fixture };
                try
                {
                    dialog.Show();
                    await CaptureServiceWindowAsync(dialog, Path.Combine(output,
                            $"dialog-{theme.ToLowerInvariant()}-{(confirmation ? "confirmation" : "notice")}.png"),
                        cancellationToken);
                }
                finally
                {
                    dialog.Close();
                }
            }
        }
    }

    private static void CompleteServicesScenario(MainWindow fixture)
    {
        fixture.SelectedSample = null;
        var newest = fixture.Session.History.Samples[^1];
        fixture.Session.History.Add(newest with
        {
            Timestamp = newest.Timestamp.AddSeconds(1),
            MonotonicTimestamp = newest.MonotonicTimestamp + Stopwatch.Frequency,
            Processes = [.. newest.Processes, ServicesScenarioProcess(9999, 999, 0, "new-worker.exe", 100, 50)]
        });
        fixture.RefreshVisiblePresentation();
        fixture.ProcessesTab.IsChecked = true;
        if (!fixture.Processes.Rows.Any(row => row.Visibility == Visibility.Visible && row.Identity?.Item1 == 9999))
            throw new InvalidOperationException("Processes must show the latest sample after leaving Services.");

        fixture.ServiceActionBusy = true;
        fixture.Close();
        if (!fixture.IsVisible || fixture.IsClosing || !fixture.CloseAfterServiceAction)
            throw new InvalidOperationException("Closing must wait for an accepted service action to finish.");
        fixture.ServiceActionBusy = false;
        fixture.CloseAfterServiceAction = false;
    }

    private static ProcessUsage ServicesScenarioProcess(int pid, long created, int parent, string name, double cpu, ulong memory)
    {
        return new ProcessUsage(pid, created, name, cpu, memory * 1048576, cpu * 250000)
            { ParentProcessId = parent, PrivateBytes = memory * 1048576 };
    }

    private static void VerifyServicesScenarioToolbar(ServicesView view)
    {
        view.UpdateLayout();
        FrameworkElement[] parts = [view.RankingControls, view.SearchBorder, view.ServiceFilters];
        var bounds = parts.Select(p => p.TransformToAncestor(view).TransformBounds(new Rect(p.RenderSize)))
            .ToArray();
        var worker = view.Workers.TransformToAncestor(view).TransformBounds(new Rect(view.Workers.RenderSize));
        var status = view.StatusFilter.TransformToAncestor(view)
            .TransformBounds(new Rect(view.StatusFilter.RenderSize));
        var pinnedOnly = view.PinnedOnlyCheckBox.TransformToAncestor(view)
            .TransformBounds(new Rect(view.PinnedOnlyCheckBox.RenderSize));
        if (worker.Left < bounds[1].Right - 1 || pinnedOnly.Left < worker.Right - 1 ||
            status.Left < pinnedOnly.Right - 1)
            throw new InvalidOperationException(
                "Service filters must follow Search, Workers, Pinned only, Status order.");
        var firstPin = ServicesScenarioPinButtons(view).First(button => button.IsVisible);
        var pinBounds = firstPin.TransformToAncestor(view).TransformBounds(new Rect(firstPin.RenderSize));
        if (pinBounds.Width < 20 || pinBounds.Left < 0 || pinBounds.Right > view.ActualWidth)
            throw new InvalidOperationException("Service pin buttons must remain visible at narrow widths.");
        if (bounds.Any(b => b.Left < -1 || b.Right > view.ActualWidth + 1) ||
            bounds.SelectMany((b, i) => bounds.Skip(i + 1).Select(other => Rect.Intersect(b, other)))
                .Any(intersection => intersection is { IsEmpty: false, Width: > 1, Height: > 1 }))
            throw new InvalidOperationException(
                "Service toolbar controls overlap or extend beyond the available width.");
        var identity = view.SelectionIdentity.TransformToAncestor(view)
            .TransformBounds(new Rect(view.SelectionIdentity.RenderSize));
        var actions = view.ActionControls.TransformToAncestor(view)
            .TransformBounds(new Rect(view.ActionControls.RenderSize));
        var overlap = Rect.Intersect(identity, actions);
        if (identity.Width < 20 || view.Summary.ActualWidth < 12 || view.Summary.ActualHeight < 1 ||
            identity.Left < -1 || actions.Right > view.ActualWidth + 1 ||
            overlap is { IsEmpty: false, Width: > 1, Height: > 1 })
            throw new InvalidOperationException(
                "Service identity and action buttons must remain visible without overlap.");
    }

    private static IEnumerable<Button> ServicesScenarioPinButtons(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is Button { Name: "ServicePin" } button) yield return button;
            foreach (var nested in ServicesScenarioPinButtons(child)) yield return nested;
        }
    }
}
