using System.Diagnostics;
using System.Globalization;
using System.Management;
using System.Runtime.InteropServices;
using System.Security;
using WinMon.Controls;
using EnumerationOptions = System.Management.EnumerationOptions;

namespace WinMon.Monitoring;

/// <summary>Read-only WMI inventory, separate from the one-second sampler.</summary>
internal interface IHardwareQuery
{
    // Null means the provider failed; an empty collection means it returned no devices.
    IReadOnlyList<HardwareRow>? Read(string scope, string className, params string[] properties);
}

internal sealed class HardwareRow : Dictionary<string, object?>
{
    internal HardwareRow() : base(StringComparer.OrdinalIgnoreCase)
    {
    }

    internal string Text(string key)
    {
        return TryGetValue(key, out var value)
            ? Convert.ToString(value, CultureInfo.InvariantCulture)?.Trim() ?? ""
            : "";
    }

    internal ulong? Number(string key)
    {
        return ulong.TryParse(Text(key), NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)
            ? number
            : null;
    }

    internal string Label(string key)
    {
        var text = Text(key);
        return text.Length == 0 || text.Equals("None", StringComparison.OrdinalIgnoreCase) ||
               text.Equals("Unknown", StringComparison.OrdinalIgnoreCase) ||
               text.Equals("To be filled by O.E.M.", StringComparison.OrdinalIgnoreCase)
            ? Strings.Unavailable
            : text;
    }
}

internal sealed class WmiHardwareQuery : IHardwareQuery
{
    public IReadOnlyList<HardwareRow>? Read(string scope, string className, params string[] properties)
    {
        try
        {
            var options = new EnumerationOptions
            {
                ReturnImmediately = true, Rewindable = false,
                Timeout = TimeSpan.FromSeconds(2), BlockSize = 1
            };
            using var searcher = new ManagementObjectSearcher(new ManagementScope(scope),
                new ObjectQuery($"SELECT {string.Join(",", properties)} FROM {className}"), options);
            using var results = searcher.Get();
            var rows = new List<HardwareRow>();
            var elapsed = Stopwatch.StartNew();
            foreach (var item in results)
                using (item)
                {
                    if (rows.Count >= 256 || elapsed.Elapsed > TimeSpan.FromSeconds(4))
                        return null; // Do not report a partial inventory as complete.
                    var row = new HardwareRow();
                    foreach (var property in properties) row[property] = item[property];
                    rows.Add(row);
                }

            return rows;
        }
        catch (Exception ex) when (ex is ManagementException or COMException or UnauthorizedAccessException or
                                       SecurityException or TimeoutException)
        {
            Trace.TraceInformation("Hardware inventory unavailable ({0}): {1}", className, ex.Message);
            return null;
        }
    }
}

internal static class HardwareInventory
{
    private const string Cim = @"root\cimv2";
    private const string Storage = @"root\Microsoft\Windows\Storage";

    internal static WindowsHardwareInfo.Section Motherboard(IHardwareQuery query)
    {
        var boards = query.Read(Cim, "Win32_BaseBoard", "Manufacturer", "Product", "Version", "SerialNumber");
        if (boards is null || boards.Count == 0)
            return new WindowsHardwareInfo.Section(Strings.Motherboard,
                Strings.MotherboardInformationUnavailableFromWindows);

        return new WindowsHardwareInfo.Section(Strings.Motherboard, string.Join("\n\n", boards.Select(board =>
            Strings.Format(Strings.ManufacturerModel, board.Label("Manufacturer"), board.Label("Product")) +
            Strings.Format(Strings.BoardVersionSerialNumber, board.Label("Version"), board.Label("SerialNumber")))));
    }

