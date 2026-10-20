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
    public void Cpu_ShowsSocketsCacheUnitsArchitectureAndReportedCapabilities()
    {
        var query = new Query();
        query.Rows["Win32_Processor"] = [Row(("SocketDesignation", "Socket A"), ("Name", "Example CPU"), ("Manufacturer", "Example"),
            ("Architecture", 9), ("NumberOfCores", 8), ("NumberOfLogicalProcessors", 16), ("MaxClockSpeed", 4500),
            ("L2CacheSize", 8192), ("L3CacheSize", 32768), ("VMMonitorModeExtensions", true), ("VirtualizationFirmwareEnabled", false))];
        var section = HardwareInventory.Cpu(query, "Native model\n8 physical cores · 16 logical processors");
        Assert.Multiple(() =>
        {
            Assert.That(section.Details, Does.Contain("1 populated socket").And.Contain("Cores per socket: 8").And.Contain("4.5 GHz (4500 MHz)").And.Contain("not live frequency"));
            Assert.That(section.Items![0].Name, Is.EqualTo("Socket A"));
            Assert.That(section.Items[0].Text, Does.Contain("Architecture: x64").And.Contain("Manufacturer: Example"));
            Assert.That(section.Items[0].Text, Does.Contain("L2 cache (reported): 8 MiB").And.Contain("L3 cache (reported): 32 MiB"));
            Assert.That(section.Items[0].Text, Does.Contain("Virtualization extensions (Windows-reported): Yes").And.Contain("enabled in firmware (Windows-reported): No"));
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
        var item = HardwareInventory.Cpu(query, "Native CPU").Items![0];
        Assert.That(item.Name, Is.EqualTo("CPU0"));
        var details = item.Text;
        Assert.That(details, Does.Contain("Architecture: Unavailable").And.Contain("Reported maximum clock: Unavailable").And.Contain("L2 cache (reported): Unavailable"));
        Assert.That(details, Does.Contain("Virtualization extensions (Windows-reported): Unavailable"));
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
            Assert.That(section.Details, Does.Contain("Configured: 4800"));
            Assert.That(section.Items, Has.Count.EqualTo(2));
            Assert.That(section.Items![0].Name, Is.EqualTo("DIMM A2"));
            Assert.That(section.Items[0].Text, Does.Contain("Configured speed: 4800").And.Contain("Reported speed: 6000").And.Contain("RAM-16"));
        });
    }

    [Test]
    public void Memory_ProviderFailureKeepsBasicCapacitiesWithoutInventingModules()
    {
        var section = HardwareInventory.Memory(new Query(), 32 * GiB, 31 * GiB);
        Assert.That(section.Details, Does.Contain("Installed: 32 GiB").And.Contain("OS-usable: 31 GiB").And.Contain("Slot usage unavailable"));
        Assert.That(section.Items, Is.Empty);
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
        Assert.That(section.Items![0].Text, Does.Contain("Windows health status: Healthy"));
        Assert.That(section.Items[0].Text, Does.Not.Contain("C:"));
    }

    [Test]
    public void Disks_DoesNotMatchMediaByDeviceNumberOrSimilarModel()
    {
        var query = DiskQuery();
        query.Rows["MSFT_PhysicalDisk"] = [Row(("DeviceId", "2"), ("UniqueId", "different"), ("UniqueIdFormat", 2), ("MediaType", 4))];
        Assert.That(HardwareInventory.Disks(query).Items![0].Text, Does.Contain("Media: Unavailable"));
    }

    [Test]
    public void Disks_AmbiguousIdentityDoesNotBorrowMediaType()
    {
        var query = DiskQuery();
        query.Rows["MSFT_PhysicalDisk"] = [Row(("UniqueId", "disk-id"), ("UniqueIdFormat", 2), ("MediaType", 4)),
            Row(("UniqueId", "disk-id"), ("UniqueIdFormat", 2), ("MediaType", 3))];
        Assert.That(HardwareInventory.Disks(query).Items![0].Text, Does.Contain("Media: Unavailable"));
    }

    [Test]
    public void Disks_StorageProviderFailureRetainsModelAndCapacity()
    {
        var query = DiskQuery();
        query.Rows.Remove("MSFT_Disk"); query.Rows.Remove("MSFT_PhysicalDisk");
        var details = HardwareInventory.Disks(query).Items![0].Text;
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
        public IReadOnlyList<HardwareRow>? Read(string scope, string className, params string[] properties) => Rows.GetValueOrDefault(className);
    }
}
