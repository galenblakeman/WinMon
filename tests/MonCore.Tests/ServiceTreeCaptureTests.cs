using System.Diagnostics;
using System.Text.Json;
using NUnit.Framework;
using WinMon.Monitoring;

namespace WinMon.Tests;

[TestFixture]
public sealed class ServiceTreeCaptureTests
{
    private static ProcessUsage Process(int id, long created, int parent = 0) =>
        new(id, created, "p" + id, id / 10d, IoBytesPerSecond: id) { ParentProcessId = parent, PrivateBytes = (ulong)id };
    private static ServiceObservation Service(string name, int pid, uint state = 4) => new(name, name, state, pid, 1);
    private static MonitorSample Sample(int second, params ProcessUsage[] processes) =>
        new(DateTimeOffset.UnixEpoch.AddSeconds(second), 40, processes, 0, false, second * Stopwatch.Frequency);
    private static ServiceCatalog Catalog(params ServiceObservation[] services) => new(1000, services);

    [TestCase(-1, false)]
    [TestCase(0, false)]
    [TestCase(1000, true)]
    [TestCase(1001, false)]
    public void Capture_HostCreationMustBePositiveAndNoLaterThanTheCatalog(long created, bool accepted)
    {
        var frame = new ServiceTree().Capture(Sample(1, Process(30, 1100, 10), Process(10, created)), Catalog(Service("agent", 10)));
        var group = frame.Groups.Single();
        Assert.That(group.Host is not null, Is.EqualTo(accepted));
        Assert.That(group.Key, Is.EqualTo(accepted ? "host:10:1000" : "pending:10"));
        Assert.That(group.Processes.Select(p => p.ProcessId), Is.EqualTo(accepted ? new[] { 10, 30 } : []));
    }

    [TestCase(0, 100, false)]
    [TestCase(100, 0, false)]
    [TestCase(100, 100, false)]
    [TestCase(101, 100, false)]
    [TestCase(99, 100, true)]
    public void Capture_AncestryRequiresStrictlyOlderPositiveParentTokens(long parent, long child, bool included)
    {
        var group = new ServiceTree().Capture(Sample(1, Process(10, 50), Process(20, parent, 10), Process(30, child, 20)),
            Catalog(Service("agent", 10))).Groups.Single();
        Assert.That(group.Processes.Any(p => p.ProcessId == 30), Is.EqualTo(included));
        Assert.That(group.Host!.CreationTicks, Is.EqualTo(50));
    }

    [TestCase(0u, false)]
    [TestCase(1u, false)]
    [TestCase(2u, false)]
    [TestCase(3u, false)]
    [TestCase(4u, true)]
    [TestCase(5u, true)]
    [TestCase(6u, true)]
    [TestCase(7u, true)]
    [TestCase(8u, false)]
    public void Capture_ServiceStatePreservesHostEligibility(uint state, bool hosted)
    {
        var group = new ServiceTree().Capture(Sample(1, Process(10, 100)), Catalog(Service("agent", 10, state))).Groups.Single();
        Assert.That(group.Host is not null, Is.EqualTo(hosted));
        Assert.That(group.Key, Is.EqualTo(hosted ? "host:10:100" : "service:agent"));
        Assert.That(group.Processes.Select(p => p.ProcessId), Is.EqualTo(hosted ? new[] { 10 } : []));
    }

