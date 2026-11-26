// Exact stored layout values and channels require exact comparison.
// ReSharper disable CompareOfFloatsByEqualityOperator

using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using MonCore.Monitoring;
using WinMon.Controls;
using WinMon.Monitoring;
using WinMon.Preferences;

namespace WinMon.Diagnostics;

internal sealed partial class WindowDiagnostics
{
    private static async Task CaptureConnectionsSetupAsync(
        MainWindow fixture, ConnectionsView view, NetworkConnectionsFrame frame, ProcessUsage process,
        long created, string output, CancellationToken cancellationToken)
    {
        RequireConnectionsScenario(view.DisplayedRows.Sum(row => row.Connections.Count) == 6,
            "All fixture endpoints, including loopback, must be included.");
        var tabs = (StackPanel)fixture.ConnectionsTab.Parent;
        RequireConnectionsScenario(tabs.Children.IndexOf(fixture.ConnectionsTab) + 1 == tabs.Children.IndexOf(fixture.UsageTab),
            "Connections must appear immediately before Usage.");
        RequireConnectionsScenario(view.Rank.SelectedIndex == (int)ConnectionSort.Tcp &&
                view.Rank.Items[0] is ComboBoxItem { IsEnabled: false } &&
                view.Rank.Items[1] is ComboBoxItem { IsEnabled: false },
            "Disabled monitoring must use TCP ranking and disable throughput choices.");
        RequireConnectionsScenario(
            view.TrafficPrompt.Visibility ==
            (NetworkSetupBroker.Supported ? Visibility.Visible : Visibility.Collapsed),
            "The setup strip must follow desktop service support.");
        foreach (var theme in new[] { "Dark", "Light", "SmokedSapphire" })
        {
            fixture.Preferences = fixture.Preferences with { Theme = theme };
            fixture.ApplyTheme();
            await CaptureServiceWindowAsync(fixture,
                Path.Combine(output, $"connections-setup-{theme.ToLowerInvariant()}.png"), cancellationToken);
            if (!NetworkSetupBroker.Supported) continue;
            var button = view.EnableTrafficMonitoring;
            RequireConnectionsScenario(ChartPalettes.ContrastRatio(((SolidColorBrush)button.Background).Color,
                    ((SolidColorBrush)view.TrafficMonitoringLabel.Foreground).Color) >= 4.5,
                "Setup button text must have readable contrast.");
            var top = view.TrafficPrompt.TranslatePoint(new Point(), view).Y;
            RequireConnectionsScenario(
                top + view.TrafficPrompt.ActualHeight <= view.HeaderScroller.TranslatePoint(new Point(), view).Y,
                "Setup strip must appear above the table header.");
        }

        var setupCulture = Strings.Culture;
        try
        {
            Strings.SetCulture(CultureInfo.GetCultureInfo("de"));
            fixture.Width = 836;
            await CaptureServiceWindowAsync(fixture, Path.Combine(output, "connections-setup-german-narrow.png"),
                cancellationToken);
            if (NetworkSetupBroker.Supported)
            {
                var button = view.EnableTrafficMonitoring;
                var origin = button.TranslatePoint(new Point(), view.TrafficPrompt);
                RequireConnectionsScenario(origin.X >= 0 && origin.X + button.ActualWidth <= view.TrafficPrompt.ActualWidth &&
                        origin.Y + button.ActualHeight <= view.TrafficPrompt.ActualHeight,
                    "Translated setup button must fit the minimum-width window.");
                RequireConnectionsScenario(button.Focus() && button.IsKeyboardFocused, "Setup button must accept keyboard focus.");
            }
        }
        finally
        {
            Strings.SetCulture(setupCulture);
        }

        fixture.Width = 1200;
        fixture.Preferences = fixture.Preferences with { Theme = "Dark" };
        fixture.ApplyTheme();
        view.Rank.IsDropDownOpen = true;
        await CaptureServiceWindowAsync(fixture, Path.Combine(output, "connections-disabled-ranking.png"),
            cancellationToken);
        view.Rank.IsDropDownOpen = false;
        view.SelectConnectionForFixture(process.ProcessId, created);
        RequireConnectionsScenario(view.DisplayedRows.Count == 2, "Connections must show only two executable/process rows.");
        RequireConnectionsScenario(fixture.ConnectionCounts.Visibility == Visibility.Visible &&
                fixture.SystemCounts.Visibility == Visibility.Collapsed &&
                fixture.ConnectionTcpValue.Text == "4" && fixture.ConnectionUdpValue.Text == "2",
            "Connections totals are not in the activity header.");
        var selected = view.Rows.SelectedItem;
        fixture.SetConnectionsForFixture(frame);
        RequireConnectionsScenario(ReferenceEquals(selected, view.Rows.SelectedItem), "Refresh lost endpoint selection.");
        RequireConnectionsScenario(view.SelectedDetailsForFixture?.Contains("203.0.113.24:443", StringComparison.Ordinal) == true,
            "Copied endpoint details omit the remote endpoint.");
        RequireConnectionsScenario(view.SelectedProcessForFixture?.Identity == process.Identity,
            "Endpoint owner lifetime did not match.");
        string? copied = null;
        view.CopyRequested += text => copied = text;
        view.CopyDetails.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        RequireConnectionsScenario(copied?.Contains("203.0.113.24:443", StringComparison.Ordinal) == true, "Copy action failed.");

        foreach (var theme in new[] { "Dark", "Light" })
        {
            fixture.Preferences = fixture.Preferences with { Theme = theme };
            fixture.ApplyTheme();
            fixture.RefreshVisiblePresentation();
            await CaptureServiceWindowAsync(fixture,
                Path.Combine(output, $"connections-{theme.ToLowerInvariant()}.png"), cancellationToken);
        }

        fixture.Width = 836;
        await CaptureServiceWindowAsync(fixture, Path.Combine(output, "connections-narrow.png"), cancellationToken);
        fixture.Width = 1200;
        view.SelectConnectionForFixture(process.ProcessId, created);
    }

