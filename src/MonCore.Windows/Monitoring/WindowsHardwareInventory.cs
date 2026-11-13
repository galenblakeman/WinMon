using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security;

namespace WinMon.Monitoring;

/// <summary>On-demand Windows hardware inventory; it is never part of the sampling loop.</summary>
public static class WindowsHardwareInventory
{
    private const string Cim = @"root\cimv2";
    private const string Storage = @"root\Microsoft\Windows\Storage";

    public static HardwareInventorySnapshot Read(bool includeIdentifiers = false, CancellationToken cancellationToken = default)
        => Read(new WmiHardwareQuery(), new NativeProbe(), includeIdentifiers, cancellationToken);

    internal static HardwareInventorySnapshot Read(IHardwareQuery query, IHardwareProbe probe, bool includeIdentifiers = false,
        CancellationToken cancellationToken = default)
    {
        var started = DateTimeOffset.UtcNow;
        var cpu = Cpu(query, probe, cancellationToken);
        var board = Board(query, includeIdentifiers, cancellationToken);
        var memory = Memory(query, probe, cancellationToken);
        var graphics = Graphics(probe, cancellationToken);
        var disks = Disks(query, cancellationToken);
        var volumes = Volumes(probe, includeIdentifiers, cancellationToken);
        var network = Network(query, includeIdentifiers, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        var all = new IHardwareSection[] { cpu, board, memory, graphics, disks, volumes, network };
        var status = all.All(s => s.Status == "ok") ? "ok" : all.All(s => s.Status == "unavailable") ? "unavailable" : "partial";
        return new(1, started, DateTimeOffset.UtcNow, status, includeIdentifiers, all.Any(s => s.Truncated), cpu, board, memory, graphics, disks, volumes, network);
    }

    private static HardwareSection<HardwareCpu> Cpu(IHardwareQuery q, IHardwareProbe probe, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var rows = q.Read(Cim, "Win32_Processor", "DeviceID", "Name", "SocketDesignation", "Manufacturer", "Architecture", "NumberOfCores", "NumberOfLogicalProcessors", "MaxClockSpeed", "L2CacheSize", "L3CacheSize", "VMMonitorModeExtensions", "VirtualizationFirmwareEnabled");
        var processors = rows?.Take(256).Select(r => new HardwareProcessor(Text(r,"Name"), Text(r,"Manufacturer"), Text(r,"SocketDesignation"), Text(r,"DeviceID"), r.Number("Architecture"), r.Number("NumberOfCores"), r.Number("NumberOfLogicalProcessors"), r.Number("MaxClockSpeed"), Bytes(r.Number("L2CacheSize")), Bytes(r.Number("L3CacheSize")), Bool(r,"VMMonitorModeExtensions"), Bool(r,"VirtualizationFirmwareEnabled"))).ToArray() ?? [];
        try
        {
            token.ThrowIfCancellationRequested(); var native = probe.Cpu();
            return Section(new HardwareCpu(native.Names.Take(256).Select(Clean).Where(x => x is not null).Cast<string>().ToArray(),
                native.PhysicalCores, native.LogicalProcessors > 0 ? native.LogicalProcessors : null, processors),
                rows is null, rows?.Count > 256 || native.Names.Count > 256);
        }
        catch (Exception ex) when (Recoverable(ex))
        {
            return processors.Length == 0 && rows is null ? Unavailable<HardwareCpu>() :
                Section(new HardwareCpu([], null, null, processors), true, rows?.Count > 256);
        }
    }

    private static HardwareSection<HardwareMotherboards> Board(IHardwareQuery q, bool identifiers, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); var rows = q.Read(Cim, "Win32_BaseBoard", "Manufacturer", "Product", "Version", "SerialNumber");
        return rows is null ? Unavailable<HardwareMotherboards>() : Section(new HardwareMotherboards(rows.Take(256)
            .Select(r => new HardwareMotherboard(Text(r,"Manufacturer"), Text(r,"Product"), Text(r,"Version"),
                identifiers ? Text(r,"SerialNumber") : null)).ToArray()), false, rows.Count > 256);
    }

