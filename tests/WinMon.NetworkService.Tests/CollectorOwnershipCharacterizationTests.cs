using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using MonCore.Monitoring;
using NUnit.Framework;

namespace WinMon.NetworkService.Tests;

[TestFixture]
public sealed class CollectorOwnershipCharacterizationTests
{
    private const string Allowed = "S-1-5-21-provisioned";
    private const string Other = "S-1-5-21-other";
    private static readonly DateTime Epoch = new(2026, 11, 21, 12, 0, 0, DateTimeKind.Utc);
    private static long Birth => Epoch.AddSeconds(1).ToFileTimeUtc();
    private static long Created => 100 + Stopwatch.Frequency;

    private sealed class Probe : EtwNetworkCollector.IProcessOwnerProbe
    {
        internal Func<int, long?> Creation = _ => Birth;
        internal Func<int, ProcessOwner?> Owner = pid => new(pid, Birth, Allowed);
        internal readonly List<string> Calls = [];
        internal Exception? CreationError, OwnerError;
        internal long FailedCreationValue;
        public bool TryReadCreation(int pid, out long creation)
        {
            Calls.Add("creation:" + pid);
            if (CreationError is not null) throw CreationError;
            var result = Creation(pid);
            creation = result ?? FailedCreationValue;
            return result.HasValue;
        }
        public bool TryRead(int pid, out ProcessOwner owner)
        {
            Calls.Add("owner:" + pid);
            if (OwnerError is not null) throw OwnerError;
            owner = Owner(pid)!;
            return owner is not null;
        }
    }

    private sealed class Fixture : IAsyncDisposable
    {
        internal readonly Probe Probe = new();
        internal readonly EtwNetworkCollector Collector;
        internal readonly CollectorOwnershipState State;
        internal readonly Func<int, long, long, bool> Ensure;
        internal Fixture(bool ledger = true, int lifetimeLimit = 8192, params string[] allowed)
        {
            Collector = new(allowed.Length == 0 ? [Allowed] : allowed, Probe);
            State = new(Collector, Epoch, lifetimeLimit: lifetimeLimit, attachLedger: ledger);
            Ensure = typeof(EtwNetworkCollector).GetMethod("EnsureKnown", BindingFlags.NonPublic | BindingFlags.Instance)!
                .CreateDelegate<Func<int, long, long, bool>>(Collector);
        }
        internal bool Register(int pid = 42) => Collector.ResolveAndRegister(pid, Created + 1, Created + 2, false);
        public ValueTask DisposeAsync() => Collector.DisposeAsync();
    }

    [TestCase("null")]
    [TestCase("empty")]
    [TestCase("blank")]
    public void DefaultConstructorKeepsProvisioningValidation(string input)
    {
        IReadOnlyCollection<string> allowed = input == "null" ? null! : input == "empty" ? [] : [" ", "\t", null!];
        var error = input == "null"
            ? Assert.Throws<ArgumentNullException>(() => _ = new EtwNetworkCollector(allowed))
            : Assert.Throws<ArgumentException>(() => _ = new EtwNetworkCollector(allowed));
        Assert.That(error!.ParamName, Is.EqualTo("allowedSids"));
        if (input != "null") Assert.That(error.Message, Does.StartWith("At least one provisioned SID is required."));
    }

    [TestCase(-1)]
    [TestCase(0)]
    [TestCase(1)]
    public async Task CreationBeforeCaptureClampsToSessionStartWithoutChangingItsLifetimeToken(int seconds)
    {
        await using var f = new Fixture();
        var birth = Epoch.AddSeconds(seconds).ToFileTimeUtc();
        var started = 100 + Math.Max(0, seconds) * Stopwatch.Frequency;
        f.Probe.Owner = pid => new(pid, birth, Allowed);
        Assert.That(f.Collector.ResolveAndRegister(42, started + 1, started + 2, true), Is.True);
        Assert.That(f.State.Cached(42), Is.EqualTo(new CachedOwner(birth, started, started + 2)));
        Assert.That(f.State.RejectedCount, Is.Zero);
        Assert.That(f.Collector.CoverageReasons, Is.EqualTo(ProcessNetworkIncompleteReason.None));
    }

