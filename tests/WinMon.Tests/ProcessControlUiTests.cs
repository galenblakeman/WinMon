using System.Windows;
using System.Windows.Controls;
using NUnit.Framework;
using WinMon.Controls;
using WinMon.Monitoring;
using WinMon.Resources;

namespace WinMon.Tests;

[TestFixture]
[Apartment(ApartmentState.STA)]
[SetUICulture("en")]
public sealed class ProcessControlUiTests
{
    private static readonly ProcessUsage Process = new(12345, 456, "example.exe", 1);

    [Test]
    public void AffinityDialog_RendersLogicalProcessorsAndAllNoneControls()
    {
        var dialog = new ProcessAffinityDialog(0b0011, 0b0111, false);
        dialog.Resources["Text"] = System.Windows.Media.Brushes.Lime;
        var choices = Descendants(dialog).OfType<CheckBox>().ToArray();
        Assert.That(choices.All(choice => Equals(choice.Foreground, System.Windows.Media.Brushes.Lime)), Is.True);
        Assert.That(choices.Select(choice => choice.IsChecked), Is.EqualTo(new bool?[] { true, true, false }));
        FindButton(dialog, Strings.AllProcessors).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
        Assert.That(choices.Select(choice => choice.IsChecked), Is.All.True);
        FindButton(dialog, Strings.NoProcessors).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
        Assert.That(choices.Select(choice => choice.IsChecked), Is.All.False);
    }

    [Test]
    public void AffinityDialog_IsReadOnlyForMultiGroupProcesses()
    {
        var dialog = new ProcessAffinityDialog(1, 3, true);
        Assert.Multiple(() =>
        {
            Assert.That(Descendants(dialog).OfType<CheckBox>().All(choice => !choice.IsEnabled), Is.True);
            Assert.That(FindButton(dialog, Strings.AllProcessors).IsEnabled, Is.False);
            Assert.That(FindButton(dialog, Strings.NoProcessors).IsEnabled, Is.False);
        });
    }

    [Test]
    public void AffinityDialog_DisablesSetForAnEmptyMask()
    {
        var dialog = new ProcessAffinityDialog(0, 3, false);
        Assert.That(FindButton(dialog, Strings.SetAffinity).IsEnabled, Is.False);
    }

    [TestCase(2, true, "windows10")]
    [TestCase(1, false, "windows10")]
    [TestCase(3, false, "mixed")]
    [TestCase(0, false, "mixed")]
    public void MoreMenu_UsesEfficiencyReadState(int stateValue, bool expectedChecked, string? tooltip)
    {
        var state = (EfficiencyModeState)stateValue;
        var bar = new ProcessActionBar();
        bar.Update(new ProcessDetailsSnapshot(Process, false, DateTimeOffset.Now, false, true, ""), true, true);
        bar.EnablePriorityActions(_ => false, _ => null, (_, _) => { });
        bar.EnableControlActions(_ => true, _ => true, _ => false, _ => false, _ => state);
        var item = bar.CreateMoreMenu()!.Items.OfType<MenuItem>().Single(menu => Equals(menu.Header, Strings.EfficiencyMode));
        Assert.That(item.IsChecked, Is.EqualTo(expectedChecked));
        Assert.That(item.ToolTip?.ToString(),
            Is.EqualTo(tooltip == "windows10" ? Strings.EfficiencyModeWindows10Note : Strings.EfficiencyModeMixed));
    }

    [Test]
    public void CloseDialog_DefaultsToCancel()
    {
        var dialog = new SuspendedProcessCloseDialog();
        Assert.Multiple(() =>
        {
            Assert.That(dialog.Choice, Is.EqualTo(SuspendedCloseChoice.Cancel));
            Assert.That(FindButton(dialog, Strings.ResumeProcess).IsDefault, Is.True);
        });
    }

    [Test]
    public void CloseDialog_ButtonsSetOnlyExplicitResumeOrLeaveChoices()
    {
        var resume = new SuspendedProcessCloseDialog();
        FindButton(resume, Strings.ResumeProcess).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
        var leave = new SuspendedProcessCloseDialog();
        FindButton(leave, Strings.LeaveSuspended).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
        var cancel = new SuspendedProcessCloseDialog();
        FindButton(cancel, Strings.CancelClose).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
        Assert.Multiple(() =>
        {
            Assert.That(resume.Choice, Is.EqualTo(SuspendedCloseChoice.Resume));
            Assert.That(leave.Choice, Is.EqualTo(SuspendedCloseChoice.Leave));
            Assert.That(cancel.Choice, Is.EqualTo(SuspendedCloseChoice.Cancel));
        });
    }

    [TestCase(true, false)]
    [TestCase(false, true)]
    public void MoreMenu_DisabledViewsDoNotReadNativeControlState(bool grouped, bool historical)
    {
        var bar = new ProcessActionBar();
        bar.Update(new ProcessDetailsSnapshot(Process, grouped, DateTimeOffset.Now, historical, true, ""), false, false);
        bar.EnablePriorityActions(_ => false, _ => null, (_, _) => { });
        bar.EnableControlActions(_ => throw new AssertionException("Disabled view."),
            _ => throw new AssertionException("No native suspend eligibility read."), _ => false,
            _ => throw new AssertionException("No native thread snapshot."),
            _ => throw new AssertionException("No native efficiency read."));
        var items = bar.CreateMoreMenu()!.Items.OfType<MenuItem>().Where(item =>
            Equals(item.Header, Strings.SuspendProcess) || Equals(item.Header, Strings.SetAffinity) ||
            Equals(item.Header, Strings.EfficiencyMode)).ToArray();
        Assert.That(items, Has.Length.EqualTo(3));
        Assert.That(items.All(item => !item.IsEnabled), Is.True);
    }

    [Test]
    public void Row_ShowsOnlyConfirmedOwnedSuspensionAndClearsItOnResume()
    {
        var suspended = true;
        var row = new ProcessRow();
        row.EnableControlActions(_ => true, _ => true, _ => suspended, _ => true,
            _ => EfficiencyModeState.Off, (_, _) => { }, _ => suspended);
        row.Update(Process, [new MonitorSample(DateTimeOffset.Now, 1, [Process], 0)], 120, true, null);
        Assert.That(System.Windows.Automation.AutomationProperties.GetName(row), Does.Contain(Strings.ProcessSuspendedByWinMon));
        suspended = false;
        row.RefreshSuspensionStatus();
        Assert.That(System.Windows.Automation.AutomationProperties.GetName(row), Does.Not.Contain(Strings.ProcessSuspendedByWinMon));
    }

    private static System.Windows.Controls.Button FindButton(DependencyObject root, string content) =>
        Descendants(root).OfType<Button>().Single(button => Equals(button.Content, content));

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
}
