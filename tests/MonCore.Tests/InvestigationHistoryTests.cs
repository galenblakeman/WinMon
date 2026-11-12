using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using NUnit.Framework;
using WinMon.Monitoring;

namespace WinMon.Tests;

[TestFixture]
public sealed class InvestigationHistoryTests
{
    private static MonitorSession Session() => new(() => throw new InvalidOperationException(),
        () => new SamplingOptions(), action => action(), retainUsageHistory: true);

    [Test]
    public void DamagedOptionalObservationsDoNotDiscardResourceHistoryOrInventUsage()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".bin");
        try
        {
            var session = Session();
            var sample = new MonitorSample(DateTimeOffset.UtcNow, 10, [], 0,
                MonotonicTimestamp: Stopwatch.GetTimestamp()) { IntervalSeconds = 1, IntervalCpuTicks = 100 };
            session.Record(sample, sample, new ServiceFrame(sample, []));
            var clock = RestartClock.System;
            Assert.That(RestartHistory.Save(path, "test", "test", [sample], null, clock,
                investigation: session.CaptureInvestigation()), Is.True);
            var bytes = File.ReadAllBytes(path);
            using (var stream = new MemoryStream(bytes))
            using (var reader = new BinaryReader(stream))
            {
                stream.Position = 8;
                _ = reader.ReadString();
                _ = reader.ReadString();
                stream.Position += 24;
                var damaged = false;
                while (stream.Position < stream.Length - 12)
                {
                    var kind = reader.ReadByte();
                    var length = reader.ReadInt32();
                    _ = reader.ReadUInt32();
                    if (kind == 3)
                    {
                        bytes[(int)stream.Position] ^= 0xff;
                        damaged = true;
                        break;
                    }
                    stream.Position += length;
                }
                Assert.That(damaged, Is.True);
            }
            File.WriteAllBytes(path, bytes);
            var contents = RestartHistory.Read(path, "test", clock)!;
            var copy = Session();
            foreach (var restored in contents.Samples) copy.Record(restored);
            copy.RestoreInvestigation(contents.Investigation);
            Assert.Multiple(() =>
            {
                Assert.That(copy.History.Samples, Has.Count.EqualTo(1));
                Assert.That(copy.Services.Frames, Is.Empty);
                Assert.That(copy.UsageHistory.Query(copy.History.Latest!, 1), Is.Null);
            });
        }
        finally { File.Delete(path); }
    }

    [TestCase(false)]
    [TestCase(true)]
    public void RestartPreservesExactServiceConnectionTrafficAndUncompactedUsageObservations(bool normal)
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".bin");
        try
        {
            var session = Session();
            session.History.Resize(1800);
            var now = DateTimeOffset.UtcNow;
            var qpc = Stopwatch.GetTimestamp();
            var clock = new RestartClock(() => qpc, () => Environment.TickCount64, Stopwatch.Frequency);
            var process = new ProcessUsage(42, 100, "worker", 25, 1024, 100) { ImagePath = "C:\\worker.exe" };
            var sample = new MonitorSample(now, 50, [process], 0, MonotonicTimestamp: qpc)
                { IntervalSeconds = 1, IntervalCpuTicks = 10_000_000 };
            var group = new ServiceGroup("svc", [new ServiceObservation("svc", "Worker service", 4, 42, 1)],
                process, [process]);
            var network = new ProcessNetworkFrame(qpc, Stopwatch.Frequency, ProcessNetworkQuality.Incomplete,
                [new ProcessNetworkReading(42, 100, 500, 200, null, 50)])
            {
                Owners = [process], CurrentLifetimes = new HashSet<ProcessLifetimeIdentity> { process.Identity },
                CurrentFlows = new HashSet<ProcessNetworkFlowIdentity>(), FlowTrafficSupported = true,
                Flows = [new ProcessNetworkFlowReading(42, 100, TransportProtocol.Tcp, "[fe80::1%3]:5",
                    "10.0.0.2:443", 500, 200, null, 50)]
            };
            var connections = new NetworkConnectionsFrame([new NetworkConnection(42, 100, TransportProtocol.Tcp,
                IPEndPoint.Parse("[fe80::1%3]:5"), IPEndPoint.Parse("10.0.0.2:443"), TcpState.Established)],
                NetworkAddressFamilyStatus.Success, new NetworkAddressFamilyStatus(false, 5));
            // Deliberately omit the process from compacted history: Usage must retain the original interval.
            var earlier = sample with { Timestamp = now.AddSeconds(-1), MonotonicTimestamp = qpc - Stopwatch.Frequency };
            session.Record(earlier with { Processes = [] }, earlier, new ServiceFrame(earlier, [group]), connections, network);
            session.Record(sample with { Processes = [] }, sample, new ServiceFrame(sample, [group]), connections, network);
            var investigation = session.CaptureInvestigation();
            IReadOnlyList<MonitorSample> samples;
            InvestigationHistory? restored;
            if (normal)
            {
                Assert.That(RestartHistory.SaveNormal(path, "test", session.History.Samples, clock, investigation), Is.True);
                var reboot = clock with { Timestamp = () => Stopwatch.Frequency * 5, TickCount = () => 5000 };
                var contents = RestartHistory.ReadNormal(path, reboot, now.AddSeconds(1), 1800)!.Value;
                samples = contents.Samples;
                restored = contents.Investigation;
            }
            else
            {
                Assert.That(RestartHistory.Save(path, "test", "test", session.History.Samples, session.Usage.Export(),
                    clock, investigation: investigation), Is.True);
                var contents = RestartHistory.Read(path, "test", clock)!;
                samples = contents.Samples;
                restored = contents.Investigation;
            }
            var copy = Session();
            copy.History.Resize(1800);
            foreach (var item in samples) copy.Record(item);
            copy.RestoreInvestigation(restored);
            var selected = copy.History.Latest!;
            Assert.Multiple(() =>
            {
                Assert.That(copy.Services.Capacity, Is.EqualTo(1800));
                Assert.That(copy.Services.Find(selected)!.Groups[0].Services[0].Name, Is.EqualTo("svc"));
                Assert.That(copy.Connections.Find(selected)!.Connections!.Connections[0].LocalEndpoint,
                    Is.EqualTo(connections.Connections[0].LocalEndpoint));
                Assert.That(copy.Connections.Find(selected)!.Connections!.PartiallyAvailable, Is.True);
                Assert.That(copy.Connections.Find(selected)!.Network!.Flows[0], Is.EqualTo(network.Flows[0]));
                Assert.That(copy.Connections.Find(selected)!.Network!.CurrentLifetimes, Does.Contain(process.Identity));
                Assert.That(copy.UsageHistory.Query(selected, 1)!.Apps[0].CpuTicks, Is.EqualTo(2_500_000));
                Assert.That(copy.UsageHistory.Query(selected, 1)!.Complete, Is.True);
                Assert.That(copy.UsageHistory.Query(selected, 2)!.Apps[0].CpuTicks, Is.EqualTo(5_000_000));
                Assert.That(copy.ProcessNetwork, Is.Null, "Historical traffic cannot seed live totals.");
                Assert.That(copy.Usage.Apps, Is.Empty, "Restored history is never counted into the new session.");
            });
        }
        finally { File.Delete(path); }
    }
}
