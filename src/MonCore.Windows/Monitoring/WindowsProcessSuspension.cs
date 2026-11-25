using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace MonCore.Windows.Monitoring;

/// <summary>One bounded system snapshot used only while a process menu is open.</summary>
public static class WindowsProcessSuspension
{
    private const uint StatusInfoLengthMismatch = 0xC0000004;
    private const int SystemProcessInformation = 5;
    private const int ProcessHeaderSize = 0x100;
    private const int ThreadSize = 0x50;
    private const int MaximumBytes = 64 * 1024 * 1024;
    private const int NumberOfThreads = 0x04;
    private const int CreateTime = 0x20;
    private const int ProcessId = 0x50;
    private const int ThreadState = 0x44;
    private const int WaitReason = 0x48;
    private const int Waiting = 5;
    private const int Suspended = 5;
    private const int WrSuspended = 12;

    /// <returns>True when every thread is waiting because it is suspended; false when observed otherwise; null when unavailable.</returns>
    public static bool? IsSuspended(int processId, long creationTicks)
    {
        if (!Environment.Is64BitProcess) return null;
        var length = 64 * 1024;
        while (length <= MaximumBytes)
        {
            var bytes = GC.AllocateUninitializedArray<byte>(length);
            var status = NtQuerySystemInformation(SystemProcessInformation, bytes, length, out var needed);
            if (status == 0 && needed >= 0 && needed <= bytes.Length) return Parse(bytes.AsSpan(0, needed), processId, creationTicks);
            if (status != StatusInfoLengthMismatch || needed <= length || needed > MaximumBytes) return null;
            length = checked((int)Math.Min(MaximumBytes, Math.Max((long)length * 2, needed)));
        }
        return null;
    }

    internal static bool? Parse(ReadOnlySpan<byte> bytes, int processId, long creationTicks)
    {
        for (var offset = 0; offset + ProcessHeaderSize <= bytes.Length;)
        {
            var entry = bytes[offset..];
            var next = BinaryPrimitives.ReadUInt32LittleEndian(entry);
            if (next > entry.Length) return null;
            var recordLength = next == 0 ? entry.Length : (int)next;
            if (recordLength < ProcessHeaderSize || recordLength > entry.Length) return null;
            if (BinaryPrimitives.ReadInt64LittleEndian(entry[ProcessId..]) == processId &&
                BinaryPrimitives.ReadInt64LittleEndian(entry[CreateTime..]) == creationTicks)
            {
                var threads = BinaryPrimitives.ReadUInt32LittleEndian(entry[NumberOfThreads..]);
                if (threads == 0 || threads > 100_000 || ProcessHeaderSize + (ulong)threads * ThreadSize > (ulong)recordLength) return null;
                for (var index = 0; index < threads; index++)
                {
                    var thread = entry.Slice(ProcessHeaderSize + index * ThreadSize, ThreadSize);
                    if (BinaryPrimitives.ReadInt32LittleEndian(thread[ThreadState..]) != Waiting ||
                        BinaryPrimitives.ReadInt32LittleEndian(thread[WaitReason..]) is not (Suspended or WrSuspended)) return false;
                }
                return true;
            }
            if (next == 0) break;
            offset += recordLength;
        }
        return null;
    }

    [DllImport("ntdll.dll")] private static extern uint NtQuerySystemInformation(int informationClass, [Out] byte[] buffer, int length, out int returnLength);
}
