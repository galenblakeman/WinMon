using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using NUnit.Framework;
using WinMon.Shortcuts;

namespace WinMon.Tests;

[TestFixture, Category("Integration"), NonParallelizable]
[Apartment(ApartmentState.STA)]
public sealed class GlobalHotkeyTests
{
    [Test]
    public void Register_ReplacementAndDisableReleasePreviousCombinations()
    {
        var window = new Window { ShowInTaskbar = false };
        try
        {
            using var hotkey = new GlobalHotkey(window, () => Assert.Fail("No key was pressed."));
            Assert.That(hotkey.Register(null), Is.EqualTo(HotkeyStatus.Off));
            Assert.That(hotkey.Current, Is.Null);
            var first = RegisterUnused(hotkey);
            Assert.That(hotkey.Current, Is.EqualTo(first));
            Assert.That(GlobalHotkey.IsAvailable(first), Is.False);
            var replacement = RegisterUnused(hotkey, first);
            Assert.That(hotkey.Current, Is.EqualTo(replacement));
            Assert.That(GlobalHotkey.IsAvailable(first), Is.True);
            Assert.That(GlobalHotkey.IsAvailable(first), Is.True, "Availability probing must release its registration.");
            Assert.That(GlobalHotkey.IsAvailable(replacement), Is.False);
            Assert.That(hotkey.Register(null), Is.EqualTo(HotkeyStatus.Off));
            Assert.That(hotkey.Current, Is.Null);
            Assert.That(GlobalHotkey.IsAvailable(replacement), Is.True);
            hotkey.Unregister();
            Assert.That(hotkey.Current, Is.Null);
        }
        finally
        {
            window.Close();
        }
    }

    [Test]
    public void ConflictingRegistration_ReportsInUseAndReleasesTheContendersPreviousGesture()
    {
        var ownerWindow = new Window { ShowInTaskbar = false };
        var contenderWindow = new Window { ShowInTaskbar = false };
        try
        {
            using var owner = new GlobalHotkey(ownerWindow, () => { });
            using var contender = new GlobalHotkey(contenderWindow, () => { });
            var held = RegisterUnused(owner);
            var previous = RegisterUnused(contender, held);
            Assert.That(contender.Register(held), Is.EqualTo(HotkeyStatus.InUse));
            Assert.That(contender.Current, Is.Null);
            Assert.That(owner.Current, Is.EqualTo(held));
            Assert.That(GlobalHotkey.IsAvailable(previous), Is.True);
            owner.Unregister();
            Assert.That(contender.Register(held), Is.EqualTo(HotkeyStatus.Registered));
            Assert.That(contender.Current, Is.EqualTo(held));
        }
        finally
        {
            contenderWindow.Close();
            ownerWindow.Close();
        }
    }

    [Test]
    public void WindowMessages_OnlyDispatchTheRegisteredIdAndDisposeRemovesTheHook()
    {
        const int wmHotkey = 0x0312;
        const int winMonHotkeyId = 0x5749;
        var window = new Window { ShowInTaskbar = false };
        var presses = 0;
        try
        {
            using var hotkey = new GlobalHotkey(window, () => presses++);
            var gesture = RegisterUnused(hotkey);
            var handle = new WindowInteropHelper(window).Handle;
            var keyData = (nint)((gesture.VirtualKey << 16) | (int)gesture.Modifiers);
            SendMessage(handle, 0, winMonHotkeyId, keyData);
            SendMessage(handle, wmHotkey, winMonHotkeyId + 1, keyData);
            Assert.That(presses, Is.Zero);
            SendMessage(handle, wmHotkey, winMonHotkeyId, keyData);
            Assert.That(presses, Is.EqualTo(1));
            hotkey.Dispose();
            Assert.That(hotkey.Current, Is.Null);
            Assert.That(GlobalHotkey.IsAvailable(gesture), Is.True);
            SendMessage(handle, wmHotkey, winMonHotkeyId, keyData);
            Assert.That(presses, Is.EqualTo(1), "Disposed hotkeys must stop receiving window messages.");
        }
        finally
        {
            window.Close();
        }
    }

    private static HotkeyGesture RegisterUnused(GlobalHotkey hotkey, HotkeyGesture? except = null)
    {
        for (var key = 0x87; key >= 0x7C; key--)
        {
            if (key == except?.VirtualKey) continue;
            var gesture = new HotkeyGesture(HotkeyModifiers.Control | HotkeyModifiers.Alt | HotkeyModifiers.Shift, key);
            var status = hotkey.Register(gesture);
            if (status == HotkeyStatus.Registered) return gesture;
            Assert.That(status, Is.EqualTo(HotkeyStatus.InUse), $"Unexpected registration failure for {gesture}.");
        }
        Assert.Fail("No unused Ctrl+Alt+Shift+F13 through F24 combination is available for this test.");
        return null!;
    }

    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern nint SendMessage(nint window, int message, nint wParam, nint lParam);
}
