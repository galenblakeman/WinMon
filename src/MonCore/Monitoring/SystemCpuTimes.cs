namespace WinMon.Monitoring;

/// <summary>Represent system CPU counters in native 100-nanosecond units.</summary>
/// <param name="IdleTicks">Aggregated idle time.</param>
/// <param name="KernelTicks">Aggregated kernel time, including idle.</param>
/// <param name="UserTicks">Aggregated user time.</param>
internal readonly record struct SystemCpuTimes(long IdleTicks, long KernelTicks, long UserTicks)
{
    /// <summary>Get total elapsed machine CPU capacity, including idle time.</summary>
    public long TotalTicks => checked(KernelTicks + UserTicks);
}
