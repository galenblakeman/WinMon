using Microsoft.Win32.SafeHandles;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace WinMon.Monitoring;

/// <summary>Read query-only Windows CPU counters for one active processor group.</summary>
internal sealed class WindowsCpuSnapshotProvider : ICpuSnapshotProvider
{
    /// <inheritdoc/>
    public CpuSnapshot Capture()
    {
        if (NativeMethods.GetActiveProcessorGroupCount() > 1)
        {
            throw new PlatformNotSupportedException(
                "CPU sampling is unavailable on systems with more than one active processor group.");
        }

        if (!NativeMethods.GetSystemTimes(out var idleTime, out var kernelTime, out var userTime))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "GetSystemTimes failed.");
        }

        var available = new List<ProcessCpuTimes>();
        var unavailable = 0;

        var processes = Process.GetProcesses();
        var capturedCount = 0;
        foreach (var process in processes)
        {
            using (process)
            {
                try
                {
                    if (process.Id == 0) continue; // Idle is not a CPU consumer.
                    if (capturedCount++ >= 20_000) { unavailable++; continue; }
                    // CPU timing needs query-only access, not Process.Handle's broader requested rights.
                    using var handle = NativeMethods.OpenProcess(0x1000, false, process.Id);
                    if (handle.IsInvalid) { unavailable++; continue; }
                    if (!NativeMethods.GetProcessTimes(
                            handle,
                            out var creationTime,
                            out _,
                            out var processKernelTime,
                            out var processUserTime))
                    {
                        unavailable++;
                        continue;
                    }

                    var memorySize = (uint)Marshal.SizeOf<ProcessMemoryCounters>();
                    var memory = new ProcessMemoryCounters { Size = memorySize };
                    var hasMemory = NativeMethods.K32GetProcessMemoryInfo(handle, ref memory, memorySize);
                    var hasIo = NativeMethods.GetProcessIoCounters(handle, out var io);
                    available.Add(new ProcessCpuTimes(
                        process.Id,
                        NativeMethods.ToLong(creationTime),
                        process.ProcessName,
                        checked(NativeMethods.ToLong(processKernelTime) + NativeMethods.ToLong(processUserTime)),
                        hasMemory ? memory.WorkingSetSize.ToUInt64() : null,
                        hasIo ? io.ReadTransferCount : null,
                        hasIo ? io.WriteTransferCount : null));
                }
                catch (Exception exception) when (exception is InvalidOperationException or Win32Exception or UnauthorizedAccessException)
                {
                    unavailable++;
                }
            }
        }

        return new CpuSnapshot(
            DateTimeOffset.UtcNow,
            Stopwatch.GetTimestamp(),
            new SystemCpuTimes(
                NativeMethods.ToLong(idleTime),
                NativeMethods.ToLong(kernelTime),
                NativeMethods.ToLong(userTime)),
            available,
            unavailable);
    }

    private static class NativeMethods
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool K32GetProcessMemoryInfo(SafeProcessHandle process, ref ProcessMemoryCounters counters, uint size);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetProcessIoCounters(SafeProcessHandle process, out IoCounters counters);
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetSystemTimes(out FileTime idleTime, out FileTime kernelTime, out FileTime userTime);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetProcessTimes(
            SafeProcessHandle process,
            out FileTime creationTime,
            out FileTime exitTime,
            out FileTime kernelTime,
            out FileTime userTime);

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern SafeProcessHandle OpenProcess(uint desiredAccess, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, int processId);

        [DllImport("kernel32.dll")]
        internal static extern ushort GetActiveProcessorGroupCount();

        internal static long ToLong(FileTime fileTime) => ((long)fileTime.HighDateTime << 32) | fileTime.LowDateTime;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessMemoryCounters
    {
        public uint Size, PageFaultCount;
        public UIntPtr PeakWorkingSetSize, WorkingSetSize, QuotaPeakPagedPoolUsage, QuotaPagedPoolUsage,
            QuotaPeakNonPagedPoolUsage, QuotaNonPagedPoolUsage, PagefileUsage, PeakPagefileUsage;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters
    {
        public ulong ReadOperationCount, WriteOperationCount, OtherOperationCount,
            ReadTransferCount, WriteTransferCount, OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct FileTime
    {
        internal readonly uint LowDateTime;
        internal readonly int HighDateTime;
    }
}



