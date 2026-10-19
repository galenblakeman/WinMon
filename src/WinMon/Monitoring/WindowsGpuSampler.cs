using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Microsoft.Win32.SafeHandles;

namespace WinMon.Monitoring;

/// <summary>Report the busiest physical GPU engine across all adapters, without vendor dependencies.</summary>
internal sealed class WindowsGpuSampler : IDisposable
{
    private SafeQuery? _query;
    private IntPtr _counter;
    private long _timestamp;
    private long _collectionFileTime;
    private static readonly Regex Instance = new(
        @"^pid_(?<pid>\d+)_luid_(0x[0-9a-f]+_0x[0-9a-f]+)_phys_(\d+)_eng_(\d+)_engtype_(.+)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

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
            var observations = ReadCounters();
            var busiest = Aggregate(observations);
            var perProcess = AggregateProcesses(observations);
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

    internal static IReadOnlyDictionary<int, GpuUsage?> AggregateProcesses(IEnumerable<EngineCounter> counters) =>
        counters.Select(c => (Counter: c, Match: Instance.Match(c.Name)))
            .Where(x => x.Match.Success && int.TryParse(x.Match.Groups["pid"].Value, out _))
            .GroupBy(x => int.Parse(x.Match.Groups["pid"].Value, System.Globalization.CultureInfo.InvariantCulture))
            .ToDictionary(g => g.Key, g => Aggregate(g.Select(x => x.Counter)));

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

    // Instances describe process contributions. Sum within each physical engine first;
    // independent engines/adapters must never be added together.
    internal static GpuUsage? Aggregate(IEnumerable<EngineCounter> counters)
    {
        var engines = new Dictionary<(string Adapter, string Engine), (string Name, double Total, bool Valid)>();
        foreach (var counter in counters)
        {
            var match = Instance.Match(counter.Name);
            if (!match.Success) continue;
            var adapter = match.Groups[1].Value.ToLowerInvariant() + "/" + match.Groups[2].Value;
            var key = (adapter, match.Groups[3].Value);
            var value = engines.GetValueOrDefault(key, (match.Groups[4].Value, 0d, true));
            var valid = counter.Status is 0 or 1 && double.IsFinite(counter.Value) && counter.Value >= 0;
            engines[key] = (value.Item1, value.Item2 + (valid ? counter.Value : 0), value.Item3 && valid);
        }
        return engines.Where(x => x.Value.Valid)
            .OrderByDescending(x => x.Value.Total).ThenBy(x => x.Key.Adapter, StringComparer.Ordinal)
            .ThenBy(x => x.Key.Engine, StringComparer.Ordinal)
            .Select(x => new GpuUsage(x.Key.Adapter, x.Value.Name, Math.Clamp(x.Value.Total, 0, 100))).FirstOrDefault();
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
                    var item = Marshal.PtrToStructure<CounterItem>(IntPtr.Add(buffer, i * stride));
                    result[i] = new EngineCounter(Marshal.PtrToStringUni(item.Name) ?? "", item.Value.Value, item.Value.Status);
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
    internal sealed record EngineCounter(string Name, double Value, uint Status = 0);
    internal sealed record GpuUsage(string Adapter, string Engine, double Percent);

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
