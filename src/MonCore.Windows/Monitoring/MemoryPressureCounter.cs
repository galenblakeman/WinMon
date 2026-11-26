using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace MonCore.Windows.Monitoring;

internal readonly record struct MemoryPressureRaw(double? PagingReadsPerSecond, ulong? StandbyCoreBytes,
    ulong? StandbyNormalBytes, ulong? StandbyReserveBytes, ulong? ModifiedBytes, ulong? FreeBytes,
    double? ProcessorPerformancePercent = null, double? ProcessorPerformanceLimitPercent = null,
    double? ProcessorPerformanceLimitFlags = null, bool Collected = true)
{
    public IReadOnlyList<ThermalZoneRaw> ThermalZones { get; init; } = [];
}

internal readonly record struct MemoryPressureReading(double? PagingReadsPerSecond, ulong? StandbyBytes,
    ulong? ModifiedBytes, ulong? FreeBytes, double? ProcessorPerformancePercent = null,
    double? ProcessorPerformanceLimitPercent = null, uint? ProcessorPerformanceLimitFlags = null)
{
    public IReadOnlyList<ThermalZoneRaw> ThermalZones { get; init; } = [];
}

internal readonly record struct ThermalZoneRaw(string Name, double Kelvin, bool HighPrecision);

internal interface IMemoryPressurePdh : IDisposable
{
    MemoryPressureRaw Collect();
    void Reset();
}

/// <summary>
/// Turns one PDH memory query into nullable readings without allowing missing optional counters to fail a frame.
/// The query also carries the CPU clock's processor-performance rate, so sampling opens no second query.
/// </summary>
internal sealed class MemoryPressureCounter(IMemoryPressurePdh? pdh = null, Func<long>? timestamp = null) : IDisposable
{
    private readonly IMemoryPressurePdh _pdh = pdh ?? new NativeMemoryPressurePdh();
    private readonly Func<long> _timestamp = timestamp ?? Stopwatch.GetTimestamp;
    private bool _hasBaseline;
    private long _previousTimestamp;

    public MemoryPressureReading Read()
    {
        var raw = _pdh.Collect();
        if (!raw.Collected)
        {
            _hasBaseline = false;
            _previousTimestamp = 0;
            return default;
        }
        var current = _timestamp();
        var validRate = _hasBaseline && (current - _previousTimestamp) / (double)Stopwatch.Frequency is > 0 and <= 5;
        _previousTimestamp = current;
        _hasBaseline = true;
        return new MemoryPressureReading(
            validRate ? ValidRate(raw.PagingReadsPerSecond) : null,
            Sum(raw.StandbyCoreBytes, raw.StandbyNormalBytes, raw.StandbyReserveBytes), raw.ModifiedBytes, raw.FreeBytes,
            validRate ? ValidRate(raw.ProcessorPerformancePercent) : null,
            validRate ? ValidPercent(raw.ProcessorPerformanceLimitPercent) : null,
            validRate ? ValidFlags(raw.ProcessorPerformanceLimitFlags) : null)
        { ThermalZones = raw.ThermalZones };
    }

    public void Reset()
    {
        _hasBaseline = false;
        _previousTimestamp = 0;
        _pdh.Reset();
    }

    public void Dispose() => _pdh.Dispose();

    private static ulong? Sum(ulong? first, ulong? second, ulong? third)
    {
        if (first is not { } a || second is not { } b || third is not { } c) return null;
        try { return checked(a + b + c); }
        catch (OverflowException) { return null; }
    }

    private static double? ValidRate(double? value) => value is { } rate && double.IsFinite(rate) && rate >= 0
        ? rate : null;

    private static double? ValidPercent(double? value) => value is { } percent && double.IsFinite(percent) &&
        percent is >= 0 and <= 100 ? percent : null;

    private static uint? ValidFlags(double? value) => value is { } flags && double.IsFinite(flags) &&
        flags is >= 0 and <= uint.MaxValue && Math.Truncate(flags) == flags ? (uint)flags : null;

