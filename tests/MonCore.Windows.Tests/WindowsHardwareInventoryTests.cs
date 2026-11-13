using NUnit.Framework;
using WinMon.Monitoring;

namespace WinMon.Tests;

[TestFixture]
public sealed class WindowsHardwareInventoryTests
{
    [Test]
    public void Read_OmitsIdentifiersAndKeepsUnambiguousDiskMapping()
    {
        var query = new Query
        {
            ["Win32_BaseBoard"] = [Row(("Manufacturer", "Maker"), ("Product", "Board"), ("SerialNumber", "secret"))],
            ["Win32_DiskDrive"] = [Row(("Index", 0), ("Model", "Disk"), ("Size", 100UL))],
            ["MSFT_Disk"] = [Row(("Number", 0), ("UniqueId", "one"), ("UniqueIdFormat", 1), ("BusType", 17), ("HealthStatus", 0))],
            ["MSFT_PhysicalDisk"] = [Row(("UniqueId", "one"), ("UniqueIdFormat", 1), ("MediaType", 4))],
            ["MSFT_Partition"] = [Row(("DiskNumber", 0), ("DriveLetter", "C"))],
            ["MSFT_NetAdapter"] = [Row(("Name", "Alias"), ("InterfaceDescription", "Adapter"), ("InterfaceType", 6), ("State", 1), ("InterfaceOperationalStatus", 1))]
        };
        var inventory = WindowsHardwareInventory.Read(query, new Probe(), false);
        Assert.Multiple(() =>
        {
            Assert.That(inventory.Motherboard.Data!.Items[0].SerialNumber, Is.Null);
            Assert.That(inventory.Volumes.Data!.Items[0].Label, Is.Null);
            Assert.That(inventory.Network.Data!.Adapters[0].Name, Is.Null);
            Assert.That(inventory.Disks.Data!.Items[0].DriveLetters, Is.EqualTo(new[] { "C:" }));
            Assert.That(inventory.Disks.Data.Items[0].MediaType, Is.EqualTo(4));
        });
    }

    [Test]
    public void Read_CancellationStopsBeforeAnyProviderRead()
    {
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        Assert.That(() => WindowsHardwareInventory.Read(new Query(), new Probe(), cancellationToken: cancellation.Token), Throws.InstanceOf<OperationCanceledException>());
    }

    [Test]
    public void Read_ReportsPartialForUnavailableSupplementalMemoryAndVolumeErrors()
    {
        var query = new Query { ["Win32_PhysicalMemory"] = null, ["Win32_PhysicalMemoryArray"] = null };
        var probe = new Probe { VolumeError = true };
        var inventory = WindowsHardwareInventory.Read(query, probe);
        Assert.Multiple(() =>
        {
            Assert.That(inventory.Memory.Status, Is.EqualTo("partial"));
            Assert.That(inventory.Memory.Data!.ModuleCount, Is.Null);
            Assert.That(inventory.Volumes.Status, Is.EqualTo("partial"));
            Assert.That(inventory.Status, Is.EqualTo("partial"));
        });
    }

    [Test]
    public void Read_TruncatesVolumesAndMarksTheSnapshot()
    {
        var probe = new Probe { VolumeCount = 257 };
        var inventory = WindowsHardwareInventory.Read(new Query(), probe);
        Assert.Multiple(() =>
        {
            Assert.That(inventory.Volumes.Data!.Items, Has.Length.EqualTo(256));
            Assert.That(inventory.Volumes.Truncated, Is.True);
            Assert.That(inventory.Volumes.Issues, Is.EquivalentTo(new[] { "truncated" }));
            Assert.That(inventory.Truncated, Is.True);
        });
    }

    [TestCase("Win32_Processor")]
    [TestCase("Win32_BaseBoard")]
    [TestCase("Win32_PhysicalMemory")]
    [TestCase("MSFT_NetAdapter")]
    public void Read_MarksBoundedProviderListsAsPartial(string provider)
    {
        var query = new Query { [provider] = Enumerable.Range(0, 257).Select(_ =>
            Row(("Capacity", 1), ("State", 1), ("InterfaceType", 6))).ToArray() };
        var inventory = WindowsHardwareInventory.Read(query, new Probe());
        Assert.Multiple(() =>
        {
            Assert.That(inventory.Truncated, Is.True);
            Assert.That(inventory.Status, Is.EqualTo("partial"));
            Assert.That(provider switch
            {
                "Win32_Processor" => inventory.Cpu.Data!.Processors.Length,
                "Win32_BaseBoard" => inventory.Motherboard.Data!.Items.Length,
                "Win32_PhysicalMemory" => inventory.Memory.Data!.Modules.Length,
                _ => inventory.Network.Data!.Adapters.Length
            }, Is.EqualTo(256));
        });
    }

    [Test]
    public void Read_PreservesSocketSpecificationsFalseAndUnknownCodesWithoutOverflow()
    {
        var query = new Query
        {
            ["Win32_Processor"] =
            [
                Row(("Name", "Socket A"), ("NumberOfCores", 4), ("Architecture", 9), ("L2CacheSize", 8192),
                    ("VirtualizationFirmwareEnabled", false)),
                Row(("Name", "Socket B"), ("NumberOfCores", 8), ("Architecture", 999), ("L2CacheSize", ulong.MaxValue))
            ],
            ["Win32_PhysicalMemoryArray"] = [Row(("Use", 3), ("MemoryDevices", ulong.MaxValue))]
        };
        var value = WindowsHardwareInventory.Read(query, new Probe { Fail = true });
        Assert.Multiple(() =>
        {
            Assert.That(value.Cpu.Status, Is.EqualTo("partial"));
            Assert.That(value.Cpu.Data!.LogicalProcessors, Is.Null);
            Assert.That(value.Cpu.Data.Processors.Select(p => p.Cores), Is.EqualTo(new ulong?[] { 4, 8 }));
            Assert.That(value.Cpu.Data.Processors[0].VirtualizationFirmwareEnabled, Is.False);
            Assert.That(value.Cpu.Data.Processors[0].L2CacheBytes, Is.EqualTo(8 * 1_048_576));
            Assert.That(value.Cpu.Data.Processors[1].Architecture, Is.EqualTo(999));
            Assert.That(value.Cpu.Data.Processors[1].ArchitectureName, Is.Null);
            Assert.That(value.Cpu.Data.Processors[1].L2CacheBytes, Is.Null);
            Assert.That(value.Memory.Data!.SlotCount, Is.Null);
        });
    }

