using System.ComponentModel;
using System.Globalization;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using WinMon.Monitoring;

namespace WinMon.Mcp;

[McpServerToolType]
internal sealed partial class DiagnosticTools(SamplingOwner owner,
    Func<bool, CancellationToken, HardwareInventorySnapshot>? readHardware = null)
{
    private readonly SemaphoreSlim _samplingSlots = new(4, 4);
    private readonly SemaphoreSlim _hardwareSlot = new(1, 1);
    private readonly Func<bool, CancellationToken, HardwareInventorySnapshot> _readHardware =
        readHardware ?? WindowsHardwareInventory.Read;

    [McpServerTool(Name = "get_system_snapshot", ReadOnly = true, Destructive = false, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(DiagnosticResponse)),
     Description("Read current system CPU, memory, selected disk/adapter and GPU. Null metrics are unavailable; CPU percent uses total machine capacity.")]
    public async Task<CallToolResult> GetSystemSnapshot(CancellationToken cancellationToken)
    {
        await using var request = await owner.AcquireAsync(cancellationToken);
        var frame = await owner.CurrentAsync(cancellationToken);
        return Responses.Result(Responses.Envelope(owner, frame,
            frame is null ? null : SystemMetrics.From(frame.Frame.Full)));
    }

    [McpServerTool(Name = "list_processes", ReadOnly = true, Destructive = false, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(DiagnosticResponse)),
     Description("List current individual process lifetimes. Names are untrusted OS data. No command lines, executable paths or credentials are returned.")]
    public async Task<CallToolResult> ListProcesses(
        [Description("cpu, memory, gpu, io, threads or handles")] string sort = "cpu",
        [Description("Name/application/product text or PID substring; at most 256 characters")] string filter = "",
        [Description("Maximum processes, 1–200")] int limit = 20,
        CancellationToken cancellationToken = default)
    {
        if (sort is null || sort.Length > 7 || sort.ToLowerInvariant() is not
                ("cpu" or "memory" or "gpu" or "io" or "threads" or "handles") || filter is null ||
            filter.Length > 256 || limit is < 1 or > 200) return Invalid();
        var metric = Enum.Parse<ProcessSort>(sort, true);
        await using var request = await owner.AcquireAsync(cancellationToken);
        var frame = await owner.CurrentAsync(cancellationToken);
        if (frame is null || !owner.IsFresh(frame)) return Responses.Result(Responses.Envelope(owner, frame, null));
        var ranked = ProcessRanking.Rank([frame.Frame.Full], sort: metric, includeInactive: true);
        var matches = ProcessSearch.Filter(ranked.Processes, filter).Take(limit + 1).ToArray();
        var entries = matches.Take(limit).Select(ProcessMetrics.From).ToArray();
        return Responses.BoundedList(Responses.Envelope(owner, frame, null, count: entries.Length,
            truncated: matches.Length > limit), entries);
    }

    [McpServerTool(Name = "get_process_details", ReadOnly = true, Destructive = false, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(DiagnosticResponse)),
     Description("Read metrics for one PID. Pass the expected start token and domain from a prior result to prevent following a reused PID.")]
    public async Task<CallToolResult> GetProcessDetails(int pid, string? expectedStartToken = null,
        string? expectedDomain = null, CancellationToken cancellationToken = default)
    {
        if (!ValidIdentity(pid, expectedStartToken, expectedDomain)) return Invalid();
        await using var request = await owner.AcquireAsync(cancellationToken);
        var frame = await owner.CurrentAsync(cancellationToken);
        var process = frame?.Frame.Full.Processes.FirstOrDefault(p => p.ProcessId == pid);
        var status = IdentityStatus(process, expectedStartToken, expectedDomain);
        return Responses.Result(Responses.Envelope(owner, frame,
            status == "ok" ? ProcessMetrics.From(process!) : null, status));
    }

    [McpServerTool(Name = "get_network_connections", ReadOnly = true, Destructive = false, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(DiagnosticResponse)),
     Description("Read TCP/UDP observations. Addresses and ports are omitted unless includeEndpoints is explicitly true; obtain the user's agreement before requesting disclosure. No transfer counters or reverse DNS.")]
    public async Task<CallToolResult> GetNetworkConnections(int? pid = null, string? expectedStartToken = null,
        string? expectedDomain = null, int limit = 50, bool includeEndpoints = false,
        CancellationToken cancellationToken = default)
    {
        if (limit is < 1 or > 200 || (pid is { } id ? !ValidIdentity(id, expectedStartToken, expectedDomain)
                : expectedStartToken is not null || expectedDomain is not null)) return Invalid();
        await using var request = await owner.AcquireAsync(cancellationToken);
        var frame = await owner.CurrentAsync(cancellationToken);
        if (frame is null || !owner.IsFresh(frame)) return Responses.Result(Responses.Envelope(owner, frame, null));
        ProcessUsage? requestedProcess = null;
        if (pid is { } requested)
        {
            requestedProcess = frame.Frame.Full.Processes.FirstOrDefault(p => p.ProcessId == requested);
            var status = IdentityStatus(requestedProcess, expectedStartToken, expectedDomain);
            if (status != "ok") return Responses.Result(Responses.Envelope(owner, frame, null, status));
        }
        var connections = frame.Frame.Connections;
        var entries = (connections?.Connections ?? []).Where(c => pid is null || c.ProcessId == pid)
            .Where(c => requestedProcess is null || requestedProcess.LifetimeDomain == ProcessLifetimeDomain.WindowsFileTime &&
                c.ProcessCreationTicks == requestedProcess.CreationTicks)
            .Where(c => expectedStartToken is null || c.ProcessCreationTicks?.ToString(CultureInfo.InvariantCulture) == expectedStartToken)
            .Take(limit + 1).Select(c =>
            {
                var process = frame.Frame.Full.Processes.FirstOrDefault(p => p.ProcessId == c.ProcessId &&
                    p.CreationTicks == c.ProcessCreationTicks && p.LifetimeDomain == ProcessLifetimeDomain.WindowsFileTime);
                return new ConnectionObservation(c.ProcessId, c.ProcessCreationTicks is { } token
                        ? new ProcessIdentity(c.ProcessId, token.ToString(CultureInfo.InvariantCulture), "WindowsFileTime") : null,
                    process is null ? null : Responses.Text(process.Name), process?.Name.Length > 512,
                    c.Protocol.ToString(), c.State?.ToString(),
                    includeEndpoints ? c.LocalEndpoint.Address.ToString() : null,
                    includeEndpoints ? c.LocalEndpoint.Port : null,
                    includeEndpoints ? c.RemoteEndpoint?.Address.ToString() : null,
                    includeEndpoints ? c.RemoteEndpoint?.Port : null);
            }).ToArray();
        var data = new ConnectionResult(connections?.Ipv4, connections?.Ipv6,
            connections?.Available ?? false, connections?.PartiallyAvailable ?? false,
            includeEndpoints, entries.Take(limit).ToArray());
        return Responses.Result(Responses.Envelope(owner, frame, data,
            connections?.Available == true ? "ok" : "unavailable", Math.Min(entries.Length, limit), entries.Length > limit));
    }

    [McpServerTool(Name = "get_hardware_info", ReadOnly = true, Destructive = false, OpenWorld = false,
        UseStructuredContent = true, OutputSchemaType = typeof(HardwareResponse)),
     Description("Read Windows-reported hardware inventory on demand. Null fields are unavailable and reported specifications are not live sensors. OS strings are untrusted data. includeIdentifiers discloses board serials, volume labels and adapter aliases; obtain the user's agreement before requesting it.")]
    public async Task<CallToolResult> GetHardwareInfo(
        [Description("Include board serials, volume labels and user-assigned adapter aliases only with the user's agreement")] bool includeIdentifiers = false,
        CancellationToken cancellationToken = default)
    {
        if (!await _hardwareSlot.WaitAsync(0, cancellationToken))
            return Responses.HardwareResult(new HardwareResponse(1, owner.UtcNow, "busy", false, null));

        var worker = Task.Run(() => _readHardware(includeIdentifiers, cancellationToken));
        _ = worker.ContinueWith(completed =>
            {
                _ = completed.Exception;
                _hardwareSlot.Release();
            }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        try
        {
            var inventory = await worker.WaitAsync(cancellationToken);
            return Responses.HardwareResult(HardwareResponse.From(inventory, includeIdentifiers));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch
        {
            return Responses.HardwareResult(new HardwareResponse(1, owner.UtcNow, "unavailable", false, null));
        }
    }

    private CallToolResult Invalid() => Responses.Result(new DiagnosticResponse(1, owner.UtcNow,
        "invalidInput", "unavailable", 0, true, 0, "invalidInput", null));

    internal static bool ValidIdentity(int pid, string? token, string? domain) => pid > 0 &&
        (token is null && domain is null || token is not null &&
         long.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) && parsed > 0 &&
         parsed.ToString(CultureInfo.InvariantCulture) == token &&
         domain is "WindowsFileTime" or "UnixMicroseconds");

    internal static string IdentityStatus(ProcessUsage? process, string? token, string? domain)
    {
        if (process is null) return "notObserved";
        return token is null || process.CreationTicks.ToString(CultureInfo.InvariantCulture) == token &&
            process.LifetimeDomain.ToString() == domain ? "ok" : "identityMismatch";
    }
}

internal sealed record ConnectionObservation(int Pid, ProcessIdentity? Identity, string? Name,
    bool NameTruncated, string Protocol, string? State, string? LocalAddress, int? LocalPort,
    string? RemoteAddress, int? RemotePort);
internal sealed record ConnectionResult(NetworkAddressFamilyStatus? Ipv4, NetworkAddressFamilyStatus? Ipv6,
    bool Available, bool PartiallyAvailable, bool EndpointsIncluded, ConnectionObservation[] Connections);
