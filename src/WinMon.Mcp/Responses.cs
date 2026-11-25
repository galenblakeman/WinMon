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

internal sealed record HardwareResponse(int SchemaVersion, DateTimeOffset TimestampUtc, string Status,
    bool Truncated, HardwareInventorySnapshot? Data)
{
    internal static HardwareResponse From(HardwareInventorySnapshot inventory, bool includeIdentifiers)
    {
        var data = HardwareProjection.Safe(inventory with { IdentifiersIncluded = includeIdentifiers }, out var changed);
        var status = data.Status is "ok" or "partial" or "unavailable" ? data.Status : "unavailable";
        return new(data.SchemaVersion, data.CollectionCompletedUtc,
            changed && status == "ok" ? "partial" : status, data.Truncated || changed, data);
    }
}

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
            (object? Data, int Count) trimmed = response.Data switch
            {
                UsageResult usage => (usage with { Apps = usage.Apps.Take(usage.Apps.Length / 2).ToArray() }, usage.Apps.Length / 2),
                ArchivedUsageResult usage => (usage with { Apps = usage.Apps.Take(usage.Apps.Length / 2).ToArray() }, usage.Apps.Length / 2),
                HistoryResult history => (history with { Points = history.Points.TakeLast(history.Points.Length / 2).ToArray() }, history.Points.Length / 2),
                PerformanceResult performance => (performance with { Trend = performance.Trend.Take(performance.Trend.Length / 2).ToArray() }, performance.Trend.Length / 2),
                ConnectionResult connections => (connections with { Connections = connections.Connections.Take(connections.Connections.Length / 2).ToArray() }, connections.Connections.Length / 2),
                TrafficResult traffic => (traffic with { Processes = traffic.Processes.Take(traffic.Processes.Length / 2).ToArray() }, traffic.Processes.Length / 2),
                CrashListResult crashes => TrimCrash(crashes),
                Array entries => (entries.Cast<object>().Take(entries.Length / 2).ToArray(), entries.Length / 2),
                _ => (null, 0)
            };
            response = response with { Data = trimmed.Data, ReturnedCount = trimmed.Count, Truncated = true };
        }
    }

    internal static CallToolResult HardwareResult(HardwareResponse response)
    {
        while (true)
        {
            var element = JsonSerializer.SerializeToElement(response, Json);
            var result = new CallToolResult
            {
                StructuredContent = element,
                Content = [new TextContentBlock { Text = element.GetRawText() }],
                IsError = response.Status == "busy"
            };
            if (JsonSerializer.SerializeToUtf8Bytes(result, Json).Length <= MaximumBytes) return result;
            var trimmed = response.Data is null ? null : HardwareProjection.Trim(response.Data);
            if (trimmed is null) return result;
            response = response with { Data = trimmed with { Truncated = true,
                Status = trimmed.Status == "ok" ? "partial" : trimmed.Status }, Truncated = true,
                Status = response.Status == "ok" ? "partial" : response.Status };
        }
    }

    internal static CallToolResult BoundedList<T>(DiagnosticResponse response, IReadOnlyList<T> entries)
        => Result(response with { Data = entries.ToArray(), ReturnedCount = entries.Count });

    private static CrashListResult Trim(CrashListResult source)
    {
        if (source.Groups.Length > 1) return source with { Groups = source.Groups.Take((source.Groups.Length + 1) / 2).ToArray() };
        if (source.Groups.Length == 0) return source;
        var group = source.Groups[0];
        return group.Versions.Length > 1
            ? source with { Groups = [group with { Versions = group.Versions.Take((group.Versions.Length + 1) / 2).ToArray() }] }
            : source with { Groups = [] };
    }

    private static (object? Data, int Count) TrimCrash(CrashListResult source)
    {
        var trimmed = Trim(source);
        return (trimmed, trimmed.Groups.Length);
    }
}

