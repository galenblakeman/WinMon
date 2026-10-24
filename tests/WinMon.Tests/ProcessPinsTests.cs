using System.Threading;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using NUnit.Framework;
using WinMon.Controls;
using WinMon.Monitoring;
using WinMon.Resources;

namespace WinMon.Tests;

[TestFixture]
public sealed class ProcessPinsTests
{
    private static ProcessUsage Process(int pid, long created = 10, string path = @"C:\Apps\example.exe") =>
        new(pid, created, "example", 0, 123, 0) { ImagePath = path, ThreadCount = 4, HandleCount = 8 };
    private static MonitorSample Sample(params ProcessUsage[] processes) => new(DateTimeOffset.Now, 10, processes, 0);

    [Test]
    public void Pins_PreserveInsertionOrderAndNeverFollowReusedPid()
    {
        var pins = new ProcessPins();
        var first = Process(20);
        var second = Process(10);
        pins.Toggle(first, false);
        pins.Toggle(second, false);
        var reused = first with { CreationTicks = 99, CpuPercent = 50 };
        var rows = pins.Resolve(Sample(second, reused), [reused, second], false, false);
        Assert.Multiple(() =>
        {
            Assert.That(rows.Select(p => p.Usage.ProcessId), Is.EqualTo(new[] { 20, 10 }));
            Assert.That(rows[0].State, Is.EqualTo(PinnedProcessState.Exited));
            Assert.That(rows[0].Usage.WorkingSetBytes, Is.Null);
            Assert.That(pins.Contains(reused, false), Is.False);
            Assert.That(rows[1].State, Is.EqualTo(PinnedProcessState.Present));
        });
        pins.Toggle(first, false);
        Assert.That(pins.Count(false), Is.EqualTo(1));
    }

    [Test]
    public void IdlePin_UsesWindowAverageAndLatestMemory()
    {
        var pins = new ProcessPins();
        var idle = Process(20);
        pins.Toggle(idle, false);
        var averaged = idle with { CpuPercent = 1.2, WorkingSetBytes = 456 };
        var row = pins.Resolve(Sample(idle), [averaged], false, false).Single();
        Assert.That(row, Is.EqualTo(new PinnedProcess(averaged, PinnedProcessState.Present)));
    }

    [Test]
    public void GroupPins_TrackExecutableAndRemainSeparateFromIndividualPins()
    {
        var pins = new ProcessPins();
        var original = Process(20);
        pins.Toggle(original, true);
        pins.Toggle(original, false);
        var replacement = Process(30, 99, @"c:\apps\EXAMPLE.exe");
        var otherPath = Process(40, 99, @"D:\Apps\example.exe");
        Assert.Multiple(() =>
        {
            Assert.That(pins.Contains(replacement, true), Is.True);
            Assert.That(pins.Contains(replacement, false), Is.False);
            Assert.That(pins.Contains(otherPath, true), Is.False);
            Assert.That(pins.Resolve(Sample(replacement), [replacement], true, false).Single().State,
                Is.EqualTo(PinnedProcessState.Present));
        });
        pins.Toggle(original, false);
        Assert.That(pins.Count(true), Is.EqualTo(1));
    }

    [Test]
    public void GapsAndHistoricalOmissions_DoNotClaimExitOrExposeOldReadings()
    {
        var pins = new ProcessPins();
        var process = Process(20) with { CpuPercent = 42 };
        pins.Toggle(process, false);
        Assert.Multiple(() =>
        {
            Assert.That(pins.Resolve(Sample() with { IsGap = true }, [], false, false).Single().State,
                Is.EqualTo(PinnedProcessState.Unavailable));
            Assert.That(pins.Resolve(Sample(), [], false, true).Single().State,
                Is.EqualTo(PinnedProcessState.NotRecorded));
            Assert.That(pins.Resolve(null, [], false, true).Single().State,
                Is.EqualTo(PinnedProcessState.Unavailable));
            Assert.That(pins.Resolve(Sample() with { UnavailableProcesses = 1 }, [], false, false).Single().State,
                Is.EqualTo(PinnedProcessState.Unavailable));
        });
    }

