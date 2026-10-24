namespace WinMon.Monitoring;

internal enum PinnedProcessState
{
    Present,
    Exited,
    Unavailable,
    NotRecorded
}

internal sealed record PinnedProcess(ProcessUsage Usage, PinnedProcessState State);

/// <summary>Pins in insertion order. Individual pins identify one process lifetime.</summary>
internal sealed class ProcessPins
{
    private sealed record Pin(ProcessUsage Usage, bool Grouped);

    private readonly List<Pin> _pins = [];

    internal int Count(bool grouped)
    {
        return _pins.Count(p => p.Grouped == grouped);
    }

    internal bool Contains(ProcessUsage process, bool grouped)
    {
        return _pins.Any(p => Matches(p, process, grouped));
    }

    internal SavedProcessPin[] Snapshot()
    {
        return
        [
            .. _pins.Select(pin => new SavedProcessPin(
                pin.Usage.ProcessId, pin.Usage.CreationTicks, pin.Usage.Name, pin.Usage.ImagePath,
                pin.Usage.AppName, pin.Usage.HasAppWindow, pin.Grouped))
        ];
    }

    internal void Restore(IEnumerable<SavedProcessPin>? saved)
    {
        _pins.Clear();
        // Deserialized preferences can hold nulls that the annotations do not admit.
        // ReSharper disable ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract, NullCoalescingConditionIsAlwaysNotNullAccordingToAPIContract
        foreach (var pin in saved ?? [])
        {
            if (pin is null || pin.ProcessId < 0 || pin.CreationTicks < 0 ||
                string.IsNullOrWhiteSpace(pin.Name)) continue;
            var usage = new ProcessUsage(pin.ProcessId, pin.CreationTicks, pin.Name, 0)
            {
                ImagePath = pin.ImagePath ?? "", AppName = pin.AppName ?? "", HasAppWindow = pin.HasAppWindow
            };
            if (!Contains(usage, pin.Grouped)) _pins.Add(new Pin(usage, pin.Grouped));
        }
        // ReSharper restore ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract, NullCoalescingConditionIsAlwaysNotNullAccordingToAPIContract
    }

    internal static SavedProcessPin[] Normalize(IEnumerable<SavedProcessPin>? saved)
    {
        var pins = new ProcessPins();
        pins.Restore(saved);
        return pins.Snapshot();
    }

    internal void Toggle(ProcessUsage process, bool grouped)
    {
        var index = _pins.FindIndex(p => Matches(p, process, grouped));
        if (index >= 0) _pins.RemoveAt(index);
        else _pins.Add(new Pin(process, grouped));
    }

    // The sampling thread receives an immutable snapshot, never the UI-owned list.
    internal Func<ProcessUsage, bool>? RetentionSnapshot()
    {
        if (_pins.Count == 0) return null;
        var pins = _pins.ToArray();
        // Compaction asks once per process per sample, so avoid a LINQ enumerator and closure each time.
        return process =>
        {
            // ReSharper disable once LoopCanBeConvertedToQuery
            foreach (var pin in pins)
                if (Matches(pin, process, pin.Grouped))
                    return true;
            return false;
        };
    }

    internal PinnedProcess[] Resolve(CpuSample? newest, IReadOnlyList<ProcessUsage> averages,
        bool grouped, bool historical)
    {
        var result = new List<PinnedProcess>();
        // ReSharper disable once LoopCanBeConvertedToQuery
        foreach (var pin in _pins.Where(p => p.Grouped == grouped))
        {
            var observed = newest?.Processes.FirstOrDefault(p => Matches(pin, p, grouped));
            var current = observed is null ? null : averages.FirstOrDefault(p => Matches(pin, p, grouped)) ?? observed;
            var state = newest is null || newest.IsGap || newest.TotalCpuPercent is null
                ? PinnedProcessState.Unavailable
                : current is not null
                    ? PinnedProcessState.Present
                    : historical
                        ? PinnedProcessState.NotRecorded
                        : newest.UnavailableProcesses > 0
                            ? PinnedProcessState.Unavailable
                            : PinnedProcessState.Exited;
            // Missing data must never expose the saved pin's stale readings as current measurements.
            var usage = current ?? pin.Usage with
            {
                CpuPercent = 0, GpuPercent = null, WorkingSetBytes = null, PrivateBytes = null,
                IoBytesPerSecond = null, ThreadCount = null, HandleCount = null
            };
            result.Add(new PinnedProcess(usage, state));
        }

        return [.. result];
    }

    private static bool Matches(Pin pin, ProcessUsage process, bool grouped)
    {
        return pin.Grouped == grouped &&
               (grouped
                   ? StringComparer.OrdinalIgnoreCase.Equals(ProcessRanking.GroupKey(pin.Usage),
                       ProcessRanking.GroupKey(process))
                   : pin.Usage.ProcessId == process.ProcessId && pin.Usage.CreationTicks == process.CreationTicks);
    }
}