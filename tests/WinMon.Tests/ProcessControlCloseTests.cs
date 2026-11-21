using System.ComponentModel;
using System.Diagnostics;
using NUnit.Framework;
using WinMon.Controls;
using WinMon.Monitoring;

namespace WinMon.Tests;

[TestFixture]
[Apartment(ApartmentState.STA)]
public sealed class ProcessControlCloseTests
{
    private static readonly ProcessUsage Process = new(1_000_000, 700, "example", 0);

    [TestCase(false)]
    [TestCase(true)]
    public void Close_CancelRetainsTrackingAndLeaveDoesNotResume(bool leave)
    {
        var target = new Target();
        var tracker = Tracked(target);
        var allowed = MainWindow.ResolveSuspensionsBeforeClose(tracker,
            () => leave ? SuspendedCloseChoice.Leave : SuspendedCloseChoice.Cancel,
            _ => Assert.Fail("No cleanup was requested."), _ => target);
        Assert.Multiple(() =>
        {
            Assert.That(allowed, Is.EqualTo(leave));
            Assert.That(tracker.Contains(Process), Is.EqualTo(!leave));
            Assert.That(target.Resumes, Is.Zero);
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Close_ResumeFailureKeepsWindowAndTrackingForRetry(bool fail)
    {
        var target = new Target { FailResume = fail };
        var tracker = Tracked(target);
        IReadOnlyList<string>? failures = null;
        var allowed = MainWindow.ResolveSuspensionsBeforeClose(tracker, () => SuspendedCloseChoice.Resume,
            errors => failures = errors, _ => target);
        Assert.Multiple(() =>
        {
            Assert.That(allowed, Is.EqualTo(!fail));
            Assert.That(tracker.Contains(Process), Is.EqualTo(fail));
            Assert.That(target.Resumes, Is.EqualTo(1));
            Assert.That(failures?.Count ?? 0, Is.EqualTo(fail ? 1 : 0));
        });
        if (fail)
        {
            target.FailResume = false;
            Assert.That(MainWindow.ResolveSuspensionsBeforeClose(tracker, () => SuspendedCloseChoice.Resume,
                _ => Assert.Fail("Retry must succeed."), _ => target), Is.True);
            Assert.That(tracker.Pending, Is.Empty);
        }
    }

    [Test]
    public void Close_ConfirmedGoneSkipsPromptButDeniedReadsDoNot()
    {
        var target = new Target();
        var tracker = Tracked(target);
        Assert.That(MainWindow.ResolveSuspensionsBeforeClose(tracker, () => SuspendedCloseChoice.Cancel,
            _ => Assert.Fail(), _ => throw new Win32Exception(5)), Is.False);
        Assert.That(tracker.Contains(Process), Is.True);
        target.Exited = true;
        Assert.That(MainWindow.ResolveSuspensionsBeforeClose(tracker,
            () => throw new AssertionException("Exited lifetimes need no prompt."), _ => Assert.Fail(), _ => target), Is.True);
        Assert.That(target.Resumes, Is.Zero);
        Assert.That(tracker.Pending, Is.Empty);
    }

    private static SuspendedProcessTracker Tracked(Target target)
    {
        var tracker = new SuspendedProcessTracker();
        Assert.That(tracker.Suspend(Process, _ => target, () => new(null, false)), Is.Null);
        return tracker;
    }

    private sealed class Target : IProcessControlTarget
    {
        public long CreationTicks => 700;
        public bool Exited { get; set; }
        public bool HasExited => Exited;
        public bool IsCritical => false;
        public bool FailResume { get; set; }
        public int Resumes { get; private set; }
        public void Suspend() { }
        public void Resume() { Resumes++; if (FailResume) throw new Win32Exception(5); }
        public void Dispose() { }
        public ulong AffinityMask => throw new AssertionException("Not an affinity operation.");
        public ulong SystemAffinityMask => throw new AssertionException("Not an affinity operation.");
        public ushort ProcessGroupCount => throw new AssertionException("Not an affinity operation.");
        public ProcessPriorityClass PriorityClass => throw new AssertionException("Not a priority operation.");
        public ProcessPowerThrottlingState PowerThrottling => throw new AssertionException("Not a throttling operation.");
        public void SetAffinityMask(ulong mask) => Assert.Fail();
        public void SetPriorityClass(ProcessPriorityClass value) => Assert.Fail();
        public void SetPowerThrottling(ProcessPowerThrottlingState value) => Assert.Fail();
    }
}
