using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using NUnit.Framework;
using Spectre.Console;
using WinMon.Monitoring;

namespace WinMon.Cli.Tests;

[TestFixture]
public sealed class ConnectionsTabTests
{
    [Test]
    public void RenderedDetailsKeepEndpointFieldsSeparated()
    {
        var owner = Owner(42, 1, "Viewer", @"C:\apps\viewer.exe");
        var frame = Frame(Connection(42, 1, TransportProtocol.Tcp, 443));
        var table = ConnectionsTab.Build(frame, [owner], null, [], null,
            new ConnectionQueryOptions(ConnectionSortMetric.Tcp, NetworkEnabled: false));
        var state = new InteractiveState(ProcessSort.Cpu, "");
        state.Present(table, 5);
        state.Handle(new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false), 5);
        using var output = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        { Enrichment = new() { UseDefaultEnrichers = false }, Out = new AnsiConsoleOutput(output), Ansi = AnsiSupport.No });
        console.Profile.Width = 120;
        console.Write(InteractiveMonitor.BuildView(state, 0, 5, 120, 24));
        Assert.That(output.ToString(), Does.Contain("PID  Protocol  Local endpoint  Remote endpoint  State")
            .And.Contain("42  TCP  203.0.113.24:443  —  Established"));
    }

    [Test]
    public void BuildGroupsExecutableAndKeepsEndpointDetailsAndPeriodTraffic()
    {
        var first = Owner(42, 1, "Viewer", @"C:\apps\viewer.exe");
        var second = Owner(43, 2, "Viewer", @"C:\apps\viewer.exe");
        var frame = Frame(
            Connection(42, 1, TransportProtocol.Tcp, 443),
            Connection(43, 2, TransportProtocol.Udp, 5353));
        var start = Capture(0, frame, [first, second],
            Network(Reading(42, 1, 100, 200), Reading(43, 2, 10, 20)));
        var endNetwork = Network(Reading(42, 1, 400, 700), Reading(43, 2, 60, 120));
        var end = Capture(5, frame, [first, second], endNetwork);

        var table = ConnectionsTab.Build(frame, [first, second], endNetwork, [start, end], end,
            new ConnectionQueryOptions(ConnectionSortMetric.Received, NetworkEnabled: true, TrafficSeconds: 5));

        Assert.Multiple(() =>
        {
            Assert.That(table.Columns, Has.Length.EqualTo(7));
            Assert.That(table.Rows, Has.Count.EqualTo(1));
            Assert.That(table.Rows[0].Key, Is.EqualTo(ConnectionQuery.OwnerKey(first)));
            Assert.That(table.Rows[0].Cells[0], Does.StartWith("Viewer (42, 43)"));
            Assert.That(table.Rows[0].Cells[1], Is.EqualTo("1"));
            Assert.That(table.Rows[0].Cells[2], Is.EqualTo("1"));
            Assert.That(table.Rows[0].Cells[5], Does.Contain("350 B"));
            Assert.That(table.Rows[0].Details, Does.Contain("203.0.113.24:443").And.Contain("[::]:5353"));
        });
    }

    [Test]
    public void BuildFallsBackToTcpOrderingWhenTrafficCollectionIsDisabled()
    {
        var first = Owner(42, 1, "one", "");
        var second = Owner(43, 2, "two", "");
        var frame = Frame(Connection(42, 1, TransportProtocol.Tcp, 80),
            Connection(43, 2, TransportProtocol.Tcp, 81), Connection(43, 2, TransportProtocol.Tcp, 82));

        var table = ConnectionsTab.Build(frame, [first, second], null, [], null,
            new ConnectionQueryOptions(ConnectionSortMetric.Receive, NetworkEnabled: false));

        Assert.Multiple(() =>
        {
            Assert.That(table.Columns, Has.Length.EqualTo(3));
            Assert.That(table.Rows.Select(row => row.Cells[0]), Is.EqualTo(["two (43)", "one (42)"]));
            Assert.That(table.Note, Is.Not.Empty);
        });
    }

    [Test]
    public void BuildAppliesSearchAndAppsOnlyToGroupedRows()
    {
        var app = Owner(42, 1, "viewer", "") with { HasAppWindow = true };
        var background = Owner(43, 2, "worker", "");
        var frame = Frame(Connection(42, 1, TransportProtocol.Tcp, 443), Connection(43, 2, TransportProtocol.Tcp, 80));

        var table = ConnectionsTab.Build(frame, [app, background], null, [], null,
            new ConnectionQueryOptions(ConnectionSortMetric.Tcp, AppsOnly: true, Search: "203.0.113.24"));

        Assert.That(table.Rows.Select(row => row.Cells[0]), Is.EqualTo(["viewer (42)"]));
    }

    [Test]
    public void BuildUsesDesktopUnknownProcessNameForTiedTrafficRows()
    {
        var owner = Owner(42, 1, "alpha", "");
        var frame = Frame(Connection(0, 0, TransportProtocol.Tcp, 80), Connection(42, 1, TransportProtocol.Tcp, 81));

        var table = ConnectionsTab.Build(frame, [owner], null, [], null,
            new ConnectionQueryOptions(ConnectionSortMetric.Receive, NetworkEnabled: true));

        Assert.That(table.Rows.Select(row => row.Cells[0]), Is.EqualTo(["alpha (42)", "Unknown process (0)"]));
    }

    private static ProcessUsage Owner(int processId, long creationTicks, string name, string imagePath) =>
        new(processId, creationTicks, name, 0) { ImagePath = imagePath };

    private static NetworkConnection Connection(int processId, long creationTicks, TransportProtocol protocol, int port) =>
        new(processId, creationTicks, protocol, new IPEndPoint(IPAddress.Parse("203.0.113.24"), port),
            protocol == TransportProtocol.Udp ? new IPEndPoint(IPAddress.IPv6Any, 5353) : null,
            protocol == TransportProtocol.Tcp ? TcpState.Established : null);

    private static NetworkConnectionsFrame Frame(params NetworkConnection[] connections) =>
        new(connections, NetworkAddressFamilyStatus.Success, NetworkAddressFamilyStatus.Success);

    private static ConnectionCapture Capture(int second, NetworkConnectionsFrame frame, IReadOnlyList<ProcessUsage> owners,
        ProcessNetworkFrame network) => new(
        new(DateTimeOffset.UnixEpoch.AddSeconds(second), 1, owners, 0, false, second * Stopwatch.Frequency),
        frame, owners, network);

    private static ProcessNetworkFrame Network(params ProcessNetworkReading[] readings) => new(5, 1,
        ProcessNetworkQuality.None, readings)
    {
        Owners = [],
        CurrentLifetimes = readings.Select(reading => new ProcessLifetimeIdentity(reading.ProcessId,
            reading.CreationTicks, ProcessLifetimeIdentity.WindowsFileTime)).ToHashSet()
    };

    private static ProcessNetworkReading Reading(int processId, long created, long received, long sent) =>
        new(processId, created, received, sent, 10, 20);
}
