

using MonCore.Monitoring;

namespace MonCore.Windows.Monitoring;

/// <summary>Bounded local protocol shared only by the Windows client and network service.</summary>
public static class NetworkServiceProtocol
{
    public const int Version = 1;
    public const string ServiceName = "WinMonNetworkMonitor";
    public const string PipeName = "WinMonNetworkMonitor.v1";
    public const int MaximumMessageBytes = 2 * 1024 * 1024;
    public const int MaximumRequestBytes = 4096;

    public static string InstallDirectory => ProtectedServiceEndpoints.Network.InstallDirectory;

    internal static string InstalledExecutable => ProtectedServiceEndpoints.Network.InstalledServiceExecutable;

    public static Task WriteAsync<T>(Stream stream, T value, CancellationToken token) =>
        LocalServiceWire.WriteAsync(stream, value, token);

    public static Task<T> ReadAsync<T>(Stream stream, CancellationToken token,
        int maximumBytes = MaximumMessageBytes) => LocalServiceWire.ReadAsync<T>(stream, token, maximumBytes);
}
public sealed record NetworkServiceRequest(int Version, string Operation);

public sealed record NetworkServiceResponse(int Version, string Status, ProcessNetworkSnapshot? Snapshot = null);
