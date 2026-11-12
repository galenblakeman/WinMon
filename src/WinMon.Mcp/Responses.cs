using System.Globalization;
using System.Text.Json;
using ModelContextProtocol.Protocol;
using WinMon.Monitoring;

namespace WinMon.Mcp;

internal sealed record ProcessIdentity(int Pid, string StartToken, string Domain)
{
    internal static ProcessIdentity From(ProcessUsage process) => new(process.ProcessId,
        process.CreationTicks.ToString(CultureInfo.InvariantCulture), process.LifetimeDomain.ToString());
}

internal sealed record ProcessMetrics(ProcessIdentity Identity, string Name, bool NameTruncated,
    double? CpuPercent, ulong? WorkingSetBytes, ulong? PrivateBytes, double? IoBytesPerSecond,
    double? GpuPercent, long? Threads, long? Handles)
{
    public Dictionary<string, bool> MetricAvailability => new()
    {
        ["cpuPercent"] = CpuPercent is not null, ["workingSetBytes"] = WorkingSetBytes is not null,
        ["privateBytes"] = PrivateBytes is not null, ["ioBytesPerSecond"] = IoBytesPerSecond is not null,
        ["gpuPercent"] = GpuPercent is not null, ["threads"] = Threads is not null, ["handles"] = Handles is not null
    };

    internal static ProcessMetrics From(ProcessUsage process) => new(ProcessIdentity.From(process),
        Responses.Text(process.Name), process.Name.Length > 512, process.ObservedCpuPercent,
        process.WorkingSetBytes, process.PrivateBytes, process.IoBytesPerSecond,
        process.GpuPercent, process.ThreadCount, process.HandleCount);
}

internal sealed record SystemMetrics(double? CpuPercent, ulong? MemoryUsedBytes, ulong? MemoryTotalBytes,
    double? MemoryPercent, double? GpuPercent, string GpuId, string GpuName, string GpuEngine, string DiskName,
    double? DiskActivePercent, double? DiskReadBytesPerSecond, double? DiskWriteBytesPerSecond,
    string NetworkId, string NetworkName, double? ReceiveBytesPerSecond, double? SendBytesPerSecond,
    bool MetadataTruncated)
{
    public Dictionary<string, bool> MetricAvailability => new()
    {
        ["cpuPercent"] = CpuPercent is not null, ["memoryUsedBytes"] = MemoryUsedBytes is not null,
        ["memoryTotalBytes"] = MemoryTotalBytes is not null, ["memoryPercent"] = MemoryPercent is not null,
        ["gpuPercent"] = GpuPercent is not null, ["diskActivePercent"] = DiskActivePercent is not null,
        ["diskReadBytesPerSecond"] = DiskReadBytesPerSecond is not null,
        ["diskWriteBytesPerSecond"] = DiskWriteBytesPerSecond is not null,
        ["receiveBytesPerSecond"] = ReceiveBytesPerSecond is not null, ["sendBytesPerSecond"] = SendBytesPerSecond is not null
    };

    internal static SystemMetrics From(MonitorSample sample)
    {
        var r = sample.Resources;
        string[] metadata = [r.GpuId, r.GpuName, r.GpuEngine, r.DiskName, r.NetworkId, r.NetworkName];
        return new(sample.TotalCpuPercent, r.UsedMemoryBytes, r.TotalMemoryBytes, r.MemoryPercent,
            r.GpuPercent, Responses.Text(r.GpuId), Responses.Text(r.GpuName), Responses.Text(r.GpuEngine), Responses.Text(r.DiskName),
            r.DiskActivePercent, r.DiskReadBytesPerSecond, r.DiskWriteBytesPerSecond,
            Responses.Text(r.NetworkId), Responses.Text(r.NetworkName), r.ReceiveBytesPerSecond,
            r.SendBytesPerSecond, metadata.Any(value => value.Length > 512));
    }
}

internal sealed record DiagnosticResponse(int SchemaVersion, DateTimeOffset TimestampUtc,
    string Status, string Freshness, double CollectionIntervalSeconds, bool IsGap,
    int UnavailableProcesses, string? CollectionError, object? Data,
    bool Truncated = false, int ReturnedCount = 0);

internal static class Responses
{
    internal const int MaximumBytes = 256 * 1024;
    internal static JsonSerializerOptions Json { get; } = new(JsonSerializerDefaults.Web);
    internal static string Text(string value)
    {
        var end = Math.Min(value.Length, 512);
        if (end < value.Length && end > 0 && char.IsHighSurrogate(value[end - 1])) end--;
        return string.Concat(value.AsSpan(0, end).ToString().Where(c => !char.IsControl(c)));
    }

    internal static DiagnosticResponse Envelope(SamplingOwner owner, PublishedFrame? published,
        object? data, string status = "ok", int count = 0, bool truncated = false, bool allowHistorical = false)
    {
        var sample = published?.Frame.Full;
        var fresh = published is not null && owner.IsFresh(published);
        return new(1, (sample?.Timestamp ?? owner.UtcNow).ToUniversalTime(),
            !fresh && !allowHistorical && status == "ok" ? "unavailable" : status,
            published is null ? "unavailable" : fresh ? "current" : "stale",
            sample?.IntervalSeconds ?? 0, sample?.IsGap ?? true,
            sample?.UnavailableProcesses ?? 0, published?.Frame.Error?.ToString(),
            fresh || allowHistorical ? data : null, truncated, fresh || allowHistorical ? count : 0);
    }

    internal static CallToolResult Result(DiagnosticResponse response)
    {
        while (true)
        {
            var element = JsonSerializer.SerializeToElement(response, Json);
            var result = new CallToolResult
            {
                StructuredContent = element,
                Content = [new TextContentBlock { Text = element.GetRawText() }],
                IsError = response.Status is "invalidInput" or "busy" or "identityMismatch"
            };
            if (JsonSerializer.SerializeToUtf8Bytes(result, Json).Length <= MaximumBytes) return result;
            object? data = response.Data switch
            {
                PerformanceResult performance => performance with { Trend = performance.Trend.Take(performance.Trend.Length / 2).ToArray() },
                ConnectionResult connections => connections with { Connections = connections.Connections.Take(connections.Connections.Length / 2).ToArray() },
                TrafficResult traffic => traffic with { Processes = traffic.Processes.Take(traffic.Processes.Length / 2).ToArray() },
                Array entries => entries.Cast<object>().Take(entries.Length / 2).ToArray(),
                _ => null
            };
            var count = data switch
            {
                PerformanceResult performance => performance.Trend.Length,
                ConnectionResult connections => connections.Connections.Length,
                TrafficResult traffic => traffic.Processes.Length,
                Array entries => entries.Length,
                _ => 0
            };
            response = response with { Data = data, ReturnedCount = count, Truncated = true };
        }
    }

    internal static CallToolResult BoundedList<T>(DiagnosticResponse response, IReadOnlyList<T> entries)
        => Result(response with { Data = entries.ToArray(), ReturnedCount = entries.Count });
}
