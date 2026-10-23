using System.Diagnostics;

namespace WinMon.Monitoring;

/// <summary>Retain CPU observations within elapsed-time, sample-count and process-entry budgets.</summary>
public sealed class CpuHistory
{
    private readonly Queue<CpuSample> _samples = [];
    private readonly int _processEntryBudget;
    private int _entries;
    private CpuSample? _latest;

    /// <summary>Get the most recent retained observation without copying history.</summary>
    public CpuSample? Latest => _samples.Count == 0 ? null : _latest;

    /// <summary>Create a bounded CPU history for one-second observations.</summary>
    /// <param name="capacity">Maximum sample count and elapsed retention in seconds.</param>
    /// <param name="processEntryBudget">Maximum retained per-process measurements.</param>
    /// <exception cref="ArgumentOutOfRangeException">A bound is less than one.</exception>
    public CpuHistory(int capacity = 600, int processEntryBudget = 500_000)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(processEntryBudget, 1);
        Capacity = capacity;
        _processEntryBudget = processEntryBudget;
    }

    /// <summary>Get the maximum sample count and elapsed retention in seconds.</summary>
    public int Capacity { get; private set; }

    /// <summary>Get whether the process-entry budget shortened the recording window.</summary>
    public bool WasMemoryLimited { get; private set; }

    /// <summary>Get a stable copy of the retained chronological observations.</summary>
    /// <remarks>The copy is shared until history next changes, so one refresh does not copy history repeatedly.</remarks>
    public IReadOnlyList<CpuSample> Samples => _snapshot ??= [.. _samples];

    private CpuSample[]? _snapshot;

    /// <summary>Add an observation and evict old data within the configured budgets.</summary>
    /// <param name="sample">The observation to retain.</param>
    /// <exception cref="ArgumentNullException">The sample or process list is null.</exception>
    /// <exception cref="ArgumentException">A single observation exceeds the process-entry budget.</exception>
    public void Add(CpuSample sample)
    {
        ArgumentNullException.ThrowIfNull(sample);
        ArgumentNullException.ThrowIfNull(sample.Processes);
        if (sample.Processes.Count > _processEntryBudget)
            // Developer-facing argument validation; this message is not displayed in the UI.
            // ReSharper disable once LocalizableElement
            throw new ArgumentException("One sample exceeds the history process-entry budget.", nameof(sample));
        _samples.Enqueue(sample);
        _snapshot = null;
        _latest = sample;
        _entries += sample.Processes.Count;
        Trim(sample);
    }

    /// <summary>Change retention and immediately evict excess old observations.</summary>
    /// <param name="capacity">Maximum sample count and elapsed retention in seconds.</param>
    /// <exception cref="ArgumentOutOfRangeException">The capacity is less than one.</exception>
    public void Resize(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        Capacity = capacity;
        if (_samples.Count > 0) Trim(_samples.Last());
    }

    private void Trim(CpuSample latest)
    {
        while (_samples.Count > 0)
        {
            var oldest = _samples.Peek();
            var age = latest.MonotonicTimestamp > 0 && oldest.MonotonicTimestamp > 0
                ? (latest.MonotonicTimestamp - oldest.MonotonicTimestamp) / (double)Stopwatch.Frequency
                : (latest.Timestamp - oldest.Timestamp).TotalSeconds;
            var overBudget = _entries > _processEntryBudget;
            if (_samples.Count <= Capacity && age < Capacity && !overBudget) break;
            WasMemoryLimited |= overBudget;
            _entries -= _samples.Dequeue().Processes.Count;
            _snapshot = null;
        }
    }
}