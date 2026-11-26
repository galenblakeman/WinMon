using System.Diagnostics;
using MonCore.Monitoring;
using NUnit.Framework;

namespace MonCore.Tests;

[TestFixture]
public sealed class CaptureRuleCharacterizationTests
{
    private const ulong MiB = 1024 * 1024;
    private static readonly string WorkerPath = Path.Combine(Path.GetTempPath(), "capture-apps", "worker.exe");
    private static ProcessUsage Process(int id = 1, double cpu = 5) => new(id, id, "worker", cpu)
        { ImagePath = WorkerPath, PrivateBytes = 2 * MiB };
    private static MonitorSample Frame(int second, params ProcessUsage[] processes) =>
        new(DateTimeOffset.UnixEpoch.AddSeconds(second), 20, processes, 0, false, (second + 1) * Stopwatch.Frequency);
    private static ServiceFrame Services(MonitorSample sample, params ProcessUsage[] processes) =>
        new(sample, [new("host", [new("svc", "Service", 4, 1, 0)], processes.FirstOrDefault(), processes)]);
    private static ProcessNetworkFrame Network(params ProcessNetworkReading[] readings) =>
        new(1, 1, ProcessNetworkQuality.None, readings);
    private static ProcessNetworkReading Reading(int id = 1, double? receive = 1_000_000, double? send = 0) =>
        new(id, id, 0, 0, receive, send);
    private static CaptureRules Engine(CaptureMetric metric, double threshold = 1, int duration = 1, string? target = null,
        int logical = 4)
    {
        var rule = new CaptureRuleSetting(metric, true, threshold, duration);
        return new([rule with { Target = target ?? (rule.IsService ? "svc" : "worker") }], TimeSpan.Zero, logical);
    }

