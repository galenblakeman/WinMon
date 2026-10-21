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
        live && !grouped && process is { MemberCount: 1, ProcessId: > 4 } &&
        process.ProcessId != Environment.ProcessId && process.CreationTicks > 0;

    /// <summary>Capture exact live lifetimes before confirmation, including idle group members.</summary>
    internal static ProcessUsage[] CaptureTargets(ProcessUsage row, bool live, bool grouped,
        IReadOnlyList<ProcessUsage> current)
    {
        if (!live) return [];
        if (!grouped) return IsEligible(row, true, false) ? [row] : [];
        var key = ProcessRanking.GroupKey(row);
        var targets = current.Where(p => string.Equals(ProcessRanking.GroupKey(p), key,
                StringComparison.OrdinalIgnoreCase))
            .DistinctBy(p => (p.ProcessId, p.CreationTicks)).OrderBy(p => p.ProcessId).ToArray();
        // Never silently omit a protected or unidentified member from an all-process request.
        return targets.All(p => IsEligible(p, true, false)) ? targets : [];
    }

    internal static IReadOnlyList<string> EndAll(IReadOnlyList<ProcessUsage> targets, Func<int, ITarget>? open = null)
    {
        var errors = new List<string>();
        foreach (var target in targets)
        {
            var error = End(target, open);
            if (error is not null) errors.Add($"{target.Name} (PID {target.ProcessId}): {error}");
        }
        return errors;
    }

    // Return null only when Windows accepted the termination request. It can complete asynchronously.
    internal static string? End(ProcessUsage process, Func<int, ITarget>? open = null)
    {
        if (!IsEligible(process, true, false)) return Strings.ThisProcessCannotBeEndedFromWinMon;
        try
        {
            using var target = (open ?? Open)(process.ProcessId);
            if (target.CreationTicks != process.CreationTicks)
                return Strings.TheOriginalProcessHasExitedAndItsPIDHasBeen;
            if (target.HasExited) return Strings.ThisProcessHasAlreadyExited;
            if (target.IsCritical) return Strings.WindowsIdentifiesThisAsACriticalProcessWinMonWillNot;
            target.Terminate();
            return null;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 5)
        { return Strings.AccessDeniedWindowsDidNotAllowWinMonToEndThis; }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 87)
        { return Strings.ThisProcessHasAlreadyExitedOrIsNoLongerAvailable; }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or UnauthorizedAccessException)
        { return Strings.Format(Strings.TheProcessCouldNotBeEnded, ex.Message); }
    }

    private static ITarget Open(int pid) => new NativeTarget(pid);

    private sealed class NativeTarget : ITarget
    {
        private readonly SafeProcessHandle _handle;
        internal NativeTarget(int pid)
        {
            _handle = OpenProcess(0x1001, false, pid); // QUERY_LIMITED_INFORMATION | TERMINATE
            if (!_handle.IsInvalid) return;
            var error = Marshal.GetLastWin32Error();
            _handle.Dispose();
            throw new Win32Exception(error);
        }
        public long CreationTicks => !GetProcessTimes(_handle, out var created, out _, out _, out _) ? throw new Win32Exception() : created;

        public bool HasExited
        {
            get
            {
                if (!GetProcessTimes(_handle, out _, out var exited, out _, out _)) throw new Win32Exception();
                return exited != 0;
            }
        }
        public bool IsCritical => !IsProcessCritical(_handle, out var critical) ? throw new Win32Exception() : critical;

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
