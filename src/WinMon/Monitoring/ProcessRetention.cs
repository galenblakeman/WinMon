namespace WinMon.Monitoring;

/// <summary>Bound history memory by keeping only the process entries that ranking and sparklines can use.</summary>
/// <remarks>
/// A sample lists about a thousand processes, but in any one second most read zero CPU, I/O and GPU.
/// Those are dropped and counted in <see cref="CpuSample.CompactedProcesses"/>, except the largest working
/// sets and largest thread/handle counts, which point-in-time ranking needs.
/// Averages treat a dropped process as zero for that interval.
/// Processes that have shown an app window are also retained, including later background observations.
/// </remarks>
internal static class ProcessRetention
{
    /// <summary>Largest working sets kept per sample; above the 20-row maximum, so memory ranking stays exact.</summary>
    internal const int LargestByMemory = 32;

    internal static CpuSample Compact(CpuSample sample, int largestByMemory = LargestByMemory)
    {
        ArgumentNullException.ThrowIfNull(sample);
        if (sample.Processes.Count <= largestByMemory) return sample;
        // A small min-heap finds the Nth-largest working set without sorting every process each second.
        var largest = new PriorityQueue<ProcessUsage, ulong>(largestByMemory + 1);
        foreach (var process in sample.Processes)
        {
            if (process.WorkingSetBytes is not { } bytes) continue;
            largest.Enqueue(process, bytes);
            if (largest.Count > largestByMemory) largest.Dequeue();
        }
        // Ties at the cut-off are all kept, so ranking never depends on which equal entry survived.
        var threshold = largest.Count == largestByMemory && largest.TryPeek(out _, out var smallest) ? smallest : 0UL;
        var threadThreshold = CountThreshold(sample.Processes, largestByMemory, threads: true);
        var handleThreshold = CountThreshold(sample.Processes, largestByMemory, threads: false);
        ProcessUsage[] kept =
        [
            .. sample.Processes.Where(p => p.HasHadAppWindow || p.HasAppWindow == true || p.CpuPercent > 0 || p.IoBytesPerSecond > 0 || p.GpuPercent is not null ||
                p.WorkingSetBytes >= threshold || p.ThreadCount >= threadThreshold || p.HandleCount >= handleThreshold)
        ];
        return kept.Length == sample.Processes.Count
            ? sample
            : sample with { Processes = kept, CompactedProcesses = sample.CompactedProcesses + sample.Processes.Count - kept.Length };
    }

    // Keep the largest counts even for CPU-idle processes, so historical count ranking has its top rows.
    private static long CountThreshold(IReadOnlyList<ProcessUsage> processes, int count, bool threads)
    {
        var largest = new PriorityQueue<long, long>(count + 1);
        foreach (var process in processes)
        {
            if ((threads ? process.ThreadCount : process.HandleCount) is not { } value) continue;
            largest.Enqueue(value, value);
            if (largest.Count > count) largest.Dequeue();
        }
        return largest.Count == count && largest.TryPeek(out var smallest, out _) ? smallest : 0;
    }
}
