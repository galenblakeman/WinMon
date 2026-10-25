using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;
using WinMon.Preferences;

// ReSharper disable StringLiteralTypo

namespace WinMon.Controls;

/// <summary>Keeps the standard Windows caption in sync with the application palette.</summary>
internal static class WindowTheme
{
    /// <summary>Use the system backdrop only when DWM supports it; keep native caption buttons.</summary>
    internal static bool SetBackdrop(MainWindow window, ThemeFinishes.BackdropKind backdrop)
    {
        var handle = new WindowInteropHelper(window).Handle;
        var canSet = OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22621) && handle != IntPtr.Zero;
        var requested = !SystemParameters.HighContrast && SystemParameters.IsGlassEnabled
            ? backdrop : ThemeFinishes.BackdropKind.None;
        var kind = requested == ThemeFinishes.BackdropKind.None ? 1 : (int)requested; // DWMSBT_NONE
        var applied = canSet && DwmSetWindowAttribute(handle, 38 /* DWMWA_SYSTEMBACKDROP_TYPE */,
            ref kind, sizeof(int)) == 0;
        var active = applied && requested != ThemeFinishes.BackdropKind.None;
        if (canSet && !applied)
        {
            kind = 1;
            _ = DwmSetWindowAttribute(handle, 38, ref kind, sizeof(int));
        }

        if (WindowChrome.GetWindowChrome(window) is { } chrome)
            chrome.GlassFrameThickness = active ? new Thickness(-1) : new Thickness(0, chrome.CaptionHeight, 0, 0);
        if (handle != IntPtr.Zero && HwndSource.FromHwnd(handle)?.CompositionTarget is { } target)
            target.BackgroundColor = Colors.Transparent;
        return active;
    }

    internal static void Attach(Window window)
    {
        window.SourceInitialized += (_, _) => Apply(window);
    }

    internal static void Apply(Window window)
    {
        // Caption colors are documented starting with Windows 11. Older systems retain native chrome.
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000)) return;
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero) return;
        var captionKey = window is MainWindow ? "TitleBarSurface" : "Surface";
        var caption = SystemParameters.HighContrast ? -1 : ColorValue(window, captionKey);
        var text = SystemParameters.HighContrast ? -1 : ColorValue(window, "Text");
        var surface = ((SolidColorBrush)window.FindResource("Surface")).Color;
        var dark = !SystemParameters.HighContrast && surface.R * 299 + surface.G * 587 + surface.B * 114 < 128000
            ? 1
            : 0;
        // A failed cosmetic request must not prevent the monitor from running.
        _ = DwmSetWindowAttribute(handle, 20 /* DWMWA_USE_IMMERSIVE_DARK_MODE */, ref dark, sizeof(int));
        _ = DwmSetWindowAttribute(handle, 35 /* DWMWA_CAPTION_COLOR */, ref caption, sizeof(int));
        _ = DwmSetWindowAttribute(handle, 36 /* DWMWA_TEXT_COLOR */, ref text, sizeof(int));
    }

    private static int ColorValue(Window window, string key)
    {
        var color = window.FindResource(key) switch
        {
            SolidColorBrush solid => solid.Color,
            LinearGradientBrush gradient => gradient.GradientStops[1].Color,
            DrawingBrush { Drawing: DrawingGroup group } when group.Children[0] is GeometryDrawing
                { Brush: LinearGradientBrush gradient } => gradient.GradientStops[1].Color,
            _ => Colors.Black
        };
        return color.R | (color.G << 8) | (color.B << 16);
    }

    /// <summary>Native Minimize/Maximize/Close bounds in window-relative device-independent units, if DWM reports them.</summary>
    internal static Rect? CaptionButtonBounds(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero ||
            DwmGetWindowAttribute(handle, 5 /* DWMWA_CAPTION_BUTTON_BOUNDS */, out var bounds,
                Marshal.SizeOf<NativeRect>()) != 0 ||
            bounds.Right <= bounds.Left || bounds.Bottom <= bounds.Top) return null;
        var dpi = VisualTreeHelper.GetDpi(window);
        return new Rect(bounds.Left / dpi.DpiScaleX, bounds.Top / dpi.DpiScaleY,
            (bounds.Right - bounds.Left) / dpi.DpiScaleX, (bounds.Bottom - bounds.Top) / dpi.DpiScaleY);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left, Top, Right, Bottom;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr window, int attribute, out NativeRect value, int size);
}
