namespace WinMon.Monitoring;

/// <summary>A selected lifetime (or executable group), independent of the ranked row slots.</summary>
public sealed class ProcessSelection
{
    private ProcessPins _target = new();
    public bool Grouped { get; private set; }
    public bool HasSelection => _target.Count(Grouped) > 0;

    public void Select(ProcessUsage process, bool grouped)
    {
        _target = new ProcessPins();
        Grouped = grouped;
        _target.Toggle(process, grouped);
    }

    public bool Contains(ProcessUsage process, bool grouped)
    {
        return _target.Contains(process, grouped);
    }

    public Func<ProcessUsage, bool>? RetentionSnapshot()
    {
        return _target.RetentionSnapshot();
    }

    public PinnedProcess? Resolve(IReadOnlyList<MonitorSample> samples, bool grouped, bool historical,
        ProcessSort sort)
    {
        return Resolve(samples, grouped, historical,
            () => ProcessRanking.Rank(samples, sort: sort, grouped: grouped, includeInactive: true));
    }

    /// <param name="samples">The samples ending at the live or selected moment.</param>
    /// <param name="grouped">Whether rows are grouped by application.</param>
    /// <param name="historical">Whether the samples end at a selected past moment.</param>
    /// <param name="inclusive">The ranking of the same samples with inactive processes included, ranked only if needed.</param>
    public PinnedProcess? Resolve(IReadOnlyList<MonitorSample> samples, bool grouped, bool historical,
        Func<ProcessRanking> inclusive)
    {
        if (grouped != Grouped) _target = new ProcessPins();
        return HasSelection
            ? _target.Resolve(samples.LastOrDefault(), inclusive().Processes, grouped, historical).Single()
            : null;
    }
}