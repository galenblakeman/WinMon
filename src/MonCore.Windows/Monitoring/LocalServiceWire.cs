using System.Buffers.Binary;
using System.Text.Json;
using MonCore.Monitoring;

namespace MonCore.Windows.Monitoring;

/// <summary>Bounded local protocol shared only by the Windows client and network service.</summary>
internal static class LocalServiceWire
{
    internal const int MaximumMessageBytes = 2 * 1024 * 1024;
    internal const int MaximumRequestBytes = 4096;
    public static async Task WriteAsync<T>(Stream stream, T value, CancellationToken token)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value);
        if (bytes.Length is <= 0 or > MaximumMessageBytes)
            throw new InvalidDataException("Local service message is too large.");
        var length = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(length, bytes.Length);
        await stream.WriteAsync(length, token).ConfigureAwait(false);
        await stream.WriteAsync(bytes, token).ConfigureAwait(false);
        await stream.FlushAsync(token).ConfigureAwait(false);
    }

    public static async Task<T> ReadAsync<T>(Stream stream, CancellationToken token,
        int maximumBytes = MaximumMessageBytes)
    {
        if (maximumBytes is <= 0 or > MaximumMessageBytes) throw new ArgumentOutOfRangeException(nameof(maximumBytes));
        var length = new byte[sizeof(int)];
        await ReadExactlyAsync(stream, length, token).ConfigureAwait(false);
        var count = BinaryPrimitives.ReadInt32LittleEndian(length);
        if (count <= 0 || count > maximumBytes)
            throw new InvalidDataException("Local service message length is invalid.");
        var bytes = GC.AllocateUninitializedArray<byte>(count);
        await ReadExactlyAsync(stream, bytes, token).ConfigureAwait(false);
        try
        {
            return JsonSerializer.Deserialize<T>(bytes) ??
                   throw new InvalidDataException("Local service message is invalid.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("Local service message is invalid.", exception);
        }
    }

    private static async Task ReadExactlyAsync(Stream stream, Memory<byte> target, CancellationToken token)
    {
        while (!target.IsEmpty)
        {
            var read = await stream.ReadAsync(target, token).ConfigureAwait(false);
            if (read == 0) throw new EndOfStreamException("Local service pipe closed.");
            target = target[read..];
        }
    }
}
