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
        RequireConnectionsScenario(received[0] == 0x57 && received[1] == 0x4d, "Loopback TCP exchange failed.");
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
            RequireConnectionsScenario(native.Connections.Any(c => c.ProcessId == Environment.ProcessId &&
                                                c.Protocol == TransportProtocol.Tcp &&
                                                c.LocalEndpoint.Port == tcpPort &&
                                                c.RemoteEndpoint is null),
                "Native TCP listener ownership was not observed.");
            RequireConnectionsScenario(native.Connections.Any(c => c.ProcessId == Environment.ProcessId &&
                                                c.Protocol == TransportProtocol.Tcp &&
                                                ((c.LocalEndpoint.Port == tcpPort &&
                                                  c.RemoteEndpoint?.Port == clientPort) ||
                                                 (c.LocalEndpoint.Port == clientPort &&
                                                  c.RemoteEndpoint?.Port == tcpPort))),
                "Native TCP peer endpoints were not observed.");
            RequireConnectionsScenario(native.Connections.Any(c => c.ProcessId == Environment.ProcessId &&
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
        RequireConnectionsScenario(exampleTotals.Sum().Received > 50_000_000 && exampleTotals.Sum().Sent > 5_000_000,
            "The traffic example must accumulate nonzero received and sent history.");
        try
        {
            fixture.Show();
            fixture.ConnectionsTab.IsChecked = true;
            fixture.SetConnectionsForFixture(frame);
            var view = fixture.ConnectionView;
            await CaptureConnectionsSetupAsync(fixture, view, frame, process, created, output, cancellationToken);

            await CaptureConnectionsHistoryAsync(fixture, view, sample, process, created, output, cancellationToken);

            await CaptureConnectionsFiltersAsync(fixture, view, frame, endpoints, process, created, output, cancellationToken);

            await CaptureConnectionsTrafficAsync(fixture, view, traffic, process, created, output, cancellationToken);

            await CaptureConnectionsPinsAsync(fixture, view, frame, process, output, cancellationToken);

            var previousCulture = Strings.Culture;
            try
            {
                Strings.SetCulture(CultureInfo.GetCultureInfo("de"));
                fixture.Width = 836;
                await CaptureServiceWindowAsync(fixture, Path.Combine(output, "connections-german-narrow.png"),
                    cancellationToken);
                VerifyConnectionsScenarioToolbar(fixture);
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


    }
}
