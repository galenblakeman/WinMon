using System.Collections;
using System.Net;
using System.Text.Json;
using MonCore.Monitoring;
using NUnit.Framework;

namespace WinMon.Cli.Tests;

[TestFixture]
public sealed class ActivityReportTests
{
    [Test]
    public void ConnectionReportsIndexOwnersOnceBeforeApplyingTop()
    {
        var processes = new CountingProcesses(Enumerable.Range(1, 128)
            .Select(pid => new ProcessUsage(pid, 123, "worker" + pid, 1)).ToArray());
        var frame = Frame(processes, Enumerable.Range(1, 1000).Select(port => Endpoint(128, 123, port)).ToArray());

        var report = ActivityReport.Create("connections", [frame], new UsageLedger(frame.Full.Timestamp), 1, "worker", 1);
        var json = JsonSerializer.SerializeToElement(report.Data, SnapshotReport.JsonOptions);

        Assert.Multiple(() =>
        {
            Assert.That(processes.Reads, Is.LessThanOrEqualTo(processes.Count), "Owner resolution must scan the process list at most once.");
            Assert.That(json.GetProperty("returnedCount").GetInt32(), Is.EqualTo(1));
            Assert.That(json.GetProperty("truncated").GetBoolean(), Is.True);
            Assert.That(json.GetProperty("entries")[0].GetProperty("name").GetString(), Is.EqualTo("worker128"));
            Assert.That(json.GetProperty("entries")[0].GetProperty("localPort").GetInt32(), Is.EqualTo(1));
            Assert.That(report.Table.Rows.Single().Cells[1], Is.EqualTo("128"));
        });
    }

    [TestCase(0, 0, "", new[] { 1001, 1002, 1003, 1000, 1004 }, false)]
    [TestCase(0, 1, "", new[] { 1001 }, true)]
    [TestCase(1, 0, "", new[] { 1003, 1004, 1001, 1002, 1000 }, false)]
    [TestCase(1, 3, "", new[] { 1003, 1004, 1001 }, true)]
    [TestCase(1, 10, "ALPHA", new[] { 1001, 1002 }, false)]
    [TestCase(0, 1, "42", new[] { 1000 }, true)]
    [TestCase(1, 1, "missing", new int[0], false)]
    public void ConnectionReportsKeepJsonAndTableOrderAndTruncationAligned(int sort, int top, string filter,
        int[] expectedPorts, bool truncated)
    {
        var frame = Frame([new ProcessUsage(42, 123, "zeta", 1), new ProcessUsage(7, 123, "alpha", 1)],
            [Endpoint(42, 123, 1000), Endpoint(7, 123, 1001), Endpoint(7, 123, 1002),
                Endpoint(11, 123, 1003), Endpoint(42, null, 1004)]);

        var report = ActivityReport.Create("connections", [frame], new UsageLedger(frame.Full.Timestamp), sort, filter, top);
        var json = JsonSerializer.SerializeToElement(report.Data, SnapshotReport.JsonOptions);
        var entries = json.GetProperty("entries").EnumerateArray().ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(entries.Select(entry => entry.GetProperty("localPort").GetInt32()), Is.EqualTo(expectedPorts));
            Assert.That(json.GetProperty("returnedCount").GetInt32(), Is.EqualTo(expectedPorts.Length));
            Assert.That(json.GetProperty("truncated").GetBoolean(), Is.EqualTo(truncated));
            Assert.That(report.Table.Rows.Count, Is.EqualTo(expectedPorts.Length));
            for (var i = 0; i < entries.Length; i++)
            {
                Assert.That(report.Table.Rows[i].Cells[0], Is.EqualTo(entries[i].GetProperty("name").GetString() ?? WinMon.Resources.Strings.Unavailable));
                Assert.That(report.Table.Rows[i].Cells[1], Is.EqualTo(entries[i].GetProperty("pid").GetInt32().ToString()));
                Assert.That(report.Table.Rows[i].Cells[2], Does.Contain("127.0.0.1:" + expectedPorts[i] + " "));
            }
        });
    }

    private static CapturedFrame Frame(IReadOnlyList<ProcessUsage> processes, NetworkConnection[] endpoints) =>
        new(new MonitorSample(DateTimeOffset.UnixEpoch, 20, processes, 0))
        {
            Connections = new(endpoints, NetworkAddressFamilyStatus.Success, NetworkAddressFamilyStatus.Success)
        };

    [Test]
    public void ConnectionReportsDoNotFormatEndpointsOutsideTop()
    {
        var frame = Frame([], [Endpoint(1, null, 1000),
            new NetworkConnection(2, null, TransportProtocol.Tcp, new IPEndPoint(new UnformattableAddress(), 1001))]);

        var report = ActivityReport.Create("connections", [frame], new UsageLedger(frame.Full.Timestamp), 0, "", 1);
        var json = JsonSerializer.SerializeToElement(report.Data, SnapshotReport.JsonOptions);

        Assert.That(report.Table.Rows.Single().Cells[2], Does.Contain("127.0.0.1:1000"));
        Assert.That(json.GetProperty("entries")[0].GetProperty("localPort").GetInt32(), Is.EqualTo(1000));
        Assert.That(json.GetProperty("truncated").GetBoolean(), Is.True);
    }

    [Test]
    public void ConnectionOwnersRequireExactWindowsLifetime()
    {
        var frame = Frame([
            new ProcessUsage(42, 123, "unix-owner", 1) { LifetimeDomain = ProcessLifetimeDomain.UnixMicroseconds },
            new ProcessUsage(42, 456, "reused-pid", 1),
            new ProcessUsage(42, 123, "windows-owner", 1),
            new ProcessUsage(42, 123, "duplicate-owner", 1)
        ], [Endpoint(42, 123, 1000), Endpoint(42, null, 1001), Endpoint(42, 999, 1002)]);

        var report = ActivityReport.Create("connections", [frame], new UsageLedger(frame.Full.Timestamp), 0, "", 0);
        var json = JsonSerializer.SerializeToElement(report.Data, SnapshotReport.JsonOptions);

        Assert.That(json.GetProperty("entries").EnumerateArray().Select(entry => entry.GetProperty("name").GetString()),
            Is.EqualTo(new string?[] { "windows-owner", null, null }));
        Assert.That(report.Table.Rows.Select(row => row.Cells[0]), Is.EqualTo(new[]
            { "windows-owner", WinMon.Resources.Strings.Unavailable, WinMon.Resources.Strings.Unavailable }));
    }

    private static NetworkConnection Endpoint(int pid, long? created, int port) =>
        new(pid, created, TransportProtocol.Tcp, new IPEndPoint(IPAddress.Loopback, port));

    private sealed class UnformattableAddress() : IPAddress(0)
    {
        public override string ToString() => throw new AssertionException("An endpoint outside Top must not be formatted.");
    }

    private sealed class CountingProcesses(ProcessUsage[] processes) : IReadOnlyList<ProcessUsage>
    {
        public int Reads { get; private set; }
        public int Count => processes.Length;
        public ProcessUsage this[int index] { get { Reads++; return processes[index]; } }
        public IEnumerator<ProcessUsage> GetEnumerator()
        {
            for (var i = 0; i < Count; i++) yield return this[i];
        }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
