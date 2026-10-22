using NUnit.Framework;
using WinMon.Monitoring;

namespace WinMon.Tests;

[TestFixture]
public sealed class HardwareInventoryTests
{
    private const ulong GiB = 1_073_741_824;
    private static HardwareRow Row(params (string Name, object? Value)[] properties)
    {
        var row = new HardwareRow();
        foreach (var (name, value) in properties) row[name] = value;
        return row;
    }

    [Test]
    public void Motherboard_ShowsManufacturerProductVersionAndSerialNumber()
    {
        var query = new Query();
        query.Rows["Win32_BaseBoard"] = [Row(("Manufacturer", "Example"), ("Product", "Board X"),
            ("Version", "Rev 2"), ("SerialNumber", "SN-123"))];

        var section = HardwareInventory.Motherboard(query);
        Assert.That(section.Name, Is.EqualTo("Motherboard"));
        Assert.That(section.Details, Is.EqualTo("Manufacturer: Example\nModel: Board X\nBoard version: Rev 2\nSerial number: SN-123"));
    }

    [Test]
    public void Motherboard_PreservesAvailableFieldsAndLabelsFirmwarePlaceholders()
    {
        var query = new Query();
        query.Rows["Win32_BaseBoard"] = [Row(("Manufacturer", "Example"), ("Product", "To be filled by O.E.M."),
            ("Version", "Unknown"), ("SerialNumber", " "))];

        Assert.That(HardwareInventory.Motherboard(query).Details,
            Is.EqualTo("Manufacturer: Example\nModel: Unavailable\nBoard version: Unavailable\nSerial number: Unavailable"));
    }

    [Test]
    public void Motherboard_HandlesMissingAndEmptyInventory()
    {
        var query = new Query();
        Assert.That(HardwareInventory.Motherboard(query).Details, Does.Contain("unavailable"));
        query.Rows["Win32_BaseBoard"] = [];
        Assert.That(HardwareInventory.Motherboard(query).Details, Does.Contain("unavailable"));
    }

    [Test]
    public void Cpu_ShowsSocketsCacheUnitsArchitectureAndReportedCapabilities()
    {
        var query = new Query();
        query.Rows["Win32_Processor"] = [Row(("SocketDesignation", "Socket A"), ("Name", "Example CPU"), ("Manufacturer", "Example"),
            ("Architecture", 9), ("NumberOfCores", 8), ("NumberOfLogicalProcessors", 16), ("MaxClockSpeed", 4500),
            ("L2CacheSize", 8192), ("L3CacheSize", 32768), ("VMMonitorModeExtensions", true), ("VirtualizationFirmwareEnabled", false))];
        var section = HardwareInventory.Cpu(query, "Native model\n8 physical cores · 16 logical processors");
        Assert.Multiple(() =>
        {
            Assert.That(section.Details, Does.StartWith("Native model\n8 physical cores · 16 logical processors"));
            Assert.That(section.Details, Does.Contain("1 populated socket · Socket A").And.Contain("4.5 GHz (4500 MHz)").And.Contain("not live frequency"));
            Assert.That(section.Items, Is.Null);
            Assert.That(section.Details, Does.Not.Contain("Model:").And.Not.Contain("cores in this socket").And.Not.Contain("Cores per socket"));
            Assert.That(section.Details.Split("4.5 GHz (4500 MHz)"), Has.Length.EqualTo(2));
            Assert.That(section.Details, Does.Contain("Architecture: x64").And.Contain("Manufacturer: Example"));
            Assert.That(section.Details, Does.Contain("L2 cache (reported): 8 MiB").And.Contain("L3 cache (reported): 32 MiB"));
            Assert.That(section.Details, Does.Contain("Virtualization extensions: Yes").And.Contain("enabled in firmware: No"));
        });
    }

    [Test]
    public void Cpu_KeepsDifferentSocketSpecificationsSeparate()
    {
        var query = new Query();
        query.Rows["Win32_Processor"] = [Row(("NumberOfCores", 8), ("MaxClockSpeed", 4000), ("L3CacheSize", 32768)),
            Row(("NumberOfCores", 16), ("MaxClockSpeed", 3500), ("L3CacheSize", 65536))];
        var section = HardwareInventory.Cpu(query, "Machine CPU");
        Assert.That(section.Details, Does.Contain("2 populated sockets").And.Contain("Cores per socket: 8, 16").And.Contain("4 GHz (4000 MHz), 3.5 GHz (3500 MHz)"));
        Assert.That(section.Items![0].Text, Does.Contain("32 MiB"));
        Assert.That(section.Items[1].Text, Does.Contain("64 MiB"));
    }

