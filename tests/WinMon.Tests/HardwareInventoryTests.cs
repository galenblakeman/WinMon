using NUnit.Framework;
using WinMon.Monitoring;

namespace WinMon.Tests;

[TestFixture]
public sealed class HardwareInventoryTests
{
    private const ulong GiB = 1_073_741_824;

    private static HardwareInventorySnapshot Snapshot(
        HardwareSection<HardwareCpu>? cpu = null, HardwareSection<HardwareMemory>? memory = null,
        HardwareSection<HardwareDisks>? disks = null, HardwareSection<HardwareNetwork>? network = null) => new(1,
        DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, "ok", true, false,
        cpu ?? new("ok", false, [], new HardwareCpu([], null, null, [])),
        new("ok", false, [], new HardwareMotherboards([])),
        memory ?? new("ok", false, [], new HardwareMemory(null, null, null, null, [])),
        new("ok", false, [], new HardwareGraphics([])),
        disks ?? new("ok", false, [], new HardwareDisks([])),
        new("ok", false, [], new HardwareVolumes([])),
        network ?? new("ok", false, [], new HardwareNetwork([])));

    [Test]
    public void FormatCpuShowsSingleSocketSpecificationsWithoutDuplicateModel()
    {
        var cpu = new HardwareCpu(["Native model"], 8, 16,
            [new("Example CPU", "Example", "Socket A", null, 9, 8, 16, 4500, 8 * 1024 * 1024,
                32 * 1024 * 1024, true, false)]);

        var section = WindowsHardwareInfo.Format(Snapshot(cpu: new("ok", false, [], cpu))).Single(x => x.Name == "CPU");

        Assert.That(section.Details, Does.Contain("4.5 GHz (4500 MHz)").And.Contain("8 MiB").And.Contain("32 MiB")
            .And.Contain("Architecture: x64").And.Contain("Virtualization extensions: Yes"));
        Assert.That(section.Details.Split("Native model"), Has.Length.EqualTo(2));
    }

    [Test]
    public void FormatMemoryShowsModuleCountSlotsTypeAndReportedSpeed()
    {
        var memory = new HardwareMemory(32 * GiB, 31 * GiB, 2, 4,
            [new("DIMM A2", null, 16 * GiB, 34, 4800, 6000, "Example", "RAM-16")]);

        var section = WindowsHardwareInfo.Format(Snapshot(memory: new("ok", false, [], memory)))
            .Single(x => x.Name == "Installed memory");

        Assert.That(section.Details, Does.Contain("Installed: 32 GiB").And.Contain("2 of 4 slots used")
            .And.Contain("DDR5").And.Contain("Configured speed: 4800").And.Contain("Reported speed: 6000"));
    }

    [Test]
    public void FormatDisksShowsMediaBusHealthLettersAndProviderCaveat()
    {
        var disk = new HardwareDisk(2, "Example SSD", "Example", 512 * GiB, 4, 17, 0, ["D:", "E:"]);

        var section = WindowsHardwareInfo.Format(Snapshot(disks: new("ok", false, [], new HardwareDisks([disk]))))
            .Single(x => x.Name == "Physical drives");

        Assert.That(section.Details, Does.Contain("Media: SSD · Connection: NVMe").And.Contain("Volumes: D:, E:")
            .And.Contain("Windows health status: Healthy").And.Contain("not a full SMART diagnostic"));
    }

    [Test]
    public void FormatNetworkShowsStateAndDoesNotConflateUnavailableSectionWithEmptyInventory()
    {
        var adapters = new HardwareNetwork([new("Ethernet", "Example", 1, 1, null, 2, false, 1_000_000_000)]);
        var present = WindowsHardwareInfo.Format(Snapshot(network: new("ok", false, [], adapters)))
            .Single(x => x.Name == "Network adapters");
        var unavailable = WindowsHardwareInfo.Format(Snapshot(network: new("unavailable", false, ["providerUnavailable"], null)))
            .Single(x => x.Name == "Network adapters");

        Assert.That(present.Details, Does.Contain("Ethernet · Up"));
        Assert.That(unavailable.Details, Does.Contain("unavailable"));
    }

    [Test]
    public void UnknownProviderCodesRemainUnavailable()
    {
        Assert.Multiple(() =>
        {
            Assert.That(HardwareInventory.MediaType(0), Is.EqualTo("Unavailable"));
            Assert.That(HardwareInventory.Health(null), Is.EqualTo("Unavailable"));
            Assert.That(HardwareInventory.BusType(999), Is.EqualTo("Unavailable"));
            Assert.That(HardwareInventory.MemoryType(999), Is.EqualTo("Unavailable"));
        });
    }
}
