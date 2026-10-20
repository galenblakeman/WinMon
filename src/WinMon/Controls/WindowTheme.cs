using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
// ReSharper disable StringLiteralTypo

namespace WinMon.Controls;

/// <summary>Keeps the standard Windows caption in sync with the application palette.</summary>
internal static class WindowTheme
{
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
        var dark = !SystemParameters.HighContrast && surface.R * 299 + surface.G * 587 + surface.B * 114 < 128000 ? 1 : 0;
        // A failed cosmetic request must not prevent the monitor from running.
        _ = DwmSetWindowAttribute(handle, 20 /* DWMWA_USE_IMMERSIVE_DARK_MODE */, ref dark, sizeof(int));
        _ = DwmSetWindowAttribute(handle, 35 /* DWMWA_CAPTION_COLOR */, ref caption, sizeof(int));
        _ = DwmSetWindowAttribute(handle, 36 /* DWMWA_TEXT_COLOR */, ref text, sizeof(int));
    }

    private static int ColorValue(Window window, string key)
    {
        var color = ((SolidColorBrush)window.FindResource(key)).Color;
        return color.R | (color.G << 8) | (color.B << 16);
    }

    /// <summary>Native Minimize/Maximize/Close bounds in window-relative device-independent units, if DWM reports them.</summary>
    internal static Rect? CaptionButtonBounds(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero ||
            DwmGetWindowAttribute(handle, 5 /* DWMWA_CAPTION_BUTTON_BOUNDS */, out var bounds, Marshal.SizeOf<NativeRect>()) != 0 ||
            bounds.Right <= bounds.Left || bounds.Bottom <= bounds.Top) return null;
        var dpi = VisualTreeHelper.GetDpi(window);
        return new Rect(bounds.Left / dpi.DpiScaleX, bounds.Top / dpi.DpiScaleY,
            (bounds.Right - bounds.Left) / dpi.DpiScaleX, (bounds.Bottom - bounds.Top) / dpi.DpiScaleY);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr window, int attribute, out NativeRect value, int size);
}
