using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security;

namespace WinMon.Monitoring;

/// <summary>On-demand hardware inventory; never runs in the resource sampling loop.</summary>
internal static class WindowsHardwareInfo
{
    internal sealed record Detail(string Name, string Text);

    internal sealed record Section(string Name, string Details, IReadOnlyList<Detail>? Items, string DetailHeading)
    {
        public Section(string Name, string Details, IReadOnlyList<Detail>? Items = null)
            : this(Name, Details, Items, Strings.Details)
        {
        }
    }

    internal static IReadOnlyList<Section> Read()
    {
        var query = new WmiHardwareQuery();
        return
        [
            HardwareInventory.Cpu(query, ReadSection(Strings.CPU, Cpu).Details), HardwareInventory.Motherboard(query),
            Memory(query),
            ReadSection(Strings.GraphicsAdapters, Graphics), HardwareInventory.Disks(query),
            ReadSection(Strings.LocalVolumes, Volumes),
            HardwareInventory.Network(query)
        ];
    }

    private static Section ReadSection(string name, Func<string> read)
    {
        try
        {
            return new Section(name, read());
        }
        catch (Exception ex) when (ex is Win32Exception or ExternalException or IOException or
                                       UnauthorizedAccessException or SecurityException or InvalidOperationException)
        {
            return new Section(name, Strings.UnavailableOnThisSystem);
        }
    }

    private static string Cpu()
    {
        var reading = WindowsHardwareProbe.Cpu();
        return $"{(reading.Names.Count == 0 ? Strings.ModelUnavailable : string.Join("\n", reading.Names))}\n" +
               Strings.Format(Strings.LogicalProcessors,
                   reading.PhysicalCores is { } count
                       ? Strings.Format(Strings.PhysicalCores, count)
                       : Strings.PhysicalCoreCountUnavailable, reading.LogicalProcessors);
    }

    private static Section Memory(IHardwareQuery query)
    {
        var reading = WindowsHardwareProbe.Memory();
        return HardwareInventory.Memory(query, reading.InstalledBytes, reading.UsableBytes);
    }

    private static string Graphics()
    {
        var readings = WindowsHardwareProbe.Graphics();
        return readings.Count == 0
            ? Strings.NoHardwareAdaptersReportedByWindows
            : string.Join("\n\n", readings.Select(reading =>
                Strings.Format(Strings.DedicatedVideoMemoryGiB, reading.Name,
                    reading.DedicatedBytes / 1_073_741_824d) +
                Strings.Format(Strings.SharedSystemMemoryLimitGiB,
                    reading.SharedLimitBytes / 1_073_741_824d)));
    }
    private static string Volumes()
    {
        var descriptions = new List<string>();
        foreach (var drive in WindowsHardwareProbe.Volumes())
            descriptions.Add(drive.Error
                ? Strings.Format(Strings.DetailsUnavailable, drive.Name)
                : drive.TotalBytes is { } total && drive.AvailableBytes is { } available
                    ? Strings.Format(Strings.GiBCapacityGiBAvailable, drive.Name, drive.Label, drive.Format,
                        total / 1_073_741_824d, available / 1_073_741_824d)
                    : Strings.Format(Strings.NotReady, drive.Name));

        return descriptions.Count == 0 ? Strings.NoLocalVolumesReported : string.Join("\n\n", descriptions);
    }

}
