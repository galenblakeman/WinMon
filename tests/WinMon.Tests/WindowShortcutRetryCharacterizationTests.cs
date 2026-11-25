using System.Reflection;
using System.Windows;
using System.Windows.Threading;
using NUnit.Framework;
using WinMon.Resources;
using WinMon.Shortcuts;

namespace WinMon.Tests;

public sealed partial class MainWindowWorkflowCharacterizationTests
{
    [Test, Category("Integration")]
    public void ShortcutConflictRetriesNineTimesAndStopsOnTheTenthTick()
    {
        using var ui = new UpdateUi(_directory, true);
        using var retry = new ShortcutRetry(ui.Window);
        for (var tick = 1; tick <= 10; tick++)
        {
            retry.Tick();
            Assert.That(retry.Timer.IsEnabled, Is.EqualTo(tick < 10), $"Tick {tick}");
            retry.AssertConflict();
        }
    }

    [TestCase(0), TestCase(9), Category("Integration")]
    public void ShortcutRecoveryRegistersBeforeTheRetryLimitAndStopsFurtherTicks(int priorTicks)
    {
        using var ui = new UpdateUi(_directory, true);
        using var retry = new ShortcutRetry(ui.Window);
        retry.ConflictingTicks(priorTicks);
        retry.Owner.Unregister();

        retry.Tick();

        Assert.That(retry.Status, Is.EqualTo(HotkeyStatus.Registered));
        Assert.That(retry.Contender.Current, Is.EqualTo(retry.Gesture));
        Assert.That(retry.Owner.Current, Is.Null);
        Assert.That(retry.Timer.IsEnabled, Is.False);
        Assert.That(retry.StatusText, Is.EqualTo(Strings.WorksFromAnyAppWhileWinMonIsRunning));
    }

    [TestCase(0), TestCase(9), Category("Integration")]
    public void ClosingStopsShortcutRetriesWithoutTakingANowAvailableGesture(int priorTicks)
    {
        using var ui = new UpdateUi(_directory, true);
        using var retry = new ShortcutRetry(ui.Window);
        retry.ConflictingTicks(priorTicks);
        retry.Owner.Unregister();
        ui.Window.IsClosing = true;

        retry.Tick();

        Assert.That(retry.Status, Is.EqualTo(HotkeyStatus.InUse));
        Assert.That(retry.Contender.Current, Is.Null);
        Assert.That(retry.Timer.IsEnabled, Is.False);
        Assert.That(GlobalHotkey.IsAvailable(retry.Gesture), Is.True);
        Assert.That(retry.StatusText, Is.EqualTo(Strings.AnotherAppIsUsingThisShortcutChooseAnother));
    }

    [Test, Category("Integration")]
    public void MissingRetryReferencePreservesTheNullableStopGuardDuringClosing()
    {
        using var ui = new UpdateUi(_directory, true);
        using var retry = new ShortcutRetry(ui.Window);
        ShortcutRetry.Field("_hotkeyRetry").SetValue(ui.Window, null);
        retry.Owner.Unregister();
        ui.Window.IsClosing = true;

        Assert.DoesNotThrow(retry.Tick);

        Assert.That(retry.Status, Is.EqualTo(HotkeyStatus.InUse));
        Assert.That(retry.Contender.Current, Is.Null);
        Assert.That(retry.Timer.IsEnabled, Is.True,
            "The original callback only stops the window's current retry reference; fixture cleanup owns this timer.");
        Assert.That(GlobalHotkey.IsAvailable(retry.Gesture), Is.True);
    }

