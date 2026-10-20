using System.Diagnostics;
using System.Globalization;
using System.Management;
using System.Runtime.InteropServices;

namespace WinMon.Monitoring;

/// <summary>Read-only WMI inventory, separate from the one-second sampler.</summary>
internal interface IHardwareQuery
{
    // Null means the provider failed; an empty collection means it returned no devices.
    IReadOnlyList<HardwareRow>? Read(string scope, string className, params string[] properties);
}

internal sealed class HardwareRow : Dictionary<string, object?>
{
    internal HardwareRow() : base(StringComparer.OrdinalIgnoreCase) { }
    internal string Text(string key) => TryGetValue(key, out var value) ? Convert.ToString(value, CultureInfo.InvariantCulture)?.Trim() ?? "" : "";
    internal ulong? Number(string key) => ulong.TryParse(Text(key), NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) ? number : null;
    internal string Label(string key)
    {
        var text = Text(key);
        return text.Length == 0 || text.Equals("None", StringComparison.OrdinalIgnoreCase) ||
            text.Equals("Unknown", StringComparison.OrdinalIgnoreCase) ||
            text.Equals("To be filled by O.E.M.", StringComparison.OrdinalIgnoreCase) ? "Unavailable" : text;
    }
}

internal sealed class WmiHardwareQuery : IHardwareQuery
{
    public IReadOnlyList<HardwareRow>? Read(string scope, string className, params string[] properties)
    {
        try
        {
            var options = new System.Management.EnumerationOptions { ReturnImmediately = true, Rewindable = false,
                Timeout = TimeSpan.FromSeconds(2), BlockSize = 1 };
            using var searcher = new ManagementObjectSearcher(new ManagementScope(scope),
                new ObjectQuery($"SELECT {string.Join(",", properties)} FROM {className}"), options);
            using var results = searcher.Get();
            var rows = new List<HardwareRow>();
            var elapsed = Stopwatch.StartNew();
            foreach (ManagementBaseObject item in results)
            {
                using (item)
                {
                    if (rows.Count >= 256 || elapsed.Elapsed > TimeSpan.FromSeconds(4))
                        return null; // Do not report a partial inventory as complete.
                    var row = new HardwareRow();
                    foreach (var property in properties) row[property] = item[property];
                    rows.Add(row);
                }
            }
            return rows;
        }
        catch (Exception ex) when (ex is ManagementException or COMException or UnauthorizedAccessException or
            System.Security.SecurityException or TimeoutException)
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

    internal static WindowsHardwareInfo.Section Cpu(IHardwareQuery query, string basicSummary)
    {
        var processors = query.Read(Cim, "Win32_Processor", "DeviceID", "Name", "SocketDesignation", "Manufacturer",
            "Architecture", "NumberOfCores", "NumberOfLogicalProcessors", "MaxClockSpeed", "L2CacheSize", "L3CacheSize",
            "VMMonitorModeExtensions", "VirtualizationFirmwareEnabled");
        if (processors is null || processors.Count == 0)
            return new WindowsHardwareInfo.Section("CPU", basicSummary + "\nAdditional CPU specifications unavailable from Windows.");

        var details = processors.Select((p, i) => new WindowsHardwareInfo.Detail(
            p.Label("SocketDesignation") is var socket && socket != "Unavailable" ? socket :
                p.Label("DeviceID") is var id && id != "Unavailable" ? id : $"Processor {i + 1}",
            $"Model: {p.Label("Name")}\nManufacturer: {p.Label("Manufacturer")}\nArchitecture: {Architecture(p.Number("Architecture"))}\n" +
            $"Physical cores in this socket: {Positive(p.Number("NumberOfCores"))}\nLogical processors in this socket: {Positive(p.Number("NumberOfLogicalProcessors"))}\n" +
            $"Reported maximum clock: {MaximumClock(p.Number("MaxClockSpeed"))}\nL2 cache (reported): {Cache(p.Number("L2CacheSize"))}\nL3 cache (reported): {Cache(p.Number("L3CacheSize"))}\n" +
            $"Virtualization extensions (Windows-reported): {ReportedBoolean(p, "VMMonitorModeExtensions")}\n" +
            $"Virtualization enabled in firmware (Windows-reported): {ReportedBoolean(p, "VirtualizationFirmwareEnabled")}" )).ToArray();
        var coreCounts = string.Join(", ", processors.Select(p => Positive(p.Number("NumberOfCores"))));
        var clocks = string.Join(", ", processors.Select(p => MaximumClock(p.Number("MaxClockSpeed"))).Distinct());
        return new WindowsHardwareInfo.Section("CPU", basicSummary +
            $"\n{processors.Count} populated {(processors.Count == 1 ? "socket" : "sockets")} · Cores per socket: {coreCounts}\nReported maximum: {clocks}" +
            "\nMaximum clock is a reported specification, not live frequency or measured boost. Virtual machines may mask specifications and capabilities.", details, "CPU details");
    }

    private static string Positive(ulong? value) => value is > 0 ? value.Value.ToString(CultureInfo.InvariantCulture) : "Unavailable";
    private static string MaximumClock(ulong? megahertz) => megahertz is > 0 ? $"{megahertz / 1000d:0.###} GHz ({megahertz} MHz)" : "Unavailable";
    private static string Cache(ulong? kibibytes) => kibibytes is > 0 ? $"{kibibytes / 1024d:0.##} MiB" : "Unavailable";
    private static string ReportedBoolean(HardwareRow row, string key) => bool.TryParse(row.Text(key), out var value) ? value ? "Yes" : "No" : "Unavailable";
    private static string Architecture(ulong? code) => code switch
    {
        0 => "x86", 1 => "MIPS", 2 => "Alpha", 3 => "PowerPC", 5 => "ARM", 6 => "Itanium", 9 => "x64", 12 => "ARM64", _ => "Unavailable"
    };

    internal static WindowsHardwareInfo.Section Memory(IHardwareQuery query, ulong? installed, ulong? usable)
    {
        var modules = query.Read(Cim, "Win32_PhysicalMemory", "DeviceLocator", "BankLabel", "Capacity", "SMBIOSMemoryType",
            "ConfiguredClockSpeed", "Speed", "Manufacturer", "PartNumber")?.Where(r => r.Number("Capacity") != 0).ToArray();
        var arrays = query.Read(Cim, "Win32_PhysicalMemoryArray", "MemoryDevices", "Use")?.Where(r => r.Number("Use") == 3).ToArray();
        var slots = arrays is { Length: > 0 } && arrays.All(r => r.Number("MemoryDevices") is > 0)
            ? arrays.Sum(r => (long)r.Number("MemoryDevices")!.Value) : (long?)null;
        var count = modules is { Length: > 0 } ? modules.Length : (int?)null;
        var slotText = slots is { } total && count is { } used && used <= total
            ? $"{used} of {total} slots used (firmware-reported)" : "Slot usage unavailable";
        var summary = $"Installed: {Capacity(installed)} · OS-usable: {Capacity(usable)}\n{(count is { } n ? $"{n} memory modules" : "Module count unavailable")} · {slotText}";
        var items = modules?.Select((m, i) => new WindowsHardwareInfo.Detail(
            m.Text("DeviceLocator") is { Length: > 0 } locator ? locator : $"Module {i + 1}",
            $"Capacity: {Capacity(m.Number("Capacity"))}\nType: {MemoryType(m.Number("SMBIOSMemoryType"))}\n" +
            $"Configured speed: {Speed(m.Number("ConfiguredClockSpeed"))}\nReported speed: {Speed(m.Number("Speed"))}\n" +
            $"Manufacturer: {m.Label("Manufacturer")}\nPart number: {m.Label("PartNumber")}\nBank: {m.Label("BankLabel")}" )).ToArray() ?? [];
        if (modules is { Length: > 0 })
            summary += "\nType: " + string.Join(" · ", modules.Select(m => MemoryType(m.Number("SMBIOSMemoryType"))).Distinct()) +
                " · Configured: " + string.Join(", ", modules.Select(m => Speed(m.Number("ConfiguredClockSpeed"))).Distinct());
        else summary += "\nModule details unavailable from Windows.";
        summary += "\nMemory specifications and slot counts reflect firmware reports.";
        return new WindowsHardwareInfo.Section("Installed memory", summary, items, "Module details");
    }

    internal static WindowsHardwareInfo.Section Disks(IHardwareQuery query)
    {
        var drives = query.Read(Cim, "Win32_DiskDrive", "Index", "Model", "Manufacturer", "Size");
        var disks = query.Read(Storage, "MSFT_Disk", "Number", "UniqueId", "UniqueIdFormat", "Model", "Manufacturer", "Size", "BusType", "HealthStatus");
        var media = query.Read(Storage, "MSFT_PhysicalDisk", "UniqueId", "UniqueIdFormat", "MediaType");
        var partitions = query.Read(Storage, "MSFT_Partition", "DiskNumber", "DriveLetter");
        var source = drives is { Count: > 0 } ? drives : disks;
        if (source is null || source.Count == 0)
            return new WindowsHardwareInfo.Section("Physical drives", "Physical-drive inventory unavailable. Volume information is listed separately.");

        var summaries = new List<string>();
        var details = new List<WindowsHardwareInfo.Detail>();
        foreach (var drive in source)
        {
            var number = drive.Number(drives is { Count: > 0 } ? "Index" : "Number");
            var matches = number is null ? [] : disks?.Where(d => d.Number("Number") == number).ToArray() ?? [];
            var disk = matches.Length == 1 ? matches[0] : null;
            // Never join by model, capacity or PhysicalDisk.DeviceId: these need not identify an OS disk.
            var id = disk?.Text("UniqueId");
            var idMatches = string.IsNullOrWhiteSpace(id) || disk?.Number("UniqueIdFormat") is null ? [] : media?.Where(m =>
                string.Equals(m.Text("UniqueId"), id, StringComparison.OrdinalIgnoreCase) &&
                m.Number("UniqueIdFormat") == disk?.Number("UniqueIdFormat")).ToArray() ?? [];
            var uniqueDiskId = id is not null && disks?.Count(d => string.Equals(d.Text("UniqueId"), id, StringComparison.OrdinalIgnoreCase)) == 1;
            var type = idMatches.Length == 1 && uniqueDiskId ? MediaType(idMatches[0].Number("MediaType")) : "Unavailable";
            var bus = BusType(disk?.Number("BusType"));
            var health = Health(disk?.Number("HealthStatus"));
            var letters = number is null || partitions is null ? null : partitions.Where(p => p.Number("DiskNumber") == number)
                .Select(p => DriveLetter(p.Text("DriveLetter"))).Where(l => l.Length > 0).Distinct().Order().ToArray();
            var volumes = letters is null ? "Mapping unavailable" : letters.Length == 0 ? "No drive letters assigned" : string.Join(", ", letters);
            var title = $"Disk {(number is { } index ? index.ToString(CultureInfo.InvariantCulture) : "?")} · {drive.Label("Model")}";
            var capacity = Capacity(drive.Number("Size"));
            summaries.Add($"{title}\nMedia: {type} · Connection: {bus} · {capacity}\nVolumes: {volumes}");
            details.Add(new WindowsHardwareInfo.Detail(title,
                $"Manufacturer: {drive.Label("Manufacturer")}\nModel: {drive.Label("Model")}\nMedia: {type}\nConnection: {bus}\nCapacity: {capacity}\n" +
                $"Volumes: {volumes}\nWindows health status: {health}\nHealth is a provider report, not a full SMART diagnostic."));
        }
        return new WindowsHardwareInfo.Section("Physical drives", string.Join("\n\n", summaries) +
            "\n\nDevices are shown as Windows exposes them; RAID and virtual disks may hide underlying hardware.", details, "Drive details");
    }

    internal static string MemoryType(ulong? type) => type switch
    {
        18 => "DDR", 19 => "DDR2", 20 => "DDR2 FB-DIMM", 24 => "DDR3", 26 => "DDR4", 27 => "LPDDR",
        28 => "LPDDR2", 29 => "LPDDR3", 30 => "LPDDR4", 34 => "DDR5", 35 => "LPDDR5", _ => "Unavailable"
    };
    internal static string MediaType(ulong? type) => type switch { 3 => "HDD", 4 => "SSD", 5 => "Storage-class memory", _ => "Unavailable" };
    internal static string Health(ulong? status) => status switch { 0 => "Healthy", 1 => "Warning", 2 => "Unhealthy", _ => "Unavailable" };
    internal static string BusType(ulong? type) => type switch
    {
        1 => "SCSI", 2 => "ATAPI", 3 => "ATA", 4 => "IEEE 1394", 5 => "SSA", 6 => "Fibre Channel", 7 => "USB",
        8 => "RAID", 9 => "iSCSI", 10 => "SAS", 11 => "SATA", 12 => "SD", 13 => "MMC", 14 => "Virtual",
        15 => "File-backed virtual", 16 => "Storage Spaces", 17 => "NVMe", _ => "Unavailable"
    };
    private static string Capacity(ulong? bytes) => bytes is > 0 ? $"{bytes / 1_073_741_824d:0.##} GiB" : "Unavailable";
    private static string Speed(ulong? speed) => speed is > 0 ? $"{speed} MHz (Windows-reported)" : "Unavailable";
    private static string DriveLetter(string value)
    {
        // WMI may expose the Char16 as a character or as its numeric value.
        if (ushort.TryParse(value, out var code)) value = ((char)code).ToString();
        return value.Length == 1 && char.ToUpperInvariant(value[0]) is >= 'A' and <= 'Z' ? $"{char.ToUpperInvariant(value[0])}:" : "";
    }
}
