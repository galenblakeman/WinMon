using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Principal;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WinMon.Controls;
using WinMon.Monitoring;

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
                native = await Task.Run(WindowsConnections.Capture, cancellationToken);
                times.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            }
            native = await Task.Run(WindowsConnections.Capture, cancellationToken);
            var tcpPort = ((IPEndPoint)listener.LocalEndpoint).Port;
            var clientPort = ((IPEndPoint)client.Client.LocalEndPoint!).Port;
            var udpPort = ((IPEndPoint)udp.Client.LocalEndPoint!).Port;
            await File.WriteAllTextAsync(Path.Combine(output, "native-connections.json"), JsonSerializer.Serialize(new
            {
                Elevated = elevated, ProcessId = Environment.ProcessId, TcpPort = tcpPort, UdpPort = udpPort,
                native.Ipv4, native.Ipv6,
                OwnEndpoints = native.Connections.Where(c => c.ProcessId == Environment.ProcessId).Select(c => new
                {
                    c.ProcessId, c.ProcessCreationTicks, Protocol = c.Protocol.ToString(),
                    Local = c.LocalEndpoint.ToString(), Remote = c.RemoteEndpoint?.ToString(), State = c.State?.ToString()
                }).ToArray()
            }, new JsonSerializerOptions { WriteIndented = true }), cancellationToken);
            Require(native.Connections.Any(c => c.ProcessId == Environment.ProcessId &&
                c.Protocol == TransportProtocol.Tcp && c.LocalEndpoint.Port == tcpPort &&
                c.RemoteEndpoint is null), "Native TCP listener ownership was not observed.");
            Require(native.Connections.Any(c => c.ProcessId == Environment.ProcessId &&
                c.Protocol == TransportProtocol.Tcp &&
                ((c.LocalEndpoint.Port == tcpPort && c.RemoteEndpoint?.Port == clientPort) ||
                 (c.LocalEndpoint.Port == clientPort && c.RemoteEndpoint?.Port == tcpPort))),
                "Native TCP peer endpoints were not observed.");
            Require(native.Connections.Any(c => c.ProcessId == Environment.ProcessId &&
                c.Protocol == TransportProtocol.Udp && c.LocalEndpoint.Port == udpPort && c.RemoteEndpoint is null),
                "Native UDP endpoint ownership was not observed.");
        }
        finally { listener.Stop(); }

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
                TotalMemoryBytes = 32UL * 1073741824, UsedMemoryBytes = 16UL * 1073741824,
                DiskName = "C:", DiskActivePercent = 8, ReceiveBytesPerSecond = 2_000_000,
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
            new(sibling.ProcessId, sibling.CreationTicks, TransportProtocol.Tcp, new(IPAddress.Parse("192.168.1.25"), 52146),
                new(IPAddress.Parse("198.51.100.12"), 443), TcpState.Established),
            new(sibling.ProcessId, sibling.CreationTicks, TransportProtocol.Udp, new(IPAddress.Any, 5354)),
            new(4, null, TransportProtocol.Tcp, new(IPAddress.Any, 445), null, TcpState.Listen)
        };
        var frame = new NetworkConnectionsFrame(endpoints, NetworkAddressFamilyStatus.Success,
            NetworkAddressFamilyStatus.Success);
        var traffic = new ProcessNetworkFrame(Stopwatch.GetTimestamp(), Stopwatch.Frequency,
            ProcessNetworkQuality.Provisional,
            [new ProcessNetworkReading(process.ProcessId, created, 18_000_000, 2_500_000, 2_000_000, 250_000),
                new ProcessNetworkReading(sibling.ProcessId, sibling.CreationTicks, 4_000_000, 750_000, 500_000, 100_000)]);
        fixture.Session.Record(sample, sample, connections: frame, network: traffic);
        try
        {
            fixture.Show();
            fixture.ConnectionsTab.IsChecked = true;
            fixture.SetConnectionsForFixture(frame);
            var view = fixture.ConnectionView;
            Require(view.Rank.SelectedIndex == (int)ConnectionSort.Tcp &&
                !((ComboBoxItem)view.Rank.Items[0]).IsEnabled && !((ComboBoxItem)view.Rank.Items[1]).IsEnabled,
                "Disabled monitoring must use TCP ranking and disable throughput choices.");
            Require(view.TrafficPrompt.Visibility == (NetworkSetupBroker.Supported ? Visibility.Visible : Visibility.Collapsed),
                "The enable link must follow desktop service support.");
            view.Rank.IsDropDownOpen = true;
            await CaptureServiceWindowAsync(fixture, Path.Combine(output, "connections-disabled-ranking.png"), cancellationToken);
            view.Rank.IsDropDownOpen = false;
            view.SelectConnectionForFixture(process.ProcessId, created);
            Require(view.DisplayedRows.Count == 7, "Executable expansion did not show endpoint rows.");
            Require(fixture.ConnectionCounts.Visibility == Visibility.Visible && fixture.SystemCounts.Visibility == Visibility.Collapsed &&
                fixture.ConnectionTcpValue.Text == "4" && fixture.ConnectionUdpValue.Text == "2", "Connections totals are not in the activity header.");
            var selected = view.Rows.SelectedItem;
            fixture.SetConnectionsForFixture(frame);
            Require(ReferenceEquals(selected, view.Rows.SelectedItem), "Refresh lost endpoint selection.");
            Require(view.SelectedDetailsForFixture?.Contains("203.0.113.24:443", StringComparison.Ordinal) == true,
                "Copied endpoint details omit the remote endpoint.");
            Require(view.SelectedProcessForFixture?.Identity == process.Identity, "Endpoint owner lifetime did not match.");
            string? copied = null;
            view.CopyRequested += text => copied = text;
            view.CopyDetails.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Require(copied?.Contains("203.0.113.24:443", StringComparison.Ordinal) == true, "Copy action failed.");

            foreach (var theme in new[] { "Dark", "Light" })
            {
                fixture.Preferences = fixture.Preferences with { Theme = theme };
                fixture.ApplyTheme();
                fixture.RefreshVisiblePresentation();
                await CaptureServiceWindowAsync(fixture, Path.Combine(output, $"connections-{theme.ToLowerInvariant()}.png"), cancellationToken);
            }
            fixture.Width = 836;
            await CaptureServiceWindowAsync(fixture, Path.Combine(output, "connections-narrow.png"), cancellationToken);
            view.Grouped = false;
            Require(view.DisplayedRows.Count == 6, "Individual mode must show one row per endpoint.");
            await CaptureServiceWindowAsync(fixture, Path.Combine(output, "connections-individual-narrow.png"), cancellationToken);
            fixture.Width = 1200;
            await CaptureServiceWindowAsync(fixture, Path.Combine(output, "connections-individual.png"), cancellationToken);
            view.Grouped = true;
            view.SelectConnectionForFixture(process.ProcessId, created);
            fixture.SelectedSample = sample;
            fixture.RefreshVisiblePresentation();
            Require(view.Notice.Text.Contains(Strings.ConnectionsHistoryNote, StringComparison.Ordinal) && view.DisplayedRows.Count == 7,
                "Capture did not show its recorded endpoint observation.");
            Require(view.SelectedDetailsForFixture?.Contains("203.0.113.24:443", StringComparison.Ordinal) == true &&
                view.SelectedDetailsForFixture.Contains(Strings.DetailReceivedSent, StringComparison.Ordinal),
                "Capture endpoint details or recorded traffic are missing.");
            await CaptureServiceWindowAsync(fixture, Path.Combine(output, "connections-capture.png"), cancellationToken);
            view.ShowProcess.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Require(ReferenceEquals(fixture.SelectedSample, sample) && fixture.ProcessesTab.IsChecked == true,
                "Historical process navigation must preserve the selected sample.");
            fixture.ConnectionsTab.IsChecked = true;
            var missingSample = sample with { Timestamp = sample.Timestamp.AddSeconds(-1) };
            fixture.SelectedSample = missingSample;
            fixture.RefreshVisiblePresentation();
            Require(view.DisplayedRows.Count == 0 && view.Notice.Text.Contains(Strings.ConnectionsHistoryUnavailable, StringComparison.Ordinal),
                "Missing endpoint history must not substitute live rows.");
            fixture.SelectedSample = sample;
            fixture.RefreshVisiblePresentation();
            view.Protocol.Focus();
            view.Protocol.IsDropDownOpen = true;
            fixture.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(fixture)!, 0, Key.Escape)
                { RoutedEvent = Keyboard.PreviewKeyDownEvent });
            Require(ReferenceEquals(fixture.SelectedSample, sample), "Escape from the protocol menu changed the historical chart.");
            view.Protocol.IsDropDownOpen = false;
            fixture.SelectedSample = null;
            fixture.RefreshVisiblePresentation();
            view.Search.Text = "no-such-process";
            Require(view.DisplayedRows.Count == 0, "Name filter did not hide nonmatching processes.");
            view.Search.Text = process.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture);
            Require(view.DisplayedRows.Count == 6, "PID search lost matching executable group endpoints.");
            view.Protocol.SelectedIndex = 2;
            Require(view.DisplayedRows.Count == 3, "UDP filter did not leave one executable group and its two endpoints.");
            Require(fixture.ConnectionTcpValue.Text == "4" && fixture.ConnectionUdpValue.Text == "2", "Filters changed system-wide header totals.");
            view.Protocol.SelectedIndex = 0;
            view.Search.Clear();
            view.Rank.SelectedIndex = (int)ConnectionSort.Tcp;
            Require(view.DisplayedRows.First().IsGroup && view.DisplayedRows.First().Connections.Count(c => c.Protocol == TransportProtocol.Tcp) == 3,
                "TCP ranking did not put the most connected app first.");
            view.AppsOnly.IsChecked = true;
            Require(view.DisplayedRows.All(row => row.Process?.HasAppWindow == true || row.IsGroup && row.Members.Any(m => m.HasAppWindow == true)),
                "App only retained a background connection group.");
            view.AppsOnly.IsChecked = false;
            fixture.SetConnectionsForFixture(frame with { Ipv6 = new(false, 5) });
            Require(view.Notice.Text.Contains(Strings.ConnectionsPartial, StringComparison.Ordinal), "Partial collection lacks a notice.");
            Require(fixture.ConnectionCounts.ToolTip?.ToString()?.Contains(Strings.ConnectionsPartial, StringComparison.Ordinal) == true,
                "Header totals do not identify partial readings.");
            fixture.SetConnectionsForFixture(frame with { Connections = [], Ipv4 = new(false, 5), Ipv6 = new(false, 5) });
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
            view.Rows.SelectedItem = view.DisplayedRows.First(row => row.IsGroup && row.Connections.Any(c => c.ProcessId == process.ProcessId));
            view.ShowProcess.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
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
            view.ShowProcess.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Require(fixture.ProcessesTab.IsChecked == true && fixture.SelectedSample is null,
                "Show in Processes did not navigate to the live process.");
            fixture.ConnectionsTab.IsChecked = true;
            fixture.SetProcessNetworkForFixture(traffic);
            view.Grouped = true;
            view.Rows.SelectedItem = view.DisplayedRows.First(row => row.IsGroup && row.Connections.Any(c => c.ProcessId == process.ProcessId));
            Require(view.SelectedDetailsForFixture?.Contains(Strings.DetailReceivedSent, StringComparison.Ordinal) == true,
                "Observed network totals are missing from executable details.");
            Require(view.SelectionNetwork.Visibility == Visibility.Visible &&
                view.SelectionNetwork.Text.Contains(Strings.DetailReceivedSent, StringComparison.Ordinal),
                "Selected executable traffic must be visible, not only copied.");
            foreach (var theme in new[] { "Dark", "Light" })
            {
                fixture.Width = 1200;
                fixture.Preferences = fixture.Preferences with { Theme = theme };
                fixture.ApplyTheme();
                fixture.RefreshVisiblePresentation();
                await CaptureServiceWindowAsync(fixture, Path.Combine(output, $"connections-network-{theme.ToLowerInvariant()}.png"), cancellationToken);
            }
            fixture.Width = 836;
            await CaptureServiceWindowAsync(fixture, Path.Combine(output, "connections-network-narrow.png"), cancellationToken);
            var scroller = FindScroller(view.Rows) ?? throw new InvalidOperationException("Connections scroller was not realized.");
            scroller.ScrollToHorizontalOffset(scroller.ScrollableWidth);
            await CaptureServiceWindowAsync(fixture, Path.Combine(output, "connections-network-narrow-scrolled.png"), cancellationToken);
            Require(Math.Abs(view.HeaderScroller.HorizontalOffset - Math.Min(scroller.HorizontalOffset, view.HeaderScroller.ScrollableWidth)) < 1,
                $"Network header must follow horizontal scrolling: rows {scroller.HorizontalOffset}/{scroller.ExtentWidth}/{scroller.ViewportWidth}, header {view.HeaderScroller.HorizontalOffset}/{view.HeaderScroller.ExtentWidth}/{view.HeaderScroller.ViewportWidth}.");
            scroller.ScrollToHorizontalOffset(0);
            view.Grouped = false;
            view.SelectConnectionForFixture(process.ProcessId, created);
            Require(view.DisplayedRows.All(row => row.Network.Length == 0), "Endpoint rows must not repeat owner rates.");
            Require(view.SelectionNetwork.Visibility == Visibility.Visible &&
                view.SelectionNetwork.Text.Contains(Strings.ConnectionsNetworkOwnerTotals, StringComparison.Ordinal),
                "Individual mode must display the selected owner's traffic summary.");
            await CaptureServiceWindowAsync(fixture, Path.Combine(output, "connections-network-individual-narrow.png"), cancellationToken);
            fixture.Width = 1200;
            fixture.SetProcessNetworkForFixture(traffic with { Quality = ProcessNetworkQuality.Unavailable | ProcessNetworkQuality.Incomplete });
            await CaptureServiceWindowAsync(fixture, Path.Combine(output, "connections-network-unavailable.png"), cancellationToken);
            Require(view.Notice.Text.Contains(Strings.NetworkProcessMonitoringUnavailable, StringComparison.Ordinal),
                "Failed network collection must remain distinct from readable endpoints.");
            fixture.SetProcessNetworkForFixture(null);
            view.Grouped = true;
            fixture.SetProcessNetworkForFixture(traffic);
            view.SelectConnectionForFixture(process.ProcessId, created);
            var details = view.SelectedDetailsForFixture ?? throw new InvalidOperationException("Properties fixture has no selection.");
            var properties = new ServicePropertiesWindow("WinMon", details, _ => { }) { Owner = fixture };
            try
            {
                properties.Show();
                await CaptureServiceWindowAsync(properties, Path.Combine(output, "connections-properties.png"), cancellationToken);
            }
            finally { properties.Close(); }
            var previousCulture = Strings.Culture;
            try
            {
                Strings.SetCulture(System.Globalization.CultureInfo.GetCultureInfo("de"));
                fixture.Width = 836;
                await CaptureServiceWindowAsync(fixture, Path.Combine(output, "connections-german-narrow.png"), cancellationToken);
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
        finally { fixture.CloseForFixture(); }

        static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        static ScrollViewer? FindScroller(DependencyObject parent)
        {
            if (parent is ScrollViewer scroller) return scroller;
            for (var index = 0; index < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); index++)
                if (FindScroller(System.Windows.Media.VisualTreeHelper.GetChild(parent, index)) is { } child) return child;
            return null;
        }
    }
}
