using System.Net;
using MonCore.Monitoring;
using NUnit.Framework;

namespace MonCore.Tests;

[TestFixture]
public sealed class ConnectionSummaryCharacterizationTests
{
    private static readonly ProcessUsage Owner = new(42, 100, "worker", 0)
        { ImagePath = @"C:\apps\worker.exe" };
    private static readonly ProcessUsage Sibling = Owner with { ProcessId = 43, CreationTicks = 101 };
    private static readonly NetworkConnectionsFrame Endpoints = new(
        [new(42, 100, TransportProtocol.Tcp, new IPEndPoint(IPAddress.Loopback, 5000))],
        NetworkAddressFamilyStatus.Success, NetworkAddressFamilyStatus.Success);

    private static ConnectionNetworkSummary Summary(ProcessNetworkFrame? network, bool enabled = true,
        bool includeSibling = false) => ConnectionQuery.Build(Endpoints,
        includeSibling ? [Owner, Sibling] : [Owner], network, [], null,
        new ConnectionQueryOptions(NetworkEnabled: enabled, TrafficSeconds: 0)).Single().Network;

    private static ProcessNetworkFrame Network(params ProcessNetworkReading[] readings) =>
        new(1, 1, ProcessNetworkQuality.None, readings);

    [TestCase(ProcessNetworkQuality.None)]
    [TestCase(ProcessNetworkQuality.WarmingUp)]
    [TestCase(ProcessNetworkQuality.Provisional)]
    [TestCase(ProcessNetworkQuality.Incomplete)]
    [TestCase(ProcessNetworkQuality.CapacityExceeded)]
    [TestCase(ProcessNetworkQuality.Unavailable)]
    [TestCase(ProcessNetworkQuality.Discontinuity)]
    [TestCase((ProcessNetworkQuality)127)]
    [TestCase((ProcessNetworkQuality)128)]
    public void QualityFlagsPreserveNumericRatesAndTotals(ProcessNetworkQuality quality)
    {
        var result = Summary(Network(new ProcessNetworkReading(42, 100, 123, 456, 10, 20)) with { Quality = quality });

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(new ConnectionNetworkSummary(123, 456, 10, 20, quality, true, true)));
            Assert.That(result.RatesAvailable, Is.EqualTo(!quality.HasFlag(ProcessNetworkQuality.Unavailable)));
        });
    }

    [Test]
    public void DisabledSummaryWinsOverMissingNetworkOrInvalidCounters()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Summary(null, false), Is.SameAs(ConnectionNetworkSummary.Disabled));
            Assert.That(Summary(Network(new ProcessNetworkReading(42, 100, -1, -1, double.NaN, null)), false),
                Is.SameAs(ConnectionNetworkSummary.Disabled));
        });
    }

    [Test]
    public void MissingNetworkOrUnmatchedReadingReturnsUnavailable()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Summary(null), Is.SameAs(ConnectionNetworkSummary.Unavailable));
            Assert.That(Summary(Network()), Is.SameAs(ConnectionNetworkSummary.Unavailable));
            Assert.That(Summary(Network(new ProcessNetworkReading(42, 99, 123, 456, 10, 20))),
                Is.SameAs(ConnectionNetworkSummary.Unavailable));
        });
    }

    [Test]
    public void UnknownEndpointWithoutLifetimeHasNoNetworkIdentity()
    {
        var frame = Endpoints with { Connections = [Endpoints.Connections[0] with { ProcessCreationTicks = null }] };
        var row = ConnectionQuery.Build(frame, [], Network(new ProcessNetworkReading(42, 100, 123, 456, 10, 20)), [], null,
            new ConnectionQueryOptions(NetworkEnabled: true)).Single();

        Assert.That(row.Network, Is.SameAs(ConnectionNetworkSummary.Unavailable));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void RetainedRatesAreIgnoredButRetainedCountersAreValidated(bool invalidCounter)
    {
        var network = Network(new ProcessNetworkReading(42, 100, 123, 456, 10, 20),
            new(43, 101, invalidCounter ? -1 : 7, 8, double.NaN, -1)) with
        {
            CurrentLifetimes = new HashSet<ProcessLifetimeIdentity> { Owner.Identity }
        };

        Assert.That(Summary(network, includeSibling: true), Is.EqualTo(invalidCounter
            ? ConnectionNetworkSummary.Unavailable
            : new ConnectionNetworkSummary(130, 464, 10, 20, ProcessNetworkQuality.None, true, true)));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void NoCurrentWindowsIdentityRetainsTotalsWithoutNumericRates(bool foreignDomain)
    {
        var current = new HashSet<ProcessLifetimeIdentity>();
        if (foreignDomain) current.Add(new(42, 100, ProcessLifetimeDomain.UnixMicroseconds));
        var result = Summary(Network(new ProcessNetworkReading(42, 100, 123, 456, 10, 20)) with { CurrentLifetimes = current });

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(new ConnectionNetworkSummary(123, 456, null, null,
                ProcessNetworkQuality.None, true, true)));
            Assert.That(result.RatesAvailable, Is.True);
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void FirstDuplicateReadingControlsCounterValidity(bool invalidFirst)
    {
        var valid = new ProcessNetworkReading(42, 100, 123, 456, 10, 20);
        var invalid = valid with { ReceivedBytes = -1 };
        var network = invalidFirst ? Network(invalid, valid) : Network(valid, invalid);

        Assert.That(Summary(network), Is.EqualTo(invalidFirst ? ConnectionNetworkSummary.Unavailable :
            new ConnectionNetworkSummary(123, 456, 10, 20, ProcessNetworkQuality.None, true, true)));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void EitherDirectionOverflowHidesBothRatesWithoutLosingTotals(bool overflowSend)
    {
        var network = Network(new ProcessNetworkReading(42, 100, 0, long.MaxValue, double.MaxValue, double.MaxValue),
            new(43, 101, long.MaxValue, 0, overflowSend ? 0 : double.MaxValue,
                overflowSend ? double.MaxValue : 0));

        Assert.That(Summary(network, includeSibling: true), Is.EqualTo(new ConnectionNetworkSummary(
            long.MaxValue, long.MaxValue, null, null, ProcessNetworkQuality.None, true, true)));
    }

    [Test]
    public void ZeroRatesAreAvailableAndMissingSiblingAddsOnlyIncompleteQuality()
    {
        var network = Network(new ProcessNetworkReading(42, 100, 0, 0, -0d, 0)) with { Quality = ProcessNetworkQuality.WarmingUp };

        Assert.That(Summary(network, includeSibling: true), Is.EqualTo(new ConnectionNetworkSummary(
            0, 0, 0, 0, ProcessNetworkQuality.WarmingUp | ProcessNetworkQuality.Incomplete, true, true)));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void OneInvalidCurrentSiblingHidesBothRatesRegardlessOfReadingOrder(bool invalidFirst)
    {
        var first = new ProcessNetworkReading(42, 100, 10, 20, invalidFirst ? null : 1, 2);
        var second = new ProcessNetworkReading(43, 101, 30, 40, invalidFirst ? 3 : null, 4);

        Assert.That(Summary(Network(first, second), includeSibling: true), Is.EqualTo(
            new ConnectionNetworkSummary(40, 60, null, null, ProcessNetworkQuality.None, true, true)));
    }
}
