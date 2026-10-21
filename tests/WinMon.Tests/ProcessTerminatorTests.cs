using System.ComponentModel;
using NUnit.Framework;
using WinMon.Monitoring;

namespace WinMon.Tests;

[TestFixture]
public sealed class ProcessTerminatorTests
{
    private static ProcessUsage Process => new(123456, 700, "example", 1);

    [Test]
    public void End_RejectsReusedPidAndDisposesHandle()
    {
        var target = new Target { CreationTicks = 701 };
        Assert.That(ProcessTerminator.End(Process, _ => target), Does.Contain("reused"));
        Assert.That(target.Terminated, Is.False);
        Assert.That(target.Disposed, Is.True);
    }

    [Test]
    public void End_RejectsCriticalAndExitedProcesses()
    {
        foreach (var target in new[] { new Target { IsCritical = true }, new Target { HasExited = true } })
        {
            Assert.That(ProcessTerminator.End(Process, _ => target), Is.Not.Null);
            Assert.That(target.Terminated, Is.False);
            Assert.That(target.Disposed, Is.True);
        }
    }

    [Test]
    public void End_FailsClosedWhenCriticalStatusCannotBeRead()
    {
        var target = new Target { CriticalError = new Win32Exception(5) };
        Assert.That(ProcessTerminator.End(Process, _ => target), Does.Contain("Access denied"));
        Assert.That(target.Terminated, Is.False);
        Assert.That(target.Disposed, Is.True);
    }

    [Test]
    public void End_ReportsAccessDenied()
    {
        Assert.That(ProcessTerminator.End(Process, _ => throw new Win32Exception(5)), Does.Contain("Access denied"));
    }

    [Test]
    public void End_UsesValidatedTargetAndDisposesIt()
    {
        var target = new Target();
        Assert.That(ProcessTerminator.End(Process, pid => { Assert.That(pid, Is.EqualTo(Process.ProcessId)); return target; }), Is.Null);
        Assert.That(target.Terminated, Is.True);
        Assert.That(target.Disposed, Is.True);
    }

    [Test]
    public void Eligibility_RejectsHistoricalGroupedUnknownAndSelfTargets()
    {
        Assert.That(ProcessTerminator.IsEligible(Process, false, false), Is.False);
        Assert.That(ProcessTerminator.IsEligible(Process, true, true), Is.False);
        Assert.That(ProcessTerminator.IsEligible(Process with { MemberCount = 2 }, true, false), Is.False);
        Assert.That(ProcessTerminator.IsEligible(Process with { CreationTicks = 0 }, true, false), Is.False);
        Assert.That(ProcessTerminator.IsEligible(Process with { ProcessId = Environment.ProcessId }, true, false), Is.False);
        Assert.That(ProcessTerminator.IsEligible(Process with { ProcessId = 4 }, true, false), Is.False);
        Assert.That(ProcessTerminator.IsEligible(Process, true, false), Is.True);
    }

    [Test]
    public void GroupTargets_IncludeIdleMembersAndExcludeOtherExecutablePaths()
    {
        var first = Process with { ImagePath = @"C:\Apps\example.exe" };
        var idle = first with { ProcessId = 123457, CreationTicks = 800, CpuPercent = 0 };
        var other = first with { ProcessId = 123458, ImagePath = @"D:\Apps\example.exe" };
        var row = first with { MemberCount = 3 };
        var targets = ProcessTerminator.CaptureTargets(row, true, true, [first, idle, other]);
        Assert.That(targets, Is.EqualTo(new[] { first, idle }));
        Assert.That(ProcessTerminator.CaptureTargets(row, false, true, [first, idle]), Is.Empty);
        Assert.That(ProcessTerminator.CaptureTargets(row, true, true, []), Is.Empty);
    }

    [Test]
    public void GroupTargets_RejectWholeGroupWithProtectedOrUnknownMember()
    {
        foreach (var member in new[] { Process with { CreationTicks = 0 },
                     Process with { ProcessId = 4 }, Process with { ProcessId = Environment.ProcessId } })
            Assert.That(ProcessTerminator.CaptureTargets(Process, true, true, [Process, member]), Is.Empty);
        Assert.That(ProcessTerminator.CaptureTargets(Process, true, true, [Process]), Has.Length.EqualTo(1));
    }

    [Test]
    public void EndAll_ContinuesAfterFailureAndChecksEveryCapturedLifetime()
    {
        var first = new Target { CreationTicks = 701 };
        var second = new Target();
        var third = new Target { IsCritical = true };
        var targets = new[] { Process, Process with { ProcessId = 123457 }, Process with { ProcessId = 123458 } };
        var opened = new List<int>();
        var errors = ProcessTerminator.EndAll(targets, pid =>
        {
            opened.Add(pid);
            return pid == 123456 ? first : pid == 123457 ? second : third;
        });
        Assert.That(opened, Is.EqualTo(targets.Select(p => p.ProcessId)));
        Assert.That(first.Terminated, Is.False);
        Assert.That(second.Terminated, Is.True);
        Assert.That(third.Terminated, Is.False);
        Assert.That(errors, Has.Count.EqualTo(2));
        Assert.That(errors[0], Does.Contain("123456").And.Contain("reused"));
        Assert.That(new[] { first, second, third }.All(t => t.Disposed), Is.True);
    }

    [Test]
    public void Native_EndRejectsWrongLifetimeThenEndsOnlyOwnedHelper()
    {
        using var helper = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe"),
            Arguments = "-NoProfile -NonInteractive -Command Start-Sleep -Seconds 60",
            UseShellExecute = false, CreateNoWindow = true, WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden
        })!;
        try
        {
            var usage = new ProcessUsage(helper.Id, helper.StartTime.ToUniversalTime().ToFileTimeUtc(), "WinMon test helper", 0);
            Assert.That(ProcessTerminator.End(usage with { CreationTicks = usage.CreationTicks + 1 }), Does.Contain("reused"));
            Assert.That(helper.HasExited, Is.False);
            Assert.That(ProcessTerminator.End(usage), Is.Null);
            Assert.That(helper.WaitForExit(5000), Is.True);
        }
        finally
        {
            if (!helper.HasExited) { helper.Kill(); helper.WaitForExit(5000); }
        }
    }

    private sealed class Target : ProcessTerminator.ITarget
    {
        public long CreationTicks { get; init; } = 700;
        public bool HasExited { get; init; }
        public Exception? CriticalError { get; init; }
        private readonly bool _critical;
        public bool IsCritical { get => CriticalError is null ? _critical : throw CriticalError; init => _critical = value; }
        public bool Terminated { get; private set; }
        public bool Disposed { get; private set; }
        public void Terminate() => Terminated = true;
        public void Dispose() => Disposed = true;
    }
}