    private static async Task CaptureConnectionsHistoryAsync(
        MainWindow fixture, ConnectionsView view, MonitorSample sample, ProcessUsage process,
        long created, string output, CancellationToken cancellationToken)
    {
        fixture.SelectedSample = sample;
        fixture.RefreshVisiblePresentation();
        RequireConnectionsScenario(
            view.Notice.Text.Contains(Strings.ConnectionsHistoryNote, StringComparison.Ordinal) &&
            view.DisplayedRows.Count == 2,
            "Capture did not show its recorded endpoint observation.");
        string? copiedCapture = null;
        fixture.CopySnapshot(text => copiedCapture = text);
        RequireConnectionsScenario(copiedCapture?.Contains("203.0.113.24:443", StringComparison.Ordinal) == true &&
                copiedCapture.Contains("198.51.100.88:4242", StringComparison.Ordinal) &&
                copiedCapture.Contains("WinMon", StringComparison.Ordinal),
            "Copy capture must include historical endpoints and observed flows absent from the endpoint table.");
        await File.WriteAllTextAsync(Path.Combine(output, "capture-report.txt"), copiedCapture!, cancellationToken);
        fixture.ProcessesTab.IsChecked = true;
        fixture.RefreshVisiblePresentation();
        string? processTabCapture = null;
        fixture.CopySnapshot(text => processTabCapture = text);
        RequireConnectionsScenario(string.Equals(copiedCapture, processTabCapture, StringComparison.Ordinal),
            "Changing the active tab must not change the captured report's process or connection sections.");
        fixture.ConnectionsTab.IsChecked = true;
        fixture.RefreshVisiblePresentation();
        RequireConnectionsScenario(view.SelectedDetailsForFixture?.Contains("203.0.113.24:443", StringComparison.Ordinal) == true &&
                view.SelectedDetailsForFixture.Contains($"{Strings.ConnectionsReceived} / {Strings.ConnectionsSent}", StringComparison.Ordinal),
            "Capture endpoint details or recorded traffic are missing.");
        await CaptureServiceWindowAsync(fixture, Path.Combine(output, "connections-capture.png"),
            cancellationToken);
        view.ShowProcess.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        RequireConnectionsScenario(ReferenceEquals(fixture.SelectedSample, sample) && fixture.ProcessesTab.IsChecked == true,
            "Historical process navigation must preserve the selected sample.");
        var connectionLink = fixture.Processes.ProcessActions.CreateMoreMenu()!.Items.OfType<MenuItem>()
            .Single(item => Equals(item.Header, Strings.ShowInConnections));
        RequireConnectionsScenario(connectionLink.IsEnabled, "Recorded process navigation must find its endpoint capture.");
        connectionLink.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        RequireConnectionsScenario(fixture.ConnectionsTab.IsChecked == true && ReferenceEquals(fixture.SelectedSample, sample) &&
                view.Rows.SelectedItem is ConnectionDisplayRow,
            "Show in Connections must select the app and preserve the captured moment.");
        await CaptureServiceWindowAsync(fixture, Path.Combine(output, "connections-from-processes.png"), cancellationToken);
        fixture.ConnectionsTab.IsChecked = true;
        var missingSample = sample with { Timestamp = sample.Timestamp.AddSeconds(-1), MonotonicTimestamp = 0 };
        fixture.SelectedSample = missingSample;
        fixture.RefreshVisiblePresentation();
        RequireConnectionsScenario(fixture.CaptureCoverage.Text.Contains(Strings.HistorySelectedUnavailable, StringComparison.Ordinal),
            "Missing endpoint history needs an explicit coverage notice.");
        RequireConnectionsScenario(
            view.DisplayedRows.Count == 0 &&
            view.Notice.Text.Contains(Strings.ConnectionsHistoryUnavailable, StringComparison.Ordinal),
            "Missing endpoint history must not substitute live rows.");
        fixture.SelectedSample = sample;
        fixture.RefreshVisiblePresentation();
        view.Rank.Focus();
        view.Rank.IsDropDownOpen = true;
        fixture.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(fixture)!, 0,
                Key.Escape)
            { RoutedEvent = Keyboard.PreviewKeyDownEvent });
        RequireConnectionsScenario(ReferenceEquals(fixture.SelectedSample, sample),
            "Escape from the ranking menu changed the historical chart.");
        view.Rank.IsDropDownOpen = false;
        fixture.SelectedSample = null;
        fixture.RefreshVisiblePresentation();
    }

    private static async Task CaptureConnectionsFiltersAsync(
        MainWindow fixture, ConnectionsView view, NetworkConnectionsFrame frame, NetworkConnection[] endpoints,
        ProcessUsage process, long created, string output, CancellationToken cancellationToken)
    {
        view.Search.Text = "no-such-process";
        RequireConnectionsScenario(view.DisplayedRows.Count == 0, "Name filter did not hide nonmatching processes.");
        view.Search.Text = process.ProcessId.ToString(CultureInfo.InvariantCulture);
        RequireConnectionsScenario(view.DisplayedRows.Count == 1, "PID search lost matching executable group endpoints.");
        RequireConnectionsScenario(fixture.ConnectionTcpValue.Text == "4" && fixture.ConnectionUdpValue.Text == "2",
            "Filters changed system-wide header totals.");
        RequireConnectionsScenario(view.ClearConnectionSearchButton.Visibility == Visibility.Visible,
            "Search clear button is missing.");
        await CaptureServiceWindowAsync(fixture, Path.Combine(output, "connections-search.png"), cancellationToken);
        view.ClearConnectionSearchButton.Focus();
        view.ClearConnectionSearchButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        RequireConnectionsScenario(view.Search.Text.Length == 0 && view.Search.IsKeyboardFocused &&
                view.ClearConnectionSearchButton.Visibility == Visibility.Collapsed,
            "Search clear button did not clear, hide and return focus to search.");
        view.Rank.SelectedIndex = (int)ConnectionSort.Tcp;
        RequireConnectionsScenario(view.DisplayedRows.First().Connections.Count(c => c.Protocol == TransportProtocol.Tcp) == 3,
            "TCP ranking did not put the most connected app first.");
        view.AppsOnly.IsChecked = true;
        RequireConnectionsScenario(
            view.DisplayedRows.All(row =>
                row.Process?.HasAppWindow == true || row.Members.Any(m => m.HasAppWindow == true)),
            "App only retained a background connection group.");
        view.AppsOnly.IsChecked = false;
        fixture.SetConnectionsForFixture(frame with { Ipv6 = new(false, 5) });
        RequireConnectionsScenario(view.Notice.Text.Contains(Strings.ConnectionsPartial, StringComparison.Ordinal),
            "Partial collection lacks a notice.");
        RequireConnectionsScenario(
            fixture.ConnectionCounts.ToolTip?.ToString()
                ?.Contains(Strings.ConnectionsPartial, StringComparison.Ordinal) == true,
            "Header totals do not identify partial readings.");
        fixture.SetConnectionsForFixture(
            frame with { Connections = [], Ipv4 = new(false, 5), Ipv6 = new(false, 5) });
        RequireConnectionsScenario(view.DisplayedRows.Count == 0 && !view.ShowProcess.IsEnabled && !view.CopyDetails.IsEnabled,
            "Unavailable readings retained rows or actions.");
        fixture.SetConnectionsForFixture(frame with
        {
            Connections = endpoints.Select(c => c with { ProcessCreationTicks = created + 1 }).ToArray()
        });
        view.SelectConnectionForFixture(process.ProcessId, created + 1);
        RequireConnectionsScenario(view.SelectedProcessForFixture is null && !view.ShowProcess.IsEnabled,
            "A reused PID inherited process navigation.");
        fixture.SetConnectionsForFixture(frame);
        view.Rows.SelectedItem =
            view.DisplayedRows.First(row => row.Connections.Any(c => c.ProcessId == process.ProcessId));
        view.ShowProcess.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        RequireConnectionsScenario(fixture.ProcessesTab.IsChecked == true && fixture.Processes.GroupedChip.IsChecked == true,
            "Executable navigation did not open Grouped Processes.");
        fixture.ConnectionsTab.IsChecked = true;
        fixture.SetActivityPanelCollapsed(true);
        fixture.SetConnectionsForFixture(frame with { Connections = endpoints.Take(3).ToArray() });
        RequireConnectionsScenario(fixture.ConnectionTcpValue.Text == "2" && fixture.ConnectionUdpValue.Text == "1",
            "Collapsed activity header did not refresh its totals.");
        fixture.SetActivityPanelCollapsed(false);
        fixture.SetConnectionsForFixture(frame);
        view.SelectConnectionForFixture(process.ProcessId, created);
        view.ShowProcess.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        RequireConnectionsScenario(fixture.ProcessesTab.IsChecked == true && fixture.SelectedSample is null,
            "Show in Processes did not navigate to the live process.");
        fixture.ConnectionsTab.IsChecked = true;
    }

    private static async Task CaptureConnectionsTrafficAsync(
        MainWindow fixture, ConnectionsView view, ProcessNetworkFrame traffic, ProcessUsage process,
        long created, string output, CancellationToken cancellationToken)
    {
        fixture.SetProcessNetworkForFixture(traffic);
        RequireConnectionsScenario(fixture.ConnectionCounts.ToolTip?.ToString()
                    ?.Contains(Strings.ConnectionsRateTiming, StringComparison.Ordinal) == true &&
                AutomationProperties.GetName(fixture.ConnectionCounts)
                    .Contains(Strings.ConnectionsRateTiming, StringComparison.Ordinal),
            "Connections totals must explain their averaging window and delay to pointer and screen-reader users.");
        view.Rows.SelectedItem =
            view.DisplayedRows.First(row => row.Connections.Any(c => c.ProcessId == process.ProcessId));
        RequireConnectionsScenario(
            view.SelectedDetailsForFixture?.Contains(Strings.DetailReceivedSent, StringComparison.Ordinal) == true,
            "Observed network totals are missing from executable details.");
        RequireConnectionsScenario(view.SelectionDetail.Text.Contains(Strings.DetailReceivedSent, StringComparison.Ordinal) &&
                !view.SelectionDetail.Text.Contains('\n') && !view.SelectionDetail.Text.Contains('↓') &&
                !view.SelectionDetail.Text.Contains('↑'),
            "Selected executable traffic must be visible, not only copied.");
        foreach (var theme in new[] { "Dark", "Light" })
        {
            fixture.Width = 1200;
            fixture.Preferences = fixture.Preferences with { Theme = theme };
            fixture.ApplyTheme();
            fixture.RefreshVisiblePresentation();
            await CaptureServiceWindowAsync(fixture,
                Path.Combine(output, $"connections-network-{theme.ToLowerInvariant()}.png"), cancellationToken);
            var timingHelp = new ToolTip
            {
                Content = fixture.ConnectionCounts.ToolTip,
                PlacementTarget = fixture.ConnectionCounts
            };
            try
            {
                timingHelp.IsOpen = true;
                await fixture.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render, cancellationToken);
                timingHelp.UpdateLayout();
                await CaptureVisualAsync(timingHelp,
                    Path.Combine(output, $"connections-rate-help-{theme.ToLowerInvariant()}.png"), 1,
                    cancellationToken);
            }
            finally
            {
                timingHelp.IsOpen = false;
            }

            RequireConnectionsScenario(Math.Abs(view.ActionBar.ActualHeight - view.ActionBar.MinHeight) < 1 &&
                    view.ActionBar.MinHeight == fixture.Processes.ProcessActions.MinHeight &&
                    view.ActionBar.MinHeight == fixture.ServiceView.ActionBar.MinHeight,
                "Connections actions must use the same compact height as Processes and Services.");
        }

        fixture.Width = 836;
        await CaptureServiceWindowAsync(fixture, Path.Combine(output, "connections-network-narrow.png"),
            cancellationToken);
        RequireConnectionsScenario(Math.Abs(view.ActionBar.ActualHeight - view.ActionBar.MinHeight) < 1,
            "The narrow Connections action bar must keep its two-line height.");
        VerifyConnectionsScenarioToolbar(fixture);
        var scroller = FindConnectionsScenarioScroller(view.Rows) ??
                       throw new InvalidOperationException("Connections scroller was not realized.");
        scroller.ScrollToHorizontalOffset(scroller.ScrollableWidth);
        await CaptureServiceWindowAsync(fixture, Path.Combine(output, "connections-network-narrow-scrolled.png"),
            cancellationToken);
        RequireConnectionsScenario(
            Math.Abs(view.HeaderScroller.HorizontalOffset -
                     Math.Min(scroller.HorizontalOffset, view.HeaderScroller.ScrollableWidth)) < 1,
            $"Network header must follow horizontal scrolling: rows {scroller.HorizontalOffset}/{scroller.ExtentWidth}/{scroller.ViewportWidth}, header {view.HeaderScroller.HorizontalOffset}/{view.HeaderScroller.ExtentWidth}/{view.HeaderScroller.ViewportWidth}.");
        scroller.ScrollToHorizontalOffset(0);
        fixture.Width = 1200;
        fixture.SetProcessNetworkForFixture(traffic with
        {
            Quality = ProcessNetworkQuality.Unavailable | ProcessNetworkQuality.Incomplete
        });
        await CaptureServiceWindowAsync(fixture, Path.Combine(output, "connections-network-unavailable.png"),
            cancellationToken);
        RequireConnectionsScenario(view.Notice.Text.Contains(Strings.NetworkProcessMonitoringUnavailable, StringComparison.Ordinal),
            "Failed network collection must remain distinct from readable endpoints.");
        RequireConnectionsScenario(view.Notice.Visibility == Visibility.Collapsed &&
                view.ConnectionHeaders.ToolTip?.ToString()?.Contains(Strings.NetworkProcessMonitoringUnavailable,
                    StringComparison.Ordinal) == true,
            "Network status must use the header tooltip without a footer row.");
        fixture.SetProcessNetworkForFixture(null);
        fixture.SetProcessNetworkForFixture(traffic);
        view.SelectConnectionForFixture(process.ProcessId, created);
        var details = view.SelectedDetailsForFixture ??
                      throw new InvalidOperationException("Properties fixture has no selection.");
        var properties = new ServicePropertiesWindow("WinMon", details, _ => { }) { Owner = fixture };
        try
        {
            properties.Show();
            await CaptureServiceWindowAsync(properties, Path.Combine(output, "connections-properties.png"),
                cancellationToken);
        }
        finally
        {
            properties.Close();
        }
    }

    private static async Task CaptureConnectionsPinsAsync(
        MainWindow fixture, ConnectionsView view, NetworkConnectionsFrame frame, ProcessUsage process,
        string output, CancellationToken cancellationToken)
    {
        var pinButton = FindConnectionsScenarioPinButton(view.Rows, process) ??
                        throw new InvalidOperationException("Pin button was not realized.");
        pinButton.Focus();
        pinButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        RequireConnectionsScenario(view.DisplayedRows.First().Pinned && fixture.Preferences.PinnedConnections.Length == 1,
            "Connection pin did not reach the preferences or pinned-first list.");
        RequireConnectionsScenario(view.DisplayedRows.Count(row => row.RankedLabel.Length > 0) == 1 &&
                view.DisplayedRows.First(row => !row.Pinned).RankedLabel ==
                Strings.Format(Strings.RankedByMetric, ((ComboBoxItem)view.Rank.SelectedItem).Content),
            "Ranked connections must have one separator after the pinned rows.");
        await CaptureServiceWindowAsync(fixture, Path.Combine(output, "connections-pinned-ranked.png"),
            cancellationToken);
        view.PinnedOnly.IsChecked = true;
        RequireConnectionsScenario(view.DisplayedRows.Count == 1, "Pinned only must hide unpinned executable rows.");
        RequireConnectionsScenario(view.DisplayedRows.All(row => row.RankedLabel.Length == 0),
            "Pinned only must hide the ranking separator.");
        await CaptureServiceWindowAsync(fixture, Path.Combine(output, "connections-pinned.png"), cancellationToken);
        VerifyConnectionsScenarioToolbar(fixture);
        RequireConnectionsScenario(FindConnectionsScenarioScroller(view.Rows)?.ScrollableWidth <= 1,
            "A focused pin must not widen its row and introduce horizontal overflow.");
        fixture.SetConnectionsForFixture(frame with { Connections = [] });
        RequireConnectionsScenario(view.DisplayedRows.Count == 1 && view.DisplayedRows[0].Connections.Count == 0 &&
                view.DisplayedRows[0].Detail == Strings.ConnectionsNoObservedEndpoints,
            "A disconnected pin must remain without stale endpoints.");
        await CaptureServiceWindowAsync(fixture, Path.Combine(output, "connections-pinned-disconnected.png"),
            cancellationToken);
        fixture.SetConnectionsForFixture(frame);
        view.PinnedOnly.IsChecked = false;
    }

    private static void RequireConnectionsScenario(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void VerifyConnectionsScenarioToolbar(MainWindow fixture)
    {
        var view = fixture.ConnectionView;
        FrameworkElement[] controls =
        [
            view.TopCount, view.Rank, view.SearchBorder,
            view.AppsOnly, view.PinnedOnly, view.TrafficPeriod
        ];
        var bounds = controls.Select(control => control.TransformToAncestor(view)
            .TransformBounds(new Rect(control.RenderSize))).ToArray();
        var center = bounds[0].Top + bounds[0].Height / 2;
        RequireConnectionsScenario(bounds.All(rect => rect.Width > 0 && rect.Left >= 0 && rect.Right <= view.ActualWidth &&
                                   Math.Abs(rect.Top + rect.Height / 2 - center) < 1),
            "Connections controls must fit on a single row at minimum width.");
        RequireConnectionsScenario(!fixture.ConnectionVolumeValue.Text.Contains('\n'), "Activity totals must stay on one line.");
        RequireConnectionsScenario(
            bounds[3].Right <= bounds[4].Left && bounds[4].Right <= bounds[5].Left && view.AppsOnly.FontSize == 12,
            "App only and Pinned only must precede the period selector and match the Processes filter style.");
        if (view.PinSummary.Visibility == Visibility.Visible)
        {
            var pins = view.PinSummary.TransformToAncestor(view.ConnectionHeaders)
                .TransformBounds(new Rect(view.PinSummary.RenderSize));
            RequireConnectionsScenario(pins.Left > 0 && pins.Right <= view.ConnectionHeaders.ColumnDefinitions[0].ActualWidth &&
                    pins.Top >= 0 && pins.Bottom <= view.ConnectionHeaders.ActualHeight,
                "Pinned count must fit beside Process / PID in the first column header.");
        }
    }

    private static ScrollViewer? FindConnectionsScenarioScroller(DependencyObject parent)
    {
        if (parent is ScrollViewer scroller) return scroller;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
            if (FindConnectionsScenarioScroller(VisualTreeHelper.GetChild(parent, index)) is { } child)
                return child;
        return null;
    }

    private static Button? FindConnectionsScenarioPinButton(DependencyObject parent, ProcessUsage process)
    {
        if (parent is Button { DataContext: ConnectionDisplayRow { Process: { } owner } } button &&
            owner.Identity == process.Identity) return button;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            if (FindConnectionsScenarioPinButton(VisualTreeHelper.GetChild(parent, i), process) is { } found)
                return found;
        return null;
    }
}
