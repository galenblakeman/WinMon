namespace WinMon.Monitoring;

/// <summary>Carry lifetime CPU time for one process identity.</summary>
/// <param name="ProcessId">The Windows process identifier.</param>
/// <param name="CreationTicks">The native creation time that disambiguates identifier reuse.</param>
/// <param name="Name">The process display name.</param>
/// <param name="CpuTicks">Combined lifetime kernel and user CPU time.</param>
/// <param name="PrivateBytes">Private committed memory, which unlike working sets can be summed across processes.</param>
/// <param name="ImagePath">Full executable path, or <see langword="null"/> when it cannot be read.</param>
internal readonly record struct ProcessCpuTimes(
    int ProcessId,
    long CreationTicks,
    string Name,
    long CpuTicks,
    ulong? WorkingSetBytes = null,
    ulong? ReadBytes = null,
    ulong? WriteBytes = null,
    ulong? PrivateBytes = null,
    string? ImagePath = null)
{
    public int ParentProcessId { get; init; }
    public long? ThreadCount { get; init; }
    public long? HandleCount { get; init; }
    public bool? HasAppWindow { get; init; }
    public bool HasHadAppWindow { get; init; }
}