internal static class HardwareProjection
{
    internal static HardwareInventorySnapshot Safe(HardwareInventorySnapshot source, out bool changed)
    {
        var cpu = ProjectCpu();
        var board = ProjectMotherboard();
        var memory = ProjectMemory();
        var graphics = ProjectGraphics();
        var disks = ProjectDisks();
        var volumes = ProjectVolumes();
        var network = ProjectNetwork();
        cpu = Clipped(cpu, CpuClipped());
        board = Clipped(board, MotherboardClipped());
        memory = Clipped(memory, MemoryClipped());
        graphics = Clipped(graphics, GraphicsClipped());
        disks = Clipped(disks, DisksClipped());
        volumes = Clipped(volumes, VolumesClipped());
        network = Clipped(network, NetworkClipped());
        IHardwareSection[] sections = [cpu, board, memory, graphics, disks, volumes, network];
        changed = sections.Any(section => section.Truncated);
        return source with { Status = changed && source.Status == "ok" ? "partial" : source.Status,
            Truncated = source.Truncated || changed, Cpu = cpu, Motherboard = board, Memory = memory,
            Graphics = graphics, Disks = disks, Volumes = volumes, Network = network };

        // Local functions share the identifier-policy capture across both passes.
        HardwareSection<HardwareCpu> ProjectCpu() =>
            SafeSection(source.Cpu) with { Data = source.Cpu.Data is { } cpuData ? cpuData with
            {
                Names = cpuData.Names.Take(256).Select(x => Text(x) ?? "").ToArray(), Processors = cpuData.Processors.Take(256).Select(Safe).ToArray()
            } : null };

        HardwareSection<HardwareMotherboards> ProjectMotherboard() =>
            SafeSection(source.Motherboard) with { Data = source.Motherboard.Data is { } boardData ? boardData with
            { Items = boardData.Items.Take(256).Select(x => x with { Manufacturer = Text(x.Manufacturer), Product = Text(x.Product), Version = Text(x.Version), SerialNumber = source.IdentifiersIncluded ? Text(x.SerialNumber) : null }).ToArray() } : null };

        HardwareSection<HardwareMemory> ProjectMemory() =>
            SafeSection(source.Memory) with { Data = source.Memory.Data is { } memoryData ? memoryData with
            { Modules = memoryData.Modules.Take(256).Select(x => x with { DeviceLocator = Text(x.DeviceLocator), BankLabel = Text(x.BankLabel), Manufacturer = Text(x.Manufacturer), PartNumber = Text(x.PartNumber) }).ToArray() } : null };

        HardwareSection<HardwareGraphics> ProjectGraphics() =>
            SafeSection(source.Graphics) with { Data = source.Graphics.Data is { } graphicsData ? graphicsData with
            { Adapters = graphicsData.Adapters.Take(256).Select(x => x with { Name = Text(x.Name) }).ToArray() } : null };

        HardwareSection<HardwareDisks> ProjectDisks() =>
            SafeSection(source.Disks) with { Data = source.Disks.Data is { } disksData ? disksData with
            { Items = disksData.Items.Take(256).Select(x => x with { Model = Text(x.Model), Manufacturer = Text(x.Manufacturer), DriveLetters = x.DriveLetters?.Take(256).Select(Text).Where(x => x is not null).Cast<string>().ToArray() }).ToArray() } : null };

        HardwareSection<HardwareVolumes> ProjectVolumes() =>
            SafeSection(source.Volumes) with { Data = source.Volumes.Data is { } volumesData ? volumesData with
            { Items = volumesData.Items.Take(256).Select(x => x with { Name = Text(x.Name) ?? "", Label = source.IdentifiersIncluded ? Text(x.Label) : null, FileSystem = Text(x.FileSystem) }).ToArray() } : null };

        HardwareSection<HardwareNetwork> ProjectNetwork() =>
            SafeSection(source.Network) with { Data = source.Network.Data is { } networkData ? networkData with
            { Adapters = networkData.Adapters.Take(256).Select(x => x with { Name = source.IdentifiersIncluded ? Text(x.Name) : null, Description = Text(x.Description) }).ToArray() } : null };

        bool CpuClipped() =>
            source.Cpu.Data is { } c && (c.Names.Length > 256 || c.Processors.Length > 256 ||
                c.Names.Any(ClippedText) || c.Processors.Any(p => new[] { p.Name, p.Manufacturer, p.SocketDesignation, p.DeviceId }.Any(ClippedText)));

        bool MotherboardClipped() =>
            source.Motherboard.Data is { } b && (b.Items.Length > 256 || b.Items.Any(x =>
                new[] { x.Manufacturer, x.Product, x.Version, source.IdentifiersIncluded ? x.SerialNumber : null }.Any(ClippedText)));

        bool MemoryClipped() =>
            source.Memory.Data is { } m && (m.Modules.Length > 256 || m.Modules.Any(x =>
                new[] { x.DeviceLocator, x.BankLabel, x.Manufacturer, x.PartNumber }.Any(ClippedText)));

        bool GraphicsClipped() =>
            source.Graphics.Data is { } g && (g.Adapters.Length > 256 || g.Adapters.Any(x => ClippedText(x.Name)));

        bool DisksClipped() =>
            source.Disks.Data is { } d && (d.Items.Count > 256 || d.Items.Any(x =>
                ClippedText(x.Model) || ClippedText(x.Manufacturer) || x.DriveLetters?.Length > 256 || x.DriveLetters?.Any(ClippedText) == true));

        bool VolumesClipped() =>
            source.Volumes.Data is { } v && (v.Items.Length > 256 || v.Items.Any(x =>
                new[] { x.Name, x.FileSystem, source.IdentifiersIncluded ? x.Label : null }.Any(ClippedText)));

        bool NetworkClipped() =>
            source.Network.Data is { } n && (n.Adapters.Length > 256 || n.Adapters.Any(x =>
                ClippedText(x.Description) || source.IdentifiersIncluded && ClippedText(x.Name)));
    }

