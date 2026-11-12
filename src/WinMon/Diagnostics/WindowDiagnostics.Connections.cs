// Exact stored layout values and channels returned by Max require exact comparison.
// ReSharper disable CompareOfFloatsByEqualityOperator

using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Principal;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using WinMon.Controls;
using WinMon.Monitoring;
using WinMon.Preferences;

namespace WinMon.Diagnostics;

internal sealed partial class WindowDiagnostics
{
    internal static async Task CaptureConnectionsDiagnosticsAsync(string output, CancellationToken cancellationToken)
    {
        using var identity = WindowsIdentity.GetCurrent();
        var elevated = new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        using var udp = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port, cancellationToken);
        using var accepted = await listener.AcceptTcpClientAsync(cancellationToken);
        await client.GetStream().WriteAsync(new byte[] { 0x57, 0x4d }, cancellationToken);
        var received = new byte[2];
        await accepted.GetStream().ReadExactlyAsync(received, cancellationToken);
        Require(received[0] == 0x57 && received[1] == 0x4d, "Loopback TCP exchange failed.");
        NetworkConnectionsFrame native;
        var times = new List<double>();
        try
        {
            for (var i = 0; i < 6; i++)
            {
                var started = Stopwatch.GetTimestamp();
                _ = await Task.Run(WindowsConnections.Capture, cancellationToken);
                times.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            }

            native = await Task.Run(WindowsConnections.Capture, cancellationToken);
            var tcpPort = ((IPEndPoint)listener.LocalEndpoint).Port;
            var clientPort = ((IPEndPoint)client.Client.LocalEndPoint!).Port;
            var udpPort = ((IPEndPoint)udp.Client.LocalEndPoint!).Port;
            await File.WriteAllTextAsync(Path.Combine(output, "native-connections.json"), JsonSerializer.Serialize(new
            {
                Elevated = elevated,
                Environment.ProcessId, TcpPort = tcpPort, UdpPort = udpPort,
                native.Ipv4, native.Ipv6,
                OwnEndpoints = native.Connections.Where(c => c.ProcessId == Environment.ProcessId).Select(c => new
                {
                    c.ProcessId, c.ProcessCreationTicks, Protocol = c.Protocol.ToString(),
                    Local = c.LocalEndpoint.ToString(), Remote = c.RemoteEndpoint?.ToString(),
                    State = c.State?.ToString()
                }).ToArray()
            }, new JsonSerializerOptions { WriteIndented = true }), cancellationToken);
            Require(native.Connections.Any(c => c.ProcessId == Environment.ProcessId &&
                                                c.Protocol == TransportProtocol.Tcp &&
                                                c.LocalEndpoint.Port == tcpPort &&
                                                c.RemoteEndpoint is null),
                "Native TCP listener ownership was not observed.");
            Require(native.Connections.Any(c => c.ProcessId == Environment.ProcessId &&
                                                c.Protocol == TransportProtocol.Tcp &&
                                                ((c.LocalEndpoint.Port == tcpPort &&
                                                  c.RemoteEndpoint?.Port == clientPort) ||
                                                 (c.LocalEndpoint.Port == clientPort &&
                                                  c.RemoteEndpoint?.Port == tcpPort))),
                "Native TCP peer endpoints were not observed.");
            Require(native.Connections.Any(c => c.ProcessId == Environment.ProcessId &&
                                                c.Protocol == TransportProtocol.Udp &&
                                                c.LocalEndpoint.Port == udpPort && c.RemoteEndpoint is null),
                "Native UDP endpoint ownership was not observed.");
        }
        finally
        {
            listener.Stop();
        }

