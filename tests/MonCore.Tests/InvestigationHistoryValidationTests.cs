using System.Net;
using MonCore.Monitoring;
using NUnit.Framework;

namespace MonCore.Tests;

[TestFixture]
public sealed class InvestigationHistoryValidationTests
{
    private static readonly ProcessUsage Process = new(1, 1, "owner", 1);

    private static InvestigationHistory.Frame Frame()
    {
        var key = new SampleKey(1, DateTimeOffset.UnixEpoch);
        return new(key,
            [new("service", [new("service", "Service", 4, 1, 0)], Process, [Process])], true,
            new([new(1, 1, TransportProtocol.Udp, new(IPAddress.Loopback, 123))],
                NetworkAddressFamilyStatus.Success, NetworkAddressFamilyStatus.Success), [Process],
            new(1, 1, ProcessNetworkQuality.Provisional, [new(1, 1, 10, 20, 1, 2)])
            {
                Owners = [Process], Flows = [new(1, 1, TransportProtocol.Tcp, "local", "remote", 10, 20, 1, 2)]
            },
            new(key, 1, true, 1, 1, [new(new(Process.Identity, "owner", "Owner", "", "", "", false), 1, 1, null)]));
    }

    [Test]
    public void Valid_AcceptsCompleteEmptyAndUnavailableSections()
    {
        var frame = Frame();
        Assert.That(InvestigationHistory.Valid(frame), Is.True);
        Assert.That(InvestigationHistory.Valid(frame with
            { Services = [], Owners = [], Connections = frame.Connections! with { Connections = [] },
                Network = frame.Network! with { Processes = [], Owners = [], Flows = [] },
                Usage = frame.Usage! with { Observations = [] } }), Is.True);
        Assert.That(InvestigationHistory.Valid(frame with
            { Services = null, Owners = null, Connections = null, Network = null, Usage = null }), Is.True);
        Assert.That(InvestigationHistory.Valid(frame with
            { Services = [frame.Services![0] with { Host = null }] }), Is.True);
    }

