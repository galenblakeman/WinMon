using System.Globalization;
using MonCore.Windows.Monitoring;
using NUnit.Framework;
using WinMon.Monitoring;
using WinMon.Resources;

namespace WinMon.Tests;

[TestFixture]
[NonParallelizable]
[SetCulture("en-US")]
[SetUICulture("en-US")]
public sealed class HardwareDetailsCharacterizationTests
{
    private static readonly string[] Cultures = ["en-US", "de-DE", "ja-JP"];

    private static HardwareInventorySnapshot Snapshot(HardwareCpu? cpu = null, HardwareNetwork? network = null) =>
        new(1, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, "ok", false, false,
            new(cpu is null ? "unavailable" : "ok", false, [], cpu),
            new("unavailable", false, [], null), new("unavailable", false, [], null),
            new("unavailable", false, [], null), new("unavailable", false, [], null),
            new("unavailable", false, [], null), new(network is null ? "unavailable" : "ok", false, [], network));

    private static HardwareProcessor Processor(ulong? architecture = 9) =>
        new("Processor model", "Vendor", "Socket A", "CPU0", architecture, 8, 16, 4500,
            1_048_576, 2_097_152, true, false);

    private static WindowsHardwareInfo.Section Cpu(HardwareCpu? cpu) =>
        WindowsHardwareInfo.Format(Snapshot(cpu: cpu)).Single(section => section.Name == Strings.CPU);

    private static WindowsHardwareInfo.Section Network(params HardwareNetworkAdapter[] adapters) =>
        WindowsHardwareInfo.Format(Snapshot(network: new HardwareNetwork(adapters)))
            .Single(section => section.Name == Strings.NetworkAdapters);

    private static IEnumerable<TestCaseData> ArchitectureCases
    {
        get
        {
            (ulong? Code, string Key)[] cases =
            [(0, "X86"), (1, "MIPS"), (2, "Alpha"), (3, "PowerPC"), (5, "ARM"), (6, "Itanium"),
                (9, "X64"), (12, "ARM64"), (null, "Unavailable"), (4, "Unavailable"),
                (13, "Unavailable"), (65536, "Unavailable"), (ulong.MaxValue, "Unavailable")];
            foreach (var culture in Cultures)
                foreach (var (code, key) in cases) yield return new TestCaseData(culture, code, key);
        }
    }

    [TestCaseSource(nameof(ArchitectureCases))]
    public void CpuArchitectureKeepsKnownAndUnknownLocalizedLabels(string culture, ulong? code, string key)
    {
        WithUiCulture(culture, () =>
        {
            var section = Cpu(new HardwareCpu(["Observed model"], 8, 16, [Processor(code)]));
            var expected = Strings.Format(Strings.ManufacturerArchitecture, "Vendor", Resource(key));
            Assert.That(section.Details, Does.Contain(expected));
            Assert.That(section.Items, Is.Null);
            Assert.That(section.DetailHeading, Is.EqualTo(Strings.Details));
        });
    }

    private static IEnumerable<TestCaseData> StatusCases
    {
        get
        {
            (ulong? Operation, ulong? Admin, ulong? Media, ulong? State, string Key)[] cases =
            [
                (null, null, null, null, "Unknown"), (0, null, null, null, "Unknown"),
                (1, null, null, null, "Up"), (2, null, null, null, "Down"),
                (3, null, null, null, "Testing"), (4, null, null, null, "Unknown"),
                (5, null, null, null, "Dormant"), (6, null, null, null, "Unknown"),
                (7, null, null, null, "LowerLayerDown"), (8, null, null, null, "Unknown"),
                (ulong.MaxValue, null, null, null, "Unknown"),
                (1, 2, 2, 3, "Disabled"), (1, null, 2, 3, "Disabled"), (1, 0, 0, 3, "Disabled"),
                (1, 2, 2, 0, "Disabled"), (1, 2, 0, null, "Disabled"), (null, 2, null, null, "Disabled"),
                (1, 0, 2, 0, "Disconnected"), (null, null, 2, null, "Disconnected"),
                (ulong.MaxValue, ulong.MaxValue, 2, ulong.MaxValue, "Disconnected"),
                (1, 0, 0, 0, "Up"), (1, ulong.MaxValue, ulong.MaxValue, ulong.MaxValue, "Up")
            ];
            foreach (var culture in Cultures)
                foreach (var (operation, admin, media, state, key) in cases)
                    yield return new TestCaseData(culture, operation, admin, media, state, key);
        }
    }

