using WinMon.Resources;

namespace WinMon.Controls;

/// <summary>Formats supporting text shown below resource gauges.</summary>
internal static class GaugeReadoutText
{
    internal static string CpuClock(double? megahertz)
    {
        return megahertz is { } value && value > 0 && double.IsFinite(value)
            ? Strings.Format(Strings.GaugeCpuClockGHz, value / 1000d)
            : Strings.Unavailable;
    }

    internal static string Memory(ulong? usedBytes, ulong? totalBytes)
    {
        return usedBytes is { } used && totalBytes is { } total
            ? Strings.Format(Strings.GiB, used / 1_073_741_824d, total / 1_073_741_824d)
            : Strings.Unavailable;
    }

    internal static string DiskRate(double? bytesPerSecond)
    {
        return bytesPerSecond is { } rate && rate >= 0 && double.IsFinite(rate)
            ? Strings.Format(Strings.MBS, rate / 1_000_000d)
            : Strings.Unavailable;
    }

    internal static string GpuMemory(ulong? usedBytes, ulong? totalBytes)
    {
        return usedBytes is { } used && totalBytes is { } total && total > 0
            ? Strings.Format(Strings.GaugeGpuMemoryPair, used / 1_073_741_824d, total / 1_073_741_824d)
            : Strings.GaugeGpuEngine3D;
    }
}
