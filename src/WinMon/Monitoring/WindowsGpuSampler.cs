using System.Buffers;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
// ReSharper disable StringLiteralTypo

namespace WinMon.Monitoring;

/// <summary>Report the busiest physical GPU engine across all adapters, without vendor dependencies.</summary>
internal sealed class WindowsGpuSampler : IDisposable
{
    private SafeQuery? _query;
    private IntPtr _counter;
    private long _timestamp;
    private long _collectionFileTime;
    // About 600 instance names repeat every second. Each is parsed once and reused while the instance exists.
    private Dictionary<string, EngineInstance?> _instances = new(StringComparer.Ordinal);
    private Dictionary<string, EngineInstance?> _nextInstances = new(StringComparer.Ordinal);
    private static readonly SearchValues<char> DecimalDigits = SearchValues.Create("0123456789");
    private static readonly SearchValues<char> HexDigits = SearchValues.Create("0123456789abcdefABCDEF");
    private static readonly int ValueOffset = (int)Marshal.OffsetOf<CounterItem>(nameof(CounterItem.Value));

    internal CpuSample Sample(CpuSample sample, bool reset)
    {
        if (reset) Reset();
        try
        {
            if (_query is null)
            {
                Check(Native.PdhOpenQueryW(null, UIntPtr.Zero, out var opened));
                _query = opened;
                Check(Native.PdhAddEnglishCounterW(_query, @"\GPU Engine(*)\Utilization Percentage", UIntPtr.Zero, out _counter));
            }
            Check(Native.PdhCollectQueryData(_query));
            var intervalStart = _collectionFileTime;
            _collectionFileTime = DateTime.UtcNow.ToFileTimeUtc();
            var now = Stopwatch.GetTimestamp();
            var seconds = (now - _timestamp) / (double)Stopwatch.Frequency;
            var validInterval = _timestamp != 0 && seconds is > 0 and <= 5;
            _timestamp = now;
            // Formatting a rate before its second collection can fail. Keep the query
            // alive so that failure cannot turn every pass into another first sample.
            if (!validInterval) return sample;
            var readings = Parse(ReadCounters(), _instances, _nextInstances);
            (_instances, _nextInstances) = (_nextInstances, _instances);
            var busiest = Busiest(readings);
            var perProcess = ByProcess(readings);
            return sample with
            {
                Resources = busiest is null ? sample.Resources : sample.Resources with
                {
                    GpuPercent = busiest.Percent, GpuId = busiest.Adapter, GpuEngine = busiest.Engine
                },
                Processes = AttachProcesses(sample.Processes, perProcess, intervalStart, ReadCreationTime)
            };
        }
        catch (Win32Exception ex)
        {
            Trace.TraceWarning("GPU counters unavailable: {0}", ex.Message);
            Reset();
            return sample;
        }
    }

    /// <summary>Busiest physical engine across all adapters, or null when no engine has a valid reading.</summary>
    internal static GpuUsage? Aggregate(IEnumerable<EngineCounter> counters) => Busiest(Parse(counters));

    /// <summary>Each process's busiest engine; null for a process whose engines have no valid reading.</summary>
    internal static IReadOnlyDictionary<int, GpuUsage?> AggregateProcesses(IEnumerable<EngineCounter> counters) => ByProcess(Parse(counters));

    /// <summary>Pair counter values with parsed instance names, dropping names that are not engine instances.</summary>
    /// <param name="counters">Values from one collection.</param>
    /// <param name="previous">Names parsed in the last pass, reused when they recur.</param>
    /// <param name="next">Cleared and filled with this pass's names.</param>
    internal static List<EngineReading> Parse(IEnumerable<EngineCounter> counters,
        Dictionary<string, EngineInstance?>? previous = null, Dictionary<string, EngineInstance?>? next = null)
    {
        next?.Clear();
        var readings = new List<EngineReading>(counters.TryGetNonEnumeratedCount(out var count) ? count : 0);
        foreach (var counter in counters)
        {
            if (previous is null || !previous.TryGetValue(counter.Name, out var instance)) instance = ParseInstance(counter.Name);
            next?[counter.Name] = instance;
            if (instance is null) continue;
            var valid = counter.Status is 0 or 1 && double.IsFinite(counter.Value) && counter.Value >= 0;
            readings.Add(new EngineReading(instance, counter.Value, valid));
        }
        return readings;
    }

