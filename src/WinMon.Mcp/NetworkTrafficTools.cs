using System.ComponentModel;
using System.Globalization;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using WinMon.Monitoring;

namespace WinMon.Mcp;

internal sealed record TrafficProcess(ProcessIdentity Identity, string? Name, bool NameTruncated,
    long ReceivedBytes, long SentBytes, double? ReceiveBytesPerSecond, double? SendBytesPerSecond);

internal sealed record TrafficResult(string ServiceState, ProcessNetworkQuality Quality, Guid SessionId,
    string Scope, TrafficProcess[] Processes);

internal sealed partial class DiagnosticTools
{
    [McpServerTool(Name = "get_process_network_traffic", ReadOnly = true, Destructive = false, OpenWorld = false,
        UseStructuredContent = true, OutputSchemaType = typeof(DiagnosticResponse)),
     Description("Read per-process receive/send rates and observed session bytes across all adapters. Connects only to the already-installed optional service; never installs or repairs it. No endpoints or paths. Rates use the delayed five-second service window; totals start with this server's subscription. First calls may be warming up; retry after a few seconds.")]
    public async Task<CallToolResult> GetProcessNetworkTraffic(
        [Description("receive, send, received or sent")] string sort = "receive",
        [Description("Maximum processes, 1–200")] int limit = 20,
        int? pid = null, string? expectedStartToken = null, string? expectedDomain = null,
        CancellationToken cancellationToken = default)
    {
        if (sort is null || sort is not ("receive" or "send" or "received" or "sent") || limit is < 1 or > 200 ||
            (pid is { } id ? !ValidIdentity(id, expectedStartToken, expectedDomain) :
                expectedStartToken is not null || expectedDomain is not null)) return Invalid();
        cancellationToken.ThrowIfCancellationRequested();
        owner.EnableTraffic();
        var frame = await owner.CurrentAsync(cancellationToken);
        if (frame is null || !owner.IsFresh(frame)) return Responses.Result(Responses.Envelope(owner, frame, null));
        var network = frame.Network;
        var candidates = (network?.Processes ?? []).Where(reading => pid is null || reading.ProcessId == pid).ToArray();
        var rows = candidates.Where(reading => expectedStartToken is null || expectedDomain == "WindowsFileTime" &&
            reading.CreationTicks.ToString(CultureInfo.InvariantCulture) == expectedStartToken).ToArray();
        if (expectedStartToken is not null && candidates.Length > 0 && rows.Length == 0)
            return Responses.Result(Responses.Envelope(owner, frame, null, "identityMismatch"));
        var quality = network?.Quality ?? ProcessNetworkQuality.Unavailable;
        var unavailable = quality.HasFlag(ProcessNetworkQuality.Unavailable);
        var entries = rows.OrderByDescending(reading => sort switch
        {
            "send" => reading.SendBytesPerSecond ?? -1,
            "received" => reading.ReceivedBytes,
            "sent" => reading.SentBytes,
            _ => reading.ReceiveBytesPerSecond ?? -1
        }).ThenBy(reading => reading.ProcessId).Take(limit + 1).Select(reading =>
        {
            var process = network?.Owners.FirstOrDefault(process => process.ProcessId == reading.ProcessId &&
                process.CreationTicks == reading.CreationTicks && process.LifetimeDomain == ProcessLifetimeDomain.WindowsFileTime)
                ?? frame.Frame.Full.Processes.FirstOrDefault(process => process.ProcessId == reading.ProcessId &&
                    process.CreationTicks == reading.CreationTicks && process.LifetimeDomain == ProcessLifetimeDomain.WindowsFileTime);
            return new TrafficProcess(new ProcessIdentity(reading.ProcessId,
                reading.CreationTicks.ToString(CultureInfo.InvariantCulture), "WindowsFileTime"),
                process is null ? null : Responses.Text(process.Name), process?.Name.Length > 512,
                reading.ReceivedBytes, reading.SentBytes, unavailable ? null : reading.ReceiveBytesPerSecond,
                unavailable ? null : reading.SendBytesPerSecond);
        }).ToArray();
        var status = unavailable ? "unavailable" : pid is not null && entries.Length == 0 ? "notObserved" : "ok";
        return Responses.Result(Responses.Envelope(owner, frame,
            new TrafficResult(owner.TrafficState, quality, owner.TrafficSessionId,
                "All adapters; observed bytes since this server subscribed; delayed five-second rates",
                entries.Take(limit).ToArray()), status, Math.Min(entries.Length, limit), entries.Length > limit));
    }
}