    [TestCase(-1)]
    [TestCase(0)]
    public void Capture_NonpositiveServicePidCannotOwnAProcess(int pid)
    {
        var group = new ServiceTree().Capture(Sample(1, Process(pid, 100)), Catalog(Service("agent", pid))).Groups.Single();
        Assert.That(group.Key, Is.EqualTo("service:agent"));
        Assert.That(group.Host, Is.Null);
        Assert.That(group.Processes, Is.Empty);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Capture_CachedOwnerWinsUntilTheWorkerLifetimeChanges(bool reusedWorker)
    {
        var tree = new ServiceTree();
        var original = Service("original", 10);
        var introduced = Service("introduced", 20);
        tree.Capture(Sample(1, Process(10, 100), Process(20, 110, 10), Process(30, 120, 20)), Catalog(original));
        var frame = tree.Capture(Sample(2, Process(30, reusedWorker ? 121 : 120, 20), Process(20, 110, 10), Process(10, 100)),
            Catalog(original, introduced));
        Assert.That(frame.Groups.Single(g => g.Services[0].Name == "original").Processes.Select(p => p.ProcessId),
            Is.EqualTo(reusedWorker ? new[] { 10 } : [10, 30]));
        Assert.That(frame.Groups.Single(g => g.Services[0].Name == "introduced").Processes.Select(p => p.ProcessId),
            Is.EqualTo(reusedWorker ? new[] { 20, 30 } : [20]));
    }

    [TestCase("reused")]
    [TestCase("missing")]
    [TestCase("stale")]
    public void Capture_CachedOwnershipRequiresTheSameCurrentHostLifetime(string change)
    {
        var tree = new ServiceTree();
        var catalog = Catalog(Service("agent", 10));
        tree.Capture(Sample(1, Process(10, 100), Process(30, 200, 10)), catalog);
        var next = change == "missing" ? Sample(2, Process(30, 200, 999))
            : Sample(2, Process(10, change == "reused" ? 101 : 100), Process(30, 200, 999));
        var frame = tree.Capture(next, change == "stale" ? catalog with { ObservedFileTime = 99 } : catalog);
        Assert.That(frame.Groups.Single().Processes.Select(p => p.ProcessId), Is.EqualTo(change == "reused" ? new[] { 10 } : []));
        var healthy = tree.Capture(Sample(3, Process(10, 101), Process(30, 200, 10)), catalog);
        Assert.That(healthy.Groups.Single().Processes.Select(p => p.ProcessId), Is.EqualTo([10, 30]));
        Assert.That(frame.Groups.Single().Processes.Any(p => p.ProcessId == 30), Is.False);
    }

    [TestCase("duplicate")]
    [TestCase("process")]
    [TestCase("processes")]
    [TestCase("service")]
    [TestCase("services")]
    [TestCase("sample")]
    public void Capture_InvalidInputKeepsReturnedFramesStableAndTheNextCaptureRecovers(string failure)
    {
        var tree = new ServiceTree();
        var service = Service("original", 10);
        var previous = tree.Capture(Sample(1, Process(10, 100), Process(20, 110, 10), Process(30, 120, 20)), Catalog(service));
        var snapshot = JsonSerializer.Serialize(previous);
        var badSample = failure switch
        {
            "duplicate" => Sample(2, Process(10, 100), Process(10, 101)),
            "process" => Sample(2, Process(10, 100), null!),
            "processes" => Sample(2) with { Processes = null! },
            "sample" => null!,
            _ => Sample(2, Process(10, 100))
        };
        var badCatalog = failure switch
        {
            "service" => Catalog(service, null!),
            "services" => new ServiceCatalog(1000, null!),
            _ => Catalog(service)
        };
        Assert.That(() => tree.Capture(badSample, badCatalog), failure == "duplicate"
            ? Throws.TypeOf<ArgumentException>() : Throws.TypeOf<NullReferenceException>());
        var recovered = tree.Capture(Sample(3, Process(100, 1000, 99), Process(99, 990)), Catalog(Service("healthy", 99)));
        Assert.That(recovered.Groups.Single().Processes.Select(p => p.ProcessId), Is.EqualTo([99, 100]));
        Assert.That(recovered.Groups.Single().Services.Single().Name, Is.EqualTo("healthy"));
        Assert.That(JsonSerializer.Serialize(previous), Is.EqualTo(snapshot));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Capture_UnavailableFramesPreserveSampleMetadataWithoutReadingProcessesAndForgetOwners(bool gap)
    {
        var tree = new ServiceTree();
        var catalog = Catalog(Service("agent", 10));
        tree.Capture(Sample(1, Process(10, 100), Process(30, 200, 10)), catalog);
        var sample = Sample(2) with { Processes = null!, IsGap = gap, CompactedProcesses = 7,
            Resources = new ResourceSample { UsedMemoryBytes = 123 } };
        var unavailable = tree.Capture(sample, gap ? catalog : null);
        Assert.Multiple(() =>
        {
            Assert.That(unavailable.Available, Is.False);
            Assert.That(unavailable.Groups, Is.Empty);
            Assert.That(unavailable.Sample.Processes, Is.Empty);
            Assert.That(unavailable.Sample.Resources, Is.SameAs(sample.Resources));
            Assert.That(unavailable.Sample.Timestamp, Is.EqualTo(sample.Timestamp));
            Assert.That(unavailable.Sample.MonotonicTimestamp, Is.EqualTo(sample.MonotonicTimestamp));
            Assert.That(unavailable.Sample.CompactedProcesses, Is.EqualTo(7));
        });
        var resumed = tree.Capture(Sample(3, Process(10, 100), Process(30, 200, 999)), catalog);
        Assert.That(resumed.Groups.Single().Processes.Select(p => p.ProcessId), Is.EqualTo([10]));
    }

    [Test]
    public void Capture_MixedGroupsKeepOrderingAndDistinctCacheReuseRules()
    {
        var idleA = Service("idle-a", 0, 1);
        var idleB = Service("idle-b", 0, 1);
        var a = Service("a", 10);
        var b = Service("b", 10);
        var catalog = Catalog(idleA, Service("pending", 50), a, b, idleB);
        var tree = new ServiceTree();
        var sample = Sample(1, Process(30, 120, 20), Process(10, 100), Process(20, 110, 10));
        var first = tree.Capture(sample, catalog);
        var next = tree.Capture(sample with { MonotonicTimestamp = 2 * Stopwatch.Frequency }, catalog);
        Assert.That(next.Groups.Select(g => g.Key), Is.EqualTo(new[] { "pending:50", "host:10:100", "service:idle-a", "service:idle-b" }));
        Assert.That(next.Groups[1].Processes.Select(p => p.ProcessId), Is.EqualTo([10, 20, 30]));
        Assert.That(next.Groups[1].Services.Select(s => s.Name), Is.EqualTo(["a", "b"]));
        Assert.That(next.Groups[0], Is.SameAs(first.Groups[0]));
        Assert.That(next.Groups[2], Is.SameAs(first.Groups[2]));
        Assert.That(next.Groups[1], Is.Not.SameAs(first.Groups[1]));
        Assert.That(next.Groups[1].Key, Is.SameAs(first.Groups[1].Key));
        Assert.That(next.Groups[1].Services, Is.SameAs(first.Groups[1].Services));
        Assert.That(next.Groups[1].Processes, Is.Not.SameAs(first.Groups[1].Processes));
        var equalButNew = catalog with { Services = [idleA, catalog.Services[1], a with { }, b, idleB] };
        var changed = tree.Capture(sample, equalButNew);
        Assert.That(changed.Groups[1].Services, Is.Not.SameAs(next.Groups[1].Services));
    }

    [Test]
    public void Capture_ValueEqualStoppedObservationsRemainSeparateReferenceKeys()
    {
        var a = Service("idle", 0, 1);
        var b = a with { };
        var tree = new ServiceTree();
        var first = tree.Capture(Sample(1), Catalog(a, b));
        var next = tree.Capture(Sample(2), Catalog(a, b));
        Assert.That(next.Groups, Has.Count.EqualTo(2));
        Assert.That(next.Groups[0], Is.SameAs(first.Groups[0]));
        Assert.That(next.Groups[1], Is.SameAs(first.Groups[1]));
        Assert.That(next.Groups[0], Is.Not.SameAs(next.Groups[1]));
        Assert.That(next.Index.IndexOf("service:idle"), Is.EqualTo(0));
    }
}