        var fixture = new MainWindow(true) { Width = 1200, Height = 950 };
        fixture.Loaded -= fixture.OnLoaded;
        fixture.Preferences = fixture.Preferences with { ActivityPanelRatio = 1.3 };
        fixture.ApplyActivityPanel();
        using var self = Process.GetCurrentProcess();
        var created = self.StartTime.ToUniversalTime().ToFileTimeUtc();
        var process = new ProcessUsage(Environment.ProcessId, created, "WinMon", 2)
            { AppName = "WinMon", ImagePath = Environment.ProcessPath ?? "", HasAppWindow = true };
        var sibling = process with { ProcessId = process.ProcessId + 100000, CreationTicks = 222, CpuPercent = 1 };
        var sample = new MonitorSample(DateTimeOffset.Now, 35, [process, sibling], 0)
        {
            Resources = new ResourceSample
            {
                CpuClockMhz = 4300,
                TotalMemoryBytes = 32UL * 1073741824, UsedMemoryBytes = 16UL * 1073741824,
                DiskName = "C:", DiskActivePercent = 8, DiskBytesPerSecond = 12_000_000,
                NetworkId = "fixture", NetworkName = "Ethernet", LinkBitsPerSecond = 1_000_000_000,
                ReceiveBytesPerSecond = 2_000_000,
                SendBytesPerSecond = 500_000, GpuPercent = 12
            }
        };
        var endpoints = new NetworkConnection[]
        {
            new(process.ProcessId, created, TransportProtocol.Tcp, new(IPAddress.Parse("192.168.1.25"), 52144),
                new(IPAddress.Parse("203.0.113.24"), 443), TcpState.Established),
            new(process.ProcessId, created, TransportProtocol.Tcp, new(IPAddress.IPv6Loopback, 52145),
                new(IPAddress.IPv6Loopback, 443), TcpState.Established),
            new(process.ProcessId, created, TransportProtocol.Udp, new(IPAddress.Any, 5353)),
            new(sibling.ProcessId, sibling.CreationTicks, TransportProtocol.Tcp,
                new(IPAddress.Parse("192.168.1.25"), 52146),
                new(IPAddress.Parse("198.51.100.12"), 443), TcpState.Established),
            new(sibling.ProcessId, sibling.CreationTicks, TransportProtocol.Udp, new(IPAddress.Any, 5354)),
            new(4, null, TransportProtocol.Tcp, new(IPAddress.Any, 445), null, TcpState.Listen)
        };
        var frame = new NetworkConnectionsFrame(endpoints, NetworkAddressFamilyStatus.Success,
            NetworkAddressFamilyStatus.Success);
        var traffic = new ProcessNetworkFrame(Stopwatch.GetTimestamp(), Stopwatch.Frequency,
            ProcessNetworkQuality.Provisional,
            [
                new ProcessNetworkReading(process.ProcessId, created, 18_000_000, 2_500_000, 2_000_000, 250_000),
                new ProcessNetworkReading(sibling.ProcessId, sibling.CreationTicks, 4_000_000, 750_000, 500_000,
                    100_000)
            ])
        {
            FlowTrafficSupported = true,
            Flows =
            [
                new(process.ProcessId, created, TransportProtocol.Tcp, "192.168.1.25:52144", "203.0.113.24:443",
                    18_000_000, 2_500_000, 2_000_000, 250_000),
                new(process.ProcessId, created, TransportProtocol.Udp, "192.168.1.25:5353", "198.51.100.88:4242",
                    100_000, 50_000, 1_000, 500)
            ]
        };
        var baseTraffic = traffic;
        var receivedTotals = new long[traffic.Processes.Count];
        var sentTotals = new long[traffic.Processes.Count];
        for (var i = 120; i >= 0; i--)
        {
            var factor = 0.3 + 0.7 * Math.Exp(-Math.Pow((i - 30) / 14d, 2));
            traffic = baseTraffic with
            {
                TimestampQpc = Stopwatch.Frequency * (121 - i),
                WindowStartQpc = Stopwatch.Frequency * Math.Max(0, 113 - i),
                WindowEndQpc = Stopwatch.Frequency * Math.Max(0, 118 - i),
                Processes = baseTraffic.Processes.Select((reading, index) =>
                {
                    var receive = reading.ReceiveBytesPerSecond!.Value * factor;
                    var send = reading.SendBytesPerSecond!.Value * factor;
                    receivedTotals[index] += (long)receive;
                    sentTotals[index] += (long)send;
                    return reading with
                    {
                        ReceivedBytes = receivedTotals[index], SentBytes = sentTotals[index],
                        ReceiveBytesPerSecond = receive, SendBytesPerSecond = send
                    };
                }).ToArray()
            };
            var past = sample with
            {
                Timestamp = sample.Timestamp.AddSeconds(-i), MonotonicTimestamp = traffic.TimestampQpc,
                TotalCpuPercent = 15 + 20 * factor,
                Resources = sample.Resources with
                {
                    DiskActivePercent = 4 + 12 * factor,
                    ReceiveBytesPerSecond = traffic.Processes.Sum(reading => reading.ReceiveBytesPerSecond),
                    SendBytesPerSecond = traffic.Processes.Sum(reading => reading.SendBytesPerSecond)
                }
            };
            fixture.Session.Record(past, past, connections: frame, network: traffic);
            if (i == 0) sample = past;
        }

