using System.Diagnostics;
using NUnit.Framework;
using WinMon.Monitoring;

namespace WinMon.Tests;

[TestFixture]
[Category("Integration")]
public sealed class WindowsProcessControlTests
{
    [Test]
    public void OwnedHelper_ControlsAndRestoresTheSameProcessLifetime()
    {
        using var helper = Process.Start(new ProcessStartInfo
        {
            FileName = Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe"),
            Arguments = "-NoProfile -NonInteractive -Command \"[Console]::WriteLine('ready'); Start-Sleep -Seconds 60\"",
            UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        })!;
        var suspended = false;
        IProcessControlTarget? target = null;
        try
        {
            Assert.That(helper.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(15)).GetAwaiter().GetResult(),
                Is.EqualTo("ready"));
            target = WindowsProcessControlTarget.Open(helper.Id,
                WindowsProcessControlTarget.ProcessControlAccess.Suspend |
                WindowsProcessControlTarget.ProcessControlAccess.Affinity);
            var created = helper.StartTime.ToUniversalTime().ToFileTimeUtc();
            Assert.Multiple(() =>
            {
                Assert.That(target.CreationTicks, Is.EqualTo(created));
                Assert.That(target.HasExited, Is.False);
                Assert.That(target.IsCritical, Is.False);
                Assert.That(target.ProcessGroupCount, Is.GreaterThan(0));
            });

            target.Suspend();
            suspended = true;
            Assert.That(SpinWait.SpinUntil(() => WindowsProcessSuspension.IsSuspended(helper.Id, created) == true,
                TimeSpan.FromSeconds(3)), Is.True, "The native thread snapshot must observe the suspension.");
            target.Resume();
            suspended = false;
            Assert.That(SpinWait.SpinUntil(() => WindowsProcessSuspension.IsSuspended(helper.Id, created) == false,
                TimeSpan.FromSeconds(3)), Is.True);

            if (target.ProcessGroupCount == 1)
            {
                var original = target.AffinityMask;
                var single = original & (~original + 1);
                Assert.That(single, Is.Not.Zero);
                target.SetAffinityMask(single);
                Assert.That(target.AffinityMask, Is.EqualTo(single));
                target.SetAffinityMask(original);
                Assert.That(target.AffinityMask, Is.EqualTo(original));
            }

            if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 16299))
            {
                var originalPower = target.PowerThrottling;
                var originalPriority = target.PriorityClass;
                target.SetPowerThrottling(new(originalPower.ControlMask | ProcessPowerThrottlingState.ExecutionSpeed,
                    originalPower.StateMask | ProcessPowerThrottlingState.ExecutionSpeed));
                target.SetPriorityClass(ProcessPriorityClass.Idle);
                Assert.Multiple(() =>
                {
                    Assert.That(target.PowerThrottling.StateMask & ProcessPowerThrottlingState.ExecutionSpeed, Is.EqualTo(1));
                    Assert.That(target.PriorityClass, Is.EqualTo(ProcessPriorityClass.Idle));
                });
                target.SetPowerThrottling(originalPower);
                target.SetPriorityClass(originalPriority);
                Assert.Multiple(() =>
                {
                    Assert.That(target.PowerThrottling, Is.EqualTo(originalPower));
                    Assert.That(target.PriorityClass, Is.EqualTo(originalPriority));
                });
            }
        }
        finally
        {
            try { if (suspended && target is not null) target.Resume(); }
            finally
            {
                target?.Dispose();
                if (!helper.HasExited) helper.Kill();
                helper.WaitForExit(5000);
            }
        }
    }
}