    [Test]
    public void RetentionSnapshot_PreservesIdlePinsWithoutFollowingLaterUiMutations()
    {
        var pins = new ProcessPins();
        var process = Process(20);
        pins.Toggle(process, false);
        var snapshot = pins.RetentionSnapshot();
        pins.Toggle(process, false);
        var all = new[] { process }.Concat(Enumerable.Range(100, ProcessRetention.LargestByMemory + 8).Select(i => Process(i) with
            { WorkingSetBytes = 1000, ThreadCount = 100, HandleCount = 100 })).ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(ProcessRetention.Compact(Sample(all)).Processes, Does.Not.Contain(process));
            Assert.That(ProcessRetention.Compact(Sample(all), retain: snapshot).Processes, Does.Contain(process));
            Assert.That(pins.RetentionSnapshot(), Is.Null);
        });
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void MenuPin_CapturesIdentityAndModeBeforeRowIsReused()
    {
        var row = new ProcessRow();
        ProcessUsage? toggled = null;
        bool? grouped = null;
        row.EnableProcessActions(_ => true, _ => { });
        row.EnablePinning((_, _) => false, (p, g) => { toggled = p; grouped = g; });
        var first = Process(20);
        row.Update(first, [], 120, true, null);
        Assert.That(row.PrepareProcessMenu(), Is.True);
        var pin = (MenuItem)row.ContextMenu!.Items[0];
        row.Update(Process(30), [], 120, true, null, true);
        pin.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Assert.Multiple(() =>
        {
            Assert.That(toggled, Is.EqualTo(first));
            Assert.That(grouped, Is.False);
        });
        row.Update(first, [], 120, true, null, state: PinnedProcessState.Exited);
        row.PrepareProcessMenu();
        Assert.That(((MenuItem)row.ContextMenu.Items[1]).IsEnabled, Is.False);
        Assert.That(row.Children.OfType<TextBlock>().Where(t => Grid.GetColumn(t) >= 2).Select(t => t.Text),
            Is.All.EqualTo(Strings.MissingReading));
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void AutomationPeer_ExposesStableRowNameReadingsAndOnlyThePinButton()
    {
        var row = new ProcessRow();
        Assert.That(UIElementAutomationPeer.CreatePeerForElement(row).GetChildren(), Is.Null);
        row.EnablePinning((_, _) => false, (_, _) => { });
        row.Update(Process(20) with { CpuPercent = 42 }, [], 120, true, null);
        var peer = UIElementAutomationPeer.CreatePeerForElement(row);
        var name = peer.GetName();
        row.Update(Process(20) with { CpuPercent = 7 }, [], 120, true, null);
        peer.ResetChildrenCache();
        var children = peer.GetChildren();
        Assert.Multiple(() =>
        {
            Assert.That(peer.GetClassName(), Is.EqualTo(nameof(ProcessRow)));
            Assert.That(peer.GetAutomationControlType(), Is.EqualTo(AutomationControlType.DataItem));
            Assert.That(name, Does.StartWith("example ·").And.Contain("20"));
            Assert.That(peer.GetName(), Is.EqualTo(name), "readings must not change the name");
            Assert.That(peer.GetHelpText(), Is.EqualTo(AutomationProperties.GetName(row)));
            Assert.That(children, Has.Count.EqualTo(1));
            Assert.That(children[0].GetAutomationControlType(), Is.EqualTo(AutomationControlType.Button));
            Assert.That(children[0].GetName(), Is.EqualTo($"{Strings.PinProcess}: example"));
        });
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void RowIdentity_FollowsLifetimeOrExecutableGroup()
    {
        var row = new ProcessRow();
        Assert.That(row.Identity, Is.Null);
        row.Update(Process(20), [], 120, true, null);
        Assert.Multiple(() =>
        {
            Assert.That(row.Identity, Is.EqualTo(ProcessRow.IdentityOf(Process(20) with { CpuPercent = 9 }, false)));
            Assert.That(row.Identity, Is.Not.EqualTo(ProcessRow.IdentityOf(Process(20, created: 11), false)));
            Assert.That(ProcessRow.IdentityOf(Process(20), true), Is.EqualTo(ProcessRow.IdentityOf(Process(30), true)));
        });
    }

    [TestCase(new int[0], new bool[0])]
    [TestCase(new[] { 0, 1, 2 }, new[] { true, true, true })]
    [TestCase(new[] { 2, 0, 1 }, new[] { false, true, true })]
    [TestCase(new[] { 1, 0, 3, 2, 4 }, new[] { false, true, false, true, true })]
    public void LongestIncreasingRun_KeepsTheMostRowsInPlace(int[] positions, bool[] staying)
    {
        Assert.That(MainWindow.LongestIncreasingRun(positions), Is.EqualTo(staying));
    }

    [Test]
    public void SnapshotExport_MarksMissingPinnedRowsUnavailable()
    {
        var process = Process(20) with { CpuPercent = 42 };
        var report = new SnapshotReport(Sample(), [process], false, "CPU", "", false, "", null)
            { ProcessStates = [Strings.ProcessNotRecorded] };
        var text = SnapshotText.Format(report);
        Assert.That(text, Does.Contain($"example · {Strings.ProcessNotRecorded}"));
        Assert.That(text, Does.Not.Contain("42.0"));
    }
}
