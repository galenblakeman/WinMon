using System.Diagnostics;
using NUnit.Framework;
using WinMon.Monitoring;

namespace WinMon.Tests;

[TestFixture]
public sealed class ServiceTreeTests
{
    [Test]
    public void Capture_AggregatesDedicatedHostAndItsLiveProcessTree()
    {
        var frame = new ServiceTree().Capture(Sample(1,
            Usage(10, 100, 0, 1, 100, 2),
            Usage(11, 110, 10, 2, 200, 3),
            Usage(12, 120, 11, 3, 300, 4)), Catalog(1_000, Service("agent", 10)));

        var group = frame.Groups.Single();

        Assert.Multiple(() =>
        {
            Assert.That(group.Processes.Select(p => p.ProcessId), Is.EqualTo([10, 11, 12]));
            Assert.That(group.Metrics(workers: false), Is.EqualTo(new ServiceMetrics(1, 100, 2)));
            Assert.That(group.Metrics(workers: true), Is.EqualTo(new ServiceMetrics(6, 600, 9)));
        });
    }

    [Test]
    public void Capture_SharedHostCountsMetricsOnceAndDoesNotAttributeThemToIndividualServices()
    {
        var frame = new ServiceTree().Capture(Sample(1,
            Usage(10, 100, 0, 4, 400, 8),
            Usage(11, 110, 10, 2, 200, 4)), Catalog(1_000, Service("alpha", 10), Service("beta", 10)));

        var group = frame.Groups.Single();

        Assert.Multiple(() =>
        {
            Assert.That(group.Shared, Is.True);
            Assert.That(group.Services.Select(s => s.Name), Is.EqualTo(["alpha", "beta"]));
            Assert.That(group.Processes.Select(p => p.ProcessId), Is.EqualTo([10, 11]));
            Assert.That(group.Metrics(workers: true), Is.EqualTo(new ServiceMetrics(6, 600, 12)),
                "The one host tree supplies one total shared by the services, never a duplicated per-service total.");
        });
    }

    [Test]
    public void Capture_StopsAtAnotherServiceRoot()
    {
        var frame = new ServiceTree().Capture(Sample(1,
            Usage(10, 100, 0),
            Usage(20, 110, 10),
            Usage(21, 120, 20),
            Usage(22, 130, 21)), Catalog(1_000, Service("first", 10), Service("second", 20)));

        Assert.Multiple(() =>
        {
            Assert.That(frame.Groups.Single(g => g.Services.Single().Name == "first").Processes.Select(p => p.ProcessId), Is.EqualTo([10]));
            Assert.That(frame.Groups.Single(g => g.Services.Single().Name == "second").Processes.Select(p => p.ProcessId), Is.EqualTo([20, 21, 22]));
        });
    }

    [Test]
    public void Capture_PreservesKnownOwnershipWhenAnIntermediateProcessExits()
    {
        var tree = new ServiceTree();
        var catalog = Catalog(1_000, Service("agent", 10));
        _ = tree.Capture(Sample(1, Usage(10, 100, 0), Usage(20, 110, 10), Usage(30, 120, 20)), catalog);

        var frame = tree.Capture(Sample(2, Usage(10, 100, 0), Usage(30, 120, 20)), catalog);

        Assert.That(frame.Groups.Single().Processes.Select(p => p.ProcessId), Is.EqualTo([10, 30]));
    }

    [Test]
    public void Capture_DoesNotFollowAParentPidWhoseCurrentLifetimeIsNewerThanTheChild()
    {
        var frame = new ServiceTree().Capture(Sample(1,
            Usage(10, 100, 0),
            Usage(20, 250, 10),
            Usage(30, 200, 20)), Catalog(1_000, Service("agent", 10), Service("recycled", 20)));

        Assert.Multiple(() =>
        {
            Assert.That(frame.Groups.Single(g => g.Services.Single().Name == "agent").Processes.Select(p => p.ProcessId), Is.EqualTo([10]));
            Assert.That(frame.Groups.Single(g => g.Services.Single().Name == "recycled").Processes.Select(p => p.ProcessId), Is.EqualTo([20]));
        });
    }

    [Test]
    public void Capture_RejectsAHostThatRestartedAfterTheCatalogWasObserved()
    {
        var tree = new ServiceTree();
        _ = tree.Capture(Sample(1, Usage(10, 900, 0), Usage(11, 910, 10)), Catalog(1_000, Service("agent", 10)));

        var frame = tree.Capture(Sample(2, Usage(10, 1_001, 0), Usage(11, 1_002, 10)), Catalog(1_000, Service("agent", 10)));

        Assert.Multiple(() =>
        {
            Assert.That(frame.Groups.Single().Host, Is.Null);
            Assert.That(frame.Groups.Single().Processes, Is.Empty);
        });
    }