    internal static HardwareInventorySnapshot? Trim(HardwareInventorySnapshot source)
    {
        var counts = new[] { source.Cpu.Data?.Processors.Length ?? 0, source.Motherboard.Data?.Items.Length ?? 0,
            source.Memory.Data?.Modules.Length ?? 0, source.Graphics.Data?.Adapters.Length ?? 0,
            source.Disks.Data?.Items.Count ?? 0, source.Volumes.Data?.Items.Length ?? 0, source.Network.Data?.Adapters.Length ?? 0,
            source.Cpu.Data?.Names.Length ?? 0 };
        var index = Array.IndexOf(counts, counts.Max());
        if (counts[index] == 0) return null;
        return index switch
        {
            0 => source with { Cpu = Take(source.Cpu, source.Cpu.Data! with { Processors = source.Cpu.Data!.Processors.Take(Half(counts[0])).ToArray() }) },
            1 => source with { Motherboard = Take(source.Motherboard, source.Motherboard.Data! with { Items = source.Motherboard.Data!.Items.Take(Half(counts[1])).ToArray() }) },
            2 => source with { Memory = Take(source.Memory, source.Memory.Data! with { Modules = source.Memory.Data!.Modules.Take(Half(counts[2])).ToArray() }) },
            3 => source with { Graphics = Take(source.Graphics, source.Graphics.Data! with { Adapters = source.Graphics.Data!.Adapters.Take(Half(counts[3])).ToArray() }) },
            4 => source with { Disks = Take(source.Disks, source.Disks.Data! with { Items = source.Disks.Data!.Items.Take(Half(counts[4])).ToArray() }) },
            5 => source with { Volumes = Take(source.Volumes, source.Volumes.Data! with { Items = source.Volumes.Data!.Items.Take(Half(counts[5])).ToArray() }) },
            6 => source with { Network = Take(source.Network, source.Network.Data! with { Adapters = source.Network.Data!.Adapters.Take(Half(counts[6])).ToArray() }) },
            _ => source with { Cpu = Take(source.Cpu, source.Cpu.Data! with { Names = source.Cpu.Data!.Names.Take(Half(counts[7])).ToArray() }) }
        };
    }

    private static HardwareProcessor Safe(HardwareProcessor value) => value with { Name = Text(value.Name), Manufacturer = Text(value.Manufacturer), SocketDesignation = Text(value.SocketDesignation), DeviceId = Text(value.DeviceId) };
    private static bool ClippedText(string? value) => value is not null && (value.Length > 512 || value.Any(char.IsControl));
    private static HardwareSection<T> Clipped<T>(HardwareSection<T> section, bool clipped) => clipped ? Take(section, section.Data!) : section;
    private static HardwareSection<T> SafeSection<T>(HardwareSection<T> section) => section with
    {
        Status = section.Status is "ok" or "partial" or "unavailable" ? section.Status : "unavailable",
        Issues = section.Issues.Take(16).Where(x => x is "providerUnavailable" or "readFailed" or "truncated").Distinct().ToArray()
    };
    private static HardwareSection<T> Take<T>(HardwareSection<T> section, T data) => section with { Data = data, Truncated = true, Issues = section.Issues.Append("truncated").Distinct().ToArray(), Status = section.Status == "ok" ? "partial" : section.Status };
    private static int Half(int count) => Math.Max(0, count / 2);
    private static string? Text(string? value) => value is null ? null : Responses.Text(value);
}
