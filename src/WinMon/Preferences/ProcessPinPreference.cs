namespace WinMon.Preferences;

/// <summary>Saved pin identity and display metadata, without sampled resource readings.</summary>
public sealed record ProcessPinPreference(
    int ProcessId,
    long CreationTicks,
    string Name,
    string ImagePath = "",
    string AppName = "",
    bool? HasAppWindow = null,
    bool Grouped = false);