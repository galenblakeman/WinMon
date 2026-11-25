using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using MonCore.Monitoring;
using NUnit.Framework;
using WinMon.Controls;
using WinMon.Monitoring;
using WinMon.Resources;

namespace WinMon.Tests;

[TestFixture, NonParallelizable, Apartment(ApartmentState.STA), SetCulture("en-US"), SetUICulture("en-US")]
public sealed class ProcessActionMenuCharacterizationTests
{
    private static readonly ProcessUsage Process = new(0, 1, "synthetic.exe", 1) { ImagePath = "C:\\Synthetic\\example.exe" };
    private static ProcessDetailsSnapshot Snapshot => new(Process, false, DateTimeOffset.UnixEpoch, false, true, "captured state");

    [OneTimeSetUp]
    public void WarmFallbackIcon() => Assert.That(ProcessIconProvider.GetAsync(Process).GetAwaiter().GetResult(),
        Is.SameAs(ProcessIconProvider.Fallback));

    [TestCase(false, false, false)]
    [TestCase(true, false, true)]
    [TestCase(true, true, false)]
    public void BaseActionsKeepTheirOrderAndIndependentLocationDumpAndNavigationFlags(bool path, bool historical, bool navigation)
    {
        var bar = new ProcessActionBar();
        bar.Update(Snapshot with { Process = Process with { ImagePath = path ? Process.ImagePath : "" }, Historical = historical },
            true, true, navigation, !navigation);
        var menu = bar.CreateMoreMenu()!;
        var items = menu.Items.OfType<MenuItem>().ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(items.Select(item => item.Header), Is.EqualTo(new[]
                { Strings.OpenFileLocation, Strings.CreateDumpFile + "…", Strings.ShowInConnections, Strings.ShowInUsage }));
            Assert.That(menu.Items, Has.Count.EqualTo(5));
            Assert.That(menu.Items[2], Is.TypeOf<Separator>());
            Assert.That(items.Select(item => item.IsEnabled), Is.EqualTo(new[] { path, !historical, navigation, !navigation }));
            Assert.That(items[1].ToolTip, Is.EqualTo(Strings.ProcessDumpUnavailable));
        });
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    public void AnyMissingPriorityDelegateStopsBeforeReadingControlState(int missing)
    {
        var bar = Bar();
        bar.EnablePriorityActions(missing == 0 ? null! : _ => throw new AssertionException("priority eligibility"),
            missing == 1 ? null! : _ => throw new AssertionException("priority read"),
            missing == 2 ? null! : (_, _) => throw new AssertionException("priority write"));
        bar.EnableControlActions(_ => throw new AssertionException("control"), _ => throw new AssertionException("suspend"),
            _ => throw new AssertionException("owned"), _ => throw new AssertionException("observed"),
            _ => throw new AssertionException("efficiency"));
        Assert.That(bar.CreateMoreMenu()!.Items, Has.Count.EqualTo(5));
    }

    [TestCase(true)]
    [TestCase(false)]
    public void MissingControlOrOwnershipDelegateOmitsTheWholeControlSection(bool missingControl)
    {
        var bar = Bar();
        Priority(bar);
        bar.EnableControlActions(missingControl ? null! : _ => throw new AssertionException("control"),
            _ => throw new AssertionException("suspend"), missingControl ? _ => throw new AssertionException("owned") : null!,
            _ => throw new AssertionException("observed"), _ => throw new AssertionException("efficiency"));
        var menu = bar.CreateMoreMenu()!;
        Assert.That(menu.Items, Has.Count.EqualTo(7));
        Assert.That(menu.Items.OfType<MenuItem>().Last().Header, Is.EqualTo(Strings.SetPriority));
    }

    [TestCase(false, false, true, true, false, false, true, true)]
    [TestCase(false, false, true, true, false, false, false, false)]
    [TestCase(false, false, true, true, false, false, null, false)]
    [TestCase(false, false, true, true, false, null, true, true)]
    [TestCase(false, false, true, true, false, true, true, false)]
    [TestCase(false, false, true, true, true, true, false, true)]
    [TestCase(false, false, true, true, true, false, false, true)]
    [TestCase(false, false, true, true, true, null, false, true)]
    [TestCase(false, false, true, false, true, true, true, false)]
    [TestCase(false, true, true, true, true, true, true, false)]
    [TestCase(true, false, true, true, false, true, true, false)]
    [TestCase(false, false, false, true, false, true, true, false)]
    public void ControlEligibilityPreservesOwnershipTriStateAndDisabledViewGuards(bool grouped, bool historical, bool available,
        bool canControl, bool owned, bool? observed, bool? canSuspend, bool suspendEnabled)
    {
        var snapshot = Snapshot with { Grouped = grouped, Historical = historical, Available = available };
        var bar = Bar(snapshot);
        var trace = new List<string>();
        Priority(bar, trace);
        bar.EnableControlActions(_ => { trace.Add("control"); return canControl; },
            canSuspend is null ? null! : _ => { trace.Add("suspend"); return canSuspend.Value; },
            _ => { trace.Add("owned"); return owned; }, _ => { trace.Add("observed"); return observed; },
            _ => { trace.Add("efficiency"); return EfficiencyModeState.Off; });

        var menu = bar.CreateMoreMenu()!;

        var enabled = available && !historical && !grouped && canControl;
        var expected = new List<string>();
        if (available && !historical) expected.AddRange(["priority-eligible", "priority-read"]);
        if (available && !historical && !grouped) expected.Add("control");
        expected.Add("owned");
        if (enabled) expected.Add("observed");
        if (enabled && observed == true) expected.Add("owned");
        if (enabled && !owned && observed != true && canSuspend is not null) expected.Add("suspend");
        if (enabled) expected.Add("efficiency");
        var suspend = Item(menu, owned ? Strings.ResumeProcess : Strings.SuspendProcess);
        Assert.Multiple(() =>
        {
            Assert.That(trace, Is.EqualTo(expected));
            Assert.That(menu.Items, Has.Count.EqualTo(11));
            Assert.That(suspend.IsEnabled, Is.EqualTo(suspendEnabled));
            Assert.That(suspend.ToolTip, Is.EqualTo(enabled && observed == true && !owned
                ? Strings.ProcessStillSuspended : Strings.SuspendProcessUnavailable));
            Assert.That(Item(menu, Strings.SetAffinity).IsEnabled, Is.EqualTo(enabled));
            Assert.That(Item(menu, Strings.EfficiencyMode).IsEnabled, Is.EqualTo(enabled));
        });
    }

    [TestCase(2, true, false)]
    [TestCase(1, false, false)]
    [TestCase(3, false, true)]
    [TestCase(0, false, true)]
    public void EfficiencyStateKeepsItsCheckedStateAndSpecificTooltip(int state, bool check, bool mixed)
    {
        var bar = Bar();
        Priority(bar);
        bar.EnableControlActions(_ => true, _ => true, _ => false, _ => false, _ => (EfficiencyModeState)state);
        var item = Item(bar.CreateMoreMenu()!, Strings.EfficiencyMode);
        Assert.That(item.IsCheckable, Is.True);
        Assert.That(item.IsChecked, Is.EqualTo(check));
        Assert.That(item.ToolTip, Is.EqualTo(mixed ? Strings.EfficiencyModeMixed : Strings.EfficiencyModeWindows10Note));
    }

    [Test]
    public void AbsentOptionalReadersKeepUnknownSuspensionAndUnavailableEfficiency()
    {
        var bar = Bar();
        Priority(bar);
        bar.EnableControlActions(_ => true, _ => true, _ => false, null!, null!);
        var menu = bar.CreateMoreMenu()!;
        Assert.That(Item(menu, Strings.SuspendProcess).IsEnabled, Is.True);
        Assert.That(Item(menu, Strings.EfficiencyMode).IsChecked, Is.False);
        Assert.That(Item(menu, Strings.EfficiencyMode).ToolTip, Is.EqualTo(Strings.EfficiencyModeMixed));
    }

    [Test]
    public void ConfirmedSuspensionReadsOwnershipAgainInsteadOfReusingTheFirstResult()
    {
        var bar = Bar();
        Priority(bar);
        var reads = 0;
        bar.EnableControlActions(_ => true, _ => throw new AssertionException("External suspension must not probe eligibility"),
            _ => ++reads > 1, _ => true, _ => EfficiencyModeState.Off);
        var menu = bar.CreateMoreMenu()!;
        Assert.That(reads, Is.EqualTo(2));
        Assert.That(Item(menu, Strings.SuspendProcess).IsEnabled, Is.False);
        Assert.That(Item(menu, Strings.SuspendProcess).ToolTip, Is.EqualTo(Strings.ProcessStillSuspended));
        Assert.That(Descendants(bar).OfType<TextBlock>().Single(text => text.Parent is Border).Text,
            Is.EqualTo(Strings.ProcessSuspendedByWinMon));
    }

    [Test]
    public void ReentrantEligibilityChangesUseTheSubsequentlyInstalledReaders()
    {
        var bar = Bar();
        Priority(bar);
        var trace = new List<string>();
        bar.EnableControlActions(_ =>
            {
                trace.Add("control");
                bar.EnableControlActions(_ => true, _ => throw new AssertionException("Owned suspension bypasses eligibility"),
                    _ => { trace.Add("new-owned"); return true; }, _ => { trace.Add("new-observed"); return false; },
                    _ => { trace.Add("new-efficiency"); return EfficiencyModeState.On; });
                return true;
            }, _ => throw new AssertionException("old eligibility"), _ => throw new AssertionException("old ownership"),
            _ => throw new AssertionException("old observation"), _ => throw new AssertionException("old efficiency"));
        var menu = bar.CreateMoreMenu()!;
        Assert.That(trace, Is.EqualTo(new[] { "control", "new-owned", "new-observed", "new-efficiency" }));
        Assert.That(Item(menu, Strings.ResumeProcess).IsEnabled, Is.True);
        Assert.That(Item(menu, Strings.EfficiencyMode).IsChecked, Is.True);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void MenuClicksKeepTheOriginalTargetAndOwnedActionButUseTheCurrentSubscriber(bool owned)
    {
        var original = Snapshot;
        var bar = Bar(original);
        Priority(bar);
        bar.EnableControlActions(_ => true, _ => true, _ => owned, _ => false, _ => EfficiencyModeState.Off);
        var menu = bar.CreateMoreMenu()!;
        bar.Update(null, false, false);
        var received = new List<(string Action, ProcessDetailsSnapshot Snapshot)>();
        bar.ActionRequested += (action, snapshot) => received.Add((action, snapshot));
        var labels = new[] { Strings.OpenFileLocation, Strings.CreateDumpFile + "…", Strings.ShowInConnections,
            Strings.ShowInUsage, owned ? Strings.ResumeProcess : Strings.SuspendProcess, Strings.SetAffinity, Strings.EfficiencyMode };
        foreach (var label in labels) Item(menu, label).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Assert.That(received.Select(item => item.Action), Is.EqualTo(new[]
            { "location", "dump", "connections", "usage", owned ? "resume" : "suspend", "affinity", "efficiency" }));
        Assert.That(received.All(item => ReferenceEquals(item.Snapshot, original)), Is.True);
        Assert.That(received.All(item => item.Snapshot.Timestamp == DateTimeOffset.UnixEpoch), Is.True);
    }

    [TestCase("priority-eligible")]
    [TestCase("priority-read")]
    [TestCase("control")]
    [TestCase("owned")]
    [TestCase("observed")]
    [TestCase("owned-confirm")]
    [TestCase("suspend")]
    [TestCase("efficiency")]
    public void CallbackExceptionsPreserveTheirIdentityAndStopTheExistingReadSequence(string stage)
    {
        var bar = Bar();
        var trace = new List<string>();
        var failure = new InvalidOperationException(stage);
        T Read<T>(string name, T result)
        {
            trace.Add(name);
            if (name == stage) throw failure;
            return result;
        }
        var ownershipReads = 0;
        bar.EnablePriorityActions(_ => Read("priority-eligible", true), _ => Read("priority-read", (ProcessPriorityClass?)ProcessPriorityClass.Normal), (_, _) => { });
        bar.EnableControlActions(_ => Read("control", true), _ => Read("suspend", true),
            _ => Read(++ownershipReads == 1 ? "owned" : "owned-confirm", false),
            _ => Read("observed", (bool?)(stage == "owned-confirm")), _ => Read("efficiency", EfficiencyModeState.Off));

        Assert.That(Assert.Throws<InvalidOperationException>(() => bar.CreateMoreMenu()), Is.SameAs(failure));

        var sequence = stage == "owned-confirm"
            ? new[] { "priority-eligible", "priority-read", "control", "owned", "observed", "owned-confirm" }
            : new[] { "priority-eligible", "priority-read", "control", "owned", "observed", "suspend", "efficiency" };
        Assert.That(trace, Is.EqualTo(sequence.Take(Array.IndexOf(sequence, stage) + 1)));
    }

    private static ProcessActionBar Bar(ProcessDetailsSnapshot? snapshot = null)
    {
        var bar = new ProcessActionBar();
        bar.Update(snapshot ?? Snapshot, true, true, true, true);
        return bar;
    }
    private static void Priority(ProcessActionBar bar, List<string>? trace = null) => bar.EnablePriorityActions(
        _ => { trace?.Add("priority-eligible"); return true; },
        _ => { trace?.Add("priority-read"); return ProcessPriorityClass.Normal; }, (_, _) => { });
    private static MenuItem Item(ContextMenu menu, string header) => menu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, header));
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            yield return child;
            foreach (var nested in Descendants(child)) yield return nested;
        }
    }
}
