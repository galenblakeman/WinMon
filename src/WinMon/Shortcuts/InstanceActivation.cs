using System.Runtime.InteropServices;
// ReSharper disable IdentifierTypo

namespace WinMon.Shortcuts;

/// <summary>
/// Lets a second WinMon launch, including one Explorer starts for the Start menu hotkey, show the running instance.
/// </summary>
internal static class InstanceActivation
{
    private const string EventName = @"Local\WinMon.CpuMonitor.Show";
    private const int AsfwAny = -1;

    /// <summary>Call <paramref name="requested"/> on a pool thread each time another launch asks to be shown.</summary>
    /// <returns>Disposing stops listening.</returns>
    public static IDisposable Listen(Action requested)
    {
        var signal = new EventWaitHandle(false, EventResetMode.AutoReset, EventName);
        var wait = ThreadPool.RegisterWaitForSingleObject(signal, (_, _) => requested(), null, Timeout.Infinite, false);
        return new Listener(signal, wait);
    }

    /// <summary>Ask the running instance to show itself.</summary>
    /// <returns><see langword="false"/> when no instance listened within two seconds.</returns>
    public static bool SignalRunningInstance()
    {
        // The running instance may still be starting; it creates the event right after taking the instance mutex.
        for (var attempt = 0; attempt < 20; attempt++)
        {
            if (EventWaitHandle.TryOpenExisting(EventName, out var signal))
            {
                using (signal)
                {
                    // This launch came from user input, so it may hand foreground rights to the running instance.
                    AllowSetForegroundWindow(AsfwAny);
                    return signal.Set();
                }
            }
            Thread.Sleep(100);
        }
        return false;
    }

    private sealed class Listener(EventWaitHandle signal, RegisteredWaitHandle wait) : IDisposable
    {
        public void Dispose()
        {
            wait.Unregister(null);
            signal.Dispose();
        }
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllowSetForegroundWindow(int processId);
}
