using NUnit.Framework;
using WinMon.Controls;
using WinMon.Monitoring;

namespace WinMon.Tests;

[TestFixture]
public sealed class GpuSamplingTests
{
    [Test]
    public void Aggregation_ReusedStorageDropsOldEnginesAndPreservesReturnedSnapshots()
    {
        var aggregation = new WindowsGpuSampler.EngineAggregation();
        var first = WindowsGpuSampler.Parse([Counter(1, "1234", 0, 90), Counter(2, "1234", 1, 80)]);
        var original = aggregation.ByProcess(first);
        Assert.That(aggregation.Busiest(first)!.Percent, Is.EqualTo(90));

        var next = WindowsGpuSampler.Parse([Counter(2, "5678", 0, 10)]);
        var current = aggregation.ByProcess(next);
        Assert.Multiple(() =>
        {
            Assert.That(current.Keys, Is.EquivalentTo(new[] { 2 }));
            Assert.That(current[2]!.Percent, Is.EqualTo(10));
            Assert.That(aggregation.Busiest(next)!.Percent, Is.EqualTo(10));
            Assert.That(original[1]!.Percent, Is.EqualTo(90));
            Assert.That(original[2]!.Percent, Is.EqualTo(80));
        });

        var invalid = WindowsGpuSampler.Parse([Counter(2, "5678", 0, 90, 0xC0000BC6)]);
        Assert.That(aggregation.ByProcess(invalid)[2], Is.Null);
        Assert.That(aggregation.Busiest(invalid), Is.Null);
        Assert.That(aggregation.ByProcess([]), Is.Empty);
        Assert.That(aggregation.Busiest([]), Is.Null);
    }

    private static WindowsGpuSampler.EngineCounter Counter(int pid, string adapter, int engine, double value, uint status = 0,
        string kind = "3D") => new($"pid_{pid}_luid_0x00000000_0x{adapter}_phys_0_eng_{engine}_engtype_{kind}", value, status);

    [Test]
    public void Usage_SumsProcessesWithinPhysicalEngine_ThenSelectsBusiestAcrossAdapters()
    {
        var result = WindowsGpuSampler.Aggregate([
            Counter(1, "1234", 0, 30), Counter(2, "1234", 0, 40),
            Counter(1, "1234", 1, 60), Counter(3, "5678", 0, 65)]);
        Assert.Multiple(() =>
        {
            Assert.That(result!.Percent, Is.EqualTo(70));
            Assert.That(result.Adapter, Is.EqualTo("0x00000000_0x1234/0"));
        });
    }

    [Test]
    public void Usage_DistinguishesEnginesWithSameType_AndPhysicalAdapters()
    {
        var result = WindowsGpuSampler.Aggregate([
            Counter(1, "1234", 0, 40), Counter(1, "1234", 1, 45),
            new WindowsGpuSampler.EngineCounter("pid_1_luid_0x00000000_0x1234_phys_1_eng_0_engtype_Compute", 60)]);
        Assert.Multiple(() =>
        {
            Assert.That(result!.Percent, Is.EqualTo(60));
            Assert.That(result.Adapter, Does.EndWith("/1"));
            Assert.That(result.Engine, Is.EqualTo("Compute"));
        });
    }

    [Test]
    public void Usage_InvalidContributionInvalidatesEngine_WithoutStoppingOtherEngines()
    {
        var result = WindowsGpuSampler.Aggregate([
            Counter(1, "1234", 0, 90), Counter(2, "1234", 0, 10, 0xC0000BC6),
            Counter(3, "1234", 1, 25, kind: "VideoDecode"),
            Counter(4, "5678", 0, double.NaN), new WindowsGpuSampler.EngineCounter("unknown", 99)]);
        Assert.Multiple(() =>
        {
            Assert.That(result!.Percent, Is.EqualTo(25));
            Assert.That(result.Engine, Is.EqualTo("VideoDecode"));
        });
    }