    [TestCase("null-service-group")]
    [TestCase("empty-services")]
    [TestCase("null-service")]
    [TestCase("null-service-name")]
    [TestCase("null-service-display")]
    [TestCase("null-workers")]
    [TestCase("invalid-worker")]
    [TestCase("invalid-host")]
    [TestCase("invalid-owner")]
    [TestCase("null-connections")]
    [TestCase("null-ipv4")]
    [TestCase("null-ipv6")]
    [TestCase("null-connection")]
    [TestCase("null-endpoint")]
    [TestCase("null-readings")]
    [TestCase("null-flows")]
    [TestCase("null-network-owners")]
    [TestCase("invalid-network-owner")]
    [TestCase("null-reading")]
    [TestCase("null-flow")]
    [TestCase("null-local-flow")]
    [TestCase("null-remote-flow")]
    [TestCase("null-usage-observations")]
    [TestCase("null-usage-owner")]
    [TestCase("null-usage-key")]
    [TestCase("null-usage-name")]
    [TestCase("null-usage-path")]
    [TestCase("null-usage-app")]
    [TestCase("null-usage-publisher")]
    public void Valid_RejectsMalformedSections(string kind)
    {
        var frame = Frame();
        var group = frame.Services![0];
        var service = group.Services[0];
        var connections = frame.Connections!;
        var network = frame.Network!;
        var usage = frame.Usage!;
        var observation = usage.Observations[0];
        var owner = observation.Owner;
        var invalid = Process with { Name = null! };
        frame = kind switch
        {
            "null-service-group" => frame with { Services = [null!] },
            "empty-services" => frame with { Services = [group with { Services = [] }] },
            "null-service" => frame with { Services = [group with { Services = [null!] }] },
            "null-service-name" => frame with { Services = [group with { Services = [service with { Name = null! }] }] },
            "null-service-display" => frame with { Services = [group with { Services = [service with { DisplayName = null! }] }] },
            "null-workers" => frame with { Services = [group with { Processes = null! }] },
            "invalid-worker" => frame with { Services = [group with { Processes = [invalid] }] },
            "invalid-host" => frame with { Services = [group with { Host = invalid }] },
            "invalid-owner" => frame with { Owners = [invalid] },
            "null-connections" => frame with { Connections = connections with { Connections = null! } },
            "null-ipv4" => frame with { Connections = connections with { Ipv4 = null! } },
            "null-ipv6" => frame with { Connections = connections with { Ipv6 = null! } },
            "null-connection" => frame with { Connections = connections with { Connections = [null!] } },
            "null-endpoint" => frame with { Connections = connections with
                { Connections = [connections.Connections[0] with { LocalEndpoint = null! }] } },
            "null-readings" => frame with { Network = network with { Processes = null! } },
            "null-flows" => frame with { Network = network with { Flows = null! } },
            "null-network-owners" => frame with { Network = network with { Owners = null! } },
            "invalid-network-owner" => frame with { Network = network with { Owners = [invalid] } },
            "null-reading" => frame with { Network = network with { Processes = [null!] } },
            "null-flow" => frame with { Network = network with { Flows = [null!] } },
            "null-local-flow" => frame with { Network = network with { Flows = [network.Flows[0] with { LocalEndpoint = null! }] } },
            "null-remote-flow" => frame with { Network = network with { Flows = [network.Flows[0] with { RemoteEndpoint = null! }] } },
            "null-usage-observations" => frame with { Usage = usage with { Observations = null! } },
            "null-usage-owner" => frame with { Usage = usage with { Observations = [observation with { Owner = null! }] } },
            "null-usage-key" => frame with { Usage = usage with { Observations = [observation with { Owner = owner with { Key = null! } }] } },
            "null-usage-name" => frame with { Usage = usage with { Observations = [observation with { Owner = owner with { Name = null! } }] } },
            "null-usage-path" => frame with { Usage = usage with { Observations = [observation with { Owner = owner with { ImagePath = null! } }] } },
            "null-usage-app" => frame with { Usage = usage with { Observations = [observation with { Owner = owner with { AppName = null! } }] } },
            "null-usage-publisher" => frame with { Usage = usage with { Observations = [observation with { Owner = owner with { Publisher = null! } }] } },
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
        Assert.That(InvestigationHistory.Valid(frame), Is.False);
    }

    [TestCase("null")]
    [TestCase("name")]
    [TestCase("path")]
    [TestCase("app")]
    [TestCase("publisher")]
    [TestCase("product")]
    [TestCase("file-version")]
    [TestCase("product-version")]
    [TestCase("gpu-engine")]
    [TestCase("gpu-id")]
    [TestCase("members")]
    public void Valid_RejectsIncompleteProcessMetadata(string field)
    {
        var process = field switch
        {
            "null" => null!,
            "name" => Process with { Name = null! },
            "path" => Process with { ImagePath = null! },
            "app" => Process with { AppName = null! },
            "publisher" => Process with { Publisher = null! },
            "product" => Process with { ProductName = null! },
            "file-version" => Process with { FileVersion = null! },
            "product-version" => Process with { ProductVersion = null! },
            "gpu-engine" => Process with { GpuEngine = null! },
            "gpu-id" => Process with { GpuId = null! },
            "members" => Process with { MemberIds = null! },
            _ => throw new ArgumentOutOfRangeException(nameof(field))
        };
        Assert.That(InvestigationHistory.Valid(Frame() with { Owners = [process] }), Is.False);
    }

    [TestCase("services", false)]
    [TestCase("services", true)]
    [TestCase("owners", false)]
    [TestCase("owners", true)]
    [TestCase("connections", false)]
    [TestCase("connections", true)]
    [TestCase("readings", false)]
    [TestCase("readings", true)]
    [TestCase("flows", false)]
    [TestCase("flows", true)]
    [TestCase("usage", false)]
    [TestCase("usage", true)]
    public void Valid_EnforcesEachSectionBudgetAtTheBoundary(string section, bool exceeds)
    {
        var frame = Frame();
        var extra = exceeds ? 1 : 0;
        frame = section switch
        {
            "services" => frame with { Services = Enumerable.Repeat(frame.Services![0], ServiceHistory.MaximumObservations + extra).ToArray() },
            "owners" => frame with { Owners = Enumerable.Repeat(Process, 100_000 + extra).ToArray() },
            "connections" => frame with { Connections = frame.Connections! with
                { Connections = Enumerable.Repeat(frame.Connections!.Connections[0], 100_000 + extra).ToArray() } },
            "readings" => frame with { Network = frame.Network! with
                { Processes = Enumerable.Repeat(frame.Network!.Processes[0], 100_000 + extra).ToArray() } },
            "flows" => frame with { Network = frame.Network! with
                { Flows = Enumerable.Repeat(frame.Network!.Flows[0], 100_000 + extra).ToArray() } },
            "usage" => frame with { Usage = frame.Usage! with
                { Observations = Enumerable.Repeat(frame.Usage!.Observations[0], 500_000 + extra).ToArray() } },
            _ => throw new ArgumentOutOfRangeException(nameof(section))
        };
        Assert.That(InvestigationHistory.Valid(frame), Is.EqualTo(!exceeds));
    }
}
