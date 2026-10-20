using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace WinMon.Monitoring;

/// <summary>Ends only the confirmed process lifetime, using one handle for validation and termination.</summary>
internal static class ProcessTerminator
{
    internal interface ITarget : IDisposable
    {
        long CreationTicks { get; }
        bool HasExited { get; }
        bool IsCritical { get; }
        void Terminate();
    }

    internal static bool IsEligible(ProcessUsage process, bool live, bool grouped) =>
        live && !grouped && process.MemberCount == 1 && process.ProcessId > 4 &&
        process.ProcessId != Environment.ProcessId && process.CreationTicks > 0;

    // Return null only when Windows accepted the termination request. It can complete asynchronously.
    internal static string? End(ProcessUsage process, Func<int, ITarget>? open = null)
    {
        if (!IsEligible(process, true, false)) return "This process cannot be ended from WinMon.";
        try
        {
            using var target = (open ?? Open)(process.ProcessId);
            if (target.CreationTicks != process.CreationTicks)
                return "The original process has exited and its PID has been reused. No process was ended.";
            if (target.HasExited) return "This process has already exited.";
            if (target.IsCritical) return "Windows identifies this as a critical process. WinMon will not end it.";
            target.Terminate();
            return null;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 5)
        { return "Access denied. Windows did not allow WinMon to end this process, or it has already exited."; }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 87)
        { return "This process has already exited or is no longer available."; }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or UnauthorizedAccessException)
        { return $"The process could not be ended: {ex.Message}"; }
    }

    private static ITarget Open(int pid) => new NativeTarget(pid);

    private sealed class NativeTarget : ITarget
    {
        private readonly SafeProcessHandle _handle;
        internal NativeTarget(int pid)
        {
            _handle = OpenProcess(0x1001, false, pid); // QUERY_LIMITED_INFORMATION | TERMINATE
            if (_handle.IsInvalid)
            {
                var error = Marshal.GetLastWin32Error();
                _handle.Dispose();
                throw new Win32Exception(error);
            }
        }
        public long CreationTicks
        {
            get
            {
                if (!GetProcessTimes(_handle, out var created, out _, out _, out _)) throw new Win32Exception();
                return created;
            }
        }
        public bool HasExited
        {
            get
            {
                if (!GetProcessTimes(_handle, out _, out var exited, out _, out _)) throw new Win32Exception();
                return exited != 0;
            }
        }
        public bool IsCritical
        {
            get
            {
                if (!IsProcessCritical(_handle, out var critical)) throw new Win32Exception();
                return critical;
            }
        }
        public void Terminate()
        {
            if (!TerminateProcess(_handle, 1)) throw new Win32Exception();
        }
        public void Dispose() => _handle.Dispose();
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeProcessHandle OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, int pid);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetProcessTimes(SafeProcessHandle process, out long created, out long exited, out long kernel, out long user);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsProcessCritical(SafeProcessHandle process, [MarshalAs(UnmanagedType.Bool)] out bool critical);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TerminateProcess(SafeProcessHandle process, uint exitCode);
}
