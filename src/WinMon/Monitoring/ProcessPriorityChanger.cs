using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace WinMon.Monitoring;

/// <summary>Reads or changes the priority of one confirmed process lifetime.</summary>
internal static class ProcessPriorityChanger
{
    internal interface ITarget : IDisposable
    {
        long CreationTicks { get; }
        bool HasExited { get; }
        bool IsCritical { get; }
        ProcessPriorityClass PriorityClass { get; }
        void SetPriorityClass(ProcessPriorityClass priority);
    }

    internal static readonly ProcessPriorityClass[] Choices =
        [ProcessPriorityClass.Idle, ProcessPriorityClass.BelowNormal,
            ProcessPriorityClass.Normal, ProcessPriorityClass.AboveNormal];

    internal static bool IsEligible(ProcessUsage process, bool live, bool grouped) =>
        ProcessTerminator.IsEligible(process, live, grouped) && process.MemberIds.Count == 0;

    internal static ProcessPriorityClass? Read(ProcessUsage process, Func<int, ITarget>? open = null)
    {
        if (!IsEligible(process, true, false)) return null;
        try
        {
            using var target = (open ?? Open)(process.ProcessId);
            return Valid(target, process) ? target.PriorityClass : null;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    internal static string? Set(ProcessUsage process, ProcessPriorityClass priority, Func<int, ITarget>? open = null)
    {
        if (!IsEligible(process, true, false) || !Choices.Contains(priority))
            return Strings.ProcessPriorityUnavailable;
        try
        {
            using var target = (open ?? Open)(process.ProcessId);
            if (target.CreationTicks != process.CreationTicks)
                return Strings.TheOriginalProcessHasExitedAndItsPIDHasBeen;
            if (target.HasExited) return Strings.ThisProcessHasAlreadyExited;
            if (target.IsCritical) return Strings.WindowsIdentifiesThisAsACriticalProcessWinMonWillNot;
            target.SetPriorityClass(priority);
            return null;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 5)
        {
            return Strings.ProcessPriorityAccessDenied;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 87)
        {
            return Strings.ThisProcessHasAlreadyExitedOrIsNoLongerAvailable;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or UnauthorizedAccessException)
        {
            return Strings.Format(Strings.ProcessPriorityFailed, ex.Message);
        }
    }

    private static bool Valid(ITarget target, ProcessUsage process) =>
        target.CreationTicks == process.CreationTicks && !target.HasExited && !target.IsCritical;

    private static ITarget Open(int pid) => new NativeTarget(pid);

    private sealed class NativeTarget : ITarget
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
            if (!ProcessPriorityChanger.SetPriorityClass(_handle, (uint)priority)) throw new Win32Exception();
        }

        public void Dispose() => _handle.Dispose();
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeProcessHandle OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, int pid);

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