    internal static WindowsHardwareInfo.Section Cpu(IHardwareQuery query, string basicSummary)
    {
        var processors = query.Read(Cim, "Win32_Processor", "DeviceID", "Name", "SocketDesignation", "Manufacturer",
            "Architecture", "NumberOfCores", "NumberOfLogicalProcessors", "MaxClockSpeed", "L2CacheSize", "L3CacheSize",
            "VMMonitorModeExtensions", "VirtualizationFirmwareEnabled");
        if (processors is null || processors.Count == 0)
            return new WindowsHardwareInfo.Section(Strings.CPU,
                basicSummary + Strings.AdditionalCPUSpecificationsUnavailableFromWindows);

        var specificationNote = Strings.MaximumClockIsAReportedSpecificationNotLiveFrequencyOr;
        if (processors.Count == 1)
        {
            var processor = processors[0];
            var socket = processor.Label("SocketDesignation");
            return new WindowsHardwareInfo.Section(Strings.CPU, basicSummary +
                                                                Strings.Format(Strings.Value1PopulatedSocket,
                                                                    socket == Strings.Unavailable
                                                                        ? ""
                                                                        : $" · {socket}") +
                                                                CpuSpecifications(processor) + "\n\n" +
                                                                specificationNote);
        }

        var details = processors.Select((p, i) => new WindowsHardwareInfo.Detail(
            p.Label("SocketDesignation") is var socket && socket != Strings.Unavailable ? socket :
            p.Label("DeviceID") is var id && id != Strings.Unavailable ? id : Strings.Format(Strings.Processor, i + 1),
            Strings.Format(Strings.Model, p.Label("Name")) +
            Strings.Format(Strings.PhysicalCoresInThisSocketLogicalProcessorsInThisSocket,
                Positive(p.Number("NumberOfCores")), Positive(p.Number("NumberOfLogicalProcessors"))) +
            CpuSpecifications(p))).ToArray();
        var coreCounts = string.Join(", ", processors.Select(p => Positive(p.Number("NumberOfCores"))));
        var clocks = string.Join(", ", processors.Select(p => MaximumClock(p.Number("MaxClockSpeed"))).Distinct());
        return new WindowsHardwareInfo.Section(Strings.CPU, basicSummary +
                                                            Strings.Format(
                                                                Strings.PopulatedCoresPerSocketReportedMaximum,
                                                                processors.Count,
                                                                processors.Count == 1
                                                                    ? Strings.Socket
                                                                    : Strings.Sockets, coreCounts, clocks) +
                                                            "\n" + specificationNote, details, Strings.CPUDetails);
    }

    private static string CpuSpecifications(HardwareRow processor)
    {
        return Strings.Format(Strings.ManufacturerArchitecture, processor.Label("Manufacturer"),
                   Architecture(processor.Number("Architecture"))) +
               Strings.Format(Strings.ReportedMaximumClock, MaximumClock(processor.Number("MaxClockSpeed"))) +
               Strings.Format(Strings.L2CacheReportedL3CacheReported, Cache(processor.Number("L2CacheSize")),
                   Cache(processor.Number("L3CacheSize"))) +
               Strings.Format(Strings.VirtualizationExtensionsWindowsReported,
                   ReportedBoolean(processor, "VMMonitorModeExtensions")) +
               Strings.Format(Strings.VirtualizationEnabledInFirmwareWindowsReported,
                   ReportedBoolean(processor, "VirtualizationFirmwareEnabled"));
    }

    private static string Positive(ulong? value)
    {
        return value is > 0 ? value.Value.ToString(CultureInfo.InvariantCulture) : Strings.Unavailable;
    }

    private static string MaximumClock(ulong? megahertz)
    {
        return megahertz is > 0 ? Strings.Format(Strings.GHzMHz, megahertz / 1000d, megahertz) : Strings.Unavailable;
    }

    private static string Cache(ulong? kibibytes)
    {
        return kibibytes is > 0 ? Strings.Format(Strings.CapacityMiB, kibibytes / 1024d) : Strings.Unavailable;
    }

    private static string ReportedBoolean(HardwareRow row, string key)
    {
        return bool.TryParse(row.Text(key), out var value) ? value ? Strings.Yes : Strings.No : Strings.Unavailable;
    }

    private static string Architecture(ulong? code)
    {
        return code switch
        {
            0 => Strings.X86, 1 => Strings.MIPS, 2 => Strings.Alpha, 3 => Strings.PowerPC, 5 => Strings.ARM,
            6 => Strings.Itanium, 9 => Strings.X64, 12 => Strings.ARM64, _ => Strings.Unavailable
        };
    }

    internal static WindowsHardwareInfo.Section Memory(IHardwareQuery query, ulong? installed, ulong? usable)
    {
        var modules = query.Read(Cim, "Win32_PhysicalMemory", "DeviceLocator", "BankLabel", "Capacity",
                "SMBIOSMemoryType",
                "ConfiguredClockSpeed", "Speed", "Manufacturer", "PartNumber")?.Where(r => r.Number("Capacity") != 0)
            .ToArray();
        var arrays = query.Read(Cim, "Win32_PhysicalMemoryArray", "MemoryDevices", "Use")
            ?.Where(r => r.Number("Use") == 3).ToArray();
        var slots = arrays is { Length: > 0 } && arrays.All(r => r.Number("MemoryDevices") is > 0)
            ? arrays.Sum(r => (long)r.Number("MemoryDevices")!.Value)
            : (long?)null;
        var count = modules is { Length: > 0 } ? modules.Length : (int?)null;
        var slotText = slots is { } total && count is { } used && used <= total
            ? Strings.Format(Strings.OfSlotsUsedFirmwareReported, used, total)
            : Strings.SlotUsageUnavailable;
        var summary = Strings.Format(Strings.InstalledOSUsable, Capacity(installed), Capacity(usable),
            count is { } n ? Strings.Format(Strings.MemoryModules, n) : Strings.ModuleCountUnavailable, slotText);
        if (modules is { Length: > 0 })
            summary += "\n\n" + string.Join("\n\n", modules.Select((m, i) =>
                (m.Text("DeviceLocator") is { Length: > 0 } locator ? locator : Strings.Format(Strings.Module, i + 1)) +
                "\n" +
                Strings.Format(Strings.CapacityType, Capacity(m.Number("Capacity")),
                    MemoryType(m.Number("SMBIOSMemoryType"))) +
                Strings.Format(Strings.ConfiguredSpeedReportedSpeed, Speed(m.Number("ConfiguredClockSpeed")),
                    Speed(m.Number("Speed"))) +
                Strings.Format(Strings.ManufacturerPartNumberBank, m.Label("Manufacturer"), m.Label("PartNumber"),
                    m.Label("BankLabel"))));
        else summary += Strings.ModuleDetailsUnavailableFromWindows;
        summary += Strings.MemorySpecificationsAndSlotCountsReflectFirmwareReports;
        return new WindowsHardwareInfo.Section(Strings.InstalledMemory, summary);
    }

