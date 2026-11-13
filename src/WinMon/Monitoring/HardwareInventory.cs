namespace WinMon.Monitoring;

/// <summary>Localized names for Windows inventory provider codes.</summary>
internal static class HardwareInventory
{
    internal static string MemoryType(ulong? type) => type switch
    {
        18 => Strings.DDR, 19 => Strings.DDR2, 20 => Strings.DDR2FBDIMM, 24 => Strings.DDR3, 26 => Strings.DDR4,
        27 => Strings.LPDDR, 28 => Strings.LPDDR2, 29 => Strings.LPDDR3, 30 => Strings.LPDDR4, 34 => Strings.DDR5,
        35 => Strings.LPDDR5, _ => Strings.Unavailable
    };

    internal static string MediaType(ulong? type) => type switch
    {
        3 => Strings.HDD, 4 => Strings.SSD, 5 => Strings.StorageClassMemory, _ => Strings.Unavailable
    };

    internal static string Health(ulong? status) => status switch
    {
        0 => Strings.Healthy, 1 => Strings.Warning, 2 => Strings.Unhealthy, _ => Strings.Unavailable
    };

    internal static string BusType(ulong? type) => type switch
    {
        1 => Strings.SCSI, 2 => Strings.ATAPI, 3 => Strings.ATA, 4 => Strings.IEEE1394, 5 => Strings.SSA,
        6 => Strings.FibreChannel, 7 => Strings.USB, 8 => Strings.RAID, 9 => Strings.ISCSI, 10 => Strings.SAS,
        11 => Strings.SATA, 12 => Strings.SD, 13 => Strings.MMC, 14 => Strings.VirtualBus, 15 => Strings.FileBackedVirtual,
        16 => Strings.StorageSpaces, 17 => Strings.NVMe, _ => Strings.Unavailable
    };
}