    private static HardwareSection<HardwareMemory> Memory(IHardwareQuery q, IHardwareProbe probe, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var modulesRows = q.Read(Cim, "Win32_PhysicalMemory", "DeviceLocator", "BankLabel", "Capacity", "SMBIOSMemoryType", "ConfiguredClockSpeed", "Speed", "Manufacturer", "PartNumber");
        var modules = modulesRows?.Where(r => r.Number("Capacity") != 0).Take(256).Select(r => new HardwareMemoryModule(Text(r,"DeviceLocator"), Text(r,"BankLabel"), r.Number("Capacity"), r.Number("SMBIOSMemoryType"), r.Number("ConfiguredClockSpeed"), r.Number("Speed"), Text(r,"Manufacturer"), Text(r,"PartNumber"))).ToArray() ?? [];
        token.ThrowIfCancellationRequested(); var arrays = q.Read(Cim, "Win32_PhysicalMemoryArray", "MemoryDevices", "Use")?.Where(r => r.Number("Use") == 3).ToArray();
        var slots = SlotCount(arrays);
        try
        {
            token.ThrowIfCancellationRequested(); var native = probe.Memory();
            return Section(new HardwareMemory(native.InstalledBytes, native.UsableBytes,
                modulesRows is null || modulesRows.Count > 256 ? null : modules.Length, slots, modules),
                arrays is null || modulesRows is null || native.InstalledBytes is null || native.UsableBytes is null,
                modulesRows?.Count > 256 || arrays?.Length > 256);
        }
        catch (Exception ex) when (Recoverable(ex))
        {
            return modulesRows is null && arrays is null ? Unavailable<HardwareMemory>() :
                Section(new HardwareMemory(null, null, modulesRows is null || modulesRows.Count > 256 ? null : modules.Length,
                    slots, modules), true, modulesRows?.Count > 256 || arrays?.Length > 256);
        }
    }

    private static HardwareSection<HardwareGraphics> Graphics(IHardwareProbe probe, CancellationToken token)
    {
        try
        {
            token.ThrowIfCancellationRequested();
            var readings = probe.Graphics();
            return Section(new HardwareGraphics(readings.Take(256).Select(x =>
                new HardwareGraphicsAdapter(Clean(x.Name), x.DedicatedBytes, x.SharedLimitBytes)).ToArray()), false, readings.Count > 256);
        }
        catch (Exception ex) when (Recoverable(ex)) { return Unavailable<HardwareGraphics>(); }
    }

