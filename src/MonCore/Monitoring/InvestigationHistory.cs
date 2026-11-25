using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WinMon.Monitoring;

/// <summary>Immutable aligned observations saved with resource history; never resumes live traffic counters.</summary>
public sealed class InvestigationHistory
{
    internal sealed record Frame(SampleKey Key, IReadOnlyList<ServiceGroup>? Services, bool ServicesAvailable,
        NetworkConnectionsFrame? Connections, IReadOnlyList<ProcessUsage>? Owners, ProcessNetworkFrame? Network,
        UsageHistory.Frame? Usage);

    private readonly Dictionary<SampleKey, Frame> _frames;
    internal InvestigationHistory(IEnumerable<Frame> frames) => _frames = frames.DistinctBy(frame => frame.Key)
        .ToDictionary(frame => frame.Key);

    internal Frame? Find(MonitorSample sample) => _frames.GetValueOrDefault(SampleKey.Of(sample));

    internal static bool Valid(Frame frame) => ValidServices(frame.Services) && ValidOwners(frame.Owners) &&
        ValidConnections(frame.Connections) && ValidNetwork(frame.Network) && ValidUsage(frame.Usage);

    private static bool ValidProcess(ProcessUsage process) => process is not null && process.Name is not null &&
        process.ImagePath is not null && process.AppName is not null && process.Publisher is not null &&
        process.ProductName is not null && process.FileVersion is not null && process.ProductVersion is not null &&
        process.GpuEngine is not null && process.GpuId is not null && process.MemberIds is not null;

    private static bool ValidServices(IReadOnlyList<ServiceGroup>? groups) =>
        groups is null || groups.Count <= ServiceHistory.MaximumObservations &&
        groups.All(group => group is not null && group.Services is { Count: > 0 } &&
            group.Services.All(service => service is not null && service.Name is not null && service.DisplayName is not null) &&
            group.Processes is not null && group.Processes.All(ValidProcess) && (group.Host is null || ValidProcess(group.Host)));

    private static bool ValidOwners(IReadOnlyList<ProcessUsage>? owners) =>
        owners is null || owners.Count <= 100_000 && owners.All(ValidProcess);

    private static bool ValidConnections(NetworkConnectionsFrame? connections) =>
        connections is null || connections.Connections is not null &&
        connections.Connections.Count <= 100_000 && connections.Ipv4 is not null &&
        connections.Ipv6 is not null && connections.Connections.All(connection =>
            connection is not null && connection.LocalEndpoint is not null);

    private static bool ValidNetwork(ProcessNetworkFrame? network) =>
        network is null || network.Processes is not null && network.Flows is not null &&
        network.Owners is not null && network.Owners.All(ValidProcess) &&
        network.Processes.Count <= 100_000 && network.Flows.Count <= 100_000 &&
        network.Processes.All(reading => reading is not null) && network.Flows.All(flow =>
            flow is not null && flow.LocalEndpoint is not null && flow.RemoteEndpoint is not null);

    private static bool ValidUsage(UsageHistory.Frame? usage) =>
        usage is null || usage.Observations is { Length: <= 500_000 } &&
        usage.Observations.All(observation => observation.Owner is { } owner &&
            owner.Key is not null && owner.Name is not null && owner.ImagePath is not null &&
            owner.AppName is not null && owner.Publisher is not null);

    internal static InvestigationHistory Capture(MonitorSession session)
    {
        var services = session.Services.Frames.ToDictionary(frame => SampleKey.Of(frame.Sample));
        var connections = session.Connections.Captures.ToDictionary(frame => SampleKey.Of(frame.Sample));
        var usage = session.UsageHistory.Export().ToDictionary(frame => frame.Key);
        return new InvestigationHistory(session.History.Samples.Select(sample =>
        {
            var key = SampleKey.Of(sample);
            services.TryGetValue(key, out var service);
            connections.TryGetValue(key, out var connection);
            return new Frame(key, service?.Groups, service?.Available == true, connection?.Connections,
                connection?.Owners, connection?.Network, usage.GetValueOrDefault(key));
        }));
    }

    internal InvestigationHistory Align(IReadOnlyList<MonitorSample> samples)
    {
        var byTime = _frames.Values.ToLookup(frame => frame.Key.Timestamp);
        return new InvestigationHistory(samples.Select(sample =>
        {
            // Normal restart may rebase the monotonic clock, including after reboot. Wall time is unchanged.
            var original = Find(sample) ?? byTime[sample.Timestamp].FirstOrDefault();
            var key = SampleKey.Of(sample);
            return original is null ? new Frame(key, null, false, null, null, null, null) : original with
            { Key = key, Usage = original.Usage is { } usage ? usage with { Key = key } : null };
        }));
    }

    internal void Restore(MonitorSession session)
    {
        session.Services.Resize(session.History.Capacity);
        session.Connections.Resize(session.History.Capacity);
        session.UsageHistory.Resize(session.History.Capacity);
        foreach (var sample in session.History.Samples)
        {
            if (Find(sample) is not { } frame) continue;
            if (frame.Services is { } groups)
                session.Services.Add(new ServiceFrame(sample, groups, frame.ServicesAvailable));
            if (frame.Owners is { } owners)
                session.Connections.Add(sample, frame.Connections, owners, frame.Network);
            if (frame.Usage is { } usage) session.UsageHistory.Restore(usage);
        }
    }

    internal static readonly JsonSerializerOptions Json = new()
    {
        IgnoreReadOnlyProperties = true,
        Converters = { new EndpointConverter(), new SetConverter<ProcessLifetimeIdentity>(),
            new SetConverter<ProcessNetworkFlowIdentity>(), new LifetimeConverter() }
    };

    private sealed class LifetimeConverter : JsonConverter<ProcessLifetimeIdentity>
    {
        public override ProcessLifetimeIdentity Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
        {
            using var json = JsonDocument.ParseValue(ref reader);
            var value = json.RootElement;
            return new ProcessLifetimeIdentity(value.GetProperty("pid").GetInt32(),
                value.GetProperty("token").GetInt64(), (ProcessLifetimeDomain)value.GetProperty("domain").GetInt32());
        }
        public override void Write(Utf8JsonWriter writer, ProcessLifetimeIdentity value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WriteNumber("pid", value.ProcessId);
            writer.WriteNumber("token", value.StartToken);
            writer.WriteNumber("domain", (int)value.Domain);
            writer.WriteEndObject();
        }
    }

    private sealed class EndpointConverter : JsonConverter<IPEndPoint>
    {
        public override IPEndPoint Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options) =>
            IPEndPoint.Parse(reader.GetString()!);
        public override void Write(Utf8JsonWriter writer, IPEndPoint value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.ToString());
    }

    private sealed class SetConverter<T> : JsonConverter<IReadOnlySet<T>>
    {
        public override IReadOnlySet<T>? Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options) =>
            JsonSerializer.Deserialize<HashSet<T>>(ref reader, options);
        public override void Write(Utf8JsonWriter writer, IReadOnlySet<T> value, JsonSerializerOptions options) =>
            JsonSerializer.Serialize(writer, value.ToArray(), options);
    }
}