        var exampleTotals = ConnectionTrafficTotals.Build(fixture.Session.Connections.Captures,
            fixture.Session.Connections.Latest, 300);
        Require(exampleTotals.Sum().Received > 50_000_000 && exampleTotals.Sum().Sent > 5_000_000,
            "The traffic example must accumulate nonzero received and sent history.");
        try
        {
            fixture.Show();
            fixture.ConnectionsTab.IsChecked = true;
            fixture.SetConnectionsForFixture(frame);
            var view = fixture.ConnectionView;
            Require(view.DisplayedRows.Sum(row => row.Connections.Count) == 6,
                "All fixture endpoints, including loopback, must be included.");
            var tabs = (StackPanel)fixture.ConnectionsTab.Parent;
            Require(tabs.Children.IndexOf(fixture.ConnectionsTab) + 1 == tabs.Children.IndexOf(fixture.UsageTab),
                "Connections must appear immediately before Usage.");
            Require(view.Rank.SelectedIndex == (int)ConnectionSort.Tcp &&
                    view.Rank.Items[0] is ComboBoxItem { IsEnabled: false } &&
                    view.Rank.Items[1] is ComboBoxItem { IsEnabled: false },
                "Disabled monitoring must use TCP ranking and disable throughput choices.");
            Require(
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
                Require(ChartPalettes.ContrastRatio(((SolidColorBrush)button.Background).Color,
                        ((SolidColorBrush)view.TrafficMonitoringLabel.Foreground).Color) >= 4.5,
                    "Setup button text must have readable contrast.");
                var top = view.TrafficPrompt.TranslatePoint(new Point(), view).Y;
                Require(
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
                    Require(origin.X >= 0 && origin.X + button.ActualWidth <= view.TrafficPrompt.ActualWidth &&
                            origin.Y + button.ActualHeight <= view.TrafficPrompt.ActualHeight,
                        "Translated setup button must fit the minimum-width window.");
                    Require(button.Focus() && button.IsKeyboardFocused, "Setup button must accept keyboard focus.");
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
            Require(view.DisplayedRows.Count == 2, "Connections must show only two executable/process rows.");
            Require(fixture.ConnectionCounts.Visibility == Visibility.Visible &&
                    fixture.SystemCounts.Visibility == Visibility.Collapsed &&
                    fixture.ConnectionTcpValue.Text == "4" && fixture.ConnectionUdpValue.Text == "2",
                "Connections totals are not in the activity header.");
            var selected = view.Rows.SelectedItem;
            fixture.SetConnectionsForFixture(frame);
            Require(ReferenceEquals(selected, view.Rows.SelectedItem), "Refresh lost endpoint selection.");
            Require(view.SelectedDetailsForFixture?.Contains("203.0.113.24:443", StringComparison.Ordinal) == true,
                "Copied endpoint details omit the remote endpoint.");
            Require(view.SelectedProcessForFixture?.Identity == process.Identity,
                "Endpoint owner lifetime did not match.");
            string? copied = null;
            view.CopyRequested += text => copied = text;
            view.CopyDetails.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Require(copied?.Contains("203.0.113.24:443", StringComparison.Ordinal) == true, "Copy action failed.");

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
            fixture.SelectedSample = sample;
            fixture.RefreshVisiblePresentation();
            Require(
                view.Notice.Text.Contains(Strings.ConnectionsHistoryNote, StringComparison.Ordinal) &&
                view.DisplayedRows.Count == 2,
                "Capture did not show its recorded endpoint observation.");
            string? copiedCapture = null;
            fixture.CopySnapshot(text => copiedCapture = text);
            Require(copiedCapture?.Contains("203.0.113.24:443", StringComparison.Ordinal) == true &&
                    copiedCapture.Contains("198.51.100.88:4242", StringComparison.Ordinal) &&
                    copiedCapture.Contains("WinMon", StringComparison.Ordinal),
                "Copy capture must include historical endpoints and observed flows absent from the endpoint table.");
            await File.WriteAllTextAsync(Path.Combine(output, "capture-report.txt"), copiedCapture!, cancellationToken);
            fixture.ProcessesTab.IsChecked = true;
            fixture.RefreshVisiblePresentation();
            string? processTabCapture = null;
            fixture.CopySnapshot(text => processTabCapture = text);
            Require(string.Equals(copiedCapture, processTabCapture, StringComparison.Ordinal),
                "Changing the active tab must not change the captured report's process or connection sections.");
            fixture.ConnectionsTab.IsChecked = true;
            fixture.RefreshVisiblePresentation();
            Require(view.SelectedDetailsForFixture?.Contains("203.0.113.24:443", StringComparison.Ordinal) == true &&
                    view.SelectedDetailsForFixture.Contains(Strings.DetailReceivedSent, StringComparison.Ordinal),
                "Capture endpoint details or recorded traffic are missing.");
            await CaptureServiceWindowAsync(fixture, Path.Combine(output, "connections-capture.png"),
                cancellationToken);
            view.ShowProcess.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Require(ReferenceEquals(fixture.SelectedSample, sample) && fixture.ProcessesTab.IsChecked == true,
                "Historical process navigation must preserve the selected sample.");
            var connectionLink = fixture.Processes.ProcessActions.CreateMoreMenu()!.Items.OfType<MenuItem>()
                .Single(item => Equals(item.Header, Strings.ShowInConnections));
            Require(connectionLink.IsEnabled, "Recorded process navigation must find its endpoint capture.");
            connectionLink.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Require(fixture.ConnectionsTab.IsChecked == true && ReferenceEquals(fixture.SelectedSample, sample) &&
                    view.Rows.SelectedItem is ConnectionDisplayRow,
                "Show in Connections must select the app and preserve the captured moment.");
            await CaptureServiceWindowAsync(fixture, Path.Combine(output, "connections-from-processes.png"), cancellationToken);
            fixture.ConnectionsTab.IsChecked = true;
            var missingSample = sample with { Timestamp = sample.Timestamp.AddSeconds(-1), MonotonicTimestamp = 0 };
            fixture.SelectedSample = missingSample;
            fixture.RefreshVisiblePresentation();
            Require(fixture.CaptureCoverage.Text.Contains(Strings.HistorySelectedUnavailable, StringComparison.Ordinal),
                "Missing endpoint history needs an explicit coverage notice.");
            Require(
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
            Require(ReferenceEquals(fixture.SelectedSample, sample),
                "Escape from the ranking menu changed the historical chart.");
            view.Rank.IsDropDownOpen = false;
            fixture.SelectedSample = null;
            fixture.RefreshVisiblePresentation();
            view.Search.Text = "no-such-process";
            Require(view.DisplayedRows.Count == 0, "Name filter did not hide nonmatching processes.");
            view.Search.Text = process.ProcessId.ToString(CultureInfo.InvariantCulture);
            Require(view.DisplayedRows.Count == 1, "PID search lost matching executable group endpoints.");
            Require(fixture.ConnectionTcpValue.Text == "4" && fixture.ConnectionUdpValue.Text == "2",
                "Filters changed system-wide header totals.");
            Require(view.ClearConnectionSearchButton.Visibility == Visibility.Visible,
                "Search clear button is missing.");
            await CaptureServiceWindowAsync(fixture, Path.Combine(output, "connections-search.png"), cancellationToken);
            view.ClearConnectionSearchButton.Focus();
            view.ClearConnectionSearchButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Require(view.Search.Text.Length == 0 && view.Search.IsKeyboardFocused &&
                    view.ClearConnectionSearchButton.Visibility == Visibility.Collapsed,
                "Search clear button did not clear, hide and return focus to search.");
            view.Rank.SelectedIndex = (int)ConnectionSort.Tcp;
            Require(view.DisplayedRows.First().Connections.Count(c => c.Protocol == TransportProtocol.Tcp) == 3,
                "TCP ranking did not put the most connected app first.");
            view.AppsOnly.IsChecked = true;
            Require(
                view.DisplayedRows.All(row =>
                    row.Process?.HasAppWindow == true || row.Members.Any(m => m.HasAppWindow == true)),
                "App only retained a background connection group.");
            view.AppsOnly.IsChecked = false;
            fixture.SetConnectionsForFixture(frame with { Ipv6 = new(false, 5) });
            Require(view.Notice.Text.Contains(Strings.ConnectionsPartial, StringComparison.Ordinal),
                "Partial collection lacks a notice.");
            Require(
                fixture.ConnectionCounts.ToolTip?.ToString()
                    ?.Contains(Strings.ConnectionsPartial, StringComparison.Ordinal) == true,
                "Header totals do not identify partial readings.");
            fixture.SetConnectionsForFixture(
                frame with { Connections = [], Ipv4 = new(false, 5), Ipv6 = new(false, 5) });
            Require(view.DisplayedRows.Count == 0 && !view.ShowProcess.IsEnabled && !view.CopyDetails.IsEnabled,
                "Unavailable readings retained rows or actions.");
            fixture.SetConnectionsForFixture(frame with
            {
                Connections = endpoints.Select(c => c with { ProcessCreationTicks = created + 1 }).ToArray()
            });
            view.SelectConnectionForFixture(process.ProcessId, created + 1);
            Require(view.SelectedProcessForFixture is null && !view.ShowProcess.IsEnabled,
                "A reused PID inherited process navigation.");
            fixture.SetConnectionsForFixture(frame);
            view.Rows.SelectedItem =
                view.DisplayedRows.First(row => row.Connections.Any(c => c.ProcessId == process.ProcessId));
            view.ShowProcess.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Require(fixture.ProcessesTab.IsChecked == true && fixture.Processes.GroupedChip.IsChecked == true,
                "Executable navigation did not open Grouped Processes.");
            fixture.ConnectionsTab.IsChecked = true;
            fixture.SetActivityPanelCollapsed(true);
            fixture.SetConnectionsForFixture(frame with { Connections = endpoints.Take(3).ToArray() });
            Require(fixture.ConnectionTcpValue.Text == "2" && fixture.ConnectionUdpValue.Text == "1",
                "Collapsed activity header did not refresh its totals.");
            fixture.SetActivityPanelCollapsed(false);
            fixture.SetConnectionsForFixture(frame);
            view.SelectConnectionForFixture(process.ProcessId, created);
            view.ShowProcess.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Require(fixture.ProcessesTab.IsChecked == true && fixture.SelectedSample is null,
                "Show in Processes did not navigate to the live process.");
            fixture.ConnectionsTab.IsChecked = true;
            fixture.SetProcessNetworkForFixture(traffic);
            Require(fixture.ConnectionCounts.ToolTip?.ToString()
                        ?.Contains(Strings.ConnectionsRateTiming, StringComparison.Ordinal) == true &&
                    AutomationProperties.GetName(fixture.ConnectionCounts)
                        .Contains(Strings.ConnectionsRateTiming, StringComparison.Ordinal),
                "Connections totals must explain their averaging window and delay to pointer and screen-reader users.");
            view.Rows.SelectedItem =
                view.DisplayedRows.First(row => row.Connections.Any(c => c.ProcessId == process.ProcessId));
            Require(
                view.SelectedDetailsForFixture?.Contains(Strings.DetailReceivedSent, StringComparison.Ordinal) == true,
                "Observed network totals are missing from executable details.");
            Require(view.SelectionDetail.Text.Contains(Strings.DetailReceivedSent, StringComparison.Ordinal) &&
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

                Require(Math.Abs(view.ActionBar.ActualHeight - view.ActionBar.MinHeight) < 1 &&
                        view.ActionBar.MinHeight == fixture.Processes.ProcessActions.MinHeight &&
                        view.ActionBar.MinHeight == fixture.ServiceView.ActionBar.MinHeight,
                    "Connections actions must use the same compact height as Processes and Services.");
            }

            fixture.Width = 836;
            await CaptureServiceWindowAsync(fixture, Path.Combine(output, "connections-network-narrow.png"),
                cancellationToken);
            Require(Math.Abs(view.ActionBar.ActualHeight - view.ActionBar.MinHeight) < 1,
                "The narrow Connections action bar must keep its two-line height.");
            CheckToolbarLayout();
            var scroller = FindScroller(view.Rows) ??
                           throw new InvalidOperationException("Connections scroller was not realized.");
            scroller.ScrollToHorizontalOffset(scroller.ScrollableWidth);
            await CaptureServiceWindowAsync(fixture, Path.Combine(output, "connections-network-narrow-scrolled.png"),
                cancellationToken);
            Require(
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
            Require(view.Notice.Text.Contains(Strings.NetworkProcessMonitoringUnavailable, StringComparison.Ordinal),
                "Failed network collection must remain distinct from readable endpoints.");
            Require(view.Notice.Visibility == Visibility.Collapsed &&
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

            var pinButton = FindPinButton(view.Rows) ??
                            throw new InvalidOperationException("Pin button was not realized.");
            pinButton.Focus();
            pinButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Require(view.DisplayedRows.First().Pinned && fixture.Preferences.PinnedConnections.Length == 1,
                "Connection pin did not reach the preferences or pinned-first list.");
            Require(view.DisplayedRows.Count(row => row.RankedLabel.Length > 0) == 1 &&
                    view.DisplayedRows.First(row => !row.Pinned).RankedLabel ==
                    Strings.Format(Strings.RankedByMetric, ((ComboBoxItem)view.Rank.SelectedItem).Content),
                "Ranked connections must have one separator after the pinned rows.");
            await CaptureServiceWindowAsync(fixture, Path.Combine(output, "connections-pinned-ranked.png"),
                cancellationToken);
            view.PinnedOnly.IsChecked = true;
            Require(view.DisplayedRows.Count == 1, "Pinned only must hide unpinned executable rows.");
            Require(view.DisplayedRows.All(row => row.RankedLabel.Length == 0),
                "Pinned only must hide the ranking separator.");
            await CaptureServiceWindowAsync(fixture, Path.Combine(output, "connections-pinned.png"), cancellationToken);
            CheckToolbarLayout();
            Require(FindScroller(view.Rows)?.ScrollableWidth <= 1,
                "A focused pin must not widen its row and introduce horizontal overflow.");
            fixture.SetConnectionsForFixture(frame with { Connections = [] });
            Require(view.DisplayedRows.Count == 1 && view.DisplayedRows[0].Connections.Count == 0 &&
                    view.DisplayedRows[0].Detail == Strings.ConnectionsNoObservedEndpoints,
                "A disconnected pin must remain without stale endpoints.");
            await CaptureServiceWindowAsync(fixture, Path.Combine(output, "connections-pinned-disconnected.png"),
                cancellationToken);
            fixture.SetConnectionsForFixture(frame);
            view.PinnedOnly.IsChecked = false;
            var previousCulture = Strings.Culture;
            try
            {
                Strings.SetCulture(CultureInfo.GetCultureInfo("de"));
                fixture.Width = 836;
                await CaptureServiceWindowAsync(fixture, Path.Combine(output, "connections-german-narrow.png"),
                    cancellationToken);
                CheckToolbarLayout();
            }
            finally
            {
                Strings.SetCulture(previousCulture);
            }

            await File.WriteAllTextAsync(Path.Combine(output, "connections.json"), JsonSerializer.Serialize(new
            {
                Elevated = elevated, NativeEndpoints = native.Connections.Count,
                NativeAvailable = native.Available, NativePartial = native.PartiallyAvailable,
                CaptureMilliseconds = times, FixtureChecks = "passed", IllustrativeData = true
            }, new JsonSerializerOptions { WriteIndented = true }), cancellationToken);
        }
        finally
        {
            fixture.CloseForFixture();
        }

        static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        void CheckToolbarLayout()
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
            Require(bounds.All(rect => rect.Width > 0 && rect.Left >= 0 && rect.Right <= view.ActualWidth &&
                                       Math.Abs(rect.Top + rect.Height / 2 - center) < 1),
                "Connections controls must fit on a single row at minimum width.");
            Require(!fixture.ConnectionVolumeValue.Text.Contains('\n'), "Activity totals must stay on one line.");
            Require(
                bounds[3].Right <= bounds[4].Left && bounds[4].Right <= bounds[5].Left && view.AppsOnly.FontSize == 12,
                "App only and Pinned only must precede the period selector and match the Processes filter style.");
            if (view.PinSummary.Visibility == Visibility.Visible)
            {
                var pins = view.PinSummary.TransformToAncestor(view.ConnectionHeaders)
                    .TransformBounds(new Rect(view.PinSummary.RenderSize));
                Require(pins.Left > 0 && pins.Right <= view.ConnectionHeaders.ColumnDefinitions[0].ActualWidth &&
                        pins.Top >= 0 && pins.Bottom <= view.ConnectionHeaders.ActualHeight,
                    "Pinned count must fit beside Process / PID in the first column header.");
            }
        }

        static ScrollViewer? FindScroller(DependencyObject parent)
        {
            if (parent is ScrollViewer scroller) return scroller;
            for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
                if (FindScroller(VisualTreeHelper.GetChild(parent, index)) is { } child)
                    return child;
            return null;
        }

        Button? FindPinButton(DependencyObject parent)
        {
            if (parent is Button { DataContext: ConnectionDisplayRow { Process: { } owner } } button &&
                owner.Identity == process.Identity) return button;
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
                if (FindPinButton(VisualTreeHelper.GetChild(parent, i)) is { } found)
                    return found;
            return null;
        }
    }
}
