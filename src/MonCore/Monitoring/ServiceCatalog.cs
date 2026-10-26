namespace WinMon.Monitoring;

/// <summary>Windows SCM observation retained for the existing Services view.</summary>
public sealed record ServiceObservation(
    string Name,
    string DisplayName,
    uint State,
    int ProcessId,
    uint ControlsAccepted)
{
    public bool HasHost => ProcessId > 0 && State is 4 or 7 or 5 or 6;
}

public sealed record ServiceCatalog(long ObservedFileTime, IReadOnlyList<ServiceObservation> Services);
