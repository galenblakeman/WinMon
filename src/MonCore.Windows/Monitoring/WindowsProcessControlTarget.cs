using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace WinMon.Monitoring;

/// <summary>One process handle used to validate and apply a single control operation.</summary>
public interface IProcessControlTarget : IDisposable
{
    long CreationTicks { get; }
    bool HasExited { get; }
    bool IsCritical { get; }
    void Suspend();
    void Resume();
    ulong AffinityMask { get; }
    ulong SystemAffinityMask { get; }
    ushort ProcessGroupCount { get; }
    void SetAffinityMask(ulong mask);
    ProcessPriorityClass PriorityClass { get; }
    void SetPriorityClass(ProcessPriorityClass value);
    ProcessPowerThrottlingState PowerThrottling { get; }
    void SetPowerThrottling(ProcessPowerThrottlingState value);
}

public readonly record struct ProcessPowerThrottlingState(uint ControlMask, uint StateMask)
{
    public const uint ExecutionSpeed = 1;
}

public static class WindowsProcessControlTarget
{
    public static IProcessControlTarget Open(int processId, ProcessControlAccess access) => new Native(processId, access);

    [Flags]
    public enum ProcessControlAccess : uint
    {
        Inspect = 0x1000,
        Suspend = 0x0800 | 0x1000,
        Affinity = 0x0200 | 0x1000,
        Efficiency = 0x0200 | 0x1000
    }

    private sealed class Native : IProcessControlTarget
    {
        private readonly SafeProcessHandle _handle;
        internal Native(int pid, ProcessControlAccess access)
        {
            if (!Environment.Is64BitProcess) throw new PlatformNotSupportedException("Process controls require a 64-bit process.");
            _handle = OpenProcess((uint)access, false, pid);
            if (!_handle.IsInvalid) return;
            var error = Marshal.GetLastWin32Error();
            _handle.Dispose();
            throw new Win32Exception(error);
        }
        public long CreationTicks => Times(out var created, out _) ? created : throw new Win32Exception();
        public bool HasExited => Times(out _, out var exited) ? exited != 0 : throw new Win32Exception();
        public bool IsCritical => !IsProcessCritical(_handle, out var value) ? throw new Win32Exception() : value;
        public void Suspend() => Check(NtSuspendProcess(_handle));
        public void Resume() => Check(NtResumeProcess(_handle));
        public ulong AffinityMask { get { ReadAffinity(out var value, out _); return value; } }
        public ulong SystemAffinityMask { get { ReadAffinity(out _, out var value); return value; } }
        public ushort ProcessGroupCount
        {
            get
            {
                ushort count = 0;
                _ = GetProcessGroupAffinity(_handle, ref count, null);
                if (count == 0) throw new Win32Exception();
                var groups = new ushort[count];
                return GetProcessGroupAffinity(_handle, ref count, groups) ? count : throw new Win32Exception();
            }
        }
        public void SetAffinityMask(ulong mask) { if (!SetProcessAffinityMask(_handle, (nuint)mask)) throw new Win32Exception(); }
        public ProcessPriorityClass PriorityClass { get { var value = GetPriorityClass(_handle); return value == 0 ? throw new Win32Exception() : (ProcessPriorityClass)value; } }
        public void SetPriorityClass(ProcessPriorityClass value) { if (!WindowsProcessControlTarget.SetPriorityClass(_handle, (uint)value)) throw new Win32Exception(); }
        public ProcessPowerThrottlingState PowerThrottling
        {
            get { var value = new PowerState { Version = 1 }; if (!GetProcessInformation(_handle, 4, ref value, (uint)Marshal.SizeOf<PowerState>())) throw new Win32Exception(); return new(value.ControlMask, value.StateMask); }
        }
        public void SetPowerThrottling(ProcessPowerThrottlingState value)
        {
            var state = new PowerState { Version = 1, ControlMask = value.ControlMask, StateMask = value.StateMask };
            if (!SetProcessInformation(_handle, 4, ref state, (uint)Marshal.SizeOf<PowerState>())) throw new Win32Exception();
        }
        private bool Times(out long created, out long exited) => GetProcessTimes(_handle, out created, out exited, out _, out _);
        private void ReadAffinity(out ulong process, out ulong system)
        {
            if (!GetProcessAffinityMask(_handle, out var p, out var s)) throw new Win32Exception();
            process = p; system = s;
        }
        private static void Check(uint status) { if (status != 0) throw new Win32Exception(unchecked((int)RtlNtStatusToDosError(status))); }
        public void Dispose() => _handle.Dispose();
    }
    [StructLayout(LayoutKind.Sequential)] private struct PowerState { public uint Version, ControlMask, StateMask; }
    [DllImport("kernel32.dll", SetLastError = true)] private static extern SafeProcessHandle OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetProcessTimes(SafeProcessHandle h, out long c, out long e, out long k, out long u);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool IsProcessCritical(SafeProcessHandle h, out bool critical);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetProcessAffinityMask(SafeProcessHandle h, out ulong process, out ulong system);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetProcessGroupAffinity(SafeProcessHandle h, ref ushort count, ushort[]? groups);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool SetProcessAffinityMask(SafeProcessHandle h, nuint mask);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern uint GetPriorityClass(SafeProcessHandle h);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool SetPriorityClass(SafeProcessHandle h, uint value);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetProcessInformation(SafeProcessHandle h, int infoClass, ref PowerState value, uint size);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool SetProcessInformation(SafeProcessHandle h, int infoClass, ref PowerState value, uint size);
    [DllImport("ntdll.dll")] private static extern uint NtSuspendProcess(SafeProcessHandle h);
    [DllImport("ntdll.dll")] private static extern uint NtResumeProcess(SafeProcessHandle h);
    [DllImport("ntdll.dll")] private static extern uint RtlNtStatusToDosError(uint status);
}
