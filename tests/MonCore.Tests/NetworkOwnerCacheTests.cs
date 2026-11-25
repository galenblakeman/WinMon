using System.Diagnostics;
using MonCore.Monitoring;
using NUnit.Framework;

namespace MonCore.Tests;

[TestFixture]
public sealed class NetworkOwnerCacheTests
{
    private static readonly long Second = Stopwatch.Frequency;
    private static readonly long Tick = Math.Max(1, (long)Math.Ceiling(Stopwatch.Frequency / (double)TimeSpan.TicksPerSecond));
    private static MonitorSession Session() => new(() => throw new InvalidOperationException("No collector expected"),
        () => new SamplingOptions(), _ => throw new InvalidOperationException("No dispatcher expected"));
    private static ProcessUsage Owner(int id = 1, long created = 100, string path = "known") =>
        new(id, created, "owner" + id, id / 10d) { ImagePath = path, PrivateBytes = (ulong)id * 1024 };
    private static MonitorSample Sample(params ProcessUsage[] processes) =>
        new(DateTimeOffset.UnixEpoch, 20, processes, 0, false, Second);
    private static ProcessNetworkFrame Traffic(params ProcessUsage[] owners) => new(123, 456, ProcessNetworkQuality.Provisional,
        owners.Select(p => new ProcessNetworkReading(p.ProcessId, p.CreationTicks, 100, 200, 10, 20)).ToArray());
    private static ProcessNetworkFlowReading Flow(ProcessUsage owner) =>
        new(owner.ProcessId, owner.CreationTicks, TransportProtocol.Tcp, "local", "remote", 100, 200, 10, 20);