    [Test]
    public void Capture_RejectsAStaleCatalogPidWithNoMatchingProcess()
    {
        var frame = new ServiceTree().Capture(Sample(1, Usage(11, 100, 0)), Catalog(1_000, Service("agent", 10)));

        Assert.Multiple(() =>
        {
            Assert.That(frame.Groups.Single().Key, Is.EqualTo("pending:10"));
            Assert.That(frame.Groups.Single().Host, Is.Null);
            Assert.That(frame.Groups.Single().Processes, Is.Empty);
        });
    }

    [Test]
    public void Capture_GapOrUnavailableCatalogMarksTheFrameUnavailableAndForgetsPriorOwnership()
    {
        var tree = new ServiceTree();
        var catalog = Catalog(1_000, Service("agent", 10));
        _ = tree.Capture(Sample(1, Usage(10, 100, 0), Usage(11, 110, 10)), catalog);

        var gap = tree.Capture(Sample(2, isGap: true, Usage(10, 100, 0)), catalog);
        var unavailable = tree.Capture(Sample(3, Usage(11, 110, 10)), null);
        var resumed = tree.Capture(Sample(4, Usage(10, 100, 0), Usage(11, 110, 99)), catalog);

        Assert.Multiple(() =>
        {
            Assert.That(gap.Available, Is.False);
            Assert.That(gap.Groups, Is.Empty);
            Assert.That(unavailable.Available, Is.False);
            Assert.That(resumed.Groups.Single().Processes.Select(p => p.ProcessId), Is.EqualTo([10]),
                "A worker cannot remain attributed after a collection gap erased its ancestry evidence.");
        });
    }

    [Test]
    public void ServiceHistory_RetainsAtMostTheLatest120SecondsAndFindsOnlyAnExactSample()
    {
        var history = new ServiceHistory();
        ServiceFrame? expected = null;
        for (var second = 0; second <= 121; second++)
        {
            var sample = Sample(second, monotonic: second + 10);
            var frame = new ServiceFrame(sample, []);
            history.Add(frame);
            if (second == 121) expected = frame;
        }

        var exact = Sample(121, monotonic: 131);
        var sameMonotonicDifferentTime = Sample(122, monotonic: 131);

        Assert.Multiple(() =>
        {
            Assert.That(history.Frames, Has.Count.EqualTo(ServiceHistory.Seconds + 1));
            Assert.That(history.Frames.First().Sample.Timestamp, Is.EqualTo(DateTimeOffset.UnixEpoch.AddSeconds(1)));
            Assert.That(history.Find(exact), Is.SameAs(expected));
            Assert.That(history.Find(sameMonotonicDifferentTime), Is.Null);
        });
    }

    [Test]
    public void ServiceHistory_ObservationCapEvictsOlderFramesButKeepsTheLatestFrame()
    {
        var history = new ServiceHistory();
        var expensive = new ServiceFrame(Sample(1),
            [new ServiceGroup("large", Enumerable.Repeat(Service("service", 0), ServiceHistory.MaximumObservations).ToArray(), null, [])]);
        var latest = new ServiceFrame(Sample(2), [new ServiceGroup("latest", [Service("latest", 0)], null, [])]);

        history.Add(expensive);
        history.Add(latest);

        Assert.That(history.Frames, Is.EqualTo([latest]));
    }

    [Test]
    public void ServiceHistory_UsesMonotonicAgeWhenTheWallClockMovesBackward()
    {
        var history = new ServiceHistory();
        var now = DateTimeOffset.UtcNow;
        var first = new ServiceFrame(new MonitorSample(now, 50, [], 0, false, Stopwatch.Frequency), []);
        var later = new ServiceFrame(new MonitorSample(now.AddHours(-1), 50, [], 0, false, Stopwatch.Frequency * 122), []);

        history.Add(first);
        history.Add(later);

        Assert.That(history.Frames, Is.EqualTo([later]));
    }

