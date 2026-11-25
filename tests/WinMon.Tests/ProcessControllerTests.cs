using System.ComponentModel;
using System.Diagnostics;
using MonCore.Monitoring;
using MonCore.Windows.Monitoring;
using NUnit.Framework;
using WinMon.Monitoring;
using WinMon.Resources;

namespace WinMon.Tests;

[TestFixture]
public sealed class ProcessControllerTests
{
    private static readonly ProcessUsage Process = new(42424, 700, "example", 1);

    [Test]
    public void Suspend_ValidatesLifetimeAndTrackerBalancesOnePair()
    {
        var target = new Target();
        var tracker = new SuspendedProcessTracker();
        Assert.That(tracker.Suspend(Process, _ => target, NoService), Is.Null);
        Assert.That(target.Suspended, Is.EqualTo(1));
        Assert.That(tracker.Suspend(Process, _ => throw new AssertionException("should not open"), NoService), Is.Not.Null);
        Assert.That(tracker.Resume(Process, _ => target), Is.Null);
        Assert.That(target.Resumed, Is.EqualTo(1));
        Assert.That(tracker.Contains(Process), Is.False);
    }

    [Test]
    public void ResumeFailure_RetainsTrackerEntry()
    {
        var tracker = new SuspendedProcessTracker();
        Assert.That(tracker.Suspend(Process, _ => new Target(), NoService), Is.Null);
        Assert.That(tracker.Resume(Process, _ => throw new Win32Exception(5)), Is.Not.Null);
        Assert.That(tracker.Contains(Process), Is.True);
    }

    [Test]
    public void Resume_ConfirmedReusedLifetimeRemovesOutstandingEntryWithoutResuming()
    {
        var tracker = new SuspendedProcessTracker();
        Assert.That(tracker.Suspend(Process, _ => new Target(), NoService), Is.Null);
        var reused = new Target { CreationTicks = 701 };
        Assert.That(tracker.Resume(Process, _ => reused), Is.Null);
        Assert.That(reused.Resumed, Is.Zero);
        Assert.That(tracker.Contains(Process), Is.False);
    }

    [Test]
    public void Resume_RejectsExternalSuspension()
    {
        var tracker = new SuspendedProcessTracker();
        Assert.That(tracker.Resume(Process, _ => throw new AssertionException("external suspension must not open")),
            Is.EqualTo(Strings.ResumeProcessUnavailable));
    }

    [Test]
    public void Suspend_RejectsCriticalAndReusedLifetimes()
    {
        Assert.That(ProcessController.Suspend(Process, _ => new Target { IsCritical = true }, NoService), Is.Not.Null);
        Assert.That(ProcessController.Suspend(Process, _ => new Target { CreationTicks = 701 }, NoService), Does.Contain("reused"));
    }

    [Test]
    public void Affinity_RejectsEmptyAndProcessorsOutsideSystemMask()
    {
        Assert.That(ProcessController.SetAffinity(Process, 0, _ => throw new AssertionException("should not open")), Is.Not.Null);
        var target = new Target { SystemMask = 3 };
        Assert.That(ProcessController.SetAffinity(Process, 4, _ => target), Is.Not.Null);
        Assert.That(target.AffinityWasSet, Is.False);
        var multiGroup = new Target { GroupCount = 2 };
        Assert.That(ProcessController.SetAffinity(Process, 1, _ => multiGroup), Is.EqualTo(Strings.CpuAffinityReadOnly));
        Assert.That(multiGroup.AffinityWasSet, Is.False);
    }

    [TestCase(false, false)]
    [TestCase(true, false)]
    [TestCase(false, true)]
    public void Eligibility_RejectsNonLiveGroupedAndUnknownProcesses(bool live, bool grouped)
    {
        var candidates = new[]
        {
            Process with { ProcessId = 4 }, Process with { ProcessId = Environment.ProcessId },
            Process with { CreationTicks = 0 }, Process with { MemberIds = [999] }
        };
        Assert.That(ProcessController.IsEligible(Process, live, grouped), Is.EqualTo(live && !grouped));
        foreach (var candidate in candidates)
            Assert.That(ProcessController.IsEligible(candidate, true, false), Is.False);
    }

