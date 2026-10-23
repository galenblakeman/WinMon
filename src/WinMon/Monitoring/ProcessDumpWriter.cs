using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace WinMon.Monitoring;

internal static class ProcessDumpWriter
{
    // DbgHelp is single threaded, including calls made for different targets.
    private static readonly Lock Gate = new();

    internal static bool IsEligible(ProcessUsage p, bool live, bool grouped)
    {
        return live &&
               p.MemberCount == 1 && p.MemberIds.Count == (grouped ? 1 : 0) &&
               (!grouped || p.MemberIds[0] == p.ProcessId) && p.ProcessId > 4 &&
               p.ProcessId != Environment.ProcessId && p.CreationTicks > 0;
    }

    internal static void Write(ProcessUsage target, string destination, bool fullMemory)
    {
        if (!IsEligible(target, true, target.MemberIds.Count > 0))
            throw new InvalidOperationException(Strings.ProcessDumpUnavailable);
        lock (Gate)
        {
            using var process = OpenProcess(0x0410, false, target.ProcessId); // QUERY_INFORMATION | VM_READ
            if (process.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
            if (!GetProcessTimes(process, out var created, out var exited, out _, out _))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            if (created != target.CreationTicks)
                throw new InvalidOperationException(Strings.TheOriginalProcessHasExitedAndItsPIDHasBeen);
            if (exited != 0) throw new InvalidOperationException(Strings.ThisProcessHasAlreadyExited);
            var path = Path.GetFullPath(destination);
            var temporary = Path.Combine(Path.GetDirectoryName(path)!, $".winmon-{Guid.NewGuid():N}.tmp");
            try
            {
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
                {
                    // MiniDumpNormal or MiniDumpWithFullMemory, with thread information.
                    if (!MiniDumpWriteDump(process, target.ProcessId, stream.SafeFileHandle,
                            0x1000u | (fullMemory ? 0x2u : 0u), IntPtr.Zero, IntPtr.Zero, IntPtr.Zero))
                        throw new Win32Exception(Marshal.GetLastWin32Error());
                    stream.Flush(true);
                }

                File.Move(temporary, path, true);
            }
            finally
            {
                try
                {
                    File.Delete(temporary);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    Trace.TraceWarning("Could not remove incomplete dump: {0}", ex.Message);
                }
            }
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeProcessHandle OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit,
        int pid);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetProcessTimes(SafeProcessHandle process, out long created, out long exited,
        out long kernel, out long user);

    [DllImport("dbghelp.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool MiniDumpWriteDump(SafeProcessHandle process, int pid, SafeFileHandle file, uint type,
        IntPtr exception, IntPtr streams, IntPtr callback);
}