    private sealed class ShortcutRetry : IDisposable
    {
        private const BindingFlags Members = BindingFlags.Instance | BindingFlags.NonPublic;
        private readonly MainWindow _window;
        private readonly Window _ownerWindow = new() { ShowInTaskbar = false };
        private readonly Window _contenderWindow = new() { ShowInTaskbar = false };
        private EventHandler _tick = null!;
        internal GlobalHotkey Owner { get; private set; } = null!;
        internal GlobalHotkey Contender { get; private set; } = null!;
        internal HotkeyGesture Gesture { get; private set; } = null!;
        internal DispatcherTimer Timer { get; private set; } = null!;
        internal HotkeyStatus Status => (HotkeyStatus)Field("_hotkeyStatus").GetValue(_window)!;
        internal string StatusText => (string)typeof(MainWindow).GetProperty("ShortcutStatus", Members)!.GetValue(_window)!;

        internal ShortcutRetry(MainWindow window)
        {
            _window = window;
            try
            {
                Assert.That(Field("_hotkey").GetValue(window), Is.Null);
                Assert.That(Field("_hotkeyRetry").GetValue(window), Is.Null);
                Owner = new GlobalHotkey(_ownerWindow, () => Assert.Fail("No physical hotkey input is expected."));
                Contender = new GlobalHotkey(_contenderWindow, () => Assert.Fail("No physical hotkey input is expected."));
                Gesture = HoldUnused(Owner);
                Field("_hotkey").SetValue(window, Contender);
                typeof(MainWindow).GetMethod("RegisterShortcut", Members)!
                    .CreateDelegate<Action<HotkeyGesture?>>(window)(Gesture);
                Timer = (DispatcherTimer)Field("_hotkeyRetry").GetValue(window)!;
                // Invoke the existing event callback, not FireTick, which also owns scheduler queue state.
                _tick = (EventHandler)typeof(DispatcherTimer).GetField("Tick", Members)!.GetValue(Timer)!;
                Assert.That(Timer.Interval, Is.EqualTo(TimeSpan.FromSeconds(1)));
                Assert.That(Timer.Dispatcher, Is.SameAs(window.Dispatcher));
                Assert.That(Timer.IsEnabled, Is.True);
                AssertConflict();
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        internal static FieldInfo Field(string name) => typeof(MainWindow).GetField(name, Members)!;
        internal void Tick()
        {
            Assert.That(_window.Dispatcher.CheckAccess(), Is.True);
            _tick(Timer, EventArgs.Empty);
        }

        internal void ConflictingTicks(int count)
        {
            for (var index = 0; index < count; index++)
            {
                Tick();
                Assert.That(Timer.IsEnabled, Is.True);
                AssertConflict();
            }
        }

        internal void AssertConflict()
        {
            Assert.That(Status, Is.EqualTo(HotkeyStatus.InUse));
            Assert.That(Contender.Current, Is.Null);
            Assert.That(Owner.Current, Is.EqualTo(Gesture));
            Assert.That(StatusText, Is.EqualTo(Strings.AnotherAppIsUsingThisShortcutChooseAnother));
        }

        // Same non-invasive unused-key search as GlobalHotkeyTests: never replace another owner's gesture.
        private static HotkeyGesture HoldUnused(GlobalHotkey owner)
        {
            for (var key = 0x87; key >= 0x7C; key--)
            {
                var gesture = new HotkeyGesture(HotkeyModifiers.Control | HotkeyModifiers.Alt | HotkeyModifiers.Shift, key);
                var status = owner.Register(gesture);
                if (status == HotkeyStatus.Registered) return gesture;
                Assert.That(status, Is.EqualTo(HotkeyStatus.InUse));
            }
            throw new AssertionException("No unused Ctrl+Alt+Shift+F13 through F24 test combination is available.");
        }

        public void Dispose()
        {
            Timer?.Stop();
            Field("_hotkeyRetry").SetValue(_window, null);
            if (ReferenceEquals(Field("_hotkey").GetValue(_window), Contender)) Field("_hotkey").SetValue(_window, null);
            Contender?.Dispose();
            Owner?.Dispose();
            _contenderWindow.Close();
            _ownerWindow.Close();
            if (Gesture is not null) Assert.That(GlobalHotkey.IsAvailable(Gesture), Is.True, "Fixture hotkey must be released.");
        }
    }
}
