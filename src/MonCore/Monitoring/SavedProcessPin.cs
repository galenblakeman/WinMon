namespace WinMon.Monitoring;

/// <summary>Saved pin identity and display metadata, without sampled resource readings.</summary>
public sealed record SavedProcessPin(
    int ProcessId,
    long CreationTicks,
    string Name,
    string ImagePath = "",
    string AppName = "",
    bool? HasAppWindow = null,
    bool Grouped = false)
{
    public ProcessLifetimeDomain LifetimeDomain { get; init; } = ProcessLifetimeIdentity.WindowsFileTime;
    public DateTimeOffset? StartedAtUtc { get; init; }
    public string IdentityPolicyId { get; init; } = ProcessIdentityPolicy.Windows.Id;
}