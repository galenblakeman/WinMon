using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using Microsoft.Win32.SafeHandles;
using WinMon.Monitoring;

namespace WinMon.Controls;

/// <summary>Loads icons off the UI thread, with a bounded cache keyed by process lifetime.</summary>
internal static class ProcessIconProvider
{
    private static readonly Dictionary<(int, long), Task<BitmapSource>> Cache = new();
    private static readonly SemaphoreSlim Workers = new(2);
    internal static BitmapSource Fallback { get; } = Convert(SystemIcons.Application);

    // Called on the UI thread. The cache retains images, never native process/icon handles.
    internal static Task<BitmapSource> GetAsync(ProcessUsage process)
    {
        var key = (process.ProcessId, process.CreationTicks);
        if (Cache.TryGetValue(key, out var task)) return task;
        if (Cache.Count >= 256) Cache.Remove(Cache.Keys.First());
        task = Task.Run(async () =>
        {
            await Workers.WaitAsync().ConfigureAwait(false);
            try
            {
                return Load(process);
            }
            finally
            {
                Workers.Release();
            }
        });
        Cache.Add(key, task);
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
            using var icon = Icon.ExtractAssociatedIcon(path.ToString());
            return icon is null ? Fallback : Convert(icon);
        }
        catch (Exception ex) when (ex is ArgumentException or Win32Exception or
                                       IOException or UnauthorizedAccessException or ExternalException)
        {
            Trace.TraceInformation("Process icon unavailable for PID {0}: {1}", process.ProcessId, ex.Message);
            return Fallback;
        }
    }

    private static readonly Dictionary<string, Task<BitmapSource>> PathCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>An executable's icon by path, for apps that may have exited; the fallback when there is no path.</summary>
    /// <remarks>Called on the UI thread, like <see cref="GetAsync"/>.</remarks>
    internal static Task<BitmapSource> GetForPathAsync(string path)
    {
        if (path.Length == 0) return Task.FromResult(Fallback);
        if (PathCache.TryGetValue(path, out var task)) return task;
        if (PathCache.Count >= 256) PathCache.Remove(PathCache.Keys.First());
        task = Task.Run(async () =>
        {
            await Workers.WaitAsync().ConfigureAwait(false);
            try
            {
                using var icon = Icon.ExtractAssociatedIcon(path);
                return icon is null ? Fallback : Convert(icon);
            }
            catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or
                                           ExternalException)
            {
                Trace.TraceInformation("Executable icon unavailable for {0}: {1}", path, ex.Message);
                return Fallback;
            }
            finally
            {
                Workers.Release();
            }
        });
        PathCache.Add(path, task);
        return task;
    }

    private static BitmapSource Convert(Icon icon)
    {
        var image = Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty,
            BitmapSizeOptions.FromEmptyOptions());
        image.Freeze();
        return image;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeProcessHandle OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit,
        int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetProcessTimes(SafeProcessHandle process, out long creation, out long exit,
        out long kernel, out long user);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageNameW(SafeProcessHandle process, uint flags, StringBuilder path,
        ref int length);
}