    [Test]
    public void EveryAction_RejectsReusedExitedCriticalAndDeniedTargetsWithoutSideEffects()
    {
        foreach (var target in new[]
                 {
                     new Target { CreationTicks = 701 }, new Target { HasExited = true }, new Target { IsCritical = true }
                 })
        {
            Assert.That(ProcessController.Suspend(Process, _ => target, NoService), Is.Not.Null);
            Assert.That(target.Suspended, Is.Zero);
            Assert.That(ProcessController.SetAffinity(Process, 1, _ => target), Is.Not.Null);
            Assert.That(target.AffinityWasSet, Is.False);
        }
        Assert.That(ProcessController.Suspend(Process, _ => throw new Win32Exception(5), NoService), Does.Contain("Access denied"));
        Assert.That(ProcessController.SetAffinity(Process, 1, _ => throw new Win32Exception(5)), Does.Contain("Access denied"));
    }

    [Test]
    public void Suspend_RejectsDwmWithoutOpeningTarget()
    {
        var dwm = Process with { Name = "dwm.exe" };
        Assert.That(ProcessController.Suspend(dwm, _ => throw new AssertionException("dwm must not open"), NoService),
            Is.EqualTo(Strings.SuspendProcessUnavailable));
    }

    [Test]
    public void Efficiency_UsesExplicitOffAndRollsBackPowerWhenPriorityFails()
    {
        var target = new Target { Power = new(8, 0), PriorityClass = ProcessPriorityClass.Normal };
        Assert.That(ProcessController.SetEfficiency(Process, false, _ => target), Is.Null);
        Assert.That(target.Power.ControlMask & ProcessPowerThrottlingState.ExecutionSpeed, Is.Not.EqualTo(0));
        Assert.That(ProcessController.ReadEfficiency(Process, _ => target), Is.EqualTo(EfficiencyModeState.Off));

        var failing = new Target { FailPriority = true, Power = new(8, 2) };
        Assert.That(ProcessController.SetEfficiency(Process, true, _ => failing), Is.Not.Null);
        Assert.That(failing.Power, Is.EqualTo(new ProcessPowerThrottlingState(8, 2)));
    }

    [Test]
    public void Efficiency_PreservesUnrelatedPowerFlagsAndReportsMixedState()
    {
        var target = new Target { Power = new(8, 4), PriorityClass = ProcessPriorityClass.Normal };
        Assert.That(ProcessController.ReadEfficiency(Process, _ => target), Is.EqualTo(EfficiencyModeState.Mixed));
        Assert.That(ProcessController.SetEfficiency(Process, true, _ => target), Is.Null);
        Assert.That(target.Power.ControlMask, Is.EqualTo(9));
        Assert.That(target.Power.StateMask, Is.EqualTo(5));
        Assert.That(target.PriorityClass, Is.EqualTo(ProcessPriorityClass.Idle));
    }

    [Test]
    public void Efficiency_PowerWriteFailureDoesNotChangePriorityAndUnsupportedIsUnavailable()
    {
        var failing = new Target { PowerWriteError = new Win32Exception(5) };
        Assert.That(ProcessController.SetEfficiency(Process, true, _ => failing), Is.Not.Null);
        Assert.That(failing.PriorityWrites, Is.Zero);
        Assert.That(ProcessController.SetEfficiency(Process, true, _ => throw new Win32Exception(87)),
            Is.EqualTo(Strings.EfficiencyModeUnavailable));
    }

    [Test]
    public void Efficiency_PreReadFailureDoesNotWriteAndStateReadFailureIsUnavailable()
    {
        var powerFailure = new Target { PowerReadError = new Win32Exception(5) };
        Assert.That(ProcessController.SetEfficiency(Process, true, _ => powerFailure), Does.Contain("Access denied"));
        Assert.That(powerFailure.PriorityWrites, Is.Zero);
        Assert.That(powerFailure.PowerWrites, Is.Zero);
        Assert.That(ProcessController.ReadEfficiency(Process, _ => powerFailure), Is.EqualTo(EfficiencyModeState.Unavailable));
    }