    [Test]
    public void Cpu_UnknownFlagsAndZeroSpecificationsRemainUnavailable()
    {
        var query = new Query();
        query.Rows["Win32_Processor"] = [Row(("Architecture", 999), ("L2CacheSize", 0), ("MaxClockSpeed", 0), ("SocketDesignation", "None"), ("DeviceID", "CPU0"))];
        var section = HardwareInventory.Cpu(query, "Native CPU");
        Assert.That(section.Items, Is.Null);
        var details = section.Details;
        Assert.That(details, Does.Not.Contain("CPU0"));
        Assert.That(details, Does.Contain("Architecture: Unavailable").And.Contain("Reported maximum clock: Unavailable").And.Contain("L2 cache (reported): Unavailable"));
        Assert.That(details, Does.Contain("Virtualization extensions: Unavailable"));
        Assert.That(details, Does.Not.Contain(": No"));
    }

    [Test]
    public void Cpu_ProviderFailurePreservesNativeSummary()
    {
        var section = HardwareInventory.Cpu(new Query(), "Native CPU\n6 physical cores · 12 logical processors");
        Assert.That(section.Details, Does.StartWith("Native CPU\n6 physical cores · 12 logical processors").And.Contain("specifications unavailable"));
        Assert.That(section.Items, Is.Null);
    }

    [Test]
    public void Memory_ShowsModulesConfiguredSpeedAndSystemMemorySlots()
    {
        var query = new Query();
        query.Rows["Win32_PhysicalMemory"] = [
            Row(("Capacity", 16 * GiB), ("DeviceLocator", "DIMM A2"), ("SMBIOSMemoryType", 34),
                ("ConfiguredClockSpeed", 4800), ("Speed", 6000), ("Manufacturer", "Example"), ("PartNumber", "RAM-16")),
            Row(("Capacity", 16 * GiB), ("DeviceLocator", "DIMM B2"), ("SMBIOSMemoryType", 34), ("ConfiguredClockSpeed", 4800))];
        query.Rows["Win32_PhysicalMemoryArray"] = [Row(("Use", 3), ("MemoryDevices", 4)), Row(("Use", 4), ("MemoryDevices", 8))];
        var section = HardwareInventory.Memory(query, 32 * GiB, 31 * GiB);
        Assert.Multiple(() =>
        {
            Assert.That(section.Details, Does.Contain("Installed: 32 GiB · OS-usable: 31 GiB"));
            Assert.That(section.Details, Does.Contain("2 of 4 slots used"));
            Assert.That(section.Details, Does.Contain("DDR5"));
            Assert.That(section.Items, Is.Null);
            Assert.That(section.Details.Split("DIMM A2"), Has.Length.EqualTo(2));
            Assert.That(section.Details.Split("DIMM B2"), Has.Length.EqualTo(2));
            Assert.That(section.Details, Does.Contain("DIMM A2\nCapacity: 16 GiB · Type: DDR5\nConfigured speed: 4800"));
            Assert.That(section.Details, Does.Contain("Reported speed: 6000").And.Contain("RAM-16"));
            Assert.That(section.Details, Does.Not.Contain("Configured:"));
            Assert.That(section.Details, Does.EndWith("Memory specifications and slot counts reflect firmware reports."));
        });
    }

    [Test]
    public void Memory_ProviderFailureKeepsBasicCapacitiesWithoutInventingModules()
    {
        var section = HardwareInventory.Memory(new Query(), 32 * GiB, 31 * GiB);
        Assert.That(section.Details, Does.Contain("Installed: 32 GiB").And.Contain("OS-usable: 31 GiB").And.Contain("Slot usage unavailable"));
        Assert.That(section.Items, Is.Null);
    }

    [Test]
    public void Memory_InconsistentSlotCountDoesNotReportNegativeFreeSlots()
    {
        var query = new Query();
        query.Rows["Win32_PhysicalMemory"] = [Row(("Capacity", GiB)), Row(("Capacity", GiB))];
        query.Rows["Win32_PhysicalMemoryArray"] = [Row(("Use", 3), ("MemoryDevices", 1))];
        Assert.That(HardwareInventory.Memory(query, null, null).Details, Does.Contain("Slot usage unavailable"));
    }

