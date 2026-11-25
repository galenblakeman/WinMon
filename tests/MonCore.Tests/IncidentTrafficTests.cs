using System.Diagnostics;
using MonCore.Monitoring;
using NUnit.Framework;

namespace MonCore.Tests;

[TestFixture]
public sealed class IncidentTrafficTests
{
    private static MonitorSample Sample(int second, params ProcessUsage[] processes) =>
        new(DateTimeOffset.UnixEpoch.AddSeconds(second), 10, processes, 0,
            MonotonicTimestamp: (second + 1) * Stopwatch.Frequency);

    private static ConnectionCapture Traffic(MonitorSample sample, ProcessUsage owner, double? rate,
        ProcessNetworkQuality quality = ProcessNetworkQuality.Provisional) => new(sample, null, [owner],
        new ProcessNetworkFrame(sample.MonotonicTimestamp, Stopwatch.Frequency, quality,
            [new(owner.ProcessId, owner.CreationTicks, 100, 200, rate, rate)]) { Owners = [owner] });

    [Test]
    public void Network_RanksTrafficInsteadOfCpuAndIncludesCompactedOwners()
    {
        var cpu = new ProcessUsage(1, 1, "cpu", 99);
        var network = new ProcessUsage(2, 2, "download", 0);
        var sample = Sample(0, cpu) with { CompactedProcesses = 1 };
        var capture = Traffic(sample, network, 500);
        var rows = IncidentTimeline.ProcessReadings(sample, CaptureMetric.Network, capture);
        Assert.That(rows.Select(row => row.Process.Name), Is.EqualTo(new[] { "download", "cpu" }));
        Assert.That(rows[0].Value, Is.EqualTo(1000));
        Assert.That(rows[1].Value, Is.Null);
    }

    [TestCase(ProcessNetworkQuality.WarmingUp)]
    [TestCase(ProcessNetworkQuality.CapacityExceeded)]
    [TestCase(ProcessNetworkQuality.Unavailable)]
    [TestCase(ProcessNetworkQuality.Discontinuity)]
    public void Network_RejectsInvalidCoverageInsteadOfInventingZero(ProcessNetworkQuality quality)
    {
        var process = new ProcessUsage(1, 1, "app", 99);
        var sample = Sample(0, process);
        Assert.That(IncidentTimeline.ProcessReadings(sample, CaptureMetric.Network,
            Traffic(sample, process, 100, quality)).Single().Value, Is.Null);
    }

    [Test]
    public void Network_UsesRecoveredRatesWhileSessionTotalsRemainIncomplete()
    {
        var session = new ProcessNetworkSession();
        var generation = Guid.NewGuid();
        ProcessNetworkSnapshot Snapshot(int sequence, long stamp) => new(generation, sequence, stamp, 10,
            stamp - 5, stamp, ProcessNetworkQuality.Provisional, 0, 0, [new(1, 1, sequence * 100, 0, 20, 40)]);
        session.Add(Snapshot(1, 100), 100);
        session.Unavailable(110, 10);
        var recovered = session.Add(Snapshot(2, 200), 200);
        var owner = new ProcessUsage(1, 1, "download", 0);
        var sample = Sample(0, owner);
        var capture = new ConnectionCapture(sample, null, [owner], recovered);

        Assert.That(recovered.Quality.HasFlag(ProcessNetworkQuality.Incomplete), Is.True);
        Assert.That(IncidentTimeline.ProcessReadings(sample, CaptureMetric.Network, capture).Single().Value,
            Is.EqualTo(60));
        Assert.That(IncidentTimeline.Compare([sample], sample, CaptureMetric.Network,
            new Dictionary<SampleKey, ConnectionCapture> { [SampleKey.Of(sample)] = capture })
            .Processes.Single().During, Is.EqualTo(new IncidentTimeline.Reading(60, 1)));
    }

