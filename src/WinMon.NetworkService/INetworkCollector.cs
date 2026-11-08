using WinMon.Monitoring;

namespace WinMon.NetworkService;

internal interface INetworkCollector : IAsyncDisposable
{
    Task StartAsync(CancellationToken cancellationToken);
    ProcessNetworkSnapshot Snapshot(string ownerSid);
    bool Failed { get; }
    ProcessNetworkFailure Failure { get; }
}