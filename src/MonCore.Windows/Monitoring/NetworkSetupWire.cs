using System.Buffers.Binary;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace WinMon.Monitoring;

/// <summary>Bounded protocol used only by the elevated network-service setup helper.</summary>
public static class NetworkSetupWire
{
    public const int Version = 1;
    public const int MaximumMessageBytes = 4096;

    public static async ValueTask WriteAsync<T>(Stream stream, T value, JsonTypeInfo<T> typeInfo,
        CancellationToken token)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, typeInfo);
        if (bytes.Length is <= 0 or > MaximumMessageBytes)
            throw new InvalidDataException("Network setup message is too large.");

        var length = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(length, bytes.Length);
        await stream.WriteAsync(length, token).ConfigureAwait(false);
        await stream.WriteAsync(bytes, token).ConfigureAwait(false);
        await stream.FlushAsync(token).ConfigureAwait(false);
    }

    public static async ValueTask<T> ReadAsync<T>(Stream stream, JsonTypeInfo<T> typeInfo, CancellationToken token)
    {
        var length = new byte[sizeof(int)];
        await ReadExactlyAsync(stream, length, token).ConfigureAwait(false);
        var count = BinaryPrimitives.ReadInt32LittleEndian(length);
        if (count is <= 0 or > MaximumMessageBytes)
            throw new InvalidDataException("Network setup message length is invalid.");

        var bytes = GC.AllocateUninitializedArray<byte>(count);
        await ReadExactlyAsync(stream, bytes, token).ConfigureAwait(false);
        try
        {
            return JsonSerializer.Deserialize(bytes, typeInfo) ??
                   throw new InvalidDataException("Network setup message is invalid.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("Network setup message is invalid.", exception);
        }
    }

    private static async ValueTask ReadExactlyAsync(Stream stream, Memory<byte> target, CancellationToken token)
    {
        while (!target.IsEmpty)
        {
            var read = await stream.ReadAsync(target, token).ConfigureAwait(false);
            if (read == 0) throw new EndOfStreamException("Network setup pipe closed.");
            target = target[read..];
        }
    }
}

public sealed record NetworkSetupRequest(int Version, string Token, string Operation);

public sealed record NetworkSetupReply(int Version, string Phase, string? FailurePhase = null, int ErrorCode = 0);

public sealed record NetworkSetupCommit(int Version, bool Commit);

[JsonSerializable(typeof(NetworkSetupRequest))]
[JsonSerializable(typeof(NetworkSetupReply))]
[JsonSerializable(typeof(NetworkSetupCommit))]
public sealed partial class NetworkSetupWireJsonContext : JsonSerializerContext;