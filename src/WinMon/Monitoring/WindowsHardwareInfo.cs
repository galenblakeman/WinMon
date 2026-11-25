using System.Globalization;
using MonCore.Windows.Monitoring;

namespace WinMon.Monitoring;

/// <summary>On-demand hardware inventory; never runs in the resource sampling loop.</summary>
internal static class WindowsHardwareInfo
{
    internal sealed record Detail(string Name, string Text);

    internal sealed record Section(string Name, string Details, IReadOnlyList<Detail>? Items, string DetailHeading)
    {
        internal string Summary => Details.Split(['\r', '\n'],
            StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";

        public Section(string Name, string Details, IReadOnlyList<Detail>? Items = null)
            : this(Name, Details, Items, Strings.Details)
        {
        }
    }

    internal static IReadOnlyList<Section> Read()
    {
        var inventory = WindowsHardwareInventory.Read(includeIdentifiers: true);
        return Format(inventory);
    }

    internal static IReadOnlyList<Section> Format(HardwareInventorySnapshot inventory)
    {
        Section[] sections =
        [
            Cpu(inventory.Cpu), Motherboard(inventory.Motherboard), Memory(inventory.Memory), Graphics(inventory.Graphics),
            Disks(inventory.Disks), Volumes(inventory.Volumes), Network(inventory.Network)
        ];
        IHardwareSection[] readings = [inventory.Cpu, inventory.Motherboard, inventory.Memory, inventory.Graphics,
            inventory.Disks, inventory.Volumes, inventory.Network];
        return sections.Select((section, index) => readings[index].Status == "partial"
            ? section with { Details = section.Details + "\n\n" + Strings.HardwarePartialInformation } : section).ToArray();
    }

    private static Section Cpu(HardwareSection<HardwareCpu> section)
    {
        if (section.Data is not { } data) return new Section(Strings.CPU, Strings.UnavailableOnThisSystem);
        var summary = $"{(data.Names.Length == 0 ? Strings.ModelUnavailable : string.Join("\n", data.Names))}\n" +
                      Strings.Format(Strings.LogicalProcessors, data.PhysicalCores is { } cores ? Strings.Format(Strings.PhysicalCores, cores) : Strings.PhysicalCoreCountUnavailable,
                          data.LogicalProcessors is { } logical ? logical.ToString(CultureInfo.InvariantCulture) : Strings.Unavailable);
        if (data.Processors.Length == 0) return new Section(Strings.CPU, summary + Strings.AdditionalCPUSpecificationsUnavailableFromWindows);
        if (data.Processors.Length == 1)
        {
            var processor = data.Processors[0];
            return new Section(Strings.CPU, summary + Strings.Format(Strings.Value1PopulatedSocket,
                processor.SocketDesignation is { } socket ? $" · {socket}" : "") +
                CpuSpecifications(processor) + "\n\n" + Strings.MaximumClockIsAReportedSpecificationNotLiveFrequencyOr);
        }
        var details = data.Processors.Select((p, i) => new Detail(p.SocketDesignation ?? p.DeviceId ?? Strings.Format(Strings.Processor, i + 1),
            Strings.Format(Strings.Model, Text(p.Name)) + Strings.Format(Strings.PhysicalCoresInThisSocketLogicalProcessorsInThisSocket, Number(p.Cores), Number(p.LogicalProcessors)) +
            CpuSpecifications(p))).ToArray();
        return new Section(Strings.CPU, summary + Strings.Format(Strings.PopulatedCoresPerSocketReportedMaximum,
            data.Processors.Length, Strings.Sockets, string.Join(", ", data.Processors.Select(p => Number(p.Cores))),
            string.Join(", ", data.Processors.Select(p => Clock(p.MaximumClockMHz)).Distinct())) + "\n" +
            Strings.MaximumClockIsAReportedSpecificationNotLiveFrequencyOr, details, Strings.CPUDetails);
    }

