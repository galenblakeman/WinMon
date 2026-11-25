using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using NUnit.Framework;
using WinMon.Controls;
using WinMon.Monitoring;
using WinMon.Resources;

namespace WinMon.Tests;

[TestFixture, NonParallelizable, Apartment(ApartmentState.STA), SetCulture("en-US"), SetUICulture("en-US")]
public sealed class ProcessMenuCharacterizationTests
{
    [TestCaseSource(typeof(PreparedProcessMenu), nameof(PreparedProcessMenu.Cases))]
    public void PreparedMenusReopenWithIdenticalItemsReadsAndNoActionSideEffects(string name)
    {
        var fixture = PreparedProcessMenu.Create(name);
        Assert.That(fixture.Prepare(), Is.True);
        var snapshot = fixture.Snapshot();
        Assert.That(fixture.Prepare(), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(fixture.Snapshot(), Is.EqualTo(snapshot));
            Assert.That(fixture.Actions, Is.Empty);
            Assert.That(fixture.Targets, Is.Empty);
        });
    }

    [Test]
    public void MissingMenuOrUsageCannotPrepareActionsAndAnExistingMenuIsClearedFirst()
    {
        var row = new ProcessRow();
        Assert.That(row.PrepareProcessMenu(), Is.False);
        row.EnableProcessActions(_ => throw new AssertionException("No target"), _ => { });
        row.ContextMenu!.Items.Add(new MenuItem { Header = "old" });
        Assert.That(row.PrepareProcessMenu(), Is.False);
        Assert.That(row.ContextMenu.Items, Is.Empty);
    }

    [Test]
    public void AManuallyAssignedMenuWithoutActionBindingsCannotPrepareAnObservedRow()
    {
        var row = new ProcessRow { ContextMenu = new ContextMenu() };
        row.Update(PreparedProcessMenu.Original, [], 120, false, null);
        row.ContextMenu.Items.Add(new MenuItem());
        Assert.That(row.PrepareProcessMenu(), Is.False);
        Assert.That(row.ContextMenu.Items, Is.Empty);
    }

    [TestCase(false, false, true)]
    [TestCase(false, true, false)]
    [TestCase(true, false, false)]
    public void EndAvailabilityKeepsTheEligibilityAndObservationGuards(bool unavailable, bool rejected, bool expected)
    {
        var fixture = new PreparedProcessMenu(unavailable: unavailable) { CanEnd = !rejected };
        Assert.That(fixture.Prepare(), Is.True);
        var item = fixture.Row.ContextMenu!.Items.Cast<MenuItem>().Single();
        Assert.Multiple(() =>
        {
            Assert.That(item.Header, Is.EqualTo(Strings.Format(Strings.EndPID, "example.exe", 8123)));
            Assert.That(item.IsEnabled, Is.EqualTo(expected));
            Assert.That(item.ToolTip, Is.EqualTo(Strings.AvailableForLiveProcesses));
            Assert.That(fixture.Calls, Is.EqualTo(new[] { "end" }));
            Assert.That(fixture.Targets, Is.Empty);
        });
    }

    [Test]
    public void AllSectionsKeepTheirExactOrderSeparatorsAndNonemptyIcons()
    {
        var fixture = new PreparedProcessMenu();
        fixture.Inspect(); fixture.Pin(); fixture.Priority(); fixture.Controls();
        Assert.That(fixture.Prepare(), Is.True);
        var expected = new[] { Strings.ProcessProperties + "…", Strings.OpenFileLocation, Strings.CopyDetails,
            Strings.ShowInConnections, Strings.ShowInUsage, "separator", Strings.PinProcess,
            Strings.CreateDumpFile + "…", "separator", Strings.SetPriority, "separator", Strings.SuspendProcess,
            Strings.SetAffinity, Strings.EfficiencyMode, Strings.Format(Strings.EndPID, "example.exe", 8123) };
        Assert.That(fixture.Row.ContextMenu!.Items.Cast<object>().Select(item => item is MenuItem menu ? menu.Header : "separator"),
            Is.EqualTo(expected));
        var items = fixture.Row.ContextMenu.Items.OfType<MenuItem>().ToArray();
        Assert.That(items, Has.Length.EqualTo(12));
        Assert.That(fixture.Item(Strings.SetPriority).Icon, Is.Null);
        Assert.That(items.Where(item => !Equals(item.Header, Strings.SetPriority)).Select(item => item.Icon),
            Is.All.InstanceOf<System.Windows.Shapes.Path>());
        Assert.That(fixture.Calls, Is.EqualTo(new[] { "end", "history", "connections", "usage", "pin", "dump",
            "priority", "read-priority", "history", "history", "control", "owned", "observed", "can-suspend", "efficiency" }));
        Assert.That(fixture.Actions, Is.Empty);
        Assert.That(fixture.Targets, Is.Empty);
        var before = fixture.Snapshot();
        Assert.That(fixture.Prepare(), Is.True);
        Assert.That(fixture.Snapshot(), Is.EqualTo(before), "Reopening replaces old items without accumulating callbacks.");
    }

    [TestCase("", false)]
    [TestCase("   ", false)]
    [TestCase(@"C:\Missing.WinMon.Menu.Fixture\example.exe", true)]
    public void LocationAvailabilityDependsOnANonblankPath(string path, bool expected)
    {
        var fixture = new PreparedProcessMenu(); fixture.Inspect(false);
        fixture.Refresh(PreparedProcessMenu.Original with { ImagePath = path });
        fixture.Prepare();
        Assert.That(fixture.Item(Strings.OpenFileLocation).IsEnabled, Is.EqualTo(expected));
    }

    [TestCase(false, false)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public void NavigationKeepsOptionalBindingAndPerTargetAvailability(bool bound, bool predicate)
    {
        var fixture = new PreparedProcessMenu(); fixture.Inspect(bound, predicate);
        fixture.Prepare();
        var navigation = fixture.Row.ContextMenu!.Items.OfType<MenuItem>()
            .Where(item => Equals(item.Header, Strings.ShowInConnections) || Equals(item.Header, Strings.ShowInUsage)).ToArray();
        Assert.That(navigation, Has.Length.EqualTo(bound ? 2 : 0));
        if (bound)
            Assert.That(navigation.Select(item => item.IsEnabled), Is.EqualTo(new[] { false, predicate }));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void PinCaptionAndCapturedGroupingSurviveRowReuse(bool pinned)
    {
        var fixture = new PreparedProcessMenu(grouped: true) { Pinned = pinned }; fixture.Pin();
        fixture.Prepare();
        var item = fixture.Item(pinned ? Strings.UnpinProcess : Strings.PinProcess);
        fixture.Refresh(PreparedProcessMenu.Original with { ProcessId = 9999 });
        item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Assert.That(fixture.Targets.Single(), Is.EqualTo(("pin-group", PreparedProcessMenu.Original)));
    }

    [TestCase(false, false, true)]
    [TestCase(false, true, false)]
    [TestCase(true, false, false)]
    public void DumpAvailabilityPreservesObservationBeforeEligibility(bool unavailable, bool rejected, bool expected)
    {
        var fixture = new PreparedProcessMenu(unavailable: unavailable) { CanDump = !rejected }; fixture.Inspect(false);
        fixture.Prepare();
        var item = fixture.Item(Strings.CreateDumpFile + "…");
        Assert.That(item.IsEnabled, Is.EqualTo(expected));
        Assert.That(item.ToolTip, Is.EqualTo(Strings.ProcessDumpUnavailable));
        Assert.That(fixture.Calls.Contains("dump"), Is.EqualTo(!unavailable));
    }

    [TestCase("unavailable")]
    [TestCase("grouped")]
    [TestCase("historical")]
    [TestCase("ineligible")]
    public void DisabledControlViewsNeverReadSuspensionOrEfficiencyState(string reason)
    {
        var fixture = new PreparedProcessMenu(reason == "grouped", reason == "unavailable")
            { Historical = reason == "historical", CanControl = reason != "ineligible" };
        fixture.Inspect(false); fixture.Controls(); fixture.Prepare();
        Assert.Multiple(() =>
        {
            Assert.That(fixture.Item(Strings.SuspendProcess).IsEnabled, Is.False);
            Assert.That(fixture.Item(Strings.SetAffinity).IsEnabled, Is.False);
            Assert.That(fixture.Item(Strings.EfficiencyMode).IsEnabled, Is.False);
            Assert.That(fixture.Calls, Does.Not.Contain("observed").And.Not.Contain("can-suspend").And.Not.Contain("efficiency"));
            Assert.That(fixture.Calls.Count(call => call == "owned"), Is.EqualTo(1));
            Assert.That(fixture.Calls.Contains("control"), Is.EqualTo(reason == "ineligible"));
        });
    }

    [TestCase(false, false, false, false)]
    [TestCase(false, false, true, true)]
    [TestCase(false, null, true, true)]
    [TestCase(false, true, true, false)]
    [TestCase(true, false, false, true)]
    [TestCase(true, true, false, true)]
    [TestCase(true, null, true, true)]
    public void SuspensionOwnershipAndObservedStateKeepTheExistingPolicy(bool owned, bool? observed, bool canSuspend, bool expected)
    {
        var fixture = new PreparedProcessMenu { Owned = owned, Observed = observed, CanSuspend = canSuspend };
        fixture.Controls(); fixture.Prepare();
        var item = fixture.Item(owned ? Strings.ResumeProcess : Strings.SuspendProcess);
        Assert.That(item.IsEnabled, Is.EqualTo(expected));
        Assert.That(item.ToolTip, Is.EqualTo(observed == true && !owned ? Strings.ProcessStillSuspended : Strings.SuspendProcessUnavailable));
        Assert.That(fixture.Calls.Contains("can-suspend"), Is.EqualTo(!owned && observed != true));
        Assert.That(fixture.Calls.Count(call => call == "observed"), Is.EqualTo(1));
        Assert.That(fixture.Item(Strings.SetAffinity).IsEnabled, Is.True);
    }

    [TestCase((int)EfficiencyModeState.Unavailable, false, true)]
    [TestCase((int)EfficiencyModeState.Off, false, false)]
    [TestCase((int)EfficiencyModeState.On, true, false)]
    [TestCase((int)EfficiencyModeState.Mixed, false, true)]
    public void EfficiencyCheckAndHelpReflectTheCapturedReadState(int state, bool selected, bool mixed)
    {
        var fixture = new PreparedProcessMenu { Efficiency = (EfficiencyModeState)state }; fixture.Controls(); fixture.Prepare();
        var item = fixture.Item(Strings.EfficiencyMode);
        Assert.Multiple(() =>
        {
            Assert.That(item.IsCheckable, Is.True);
            Assert.That(item.IsChecked, Is.EqualTo(selected));
            Assert.That(item.IsEnabled, Is.True);
            Assert.That(item.ToolTip, Is.EqualTo(mixed ? Strings.EfficiencyModeMixed : Strings.EfficiencyModeWindows10Note));
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ControlClicksKeepTheOriginalSnapshotAndOwnershipAfterRowReuse(bool owned)
    {
        var fixture = new PreparedProcessMenu { Owned = owned }; fixture.Inspect(false); fixture.Controls(); fixture.Prepare();
        var items = new[] { fixture.Item(owned ? Strings.ResumeProcess : Strings.SuspendProcess), fixture.Item(Strings.SetAffinity), fixture.Item(Strings.EfficiencyMode) };
        fixture.Owned = !owned; fixture.Historical = true;
        fixture.Refresh(PreparedProcessMenu.Original with { ProcessId = 9999, CreationTicks = 999 }, true, true);
        foreach (var item in items) item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Assert.That(fixture.Actions.Select(a => a.Action), Is.EqualTo(new[] { owned ? "resume" : "suspend", "affinity", "efficiency" }));
        Assert.That(fixture.Actions, Has.Count.EqualTo(3));
        foreach (var action in fixture.Actions)
        {
            Assert.That(action.Snapshot.Process, Is.SameAs(PreparedProcessMenu.Original));
            Assert.That(action.Snapshot.Grouped, Is.False);
            Assert.That(action.Snapshot.Historical, Is.False);
            Assert.That(action.Snapshot.Available, Is.True);
            Assert.That(action.Snapshot.Timestamp, Is.EqualTo(PreparedProcessMenu.Sample.Timestamp));
            Assert.That(action.Snapshot.State, Is.Empty);
            Assert.That(action.Snapshot.Sample, Is.EqualTo(SampleKey.Of(PreparedProcessMenu.Sample)));
        }
    }

    [Test]
    public void ExistingControlItemsReadTheReboundCallbackWithoutReplacingTheirSnapshot()
    {
        var fixture = new PreparedProcessMenu(); fixture.Controls(); fixture.Prepare();
        var item = fixture.Item(Strings.SetAffinity);
        ProcessDetailsSnapshot? received = null;
        fixture.Row.EnableControlActions(_ => false, _ => false, _ => false, _ => false,
            _ => EfficiencyModeState.Off, (action, snapshot) => { Assert.That(action, Is.EqualTo("affinity")); received = snapshot; });
        item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Assert.That(fixture.Actions, Is.Empty);
        Assert.That(received!.Process, Is.SameAs(PreparedProcessMenu.Original));
    }

    [TestCase("end", 0)]
    [TestCase("control", 0)]
    [TestCase("owned", 0)]
    [TestCase("observed", 0)]
    [TestCase("can-suspend", 0)]
    [TestCase("efficiency", 2)]
    public void ReadFailuresEscapeAtTheOriginalPartialMenuBoundary(string callback, int count)
    {
        var fixture = new PreparedProcessMenu { ThrowAt = callback }; fixture.Controls();
        var error = Assert.Throws<InvalidOperationException>(() => fixture.Prepare());
        Assert.That(error!.Message, Is.EqualTo(callback));
        Assert.That(fixture.Row.ContextMenu!.Items, Has.Count.EqualTo(count));
        Assert.That(fixture.Actions, Is.Empty);
        Assert.That(fixture.Targets, Is.Empty);
    }

    [Test]
    public void ControlReadFailurePreservesTheExactExceptionInstance()
    {
        var fixture = new PreparedProcessMenu();
        var failure = new InvalidOperationException("controlled failure");
        fixture.Row.EnableControlActions(_ => throw failure, _ => true, _ => false, _ => false,
            _ => EfficiencyModeState.Off, (_, _) => Assert.Fail("No action should run"));
        Assert.That(Assert.Throws<InvalidOperationException>(() => fixture.Prepare()), Is.SameAs(failure));
        Assert.That(fixture.Row.ContextMenu!.Items, Is.Empty);
    }
}
