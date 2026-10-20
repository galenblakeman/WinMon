using System.Buffers.Binary;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace WinMon.Monitoring;

/// <summary>Read Windows CPU counters for one active processor group, and every process in one system query.</summary>
/// <remarks>
/// One <c>NtQuerySystemInformation(SystemProcessInformation)</c> call returns CPU times, creation time, working set
/// and I/O transfer counts for every process without opening process handles, so access-restricted processes are
/// measured too. The buffer is pinned and reused, and a process name string is reused for the life of the process.
/// </remarks>
internal sealed class WindowsCpuSnapshotProvider : ICpuSnapshotProvider
{
    private const int SystemProcessInformation = 5;
    private const uint StatusInfoLengthMismatch = 0xC0000004;
    private const int MaximumProcesses = 20_000;
    private const int MaximumBufferBytes = 64 * 1024 * 1024;

    // SYSTEM_PROCESS_INFORMATION field offsets for 64-bit Windows.
    internal const int EntrySize = 0x100;
    private const int NextEntryOffset = 0x00;
    private const int CreateTime = 0x20;
    private const int UserTime = 0x28;
    private const int KernelTime = 0x30;
    private const int ImageNameLength = 0x38;
    private const int ImageNameBuffer = 0x40;
    private const int UniqueProcessId = 0x50;
    private const int WorkingSetSize = 0x90;
    private const int ReadTransferCount = 0xE8;
    private const int WriteTransferCount = 0xF0;

    private byte[] _buffer = GC.AllocateUninitializedArray<byte>(1024 * 1024, pinned: true);
    private Dictionary<(int, long), string> _names = [];
    private Dictionary<(int, long), string> _nextNames = [];

    /// <inheritdoc/>
    public CpuSnapshot Capture()
    {
        if (!Environment.Is64BitProcess)
            throw new PlatformNotSupportedException("Process sampling requires a 64-bit process.");
        if (NativeMethods.GetActiveProcessorGroupCount() > 1)
        {
            throw new PlatformNotSupportedException(
                "CPU sampling is unavailable on systems with more than one active processor group.");
        }

        if (!NativeMethods.GetSystemTimes(out var idleTime, out var kernelTime, out var userTime))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "GetSystemTimes failed.");
        }

        var length = Query();
        var available = Parse(_buffer.AsSpan(0, length), Marshal.UnsafeAddrOfPinnedArrayElement(_buffer, 0),
            _names, _nextNames, out var unavailable);
        (_names, _nextNames) = (_nextNames, _names);

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

    /// <summary>Walk a SystemProcessInformation buffer, skipping the Idle process.</summary>
    /// <param name="buffer">Bytes the query returned.</param>
    /// <param name="baseAddress">Address of the buffer while the query ran, to resolve image-name pointers.</param>
    /// <param name="previousNames">Names from the last capture, reused for unchanged process lifetimes.</param>
    /// <param name="names">Cleared and filled with this capture's names.</param>
    /// <param name="unavailable">Processes beyond the collection cap.</param>
    internal static List<ProcessCpuTimes> Parse(ReadOnlySpan<byte> buffer, nint baseAddress,
        Dictionary<(int, long), string> previousNames, Dictionary<(int, long), string> names, out int unavailable)
    {
        names.Clear();
        var processes = new List<ProcessCpuTimes>(previousNames.Count + 16);
        unavailable = 0;
        for (var offset = 0; offset + EntrySize <= buffer.Length;)
        {
            var entry = buffer.Slice(offset, EntrySize);
            var processId = (int)BinaryPrimitives.ReadInt64LittleEndian(entry[UniqueProcessId..]);
            if (processId != 0)
            {
                if (processes.Count >= MaximumProcesses) unavailable++;
                else
                {
                    var created = BinaryPrimitives.ReadInt64LittleEndian(entry[CreateTime..]);
                    var key = (processId, created);
                    if (!previousNames.TryGetValue(key, out var name))
                        name = ReadName(buffer, baseAddress, entry);
                    names[key] = name;
                    processes.Add(new ProcessCpuTimes(
                        processId,
                        created,
                        name,
                        checked(BinaryPrimitives.ReadInt64LittleEndian(entry[KernelTime..]) +
                                BinaryPrimitives.ReadInt64LittleEndian(entry[UserTime..])),
                        BinaryPrimitives.ReadUInt64LittleEndian(entry[WorkingSetSize..]),
                        BinaryPrimitives.ReadUInt64LittleEndian(entry[ReadTransferCount..]),
                        BinaryPrimitives.ReadUInt64LittleEndian(entry[WriteTransferCount..])));
                }
            }
            var next = BinaryPrimitives.ReadUInt32LittleEndian(entry[NextEntryOffset..]);
            if (next == 0) break;
            offset += checked((int)next);
        }
        return processes;
    }

    // Match Process.ProcessName: the image file name without a trailing ".exe".
    private static string ReadName(ReadOnlySpan<byte> buffer, nint baseAddress, ReadOnlySpan<byte> entry)
    {
        var bytes = BinaryPrimitives.ReadUInt16LittleEndian(entry[ImageNameLength..]);
        var offset = BinaryPrimitives.ReadInt64LittleEndian(entry[ImageNameBuffer..]) - baseAddress;
        if (bytes == 0 || offset < 0 || offset + bytes > buffer.Length) return "";
        var chars = MemoryMarshal.Cast<byte, char>(buffer.Slice((int)offset, bytes));
        if (chars.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) chars = chars[..^4];
        return new string(chars);
    }

    private int Query()
    {
        while (true)
        {
            var status = NativeMethods.NtQuerySystemInformation(SystemProcessInformation,
                Marshal.UnsafeAddrOfPinnedArrayElement(_buffer, 0), _buffer.Length, out var needed);
            if (status == 0) return Math.Min(needed, _buffer.Length);
            if (status != StatusInfoLengthMismatch || _buffer.Length >= MaximumBufferBytes)
                throw new Win32Exception($"NtQuerySystemInformation failed with status 0x{status:X8}.");
            // Processes and threads can appear between calls, so leave headroom.
            var size = Math.Min(MaximumBufferBytes, Math.Max(_buffer.Length * 2, needed + 256 * 1024));
            _buffer = GC.AllocateUninitializedArray<byte>(size, pinned: true);
        }
    }

    private static class NativeMethods
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetSystemTimes(out FileTime idleTime, out FileTime kernelTime, out FileTime userTime);

        [DllImport("kernel32.dll")]
        internal static extern ushort GetActiveProcessorGroupCount();

        [DllImport("ntdll.dll")]
        internal static extern uint NtQuerySystemInformation(int informationClass, nint information, int length, out int returnLength);

        internal static long ToLong(FileTime fileTime) => ((long)fileTime.HighDateTime << 32) | fileTime.LowDateTime;
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct FileTime
    {
        internal readonly uint LowDateTime;
        internal readonly int HighDateTime;
    }
}
