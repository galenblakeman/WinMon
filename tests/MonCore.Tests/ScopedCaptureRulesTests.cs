using NUnit.Framework;
using WinMon.Monitoring;

namespace WinMon.Tests;

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
        Assert.That(rules.Observe(sample, network: valid with { Quality = ProcessNetworkQuality.Incomplete }), Is.Empty);
        Assert.That(rules.Observe(sample, network: valid).Single().PeakValue, Is.EqualTo(8));
    }
}