    internal static WindowsHardwareInfo.Section Disks(IHardwareQuery query)
    {
        var drives = query.Read(Cim, "Win32_DiskDrive", "Index", "Model", "Manufacturer", "Size");
        var disks = query.Read(Storage, "MSFT_Disk", "Number", "UniqueId", "UniqueIdFormat", "Model", "Manufacturer",
            "Size", "BusType", "HealthStatus");
        var media = query.Read(Storage, "MSFT_PhysicalDisk", "UniqueId", "UniqueIdFormat", "MediaType");
        var partitions = query.Read(Storage, "MSFT_Partition", "DiskNumber", "DriveLetter");
        var source = drives is { Count: > 0 } ? drives : disks;
        if (source is null || source.Count == 0)
            return new WindowsHardwareInfo.Section(Strings.PhysicalDrives,
                Strings.PhysicalDriveInventoryUnavailableVolumeInformationIsListedSeparately);

        var summaries = new List<string>();
        foreach (var drive in source)
        {
            var number = drive.Number(drives is { Count: > 0 } ? Strings.Index : Strings.Number);
            var matches = number is null ? [] : disks?.Where(d => d.Number("Number") == number).ToArray() ?? [];
            var disk = matches.Length == 1 ? matches[0] : null;
            // Never join by model, capacity or PhysicalDisk.DeviceId: these need not identify an OS disk.
            var id = disk?.Text("UniqueId");
            var idMatches = string.IsNullOrWhiteSpace(id) || disk?.Number("UniqueIdFormat") is null
                ? []
                : media?.Where(m =>
                    string.Equals(m.Text("UniqueId"), id, StringComparison.OrdinalIgnoreCase) &&
                    m.Number("UniqueIdFormat") == disk.Number("UniqueIdFormat")).ToArray() ?? [];
            var uniqueDiskId = id is not null &&
                               disks?.Count(d =>
                                   string.Equals(d.Text("UniqueId"), id, StringComparison.OrdinalIgnoreCase)) == 1;
            var type = idMatches.Length == 1 && uniqueDiskId
                ? MediaType(idMatches[0].Number("MediaType"))
                : Strings.Unavailable;
            var bus = BusType(disk?.Number("BusType"));
            var health = Health(disk?.Number("HealthStatus"));
            var letters = number is null || partitions is null
                ? null
                : partitions.Where(p => p.Number("DiskNumber") == number)
                    .Select(p => DriveLetter(p.Text("DriveLetter"))).Where(l => l.Length > 0).Distinct().Order()
                    .ToArray();
            var volumes = letters is null ? Strings.MappingUnavailable :
                letters.Length == 0 ? Strings.NoDriveLettersAssigned : string.Join(", ", letters);
            var title = Strings.Format(Strings.PhysicalDiskTitle,
                number is { } index ? index.ToString(CultureInfo.InvariantCulture) : Strings.UnknownDiskNumber,
                drive.Label("Model"));
            var capacity = Capacity(drive.Number("Size"));
            summaries.Add(Strings.Format(Strings.MediaConnectionCapacity, title, type, bus, capacity) +
                          Strings.Format(Strings.VolumesManufacturerWindowsHealthStatus, volumes,
                              drive.Label("Manufacturer"), health));
        }

        return new WindowsHardwareInfo.Section(Strings.PhysicalDrives, string.Join("\n\n", summaries) +
                                                                       Strings
                                                                           .DevicesAreShownAsWindowsExposesThemRAIDAndVirtual +
                                                                       Strings
                                                                           .HealthIsAProviderReportNotAFullSMARTDiagnostic);
    }

