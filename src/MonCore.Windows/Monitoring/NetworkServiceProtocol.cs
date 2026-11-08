using System.Buffers.Binary;
using System.Text.Json;
using WinMon.Monitoring;

namespace WinMon.Monitoring;

/// <summary>Bounded local protocol shared only by the Windows client and network service.</summary>
public static class NetworkServiceProtocol
{
    public const int Version = 1;
    public const string ServiceName = "WinMonNetworkMonitor";
    public const string PipeName = "WinMonNetworkMonitor.v1";
    public const int MaximumMessageBytes = 2 * 1024 * 1024;
    public const int MaximumRequestBytes = 4096;
    public static string InstallDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WinMon", "NetworkMonitor");
    internal static string InstalledExecutable => Path.Combine(InstallDirectory, "WinMon.NetworkService.exe");

    public static async Task WriteAsync<T>(Stream stream, T value, CancellationToken token)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value);
        if (bytes.Length is <= 0 or > MaximumMessageBytes) throw new InvalidDataException("Network service message is too large.");
        var length = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(length, bytes.Length);
        await stream.WriteAsync(length, token).ConfigureAwait(false);
        await stream.WriteAsync(bytes, token).ConfigureAwait(false);
        await stream.FlushAsync(token).ConfigureAwait(false);
    }

    public static async Task<T> ReadAsync<T>(Stream stream, CancellationToken token, int maximumBytes = MaximumMessageBytes)
    {
        if (maximumBytes <= 0 || maximumBytes > MaximumMessageBytes) throw new ArgumentOutOfRangeException(nameof(maximumBytes));
        var length = new byte[sizeof(int)];
        await ReadExactlyAsync(stream, length, token).ConfigureAwait(false);
        var count = BinaryPrimitives.ReadInt32LittleEndian(length);
        if (count <= 0 || count > maximumBytes) throw new InvalidDataException("Network service message length is invalid.");
        var bytes = GC.AllocateUninitializedArray<byte>(count);
        await ReadExactlyAsync(stream, bytes, token).ConfigureAwait(false);
        try
        {
            return JsonSerializer.Deserialize<T>(bytes) ?? throw new InvalidDataException("Network service message is invalid.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("Network service message is invalid.", exception);
        }
    }

    private static async Task ReadExactlyAsync(Stream stream, Memory<byte> target, CancellationToken token)
    {
        while (!target.IsEmpty)
        {
            var read = await stream.ReadAsync(target, token).ConfigureAwait(false);
            if (read == 0) throw new EndOfStreamException("Network service pipe closed.");
            target = target[read..];
        }
    }
}

public sealed record NetworkServiceRequest(int Version, string Operation);
public sealed record NetworkServiceResponse(int Version, string Status, ProcessNetworkSnapshot? Snapshot = null);
