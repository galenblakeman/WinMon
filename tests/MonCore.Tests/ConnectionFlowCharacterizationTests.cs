using System.Net;
using MonCore.Monitoring;
using NUnit.Framework;

namespace MonCore.Tests;

[TestFixture]
public sealed class ConnectionFlowCharacterizationTests
{
    private static ProcessUsage Owner(int id, string name, string path = @"C:\apps\worker.exe") =>
        new(id, 100 + id, name, 0) { ImagePath = path };

    private static ProcessNetworkFlowReading Flow(ProcessUsage owner, string local = "127.0.0.1:4000") =>
        new(owner.ProcessId, owner.CreationTicks, TransportProtocol.Udp, local, "10.0.0.1:5000", 999, 888, 77, 66);

    private static ProcessNetworkFrame Network(params ProcessNetworkFlowReading[] flows) =>
        new(1, 1, ProcessNetworkQuality.None, []) { FlowTrafficSupported = true, Flows = flows };

    private static IReadOnlyList<ConnectionQueryRow> Rows(ProcessNetworkFrame? network,
        IReadOnlyList<ProcessUsage>? processes = null, ConnectionQueryOptions? options = null,
        NetworkConnectionsFrame? endpoints = null) =>
        ConnectionQuery.Build(endpoints, processes ?? [], network, [], null,
            options ?? new ConnectionQueryOptions(NetworkEnabled: true, TrafficSeconds: 0));

