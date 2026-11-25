using MonCore.Monitoring;
using NUnit.Framework;

namespace MonCore.Tests;

[TestFixture]
public sealed class ScopedCaptureRulesTests
{
    private static MonitorSample Frame(int second, params ProcessUsage[] processes) => new(DateTimeOffset.UnixEpoch.AddSeconds(second), 20, processes, 0);
    private static ProcessUsage Process(int pid, long token, string name, double cpu = 20) => new(pid, token, name, cpu)
        { ImagePath = "C:\\apps\\" + name + ".exe", PrivateBytes = 600UL * 1024 * 1024 };

    [Test]
    public void ExecutableRulesAggregateInstancesAndBreakOnMissingReadings()
    {
        var rule = new CaptureRuleSetting(CaptureMetric.ApplicationCpu, true, 90, 2) { Target = "worker.exe" };
        var rules = new CaptureRules([rule], TimeSpan.Zero, 4);
        Assert.That(rules.Observe(Frame(1, Process(1, 1, "worker"), Process(2, 2, "worker"))), Is.Empty);
        var fired = rules.Observe(Frame(2, Process(1, 1, "worker"), Process(2, 2, "worker"))).Single();
        Assert.That(fired.PeakValue, Is.EqualTo(160));
        var memory = new CaptureRules([new CaptureRuleSetting(CaptureMetric.ApplicationMemory, true, 1000, 1) { Target = "worker" }], TimeSpan.Zero, 4);
        Assert.That(memory.Observe(Frame(1, Process(1, 1, "worker"), Process(2, 2, "worker"))).Single().PeakValue, Is.EqualTo(1200));
        Assert.That(new CaptureRules([rule with { Target = "absent" }], TimeSpan.Zero, 4).Observe(Frame(1, Process(1, 1, "worker"))), Is.Empty);
    }

    [Test]
    public void ServiceRulesRejectSharedHostsAndUnalignedServiceObservations()
    {
        var rule = new CaptureRuleSetting(CaptureMetric.ServiceCpu, true, 10, 1) { Target = "svc" };
        var rules = new CaptureRules([rule], TimeSpan.Zero, 4);
        var sample = Frame(1, Process(1, 1, "worker"));
        var group = new ServiceGroup("host", [new ServiceObservation("svc", "Service", 4, 1, 0)], sample.Processes[0], sample.Processes);
        var shared = group with { Services = [.. group.Services, new ServiceObservation("other", "Other", 4, 1, 0)] };
        Assert.That(rules.Observe(sample, new ServiceFrame(sample, [shared])), Is.Empty);
        Assert.That(rules.Observe(sample, new ServiceFrame(sample with { Timestamp = sample.Timestamp.AddSeconds(-1) }, [group])), Is.Empty);
        Assert.That(rules.Observe(sample, new ServiceFrame(sample, [group])).Single().PeakValue, Is.EqualTo(80));
    }

    [Test]
    public void NetworkRulesRequireLifetimeSafeAvailableRates()
    {
        var rules = new CaptureRules([new CaptureRuleSetting(CaptureMetric.ApplicationNetwork, true, 1, 1) { Target = "worker" }], TimeSpan.Zero, 4);
        var sample = Frame(1, Process(1, 2, "worker"));
        var old = new ProcessNetworkFrame(1, 1, ProcessNetworkQuality.None, [new ProcessNetworkReading(1, 1, 0, 0, 1_000_000, 0)]);
        Assert.That(rules.Observe(sample, network: old), Is.Empty);
        var valid = old with { Processes = [new ProcessNetworkReading(1, 2, 0, 0, 1_000_000, 0)] };
        Assert.That(rules.Observe(sample, network: valid).Single().PeakValue, Is.EqualTo(8));
    }

    [TestCase(CaptureMetric.ApplicationCpu)]
    [TestCase(CaptureMetric.ApplicationMemory)]
    public void MissingMemberReadingBreaksTheWholeApplicationRun(CaptureMetric metric)
    {
        var rules = new CaptureRules([new CaptureRuleSetting(metric, true, 10, 2) { Target = "worker" }], TimeSpan.Zero, 4);
        var first = Process(1, 1, "worker");
        var second = Process(2, 2, "worker");
        var missing = metric == CaptureMetric.ApplicationCpu
            ? second with { CpuAvailable = false }
            : second with { PrivateBytes = null };

        Assert.That(rules.Observe(Frame(1, first, second)), Is.Empty);
        Assert.That(rules.Observe(Frame(2, first, missing)), Is.Empty);
        Assert.That(rules.Observe(Frame(3, first, second)), Is.Empty);
        Assert.That(rules.Observe(Frame(4, first, second)).Single().Run,
            Is.EqualTo(metric == CaptureMetric.ApplicationCpu ? new[] { 160d, 160d } : new[] { 1200d, 1200d }));
    }

    [Test]
    public void FullPathTargetDoesNotIncludeOtherExecutablesWithTheSameName()
    {
        var rules = new CaptureRules([new CaptureRuleSetting(CaptureMetric.ApplicationCpu, true, 90, 2)
            { Target = @"C:\apps\WORKER.exe" }], TimeSpan.Zero, 4);
        var wanted = Process(1, 1, "worker");
        var other = Process(2, 2, "worker") with { ImagePath = @"C:\other\worker.exe", CpuPercent = 99 };

        Assert.That(rules.Observe(Frame(1, wanted, other)), Is.Empty);
        Assert.That(rules.Observe(Frame(2, wanted, other)), Is.Empty, "Only the selected path contributes 80% of one core.");
        Assert.That(rules.Observe(Frame(3, wanted with { CpuPercent = 25 }, other)), Is.Empty);
        Assert.That(rules.Observe(Frame(4, wanted with { CpuPercent = 25 }, other)).Single().PeakValue, Is.EqualTo(100));
    }