    [TestCaseSource(nameof(StatusCases))]
    public void AdapterStatusKeepsDisabledAndDisconnectedPrecedence(string culture, ulong? operation,
        ulong? admin, ulong? media, ulong? state, string key)
    {
        WithUiCulture(culture, () =>
        {
            var adapter = new HardwareNetworkAdapter("Owned", "Description", operation, admin, media, state,
                false, 1_000_000_000);
            var section = Network(adapter);
            Assert.That(section.Details, Is.EqualTo($"Owned · {Resource(key)}\nDescription\n1 {Strings.GigabitsPerSecond}"));
            Assert.That(section.Items, Is.Null);
        });
    }

    private static IEnumerable<TestCaseData> SpeedCases
    {
        get
        {
            yield return new TestCaseData(null, null, null);
            yield return new TestCaseData(0UL, false, null);
            yield return new TestCaseData(1UL, true, "0 Mbps");
            yield return new TestCaseData(999_999_999UL, false, "1000 Mbps");
            yield return new TestCaseData(1_000_000_000UL, true, "1 Gbps");
            yield return new TestCaseData((ulong)long.MaxValue, null, "9223372037 Gbps");
            yield return new TestCaseData((ulong)long.MaxValue + 1, true, null);
            yield return new TestCaseData(ulong.MaxValue, false, null);
        }
    }

    [TestCaseSource(nameof(SpeedCases))]
    public void AdapterDetailsKeepVirtualAndUnsignedSpeedBoundaries(ulong? speed, bool? isVirtual, string? expectedSpeed)
    {
        var section = Network(new HardwareNetworkAdapter(null, "", 1, null, null, null, isVirtual, speed));
        var suffix = isVirtual == true ? " · Virtual" : "";
        Assert.That(section.Details, Is.EqualTo($"Unavailable · Up{suffix}\n\n{expectedSpeed ?? "Link speed unavailable"}"));
    }

    [Test]
    public void AdapterDetailsPreserveInputOrderAndEmptyVersusMissingText()
    {
        var section = Network(new HardwareNetworkAdapter("", null, 2, null, null, null, false, null),
            new HardwareNetworkAdapter("Second", "Description", 7, null, null, null, false, 1_000_000_000));
        Assert.That(section.Details, Is.EqualTo(" · Down\nUnavailable\nLink speed unavailable\n\nSecond · Lower layer down\nDescription\n1 Gbps"));
    }

    [Test]
    public void MissingAndEmptyAdapterSectionsRemainDifferent()
    {
        var missing = WindowsHardwareInfo.Format(Snapshot()).Single(section => section.Name == Strings.NetworkAdapters);
        Assert.That(missing.Details, Is.EqualTo(Strings.NetworkAdapterInventoryUnavailableFromWindows));
        Assert.That(Network().Details, Is.EqualTo(Strings.NoPresentNetworkAdaptersReported));
    }

