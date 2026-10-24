namespace WinMon.Monitoring;

/// <summary>Identifies one recorded sample, so a later lookup finds that exact sample and never a neighbour.</summary>
/// <remarks>Compaction keeps both timestamps, so a full sample and its compacted copy share a key.</remarks>
internal readonly record struct SampleKey(long MonotonicTimestamp, DateTimeOffset Timestamp)
{
    internal static SampleKey Of(MonitorSample sample)
    {
        return new SampleKey(sample.MonotonicTimestamp, sample.Timestamp);
    }

    /// <summary>The sample's position in <see cref="ProcessRanking.Moment"/> seconds.</summary>
    internal double Moment => MonotonicTimestamp > 0
        ? MonotonicTimestamp / (double)System.Diagnostics.Stopwatch.Frequency
        : Timestamp.UtcTicks / (double)TimeSpan.TicksPerSecond;

    internal bool Matches(MonitorSample sample)
    {
        return sample.MonotonicTimestamp == MonotonicTimestamp && sample.Timestamp == Timestamp;
    }
}