    [Test]
    public void Read_DoesNotInferMediaFromAmbiguousIdsOrMatchingModelAndRetainsFallbackFields()
    {
        var query = new Query
        {
            ["Win32_DiskDrive"] = null,
            ["MSFT_Disk"] = [Row(("Number", 2), ("Model", "Same"), ("Manufacturer", "Maker"), ("Size", 123UL),
                ("UniqueId", "duplicated"), ("UniqueIdFormat", 1), ("BusType", 999))],
            ["MSFT_PhysicalDisk"] = [Row(("UniqueId", "duplicated"), ("UniqueIdFormat", 1), ("MediaType", 4)),
                Row(("UniqueId", "duplicated"), ("UniqueIdFormat", 1), ("MediaType", 3))],
            ["MSFT_Partition"] = null
        };
        var value = WindowsHardwareInventory.Read(query, new Probe());
        var disk = value.Disks.Data!.Items.Single();
        Assert.Multiple(() =>
        {
            Assert.That(value.Disks.Status, Is.EqualTo("partial"));
            Assert.That(disk.Model, Is.EqualTo("Same"));
            Assert.That(disk.CapacityBytes, Is.EqualTo(123));
            Assert.That(disk.MediaType, Is.Null);
            Assert.That(disk.BusType, Is.EqualTo(999));
            Assert.That(disk.BusTypeName, Is.Null);
            Assert.That(disk.DriveLetters, Is.Null);
        });
    }

    [Test]
    public void Read_EmptySuccessfulSourcesDifferFromFailedSources()
    {
        var query = new Query();
        var empty = WindowsHardwareInventory.Read(query, new Probe());
        foreach (var provider in new[] { "Win32_Processor", "Win32_BaseBoard", "Win32_PhysicalMemory",
            "Win32_PhysicalMemoryArray", "Win32_DiskDrive", "MSFT_Disk", "MSFT_PhysicalDisk", "MSFT_Partition", "MSFT_NetAdapter" })
            query[provider] = null;
        var failed = WindowsHardwareInventory.Read(query, new Probe { Fail = true });
        Assert.Multiple(() =>
        {
            Assert.That(empty.Motherboard.Status, Is.EqualTo("ok"));
            Assert.That(empty.Motherboard.Data!.Items, Is.Empty);
            Assert.That(empty.Memory.Data!.ModuleCount, Is.EqualTo(0));
            Assert.That(failed.Status, Is.EqualTo("unavailable"));
            Assert.That(failed.Motherboard.Data, Is.Null);
            Assert.That(failed.Memory.Data, Is.Null);
        });
    }

    [TestCase("Win32_Processor")]
    [TestCase("Win32_BaseBoard")]
    [TestCase("Win32_PhysicalMemory")]
    [TestCase("Win32_PhysicalMemoryArray")]
    [TestCase("Win32_DiskDrive")]
    [TestCase("MSFT_Disk")]
    [TestCase("MSFT_PhysicalDisk")]
    [TestCase("MSFT_Partition")]
    [TestCase("MSFT_NetAdapter")]
    public void Read_CancellationStopsAfterEachProvider(string provider)
    {
        using var cancellation = new CancellationTokenSource();
        var query = new Query { OnRead = name => { if (name == provider) cancellation.Cancel(); } };
        Assert.That(() => WindowsHardwareInventory.Read(query, new Probe(), cancellationToken: cancellation.Token),
            Throws.InstanceOf<OperationCanceledException>());
    }

    private sealed class Query : Dictionary<string, IReadOnlyList<HardwareRow>?>, IHardwareQuery
    {
        public Action<string>? OnRead { get; init; }
        public IReadOnlyList<HardwareRow>? Read(string scope, string className, params string[] properties)
        {
            OnRead?.Invoke(className);
            return TryGetValue(className, out var rows) ? rows : [];
        }
    }
    private sealed class Probe : IHardwareProbe
    {
        public bool VolumeError { get; init; }
        public int VolumeCount { get; init; } = 1;
        public bool Fail { get; init; }
        public WindowsHardwareProbe.CpuReading Cpu() => Fail ? throw new IOException() : new(["CPU"], 4, 8);
        public (ulong? InstalledBytes, ulong? UsableBytes) Memory() => Fail ? throw new IOException() : (16UL, 15UL);
        public IReadOnlyList<WindowsHardwareProbe.GpuReading> Graphics() => Fail ? throw new IOException() : [];
        public IReadOnlyList<WindowsHardwareProbe.VolumeReading> Volumes() => Fail ? throw new IOException() : Enumerable.Range(0, VolumeCount).Select(i => new WindowsHardwareProbe.VolumeReading($"{(char)('A' + i % 26)}:\\", "label", "NTFS", 100, 50, VolumeError)).ToArray();
    }
    private static HardwareRow Row(params (string Key, object Value)[] fields)
    {
        var result = new HardwareRow(); foreach (var (key, value) in fields) result[key] = value; return result;
    }
}
