using System.Buffers.Binary;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace MonCore.Windows.Monitoring;

/// <summary>Throttled read-only storage queries for one configured volume.</summary>
internal sealed class WindowsDiskTemperature
{
    private static readonly long Interval = 15 * Stopwatch.Frequency;
    private static readonly long MaximumAge = 30 * Stopwatch.Frequency;
    private string? _volume;
    private Task<(double? Celsius, long ReadAt)>? _pending;
    private long _nextRead;
    private (double? Celsius, long ReadAt) _last;
    private bool _discardPending;

    internal double? Read(string? volume)
    {
        if (!string.Equals(volume, _volume, StringComparison.OrdinalIgnoreCase))
        {
            Reset();
            _volume = volume;
        }

        var now = Stopwatch.GetTimestamp();
        if (_pending is { IsCompleted: true } finished)
        {
            if (finished.IsCompletedSuccessfully && !_discardPending) _last = finished.Result;
            _ = finished.Exception;
            _pending = null;
            _discardPending = false;
        }

        if (_pending is null && now >= _nextRead &&
            volume is { Length: 2 } && char.IsAsciiLetter(volume[0]) && volume[1] == ':')
        {
            _nextRead = now + Interval;
            _pending = Task.Run(() => (Query(volume), Stopwatch.GetTimestamp()));
        }

        return now - _last.ReadAt <= MaximumAge ? _last.Celsius : null;
    }

    internal void Reset()
    {
        _last = default;
        _nextRead = 0;
        _discardPending = _pending is not null;
    }

    private static double? Query(string volume)
    {
        const uint getDiskExtents = 0x00560000;
        const uint queryStorageProperty = 0x002D1400;
        const int storageDeviceTemperatureProperty = 52;
        using var volumeHandle = Open(volume);
        // One DISK_EXTENT is sufficient. ERROR_MORE_DATA or multiple extents leaves the mapping unavailable.
        var extents = new byte[32];
        if (volumeHandle.IsInvalid ||
            !DeviceIoControl(volumeHandle, getDiskExtents, null, 0, extents, extents.Length, out var count, IntPtr.Zero) ||
            count < extents.Length || BinaryPrimitives.ReadUInt32LittleEndian(extents) != 1)
            return null;

        var diskNumber = BinaryPrimitives.ReadUInt32LittleEndian(extents.AsSpan(8));
        using var disk = Open(FormattableString.Invariant($"PhysicalDrive{diskNumber}"));
        if (disk.IsInvalid) return null;
        var request = new byte[12];
        BinaryPrimitives.WriteInt32LittleEndian(request, storageDeviceTemperatureProperty);
        var data = new byte[24 + 16 * 64];
        return DeviceIoControl(disk, queryStorageProperty, request, request.Length, data, data.Length,
            out count, IntPtr.Zero) && count <= data.Length ? CompositeTemperature(data.AsSpan(0, count)) : null;
    }

    internal static double? CompositeTemperature(ReadOnlySpan<byte> descriptor)
    {
        const int headerBytes = 24, entryBytes = 16;
        if (descriptor.Length < headerBytes) return null;
        var size = BinaryPrimitives.ReadUInt32LittleEndian(descriptor[4..]);
        var count = BinaryPrimitives.ReadUInt16LittleEndian(descriptor[12..]);
        if (size > descriptor.Length || size < headerBytes + count * entryBytes) return null;
        for (var i = 0; i < count; i++)
        {
            var entry = descriptor[(headerBytes + i * entryBytes)..];
            if (BinaryPrimitives.ReadUInt16LittleEndian(entry) != 0) continue;
            var celsius = BinaryPrimitives.ReadInt16LittleEndian(entry[2..]);
            return celsius is >= -40 and <= 150 ? celsius : null;
        }
        return null;
    }

    private static SafeFileHandle Open(string device) =>
        CreateFileW(@"\\.\" + device, 0, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern SafeFileHandle CreateFileW(string path, uint access, uint share,
        IntPtr security, uint disposition, uint flags, IntPtr template);

    [DllImport("kernel32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(SafeFileHandle device, uint code, byte[]? input,
        int inputLength, [Out] byte[] output, int outputLength, out int bytesReturned, IntPtr overlapped);
}