    [Test]
    public void Capture_SharesUnchangedGroupsKeysAndServiceListsBetweenFrames()
    {
        var tree = new ServiceTree();
        var hosted = Service("agent", 10);
        var stopped = new ServiceObservation("idle", "idle", 1, 0, 0);
        var pending = Service("pending", 30);
        var catalog = Catalog(1_000, hosted, stopped, pending);

        var first = tree.Capture(Sample(1, Usage(10, 100, 0, cpu: 1)), catalog);
        var second = tree.Capture(Sample(2, Usage(10, 100, 0, cpu: 2)), catalog);
        var host = (First: first.Groups.Single(g => g.Host is not null), Second: second.Groups.Single(g => g.Host is not null));

        Assert.Multiple(() =>
        {
            Assert.That(second.Groups.Select(g => g.Key), Is.EqualTo(first.Groups.Select(g => g.Key)));
            Assert.That(second.Groups.Single(g => g.Key == "service:idle"), Is.SameAs(first.Groups.Single(g => g.Key == "service:idle")));
            Assert.That(second.Groups.Single(g => g.Key == "pending:30"), Is.SameAs(first.Groups.Single(g => g.Key == "pending:30")));
            Assert.That(host.Second, Is.Not.SameAs(host.First), "A host group carries this frame's process readings.");
            Assert.That(host.Second.Key, Is.SameAs(host.First.Key));
            Assert.That(host.Second.Services, Is.SameAs(host.First.Services));
            Assert.That(host.Second.Processes.Single().CpuPercent, Is.EqualTo(2));
        });
    }

    [Test]
    public void Capture_ReplacesAServiceListWhenAnObservationChanges()
    {
        var tree = new ServiceTree();
        var first = tree.Capture(Sample(1, Usage(10, 100, 0)), Catalog(1_000, Service("alpha", 10), Service("beta", 10)));
        var changed = Service("alpha", 10) with { ControlsAccepted = 5 };

        var second = tree.Capture(Sample(2, Usage(10, 100, 0)), Catalog(1_000, changed, Service("beta", 10)));

        Assert.Multiple(() =>
        {
            Assert.That(second.Groups.Single().Services, Is.Not.SameAs(first.Groups.Single().Services));
            Assert.That(second.Groups.Single().Services[0], Is.SameAs(changed));
        });
    }

    [Test]
    public void FrameIndex_MatchesEachGroupsMetricsAndFindsGroupsByKey()
    {
        var frame = new ServiceTree().Capture(Sample(1,
            Usage(10, 100, 0, cpu: 1, privateBytes: 100, io: 2),
            Usage(11, 110, 10, cpu: 4, privateBytes: 200, io: null),
            Usage(20, 120, 0, cpu: 3)), Catalog(1_000, Service("agent", 10), Service("other", 20)));

        Assert.Multiple(() =>
        {
            for (var i = 0; i < frame.Groups.Count; i++)
            {
                Assert.That(frame.Index.Metrics(i, workers: false), Is.EqualTo(frame.Groups[i].Metrics(false)));
                Assert.That(frame.Index.Metrics(i, workers: true), Is.EqualTo(frame.Groups[i].Metrics(true)));
                Assert.That(frame.Index.Find(frame.Groups[i].Key), Is.SameAs(frame.Groups[i]));
            }
            Assert.That(frame.Index.IndexOf("missing"), Is.EqualTo(-1));
            Assert.That(frame.Index.PeakCpu(workers: false), Is.EqualTo(3));
            Assert.That(frame.Index.PeakCpu(workers: true), Is.EqualTo(5));
            Assert.That(frame.Groups.Single(g => g.Host?.ProcessId == 10).Metrics(true), Is.EqualTo(new ServiceMetrics(5, 300, null)),
                "A worker without an I/O reading leaves the group's I/O unavailable.");
        });
    }

    private static ServiceObservation Service(string name, int processId) => new(name, name, 4, processId, 1);
    private static ServiceCatalog Catalog(long observed, params ServiceObservation[] services) => new(observed, services);
    private static ProcessUsage Usage(int id, long created, int parent, double cpu = 0, ulong? privateBytes = 0, double? io = 0) =>
        new(id, created, $"p{id}", cpu, WorkingSetBytes: null, IoBytesPerSecond: io) { ParentProcessId = parent, PrivateBytes = privateBytes };
    private static MonitorSample Sample(int second, params ProcessUsage[] processes) => Sample(second, false, second, processes);
    private static MonitorSample Sample(int second, bool isGap, params ProcessUsage[] processes) => Sample(second, isGap, second, processes);
    private static MonitorSample Sample(int second, long monotonic) => Sample(second, false, monotonic, []);
    private static MonitorSample Sample(int second, bool isGap, long monotonic, params ProcessUsage[] processes) =>
        new(DateTimeOffset.UnixEpoch.AddSeconds(second), 50, processes, 0, isGap, monotonic * Stopwatch.Frequency);

}
