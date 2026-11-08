using System.Diagnostics;
using System.Net;
using NUnit.Framework;
using WinMon.Monitoring;

namespace WinMon.Tests;

[TestFixture]
public sealed class ConnectionHistoryTests
{
    private static MonitorSample Sample(int second) =>
        new(DateTimeOffset.UnixEpoch.AddSeconds(second), 0, [], 0, false, second * Stopwatch.Frequency);

    private static NetworkConnectionsFrame Endpoints(int count) => new(
        Enumerable.Range(0, count).Select(index => new NetworkConnection(42, 1, TransportProtocol.Udp,
            new IPEndPoint(IPAddress.Loopback, 5000 + index))).ToArray(),
        NetworkAddressFamilyStatus.Success, NetworkAddressFamilyStatus.Success);

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
