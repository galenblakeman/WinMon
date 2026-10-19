using System.Diagnostics;

namespace WinMon.Monitoring;

/// <summary>Retain CPU observations within elapsed-time, sample-count and process-entry budgets.</summary>
public sealed class CpuHistory
{
    private readonly Queue<CpuSample> samples = [];
    private readonly int processEntryBudget;
    private int entries;
    private CpuSample? latest;

    /// <summary>Get the most recent retained observation without copying history.</summary>
    public CpuSample? Latest => samples.Count == 0 ? null : latest;

    /// <summary>Create a bounded CPU history for one-second observations.</summary>
    /// <param name="capacity">Maximum sample count and elapsed retention in seconds.</param>
    /// <param name="processEntryBudget">Maximum retained per-process measurements.</param>
    /// <exception cref="ArgumentOutOfRangeException">A bound is less than one.</exception>
    public CpuHistory(int capacity = 600, int processEntryBudget = 500_000)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(processEntryBudget, 1);
        Capacity = capacity;
        this.processEntryBudget = processEntryBudget;
    }

    /// <summary>Get the maximum sample count and elapsed retention in seconds.</summary>
    public int Capacity { get; private set; }

    /// <summary>Get whether the process-entry budget shortened the recording window.</summary>
    public bool WasMemoryLimited { get; private set; }

    /// <summary>Get a stable copy of the retained chronological observations.</summary>
    public IReadOnlyList<CpuSample> Samples => samples.ToArray();

    /// <summary>Add an observation and evict old data within the configured budgets.</summary>
    /// <param name="sample">The observation to retain.</param>
    /// <exception cref="ArgumentNullException">The sample or process list is null.</exception>
    /// <exception cref="ArgumentException">A single observation exceeds the process-entry budget.</exception>
    public void Add(CpuSample sample)
    {
        ArgumentNullException.ThrowIfNull(sample);
        ArgumentNullException.ThrowIfNull(sample.Processes);
        if (sample.Processes.Count > processEntryBudget)
            throw new ArgumentException("One sample exceeds the history process-entry budget.", nameof(sample));
        samples.Enqueue(sample);
        latest = sample;
        entries += sample.Processes.Count;
        Trim(sample);
    }

    /// <summary>Change retention and immediately evict excess old observations.</summary>
    /// <param name="capacity">Maximum sample count and elapsed retention in seconds.</param>
    /// <exception cref="ArgumentOutOfRangeException">The capacity is less than one.</exception>
    public void Resize(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        Capacity = capacity;
        if (samples.Count > 0) Trim(samples.Last());
    }

    private void Trim(CpuSample latest)
    {
        while (samples.Count > 0)
        {
            var oldest = samples.Peek();
            var age = latest.MonotonicTimestamp > 0 && oldest.MonotonicTimestamp > 0
                ? (latest.MonotonicTimestamp - oldest.MonotonicTimestamp) / (double)Stopwatch.Frequency
                : (latest.Timestamp - oldest.Timestamp).TotalSeconds;
            var overBudget = entries > processEntryBudget;
            if (samples.Count <= Capacity && age < Capacity && !overBudget) break;
            WasMemoryLimited |= overBudget;
            entries -= samples.Dequeue().Processes.Count;
        }
    }
}
