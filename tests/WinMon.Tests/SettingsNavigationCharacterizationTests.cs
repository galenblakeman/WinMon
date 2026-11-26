using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using NUnit.Framework;
using WinMon.Preferences;

namespace WinMon.Tests;

public sealed partial class SettingsShortcutCharacterizationTests
{
    private static Action<bool> NetworkBusy(SettingsWindow window) =>
        (typeof(SettingsWindow).GetMethod("SetNetworkMonitoringBusy", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new AssertionException("Missing network busy boundary")).CreateDelegate<Action<bool>>(window);

    private static void WithInactiveKeyboard(Action<SettingsWindow> exercise)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            SettingsWindow? window = null;
            try
            {
                window = new SettingsWindow(new MonitorPreferences(), () => [],
                    _ => throw new AssertionException("Navigation must not probe shortcuts"), "owned shortcut status",
                    startupAvailability: StartupAvailability.Available, loadNetworkInterfaces: () => [],
                    checkForUpdates: () => Task.FromResult("owned check"), captureFolder: "owned-reports");
                Assert.That(Keyboard.PrimaryDevice.ActiveSource, Is.Null);
                Assert.That(Keyboard.Modifiers, Is.EqualTo(ModifierKeys.None));
                exercise(window);
            }
            catch (Exception error) { failure = error; }
            finally
            {
                try { window?.Close(); Dispatcher.CurrentDispatcher.InvokeShutdown(); }
                catch (Exception error) { failure ??= error; }
            }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.That(thread.Join(TimeSpan.FromSeconds(10)), Is.True, "The owned keyboard thread must finish before cleanup");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    [TestCase(Key.A), TestCase(Key.Escape), TestCase(Key.Left), TestCase(Key.Right), TestCase(Key.Enter), TestCase(Key.F1)]
    public void SectionNavigationIgnoresOtherKeysWithoutSavingOrProbingShortcuts(Key key)
    {
        var window = Window(new MonitorPreferences(), _ => throw new AssertionException("Navigation must not probe shortcuts"));
        window.ShowSection(SettingsSection.Monitoring);
        var input = Input(key);
        window.RaiseEvent(input);
        Assert.That(window.Section, Is.EqualTo(SettingsSection.Monitoring));
        Assert.That(input.Handled, Is.False);
        Assert.That(window.Result, Is.Null);
        Assert.That(window.IsVisible, Is.False);
    }

    [Test]
    public void SectionNavigationBusyGuardKeepsThePageAndDisabledControls()
    {
        var window = Window(new MonitorPreferences(), _ => throw new AssertionException("Busy navigation must not probe shortcuts"));
        window.ShowSection(SettingsSection.Appearance);
        var busy = NetworkBusy(window);
        busy(true);
        try
        {
            var input = Input(Key.Tab);
            window.RaiseEvent(input);
            Assert.That(window.Section, Is.EqualTo(SettingsSection.Appearance));
            Assert.That(input.Handled, Is.False);
            Assert.That(window.Navigation.IsEnabled, Is.False);
            Assert.That(window.SaveButton.IsEnabled, Is.False);
            Assert.That(window.Result, Is.Null);
        }
        finally { busy(false); }
    }

    [Test]
    public void SectionNavigationPlainTabWithAnInactiveKeyboardPreservesThePage()
    {
        WithInactiveKeyboard(window =>
        {
            window.ShowSection(SettingsSection.Services);
            var input = Input(Key.Tab);
            window.RaiseEvent(input);
            Assert.That(window.Section, Is.EqualTo(SettingsSection.Services));
            Assert.That(input.Handled, Is.False);
            Assert.That(window.Result, Is.Null);
        });
    }

    [TestCase(ModifierKeys.Control, 1)]
    [TestCase(ModifierKeys.Control | ModifierKeys.Shift, -1)]
    [TestCase(ModifierKeys.Control | ModifierKeys.Alt, 1)]
    [TestCase(ModifierKeys.Control | ModifierKeys.Windows, 1)]
    [TestCase(ModifierKeys.Control | ModifierKeys.Shift | ModifierKeys.Alt | ModifierKeys.Windows, -1)]
    public void SectionNavigationControlledModifiersStepAndWrapAcrossEveryAvailablePage(ModifierKeys modifiers, int direction)
    {
        var window = Window(new MonitorPreferences(), _ => throw new AssertionException("Section navigation must not probe shortcuts"));
        var count = window.Navigation.Items.Count;
        Assert.That(count, Is.GreaterThan(1));
        for (var start = 0; start < count; start++)
        {
            window.Navigation.SelectedIndex = start;
            var input = Input(Key.Tab);
            window.HandleSectionKeyDown(input, modifiers);
            var expected = (start + direction + count) % count;
            Assert.That(window.Navigation.SelectedIndex, Is.EqualTo(expected));
            Assert.That(window.Navigation.SelectedItem, Is.SameAs(window.Navigation.Items[expected]));
            Assert.That(input.Handled, Is.True);
            Assert.That(window.Result, Is.Null);
            Assert.That(window.SaveButton.IsEnabled, Is.True);
        }
    }

    [TestCase(ModifierKeys.None), TestCase(ModifierKeys.Shift), TestCase(ModifierKeys.Alt)]
    [TestCase(ModifierKeys.Windows), TestCase(ModifierKeys.Shift | ModifierKeys.Alt | ModifierKeys.Windows)]
    public void SectionNavigationControlledModifiersRequireControl(ModifierKeys modifiers)
    {
        var window = Window(new MonitorPreferences(), _ => throw new AssertionException("Non-navigation keys must not probe shortcuts"));
        window.ShowSection(SettingsSection.Monitoring);
        var input = Input(Key.Tab);
        window.HandleSectionKeyDown(input, modifiers);
        Assert.That(window.Section, Is.EqualTo(SettingsSection.Monitoring));
        Assert.That(input.Handled, Is.False);
        Assert.That(window.Result, Is.Null);
    }

    [TestCase(Key.Tab), TestCase(Key.A)]
    public void SectionNavigationControlledModifiersRespectTheBusyGuard(Key key)
    {
        var window = Window(new MonitorPreferences(), _ => throw new AssertionException("Busy navigation must not probe shortcuts"));
        window.ShowSection(SettingsSection.Services);
        var busy = NetworkBusy(window);
        busy(true);
        try
        {
            var input = Input(key);
            window.HandleSectionKeyDown(input, ModifierKeys.Control | ModifierKeys.Shift);
            Assert.That(window.Section, Is.EqualTo(SettingsSection.Services));
            Assert.That(input.Handled, Is.False);
            Assert.That(window.Navigation.IsEnabled, Is.False);
            Assert.That(window.SaveButton.IsEnabled, Is.False);
        }
        finally { busy(false); }
    }

    [Test]
    public void SectionNavigationControlledModifiersDoNotPersistIntoTheNativeEventPath()
    {
        WithInactiveKeyboard(window =>
        {
            window.HandleSectionKeyDown(Input(Key.Tab), ModifierKeys.Control);
            Assert.That(window.Navigation.SelectedIndex, Is.EqualTo(1));
            var plainTab = Input(Key.Tab);
            window.RaiseEvent(plainTab);
            Assert.That(window.Navigation.SelectedIndex, Is.EqualTo(1));
            Assert.That(plainTab.Handled, Is.False);
            window.HandleSectionKeyDown(Input(Key.Tab), ModifierKeys.Control | ModifierKeys.Shift);
            Assert.That(window.Navigation.SelectedIndex, Is.Zero);
            Assert.That(Keyboard.Modifiers, Is.EqualTo(ModifierKeys.None));
            Assert.That(window.Result, Is.Null);
        });
    }

    [TestCase(false), TestCase(true)]
    public void SectionNavigationHandlesBothUnrealizedAndRealizedSidebarContainers(bool realize)
    {
        var window = Window(new MonitorPreferences(), _ => throw new AssertionException("Section navigation must not probe shortcuts"));
        if (realize)
        {
            window.Navigation.Measure(new Size(240, 600));
            window.Navigation.Arrange(new Rect(0, 0, 240, 600));
            window.Navigation.UpdateLayout();
            Assert.That(window.Navigation.ItemContainerGenerator.ContainerFromIndex(1), Is.InstanceOf<ListBoxItem>());
        }
        else Assert.That(window.Navigation.ItemContainerGenerator.ContainerFromIndex(1), Is.Null);
        var input = Input(Key.Tab);
        window.HandleSectionKeyDown(input, ModifierKeys.Control);
        Assert.That(window.Navigation.SelectedIndex, Is.EqualTo(1));
        Assert.That(input.Handled, Is.True);
        Assert.That(window.Result, Is.Null);
        Assert.That(window.IsVisible, Is.False);
    }
}