    [Test]
    public void Disks_MapsSeveralVolumesToOneDiskAndMatchesMediaByUniqueIdentity()
    {
        var query = DiskQuery();
        query.Rows["MSFT_Partition"] = [Row(("DiskNumber", 2), ("DriveLetter", 'D')), Row(("DiskNumber", 2), ("DriveLetter", 69)),
            Row(("DiskNumber", 2), ("DriveLetter", 0)), Row(("DiskNumber", 3), ("DriveLetter", 'C'))];
        var section = HardwareInventory.Disks(query);
        Assert.That(section.Details, Does.Contain("Disk 2 · Example SSD").And.Contain("Media: SSD · Connection: NVMe").And.Contain("Volumes: D:, E:"));
        Assert.That(section.Details, Does.Contain("Windows health status: Healthy"));
        Assert.That(section.Details, Does.Not.Contain("C:"));
        Assert.That(section.Items, Is.Null);
    }

    [Test]
    public void Disks_CombinesEachDriveOnceAndSharesProviderNotes()
    {
        var query = DiskQuery();
        query.Rows["Win32_DiskDrive"] = [
            Row(("Index", 2), ("Model", "Example SSD"), ("Manufacturer", "Example"), ("Size", 512 * GiB)),
            Row(("Index", 3), ("Model", "Other drive"), ("Manufacturer", "Other"), ("Size", 256 * GiB))];
        query.Rows["MSFT_Partition"] = [Row(("DiskNumber", 2), ("DriveLetter", 'D')), Row(("DiskNumber", 3), ("DriveLetter", 'E'))];

        var section = HardwareInventory.Disks(query);
        Assert.Multiple(() =>
        {
            Assert.That(section.Items, Is.Null);
            Assert.That(section.Details.Split("Disk 2 · Example SSD"), Has.Length.EqualTo(2));
            Assert.That(section.Details.Split("Disk 3 · Other drive"), Has.Length.EqualTo(2));
            Assert.That(section.Details, Does.Contain("Capacity: 512 GiB\nVolumes: D:\nManufacturer: Example\nWindows health status: Healthy"));
            Assert.That(section.Details, Does.Contain("Capacity: 256 GiB\nVolumes: E:\nManufacturer: Other\nWindows health status: Unavailable"));
            Assert.That(section.Details.Split("Health is a provider report"), Has.Length.EqualTo(2));
            Assert.That(section.Details, Does.EndWith("Health is a provider report, not a full SMART diagnostic."));
        });
    }

    [Test]
    public void Disks_DoesNotMatchMediaByDeviceNumberOrSimilarModel()
    {
        var query = DiskQuery();
        query.Rows["MSFT_PhysicalDisk"] = [Row(("DeviceId", "2"), ("UniqueId", "different"), ("UniqueIdFormat", 2), ("MediaType", 4))];
        Assert.That(HardwareInventory.Disks(query).Details, Does.Contain("Media: Unavailable"));
    }

    [Test]
    public void Disks_AmbiguousIdentityDoesNotBorrowMediaType()
    {
        var query = DiskQuery();
        query.Rows["MSFT_PhysicalDisk"] = [Row(("UniqueId", "disk-id"), ("UniqueIdFormat", 2), ("MediaType", 4)),
            Row(("UniqueId", "disk-id"), ("UniqueIdFormat", 2), ("MediaType", 3))];
        Assert.That(HardwareInventory.Disks(query).Details, Does.Contain("Media: Unavailable"));
    }

    [Test]
    public void Disks_StorageProviderFailureRetainsModelAndCapacity()
    {
        var query = DiskQuery();
        query.Rows.Remove("MSFT_Disk"); query.Rows.Remove("MSFT_PhysicalDisk");
        var details = HardwareInventory.Disks(query).Details;
        Assert.That(details, Does.Contain("Example SSD").And.Contain("Capacity: 512 GiB").And.Contain("Mapping unavailable").And.Contain("Windows health status: Unavailable"));
    }

    [Test]
    public void UnknownValuesAreUnavailableRatherThanHealthyOrHdd()
    {
        Assert.That(HardwareInventory.MediaType(0), Is.EqualTo("Unavailable"));
        Assert.That(HardwareInventory.Health(null), Is.EqualTo("Unavailable"));
        Assert.That(HardwareInventory.BusType(999), Is.EqualTo("Unavailable"));
        Assert.That(HardwareInventory.MemoryType(999), Is.EqualTo("Unavailable"));
        Assert.That(HardwareInventory.Disks(new Query()).Items, Is.Null);
        Assert.That(Row(("PartNumber", "None")).Label("PartNumber"), Is.EqualTo("Unavailable"));
    }