    [Test]
    public void UnsupportedAndEmptyFlowsDoNotReadNullOwnersOrUnsupportedFlowList()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Rows(null), Is.Empty);
            Assert.That(Rows(Network() with { Owners = null! }), Is.Empty);
            Assert.That(Rows(Network() with { FlowTrafficSupported = false, Flows = null!, Owners = null! }), Is.Empty);
            Assert.That(Rows(Network(Flow(Owner(20, "worker"))) with { FlowTrafficSupported = false }), Is.Empty);
        });
    }

    [Test]
    public void ObservedOwnersChooseRepresentativeButAllExecutableSiblingsRemainMembers()
    {
        var zeta = Owner(20, "Zeta");
        var alpha = Owner(10, "Alpha");
        var beta = Owner(30, "Beta");
        var zetaFlow = Flow(zeta);
        var betaFirst = Flow(beta, "127.0.0.1:4001");
        var betaLast = Flow(beta, "127.0.0.1:4002");
        var network = Network(betaFirst, zetaFlow, betaLast) with
        {
            Owners = [zeta, alpha, beta],
            FlowQuality = ProcessNetworkQuality.Unavailable,
            Processes = [new(20, 120, 10, 20, 1, 2), new(30, 130, 30, 40, 3, 4)]
        };

        var row = Rows(network).Single();

        Assert.Multiple(() =>
        {
            Assert.That(row.Process, Is.EqualTo(beta));
            Assert.That(row.Members, Is.EqualTo(new[] { zeta, alpha, beta }));
            Assert.That(row.Flows, Is.EqualTo(new[] { zetaFlow, betaFirst, betaLast }));
            Assert.That(row.Connections, Is.Empty);
            Assert.That(row.TcpCount, Is.Zero);
            Assert.That(row.UdpCount, Is.Zero);
            Assert.That(row.Network, Is.EqualTo(new ConnectionNetworkSummary(40, 60, 4, 6,
                ProcessNetworkQuality.Incomplete, true, true)));
        });
    }

    [Test]
    public void FlowMergeKeepsEndpointRepresentativeAndPolledEndpointCounts()
    {
        var endpointOwner = Owner(20, "Zeta");
        var flowOwner = Owner(30, "Alpha");
        var flow = Flow(flowOwner);
        var connection = new NetworkConnection(20, 120, TransportProtocol.Tcp,
            new IPEndPoint(IPAddress.Loopback, 1234));
        var endpoints = new NetworkConnectionsFrame([connection], NetworkAddressFamilyStatus.Success,
            NetworkAddressFamilyStatus.Success);

        var row = Rows(Network(flow), [endpointOwner, flowOwner], endpoints: endpoints).Single();

        Assert.Multiple(() =>
        {
            Assert.That(row.Process, Is.EqualTo(endpointOwner));
            Assert.That(row.Members, Is.EqualTo(new[] { endpointOwner, flowOwner }));
            Assert.That(row.Connections, Is.EqualTo(new[] { connection }));
            Assert.That(row.Flows, Is.EqualTo(new[] { flow }));
            Assert.That(row.TcpCount, Is.EqualTo(1));
            Assert.That(row.UdpCount, Is.Zero);
        });
    }

    [Test]
    public void CachedExecutableMetadataSurvivesCurrentPathLossInExecutableGroups()
    {
        var cached = Owner(20, "Cached");
        var current = cached with { Name = "Current", ImagePath = "" };
        var row = Rows(Network(Flow(cached)) with { Owners = [cached] }, [current]).Single();

        Assert.Multiple(() =>
        {
            Assert.That(row.Process, Is.EqualTo(cached));
            Assert.That(row.Members, Is.EqualTo(new[] { cached }));
            Assert.That(row.Key, Does.StartWith("image:"));
        });
    }

    [Test]
    public void CurrentPathlessOwnerReplacesCachedPathlessOwnerInResidualGroups()
    {
        var cached = Owner(20, "Cached", "");
        var current = cached with { Name = "Current" };
        var row = Rows(Network(Flow(cached)) with { Owners = [cached] }, [current]).Single();

        Assert.Multiple(() =>
        {
            Assert.That(row.Process, Is.EqualTo(current));
            Assert.That(row.Members, Is.EqualTo(new[] { current }));
            Assert.That(row.Key, Is.EqualTo("lifetime:20:120"));
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ReusedPidOrForeignLifetimeCannotSupplyFlowMetadata(bool foreignDomain)
    {
        var wireOwner = Owner(20, "Wire");
        var current = foreignDomain
            ? wireOwner with { LifetimeDomain = ProcessLifetimeDomain.UnixMicroseconds }
            : wireOwner with { CreationTicks = 121 };
        var flow = Flow(wireOwner);
        var row = Rows(Network(flow), [current]).Single();

        Assert.Multiple(() =>
        {
            Assert.That(row.Key, Is.EqualTo("lifetime:20:120"));
            Assert.That(row.Process, Is.Null);
            Assert.That(row.Members, Is.Empty);
            Assert.That(row.Flows, Is.EqualTo(new[] { flow }));
        });
    }

    [Test]
    public void FriendlyNameThenPidBreakRepresentativeTies()
    {
        var first = Owner(30, "Alpha") with { AppName = "Beta" };
        var second = Owner(20, "Zeta") with { AppName = "Beta" };
        var row = Rows(Network(Flow(first), Flow(second)) with { Owners = [first, second] }).Single();

        Assert.That(row.Process, Is.EqualTo(second));
    }

    [TestCase("127.0.0.1:4000")]
    [TestCase("10.0.0.1:5000")]
    [TestCase("worker")]
    [TestCase("20")]
    public void FlowAndOwnerSearchStillWorkWhenNetworkColumnsAreDisabled(string search)
    {
        var owner = Owner(20, "worker");
        var flow = Flow(owner);
        var row = Rows(Network(flow), [owner], new ConnectionQueryOptions(Search: search)).Single();

        Assert.Multiple(() =>
        {
            Assert.That(row.Process, Is.EqualTo(owner));
            Assert.That(row.Flows, Is.EqualTo(new[] { flow }));
            Assert.That(row.Network, Is.SameAs(ConnectionNetworkSummary.Disabled));
        });
    }

    [TestCase(false, false, 0)]
    [TestCase(false, true, 1)]
    [TestCase(true, false, 1)]
    public void AppsOnlyRecognizesCurrentAndHistoricalWindows(bool current, bool historical, int count)
    {
        var owner = Owner(20, "worker") with { HasAppWindow = current, HasHadAppWindow = historical };
        var rows = Rows(Network(Flow(owner)), [owner], new ConnectionQueryOptions(AppsOnly: true));

        Assert.That(rows.Select(row => row.Process), Is.EqualTo(count == 0 ? Array.Empty<ProcessUsage>() : [owner]));
    }

    [Test]
    public void NullOwnerListFailsBeforeMalformedReadingsEvenWhenEveryFlowIsRepresented()
    {
        var owner = Owner(20, "worker");
        var network = Network(Flow(owner)) with { Owners = null!, Processes = [null!] };

        Assert.Throws<ArgumentNullException>(() => Rows(network, [owner]));
    }

    [Test]
    public void SupportedNullFlowListFailsBeforeOwnerIndexing()
    {
        var network = Network() with { Flows = null!, Owners = null! };

        Assert.Throws<NullReferenceException>(() => Rows(network));
    }

    [Test]
    public void RepresentativeUsesDefaultUnknownNameBeforeCustomDisplaySorting()
    {
        var unnamed = Owner(30, "");
        var named = Owner(20, "Zeta");
        var row = Rows(Network(Flow(unnamed), Flow(named)), [named, unnamed],
            new ConnectionQueryOptions(UnknownProcessName: "ZZZ")).Single();

        Assert.That(row.Process, Is.EqualTo(unnamed));
    }

    [Test]
    public void CustomUnknownLabelDoesNotMakeUnknownFlowOwnersSearchableByName()
    {
        var rows = Rows(Network(Flow(Owner(20, "worker"))), options:
            new ConnectionQueryOptions(Search: "Custom unknown", UnknownProcessName: "Custom unknown"));

        Assert.That(rows, Is.Empty);
    }
}
