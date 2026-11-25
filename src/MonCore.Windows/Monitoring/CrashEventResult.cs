using MonCore.Monitoring;

namespace MonCore.Windows.Monitoring;

/// <summary>Bounded crash events and Application log coverage for an on-demand diagnostic query.</summary>
public sealed record CrashEventResult(
    IReadOnlyList<IncidentEvent> Events,
    IncidentEventLogCoverage ApplicationCoverage,
    bool LimitReached,
    DateTimeOffset? OldestRetainedApplicationEvent);