    internal static WindowsHardwareInfo.Section Network(IHardwareQuery query)
    {
        var adapters = query.Read(@"root\StandardCimv2", "MSFT_NetAdapter", "Name", "InterfaceDescription",
            "InterfaceType", "InterfaceOperationalStatus", "InterfaceAdminStatus", "MediaConnectState",
            "State", "Virtual", "ReceiveLinkSpeed");
        if (adapters is null)
            return new WindowsHardwareInfo.Section(Strings.NetworkAdapters,
                Strings.NetworkAdapterInventoryUnavailableFromWindows);

        var descriptions = new List<string>();
        foreach (var adapter in adapters)
        {
            var operation = adapter.Number("InterfaceOperationalStatus");
            var state = adapter.Number("State");
            // Operational NotPresent can also be reported for a disabled PnP device.
            // State 3 identifies disabled devices. ConfigManagerErrorCode is not exposed by this provider.
            if (adapter.Number("InterfaceType") == 24 ||
                state is not (1 or 2 or 3) || (operation == 6 && state != 3)) continue;

            var status = state == 3 || adapter.Number("InterfaceAdminStatus") == 2 ? Strings.Disabled :
                adapter.Number("MediaConnectState") == 2 ? Strings.Disconnected : operation switch
                {
                    1 => Strings.Up, 2 => Strings.Down, 3 => Strings.Testing, 5 => Strings.Dormant,
                    7 => Strings.LowerLayerDown, _ => Strings.Unknown
                };
            var virtualLabel = bool.TryParse(adapter.Text("Virtual"), out var isVirtual) && isVirtual
                ? Strings.Virtual
                : "";
            var speed = adapter.Number("ReceiveLinkSpeed");
            var speedText = speed is > 0 and <= long.MaxValue
                ? RateText.Format((long)speed.Value)
                : Strings.LinkSpeedUnavailableLabel;
            descriptions.Add(
                $"{adapter.Label("Name")} · {status}{virtualLabel}\n{adapter.Label("InterfaceDescription")}\n{speedText}");
        }

        return new WindowsHardwareInfo.Section(Strings.NetworkAdapters,
            descriptions.Count == 0 ? Strings.NoPresentNetworkAdaptersReported : string.Join("\n\n", descriptions));
    }

    internal static string MemoryType(ulong? type)
    {
        return type switch
        {
            18 => Strings.DDR, 19 => Strings.DDR2, 20 => Strings.DDR2FBDIMM, 24 => Strings.DDR3, 26 => Strings.DDR4,
            27 => Strings.LPDDR,
            28 => Strings.LPDDR2, 29 => Strings.LPDDR3, 30 => Strings.LPDDR4, 34 => Strings.DDR5, 35 => Strings.LPDDR5,
            _ => Strings.Unavailable
        };
    }

    internal static string MediaType(ulong? type)
    {
        return type switch
        {
            3 => Strings.HDD, 4 => Strings.SSD, 5 => Strings.StorageClassMemory, _ => Strings.Unavailable
        };
    }

    internal static string Health(ulong? status)
    {
        return status switch
        {
            0 => Strings.Healthy, 1 => Strings.Warning, 2 => Strings.Unhealthy, _ => Strings.Unavailable
        };
    }

    internal static string BusType(ulong? type)
    {
        return type switch
        {
            1 => Strings.SCSI, 2 => Strings.ATAPI, 3 => Strings.ATA, 4 => Strings.IEEE1394, 5 => Strings.SSA,
            6 => Strings.FibreChannel, 7 => Strings.USB,
            8 => Strings.RAID, 9 => Strings.ISCSI, 10 => Strings.SAS, 11 => Strings.SATA, 12 => Strings.SD,
            13 => Strings.MMC, 14 => Strings.VirtualBus,
            15 => Strings.FileBackedVirtual, 16 => Strings.StorageSpaces, 17 => Strings.NVMe, _ => Strings.Unavailable
        };
    }

    private static string Capacity(ulong? bytes)
    {
        return bytes is > 0 ? Strings.Format(Strings.CapacityGiB, bytes / 1_073_741_824d) : Strings.Unavailable;
    }

    private static string Speed(ulong? speed)
    {
        return speed is > 0 ? Strings.Format(Strings.MHzWindowsReported, speed) : Strings.Unavailable;
    }

    private static string DriveLetter(string value)
    {
        // WMI may expose the Char16 as a character or as its numeric value.
        if (ushort.TryParse(value, out var code)) value = ((char)code).ToString();
        return value.Length == 1 && char.ToUpperInvariant(value[0]) is >= 'A' and <= 'Z'
            ? $"{char.ToUpperInvariant(value[0])}:"
            : "";
    }
}