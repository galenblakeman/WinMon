using System.Runtime.InteropServices;

// ReSharper disable StringLiteralTypo

namespace WinMon.Monitoring;

/// <summary>
/// Effective clock: rated clock * <c>% Processor Performance</c> / 100, averaged across logical processors.
/// <c>CurrentMhz</c> is not used because on some CPUs (Ryzen 9000) it stays at the rated clock through idle and boost.
/// </summary>
public sealed class WindowsCpuClock
{
    private ProcessorPowerInformation[] _processors = [];

    /// <summary>
    /// Logical processors in all groups. Unlike <see cref="Environment.ProcessorCount"/>, this ignores WinMon's own
    /// affinity and job CPU limits, so it matches the machine-wide CPU percentages the sampler reports.
    /// </summary>
    public static int LogicalProcessorCount => Math.Max(1, (int)GetActiveProcessorCount(0xFFFF));

    /// <param name="performancePercent">
    /// <c>\Processor Information(_Total)\% Processor Performance</c>, null before the rate counter has two samples.
    /// </param>
    public double? ReadMhz(double? performancePercent)
    {
        if (performancePercent is null) return null;
        var count = GetActiveProcessorCount(0xFFFF); // All processor groups, independent of process affinity.
        if (count == 0) return null;
        if (_processors.Length != count) _processors = new ProcessorPowerInformation[count];
        // ProcessorInformation is read-only. Neither a failure nor a missing performance reading may report the rated clock.
        var status = CallNtPowerInformation(11, IntPtr.Zero, 0, _processors,
            checked((uint)(_processors.Length * Marshal.SizeOf<ProcessorPowerInformation>())));
        return status == 0 ? EffectiveMhz(_processors, performancePercent) : null;
    }

    public static double? EffectiveMhz(ReadOnlySpan<ProcessorPowerInformation> processors, double? performancePercent)
    {
        if (processors.IsEmpty || performancePercent is not > 0 || !double.IsFinite(performancePercent.Value))
            return null;
        double total = 0;
        foreach (var processor in processors)
        {
            if (processor.MaxMhz == 0) return null;
            total += processor.MaxMhz;
        }

        // Not clamped to MaxMhz: boost runs above the rated clock.
        return total / processors.Length * performancePercent.Value / 100;
    }

    // https://learn.microsoft.com/windows/win32/power/processor-power-information-str
    [StructLayout(LayoutKind.Sequential)]
    public struct ProcessorPowerInformation
    {
        public uint Number, MaxMhz, CurrentMhz, MhzLimit, MaxIdleState, CurrentIdleState;
    }

    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern uint GetActiveProcessorCount(ushort group);

    [DllImport("powrprof.dll", ExactSpelling = true)]
    private static extern uint CallNtPowerInformation(int level, IntPtr input, uint inputLength,
        [Out] ProcessorPowerInformation[] output, uint outputLength);
}