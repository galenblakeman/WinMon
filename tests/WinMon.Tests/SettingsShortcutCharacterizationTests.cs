using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using NUnit.Framework;
using WinMon.Preferences;
using WinMon.Resources;
using WinMon.Shortcuts;

namespace WinMon.Tests;

[TestFixture, NonParallelizable, Apartment(ApartmentState.STA), SetCulture("en-US"), SetUICulture("en-US")]
public sealed partial class SettingsShortcutCharacterizationTests
{
    private readonly List<SettingsWindow> _windows = [];
    private readonly List<object> _addedResources = [];

    [SetUp]
    public void SetUp()
    {
        var app = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        foreach (var (key, brush) in new[] { ("Surface", Brushes.Black),
                     ("Text", Brushes.White) })
            if (!app.Resources.Contains(key)) { app.Resources[key] = brush; _addedResources.Add(key); }
    }

    [TearDown]
    public void TearDown()
    {
        foreach (var window in _windows) window.Close();
        _windows.Clear();
        foreach (var key in _addedResources) Application.Current.Resources.Remove(key);
        _addedResources.Clear();
    }

    private SettingsWindow Window(MonitorPreferences preferences, Func<HotkeyGesture, bool> available)
    {
        var window = new SettingsWindow(preferences, () => [], available, "owned shortcut status",
            startupAvailability: StartupAvailability.Available, loadNetworkInterfaces: () => [],
            checkForUpdates: () => Task.FromResult("owned check"), captureFolder: "owned-reports");
        _windows.Add(window);
        return window;
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    private static TextBox Box(SettingsWindow window) => Descendants(window).OfType<TextBox>()
        .Single(box => AutomationProperties.GetName(box) == Strings.ShowHideWinMon);

    private static TextBlock Status(SettingsWindow window) => Descendants(window).OfType<TextBlock>()
        .Single(text => text.Text == "owned shortcut status");

    private static Button Reset(SettingsWindow window) => Descendants(window).OfType<Button>()
        .Single(button => AutomationProperties.GetName(button) == Strings.RestoreTheDefaultShortcut);

    [TestCase("Ctrl+Shift+M", true)]
    [TestCase("Ctrl+Shift+M", false)]
    [TestCase("Ctrl+Alt+J", true)]
    [TestCase("Ctrl+Alt+J", false)]
    [TestCase("None", true)]
    [TestCase("None", false)]
    public void DefaultButtonChecksOnlyChangedGesturesAndKeepsRejectedChoices(string initial, bool available)
    {
        var preferences = new MonitorPreferences { ShowShortcut = initial };
        var probes = new List<HotkeyGesture>();
        var window = Window(preferences, gesture => { probes.Add(gesture); return available; });
        var box = Box(window);
        var status = Status(window);
        var oldText = box.Text;
        var changed = initial != HotkeyGesture.Default.ToString();
        var accepted = !changed || available;
        var expectedStatus = accepted ? Strings.AppliesWhenYouSaveIfACombinationDoesnTAppear
            : Strings.Format(Strings.AnotherAppIsUsingChooseAnother, HotkeyGesture.Default.DisplayText);

        Reset(window).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        Assert.Multiple(() =>
        {
            Assert.That(box.Text, Is.EqualTo(accepted ? HotkeyGesture.Default.DisplayText : oldText));
            Assert.That(status.Text, Is.EqualTo(expectedStatus));
            Assert.That(probes, Is.EqualTo(changed ? new[] { HotkeyGesture.Default } : Array.Empty<HotkeyGesture>()));
            Assert.That(preferences.ShowShortcut, Is.EqualTo(initial));
            Assert.That(window.Result, Is.Null);
            Assert.That(window.IsVisible, Is.False);
        });
        Reset(window).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.That(probes.Count, Is.EqualTo(!changed ? 0 : accepted ? 1 : 2),
            "Accepted gestures become current; rejected gestures do not replace the current shortcut.");
    }

    [TestCase("Ctrl+Alt+J")]
    [TestCase("None")]
    public void AvailabilityFailureLeavesCurrentGestureAndFeedbackIntactAndCanRetry(string initial)
    {
        var failure = new InvalidOperationException("owned availability failure");
        var fail = true;
        var probes = 0;
        var window = Window(new MonitorPreferences { ShowShortcut = initial }, _ =>
        {
            probes++;
            if (fail) throw failure;
            return true;
        });
        var box = Box(window);
        var status = Status(window);
        var oldText = box.Text;
        var button = Reset(window);
        Assert.That(Assert.Throws<InvalidOperationException>(() =>
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent))), Is.SameAs(failure));
        Assert.Multiple(() =>
        {
            Assert.That(box.Text, Is.EqualTo(oldText));
            Assert.That(status.Text, Is.EqualTo("owned shortcut status"));
            Assert.That(probes, Is.EqualTo(1));
        });
        fail = false;
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Multiple(() =>
        {
            Assert.That(box.Text, Is.EqualTo(HotkeyGesture.Default.DisplayText));
            Assert.That(status.Text, Is.EqualTo(Strings.AppliesWhenYouSaveIfACombinationDoesnTAppear));
            Assert.That(probes, Is.EqualTo(2));
        });
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.That(probes, Is.EqualTo(2));
    }

    private static KeyEventArgs Input(Key key, bool handled = false) =>
        new(Keyboard.PrimaryDevice, new WindowlessInputSource(), 0, key)
            { RoutedEvent = Keyboard.PreviewKeyDownEvent, Handled = handled };

    private sealed class WindowlessInputSource : PresentationSource
    {
        public override Visual RootVisual { get; set; } = null!;
        public override bool IsDisposed => false;
        protected override CompositionTarget GetCompositionTargetCore() => null!;
    }

    [TestCase(Key.Tab, ModifierKeys.None, false)]
    [TestCase(Key.Tab, ModifierKeys.None, true)]
    [TestCase(Key.Tab, ModifierKeys.Shift, false)]
    [TestCase(Key.Tab, ModifierKeys.Shift, true)]
    [TestCase(Key.Escape, ModifierKeys.None, false)]
    [TestCase(Key.Escape, ModifierKeys.None, true)]
    [TestCase(Key.Escape, ModifierKeys.Shift, false)]
    [TestCase(Key.Escape, ModifierKeys.Shift, true)]
    public void DialogKeysKeepHandledStateGestureFeedbackAndAvailabilityUntouched(Key key, ModifierKeys modifiers,
        bool handled)
    {
        var window = Window(new MonitorPreferences { ShowShortcut = "Ctrl+Alt+J" }, _ =>
            throw new AssertionException("Dialog keys must not probe hotkeys."));
        var box = Box(window);
        var status = Status(window);
        var input = Input(key, handled);
        window.CaptureShortcut(key, modifiers, input);
        Assert.Multiple(() =>
        {
            Assert.That(input.Handled, Is.EqualTo(handled));
            Assert.That(box.Text, Is.EqualTo("Ctrl + Alt + J"));
            Assert.That(status.Text, Is.EqualTo("owned shortcut status"));
            Assert.That(window.Result, Is.Null);
        });
    }

    [TestCase(Key.Back, "Ctrl+Alt+J")]
    [TestCase(Key.Delete, "Ctrl+Alt+J")]
    [TestCase(Key.Back, "None")]
    [TestCase(Key.Delete, "None")]
    public void UnmodifiedBackAndDeleteDisableTheShortcutBeforeAnyAvailabilityProbe(Key key, string initial)
    {
        var probes = new List<HotkeyGesture>();
        var window = Window(new MonitorPreferences { ShowShortcut = initial }, gesture => { probes.Add(gesture); return false; });
        var box = Box(window);
        var status = Status(window);
        var input = Input(key);
        window.CaptureShortcut(key, ModifierKeys.None, input);
        Assert.Multiple(() =>
        {
            Assert.That(input.Handled, Is.True);
            Assert.That(box.Text, Is.EqualTo(Strings.None));
            Assert.That(status.Text, Is.EqualTo(Strings.OffClickTheBoxAndPressACombinationToSet));
            Assert.That(probes, Is.Empty);
        });
        Reset(window).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Multiple(() =>
        {
            Assert.That(probes, Is.EqualTo(new[] { HotkeyGesture.Default }));
            Assert.That(box.Text, Is.EqualTo(Strings.None), "A rejected reset must retain the disabled choice.");
        });
    }

    [TestCase(Key.LeftCtrl)]
    [TestCase(Key.RightCtrl)]
    [TestCase(Key.LeftAlt)]
    [TestCase(Key.RightAlt)]
    [TestCase(Key.LeftShift)]
    [TestCase(Key.RightShift)]
    [TestCase(Key.LWin)]
    [TestCase(Key.RWin)]
    public void EachModifierKeyEchoesHeldModifiersWithoutReplacingTheGestureOrFeedback(Key key)
    {
        var window = Window(new MonitorPreferences { ShowShortcut = "Ctrl+Alt+J" }, _ =>
            throw new AssertionException("Modifier keys must not probe hotkeys."));
        var box = Box(window);
        var status = Status(window);
        var input = Input(key);
        window.CaptureShortcut(key, ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Shift | ModifierKeys.Windows, input);
        Assert.Multiple(() =>
        {
            Assert.That(input.Handled, Is.True);
            Assert.That(box.Text, Is.EqualTo("Ctrl + Alt + Shift + Win + …"));
            Assert.That(status.Text, Is.EqualTo("owned shortcut status"));
        });
        window.CaptureShortcut(Key.LeftCtrl, ModifierKeys.None, Input(Key.LeftCtrl));
        Assert.That(box.Text, Is.EqualTo("Ctrl + Alt + J"));
    }

    [TestCase(0, "Ctrl + Alt + J")]
    [TestCase(1, "Alt + …")]
    [TestCase(2, "Ctrl + …")]
    [TestCase(3, "Ctrl + Alt + …")]
    [TestCase(4, "Shift + …")]
    [TestCase(5, "Alt + Shift + …")]
    [TestCase(6, "Ctrl + Shift + …")]
    [TestCase(7, "Ctrl + Alt + Shift + …")]
    [TestCase(8, "Win + …")]
    [TestCase(9, "Alt + Win + …")]
    [TestCase(10, "Ctrl + Win + …")]
    [TestCase(11, "Ctrl + Alt + Win + …")]
    [TestCase(12, "Shift + Win + …")]
    [TestCase(13, "Alt + Shift + Win + …")]
    [TestCase(14, "Ctrl + Shift + Win + …")]
    [TestCase(15, "Ctrl + Alt + Shift + Win + …")]
    public void EveryModifierBitsetHasTheExistingDisplayOrderAndKeepsTheStoredChoice(int mask, string expected)
    {
        var window = Window(new MonitorPreferences { ShowShortcut = "Ctrl+Alt+J" }, _ =>
            throw new AssertionException("Modifier echo must not probe hotkeys."));
        var status = Status(window);
        var input = Input(Key.LeftCtrl);
        window.CaptureShortcut(Key.LeftCtrl, (ModifierKeys)mask, input);
        Assert.Multiple(() =>
        {
            Assert.That(input.Handled, Is.True);
            Assert.That(Box(window).Text, Is.EqualTo(expected));
            Assert.That(status.Text, Is.EqualTo("owned shortcut status"));
        });
    }

    [TestCase(Key.A, ModifierKeys.Control | ModifierKeys.Shift, 65, false)]
    [TestCase(Key.A, ModifierKeys.Control | ModifierKeys.Shift, 65, true)]
    [TestCase(Key.Z, ModifierKeys.Alt | ModifierKeys.Shift, 90, false)]
    [TestCase(Key.Z, ModifierKeys.Alt | ModifierKeys.Shift, 90, true)]
    [TestCase(Key.D0, ModifierKeys.Control | ModifierKeys.Alt, 48, false)]
    [TestCase(Key.D0, ModifierKeys.Control | ModifierKeys.Alt, 48, true)]
    [TestCase(Key.D9, ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Shift, 57, false)]
    [TestCase(Key.D9, ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Shift, 57, true)]
    [TestCase(Key.F1, ModifierKeys.Control | ModifierKeys.Shift, 0x70, false)]
    [TestCase(Key.F1, ModifierKeys.Control | ModifierKeys.Shift, 0x70, true)]
    [TestCase(Key.F24, ModifierKeys.Alt | ModifierKeys.Shift, 0x87, false)]
    [TestCase(Key.F24, ModifierKeys.Alt | ModifierKeys.Shift, 0x87, true)]
    [TestCase(Key.PageUp, ModifierKeys.Control | ModifierKeys.Alt, 0x21, false)]
    [TestCase(Key.PageUp, ModifierKeys.Control | ModifierKeys.Alt, 0x21, true)]
    [TestCase(Key.Delete, ModifierKeys.Control | ModifierKeys.Shift, 0x2E, false)]
    [TestCase(Key.Delete, ModifierKeys.Control | ModifierKeys.Shift, 0x2E, true)]
    public void ValidKeysUseTheCorrectVirtualKeyAndModifierMappingAndBypassTheProbeAfterAcceptance(Key key,
        ModifierKeys modifiers, int virtualKey, bool available)
    {
        var probes = new List<HotkeyGesture>();
        var window = Window(new MonitorPreferences { ShowShortcut = "Ctrl+Alt+J" }, gesture => { probes.Add(gesture); return available; });
        var box = Box(window);
        var status = Status(window);
        var input = Input(key);
        window.CaptureShortcut(key, modifiers, input);
        var expectedModifiers = modifiers switch
        {
            ModifierKeys.Control | ModifierKeys.Shift => HotkeyModifiers.Control | HotkeyModifiers.Shift,
            ModifierKeys.Alt | ModifierKeys.Shift => HotkeyModifiers.Alt | HotkeyModifiers.Shift,
            ModifierKeys.Control | ModifierKeys.Alt => HotkeyModifiers.Control | HotkeyModifiers.Alt,
            _ => HotkeyModifiers.Control | HotkeyModifiers.Alt | HotkeyModifiers.Shift
        };
        var expected = new HotkeyGesture(expectedModifiers, virtualKey);
        Assert.Multiple(() =>
        {
            Assert.That(input.Handled, Is.True);
            Assert.That(probes, Is.EqualTo(new[] { expected }));
            Assert.That(box.Text, Is.EqualTo(available ? expected.DisplayText : "Ctrl + Alt + J"));
            Assert.That(status.Text, Is.EqualTo(available ? Strings.AppliesWhenYouSaveIfACombinationDoesnTAppear
                : Strings.Format(Strings.AnotherAppIsUsingChooseAnother, expected.DisplayText)));
        });
        window.CaptureShortcut(key, modifiers, Input(key));
        Assert.That(probes.Count, Is.EqualTo(available ? 1 : 2));
    }

    [TestCase(Key.A, ModifierKeys.None, "modifiers")]
    [TestCase(Key.A, ModifierKeys.Control, "modifiers")]
    [TestCase(Key.A, ModifierKeys.Alt, "modifiers")]
    [TestCase(Key.A, ModifierKeys.Shift, "modifiers")]
    [TestCase(Key.Delete, ModifierKeys.Shift, "modifiers")]
    [TestCase(Key.A, ModifierKeys.Windows | ModifierKeys.Shift, "windows")]
    [TestCase(Key.A, ModifierKeys.Windows | ModifierKeys.Control, "windows")]
    [TestCase(Key.Delete, ModifierKeys.Control | ModifierKeys.Alt, "secure-attention")]
    [TestCase(Key.Back, ModifierKeys.Control | ModifierKeys.Shift, "key")]
    [TestCase(Key.Tab, ModifierKeys.Control | ModifierKeys.Shift, "key")]
    [TestCase(Key.Escape, ModifierKeys.Control | ModifierKeys.Shift, "key")]
    [TestCase(Key.Enter, ModifierKeys.Control | ModifierKeys.Shift, "key")]
    [TestCase(Key.None, ModifierKeys.Control | ModifierKeys.Shift, "key")]
    public void InvalidModifiedDialogDeleteAndUnsupportedKeysKeepTheOldGestureWithoutAvailabilityChecks(Key key,
        ModifierKeys modifiers, string reason)
    {
        var window = Window(new MonitorPreferences { ShowShortcut = "Ctrl+Alt+J" }, _ =>
            throw new AssertionException("Invalid shortcuts must not probe availability."));
        var box = Box(window);
        var status = Status(window);
        var input = Input(key);
        window.CaptureShortcut(key, modifiers, input);
        var problem = reason switch
        {
            "modifiers" => Strings.UseAtLeastTwoOfCtrlAltAndShiftIncluding,
            "windows" => Strings.TheWindowsKeyIsReservedForWindowsShortcuts,
            "secure-attention" => Strings.CtrlAltDeleteIsReservedByWindows,
            _ => Strings.UseALetterDigitFunctionOrNavigationKey
        };
        var gesture = new HotkeyGesture((HotkeyModifiers)(int)modifiers, KeyInterop.VirtualKeyFromKey(key));
        Assert.Multiple(() =>
        {
            Assert.That(input.Handled, Is.True);
            Assert.That(box.Text, Is.EqualTo("Ctrl + Alt + J"));
            Assert.That(status.Text, Is.EqualTo(Strings.Format(Strings.CanTBeUsed, gesture.DisplayText, problem)));
        });
    }

    [Test]
    public void CapturingTheCurrentGestureDoesNotQueryAvailability()
    {
        var window = Window(new MonitorPreferences { ShowShortcut = "Ctrl+Alt+J" }, _ =>
            throw new AssertionException("A current gesture is already owned."));
        var status = Status(window);
        var input = Input(Key.J);
        window.CaptureShortcut(Key.J, ModifierKeys.Control | ModifierKeys.Alt, input);
        Assert.Multiple(() =>
        {
            Assert.That(input.Handled, Is.True);
            Assert.That(Box(window).Text, Is.EqualTo("Ctrl + Alt + J"));
            Assert.That(status.Text, Is.EqualTo(Strings.AppliesWhenYouSaveIfACombinationDoesnTAppear));
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void AvailabilityExceptionOccursAfterHandlingButBeforeGestureOrFeedbackChanges(bool handled)
    {
        var failure = new InvalidOperationException("owned capture availability failure");
        var fail = true;
        var probes = 0;
        var input = Input(Key.A, handled);
        var window = Window(new MonitorPreferences { ShowShortcut = "Ctrl+Alt+J" }, gesture =>
        {
            probes++;
            Assert.That(input.Handled, Is.True);
            Assert.That(gesture, Is.EqualTo(new HotkeyGesture(HotkeyModifiers.Control | HotkeyModifiers.Shift, 'A')));
            if (fail) throw failure;
            return true;
        });
        var box = Box(window);
        var status = Status(window);
        Assert.That(Assert.Throws<InvalidOperationException>(() =>
            window.CaptureShortcut(Key.A, ModifierKeys.Control | ModifierKeys.Shift, input)), Is.SameAs(failure));
        Assert.Multiple(() =>
        {
            Assert.That(input.Handled, Is.True);
            Assert.That(box.Text, Is.EqualTo("Ctrl + Alt + J"));
            Assert.That(status.Text, Is.EqualTo("owned shortcut status"));
            Assert.That(probes, Is.EqualTo(1));
        });
        fail = false;
        window.CaptureShortcut(Key.A, ModifierKeys.Control | ModifierKeys.Shift, input);
        Assert.Multiple(() =>
        {
            Assert.That(box.Text, Is.EqualTo("Ctrl + Shift + A"));
            Assert.That(status.Text, Is.EqualTo(Strings.AppliesWhenYouSaveIfACombinationDoesnTAppear));
            Assert.That(probes, Is.EqualTo(2));
        });
        window.CaptureShortcut(Key.A, ModifierKeys.Control | ModifierKeys.Shift, input);
        Assert.That(probes, Is.EqualTo(2));
    }

    [Test]
    public void SeparateDialogsKeepCaptureAndAvailabilityStateIndependent()
    {
        var firstProbes = 0;
        var secondProbes = 0;
        var first = Window(new MonitorPreferences { ShowShortcut = "None" }, _ => { firstProbes++; return true; });
        var second = Window(new MonitorPreferences { ShowShortcut = "None" }, _ => { secondProbes++; return false; });
        var secondStatus = Status(second);
        first.CaptureShortcut(Key.A, ModifierKeys.Control | ModifierKeys.Shift, Input(Key.A));
        Assert.Multiple(() =>
        {
            Assert.That(Box(first).Text, Is.EqualTo("Ctrl + Shift + A"));
            Assert.That(Box(second).Text, Is.EqualTo(Strings.None));
            Assert.That(secondStatus.Text, Is.EqualTo("owned shortcut status"));
            Assert.That((firstProbes, secondProbes), Is.EqualTo((1, 0)));
        });
        second.CaptureShortcut(Key.A, ModifierKeys.Control | ModifierKeys.Shift, Input(Key.A));
        Assert.Multiple(() =>
        {
            Assert.That(Box(first).Text, Is.EqualTo("Ctrl + Shift + A"));
            Assert.That(Box(second).Text, Is.EqualTo(Strings.None));
            Assert.That(secondStatus.Text, Is.EqualTo(Strings.Format(Strings.AnotherAppIsUsingChooseAnother, "Ctrl + Shift + A")));
            Assert.That((firstProbes, secondProbes), Is.EqualTo((1, 1)));
        });
    }
}
