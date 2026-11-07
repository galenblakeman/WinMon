using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Microsoft.Win32;
using Forms = System.Windows.Forms;
using Point = System.Drawing.Point;

namespace WinMon;

public partial class MainWindow
{
    private WindowPosition? _windowPosition;
    private HwndSource? _positionSource;
    private DispatcherTimer? _positionRecoveryTimer;
    private bool _positionRecoveryPending;
    private bool _movingWindow;

    private void InitializeWindowPosition()
    {
        _positionSource = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
        _positionSource?.AddHook(WindowPositionHook);
        _positionRecoveryTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _positionRecoveryTimer.Tick += (_, _) => RecoverWindowPosition();
        RememberWindowPosition();
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        StateChanged += OnPositionStateChanged;
    }

    private void RememberWindowPosition()
    {
        if (WindowState != WindowState.Normal || _positionSource is null ||
            !GetWindowRect(_positionSource.Handle, out var bounds)) return;
        var screen = Forms.Screen.FromHandle(_positionSource.Handle);
        _windowPosition = WindowPosition.Capture(screen.DeviceName, screen.WorkingArea,
            new Point(bounds.Left, bounds.Top));
    }

    private IntPtr WindowPositionHook(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        // Automatic display rearrangements must never replace the user's chosen monitor.
        if (message == 0x0231) _movingWindow = true; // WM_ENTERSIZEMOVE
        if (message == 0x0232) // WM_EXITSIZEMOVE
        {
            _movingWindow = false;
            _positionRecoveryTimer?.Stop();
            _positionRecoveryPending = false;
            RememberWindowPosition();
        }

        if (message == 0x0047 && (GetKeyState(0x5B) < 0 || GetKeyState(0x5C) < 0)) // Win+arrow placement
        {
            _positionRecoveryTimer?.Stop();
            _positionRecoveryPending = false;
            Dispatcher.BeginInvoke(() =>
            {
                if (!IsClosing) RememberWindowPosition();
            });
        }

        return IntPtr.Zero;
    }

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Resume) QueueWindowPositionRecovery();
    }

    private void OnPositionStateChanged(object? sender, EventArgs e)
    {
        if (_positionRecoveryPending && WindowState == WindowState.Normal) QueueWindowPositionRecovery();
    }

    private void QueueWindowPositionRecovery()
    {
        if (IsClosing) return;
        Dispatcher.BeginInvoke(() =>
        {
            if (IsClosing || _positionRecoveryTimer is null) return;
            _positionRecoveryPending = true;
            _positionRecoveryTimer.Stop();
            _positionRecoveryTimer.Start();
        });
    }

    private void RecoverWindowPosition()
    {
        _positionRecoveryTimer?.Stop();
        if (IsClosing || _movingWindow || WindowState != WindowState.Normal || _positionSource is null) return;
        _positionRecoveryPending = false;
        if (_windowPosition is not null && GetWindowRect(_positionSource.Handle, out var bounds) &&
            _windowPosition.Resolve(Forms.Screen.AllScreens.Select(s => (s.DeviceName, s.WorkingArea)),
                new System.Drawing.Size(bounds.Right - bounds.Left, bounds.Bottom - bounds.Top)) is { } location)
        {
            const uint swpNoSize = 0x0001, swpNoZOrder = 0x0004, swpNoActivate = 0x0010;
            if (SetWindowPos(_positionSource.Handle, IntPtr.Zero, location.X, location.Y, 0, 0,
                    swpNoSize | swpNoZOrder | swpNoActivate)) return;
            Trace.TraceWarning("WinMon could not restore its monitor position: {0}", Marshal.GetLastPInvokeError());
        }

        // Keep the original target for a later reconnect, unless the user moves the window.
        EnsureVisible();
    }

    private void CloseWindowPosition()
    {
        _positionRecoveryTimer?.Stop();
        _positionSource?.RemoveHook(WindowPositionHook);
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        StateChanged -= OnPositionStateChanged;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeWindowRect
    {
        public int Left, Top, Right, Bottom;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hwnd, out NativeWindowRect bounds);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int width, int height,
        uint flags);

    [DllImport("user32.dll")]
    private static extern short GetKeyState(int virtualKey);
}
