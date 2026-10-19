using Microsoft.Win32.SafeHandles;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using WinMon.Monitoring;

namespace WinMon.Controls;

/// <summary>Loads icons off the UI thread, with a bounded cache keyed by process lifetime.</summary>
internal static class ProcessIconProvider
{
    private static readonly Dictionary<(int, long), Task<BitmapSource>> cache = new();
    private static readonly SemaphoreSlim workers = new(2);
    internal static BitmapSource Fallback { get; } = Convert(System.Drawing.SystemIcons.Application);

    // Called on the UI thread. The cache retains images, never native process/icon handles.
    internal static Task<BitmapSource> GetAsync(ProcessUsage process)
    {
        var key = (process.ProcessId, process.CreationTicks);
        if (cache.TryGetValue(key, out var task)) return task;
        if (cache.Count >= 256) cache.Remove(cache.Keys.First());
        task = Task.Run(async () =>
        {
            await workers.WaitAsync().ConfigureAwait(false);
            try { return Load(process); }
            finally { workers.Release(); }
        });
        cache.Add(key, task);
        return task;
    }

    private static BitmapSource Load(ProcessUsage process)
    {
        try
        {
            using var handle = OpenProcess(0x1000, false, process.ProcessId);
            if (handle.IsInvalid || !GetProcessTimes(handle, out var creation, out _, out _, out _) ||
                creation != process.CreationTicks) return Fallback;
            var path = new StringBuilder(32768);
            var length = path.Capacity;
            if (!QueryFullProcessImageNameW(handle, 0, path, ref length)) return Fallback;
            using var icon = System.Drawing.Icon.ExtractAssociatedIcon(path.ToString());
            return icon is null ? Fallback : Convert(icon);
        }
        catch (Exception ex) when (ex is ArgumentException or System.ComponentModel.Win32Exception or
            IOException or UnauthorizedAccessException or System.Runtime.InteropServices.ExternalException)
        {
            Trace.TraceInformation("Process icon unavailable for PID {0}: {1}", process.ProcessId, ex.Message);
            return Fallback;
        }
    }

    private static BitmapSource Convert(System.Drawing.Icon icon)
    {
        var image = Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
        image.Freeze();
        return image;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeProcessHandle OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetProcessTimes(SafeProcessHandle process, out long creation, out long exit, out long kernel, out long user);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageNameW(SafeProcessHandle process, uint flags, StringBuilder path, ref int length);
}