    /// <summary>Parse <c>pid_N_luid_0xH_0xL_phys_N_eng_N_engtype_T</c>, ignoring case; null for any other name.</summary>
    internal static EngineInstance? ParseInstance(string name)
    {
        var rest = name.AsSpan();
        if (!Literal(ref rest, "pid_") || !Run(ref rest, DecimalDigits, out var pid) || !Literal(ref rest, "_luid_")) return null;
        var luidStart = name.Length - rest.Length;
        if (!Literal(ref rest, "0x") || !Run(ref rest, HexDigits, out _) || !Literal(ref rest, "_0x") || !Run(ref rest, HexDigits, out _)) return null;
        var luid = name.AsSpan(luidStart, name.Length - rest.Length - luidStart);
        if (!Literal(ref rest, "_phys_") || !Run(ref rest, DecimalDigits, out var physical) ||
            !Literal(ref rest, "_eng_") || !Run(ref rest, DecimalDigits, out var engine) || !Literal(ref rest, "_engtype_")) return null;
        // The engine type runs to the end of the name: at least one character, no line breaks.
        if (rest.EndsWith('\n')) rest = rest[..^1];
        if (rest.IsEmpty || rest.Contains('\n')) return null;
        // An out-of-range PID still describes a physical engine, so it counts towards the busiest engine.
        int? processId = int.TryParse(pid, NumberStyles.None, CultureInfo.InvariantCulture, out var id) ? id : null;
        return new EngineInstance(processId, $"{luid.ToString().ToLowerInvariant()}/{physical}", engine.ToString(), rest.ToString());

        static bool Literal(ref ReadOnlySpan<char> text, string literal)
        {
            if (!text.StartsWith(literal, StringComparison.OrdinalIgnoreCase)) return false;
            text = text[literal.Length..];
            return true;
        }

        static bool Run(ref ReadOnlySpan<char> text, SearchValues<char> allowed, out ReadOnlySpan<char> run)
        {
            var end = text.IndexOfAnyExcept(allowed);
            run = end < 0 ? text : text[..end];
            text = text[run.Length..];
            return !run.IsEmpty;
        }
    }

    // Instances describe process contributions. Sum within each physical engine first;
    // independent engines/adapters must never be added together.
    private static GpuUsage? Busiest(List<EngineReading> readings)
    {
        var engines = new Dictionary<(string Adapter, string Engine), EngineTotal>();
        foreach (var reading in readings)
        {
            var key = (reading.Instance.Adapter, reading.Instance.Engine);
            engines[key] = (engines.TryGetValue(key, out var total) ? total : EngineTotal.Start(reading.Instance)).Add(reading);
        }
        EngineTotal? busiest = null;
        foreach (var total in engines.Values)
            if (total.Valid && (busiest is not { } current || total.Precedes(current))) busiest = total;
        return busiest?.ToUsage();
    }

    private static Dictionary<int, GpuUsage?> ByProcess(List<EngineReading> readings)
    {
        var engines = new Dictionary<(int ProcessId, string Adapter, string Engine), EngineTotal>();
        foreach (var reading in readings)
        {
            if (reading.Instance.ProcessId is not { } processId) continue;
            var key = (processId, reading.Instance.Adapter, reading.Instance.Engine);
            engines[key] = (engines.TryGetValue(key, out var total) ? total : EngineTotal.Start(reading.Instance)).Add(reading);
        }
        var busiest = new Dictionary<int, EngineTotal?>();
        foreach (var (key, total) in engines)
        {
            busiest.TryGetValue(key.ProcessId, out var current);
            busiest[key.ProcessId] = total.Valid && (current is not { } best || total.Precedes(best)) ? total : current;
        }
        var result = new Dictionary<int, GpuUsage?>(busiest.Count);
        foreach (var (processId, total) in busiest) result[processId] = total?.ToUsage();
        return result;
    }

    internal static IReadOnlyList<ProcessUsage> AttachProcesses(IReadOnlyList<ProcessUsage> processes,
        IReadOnlyDictionary<int, GpuUsage?> readings, long intervalStart, Func<int, long?> creationTime)
    {
        return
        [
            .. processes.Select(process =>
            {
                // No engine instance is not proof of idle. Only attribute counters when this
                // process lifetime spans the baseline and still owns the PID after collection.
                var gpu = readings.GetValueOrDefault(process.ProcessId);
                if (gpu is null || process.CreationTicks > intervalStart ||
                    creationTime(process.ProcessId) != process.CreationTicks) return process;
                return process with { GpuPercent = gpu.Percent, GpuEngine = gpu.Engine, GpuId = gpu.Adapter };
            })
        ];
    }

    private static long? ReadCreationTime(int pid)
    {
        using var handle = Native.OpenProcess(0x1000, false, pid);
        return !handle.IsInvalid && Native.GetProcessTimes(handle, out var created, out _, out _, out _) ? created : null;
    }

