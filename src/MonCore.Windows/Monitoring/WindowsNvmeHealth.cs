using System.Buffers.Binary;
using System.Diagnostics;

namespace MonCore.Windows.Monitoring;

/// <summary>Read two counters from the NVMe health log for fixed, single-disk volumes.</summary>
internal static class WindowsNvmeHealth
{
    private const int ProtocolHeaderBytes = 40;
    private const int DescriptorBytes = 8 + ProtocolHeaderBytes;
    private const int HealthLogBytes = 512;
    private const uint ProtocolTypeNvme = 3;
    private const uint LogPage = 2;

    internal static IReadOnlyList<HardwareSensorReading> ReadFixedVolumes(CancellationToken token)
    {
        var disks = new Dictionary<uint, (byte Endurance, byte? Spare, long ReadAt)?>();
        var readings = new List<HardwareSensorReading>();
        foreach (var drive in DriveInfo.GetDrives())
        {
            token.ThrowIfCancellationRequested();
            if (drive.DriveType != DriveType.Fixed || drive.Name is not { Length: 3 } root ||
                WindowsDiskTemperature.DiskNumber(root[..2]) is not { } number)
                continue;

            if (!disks.TryGetValue(number, out var health))
                disks[number] = health = Read(number);
            if (health is not { } data) continue;
            var volume = root[..2];
            readings.Add(new(HardwareSensorKind.NvmeEnduranceUsed, volume, null, data.Endurance, data.ReadAt));
            if (data.Spare is { } spare)
                readings.Add(new(HardwareSensorKind.NvmeAvailableSpare, volume, null, spare, data.ReadAt));
        }
        return readings;
    }

    private static (byte Endurance, byte? Spare, long ReadAt)? Read(uint diskNumber)
    {
        const uint genericRead = 0x80000000;
        const uint storageDeviceProtocolSpecificProperty = 50;
        const uint nvmeHealthLogPage = 0x02;
        using var disk = WindowsDiskTemperature.Open(FormattableString.Invariant($"PhysicalDrive{diskNumber}"), genericRead);
        if (disk.IsInvalid) return null;

        var request = new byte[DescriptorBytes + HealthLogBytes];
        BinaryPrimitives.WriteUInt32LittleEndian(request, storageDeviceProtocolSpecificProperty);
        BinaryPrimitives.WriteUInt32LittleEndian(request.AsSpan(8), ProtocolTypeNvme);
        BinaryPrimitives.WriteUInt32LittleEndian(request.AsSpan(12), LogPage);
        BinaryPrimitives.WriteUInt32LittleEndian(request.AsSpan(16), nvmeHealthLogPage);
        BinaryPrimitives.WriteUInt32LittleEndian(request.AsSpan(24), ProtocolHeaderBytes);
        BinaryPrimitives.WriteUInt32LittleEndian(request.AsSpan(28), HealthLogBytes);
        var data = new byte[request.Length];
        if (!WindowsDiskTemperature.QueryStorageProperty(disk, request, data, out var count) ||
            count < DescriptorBytes || count > data.Length ||
            Parse(data.AsSpan(0, count)) is not { } health)
            return null;

        return (health.Endurance, health.Spare, Stopwatch.GetTimestamp());
    }

    internal static (byte Endurance, byte? Spare)? Parse(ReadOnlySpan<byte> descriptor)
    {
        if (descriptor.Length < DescriptorBytes + HealthLogBytes ||
            BinaryPrimitives.ReadUInt32LittleEndian(descriptor) != DescriptorBytes ||
            BinaryPrimitives.ReadUInt32LittleEndian(descriptor[4..]) != DescriptorBytes ||
            BinaryPrimitives.ReadUInt32LittleEndian(descriptor[8..]) != ProtocolTypeNvme ||
            BinaryPrimitives.ReadUInt32LittleEndian(descriptor[12..]) != LogPage)
            return null;

        var offset = BinaryPrimitives.ReadUInt32LittleEndian(descriptor[24..]);
        var size = BinaryPrimitives.ReadUInt32LittleEndian(descriptor[28..]);
        if (offset < ProtocolHeaderBytes || offset > descriptor.Length - 8 ||
            size < HealthLogBytes || size > descriptor.Length - 8 - offset)
            return null;

        var log = descriptor.Slice(8 + (int)offset, HealthLogBytes);
        if (log.IndexOfAnyExcept((byte)0) < 0) return null;
        return (log[5], log[3] <= 100 ? log[3] : null);
    }
}
