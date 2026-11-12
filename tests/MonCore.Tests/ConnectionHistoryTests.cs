using System.Diagnostics;
using System.Net;
using NUnit.Framework;
using WinMon.Monitoring;

namespace WinMon.Tests;

[TestFixture]
public sealed class ConnectionHistoryTests
{
    private static ProcessNetworkFlowReading Flow()
    {
        return new ProcessNetworkFlowReading(42, 1, TransportProtocol.Tcp, "127.0.0.1:5000", "127.0.0.1:443",
            10, 20, 1, 2);
    }
    private static MonitorSample Sample(int second)
    {
        return new MonitorSample(DateTimeOffset.UnixEpoch.AddSeconds(second), 0, [], 0, false,
            second * Stopwatch.Frequency);
    }

    private static NetworkConnectionsFrame Endpoints(int count)
    {
        return new NetworkConnectionsFrame(
            Enumerable.Range(0, count).Select(index => new NetworkConnection(42, 1, TransportProtocol.Udp,
                new IPEndPoint(IPAddress.Loopback, 5000 + index))).ToArray(),
            NetworkAddressFamilyStatus.Success, NetworkAddressFamilyStatus.Success);
    }

    [Test]
    public void Find_RequiresExactRecordedSampleAndKeepsEarlierObservation()
    {
        var history = new ConnectionHistory();
        var first = Sample(1);
        history.Add(first, Endpoints(1), [], null);
        history.Add(Sample(3), Endpoints(2), [], null);
        Assert.Multiple(() =>
        {
            Assert.That(history.Find(first)!.Connections!.Connections, Has.Count.EqualTo(1));
            Assert.That(history.Find(Sample(2)), Is.Null);
            Assert.That(history.Find(0), Is.Null);
        });
    }

    [Test]
    public void Add_EnforcesElapsedRetentionAndResize()
    {
        var history = new ConnectionHistory(10);
        var first = Sample(1);
        history.Add(first, Endpoints(1), [], null);
        var latest = Sample(11);
        history.Add(latest, Endpoints(1), [], null);
        Assert.That(history.Find(first), Is.Null);
        history.Add(Sample(12), Endpoints(1), [], null);
        history.Resize(1);
        Assert.That(history.Find(latest), Is.Null);
        Assert.That(history.Oldest, Is.SameAs(history.Latest));
    }

    [Test]
    public void Add_BudgetIncludesOwnerAndTrafficEntries()
    {
        var history = new ConnectionHistory(entryBudget: 3);
        var first = Sample(1);
        var owner = new ProcessUsage(42, 1, "app", 0);
        var network = new ProcessNetworkFrame(1, 1, ProcessNetworkQuality.None,
            [new ProcessNetworkReading(42, 1, 0, 0, 0, 0)]);
        history.Add(first, Endpoints(1), [owner], network);
        var second = Sample(2);
        history.Add(second, Endpoints(1), [], null);
        Assert.Multiple(() =>
        {
            Assert.That(history.Find(first), Is.Null);
            Assert.That(history.Find(second)!.Available, Is.True);
            Assert.That(history.WasMemoryLimited, Is.True);
        });
    }

    [Test]
    public void Add_BudgetIncludesFlowEntries()
    {
        var history = new ConnectionHistory(entryBudget: 2);
        var network = new ProcessNetworkFrame(1, 1, ProcessNetworkQuality.None, []) { Flows = [Flow(), Flow()] };
        history.Add(Sample(1), null, [], network);

        Assert.Multiple(() =>
        {
            Assert.That(history.Latest!.Network!.Flows, Has.Count.EqualTo(2));
            Assert.That(history.WasMemoryLimited, Is.False);
        });

        history.Add(Sample(2), null, [], network);
        Assert.Multiple(() =>
        {
            Assert.That(history.Captures, Has.Count.EqualTo(1));
            Assert.That(history.WasMemoryLimited, Is.True);
        });
    }

    [TestCase(false, -1, true)]
    [TestCase(false, 0, false)]
    [TestCase(false, 1, false)]
    [TestCase(true, -1, true)]
    [TestCase(true, 0, false)]
    [TestCase(true, 1, false)]
    public void Add_ExpiresExactlyAtRetentionBoundaryUsingTheAvailableClock(bool monotonic, int offset,
        bool retained)
    {
        var history = new ConnectionHistory(10);
        var first = Sample(1) with { MonotonicTimestamp = monotonic ? Stopwatch.Frequency : 0 };
        var latest = first with
        {
            Timestamp = monotonic ? first.Timestamp.AddHours(-1) : first.Timestamp.AddSeconds(10).AddTicks(offset),
            MonotonicTimestamp = monotonic ? first.MonotonicTimestamp + 10 * Stopwatch.Frequency + offset : 0
        };
        history.Add(first, Endpoints(1), [], null);
        history.Add(latest, Endpoints(1), [], null);

        Assert.Multiple(() =>
        {
            Assert.That(history.Find(first) is not null, Is.EqualTo(retained));
            Assert.That(history.Find(latest), Is.SameAs(history.Latest));
            Assert.That(history.WasMemoryLimited, Is.False);
        });
    }

    [Test]
    public void Add_UsesTheFullEntryBudgetAndResizeReleasesEvictedEntries()
    {
        var history = new ConnectionHistory(10, entryBudget: 2);
        var first = Sample(1);
        var second = Sample(2);
        history.Add(first, Endpoints(1), [], null);
        history.Add(second, Endpoints(1), [], null);
        Assert.That(history.Captures, Has.Count.EqualTo(2));
        Assert.That(history.WasMemoryLimited, Is.False);

        history.Resize(1);
        history.Resize(10);
        history.Add(Sample(3), Endpoints(1), [], null);
        Assert.Multiple(() =>
        {
            Assert.That(history.Find(first), Is.Null);
            Assert.That(history.Find(second), Is.Not.Null);
            Assert.That(history.Captures, Has.Count.EqualTo(2));
            Assert.That(history.WasMemoryLimited, Is.False, "Resizing must release the evicted entries' budget.");
        });
    }

    [Test]
    public void Add_OversizedObservationIsUnavailableWithoutEvictingUsefulHistory()
    {
        var history = new ConnectionHistory(entryBudget: 2);
        var first = Sample(1);
        var oversized = Sample(2);
        history.Add(first, Endpoints(1), [], null);
        history.Add(oversized, Endpoints(3), [], null);
        Assert.Multiple(() =>
        {
            Assert.That(history.Find(first)!.Available, Is.True);
            Assert.That(history.Find(oversized)!.Available, Is.False);
            Assert.That(history.Find(oversized)!.Owners, Is.Empty);
            Assert.That(history.WasMemoryLimited, Is.True);
        });
    }
}