    [Test]
    public void Suspend_DeniesTheRegisteredServiceAndUnreadableServiceStatus()
    {
        var target = new Target();
        Assert.That(ProcessController.Suspend(Process, _ => target,
            () => new NetworkServiceProcessStatus(Process.ProcessId, false)), Is.EqualTo(Strings.SuspendProcessUnavailable));
        Assert.That(target.Suspended, Is.Zero);
        Assert.That(ProcessController.Suspend(Process, _ => target,
            () => new NetworkServiceProcessStatus(null, true)), Is.EqualTo(Strings.SuspendProcessUnavailable));
    }

    [Test]
    public void Tracker_RemovesOnlyConfirmedExitedLifetime()
    {
        var tracker = new SuspendedProcessTracker();
        Assert.That(tracker.Suspend(Process, _ => new Target(), NoService), Is.Null);
        tracker.RemoveExited(_ => new Target { HasExited = true });
        Assert.That(tracker.Contains(Process), Is.False);

        Assert.That(tracker.Suspend(Process, _ => new Target(), NoService), Is.Null);
        tracker.RemoveExited(_ => throw new Win32Exception(5));
        Assert.That(tracker.Contains(Process), Is.True);
    }

    private static NetworkServiceProcessStatus NoService() => new(null, false);

    [Test]
    public void Efficiency_RollbackFailureReportsBothStepsAndRereadsBothValues()
    {
        var target = new Target { FailPriority = true, FailRollback = true };
        var error = ProcessController.SetEfficiency(Process, true, _ => target);
        Assert.Multiple(() =>
        {
            Assert.That(error, Does.Contain("rollback denied"));
            Assert.That(target.PowerWrites, Is.EqualTo(2));
            Assert.That(target.PowerReads, Is.EqualTo(2));
            Assert.That(target.PriorityReads, Is.EqualTo(2));
        });
    }

    [Test]
    public void Tracker_ConcurrentSuspendAllowsOnlyOneNativeRequest()
    {
        var tracker = new SuspendedProcessTracker();
        var target = new Target();
        Parallel.For(0, 16, iteration => tracker.Suspend(Process, id => target, NoService));
        Assert.That(target.Suspended, Is.EqualTo(1));
        Assert.That(tracker.Pending, Has.Count.EqualTo(1));
    }

    private sealed class Target : IProcessControlTarget
    {
        public long CreationTicks { get; init; } = 700;
        public bool HasExited { get; init; }
        public bool IsCritical { get; init; }
        public int Suspended { get; private set; }
        public int Resumed { get; private set; }
        public ulong AffinityMask { get; private set; } = 3;
        public ulong SystemAffinityMask { get => SystemMask; }
        public ushort GroupCount { get; init; } = 1;
        public ushort ProcessGroupCount => GroupCount;
        public ulong SystemMask { get; init; } = 3;
        public bool AffinityWasSet { get; private set; }

        public ProcessPriorityClass PriorityClass
        {
            get
            {
                PriorityReads++;
                return PriorityReadError is null ? field : throw PriorityReadError;
            }
            set;
        } = ProcessPriorityClass.Normal;

        public ProcessPowerThrottlingState Power { get; set; } = new(0, 0);
        public bool FailPriority { get; init; }
        public bool FailRollback { get; init; }
        public Exception? PowerWriteError { get; init; }
        public Exception? PowerReadError { get; init; }
        public Exception? PriorityReadError { get; init; }
        public int PriorityWrites { get; private set; }
        public int PowerWrites { get; private set; }
        public int PowerReads { get; private set; }
        public int PriorityReads { get; private set; }
        public ProcessPowerThrottlingState PowerThrottling { get { PowerReads++; return PowerReadError is null ? Power : throw PowerReadError; } }
        public void Suspend() => Suspended++;
        public void Resume() => Resumed++;
        public void SetAffinityMask(ulong mask) { AffinityMask = mask; AffinityWasSet = true; }
        public void SetPriorityClass(ProcessPriorityClass value) { PriorityWrites++; if (FailPriority) throw new Win32Exception(5); PriorityClass = value; }
        public void SetPowerThrottling(ProcessPowerThrottlingState value) { PowerWrites++; if (FailRollback && PowerWrites > 1) throw new Win32Exception(5, "rollback denied"); if (PowerWriteError is not null) throw PowerWriteError; Power = value; }
        public void Dispose() { }
    }
}
