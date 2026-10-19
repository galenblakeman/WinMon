using NUnit.Framework;
using WinMon.Controls;
using WinMon.Monitoring;

namespace WinMon.Tests;

[TestFixture]
public sealed class GpuSamplingTests
{
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