    [Test]
    public void Usage_MissingIsNull_IdleIsZero_AndOvershootIsClamped()
    {
        Assert.Multiple(() =>
        {
            Assert.That(WindowsGpuSampler.Aggregate([]), Is.Null);
            Assert.That(WindowsGpuSampler.Aggregate([Counter(1, "1234", 0, -1)]), Is.Null);
            Assert.That(WindowsGpuSampler.Aggregate([Counter(1, "1234", 0, 0, 1)])!.Percent, Is.Zero);
            Assert.That(WindowsGpuSampler.Aggregate([Counter(1, "1234", 0, 101)])!.Percent, Is.EqualTo(100));
        });
    }

    [Test]
    public void ProcessCounters_SelectBusiestEnginePerPid_WithoutMixingProcesses()
    {
        var result = WindowsGpuSampler.AggregateProcesses([
            Counter(1, "1234", 0, 30), Counter(1, "1234", 1, 50),
            Counter(2, "1234", 0, 60), Counter(1, "5678", 0, 45)]);
        Assert.Multiple(() =>
        {
            Assert.That(result[1]!.Percent, Is.EqualTo(50));
            Assert.That(result[2]!.Percent, Is.EqualTo(60));
        });
    }

    [TestCase("pid_1234_luid_0x00000000_0x0000D1E8_phys_0_eng_3_engtype_VideoDecode")]
    [TestCase("PID_7_LUID_0xABC_0XdeF_PHYS_12_ENG_0_ENGTYPE_3D")]
    [TestCase("pid_99999999999_luid_0x0_0x1_phys_0_eng_0_engtype_Copy")]
    [TestCase("pid_1_luid_0x0_0x1_phys_0_eng_0_engtype_Video Encode_eng_9")]
    [TestCase("pid_1_luid_0x0_0x1_phys_0_eng_0_engtype_3D\n")]
    [TestCase("pid_1_luid_0x0_0x1_phys_0_eng_0_engtype_3D\nmore")]
    [TestCase("pid_1_luid_0x0_0x1_phys_0_eng_0_engtype_")]
    [TestCase("pid__luid_0x0_0x1_phys_0_eng_0_engtype_3D")]
    [TestCase("pid_1_luid_0x_0x1_phys_0_eng_0_engtype_3D")]
    [TestCase("pid_1_luid_0xg_0x1_phys_0_eng_0_engtype_3D")]
    [TestCase("pid_1_luid_0x0_0x1_phys_a_eng_0_engtype_3D")]
    [TestCase("xpid_1_luid_0x0_0x1_phys_0_eng_0_engtype_3D")]
    [TestCase("unknown")]
    [TestCase("")]
    public void InstanceParsing_MatchesTheInstanceNamePattern(string name)
    {
        // The pattern the parser replaced; named groups number after unnamed ones.
        var match = System.Text.RegularExpressions.Regex.Match(name,
            @"^pid_(?<pid>\d+)_luid_(0x[0-9a-f]+_0x[0-9a-f]+)_phys_(\d+)_eng_(\d+)_engtype_(.+)$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        var parsed = WindowsGpuSampler.ParseInstance(name);
        if (!match.Success)
        {
            Assert.That(parsed, Is.Null);
            return;
        }
        Assert.That(parsed, Is.EqualTo(new WindowsGpuSampler.EngineInstance(
            int.TryParse(match.Groups["pid"].Value, out var pid) ? pid : null,
            match.Groups[1].Value.ToLowerInvariant() + "/" + match.Groups[2].Value, match.Groups[3].Value, match.Groups[4].Value)));
    }

    [Test]
    public void InstanceParsing_ReusesNamesFromThePreviousPass_AndForgetsNamesThatDisappear()
    {
        var previous = new Dictionary<string, WindowsGpuSampler.EngineInstance?>();
        var next = new Dictionary<string, WindowsGpuSampler.EngineInstance?>();
        var first = WindowsGpuSampler.Parse([Counter(1, "1234", 0, 10), Counter(2, "1234", 0, 20), new WindowsGpuSampler.EngineCounter("unknown", 5)], previous, next);
        (previous, next) = (next, previous);
        var second = WindowsGpuSampler.Parse([Counter(1, "1234", 0, 30), new WindowsGpuSampler.EngineCounter("unknown", 5)], previous, next);
        Assert.Multiple(() =>
        {
            Assert.That(first.Select(r => r.Value), Is.EqualTo([10d, 20d]), "Unparsed names carry no reading.");
            Assert.That(second.Single().Instance, Is.SameAs(first[0].Instance));
            Assert.That(second.Single().Value, Is.EqualTo(30));
            Assert.That(next.Keys, Is.EquivalentTo([Counter(1, "1234", 0, 0).Name, "unknown"]));
        });
    }

    [Test]
    public void ProcessCounters_KeepAProcessWithOnlyInvalidEngines_AsUnreadable()
    {
        var result = WindowsGpuSampler.AggregateProcesses([Counter(1, "1234", 0, 10, 0xC0000BC6), Counter(2, "1234", 0, 5),
            new WindowsGpuSampler.EngineCounter("pid_99999999999_luid_0x0_0x1_phys_0_eng_0_engtype_3D", 80)]);
        Assert.Multiple(() =>
        {
            Assert.That(result.Keys, Is.EquivalentTo([1, 2]), "An out-of-range PID is not attributed to any process.");
            Assert.That(result[1], Is.Null);
            Assert.That(result[2]!.Percent, Is.EqualTo(5));
        });
    }

    [Test]
    public void Usage_BreaksTiesByAdapterThenEngine_AndComparesBeforeClamping()
    {
        Assert.Multiple(() =>
        {
            Assert.That(WindowsGpuSampler.Aggregate([Counter(1, "5678", 0, 40, kind: "Compute"), Counter(1, "1234", 1, 40, kind: "Copy"),
                    Counter(1, "1234", 0, 40)]),
                Is.EqualTo(new WindowsGpuSampler.GpuUsage("0x00000000_0x1234/0", "3D", 40)));
            Assert.That(WindowsGpuSampler.Aggregate([Counter(1, "1234", 0, 110), Counter(1, "5678", 0, 120, kind: "Compute")])!.Engine,
                Is.EqualTo("Compute"));
        });
    }

    [Test]
    public void ProcessAttribution_RequiresMatchingLifetimeAcrossBaselineAndCollection()
    {
        ProcessUsage[] processes = [new(1, 10, "stable", 1), new(2, 20, "reused", 1),
            new(3, 110, "new", 1), new(4, 40, "gone", 1), new(5, 50, "no counter", 1)];
        var readings = Enumerable.Range(1, 4).ToDictionary(i => i,
            i => (WindowsGpuSampler.GpuUsage?)new WindowsGpuSampler.GpuUsage("adapter", "3D", i * 10));
        var result = WindowsGpuSampler.AttachProcesses(processes, readings, 100,
            pid => pid switch { 1 => 10, 2 => 99, 3 => 110, _ => null });
        Assert.Multiple(() =>
        {
            Assert.That(result[0].GpuPercent, Is.EqualTo(10));
            Assert.That(result.Skip(1).Select(p => p.GpuPercent), Is.All.Null);
            Assert.That(result.Select(p => p.CreationTicks), Is.EqualTo(processes.Select(p => p.CreationTicks)));
        });
    }

    [Test]
    public void History_RetainsGpuValuesAndIdentity_AndExposesThemInGraphAndTooltip()
    {
        var first = new CpuSample(DateTimeOffset.UnixEpoch, 10, [], 0)
        {
            Resources = new ResourceSample { GpuPercent = 46.2, GpuId = "adapter-a", GpuEngine = "3D" }
        };
        var history = new CpuHistory();
        history.Add(first);
        history.Add(first with { Timestamp = first.Timestamp.AddSeconds(1), Resources = new ResourceSample() });
        Assert.Multiple(() =>
        {
            Assert.That(CpuGraph.MetricValue(history.Samples[0], 4), Is.EqualTo(46.2));
            Assert.That(CpuGraph.MetricValue(history.Samples[1], 4), Is.Null);
            Assert.That(history.Samples[0].Resources.GpuId, Is.EqualTo("adapter-a"));
            Assert.That(CpuGraph.DescribeSample(first), Does.Contain("GPU 46.2%"));
        });
    }
}
