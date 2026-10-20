using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace WinMon.Shortcuts;

/// <summary>How the show shortcut currently reaches WinMon.</summary>
public enum HotkeyStatus
{
    /// <summary>The shortcut is turned off.</summary>
    Off,

    /// <summary>WinMon holds the combination with RegisterHotKey.</summary>
    Registered,

    /// <summary>Explorer holds the combination for the Start menu shortcut and starts or signals WinMon.</summary>
    StartMenu,

    /// <summary>Another application already holds the combination.</summary>
    InUse,

    /// <summary>Registration failed for another reason.</summary>
    Failed
}

/// <summary>Holds one system-wide hotkey on a window and reports each press.</summary>
internal sealed class GlobalHotkey : IDisposable
{
    private const int HotkeyId = 0x5749;
    private const int ProbeId = 0x574A;
    private const int WmHotkey = 0x0312;
    private const int WmSysCommand = 0x0112;
    private const int ScHotkey = 0xF150;
    private const int ErrorHotkeyAlreadyRegistered = 1409;
    private const uint ModNoRepeat = 0x4000;

    private readonly HwndSource _source;
    private readonly Action _pressed;
    private bool _registered;

    /// <summary>Hook the window's messages; the hotkey stays registered while the window is hidden.</summary>
    public GlobalHotkey(Window window, Action pressed)
    {
        _pressed = pressed;
        _source = HwndSource.FromHwnd(new WindowInteropHelper(window).EnsureHandle())
            ?? throw new InvalidOperationException("The window handle is unavailable.");
        _source.AddHook(Hook);
    }

    /// <summary>Gets the combination WinMon currently holds, if any.</summary>
    public HotkeyGesture? Current { get; private set; }

    /// <summary>Replace any held combination with <paramref name="gesture"/>.</summary>
    public HotkeyStatus Register(HotkeyGesture? gesture)
    {
        Unregister();
        if (gesture is null) return HotkeyStatus.Off;
        if (!RegisterHotKey(_source.Handle, HotkeyId, (uint)gesture.Modifiers | ModNoRepeat, (uint)gesture.VirtualKey))
            return Marshal.GetLastPInvokeError() == ErrorHotkeyAlreadyRegistered ? HotkeyStatus.InUse : HotkeyStatus.Failed;
        _registered = true;
        Current = gesture;
        return HotkeyStatus.Registered;
    }

    /// <summary>Release the held combination so another owner, such as Explorer, can take it.</summary>
    public void Unregister()
    {
        if (_registered) UnregisterHotKey(_source.Handle, HotkeyId);
        _registered = false;
        Current = null;
    }

    /// <summary>Report whether no other application holds <paramref name="gesture"/>, by registering and releasing it.</summary>
    public static bool IsAvailable(HotkeyGesture gesture)
    {
        if (!RegisterHotKey(IntPtr.Zero, ProbeId, (uint)gesture.Modifiers | ModNoRepeat, (uint)gesture.VirtualKey)) return false;
        UnregisterHotKey(IntPtr.Zero, ProbeId);
        return true;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        Unregister();
        _source.RemoveHook(Hook);
    }

    private IntPtr Hook(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        // SC_HOTKEY arrives when Explorer started WinMon from the shortcut and passed the hotkey to its first window.
        if ((message != WmHotkey || wParam.ToInt32() != HotkeyId) &&
            (message != WmSysCommand || (wParam.ToInt32() & 0xFFF0) != ScHotkey)) return IntPtr.Zero;
        handled = true;
        _pressed();
        return IntPtr.Zero;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
