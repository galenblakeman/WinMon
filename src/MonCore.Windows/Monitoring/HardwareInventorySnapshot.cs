namespace MonCore.Windows.Monitoring;

public interface IHardwareSection { string Status { get; } bool Truncated { get; } }
public sealed record HardwareSection<T>(string Status, bool Truncated, string[] Issues, T? Data) : IHardwareSection;
public sealed record HardwareInventorySnapshot(int SchemaVersion, DateTimeOffset CollectionStartedUtc, DateTimeOffset CollectionCompletedUtc,
    string Status, bool IdentifiersIncluded, bool Truncated, HardwareSection<HardwareCpu> Cpu,
    HardwareSection<HardwareMotherboards> Motherboard, HardwareSection<HardwareMemory> Memory,
    HardwareSection<HardwareGraphics> Graphics, HardwareSection<HardwareDisks> Disks,
    HardwareSection<HardwareVolumes> Volumes, HardwareSection<HardwareNetwork> Network);
public sealed record HardwareCpu(string[] Names, int? PhysicalCores, uint? LogicalProcessors, HardwareProcessor[] Processors);
public sealed record HardwareProcessor(string? Name, string? Manufacturer, string? SocketDesignation, string? DeviceId, ulong? Architecture,
    ulong? Cores, ulong? LogicalProcessors, ulong? MaximumClockMHz, ulong? L2CacheBytes, ulong? L3CacheBytes,
    bool? VirtualizationExtensions, bool? VirtualizationFirmwareEnabled)
{
    public string? ArchitectureName => Architecture switch
    {
        0 => "x86", 1 => "MIPS", 2 => "Alpha", 3 => "PowerPC", 5 => "ARM", 6 => "Itanium", 9 => "x64", 12 => "ARM64", _ => null
    };
}
public sealed record HardwareMotherboards(HardwareMotherboard[] Items);
public sealed record HardwareMotherboard(string? Manufacturer, string? Product, string? Version, string? SerialNumber);
public sealed record HardwareMemory(ulong? InstalledBytes, ulong? UsableBytes, int? ModuleCount, long? SlotCount, HardwareMemoryModule[] Modules);
public sealed record HardwareMemoryModule(string? DeviceLocator, string? BankLabel, ulong? CapacityBytes, ulong? MemoryType,
    ulong? ConfiguredClockMHz, ulong? ReportedClockMHz, string? Manufacturer, string? PartNumber)
{
    public string? MemoryTypeName => MemoryType switch
    {
        18 => "DDR", 19 => "DDR2", 20 => "DDR2FBDIMM", 24 => "DDR3", 26 => "DDR4", 27 => "LPDDR", 28 => "LPDDR2",
        29 => "LPDDR3", 30 => "LPDDR4", 34 => "DDR5", 35 => "LPDDR5", _ => null
    };
}
public sealed record HardwareGraphics(HardwareGraphicsAdapter[] Adapters);
public sealed record HardwareGraphicsAdapter(string? Name, ulong DedicatedBytes, ulong SharedSystemMemoryLimitBytes);
public sealed record HardwareDisks(IReadOnlyList<HardwareDisk> Items);
public sealed record HardwareDisk(ulong? Number, string? Model, string? Manufacturer, ulong? CapacityBytes, ulong? MediaType,
    ulong? BusType, ulong? HealthStatus, string[]? DriveLetters)
{
    public string? MediaTypeName => MediaType switch { 3 => "HDD", 4 => "SSD", 5 => "StorageClassMemory", _ => null };
    public string? HealthStatusName => HealthStatus switch { 0 => "healthy", 1 => "warning", 2 => "unhealthy", _ => null };
    public string? BusTypeName => BusType switch
    {
        1 => "SCSI", 2 => "ATAPI", 3 => "ATA", 4 => "IEEE1394", 5 => "SSA", 6 => "FibreChannel", 7 => "USB", 8 => "RAID",
        9 => "iSCSI", 10 => "SAS", 11 => "SATA", 12 => "SD", 13 => "MMC", 14 => "Virtual", 15 => "FileBackedVirtual",
        16 => "StorageSpaces", 17 => "NVMe", _ => null
    };
}
public sealed record HardwareVolumes(HardwareVolume[] Items);
public sealed record HardwareVolume(string Name, string? Label, string? FileSystem, long? TotalBytes, long? AvailableBytes, bool Ready, bool Error);
public sealed record HardwareNetwork(HardwareNetworkAdapter[] Adapters);
public sealed record HardwareNetworkAdapter(string? Name, string? Description, ulong? OperationalStatus, ulong? AdminStatus,
    ulong? MediaConnectState, ulong? State, bool? Virtual, ulong? ReceiveLinkSpeedBitsPerSecond);