    [Test]
    public void MissingCpuDataRemainsUnavailable()
    {
        Assert.That(Cpu(null).Details, Is.EqualTo(Strings.UnavailableOnThisSystem));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void CpuWithoutSocketDetailsKeepsModelAndCountFallbacks(bool reported)
    {
        var section = Cpu(new HardwareCpu(reported ? ["First model", "Second model"] : [],
            reported ? 8 : null, reported ? 16U : null, []));
        var summary = reported ? "First model\nSecond model\n8 physical cores · 16 logical processors"
            : "Model unavailable\nPhysical core count unavailable · Unavailable logical processors";
        Assert.That(section.Details, Is.EqualTo(summary + "\nAdditional CPU specifications unavailable from Windows."));
        Assert.That(section.Items, Is.Null);
    }

    private static IEnumerable<TestCaseData> SingleSocketCases
    {
        get
        {
            foreach (var socket in new string?[] { null, "", "Socket A" })
                foreach (var flag in new bool?[] { null, false, true })
                    foreach (var cache in new ulong?[] { null, 0, 1_048_576 })
                        yield return new TestCaseData(socket, flag, cache);
        }
    }

    [TestCaseSource(nameof(SingleSocketCases))]
    public void SingleSocketKeepsNullableFlagsCachesAndSocketText(string? socket, bool? flag, ulong? cache)
    {
        var processor = Processor() with { SocketDesignation = socket, VirtualizationExtensions = flag,
            VirtualizationFirmwareEnabled = flag, L2CacheBytes = cache, L3CacheBytes = cache,
            MaximumClockMHz = cache is null ? null : cache == 0 ? 0UL : 4500UL };
        var section = Cpu(new HardwareCpu(["Observed model"], 8, 16, [processor]));
        var flagText = flag is true ? "Yes" : flag is false ? "No" : "Unavailable";
        var cacheText = cache is > 0 ? "1 MiB" : "Unavailable";
        var clockText = cache is > 0 ? "4.5 GHz (4500 MHz)" : "Unavailable";
        var socketText = socket is null ? "" : " · " + socket;
        var expected = "Observed model\n8 physical cores · 16 logical processors\n1 populated socket" + socketText +
            "\nManufacturer: Vendor · Architecture: x64\nReported maximum clock: " + clockText +
            $"\nL2 cache (reported): {cacheText} · L3 cache (reported): {cacheText}\nVirtualization extensions: {flagText}\nVirtualization enabled in firmware: {flagText}\n\n" +
            Strings.MaximumClockIsAReportedSpecificationNotLiveFrequencyOr;
        Assert.That(section.Details, Is.EqualTo(expected));
        Assert.That(section.Items, Is.Null);
    }

    [Test]
    public void MultipleSocketsKeepFallbackNamesOrderCountsAndDistinctClockSummary()
    {
        var first = Processor(0) with { SocketDesignation = "", DeviceId = "ignored", Name = null,
            Manufacturer = null, Cores = 0, LogicalProcessors = 0, MaximumClockMHz = null };
        var second = Processor() with { SocketDesignation = null, DeviceId = "Device B", Cores = 2, LogicalProcessors = 4 };
        var third = Processor(12) with { SocketDesignation = null, DeviceId = null, Cores = null, LogicalProcessors = null };
        var section = Cpu(new HardwareCpu(["Observed model"], 10, 20, [first, second, third]));
        Assert.That(section.DetailHeading, Is.EqualTo(Strings.CPUDetails));
        var items = section.Items!;
        Assert.That(items, Has.Count.EqualTo(3));
        Assert.That(items.Select(item => item.Name), Is.EqualTo(new[] { "", "Device B", "Processor 3" }));
        Assert.That(items[0].Text, Does.StartWith("Model: Unavailable\nPhysical cores in this socket: Unavailable\nLogical processors in this socket: Unavailable\n")
            .And.Contain("Manufacturer: Unavailable · Architecture: x86"));
        Assert.That(items[1].Text, Does.Contain("Physical cores in this socket: 2\nLogical processors in this socket: 4\n")
            .And.Contain("Architecture: x64"));
        Assert.That(items[2].Text, Does.Contain("Architecture: ARM64"));
        Assert.That(section.Details, Is.EqualTo("Observed model\n10 physical cores · 20 logical processors\n3 populated sockets · Cores per socket: Unavailable, 2, Unavailable\nReported maximum: Unavailable, 4.5 GHz (4500 MHz)\n" +
            Strings.MaximumClockIsAReportedSpecificationNotLiveFrequencyOr));
    }

    private static WindowsHardwareInfo.Section StorageSection(HardwareDisks? disks = null,
        HardwareVolumes? volumes = null, string status = "ok", bool disk = true)
    {
        var snapshot = Snapshot() with
        {
            Disks = new(status, false, [], disks), Volumes = new(status, false, [], volumes)
        };
        return WindowsHardwareInfo.Format(snapshot).Single(section =>
            section.Name == (disk ? Strings.PhysicalDrives : Strings.LocalVolumes));
    }

    private static IEnumerable<TestCaseData> DiskMappingCases =>
        from number in Enumerable.Range(0, 3)
        from letters in Enumerable.Range(0, 3)
        select new TestCaseData(number, letters);

    [TestCaseSource(nameof(DiskMappingCases))]
    public void DiskDetailsKeepUnknownNumbersAndDistinctLetterMappingStates(int number, int letters)
    {
        ulong? observedNumber = number == 0 ? null : number == 1 ? 0UL : ulong.MaxValue;
        string[]? observedLetters = letters == 0 ? null : letters == 1 ? [] : ["Z:", "C:", "Z:"];
        var disk = new HardwareDisk(observedNumber, null, "", 512UL << 30, 4, 17, 0, observedLetters);
        var section = StorageSection(disks: new([disk]));
        var expectedNumber = number == 0 ? "?" : number == 1 ? "0" : "18446744073709551615";
        var expectedLetters = letters == 0 ? "Mapping unavailable" : letters == 1 ? "No drive letters assigned" : "Z:, C:, Z:";
        Assert.Multiple(() =>
        {
            Assert.That(section.Details, Is.EqualTo($"Disk {expectedNumber} · Unavailable\nMedia: SSD · Connection: NVMe · Capacity: 512 GiB\n" +
                $"Volumes: {expectedLetters}\nManufacturer: \nWindows health status: Healthy" +
                Strings.DevicesAreShownAsWindowsExposesThemRAIDAndVirtual + Strings.HealthIsAProviderReportNotAFullSMARTDiagnostic));
            Assert.That(section.Name, Is.EqualTo(Strings.PhysicalDrives));
            Assert.That(section.Items, Is.Null);
            Assert.That(section.DetailHeading, Is.EqualTo(Strings.Details));
            Assert.That(disk.DriveLetters, Is.SameAs(observedLetters));
        });
    }

    [TestCase(null, "Unavailable")]
    [TestCase(0UL, "Unavailable")]
    [TestCase(1UL, "0 GiB")]
    [TestCase(1073741824UL, "1 GiB")]
    public void DiskCapacityRetainsMissingZeroAndPositiveRoundingBoundaries(ulong? capacity, string expected)
    {
        var disk = new HardwareDisk(1, "", null, capacity, null, null, null, []);
        var text = StorageSection(disks: new([disk])).Details;
        Assert.That(text, Does.StartWith("Disk 1 · \nMedia: Unavailable · Connection: Unavailable · Capacity: " + expected +
            "\nVolumes: No drive letters assigned\nManufacturer: Unavailable\nWindows health status: Unavailable"));
    }

    private static IEnumerable<TestCaseData> VolumeReadingCases
    {
        get
        {
            (long? Total, long? Free, bool Error, string Expected)[] readings =
            [
                (null, null, false, "C: Not ready"), (1L << 30, null, false, "C: Not ready"),
                (null, 1L << 30, false, "C: Not ready"),
                (0, 0, false, "C: Work · NTFS\n0 GiB capacity · 0 GiB available"),
                (2L << 30, 1L << 30, false, "C: Work · NTFS\n2 GiB capacity · 1 GiB available"),
                (2L << 30, 1L << 30, true, "C: Details unavailable"),
                (null, null, true, "C: Details unavailable"),
                (-(1L << 30), 3L << 30, false, "C: Work · NTFS\n-1 GiB capacity · 3 GiB available")
            ];
            foreach (var ready in new[] { false, true })
                foreach (var (total, free, error, expected) in readings)
                    yield return new TestCaseData(ready, total, free, error, expected);
        }
    }

    [TestCaseSource(nameof(VolumeReadingCases))]
    public void VolumeDetailsKeepErrorAndNullableReadingPrecedenceIndependentlyOfReady(
        bool ready, long? total, long? free, bool error, string expected)
    {
        var volume = new HardwareVolume("C:", "Work", "NTFS", total, free, ready, error);
        var section = StorageSection(volumes: new([volume]), disk: false);
        Assert.Multiple(() =>
        {
            Assert.That(section.Details, Is.EqualTo(expected));
            Assert.That(section.Items, Is.Null);
            Assert.That(section.Name, Is.EqualTo(Strings.LocalVolumes));
            Assert.That(section.DetailHeading, Is.EqualTo(Strings.Details));
        });
    }

    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public void MissingAndEmptyStorageSectionsRetainTheirDistinctFallbacksAndPartialNotice(bool empty, bool partial)
    {
        var status = partial ? "partial" : "unavailable";
        var disks = StorageSection(disks: empty ? new([]) : null, status: status);
        var volumes = StorageSection(volumes: empty ? new([]) : null, status: status, disk: false);
        var suffix = partial ? "\n\n" + Strings.HardwarePartialInformation : "";
        Assert.Multiple(() =>
        {
            Assert.That(disks.Details, Is.EqualTo(Strings.PhysicalDriveInventoryUnavailableVolumeInformationIsListedSeparately + suffix));
            Assert.That(volumes.Details, Is.EqualTo((empty ? Strings.NoLocalVolumesReported : Strings.UnavailableOnThisSystem) + suffix));
        });
    }

    [Test]
    public void StorageFormattingPreservesOrderingLiteralNamesAndCallerOwnedCollections()
    {
        var letters = new[] { "Z:", "C:" };
        var disk = new HardwareDisk(7, "[literal]\nmodel", "Maker", 1UL << 30, 3, 7, 1, letters);
        var disks = new[] { disk, disk with { Number = 2, Model = "Second" } };
        var volumes = new[] { new HardwareVolume("[C:]", null, null, 2L << 30, 1L << 30, true, false),
            new HardwareVolume("D:", "Next", "NTFS", null, null, false, false) };
        var first = StorageSection(disks: new(disks)).Details;
        var volumeText = StorageSection(volumes: new(volumes), disk: false).Details;
        Assert.Multiple(() =>
        {
            Assert.That(first, Does.StartWith("Disk 7 · [literal]\nmodel\nMedia: HDD · Connection: USB"));
            Assert.That(first.IndexOf("Disk 2 · Second", StringComparison.Ordinal), Is.GreaterThan(first.IndexOf("Disk 7", StringComparison.Ordinal)));
            Assert.That(first.Split(Strings.HealthIsAProviderReportNotAFullSMARTDiagnostic), Has.Length.EqualTo(2));
            Assert.That(volumeText, Is.EqualTo("[C:]  · \n2 GiB capacity · 1 GiB available\n\nD: Not ready"));
            Assert.That(StorageSection(disks: new(disks)).Details, Is.EqualTo(first));
            Assert.That(StorageSection(volumes: new(volumes), disk: false).Details, Is.EqualTo(volumeText));
            Assert.That(disks[0], Is.SameAs(disk));
            Assert.That(disk.DriveLetters, Is.SameAs(letters));
            Assert.That(letters, Is.EqualTo(new[] { "Z:", "C:" }));
            Assert.That(volumes[0].Label, Is.Null);
        });
    }

    [TestCase("en-US", "1.5", "0.5")]
    [TestCase("de-DE", "1,5", "0,5")]
    public void StorageCapacityUsesTheCurrentNumberCultureWithoutChangingItsUnits(string culture, string total, string free)
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            var disk = new HardwareDisk(1, "Model", "Maker", 3UL << 29, 4, 17, 0, []);
            var volume = new HardwareVolume("C:", "Work", "NTFS", 3L << 29, 1L << 29, true, false);
            Assert.Multiple(() =>
            {
                Assert.That(StorageSection(disks: new([disk])).Details, Does.Contain("Capacity: " + total + " GiB"));
                Assert.That(StorageSection(volumes: new([volume]), disk: false).Details,
                    Is.EqualTo($"C: Work · NTFS\n{total} GiB capacity · {free} GiB available"));
            });
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    private static string Resource(string key)
    {
        var result = Strings.ResourceManager.GetString(key, CultureInfo.CurrentUICulture);
        Assert.That(result, Is.Not.Null.And.Not.Empty);
        return result!;
    }

    private static void WithUiCulture(string culture, Action assertion)
    {
        var previous = CultureInfo.CurrentUICulture;
        try { CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture); assertion(); }
        finally { CultureInfo.CurrentUICulture = previous; }
    }
}