    private static string CpuSpecifications(HardwareProcessor processor) =>
        Strings.Format(Strings.ManufacturerArchitecture, Text(processor.Manufacturer), Architecture(processor.Architecture)) +
        Strings.Format(Strings.ReportedMaximumClock, Clock(processor.MaximumClockMHz)) +
        Strings.Format(Strings.L2CacheReportedL3CacheReported, Cache(processor.L2CacheBytes), Cache(processor.L3CacheBytes)) +
        Strings.Format(Strings.VirtualizationExtensionsWindowsReported, Boolean(processor.VirtualizationExtensions)) +
        Strings.Format(Strings.VirtualizationEnabledInFirmwareWindowsReported, Boolean(processor.VirtualizationFirmwareEnabled));

    private static Section Motherboard(HardwareSection<HardwareMotherboards> section) => section.Data is not { } data || data.Items.Length == 0 ? new Section(Strings.Motherboard, Strings.MotherboardInformationUnavailableFromWindows) : new Section(Strings.Motherboard, string.Join("\n\n", data.Items.Select(x => Strings.Format(Strings.ManufacturerModel, Text(x.Manufacturer), Text(x.Product)) + Strings.Format(Strings.BoardVersionSerialNumber, Text(x.Version), Text(x.SerialNumber)))));
    private static Section Memory(HardwareSection<HardwareMemory> section)
    {
        if (section.Data is not { } data) return new Section(Strings.InstalledMemory, Strings.UnavailableOnThisSystem);
        var slotText = data.SlotCount is { } total && data.ModuleCount is { } used && used <= total
            ? Strings.Format(Strings.OfSlotsUsedFirmwareReported, used, total) : Strings.SlotUsageUnavailable;
        var summary = Strings.Format(Strings.InstalledOSUsable, Capacity(data.InstalledBytes), Capacity(data.UsableBytes),
            data.ModuleCount is { } count ? Strings.Format(Strings.MemoryModules, count) : Strings.ModuleCountUnavailable, slotText);
        summary += data.Modules.Length == 0 ? Strings.ModuleDetailsUnavailableFromWindows : "\n\n" +
            string.Join("\n\n", data.Modules.Select((module, index) =>
                (module.DeviceLocator ?? Strings.Format(Strings.Module, index + 1)) + "\n" +
                Strings.Format(Strings.CapacityType, Capacity(module.CapacityBytes), HardwareInventory.MemoryType(module.MemoryType)) +
                Strings.Format(Strings.ConfiguredSpeedReportedSpeed, Speed(module.ConfiguredClockMHz), Speed(module.ReportedClockMHz)) +
                Strings.Format(Strings.ManufacturerPartNumberBank, Text(module.Manufacturer), Text(module.PartNumber), Text(module.BankLabel))));
        return new Section(Strings.InstalledMemory, summary + Strings.MemorySpecificationsAndSlotCountsReflectFirmwareReports);
    }
    private static Section Graphics(HardwareSection<HardwareGraphics> section) => section.Data is not { } data ? new Section(Strings.GraphicsAdapters, Strings.UnavailableOnThisSystem) : new Section(Strings.GraphicsAdapters, data.Adapters.Length == 0 ? Strings.NoHardwareAdaptersReportedByWindows : string.Join("\n\n", data.Adapters.Select(x => Strings.Format(Strings.DedicatedVideoMemoryGiB, Text(x.Name), x.DedicatedBytes / 1_073_741_824d) + Strings.Format(Strings.SharedSystemMemoryLimitGiB, x.SharedSystemMemoryLimitBytes / 1_073_741_824d))));
    private static Section Disks(HardwareSection<HardwareDisks> section) => section.Data is not { } data || data.Items.Count == 0 ? new Section(Strings.PhysicalDrives, Strings.PhysicalDriveInventoryUnavailableVolumeInformationIsListedSeparately) : new Section(Strings.PhysicalDrives, string.Join("\n\n", data.Items.Select(x => Strings.Format(Strings.MediaConnectionCapacity, Strings.Format(Strings.PhysicalDiskTitle, x.Number?.ToString() ?? Strings.UnknownDiskNumber, Text(x.Model)), HardwareInventory.MediaType(x.MediaType), HardwareInventory.BusType(x.BusType), Capacity(x.CapacityBytes)) + Strings.Format(Strings.VolumesManufacturerWindowsHealthStatus, x.DriveLetters is null ? Strings.MappingUnavailable : x.DriveLetters.Length == 0 ? Strings.NoDriveLettersAssigned : string.Join(", ", x.DriveLetters), Text(x.Manufacturer), HardwareInventory.Health(x.HealthStatus)))) + Strings.DevicesAreShownAsWindowsExposesThemRAIDAndVirtual + Strings.HealthIsAProviderReportNotAFullSMARTDiagnostic);
    private static Section Volumes(HardwareSection<HardwareVolumes> section) => section.Data is not { } data ? new Section(Strings.LocalVolumes, Strings.UnavailableOnThisSystem) : new Section(Strings.LocalVolumes, data.Items.Length == 0 ? Strings.NoLocalVolumesReported : string.Join("\n\n", data.Items.Select(x => x.Error ? Strings.Format(Strings.DetailsUnavailable, x.Name) : x.TotalBytes is { } total && x.AvailableBytes is { } available ? Strings.Format(Strings.GiBCapacityGiBAvailable, x.Name, x.Label, x.FileSystem, total / 1_073_741_824d, available / 1_073_741_824d) : Strings.Format(Strings.NotReady, x.Name))));
    private static Section Network(HardwareSection<HardwareNetwork> section) => section.Data is not { } data ? new Section(Strings.NetworkAdapters, Strings.NetworkAdapterInventoryUnavailableFromWindows) : new Section(Strings.NetworkAdapters, data.Adapters.Length == 0 ? Strings.NoPresentNetworkAdaptersReported : string.Join("\n\n", data.Adapters.Select(x => $"{Text(x.Name)} · {NetworkStatus(x)}{(x.Virtual == true ? Strings.Virtual : "")}\n{Text(x.Description)}\n{(x.ReceiveLinkSpeedBitsPerSecond is > 0 and <= long.MaxValue ? RateText.LinkSpeed((long)x.ReceiveLinkSpeedBitsPerSecond.Value) : Strings.LinkSpeedUnavailableLabel)}")));
    private static string Text(string? value) => value ?? Strings.Unavailable;
    private static string Number(ulong? value) => value is > 0 ? value.Value.ToString(CultureInfo.InvariantCulture) : Strings.Unavailable;
    private static string Cache(ulong? bytes) => bytes is > 0 ? Strings.Format(Strings.CapacityMiB, bytes / 1_048_576d) : Strings.Unavailable;
    private static string Capacity(ulong? value) => value is > 0 ? Strings.Format(Strings.CapacityGiB, value.Value / 1_073_741_824d) : Strings.Unavailable;
    private static string Clock(ulong? value) => value is > 0 ? Strings.Format(Strings.GHzMHz, value.Value / 1000d, value) : Strings.Unavailable;
    private static string Speed(ulong? value) => value is > 0 ? Strings.Format(Strings.MHzWindowsReported, value) : Strings.Unavailable;
    private static string Boolean(bool? value) => value is true ? Strings.Yes : value is false ? Strings.No : Strings.Unavailable;
    private static string Architecture(ulong? value) => value switch { 0 => Strings.X86, 1 => Strings.MIPS, 2 => Strings.Alpha, 3 => Strings.PowerPC, 5 => Strings.ARM, 6 => Strings.Itanium, 9 => Strings.X64, 12 => Strings.ARM64, _ => Strings.Unavailable };
    private static string NetworkStatus(HardwareNetworkAdapter value) => value.State == 3 || value.AdminStatus == 2 ? Strings.Disabled : value.MediaConnectState == 2 ? Strings.Disconnected : value.OperationalStatus switch { 1 => Strings.Up, 2 => Strings.Down, 3 => Strings.Testing, 5 => Strings.Dormant, 7 => Strings.LowerLayerDown, _ => Strings.Unknown };

}
