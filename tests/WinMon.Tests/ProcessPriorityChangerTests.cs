using System.ComponentModel;
using System.Diagnostics;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using NUnit.Framework;
using WinMon.Controls;
using WinMon.Monitoring;
using WinMon.Resources;

namespace WinMon.Tests;

[TestFixture, SetUICulture("en")]
public sealed class ProcessPriorityChangerTests
{
    private static ProcessUsage Process => new(123456, 700, "example", 1);

    [Test]
    public void Set_RejectsReusedExitedCriticalAndUnsupportedTargets()
    {
        foreach (var target in new[]
                 {
                     new Target { CreationTicks = 701 }, new Target { HasExited = true },
                     new Target { IsCritical = true }
                 })
        {
            Assert.That(ProcessPriorityChanger.Set(Process, ProcessPriorityClass.Normal, _ => target), Is.Not.Null);
            Assert.That(target.SetTo, Is.Null);
            Assert.That(target.Disposed, Is.True);
        }
        Assert.That(ProcessPriorityChanger.Set(Process, ProcessPriorityClass.High, _ => throw new AssertionException("opened")),
            Is.EqualTo(Strings.ProcessPriorityUnavailable));
        Assert.That(ProcessPriorityChanger.Set(Process, ProcessPriorityClass.RealTime, _ => throw new AssertionException("opened")),
            Is.EqualTo(Strings.ProcessPriorityUnavailable));
    }

    [Test]
    public void Set_UsesValidatedHandleAndReportsDeniedAccess()
    {
        var target = new Target();
        Assert.That(ProcessPriorityChanger.Set(Process, ProcessPriorityClass.BelowNormal, _ => target), Is.Null);
        Assert.That(target.SetTo, Is.EqualTo(ProcessPriorityClass.BelowNormal));
        Assert.That(target.Disposed, Is.True);
        Assert.That(ProcessPriorityChanger.Set(Process, ProcessPriorityClass.Normal,
            _ => throw new Win32Exception(5)), Does.Contain("Access denied"));
    }

    [Test]
    public void Eligibility_RequiresIndividualLiveKnownLifetime()
    {
        Assert.That(ProcessPriorityChanger.IsEligible(Process, true, false), Is.True);
        Assert.That(ProcessPriorityChanger.IsEligible(Process, false, false), Is.False);
        Assert.That(ProcessPriorityChanger.IsEligible(Process, true, true), Is.False);
        Assert.That(ProcessPriorityChanger.IsEligible(Process with { CreationTicks = 0 }, true, false), Is.False);
        Assert.That(ProcessPriorityChanger.IsEligible(Process with { ProcessId = Environment.ProcessId }, true, false), Is.False);
        Assert.That(ProcessPriorityChanger.IsEligible(Process with { MemberIds = [123456] }, true, false), Is.False);
    }

    [Test]
    public void Native_ChangesOnlyTheOwnedHelperProcess()
    {
        using var helper = System.Diagnostics.Process.Start(new ProcessStartInfo
        {
            FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
                @"WindowsPowerShell\v1.0\powershell.exe"),
            Arguments = "-NoProfile -NonInteractive -Command Start-Sleep -Seconds 60",
            UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden
        })!;
        try
        {
            var usage = new ProcessUsage(helper.Id, helper.StartTime.ToUniversalTime().ToFileTimeUtc(),
                "WinMon priority test helper", 0);
            Assert.That(ProcessPriorityChanger.Set(usage with { CreationTicks = usage.CreationTicks + 1 },
                ProcessPriorityClass.BelowNormal), Does.Contain("reused"));
            Assert.That(ProcessPriorityChanger.Set(usage, ProcessPriorityClass.BelowNormal), Is.Null);
            Assert.That(ProcessPriorityChanger.Read(usage), Is.EqualTo(ProcessPriorityClass.BelowNormal));
        }
        finally
        {
            if (!helper.HasExited) { helper.Kill(); helper.WaitForExit(5000); }
        }
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void Menu_ShowsCurrentClassAndCapturesOriginalTarget()
    {
        var selected = Process;
        ProcessUsage? changed = null;
        ProcessPriorityClass? requested = null;
        var menu = ProcessPriorityMenu.Create(selected, true, _ => ProcessPriorityClass.Normal,
            (process, priority) => { changed = process; requested = priority; });
        var choices = menu.Items.OfType<MenuItem>().ToArray();
        Assert.That(menu.Header, Is.EqualTo(Strings.SetPriority));
        Assert.That(choices.Select(i => i.Header), Is.EqualTo(new[]
            { Strings.PriorityLow, Strings.PriorityBelowNormal, Strings.PriorityNormal, Strings.PriorityAboveNormal }));
        Assert.That(choices[2].IsChecked, Is.True);
        Assert.That(choices[2].IsEnabled, Is.False);
        selected = selected with { ProcessId = 999 };
        choices[1].RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Assert.That(changed, Is.EqualTo(Process));
        Assert.That(requested, Is.EqualTo(ProcessPriorityClass.BelowNormal));
        Assert.That(ProcessPriorityMenu.Create(Process, false, _ => throw new AssertionException("read"),
            (_, _) => { }).IsEnabled, Is.False);
    }

    private sealed class Target : ProcessPriorityChanger.ITarget
    {
        public long CreationTicks { get; init; } = 700;
        public bool HasExited { get; init; }
        public bool IsCritical { get; init; }
        public ProcessPriorityClass PriorityClass => ProcessPriorityClass.Normal;
        public ProcessPriorityClass? SetTo { get; private set; }
        public bool Disposed { get; private set; }
        public void SetPriorityClass(ProcessPriorityClass priority) => SetTo = priority;
        public void Dispose() => Disposed = true;
    }
}
