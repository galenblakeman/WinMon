using System.Diagnostics;
using NUnit.Framework;
using WinMon.Monitoring;

namespace WinMon.Tests;

[TestFixture]
public sealed class ServiceIntegrationTests
{
    [Test]
    public void ExecuteAsync_RejectsAnUnsupportedActionBeforeOpeningTheServiceManager()
    {
        Assert.That(async () => await WindowsServices.ExecuteAsync("irrelevant", "unsupported", 0, 0, CancellationToken.None),
            Throws.TypeOf<ArgumentException>());
    }

    [Test]
    public void Sampler_CarriesTheNativeParentPidIntoUsageForServiceTreeAttribution()
    {
        using var sampler = new WindowsCpuSampler(new Snapshots(
            Snapshot(1, new ProcessCpuTimes(10, 100, "host", 100), new ProcessCpuTimes(11, 110, "worker", 100) { ParentProcessId = 10 }),
            Snapshot(2, new ProcessCpuTimes(10, 100, "host", 200), new ProcessCpuTimes(11, 110, "worker", 200) { ParentProcessId = 10 })));

        _ = sampler.Sample();
        var sample = sampler.Sample();

        Assert.That(sample.Processes.Single(p => p.ProcessId == 11).ParentProcessId, Is.EqualTo(10));
    }

    private static CpuSnapshot Snapshot(int second, params ProcessCpuTimes[] processes) => new(
        DateTimeOffset.UnixEpoch.AddSeconds(second), second * Stopwatch.Frequency,
        new SystemCpuTimes(second * 100, second * 1_000, 0), processes, 0);

    private sealed class Snapshots(params CpuSnapshot[] values) : ICpuSnapshotProvider
    {
        private readonly Queue<CpuSnapshot> _values = new(values);
        public CpuSnapshot Capture() => _values.Dequeue();
    }
}