    [TestCase("name", 20d)]
    [TestCase("filename", 60d)]
    [TestCase("stem", 60d)]
    [TestCase("full", 20d)]
    [TestCase("different-full", null)]
    [TestCase("prefix", null)]
    [TestCase("relative", null)]
    public void Observe_ApplicationTargetsKeepExactCaseInsensitiveNameAndPathRules(string kind, double? expected)
    {
        var selected = Process() with { Name = "alias" };
        var sibling = Process(2, 10) with { Name = "unrelated", ImagePath = Path.Combine(Path.GetTempPath(), "other", "worker.exe") };
        var target = kind switch
        {
            "name" => "ALIAS", "filename" => "WORKER.EXE", "stem" => "WoRkEr", "full" => WorkerPath.ToUpperInvariant(),
            "different-full" => Path.Combine(Path.GetTempPath(), "absent", "worker.exe"),
            "prefix" => "work", _ => Path.Combine("relative", "worker.exe")
        };
        var result = Engine(CaptureMetric.ApplicationCpu, target: target).Observe(Frame(1, selected, sibling));
        if (expected is null) Assert.That(result, Is.Empty);
        else Assert.That(result.Single().PeakValue, Is.EqualTo(expected));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void Observe_EmptyNormalizedTargetsDoNotReadMalformedProcessCollections(string? target)
    {
        var rule = new CaptureRuleSetting(CaptureMetric.ApplicationCpu, true, 1, 1) { Target = target! };
        var sample = Frame(1) with { Processes = null! };
        Assert.That(new CaptureRules([rule], TimeSpan.Zero, 4).Observe(sample), Is.Empty);
    }

    [TestCase("missing-frame", false)]
    [TestCase("unavailable", false)]
    [TestCase("qpc", false)]
    [TestCase("wall", false)]
    [TestCase("absent-name", false)]
    [TestCase("shared", false)]
    [TestCase("empty", false)]
    [TestCase("pending", true)]
    [TestCase("offset", true)]
    [TestCase("valid", true)]
    public void Observe_ServiceSelectionPreservesExactFrameAlignmentAndGroupEligibility(string condition, bool fires)
    {
        var sample = Frame(1);
        var services = Services(sample, Process());
        services = condition switch
        {
            "missing-frame" => null!,
            "unavailable" => services with { Available = false, Sample = null!, Groups = null! },
            "qpc" => services with { Sample = sample with { MonotonicTimestamp = sample.MonotonicTimestamp + 1 } },
            "wall" => services with { Sample = sample with { Timestamp = sample.Timestamp.AddTicks(1) } },
            "offset" => services with { Sample = sample with { Timestamp = sample.Timestamp.ToOffset(TimeSpan.FromHours(2)) } },
            "absent-name" => services with { Groups = [] },
            "empty" => Services(sample),
            "pending" => services with { Groups = [services.Groups[0] with { Host = null }] },
            "shared" => services with { Groups = [services.Groups[0] with { Services = [.. services.Groups[0].Services, new("other", "Other", 4, 1, 0)] }] },
            _ => services
        };
        var result = Engine(CaptureMetric.ServiceCpu, target: "SVC").Observe(sample, services);
        if (fires) Assert.That(result.Single().PeakValue, Is.EqualTo(20));
        else Assert.That(result, Is.Empty);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Observe_FirstMatchingServiceGroupWinsEvenWhenALaterGroupIsExclusive(bool sharedFirst)
    {
        var sample = Frame(1, Process());
        var exclusive = Services(sample, Process()).Groups[0];
        var shared = exclusive with { Services = [.. exclusive.Services, new("other", "Other", 4, 1, 0)] };
        var services = new ServiceFrame(sample, sharedFirst ? [shared, exclusive] : [exclusive, shared]);
        var result = Engine(CaptureMetric.ServiceCpu).Observe(sample, services);
        if (sharedFirst) Assert.That(result, Is.Empty);
        else Assert.That(result.Single().PeakValue, Is.EqualTo(20));
    }

    [TestCase(CaptureMetric.Cpu, true, false, false, 20d)]
    [TestCase(CaptureMetric.Cpu, false, false, false, null)]
    [TestCase(CaptureMetric.Cpu, false, true, false, null)]
    [TestCase(CaptureMetric.Cpu, true, true, true, null)]
    [TestCase(CaptureMetric.Process, true, false, false, 20d)]
    [TestCase(CaptureMetric.Process, false, false, false, null)]
    [TestCase(CaptureMetric.Process, false, true, false, 20d)]
    [TestCase(CaptureMetric.Process, true, true, true, null)]
    [TestCase(CaptureMetric.Process, false, true, true, null)]
    [TestCase(CaptureMetric.ApplicationCpu, false, false, false, 20d)]
    [TestCase(CaptureMetric.ApplicationCpu, true, true, true, null)]
    [TestCase(CaptureMetric.ApplicationMemory, false, false, false, 2d)]
    [TestCase(CaptureMetric.ApplicationMemory, true, true, true, null)]
    [TestCase(CaptureMetric.ApplicationNetwork, false, false, false, 8d)]
    [TestCase(CaptureMetric.ApplicationNetwork, true, true, true, null)]
    [TestCase(CaptureMetric.ServiceCpu, false, false, false, 20d)]
    [TestCase(CaptureMetric.ServiceCpu, true, true, true, null)]
    [TestCase(CaptureMetric.ServiceMemory, false, false, false, 2d)]
    [TestCase(CaptureMetric.ServiceMemory, true, true, true, null)]
    [TestCase(CaptureMetric.ServiceNetwork, false, false, false, 8d)]
    [TestCase(CaptureMetric.ServiceNetwork, true, true, true, null)]
    public void Observe_MachineProcessAndTargetedRulesKeepDistinctValidityGates(CaptureMetric metric, bool machineKnown,
        bool independent, bool gap, double? expected)
    {
        var process = Process();
        var sample = Frame(1, process) with { TotalCpuPercent = machineKnown ? 20 : null,
            IndependentProcessReadings = independent, IsGap = gap };
        var result = Engine(metric).Observe(sample, Services(sample, process), Network(Reading()));
        if (expected is null) Assert.That(result, Is.Empty);
        else
        {
            var fired = result.Single();
            Assert.That(fired.PeakValue, Is.EqualTo(expected));
            Assert.That(fired.Fired, Is.SameAs(sample));
            Assert.That(fired.Process, metric == CaptureMetric.Process ? Is.SameAs(process) : Is.Null);
        }
    }

    [Test]
    public void Observe_ScopedCpuUsesTheConfiguredLogicalProcessorMultiplier(
        [Values(CaptureMetric.ApplicationCpu, CaptureMetric.ServiceCpu)] CaptureMetric metric, [Values(-1, 0, 1, 4)] int logical)
    {
        var process = Process();
        var sample = Frame(1, process);
        var result = Engine(metric, logical: logical).Observe(sample, Services(sample, process));
        if (logical <= 0) Assert.That(result, Is.Empty);
        else Assert.That(result.Single().PeakValue, Is.EqualTo(logical == 1 ? 5 : 20));
    }

    [Test]
    public void Observe_ScopedMetricThresholdIsStrict(
        [Values(CaptureMetric.ApplicationCpu, CaptureMetric.ApplicationMemory, CaptureMetric.ApplicationNetwork)] CaptureMetric metric,
        [Values(-1, 0, 1)] int offset)
    {
        var cpu = offset < 0 ? Math.BitDecrement(5d) : offset > 0 ? Math.BitIncrement(5d) : 5;
        var process = Process(cpu: cpu) with { PrivateBytes = (ulong)((long)(2 * MiB) + offset) };
        var threshold = metric == CaptureMetric.ApplicationMemory ? 2 : 20;
        var sample = Frame(1, process);
        var result = Engine(metric, threshold).Observe(sample, network: Network(Reading(receive: 2_500_000 + offset)));
        if (offset <= 0) Assert.That(result, Is.Empty);
        else Assert.That(result.Single().PeakValue, Is.GreaterThan(threshold));
    }

    [TestCase(ProcessNetworkQuality.None, true)]
    [TestCase(ProcessNetworkQuality.Provisional, true)]
    [TestCase(ProcessNetworkQuality.Incomplete, true)]
    [TestCase(ProcessNetworkQuality.Provisional | ProcessNetworkQuality.Incomplete, true)]
    [TestCase(ProcessNetworkQuality.WarmingUp, false)]
    [TestCase(ProcessNetworkQuality.CapacityExceeded, false)]
    [TestCase(ProcessNetworkQuality.Unavailable, false)]
    [TestCase(ProcessNetworkQuality.Discontinuity, false)]
    [TestCase((ProcessNetworkQuality)64, false)]
    public void Observe_ScopedNetworkPreservesItsQualityMask(ProcessNetworkQuality quality, bool allowed)
    {
        var network = Network(Reading()) with { Quality = quality };
        var result = Engine(CaptureMetric.ApplicationNetwork).Observe(Frame(1, Process()), network: network);
        if (allowed) Assert.That(result.Single().PeakValue, Is.EqualTo(8));
        else Assert.That(result, Is.Empty);
    }

    [TestCase("missing")]
    [TestCase("domain")]
    [TestCase("pid")]
    [TestCase("token")]
    [TestCase("receive")]
    [TestCase("send")]
    public void Observe_EachNetworkMemberRequiresWindowsIdentityAndBothRateDirections(string unavailable)
    {
        var second = Process(2);
        if (unavailable == "domain") second = second with { LifetimeDomain = ProcessLifetimeDomain.UnixMicroseconds };
        var reading = Reading(2);
        reading = unavailable switch
        {
            "pid" => reading with { ProcessId = 3 }, "token" => reading with { CreationTicks = 3 },
            "receive" => reading with { ReceiveBytesPerSecond = null }, "send" => reading with { SendBytesPerSecond = null },
            _ => reading
        };
        var network = Network(Reading(), reading);
        if (unavailable == "missing") network = network with { Processes = [Reading()] };
        Assert.That(Engine(CaptureMetric.ApplicationNetwork).Observe(Frame(1, Process(), second), network: network), Is.Empty);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Observe_FirstMatchingNetworkDuplicateWinsEvenIfItsRatesAreMissing(bool missingFirst)
    {
        var network = Network(Reading(receive: missingFirst ? null : 1_000_000), Reading(receive: 100_000_000));
        var result = Engine(CaptureMetric.ApplicationNetwork).Observe(Frame(1, Process()), network: network);
        if (missingFirst) Assert.That(result, Is.Empty);
        else Assert.That(result.Single().PeakValue, Is.EqualTo(8));
    }

    [Test]
    public void Observe_NetworkUsesTheBusierCombinedDirectionWithoutAligningNetworkClockMetadata()
    {
        var network = Network(Reading(receive: 1_000_000), Reading(2, 0, 2_000_000)) with
            { TimestampQpc = -100, Frequency = 0, WindowStartQpc = -1, WindowEndQpc = -1 };
        var result = Engine(CaptureMetric.ApplicationNetwork).Observe(Frame(1, Process(), Process(2)), network: network).Single();
        Assert.That(result.PeakValue, Is.EqualTo(16));
        Assert.That(result.Run, Is.EqualTo(new[] { 16d }));
    }

    [TestCase(double.NaN, false)]
    [TestCase(double.PositiveInfinity, true)]
    [TestCase(double.NegativeInfinity, false)]
    public void Observe_NonfiniteScopedCpuKeepsItsExistingComparisonBehavior(double cpu, bool fires)
    {
        var result = Engine(CaptureMetric.ApplicationCpu).Observe(Frame(1, Process(cpu: cpu)));
        if (fires) Assert.That(result.Single().PeakValue, Is.EqualTo(double.PositiveInfinity));
        else Assert.That(result, Is.Empty);
    }

    [TestCase(double.NaN, false)]
    [TestCase(double.PositiveInfinity, true)]
    public void Observe_ScopedNetworkDoesNotAddFiniteRateValidation(double receive, bool fires)
    {
        var result = Engine(CaptureMetric.ApplicationNetwork).Observe(Frame(1, Process()), network: Network(Reading(receive: receive)));
        if (fires) Assert.That(result.Single().PeakValue, Is.EqualTo(double.PositiveInfinity));
        else Assert.That(result, Is.Empty);
    }

    [Test]
    public void Observe_MissingNetworkBreaksATargetedRunWithoutCountingAsBelowThreshold()
    {
        var engine = Engine(CaptureMetric.ApplicationNetwork, 1, 2);
        Assert.That(engine.Observe(Frame(1, Process()), network: Network(Reading())), Is.Empty);
        Assert.That(engine.Observe(Frame(2, Process())), Is.Empty);
        Assert.That(engine.Observe(Frame(3, Process()), network: Network(Reading())), Is.Empty);
        Assert.That(engine.Observe(Frame(4, Process()), network: Network(Reading())).Single().Run, Is.EqualTo(new[] { 8d, 8d }));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Observe_MachineValuesAreStillEvaluatedBeforeTheValidityGate(bool gap)
    {
        var sample = Frame(1) with { IsGap = gap, TotalCpuPercent = null, Resources = null! };
        Assert.That(() => Engine(CaptureMetric.Memory).Observe(sample), Throws.TypeOf<NullReferenceException>());
    }

    [Test]
    public void Observe_TargetedRearmingMissingDataCooldownAndPeakSnapshotsStayIndependent()
    {
        var rule = new CaptureRuleSetting(CaptureMetric.ApplicationCpu, true, 20, 2) { Target = "worker" };
        var engine = new CaptureRules([rule], TimeSpan.FromSeconds(10), 4);
        Assert.That(engine.Observe(Frame(1, Process(cpu: 6))), Is.Empty);
        var first = engine.Observe(Frame(2, Process(cpu: 7))).Single();
        Assert.That(first.Run, Is.EqualTo(new[] { 24d, 28d }));
        Assert.That(engine.Observe(Frame(3, Process(cpu: 3))), Is.Empty);
        Assert.That(engine.Observe(Frame(4, Process() with { CpuAvailable = false })), Is.Empty);
        Assert.That(engine.Observe(Frame(5, Process(cpu: 3))), Is.Empty);
        Assert.That(engine.Observe(Frame(6, Process(cpu: 3))), Is.Empty);
        Assert.That(engine.Observe(Frame(7, Process(cpu: 6))), Is.Empty);
        var peak = Frame(8, Process(cpu: 8));
        Assert.That(engine.Observe(peak), Is.Empty);
        Assert.That(engine.Observe(Frame(9, Process(cpu: 7))), Is.Empty);
        var final = Frame(12, Process(cpu: 6));
        var fired = engine.Observe(final).Single();
        Assert.Multiple(() =>
        {
            Assert.That(fired.Fired, Is.SameAs(final));
            Assert.That(fired.Peak, Is.SameAs(peak));
            Assert.That(fired.PeakValue, Is.EqualTo(32));
            Assert.That(fired.Run, Is.EqualTo(new[] { 24d, 32d, 28d, 24d }));
            Assert.That(fired.Rule, Is.EqualTo(rule));
            Assert.That(fired.Process, Is.Null);
            Assert.That(first.Run, Is.EqualTo(new[] { 24d, 28d }));
        });
    }

    [TestCase(9)]
    [TestCase(10)]
    [TestCase(100)]
    public void Observe_DuplicateBackwardAndSparseMomentsStillCountQualifyingSamples(int second)
    {
        var engine = Engine(CaptureMetric.ApplicationCpu, 20, 2);
        Assert.That(engine.Observe(Frame(10, Process(cpu: 6))), Is.Empty);
        var sample = Frame(second, Process(cpu: 7));
        var result = engine.Observe(sample).Single();
        Assert.That(result.Run, Is.EqualTo(new[] { 24d, 28d }));
        Assert.That(result.Fired, Is.SameAs(sample));
    }

    [Test]
    public void Observe_ScopedTargetsKeepSeparateRunsAndCooldownsForTheSameMetric()
    {
        var a = new CaptureRuleSetting(CaptureMetric.ApplicationCpu, true, 10, 2) { Target = "a" };
        var b = a with { Target = "b" };
        var engine = new CaptureRules([a, b], TimeSpan.FromMinutes(1), 4);
        var processA = Process() with { Name = "a" };
        var processB = Process(2, 6) with { Name = "b" };
        Assert.That(engine.Observe(Frame(1, processA, processB)), Is.Empty);
        var resultA = engine.Observe(Frame(2, processA)).Single();
        Assert.That(engine.Observe(Frame(3, processA, processB)), Is.Empty);
        var resultB = engine.Observe(Frame(4, processA, processB)).Single();
        Assert.Multiple(() =>
        {
            Assert.That(resultA.Rule, Is.EqualTo(a));
            Assert.That(resultA.Run, Is.EqualTo(new[] { 20d, 20d }));
            Assert.That(resultB.Rule, Is.EqualTo(b));
            Assert.That(resultB.Run, Is.EqualTo(new[] { 24d, 24d }));
        });
    }

    [Test]
    public void Observe_PreservesMixedRuleOrderAndProcessAttachment()
    {
        CaptureRuleSetting[] rules = [new(CaptureMetric.Process, true, 1, 1),
            new(CaptureMetric.ApplicationCpu, true, 1, 1) { Target = "worker" }, new(CaptureMetric.Cpu, true, 1, 1)];
        var process = Process();
        var sample = Frame(1, process);
        var result = new CaptureRules(rules, TimeSpan.Zero, 4).Observe(sample);
        Assert.That(result.Select(f => f.Rule.Metric), Is.EqualTo(rules.Select(r => r.Metric)));
        Assert.That(result.Select(f => f.Process), Is.EqualTo(new ProcessUsage?[] { process, null, null }));
        Assert.That(result.Select(f => f.PeakValue), Is.EqualTo(new[] { 20d, 20d, 20d }));
    }

    [TestCase("processes", false)]
    [TestCase("processes", true)]
    [TestCase("process", false)]
    [TestCase("process", true)]
    [TestCase("service-members", false)]
    [TestCase("service-members", true)]
    [TestCase("service-sample", false)]
    [TestCase("service-sample", true)]
    [TestCase("network", false)]
    [TestCase("network", true)]
    public void Observe_MalformedTargetDataIsStillEvaluatedBeforeTheGapGate(string invalid, bool gap)
    {
        var sample = Frame(1, Process()) with { IsGap = gap };
        var services = Services(sample, Process());
        var network = Network(Reading());
        var metric = invalid.StartsWith("service", StringComparison.Ordinal) ? CaptureMetric.ServiceCpu
            : invalid == "network" ? CaptureMetric.ApplicationNetwork : CaptureMetric.ApplicationCpu;
        if (invalid == "processes") sample = sample with { Processes = null! };
        if (invalid == "process") sample = sample with { Processes = [null!] };
        if (invalid == "service-members") services = services with { Groups = [services.Groups[0] with { Processes = null! }] };
        if (invalid == "service-sample") services = services with { Sample = null! };
        if (invalid == "network") network = network with { Processes = null! };
        Assert.That(() => Engine(metric).Observe(sample, services, network), invalid is "processes" or "network"
            ? Throws.TypeOf<ArgumentNullException>() : Throws.TypeOf<NullReferenceException>());
    }

    [Test]
    public void Observe_NullSampleRetainsItsArgumentExceptionAndDoesNotBreakAnExistingRun()
    {
        var engine = Engine(CaptureMetric.ApplicationCpu, 10, 2);
        Assert.That(engine.Observe(Frame(1, Process())), Is.Empty);
        Assert.That(() => engine.Observe(null!), Throws.TypeOf<ArgumentNullException>().With.Property("ParamName").EqualTo("full"));
        Assert.That(engine.Observe(Frame(2, Process())).Single().Run, Is.EqualTo(new[] { 20d, 20d }));
    }
}