    [TestCase(-1L)]
    [TestCase(long.MaxValue)]
    public async Task InvalidNativeCreationTimeKeepsConversionFailureBeforePublishingState(long birth)
    {
        await using var f = new Fixture();
        f.Probe.Owner = pid => new(pid, birth, Allowed);
        Assert.Throws<ArgumentOutOfRangeException>(() => f.Register());
        Assert.That(f.Probe.Calls, Is.EqualTo(new[] { "owner:42" }));
        Assert.That(f.State.OwnerCount, Is.Zero);
        Assert.That(f.State.RejectedCount, Is.Zero);
        Assert.That(f.Collector.CoverageReasons, Is.EqualTo(ProcessNetworkIncompleteReason.None));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task MissingLedgerRejectsWithoutCallingTheProbe(bool observed)
    {
        await using var f = new Fixture(ledger: false);
        Assert.That(f.Collector.ResolveAndRegister(42, 10, 20, observed), Is.False);
        Assert.Multiple(() =>
        {
            Assert.That(f.Probe.Calls, Is.Empty);
            Assert.That(f.State.OwnerCount, Is.Zero);
            Assert.That(f.State.Rejected(42), Is.EqualTo(new RejectedOwner(20 + Stopwatch.Frequency, true, 0)));
            Assert.That(f.Collector.CoverageReasons, Is.EqualTo(observed ? ProcessNetworkIncompleteReason.OwnershipUnavailable : ProcessNetworkIncompleteReason.None));
            Assert.That(f.State.RateGap, Is.Zero);
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task FailedOwnerProbeMarksOnlyObservedUnretainedTraffic(bool observed)
    {
        await using var f = new Fixture();
        f.Probe.Owner = _ => null;
        Assert.That(f.Collector.ResolveAndRegister(42, Created + 1, Created + 2, observed), Is.False);
        Assert.Multiple(() =>
        {
            Assert.That(f.Probe.Calls, Is.EqualTo(new[] { "owner:42" }));
            Assert.That(f.State.Cached(42), Is.Null);
            Assert.That(f.State.Rejected(42), Is.EqualTo(new RejectedOwner(Created + 2 + Stopwatch.Frequency, true, 0)));
            Assert.That(f.Collector.CoverageReasons, Is.EqualTo(observed ? ProcessNetworkIncompleteReason.OwnershipUnavailable : ProcessNetworkIncompleteReason.None));
            Assert.That(f.State.RateGap, Is.Zero);
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task FailedProbeRetainsClosedLifetimeWithoutMarkingOwnershipLoss(bool observed)
    {
        await using var f = new Fixture();
        Assert.That(f.Register(), Is.True);
        f.State.End(42, Created + 10);
        f.Probe.Owner = _ => null;
        f.Probe.Calls.Clear();
        Assert.That(f.Collector.ResolveAndRegister(42, Created + 5, Created + 20, observed), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(f.Probe.Calls, Is.EqualTo(new[] { "owner:42" }));
            Assert.That(f.State.Rejected(42), Is.EqualTo(new RejectedOwner(Created + 20 + Stopwatch.Frequency, true, 0)));
            Assert.That(f.State.Cached(42), Is.Null);
            Assert.That(f.Collector.CoverageReasons, Is.EqualTo(ProcessNetworkIncompleteReason.None));
            Assert.That(f.State.Ledger.Add(42, Created + 5, Created + 20, false, 9), Is.True);
            Assert.That(f.State.Ledger.Snapshot(Allowed, Created + 20).Processes.Single().ReceivedBytes, Is.EqualTo(9));
        });
    }

    [TestCase(-1, false)]
    [TestCase(0, false)]
    [TestCase(1, true)]
    public async Task AuthorizedEventsMustBeStrictlyAfterCreation(int delta, bool expected)
    {
        await using var f = new Fixture();
        Assert.That(f.Collector.ResolveAndRegister(42, Created + delta, Created + 10, true), Is.EqualTo(expected));
        Assert.Multiple(() =>
        {
            Assert.That(f.Probe.Calls, Is.EqualTo(new[] { "owner:42" }));
            Assert.That(f.State.Cached(42), Is.EqualTo(expected ? new CachedOwner(Birth, Created, Created + 10) : null));
            Assert.That(f.State.Rejected(42), Is.EqualTo(expected ? null : new RejectedOwner(Created + 10 + Stopwatch.Frequency, false, 0)));
            Assert.That(f.Collector.CoverageReasons, Is.EqualTo(ProcessNetworkIncompleteReason.None));
        });
    }

    [TestCase(Other)]
    [TestCase("s-1-5-21-provisioned")]
    public async Task UnprovisionedOwnerVerdictUsesOrdinalSidAndItsCreationToken(string sid)
    {
        await using var f = new Fixture();
        f.Probe.Owner = pid => new(pid, Birth, sid);
        Assert.That(f.Register(), Is.False);
        Assert.Multiple(() =>
        {
            Assert.That(f.State.OwnerCount, Is.Zero);
            Assert.That(f.State.Rejected(42), Is.EqualTo(new RejectedOwner(Created + 2 + Stopwatch.Frequency, false, Birth)));
            Assert.That(f.Collector.CoverageReasons, Is.EqualTo(ProcessNetworkIncompleteReason.None));
            Assert.That(f.State.RateGap, Is.Zero);
            Assert.That(f.State.Ledger.Add(42, Created + 1, Created + 2, false, 9), Is.False);
        });
    }

    [Test]
    public async Task DelayedPacketKeepsOldOwnerWhenTheCurrentPidBelongsToAnUnprovisionedUser()
    {
        await using var f = new Fixture();
        Assert.That(f.Register(), Is.True);
        f.State.End(42, Created + 10);
        f.Probe.Owner = pid => new(pid, Epoch.AddSeconds(2).ToFileTimeUtc(), Other);
        Assert.That(f.Collector.ResolveAndRegister(42, Created + 5, Created + 20, true), Is.True);
        Assert.That(f.State.Ledger.Add(42, Created + 5, Created + 20, false, 9), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(f.State.Ledger.Snapshot(Allowed, Created + 20).Processes.Single().CreationTicks, Is.EqualTo(Birth));
            Assert.That(f.State.Ledger.Snapshot(Other, Created + 20).Processes, Is.Empty);
            Assert.That(f.State.OwnerCount, Is.Zero);
            Assert.That(f.State.Rejected(42)!.OtherOwnerCreationTicks, Is.EqualTo(Epoch.AddSeconds(2).ToFileTimeUtc()));
            Assert.That(f.Collector.CoverageReasons, Is.EqualTo(ProcessNetworkIncompleteReason.None));
        });
    }

    [TestCase(-1)]
    [TestCase(20)]
    public async Task KnownCreationRefreshSkipsSidProbeAndAssignsTheSuppliedVerificationTime(int verifiedDelta)
    {
        await using var f = new Fixture();
        Assert.That(f.Register(), Is.True);
        f.Probe.Calls.Clear();
        f.Probe.OwnerError = new InvalidOperationException("Full owner probe must not run");
        Assert.That(f.Collector.ResolveAndRegister(42, Created + 3, Created + verifiedDelta, true), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(f.Probe.Calls, Is.EqualTo(new[] { "creation:42" }));
            Assert.That(f.State.Cached(42), Is.EqualTo(new CachedOwner(Birth, Created, Created + verifiedDelta)));
            Assert.That(f.State.OwnerCount, Is.EqualTo(1));
            Assert.That(f.Collector.CoverageReasons, Is.EqualTo(ProcessNetworkIncompleteReason.None));
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task FailedOrMismatchedCreationFallsBackToTheFullProbeAndCanRefreshTheSameLifetime(bool mismatched)
    {
        await using var f = new Fixture();
        Assert.That(f.Register(), Is.True);
        f.Probe.Creation = _ => mismatched ? Birth + 1 : null;
        f.Probe.Calls.Clear();
        Assert.That(f.Collector.ResolveAndRegister(42, Created + 3, Created + 4, false), Is.True);
        Assert.That(f.Probe.Calls, Is.EqualTo(new[] { "creation:42", "owner:42" }));
        Assert.That(f.State.Cached(42), Is.EqualTo(new CachedOwner(Birth, Created, Created + 4)));
        Assert.That(f.State.Ledger.Snapshot(Allowed, Created + 4).IncompleteReasons, Is.EqualTo(ProcessNetworkIncompleteReason.None));
    }

    [Test]
    public async Task FailedReprobeKeepsPreviouslyVerifiedLifetimeAndItsEarlierPacketWindow()
    {
        await using var f = new Fixture();
        Assert.That(f.Register(), Is.True);
        var old = f.State.Cached(42);
        f.Probe.Creation = _ => null;
        f.Probe.Owner = _ => null;
        f.Probe.Calls.Clear();
        Assert.That(f.Collector.ResolveAndRegister(42, Created + 3, Created + 4, true), Is.False);
        Assert.That(f.Probe.Calls, Is.EqualTo(new[] { "creation:42", "owner:42" }));
        Assert.That(f.State.Cached(42), Is.EqualTo(old));
        Assert.That(f.State.Rejected(42), Is.EqualTo(new RejectedOwner(Created + 4 + Stopwatch.Frequency, true, 0)));
        f.Probe.Calls.Clear();
        Assert.That(f.Ensure(42, Created + 1, Created + 5), Is.True);
        Assert.That(f.Probe.Calls, Is.Empty);
        Assert.That(f.Collector.CoverageReasons, Is.EqualTo(ProcessNetworkIncompleteReason.OwnershipUnavailable));
    }

    [Test]
    public async Task FailedCreationWithTheOldTokenStillRechecksSidBeforeAllowingAReusedPid()
    {
        await using var f = new Fixture();
        Assert.That(f.Register(), Is.True);
        var old = f.State.Cached(42);
        var newBirth = Epoch.AddSeconds(2).ToFileTimeUtc();
        var now = Created + Stopwatch.Frequency + 10;
        f.Probe.Creation = _ => null;
        f.Probe.FailedCreationValue = Birth;
        f.Probe.Owner = pid => new(pid, newBirth, Other);
        f.Probe.Calls.Clear();
        Assert.That(f.Collector.ResolveAndRegister(42, now, now, true), Is.False);
        Assert.Multiple(() =>
        {
            Assert.That(f.Probe.Calls, Is.EqualTo(new[] { "creation:42", "owner:42" }));
            Assert.That(f.State.Cached(42), Is.EqualTo(old));
            Assert.That(f.State.Rejected(42), Is.EqualTo(new RejectedOwner(now + Stopwatch.Frequency, false, newBirth)));
            Assert.That(f.Collector.CoverageReasons, Is.EqualTo(ProcessNetworkIncompleteReason.None));
        });
    }

    [Test]
    public async Task PidReuseEndsTheOldLifetimeBeforeRegisteringTheNewOwner()
    {
        await using var f = new Fixture(allowed: [Allowed, Other]);
        Assert.That(f.Register(), Is.True);
        var nextBirth = Epoch.AddSeconds(2).ToFileTimeUtc();
        var nextCreated = Created + Stopwatch.Frequency;
        f.Probe.Creation = _ => nextBirth;
        f.Probe.Owner = pid => new(pid, nextBirth, Other);
        f.Probe.Calls.Clear();
        Assert.That(f.Collector.ResolveAndRegister(42, nextCreated + 1, nextCreated + 2, true), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(f.Probe.Calls, Is.EqualTo(new[] { "creation:42", "owner:42" }));
            Assert.That(f.State.Cached(42), Is.EqualTo(new CachedOwner(nextBirth, nextCreated, nextCreated + 2)));
            Assert.That(f.State.Ledger.Add(42, nextCreated - 1, nextCreated + 2, false, 5), Is.True);
            Assert.That(f.State.Ledger.Add(42, nextCreated, nextCreated + 2, false, 100), Is.False);
            Assert.That(f.State.Ledger.Add(42, nextCreated + 1, nextCreated + 2, true, 7), Is.True);
        });
        var old = f.State.Ledger.Snapshot(Allowed, nextCreated + 2).Processes.Single();
        var current = f.State.Ledger.Snapshot(Other, nextCreated + 2).Processes.Single();
        Assert.Multiple(() =>
        {
            Assert.That((old.CreationTicks, old.ReceivedBytes, old.SentBytes), Is.EqualTo((Birth, 5L, 0L)));
            Assert.That((current.CreationTicks, current.ReceivedBytes, current.SentBytes), Is.EqualTo((nextBirth, 0L, 7L)));
        });
    }

    [TestCase(8191, true)]
    [TestCase(8192, false)]
    public async Task OwnerCapacityKeepsAllActiveOwnersAndNeverEvictsOneToRegisterAnother(int count, bool expected)
    {
        await using var f = new Fixture(lifetimeLimit: 9000);
        for (var pid = 1; pid <= count; pid++) Assert.That(f.Register(pid), Is.True);
        var first = f.State.Cached(1);
        Assert.That(f.Register(10000), Is.EqualTo(expected));
        Assert.Multiple(() =>
        {
            Assert.That(f.State.OwnerCount, Is.EqualTo(count + (expected ? 1 : 0)));
            Assert.That(f.State.Cached(1), Is.EqualTo(first));
            Assert.That(f.State.Cached(10000), Is.EqualTo(expected ? new CachedOwner(Birth, Created, Created + 2) : null));
            Assert.That(f.Collector.CoverageReasons, Is.EqualTo(expected ? ProcessNetworkIncompleteReason.None : ProcessNetworkIncompleteReason.CapacityExceeded));
            Assert.That(f.State.RateGap, Is.Zero);
            Assert.That(f.State.RejectedCount, Is.Zero);
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task LedgerRegistrationFailureReturnsOnlyAnExistingClosedLifetime(bool retained)
    {
        await using var f = new Fixture(lifetimeLimit: 1);
        Assert.That(f.Register(retained ? 42 : 41), Is.True);
        f.State.End(retained ? 42 : 41, Created + Stopwatch.Frequency + 20);
        f.Probe.Owner = pid => new(pid, Epoch.AddSeconds(2).ToFileTimeUtc(), Allowed);
        var eventQpc = Created + Stopwatch.Frequency + 10;
        Assert.That(f.Collector.ResolveAndRegister(42, eventQpc, eventQpc + 1, true), Is.EqualTo(retained));
        Assert.Multiple(() =>
        {
            Assert.That(f.State.Cached(42), Is.Null);
            Assert.That(f.State.Rejected(42), Is.Null);
            Assert.That(f.Collector.CoverageReasons, Is.EqualTo(ProcessNetworkIncompleteReason.None));
            Assert.That(f.State.Ledger.Snapshot(Allowed, eventQpc + 1).IncompleteReasons,
                Is.EqualTo(retained ? ProcessNetworkIncompleteReason.AmbiguousLifetime : ProcessNetworkIncompleteReason.CapacityExceeded));
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task ProbeExceptionsKeepTheirIdentityAndDoNotChangeCacheOrRejection(bool creation)
    {
        await using var f = new Fixture();
        if (creation) Assert.That(f.Register(), Is.True);
        var cached = f.State.Cached(42);
        var error = new Win32Exception(5);
        if (creation) f.Probe.CreationError = error; else f.Probe.OwnerError = error;
        Assert.That(Assert.Throws<Win32Exception>(() => f.Collector.ResolveAndRegister(42, Created + 3, Created + 4, true)), Is.SameAs(error));
        Assert.Multiple(() =>
        {
            Assert.That(f.State.Cached(42), Is.EqualTo(cached));
            Assert.That(f.State.RejectedCount, Is.Zero);
            Assert.That(f.Collector.CoverageReasons, Is.EqualTo(ProcessNetworkIncompleteReason.None));
        });
    }

    [Test]
    public async Task RejectionExpiryOverflowPrecedesPublishingRejectionOrOwnershipLoss()
    {
        await using var f = new Fixture();
        f.Probe.Owner = _ => null;
        Assert.Throws<OverflowException>(() => f.Collector.ResolveAndRegister(42, Created + 1, long.MaxValue, true));
        Assert.That(f.State.RejectedCount, Is.Zero);
        Assert.That(f.Collector.CoverageReasons, Is.EqualTo(ProcessNetworkIncompleteReason.None));
    }

    [TestCase(8191, false)]
    [TestCase(8192, false)]
    [TestCase(8192, true)]
    public async Task RejectedPidCapacityEvictsOnlyTheEarliestVerdictForANewPid(int count, bool replacing)
    {
        await using var f = new Fixture();
        f.Probe.Owner = _ => null;
        for (var pid = 1; pid <= count; pid++)
            Assert.That(f.Collector.ResolveAndRegister(pid, 1, pid, false), Is.False);
        var candidate = replacing ? 1 : 10000;
        Assert.That(f.Collector.ResolveAndRegister(candidate, 1, 20000, false), Is.False);
        Assert.Multiple(() =>
        {
            Assert.That(f.State.RejectedCount, Is.EqualTo(Math.Min(8192, count + (replacing ? 0 : 1))));
            Assert.That(f.State.Rejected(candidate), Is.EqualTo(new RejectedOwner(20000 + Stopwatch.Frequency, true, 0)));
            Assert.That(f.State.Rejected(2), Is.EqualTo(new RejectedOwner(2 + Stopwatch.Frequency, true, 0)));
            Assert.That(f.State.Rejected(1), Is.EqualTo(replacing ? new RejectedOwner(20000 + Stopwatch.Frequency, true, 0)
                : count == 8192 ? null : new RejectedOwner(1 + Stopwatch.Frequency, true, 0)));
            Assert.That(f.State.OwnerCount, Is.Zero);
            Assert.That(f.Collector.CoverageReasons, Is.EqualTo(ProcessNetworkIncompleteReason.None));
        });
    }

    [TestCase(-1, false)]
    [TestCase(0, false)]
    [TestCase(1, true)]
    public async Task EnsureHonorsInclusiveRejectionExpiryBeforeRetryingTheOwner(int delta, bool expected)
    {
        await using var f = new Fixture();
        f.Probe.Owner = _ => null;
        Assert.That(f.Register(), Is.False);
        var until = Created + 2 + Stopwatch.Frequency;
        f.Probe.Calls.Clear();
        f.Probe.Owner = pid => new(pid, Birth, Allowed);
        Assert.That(f.Ensure(42, Created + 3, until + delta), Is.EqualTo(expected));
        Assert.Multiple(() =>
        {
            Assert.That(f.Probe.Calls, Is.EqualTo(expected ? new[] { "owner:42" } : Array.Empty<string>()));
            Assert.That(f.State.Cached(42), Is.EqualTo(expected ? new CachedOwner(Birth, Created, until + delta) : null));
            Assert.That(f.Collector.CoverageReasons, Is.EqualTo(expected ? ProcessNetworkIncompleteReason.None : ProcessNetworkIncompleteReason.OwnershipUnavailable));
            Assert.That(f.State.RateGap, Is.Zero);
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task EnsureUsesClosedLifetimesOrVerifiedCacheWithoutAnyNativeRead(bool closed)
    {
        await using var f = new Fixture();
        Assert.That(f.Register(), Is.True);
        if (closed) f.State.End(42, Created + 10);
        f.Probe.Calls.Clear();
        f.Probe.CreationError = f.Probe.OwnerError = new InvalidOperationException("No native read expected");
        Assert.That(f.Ensure(42, Created + (closed ? 5 : 2), Created + 20), Is.True);
        Assert.That(f.Probe.Calls, Is.Empty);
        Assert.That(f.State.Cached(42), Is.EqualTo(closed ? null : new CachedOwner(Birth, Created, Created + 2)));
        Assert.That(f.Collector.CoverageReasons, Is.EqualTo(ProcessNetworkIncompleteReason.None));
    }

    [TestCase(-1, false)]
    [TestCase(0, false)]
    [TestCase(1, true)]
    public async Task EnsureBeforeAtAndAfterBirthKeepsTheOriginalProbeOrder(int delta, bool expected)
    {
        await using var f = new Fixture();
        Assert.That(f.Register(), Is.True);
        f.Probe.Calls.Clear();
        Assert.That(f.Ensure(42, Created + delta, Created + 10), Is.EqualTo(expected));
        Assert.That(f.Probe.Calls, Is.EqualTo(expected ? Array.Empty<string>() : new[] { "owner:42" }));
        Assert.That(f.State.Cached(42), Is.EqualTo(new CachedOwner(Birth, Created, Created + 2)));
    }

    [TestCase("same")]
    [TestCase("changed")]
    [TestCase("failed")]
    [TestCase("exited")]
    public async Task ExpiredForeignVerdictRechecksCreationBeforeReadingTheSid(string verdict)
    {
        await using var f = new Fixture();
        f.Probe.Owner = pid => new(pid, Birth, Other);
        Assert.That(f.Register(), Is.False);
        var verified = Created + 3 + Stopwatch.Frequency;
        f.Probe.Creation = _ => verdict is "failed" or "exited" ? null : verdict == "changed" ? Birth + 1 : Birth;
        if (verdict == "exited") f.Probe.FailedCreationValue = Birth;
        f.Probe.Owner = pid => new(pid, Birth + 1, Allowed);
        f.Probe.Calls.Clear();
        Assert.That(f.Ensure(42, Created + 3, verified), Is.EqualTo(verdict != "same"));
        Assert.Multiple(() =>
        {
            Assert.That(f.Probe.Calls, Is.EqualTo(verdict == "same" ? new[] { "creation:42" } : new[] { "creation:42", "owner:42" }));
            Assert.That(f.State.Rejected(42), Is.EqualTo(verdict == "same" ? new RejectedOwner(verified + Stopwatch.Frequency, false, Birth) : null));
            Assert.That(f.Collector.CoverageReasons, Is.EqualTo(ProcessNetworkIncompleteReason.None));
        });
    }

    [TestCase(-1)]
    [TestCase(0)]
    public async Task UnexpiredForeignVerdictAvoidsNativeReadsAndDoesNotMarkLoss(int delta)
    {
        await using var f = new Fixture();
        f.Probe.Owner = pid => new(pid, Birth, Other);
        Assert.That(f.Register(), Is.False);
        var until = Created + 2 + Stopwatch.Frequency;
        f.Probe.Calls.Clear();
        f.Probe.CreationError = f.Probe.OwnerError = new InvalidOperationException("Unexpired verdict must be reused");
        Assert.That(f.Ensure(42, Created + 3, until + delta), Is.False);
        Assert.Multiple(() =>
        {
            Assert.That(f.Probe.Calls, Is.Empty);
            Assert.That(f.State.Rejected(42), Is.EqualTo(new RejectedOwner(until, false, Birth)));
            Assert.That(f.State.OwnerCount, Is.Zero);
            Assert.That(f.Collector.CoverageReasons, Is.EqualTo(ProcessNetworkIncompleteReason.None));
            Assert.That(f.State.RateGap, Is.Zero);
        });
    }

    [Test]
    public async Task BeyondTheVerifiedCacheWindowRefreshesCreationWithoutReadingTheSid()
    {
        await using var f = new Fixture();
        Assert.That(f.Register(), Is.True);
        f.Probe.Calls.Clear();
        Assert.That(f.Ensure(42, Created + 3, Created + 10), Is.True);
        Assert.That(f.Probe.Calls, Is.EqualTo(new[] { "creation:42" }));
        Assert.That(f.State.Cached(42), Is.EqualTo(new CachedOwner(Birth, Created, Created + 10)));
    }

    [Test]
    public async Task CollectorsKeepIndependentProbeVerdictsAndQualityState()
    {
        await using var first = new Fixture();
        await using var second = new Fixture();
        second.Probe.Owner = _ => null;
        Assert.That(first.Register(), Is.True);
        Assert.That(second.Collector.ResolveAndRegister(42, Created + 1, Created + 2, true), Is.False);
        Assert.Multiple(() =>
        {
            Assert.That(first.State.OwnerCount, Is.EqualTo(1));
            Assert.That(first.State.RejectedCount, Is.Zero);
            Assert.That(first.Collector.CoverageReasons, Is.EqualTo(ProcessNetworkIncompleteReason.None));
            Assert.That(second.State.OwnerCount, Is.Zero);
            Assert.That(second.State.RejectedCount, Is.EqualTo(1));
            Assert.That(second.Collector.CoverageReasons, Is.EqualTo(ProcessNetworkIncompleteReason.OwnershipUnavailable));
        });
    }
}