    [TestCase(ProcessNetworkQuality.Provisional)]
    [TestCase(ProcessNetworkQuality.Provisional | ProcessNetworkQuality.Incomplete)]
    public void Network_RequiresExactObservationLifetimeAndCurrentRate(ProcessNetworkQuality quality)
    {
        var old = new ProcessUsage(1, 1, "old", 0);
        var replacement = old with { CreationTicks = 2, Name = "replacement" };
        var sample = Sample(0, replacement);
        var capture = Traffic(sample, old, 100, quality);
        Assert.That(IncidentTimeline.ProcessReadings(sample, CaptureMetric.Network, capture)
            .Single(row => row.Process == replacement).Value, Is.Null);
        Assert.That(IncidentTimeline.ProcessReadings(sample, CaptureMetric.Network,
            capture with { Sample = Sample(1) }).Select(row => row.Value), Is.EqualTo(new double?[] { null }));
        Assert.That(IncidentTimeline.ProcessReadings(sample, CaptureMetric.Network).Single().Value, Is.Null);
        capture = Traffic(sample, replacement, 100, quality);
        foreach (var rate in new double?[] { null, -1, double.NaN, double.PositiveInfinity })
            Assert.That(IncidentTimeline.ProcessReadings(sample, CaptureMetric.Network,
                Traffic(sample, replacement, rate, quality)).Single().Value, Is.Null);
        Assert.That(IncidentTimeline.ProcessReadings(sample with { IsGap = true }, CaptureMetric.Network, capture)
            .Single().Value, Is.Null);
        Assert.That(IncidentTimeline.ProcessReadings(sample, CaptureMetric.Network, capture with
            { Network = capture.Network! with { CurrentLifetimes = new HashSet<ProcessLifetimeIdentity>() } }).Single().Value, Is.Null);
        var portable = replacement with { LifetimeDomain = ProcessLifetimeDomain.UnixMicroseconds };
        Assert.That(IncidentTimeline.ProcessReadings(Sample(0, portable), CaptureMetric.Network, capture)
            .Single(row => row.Process == portable).Value, Is.Null);
    }

    [Test]
    public void Compare_NetworkUsesObservedTrafficAndKeepsMissingPeriodsUnavailable()
    {
        var owner = new ProcessUsage(1, 1, "download", 99);
        var samples = new[] { Sample(0), Sample(15), Sample(20) };
        var frames = new[] { Traffic(samples[0], owner, 10), Traffic(samples[1], owner, 70) }
            .ToDictionary(frame => SampleKey.Of(frame.Sample));
        var result = IncidentTimeline.Compare(samples, samples[1], CaptureMetric.Network, frames);
        Assert.That(result.Processes, Has.Count.EqualTo(1));
        Assert.That(result.Processes[0].Before, Is.EqualTo(new IncidentTimeline.Reading(20, 1)));
        Assert.That(result.Processes[0].During, Is.EqualTo(new IncidentTimeline.Reading(140, 1)));
        Assert.That(result.Processes[0].Change, Is.EqualTo(120));
        Assert.That(result.Processes[0].After, Is.EqualTo(new IncidentTimeline.Reading(null, 0)));
    }

    [Test]
    public void Network_UsesExactOwnersAndDoesNotShowStaleNonNetworkReadings()
    {
        var sample = Sample(0);
        var stale = new ProcessUsage(1, 1, "app", 99, 1000, 500) { GpuPercent = 50 };
        var exact = stale with { CpuPercent = 2 };
        var capture = Traffic(sample, stale, 100) with { Owners = [exact] };
        Assert.That(IncidentTimeline.ProcessReadings(sample, CaptureMetric.Network, capture).Single().Process.CpuPercent, Is.EqualTo(2));
        var row = IncidentTimeline.ProcessReadings(sample, CaptureMetric.Network, capture with { Owners = [] }).Single();
        Assert.That(row.Value, Is.EqualTo(200));
        Assert.That(row.Process.ObservedCpuPercent, Is.Null);
        Assert.That(row.Process.WorkingSetBytes, Is.Null);
        Assert.That(row.Process.IoBytesPerSecond, Is.Null);
        Assert.That(row.Process.GpuPercent, Is.Null);
    }
}
