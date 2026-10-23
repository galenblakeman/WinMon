namespace WinMon.Monitoring;

/// <summary>A selected lifetime (or executable group), independent of the ranked row slots.</summary>
internal sealed class ProcessSelection
{
    private ProcessPins _target = new();
    internal bool Grouped { get; private set; }
    internal bool HasSelection => _target.Count(Grouped) > 0;

    internal void Select(ProcessUsage process, bool grouped)
    {
        _target = new ProcessPins();
        Grouped = grouped;
        _target.Toggle(process, grouped);
    }

    internal bool Contains(ProcessUsage process, bool grouped) => _target.Contains(process, grouped);
    internal Func<ProcessUsage, bool>? RetentionSnapshot() => _target.RetentionSnapshot();

    internal PinnedProcess? Resolve(IReadOnlyList<CpuSample> samples, bool grouped, bool historical, ProcessSort sort) =>
        Resolve(samples, grouped, historical, () => ProcessRanking.Rank(samples, sort: sort, grouped: grouped, includeInactive: true));

    /// <param name="inclusive">The ranking of the same samples with inactive processes included, ranked only if needed.</param>
    internal PinnedProcess? Resolve(IReadOnlyList<CpuSample> samples, bool grouped, bool historical, Func<ProcessRanking> inclusive)
    {
        if (grouped != Grouped) _target = new ProcessPins();
        if (!HasSelection) return null;
        return _target.Resolve(samples.LastOrDefault(), inclusive().Processes, grouped, historical).Single();
    }
}
