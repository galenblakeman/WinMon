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
            { AppName = "WinMon", ImagePath = Environment.ProcessPath ?? "" };
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
        fixture.Session.Record(sample, sample);
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
        try
        {
            fixture.Show();
            fixture.ConnectionsTab.IsChecked = true;
            fixture.SetConnectionsForFixture(frame);
            var view = fixture.ConnectionView;
            view.SelectConnectionForFixture(process.ProcessId, created);
            Require(view.DisplayedRows.Count == 7, "Executable expansion did not show both processes' endpoints.");
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
            Require(view.Notice.Text.Contains(Strings.ConnectionsLiveOnly, StringComparison.Ordinal), "History must not imply endpoint history.");
            view.Protocol.Focus();
            view.Protocol.IsDropDownOpen = true;
            fixture.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(fixture)!, 0, Key.Escape)
                { RoutedEvent = Keyboard.PreviewKeyDownEvent });
            Require(ReferenceEquals(fixture.SelectedSample, sample), "Escape from the protocol menu changed the historical chart.");
            view.Protocol.IsDropDownOpen = false;
            view.Search.Text = "no-such-process";
            Require(view.DisplayedRows.Count == 0, "Name filter did not hide nonmatching processes.");
            view.Search.Text = process.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture);
            Require(view.DisplayedRows.Count == 6, "PID search lost matching executable group endpoints.");
            view.Protocol.SelectedIndex = 2;
            Require(view.DisplayedRows.Count == 3, "UDP filter did not leave one executable group and its two endpoints.");
            Require(fixture.ConnectionTcpValue.Text == "4" && fixture.ConnectionUdpValue.Text == "2", "Filters changed system-wide header totals.");
            view.Protocol.SelectedIndex = 0;
            view.Search.Clear();
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
    }
}
