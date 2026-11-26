using MonCore.Windows.Monitoring;

namespace WinMon.NetworkService;

/// <summary>Compatibility facade for network service process security.</summary>
internal static class ServiceProcessSecurity
{
    internal const uint ProcessQueryLimitedInformation = ProtectedServiceProcess.ProcessQueryLimitedInformation;
    internal static void Apply(IReadOnlyCollection<string> users) =>
        ProtectedServiceProcess.ApplySecurity(ProtectedServiceEndpoints.Network, users);
    internal static string BuildSddl(IReadOnlyCollection<string> users) =>
        ProtectedServiceProcess.BuildSddl(users);
}