    internal static IReadOnlyList<ThermalZoneRaw> SelectThermalZones(IReadOnlyList<ThermalZoneRaw> high,
        IReadOnlyList<ThermalZoneRaw> coarse)
    {
        if (high.Count == 0 && coarse.Count == 0) return [];
        var result = new List<ThermalZoneRaw>(high.Count + coarse.Count);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var zone in high)
            if (!string.IsNullOrWhiteSpace(zone.Name) && WindowsThermalZones.ToCelsius(zone.Kelvin, zone.HighPrecision) is not null && names.Add(zone.Name))
                result.Add(zone);
        foreach (var zone in coarse)
            if (!string.IsNullOrWhiteSpace(zone.Name) && WindowsThermalZones.ToCelsius(zone.Kelvin, zone.HighPrecision) is not null && names.Add(zone.Name))
                result.Add(zone);
        return result;
    }

    private sealed class NativeMemoryPressurePdh : IMemoryPressurePdh
    {
        private static readonly long RetryDelay = 30 * Stopwatch.Frequency;
        private SafeMemoryPdhQuery? _query;
        private IntPtr _pagingReads, _standbyCore, _standbyNormal, _standbyReserve, _modified, _free, _performance,
            _performanceLimit, _performanceLimitFlags, _thermalHigh, _thermalCoarse;
        private long _retryAfter;

        public MemoryPressureRaw Collect()
        {
            if (!EnsureQuery()) return default;
            var status = Native.PdhCollectQueryData(_query!);
            if (status != 0)
            {
                Fail(status);
                return default;
            }

            var high = ReadZones(_thermalHigh, true);
            var coarse = ReadZones(_thermalCoarse, false);
            return new MemoryPressureRaw(Read(_pagingReads), Bytes(_standbyCore), Bytes(_standbyNormal),
                Bytes(_standbyReserve), Bytes(_modified), Bytes(_free), Read(_performance), Read(_performanceLimit),
                Read(_performanceLimitFlags)) { ThermalZones = SelectThermalZones(high, coarse) };
        }

        public void Reset()
        {
            _query?.Dispose();
            _query = null;
            _pagingReads = _standbyCore = _standbyNormal = _standbyReserve = _modified = _free = _performance =
                _performanceLimit = _performanceLimitFlags = _thermalHigh = _thermalCoarse = IntPtr.Zero;
            _retryAfter = 0;
        }

        public void Dispose() => Reset();

        private bool EnsureQuery()
        {
            if (_query is not null) return true;
            if (Stopwatch.GetTimestamp() < _retryAfter) return false;
            var status = Native.PdhOpenQueryW(null, UIntPtr.Zero, out var query);
            if (status != 0)
            {
                query.Dispose();
                _retryAfter = Stopwatch.GetTimestamp() + RetryDelay;
                Trace.TraceWarning("Memory pressure counters unavailable: 0x{0:X8}", status);
                return false;
            }

            _query = query;
            _pagingReads = AddOptional(query, @"\Memory\Page Reads/sec");
            _standbyCore = AddOptional(query, @"\Memory\Standby Cache Core Bytes");
            _standbyNormal = AddOptional(query, @"\Memory\Standby Cache Normal Priority Bytes");
            _standbyReserve = AddOptional(query, @"\Memory\Standby Cache Reserve Bytes");
            _modified = AddOptional(query, @"\Memory\Modified Page List Bytes");
            _free = AddOptional(query, @"\Memory\Free & Zero Page List Bytes");
            // Can exceed 100 while boosting; Read passes PDH_FMT_NOCAP100 so it is not clipped.
            _performance = AddOptional(query, @"\Processor Information(_Total)\% Processor Performance");
            _performanceLimit = AddOptional(query, @"\Processor Information(_Total)\% Performance Limit");
            _performanceLimitFlags = AddOptional(query, @"\Processor Information(_Total)\Performance Limit Flags");
            _thermalHigh = AddOptional(query, @"\Thermal Zone Information(*)\High Precision Temperature");
            _thermalCoarse = AddOptional(query, @"\Thermal Zone Information(*)\Temperature");
            return true;
        }

        private void Fail(uint status)
        {
            Trace.TraceWarning("Memory pressure counters unavailable: 0x{0:X8}", status);
            Reset();
            _retryAfter = Stopwatch.GetTimestamp() + RetryDelay;
        }

        private static IntPtr AddOptional(SafeMemoryPdhQuery query, string path)
        {
            var status = Native.PdhAddEnglishCounterW(query, path, UIntPtr.Zero, out var counter);
            if (status == 0) return counter;
            Trace.TraceWarning("Optional PDH counter unavailable ({0}): 0x{1:X8}", path, status);
            return IntPtr.Zero;
        }

        private static double? Read(IntPtr counter)
        {
            if (counter == IntPtr.Zero) return null;
            var status = Native.PdhGetFormattedCounterValue(counter, PdhFmtDouble | PdhFmtNoScale | PdhFmtNoCap100, out _, out var value);
            return status == 0 && value.Status is 0 or 1 && double.IsFinite(value.Value) && value.Value >= 0
                ? value.Value : null;
        }

        private static ulong? Bytes(IntPtr counter)
        {
            var value = Read(counter);
            return value is >= 0 and <= ulong.MaxValue ? (ulong)value.Value : null;
        }

        private static IReadOnlyList<ThermalZoneRaw> ReadZones(IntPtr counter, bool highPrecision)
        {
            if (counter == IntPtr.Zero) return [];
            uint bytes = 0;
            var status = Native.PdhGetFormattedCounterArrayW(counter, PdhFmtDouble, ref bytes, out _, IntPtr.Zero);
            if (status == 0 && bytes == 0) return [];
            if (status != 0x800007D2 || bytes is 0 or > 1024 * 1024) return [];
            var buffer = Marshal.AllocHGlobal(checked((int)bytes));
            try
            {
                status = Native.PdhGetFormattedCounterArrayW(counter, PdhFmtDouble, ref bytes, out var count, buffer);
                if (status != 0 || count > 4096 || (ulong)count * (uint)Marshal.SizeOf<CounterItem>() > bytes) return [];
                var result = new List<ThermalZoneRaw>((int)count);
                var stride = Marshal.SizeOf<CounterItem>();
                for (var i = 0; i < count; i++)
                {
                    var item = IntPtr.Add(buffer, checked((int)i * stride));
                    var value = Marshal.PtrToStructure<CounterItem>(item);
                    if (value.Value.Status is 0 or 1 && double.IsFinite(value.Value.Value))
                        result.Add(new ThermalZoneRaw(Marshal.PtrToStringUni(value.Name) ?? "", value.Value.Value, highPrecision));
                }
                return result;
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }

        private const uint PdhFmtDouble = 0x00000200;
        // Preserve unscaled byte/rate values and avoid clipping processor boost at 100.
        private const uint PdhFmtNoScale = 0x00001000;
        private const uint PdhFmtNoCap100 = 0x00008000;

        private sealed class SafeMemoryPdhQuery() : SafeHandleZeroOrMinusOneIsInvalid(true)
        {
            protected override bool ReleaseHandle() => Native.PdhCloseQuery(handle) == 0;
        }

        [StructLayout(LayoutKind.Explicit, Size = 16)]
        private struct CounterValue
        {
            [FieldOffset(0)] public uint Status;
            [FieldOffset(8)] public double Value;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct CounterItem { public IntPtr Name; public CounterValue Value; }

        private static class Native
        {
            [DllImport("pdh.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
            internal static extern uint PdhOpenQueryW(string? source, UIntPtr userData, out SafeMemoryPdhQuery query);

            [DllImport("pdh.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
            internal static extern uint PdhAddEnglishCounterW(SafeMemoryPdhQuery query, string path, UIntPtr userData,
                out IntPtr counter);

            [DllImport("pdh.dll", ExactSpelling = true)]
            internal static extern uint PdhCollectQueryData(SafeMemoryPdhQuery query);

            [DllImport("pdh.dll", ExactSpelling = true)]
            internal static extern uint PdhGetFormattedCounterValue(IntPtr counter, uint format, out uint type,
                out CounterValue value);

            [DllImport("pdh.dll", ExactSpelling = true)]
            internal static extern uint PdhGetFormattedCounterArrayW(IntPtr counter, uint format, ref uint bytes,
                out uint count, IntPtr buffer);

            [DllImport("pdh.dll", ExactSpelling = true)]
            internal static extern uint PdhCloseQuery(IntPtr query);
        }
    }
}
