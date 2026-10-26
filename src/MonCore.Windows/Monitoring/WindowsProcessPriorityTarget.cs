using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace WinMon.Monitoring;

public interface IProcessPriorityTarget : IDisposable
{
    long CreationTicks { get; }
    bool HasExited { get; }
    bool IsCritical { get; }
    ProcessPriorityClass PriorityClass { get; }
    void SetPriorityClass(ProcessPriorityClass priority);
}

public static class WindowsProcessPriorityTarget
{
    public static IProcessPriorityTarget Open(int pid)
    {
        return new NativeTarget(pid);
    }

    private sealed class NativeTarget : IProcessPriorityTarget
    {
        private readonly SafeProcessHandle _handle;

        internal NativeTarget(int pid)
        {
            _handle = OpenProcess(0x1200, false, pid); // QUERY_LIMITED_INFORMATION | SET_INFORMATION
            if (!_handle.IsInvalid) return;
            var error = Marshal.GetLastWin32Error();
            _handle.Dispose();
            throw new Win32Exception(error);
        }

        public long CreationTicks => !GetProcessTimes(_handle, out var created, out _, out _, out _)
            ? throw new Win32Exception()
            : created;

        public bool HasExited
        {
            get
            {
                if (!GetProcessTimes(_handle, out _, out var exited, out _, out _)) throw new Win32Exception();
                return exited != 0;
            }
        }

        public bool IsCritical => !IsProcessCritical(_handle, out var critical) ? throw new Win32Exception() : critical;

        public ProcessPriorityClass PriorityClass
        {
            get
            {
                var priority = GetPriorityClass(_handle);
                return priority == 0 ? throw new Win32Exception() : (ProcessPriorityClass)priority;
            }
        }

        public void SetPriorityClass(ProcessPriorityClass priority)
        {
            if (!WindowsProcessPriorityTarget.SetPriorityClass(_handle, (uint)priority)) throw new Win32Exception();
        }

        public void Dispose()
        {
            _handle.Dispose();
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeProcessHandle OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit,
        int pid);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetProcessTimes(SafeProcessHandle process, out long created, out long exited,
        out long kernel, out long user);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsProcessCritical(SafeProcessHandle process,
        [MarshalAs(UnmanagedType.Bool)] out bool critical);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint GetPriorityClass(SafeProcessHandle process);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetPriorityClass(SafeProcessHandle process, uint priority);
}