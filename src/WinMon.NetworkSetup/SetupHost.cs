using WinMon.Diagnostics;
using WinMon.Monitoring;

namespace WinMon.NetworkSetup;

// Per-run OS boundaries keep transaction tests away from the installed service and registration.
internal sealed class SetupHost
{
    internal string InstallDirectory { get; init; } = SetupPaths.InstallDirectory;
    internal Action PrepareParent { get; init; } = ProtectedInstallation.PrepareParent;
    internal Action EnsureOwnedOrAbsent { get; init; } = NativeService.EnsureOwnedOrAbsent;
    internal Func<NativeService.ServiceState> CaptureState { get; init; } = NativeService.CaptureState;
    internal Action<CancellationToken> StopOwnedIfRunning { get; init; } = NativeService.StopOwnedIfRunning;
    internal Action<IReadOnlyCollection<string>> CreateOrUpdateOwned { get; init; } = NativeService.CreateOrUpdateOwned;
    internal Action<CancellationToken> StartAndVerify { get; init; } = NativeService.StartAndVerify;
    internal Action<NativeService.ServiceState, CancellationToken> RestoreService { get; init; } = NativeService.Restore;
    internal Action<CancellationToken> StopAndDeleteOwned { get; init; } = NativeService.StopAndDeleteOwned;
    internal Func<Dictionary<string, object>?> CaptureRegistration { get; init; } = SetupRegistration.Capture;
    internal Action WriteRegistration { get; init; } = SetupRegistration.Write;
    internal Action<Dictionary<string, object>?> RestoreRegistration { get; init; } = SetupRegistration.Restore;
    internal Action<string> VerifyPublisher { get; init; } = NetworkSetupPackage.VerifyPublisher;
    internal Action<string> ProvisionLogs { get; init; } = LogDirectories.Provision;
}