    [TestCase(-1, true)]
    [TestCase(0, true)]
    [TestCase(1, false)]
    public void Attach_UnobservedMetadataExpiresOnlyAfterThirtySeconds(int offset, bool retained)
    {
        var session = Session();
        var owner = Owner();
        session.AttachNetworkOwners(Traffic(), Sample(owner), 100 * Second);
        var now = 130 * Second + offset * Tick;
        session.AttachNetworkOwners(Traffic(), null, now);
        var probe = session.AttachNetworkOwners(Traffic(owner), null, now);
        Assert.That(probe.Owners, Is.EqualTo(retained ? new[] { owner } : []));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Attach_CurrentProcessOrFlowRetainsMetadataWithoutRefreshingItsLastSeenTime(bool flowOnly)
    {
        var session = Session();
        var owner = Owner();
        session.AttachNetworkOwners(Traffic(), Sample(owner), 100 * Second);
        var frame = flowOnly ? Traffic() with { Flows = [Flow(owner)] } : Traffic(owner);
        Assert.That(session.AttachNetworkOwners(frame, null, 500 * Second).Owners.Single(), Is.SameAs(owner));
        session.AttachNetworkOwners(Traffic(), null, 500 * Second);
        Assert.That(session.AttachNetworkOwners(frame, null, 500 * Second).Owners, Is.Empty);
    }

    [TestCase(-1, true)]
    [TestCase(0, true)]
    [TestCase(1, false)]
    public void Attach_PathFallbackStillRefreshesTheMetadataExpiryClock(int offset, bool retained)
    {
        var session = Session();
        var owner = Owner();
        session.AttachNetworkOwners(Traffic(), Sample(owner), 100 * Second);
        session.AttachNetworkOwners(Traffic(), Sample(owner with { Name = "fresh", ImagePath = "" }), 120 * Second);
        var now = 150 * Second + offset * Tick;
        session.AttachNetworkOwners(Traffic(), null, now);
        var probe = session.AttachNetworkOwners(Traffic(owner), null, now);
        Assert.That(probe.Owners, Is.EqualTo(retained ? new[] { owner } : []));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Attach_NullFullSampleOrProcessCollectionKeepsRetainedMetadata(bool nullCollection)
    {
        var session = Session();
        var owner = Owner();
        session.AttachNetworkOwners(Traffic(owner), Sample(owner), 100 * Second);
        var result = session.AttachNetworkOwners(Traffic(owner), nullCollection ? Sample() with { Processes = null! } : null,
            200 * Second);
        Assert.That(result.Owners.Single(), Is.SameAs(owner));
    }

    [TestCase(-1, false)]
    [TestCase(0, false)]
    [TestCase(1, true)]
    public void Attach_PruningPrecedesCapacityAdmissionAtTheExpiryBoundary(int offset, bool admitted)
    {
        var session = Session();
        var cached = Enumerable.Range(1, 8192).Select(id => Owner(id)).ToArray();
        session.AttachNetworkOwners(Traffic(), Sample(cached), 100 * Second);
        var added = Owner(9000);
        var result = session.AttachNetworkOwners(Traffic(added), Sample(added), 130 * Second + offset * Tick);
        Assert.That(result.Owners, Is.EqualTo(admitted ? new[] { added } : []));
        Assert.That(result.Quality, Is.EqualTo(admitted ? ProcessNetworkQuality.Provisional
            : ProcessNetworkQuality.Provisional | ProcessNetworkQuality.CapacityExceeded | ProcessNetworkQuality.Incomplete));
    }

    [Test]
    public void Attach_KnownMetadataUpdatesAtCapacityEvenAfterANewLifetimeWasRejected()
    {
        var session = Session();
        var cached = Enumerable.Range(1, 8192).Select(id => Owner(id)).ToArray();
        var first = session.AttachNetworkOwners(Traffic(cached[0]), Sample(cached), 100 * Second);
        var updated = cached[0] with { Name = "updated", ImagePath = "new-path" };
        var rejected = Owner(9000);
        var result = session.AttachNetworkOwners(Traffic(updated, rejected), Sample(rejected, updated), 100 * Second);
        Assert.That(result.Owners.Single(), Is.SameAs(updated));
        Assert.That(result.Quality, Is.EqualTo(ProcessNetworkQuality.Provisional | ProcessNetworkQuality.CapacityExceeded |
            ProcessNetworkQuality.Incomplete));
        Assert.That(first.Owners.Single(), Is.SameAs(cached[0]));
    }

    [TestCase(ProcessLifetimeDomain.UnixMicroseconds)]
    [TestCase((ProcessLifetimeDomain)99)]
    public void Attach_NonWindowsMetadataDoesNotConsumeTheLastCapacitySlot(ProcessLifetimeDomain domain)
    {
        var session = Session();
        session.AttachNetworkOwners(Traffic(), Sample(Enumerable.Range(1, 8191).Select(id => Owner(id)).ToArray()), 100 * Second);
        var wanted = Owner(9000);
        var result = session.AttachNetworkOwners(Traffic(wanted),
            Sample(wanted with { LifetimeDomain = domain, Name = "foreign" }, wanted), 100 * Second);
        Assert.That(result.Owners.Single(), Is.SameAs(wanted));
        Assert.That(result.Quality, Is.EqualTo(ProcessNetworkQuality.Provisional));
    }

    [TestCase("")]
    [TestCase("updated")]
    public void Attach_DuplicateMetadataUsesTheExistingPathPrecedenceWithoutConsumingAnotherSlot(string lastPath)
    {
        var session = Session();
        session.AttachNetworkOwners(Traffic(), Sample(Enumerable.Range(1, 8191).Select(id => Owner(id)).ToArray()), 100 * Second);
        var first = Owner(9000);
        var last = first with { Name = "last", ImagePath = lastPath };
        var result = session.AttachNetworkOwners(Traffic(first), Sample(first, last), 100 * Second);
        Assert.That(result.Owners.Single(), Is.SameAs(lastPath.Length == 0 ? first : last));
        Assert.That(result.Quality, Is.EqualTo(ProcessNetworkQuality.Provisional));
    }

    [Test]
    public void Attach_ReusedPidCannotBorrowThePreviousLifetimesPath()
    {
        var session = Session();
        var old = Owner();
        var reused = Owner(created: 101, path: "") with { Name = "reused" };
        session.AttachNetworkOwners(Traffic(old), Sample(old), 100 * Second);
        var result = session.AttachNetworkOwners(Traffic(old, reused), Sample(reused), 100 * Second);
        Assert.That(result.Owners, Is.EqualTo(new[] { old, reused }));
        Assert.That(result.Owners[1].ImagePath, Is.Empty);
    }

    [TestCase(0L)]
    [TestCase(-1L)]
    public void Attach_DoesNotAddCreationTokenValidationToAlreadySuppliedFrames(long created)
    {
        var owner = Owner(created: created);
        Assert.That(Session().AttachNetworkOwners(Traffic(owner), Sample(owner), 100 * Second).Owners.Single(), Is.SameAs(owner));
    }

    [Test]
    public void Attach_OwnersFollowTheRetainedIdentityUnionAndOmitUnknownLifetimes()
    {
        var session = Session();
        var a = Owner(1); var b = Owner(2); var c = Owner(3); var unknown = Owner(4);
        var frame = Traffic(c, a, c, unknown) with { Flows = [Flow(b), Flow(a)] };
        var result = session.AttachNetworkOwners(frame, Sample(a, b, c), 100 * Second);
        Assert.That(result.Owners, Is.EqualTo(new[] { c, a, b }));
    }

    [TestCase(ProcessNetworkQuality.None)]
    [TestCase(ProcessNetworkQuality.Unavailable | ProcessNetworkQuality.Incomplete)]
    [TestCase((ProcessNetworkQuality)64)]
    public void Attach_ReplacesOwnersWithoutMutatingInputOrOtherNetworkFields(ProcessNetworkQuality quality)
    {
        var owner = Owner();
        var unrelated = Owner(2);
        var frame = Traffic(owner) with { Owners = [unrelated], Quality = quality, FlowQuality = ProcessNetworkQuality.Discontinuity,
            WindowStartQpc = 10, WindowEndQpc = 20, FlowTrafficSupported = true,
            CurrentLifetimes = new HashSet<ProcessLifetimeIdentity> { owner.Identity },
            CurrentFlows = new HashSet<ProcessNetworkFlowIdentity>() };
        var result = Session().AttachNetworkOwners(frame, Sample(owner), 100 * Second);
        Assert.That(result.Owners.Single(), Is.SameAs(owner));
        Assert.That(frame.Owners.Single(), Is.SameAs(unrelated));
        Assert.That(result, Is.EqualTo(frame with { Owners = result.Owners }));
        Assert.That(result, Is.Not.SameAs(frame));
    }

    [Test]
    public void Attach_MetadataFailureKeepsEarlierUpdatesAndTheNextCallRecovers()
    {
        var session = Session();
        var old = Owner();
        var updated = old with { Name = "updated", ImagePath = "new" };
        var missing = Owner(2);
        var first = session.AttachNetworkOwners(Traffic(old), Sample(old), 100 * Second);
        Assert.That(() => session.AttachNetworkOwners(Traffic(updated, missing), Sample(updated, null!, missing), 100 * Second),
            Throws.TypeOf<NullReferenceException>());
        var result = session.AttachNetworkOwners(Traffic(updated, missing), null, 100 * Second);
        Assert.That(result.Owners.Single(), Is.SameAs(updated));
        Assert.That(first.Owners.Single(), Is.SameAs(old));
    }

    [TestCase("frame")]
    [TestCase("readings")]
    [TestCase("reading")]
    [TestCase("flows")]
    [TestCase("flow")]
    public void Attach_MalformedNetworkInputRetainsItsExceptionAndDoesNotLoseCachedMetadata(string malformed)
    {
        var session = Session();
        var owner = Owner();
        session.AttachNetworkOwners(Traffic(owner), Sample(owner), 100 * Second);
        var bad = malformed switch
        {
            "frame" => null!, "readings" => Traffic() with { Processes = null! },
            "reading" => Traffic() with { Processes = [null!] }, "flows" => Traffic() with { Flows = null! },
            _ => Traffic() with { Flows = [null!] }
        };
        Assert.That(() => session.AttachNetworkOwners(bad, null, 200 * Second), Throws.TypeOf<NullReferenceException>());
        Assert.That(session.AttachNetworkOwners(Traffic(owner), null, 200 * Second).Owners.Single(), Is.SameAs(owner));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Attach_CapacityRejectionStillPrecedesAccessingAnInvalidMetadataPath(bool full)
    {
        var session = Session();
        session.AttachNetworkOwners(Traffic(), Sample(Enumerable.Range(1, full ? 8192 : 8191).Select(id => Owner(id)).ToArray()),
            100 * Second);
        var malformed = Owner(9000) with { ImagePath = null! };
        if (full)
        {
            var result = session.AttachNetworkOwners(Traffic(malformed), Sample(malformed), 100 * Second);
            Assert.That(result.Owners, Is.Empty);
            Assert.That(result.Quality.HasFlag(ProcessNetworkQuality.CapacityExceeded), Is.True);
        }
        else Assert.That(() => session.AttachNetworkOwners(Traffic(malformed), Sample(malformed), 100 * Second),
            Throws.TypeOf<NullReferenceException>());
    }

    [Test]
    public void Attach_PruningIsNotRolledBackWhenLaterMetadataFails()
    {
        var session = Session();
        var owner = Owner();
        session.AttachNetworkOwners(Traffic(), Sample(owner), 100 * Second);
        Assert.That(() => session.AttachNetworkOwners(Traffic(), Sample([null!]), 131 * Second), Throws.TypeOf<NullReferenceException>());
        Assert.That(session.AttachNetworkOwners(Traffic(owner), null, 131 * Second).Owners, Is.Empty);
    }

    [Test]
    public void Attach_BackwardTimestampDoesNotExpireCachedMetadata()
    {
        var session = Session();
        var owner = Owner();
        session.AttachNetworkOwners(Traffic(), Sample(owner), 100 * Second);
        session.AttachNetworkOwners(Traffic(), null, 99 * Second);
        Assert.That(session.AttachNetworkOwners(Traffic(owner), null, 99 * Second).Owners.Single(), Is.SameAs(owner));
    }
}
