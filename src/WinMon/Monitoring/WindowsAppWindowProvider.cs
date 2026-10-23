using System.Runtime.InteropServices;

namespace WinMon.Monitoring;

/// <summary>Observes window ownership once per sample, without opening each process.</summary>
internal sealed class WindowsAppWindowProvider
{
    private readonly HashSet<int> _processIds = [];
    private readonly EnumWindowsCallback _callback;

    internal WindowsAppWindowProvider()
    {
        _callback = Observe;
    }

    // The returned set is reused on the next capture. A failed enumeration is unavailable, not background.
    internal IReadOnlySet<int>? Capture()
    {
        _processIds.Clear();
        return EnumWindows(_callback, 0) ? _processIds : null;
    }

    private bool Observe(nint window, nint parameter)
    {
        // Minimized windows retain WS_VISIBLE and remain apps. Hidden tray-only windows do not.
        if (IsWindowVisible(window) && GetWindow(window, 4 /* GW_OWNER */) == 0 &&
            GetWindowThreadProcessId(window, out var processId) != 0)
            _processIds.Add((int)processId);
        return true;
    }

    private delegate bool EnumWindowsCallback(nint window, nint parameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsCallback callback, nint parameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint window);

    [DllImport("user32.dll")]
    private static extern nint GetWindow(nint window, uint command);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint window, out uint processId);
}