    private EngineCounter[] ReadCounters()
    {
        // Instance counts can change between sizing and reading. Bound retries and allocation.
        for (var attempt = 0; attempt < 3; attempt++)
        {
            uint bytes = 0;
            var status = Native.PdhGetFormattedCounterArrayW(_counter, 0x200 | 0x8000, ref bytes, out _, IntPtr.Zero);
            if (status == 0 && bytes == 0) return [];
            if (status != 0x800007D2) Check(status); // PDH_MORE_DATA
            if (bytes is 0 or > 16 * 1024 * 1024) throw new Win32Exception("GPU counter data exceeds the collection limit.");
            var allocated = bytes;
            var buffer = Marshal.AllocHGlobal(checked((int)bytes));
            try
            {
                status = Native.PdhGetFormattedCounterArrayW(_counter, 0x200 | 0x8000, ref bytes, out var count, buffer);
                if (status == 0x800007D2) continue;
                Check(status);
                var stride = Marshal.SizeOf<CounterItem>();
                if ((ulong)count * (uint)stride > allocated) throw new Win32Exception("Invalid GPU counter array.");
                var result = new EngineCounter[count];
                for (var i = 0; i < result.Length; i++)
                {
                    // Read fields in place; PtrToStructure would box every item.
                    var item = IntPtr.Add(buffer, i * stride);
                    result[i] = new EngineCounter(Marshal.PtrToStringUni(Marshal.ReadIntPtr(item)) ?? "",
                        BitConverter.Int64BitsToDouble(Marshal.ReadInt64(item, ValueOffset + 8)),
                        unchecked((uint)Marshal.ReadInt32(item, ValueOffset)));
                }
                return result;
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }
        throw new Win32Exception("GPU counter instances changed during collection.");
    }

    private static void Check(uint status)
    {
        if (status != 0) throw new Win32Exception(unchecked((int)status));
    }

    internal void Reset()
    {
        _query?.Dispose();
        _query = null;
        _counter = IntPtr.Zero;
        _timestamp = 0;
        _collectionFileTime = 0;
    }

    public void Dispose() => Reset();
    internal readonly record struct EngineCounter(string Name, double Value, uint Status = 0);
    internal sealed record GpuUsage(string Adapter, string Engine, double Percent);

    /// <summary>A parsed engine instance name. Adapter is the lower-case LUID and physical index, such as <c>0x0_0x1234/0</c>.</summary>
    /// <param name="ProcessId">Owning process, or null when the PID does not fit an <see cref="int"/>.</param>
    internal sealed record EngineInstance(int? ProcessId, string Adapter, string Engine, string EngineType);

    internal readonly record struct EngineReading(EngineInstance Instance, double Value, bool Valid);

    /// <summary>One physical engine's summed contributions; any invalid contribution invalidates the engine.</summary>
    private readonly record struct EngineTotal(string Adapter, string Engine, string EngineType, double Total, bool Valid)
    {
        internal static EngineTotal Start(EngineInstance instance) => new(instance.Adapter, instance.Engine, instance.EngineType, 0, true);

        internal EngineTotal Add(EngineReading reading) =>
            this with { Total = Total + (reading.Valid ? reading.Value : 0), Valid = Valid && reading.Valid };

        // Busiest first; ties fall to adapter, then engine, in ordinal order, so selection is stable.
        internal bool Precedes(EngineTotal other)
        {
            if (Math.Abs(Total - other.Total) > 0.001) return Total > other.Total;
            var adapter = string.CompareOrdinal(Adapter, other.Adapter);
            return adapter != 0 ? adapter < 0 : string.CompareOrdinal(Engine, other.Engine) < 0;
        }

        internal GpuUsage ToUsage() => new(Adapter, EngineType, Math.Clamp(Total, 0, 100));
    }

    // The P/Invoke marshaller constructs this handle for PdhOpenQueryW.
    // ReSharper disable once ClassNeverInstantiated.Local
    private sealed class SafeQuery() : SafeHandleZeroOrMinusOneIsInvalid(true)
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
    private struct CounterItem
    {
        public IntPtr Name;
        public CounterValue Value;
    }

    private static class Native
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern SafeProcessHandle OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, int pid);
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetProcessTimes(SafeProcessHandle process, out long created, out long exited, out long kernel, out long user);
        [DllImport("pdh.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        internal static extern uint PdhOpenQueryW(string? source, UIntPtr userData, out SafeQuery query);
        [DllImport("pdh.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        internal static extern uint PdhAddEnglishCounterW(SafeQuery query, string path, UIntPtr userData, out IntPtr counter);
        [DllImport("pdh.dll", ExactSpelling = true)]
        internal static extern uint PdhCollectQueryData(SafeQuery query);
        [DllImport("pdh.dll", ExactSpelling = true)]
        internal static extern uint PdhGetFormattedCounterArrayW(IntPtr counter, uint format, ref uint bytes, out uint count, IntPtr buffer);
        [DllImport("pdh.dll", ExactSpelling = true)]
        internal static extern uint PdhCloseQuery(IntPtr query);
    }
}