    [Test]
    public void Disks_UsesStorageInventoryWhenLegacyProviderIsUnavailable()
    {
        var query = DiskQuery();
        query.Rows.Remove("Win32_DiskDrive");
        query.Rows["MSFT_Disk"] = [Row(("Number", 2), ("Model", "Example SSD"), ("Size", 512 * GiB),
            ("UniqueId", "disk-id"), ("UniqueIdFormat", 2), ("BusType", 17))];
        query.Rows["MSFT_Partition"] = [];
        var section = HardwareInventory.Disks(query);
        Assert.That(section.Details, Does.Contain("Disk 2 · Example SSD").And.Contain("Media: SSD").And.Contain("512 GiB").And.Contain("No drive letters assigned"));
    }

    [Test]
    public void Network_KeepsDisconnectedAndDisabledAdaptersAndLabelsVirtualAdapters()
    {
        var query = new Query();
        query.Rows["MSFT_NetAdapter"] = [
            Row(("Name", "Ethernet"), ("State", 2), ("InterfaceOperationalStatus", 1), ("Virtual", false), ("ReceiveLinkSpeed", 1_000_000_000)),
            Row(("Name", "Wi-Fi"), ("State", 2), ("InterfaceOperationalStatus", 2), ("MediaConnectState", 2)),
            Row(("Name", "Disabled NIC"), ("State", 3), ("InterfaceOperationalStatus", 6), ("InterfaceAdminStatus", 2)),
            Row(("Name", "VPN"), ("State", 1), ("InterfaceOperationalStatus", 2), ("MediaConnectState", 2), ("Virtual", true)),
            Row(("Name", "Absent"), ("State", 2), ("InterfaceOperationalStatus", 6)),
            Row(("Name", "Removed"), ("State", 0), ("InterfaceOperationalStatus", 6)),
            Row(("Name", "Unknown presence"), ("InterfaceOperationalStatus", 1)),
            Row(("Name", "Loopback"), ("State", 2), ("InterfaceOperationalStatus", 1), ("InterfaceType", 24))];

        var details = HardwareInventory.Network(query).Details;
        Assert.Multiple(() =>
        {
            Assert.That(details, Does.Contain("Ethernet · Up\n"));
            Assert.That(details, Does.Contain("Wi-Fi · Disconnected"));
            Assert.That(details, Does.Contain("Disabled NIC · Disabled"));
            Assert.That(details, Does.Contain("VPN · Disconnected · Virtual"));
            Assert.That(details, Does.Not.Contain("Absent").And.Not.Contain("Removed").And.Not.Contain("Unknown presence").And.Not.Contain("Loopback"));
        });
    }

    [Test]
    public void Network_LoadsWhenProviderExposesOnlySupportedAdapterProperties()
    {
        var query = new Query
        {
            SupportedProperties = ["Name", "InterfaceDescription", "InterfaceType", "InterfaceOperationalStatus",
                "InterfaceAdminStatus", "MediaConnectState", "State", "Virtual", "ReceiveLinkSpeed"]
        };
        query.Rows["MSFT_NetAdapter"] = [Row(("Name", "Ethernet"), ("State", 2), ("InterfaceOperationalStatus", 1))];

        Assert.That(HardwareInventory.Network(query).Details, Does.Contain("Ethernet · Up"));
    }

    [Test]
    public void Network_DistinguishesProviderFailureFromNoPresentAdapters()
    {
        Assert.That(HardwareInventory.Network(new Query()).Details, Does.Contain("unavailable"));
        var query = new Query();
        query.Rows["MSFT_NetAdapter"] = [Row(("State", 1), ("InterfaceOperationalStatus", 6))];
        Assert.That(HardwareInventory.Network(query).Details, Is.EqualTo("No present network adapters reported."));
    }

    private static Query DiskQuery()
    {
        var query = new Query();
        query.Rows["Win32_DiskDrive"] = [Row(("Index", 2), ("Model", "Example SSD"), ("Manufacturer", "Example"), ("Size", 512 * GiB))];
        query.Rows["MSFT_Disk"] = [Row(("Number", 2), ("UniqueId", "disk-id"), ("UniqueIdFormat", 2), ("BusType", 17), ("HealthStatus", 0))];
        query.Rows["MSFT_PhysicalDisk"] = [Row(("UniqueId", "disk-id"), ("UniqueIdFormat", 2), ("MediaType", 4))];
        return query;
    }

    private sealed class Query : IHardwareQuery
    {
        internal Dictionary<string, IReadOnlyList<HardwareRow>> Rows { get; } = new();
        internal HashSet<string>? SupportedProperties { get; init; }
        public IReadOnlyList<HardwareRow>? Read(string scope, string className, params string[] properties) =>
            SupportedProperties is { } supported && properties.Any(p => !supported.Contains(p))
                ? null : Rows.GetValueOrDefault(className);
    }
}