    [TestCase(ProcessNetworkQuality.WarmingUp)]
    [TestCase(ProcessNetworkQuality.CapacityExceeded)]
    [TestCase(ProcessNetworkQuality.Unavailable)]
    [TestCase(ProcessNetworkQuality.Discontinuity)]
    public void UnreliableTrafficBreaksTheRunAndProvisionalTrafficCanStartAFreshOne(ProcessNetworkQuality quality)
    {
        var rules = new CaptureRules([new CaptureRuleSetting(CaptureMetric.ApplicationNetwork, true, 1, 2)
            { Target = "worker" }], TimeSpan.Zero, 4);
        var process = Process(1, 1, "worker");
        var network = new ProcessNetworkFrame(1, 1, ProcessNetworkQuality.Provisional,
            [new ProcessNetworkReading(1, 1, 0, 0, 1_000_000, 0)]);

        Assert.That(rules.Observe(Frame(1, process), network: network), Is.Empty);
        Assert.That(rules.Observe(Frame(2, process), network: network with { Quality = quality }), Is.Empty);
        Assert.That(rules.Observe(Frame(3, process), network: network), Is.Empty);
        Assert.That(rules.Observe(Frame(4, process), network: network).Single().Run, Is.EqualTo(new[] { 8d, 8d }));
    }

    [TestCase(CaptureMetric.ApplicationNetwork)]
    [TestCase(CaptureMetric.ServiceNetwork)]
    public void NetworkRulesResumeAfterDisconnectWithIncompleteSessionTotals(CaptureMetric metric)
    {
        var rules = new CaptureRules([new CaptureRuleSetting(metric, true, 1, 2)
            { Target = metric == CaptureMetric.ServiceNetwork ? "svc" : "worker" }], TimeSpan.Zero, 4);
        var session = new ProcessNetworkSession();
        var generation = Guid.NewGuid();
        ProcessNetworkSnapshot Snapshot(int sequence, long stamp) => new(generation, sequence, stamp, 10,
            stamp - 5, stamp, ProcessNetworkQuality.Provisional, 0, 0, [new(1, 1, sequence * 100, 0, 1_000_000, 0)]);
        IReadOnlyList<CaptureFired> Observe(int second, ProcessNetworkFrame network)
        {
            var sample = Frame(second, Process(1, 1, "worker"));
            var services = new ServiceFrame(sample, [new ServiceGroup("host",
                [new ServiceObservation("svc", "Service", 4, 1, 0)], sample.Processes[0], sample.Processes)]);
            return rules.Observe(sample, services, network);
        }
        session.Add(Snapshot(1, 100), 100);
        Assert.That(Observe(1, session.Add(Snapshot(2, 110), 110)), Is.Empty);
        Assert.That(Observe(2, session.Unavailable(120, 10)), Is.Empty);
        var recovered = session.Add(Snapshot(3, 200), 200);
        Assert.That(recovered.Quality.HasFlag(ProcessNetworkQuality.Incomplete), Is.True);
        Assert.That(Observe(3, recovered), Is.Empty, "The disconnect breaks the prior run.");
        Assert.That(Observe(4, session.Add(Snapshot(4, 210), 210)).Single().Run, Is.EqualTo(new[] { 8d, 8d }));
    }

    [TestCase(null, 1_000_000d, ProcessNetworkQuality.None)]
    [TestCase(1_000_000d, null, ProcessNetworkQuality.None)]
    [TestCase(null, 1_000_000d, ProcessNetworkQuality.Incomplete)]
    [TestCase(1_000_000d, null, ProcessNetworkQuality.Incomplete)]
    public void NetworkRulesRequireBothDirectionsForEveryMatchingMember(double? receive, double? send,
        ProcessNetworkQuality quality)
    {
        var rules = new CaptureRules([new CaptureRuleSetting(CaptureMetric.ApplicationNetwork, true, 1, 1)
            { Target = "worker" }], TimeSpan.Zero, 4);
        var network = new ProcessNetworkFrame(1, 1, quality,
            [new ProcessNetworkReading(1, 1, 0, 0, 1_000_000, 0), new ProcessNetworkReading(2, 2, 0, 0, receive, send)]);
        Assert.That(rules.Observe(Frame(1, Process(1, 1, "worker"), Process(2, 2, "worker")), network: network), Is.Empty);
    }

    [Test]
    public void ServiceNetworkAggregatesHostAndWorkersUsingTheBusierCombinedDirection()
    {
        var rules = new CaptureRules([new CaptureRuleSetting(CaptureMetric.ServiceNetwork, true, 10, 1)
            { Target = "SVC" }], TimeSpan.Zero, 4);
        var sample = Frame(1, Process(1, 1, "host"), Process(2, 2, "worker"));
        var services = new ServiceFrame(sample, [new ServiceGroup("host",
            [new ServiceObservation("svc", "Service", 4, 1, 0)], sample.Processes[0], sample.Processes)]);
        var network = new ProcessNetworkFrame(1, 1, ProcessNetworkQuality.None,
            [new ProcessNetworkReading(1, 1, 0, 0, 1_000_000, 0), new ProcessNetworkReading(2, 2, 0, 0, 0, 2_000_000)]);

        Assert.That(rules.Observe(sample, services with { Available = false }, network), Is.Empty);
        Assert.That(rules.Observe(sample, services, network).Single().PeakValue, Is.EqualTo(16));
    }
}
