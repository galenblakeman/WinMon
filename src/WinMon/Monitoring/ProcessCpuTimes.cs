namespace WinMon.Monitoring;

/// <summary>Carry lifetime CPU time for one process identity.</summary>
/// <param name="ProcessId">The Windows process identifier.</param>
/// <param name="CreationTicks">The native creation time that disambiguates identifier reuse.</param>
/// <param name="Name">The process display name.</param>
/// <param name="CpuTicks">Combined lifetime kernel and user CPU time.</param>
internal readonly record struct ProcessCpuTimes(int ProcessId, long CreationTicks, string Name, long CpuTicks,
    ulong? WorkingSetBytes = null, ulong? ReadBytes = null, ulong? WriteBytes = null);