    private static HardwareSection<HardwareDisks> Disks(IHardwareQuery q, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); var drives = q.Read(Cim, "Win32_DiskDrive", "Index", "Model", "Manufacturer", "Size");
        token.ThrowIfCancellationRequested();
        var disks = q.Read(Storage, "MSFT_Disk", "Number", "UniqueId", "UniqueIdFormat", "Model", "Manufacturer", "Size", "BusType", "HealthStatus");
        token.ThrowIfCancellationRequested();
        var media = q.Read(Storage, "MSFT_PhysicalDisk", "UniqueId", "UniqueIdFormat", "MediaType");
        token.ThrowIfCancellationRequested();
        var partitions = q.Read(Storage, "MSFT_Partition", "DiskNumber", "DriveLetter");
        var source = drives is { Count: > 0 } ? drives : disks ?? drives;
        if (source is null) return Unavailable<HardwareDisks>();
        var values = new List<HardwareDisk>();
        foreach (var drive in source.Take(256))
        {
            token.ThrowIfCancellationRequested(); var number = drive.Number(drives is { Count: > 0 } ? "Index" : "Number");
            var match = number is null ? [] : disks?.Where(d => d.Number("Number") == number).ToArray() ?? []; var disk = match.Length == 1 ? match[0] : null;
            var id = disk?.Text("UniqueId"); var matches = string.IsNullOrWhiteSpace(id) || disk?.Number("UniqueIdFormat") is null ? [] : media?.Where(m => string.Equals(m.Text("UniqueId"), id, StringComparison.OrdinalIgnoreCase) && m.Number("UniqueIdFormat") == disk.Number("UniqueIdFormat")).ToArray() ?? [];
            var unique = id is not null && disks?.Count(d => string.Equals(d.Text("UniqueId"), id, StringComparison.OrdinalIgnoreCase)) == 1;
            var letters = number is null || partitions is null ? null : partitions.Where(p => p.Number("DiskNumber") == number).Select(p => Letter(p.Text("DriveLetter"))).Where(x => x is not null).Cast<string>().Distinct().Order().ToArray();
            values.Add(new HardwareDisk(number, Text(drive,"Model"), Text(drive,"Manufacturer"), drive.Number("Size"), matches.Length == 1 && unique ? matches[0].Number("MediaType") : null, disk?.Number("BusType"), disk?.Number("HealthStatus"), letters));
        }
        return Section(new HardwareDisks(values), drives is null || disks is null || media is null || partitions is null,
            source.Count > 256 || disks?.Count > 256 || media?.Count > 256 || partitions?.Count > 256);
    }

    private static HardwareSection<HardwareVolumes> Volumes(IHardwareProbe probe, bool identifiers, CancellationToken token)
    {
        try
        {
            token.ThrowIfCancellationRequested();
            var readings = probe.Volumes();
            var values = readings.Take(256).Select(x => new HardwareVolume(x.Name, identifiers ? Clean(x.Label) : null, Clean(x.Format), x.TotalBytes, x.AvailableBytes, !x.Error && x.TotalBytes is not null, x.Error)).ToArray();
            return new(readings.Count > 256 || values.Any(x => x.Error) ? "partial" : "ok", readings.Count > 256,
                readings.Count > 256 ? ["truncated"] : values.Any(x => x.Error) ? ["readFailed"] : [], new HardwareVolumes(values));
        }
        catch (Exception ex) when (Recoverable(ex)) { return Unavailable<HardwareVolumes>(); }
    }

    private static HardwareSection<HardwareNetwork> Network(IHardwareQuery q, bool identifiers, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); var rows = q.Read(@"root\StandardCimv2", "MSFT_NetAdapter", "Name", "InterfaceDescription", "InterfaceType", "InterfaceOperationalStatus", "InterfaceAdminStatus", "MediaConnectState", "State", "Virtual", "ReceiveLinkSpeed");
        if (rows is null) return Unavailable<HardwareNetwork>();
        var adapters = rows.Where(r => r.Number("InterfaceType") != 24 && r.Number("State") is 1 or 2 or 3 && !(r.Number("InterfaceOperationalStatus") == 6 && r.Number("State") != 3)).Take(256).Select(r => new HardwareNetworkAdapter(identifiers ? Text(r,"Name") : null, Text(r,"InterfaceDescription"), r.Number("InterfaceOperationalStatus"), r.Number("InterfaceAdminStatus"), r.Number("MediaConnectState"), r.Number("State"), Bool(r,"Virtual"), r.Number("ReceiveLinkSpeed"))).ToArray();
        return Section(new HardwareNetwork(adapters), false, rows.Count > 256);
    }

    private static HardwareSection<T> Unavailable<T>() => new("unavailable", false, ["providerUnavailable"], default);
    private static HardwareSection<T> Section<T>(T value, bool failed, bool truncated = false) =>
        new(failed || truncated ? "partial" : "ok", truncated,
            failed && truncated ? ["providerUnavailable", "truncated"] : failed ? ["providerUnavailable"] : truncated ? ["truncated"] : [], value);
    private static long? SlotCount(HardwareRow[]? arrays)
    {
        if (arrays is not { Length: > 0 and <= 256 }) return null;
        long total = 0;
        foreach (var array in arrays)
        {
            if (array.Number("MemoryDevices") is not (> 0 and <= long.MaxValue) ||
                array.Number("MemoryDevices")!.Value > (ulong)(long.MaxValue - total)) return null;
            total += (long)array.Number("MemoryDevices")!.Value;
        }
        return total;
    }
    private static ulong? Bytes(ulong? kib) => kib is > 0 and <= ulong.MaxValue / 1024 ? kib.Value * 1024 : null;
    private static string? Text(HardwareRow row, string property) => Clean(row.Text(property));
    private static bool? Bool(HardwareRow row, string property) => bool.TryParse(row.Text(property), out var value) ? value : null;
    private static string? Clean(string? text) => string.IsNullOrWhiteSpace(text) || text.Equals("None", StringComparison.OrdinalIgnoreCase) || text.Equals("Unknown", StringComparison.OrdinalIgnoreCase) || text.Equals("To be filled by O.E.M.", StringComparison.OrdinalIgnoreCase) ? null : text.Trim();
    private static string? Letter(string text) { if (ushort.TryParse(text, out var n)) text = ((char)n).ToString(); return text.Length == 1 && char.ToUpperInvariant(text[0]) is >= 'A' and <= 'Z' ? $"{char.ToUpperInvariant(text[0])}:" : null; }
    private static bool Recoverable(Exception ex) => ex is Win32Exception or ExternalException or IOException or UnauthorizedAccessException or SecurityException or InvalidOperationException;
}

internal interface IHardwareProbe
{
    WindowsHardwareProbe.CpuReading Cpu();
    (ulong? InstalledBytes, ulong? UsableBytes) Memory();
    IReadOnlyList<WindowsHardwareProbe.GpuReading> Graphics();
    IReadOnlyList<WindowsHardwareProbe.VolumeReading> Volumes();
}

internal sealed class NativeProbe : IHardwareProbe
{
    public WindowsHardwareProbe.CpuReading Cpu() => WindowsHardwareProbe.Cpu();
    public (ulong? InstalledBytes, ulong? UsableBytes) Memory() => WindowsHardwareProbe.Memory();
    public IReadOnlyList<WindowsHardwareProbe.GpuReading> Graphics() => WindowsHardwareProbe.Graphics();
    public IReadOnlyList<WindowsHardwareProbe.VolumeReading> Volumes() => WindowsHardwareProbe.Volumes();
}
