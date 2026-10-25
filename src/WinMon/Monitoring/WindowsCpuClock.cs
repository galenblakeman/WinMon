using System.Runtime.InteropServices;

// ReSharper disable StringLiteralTypo

namespace WinMon.Monitoring;

/// <summary>Windows-reported current clock, averaged across logical processors.</summary>
internal sealed class WindowsCpuClock
{
    private ProcessorPowerInformation[] _processors = [];

    /// <summary>
    /// Logical processors in all groups. Unlike <see cref="Environment.ProcessorCount"/>, this ignores WinMon's own
    /// affinity and job CPU limits, so it matches the machine-wide CPU percentages the sampler reports.
    /// </summary>
    internal static int LogicalProcessorCount => Math.Max(1, (int)GetActiveProcessorCount(0xFFFF));

    internal double? ReadMhz()
    {
        var count = GetActiveProcessorCount(0xFFFF); // All processor groups, independent of process affinity.
        if (count == 0) return null;
        if (_processors.Length != count) _processors = new ProcessorPowerInformation[count];
        // ProcessorInformation is read-only. Failure must never substitute the rated maximum clock.
        var status = CallNtPowerInformation(11, IntPtr.Zero, 0, _processors,
            checked((uint)(_processors.Length * Marshal.SizeOf<ProcessorPowerInformation>())));
        return status == 0 ? AverageMhz(_processors) : null;
    }

    internal static double? AverageMhz(ReadOnlySpan<ProcessorPowerInformation> processors)
    {
        if (processors.IsEmpty) return null;
        double total = 0;
        foreach (var processor in processors)
        {
            if (processor.CurrentMhz == 0) return null;
            total += processor.CurrentMhz;
        }

        return total / processors.Length;
    }

    // https://learn.microsoft.com/windows/win32/power/processor-power-information-str
    [StructLayout(LayoutKind.Sequential)]
    internal struct ProcessorPowerInformation
    {
        public uint Number, MaxMhz, CurrentMhz, MhzLimit, MaxIdleState, CurrentIdleState;
    }

    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern uint GetActiveProcessorCount(ushort group);

    [DllImport("powrprof.dll", ExactSpelling = true)]
    private static extern uint CallNtPowerInformation(int level, IntPtr input, uint inputLength,
        [Out] ProcessorPowerInformation[] output, uint outputLength);
}
