using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;

namespace WinMon.NetworkProbe;

internal static class SocketWorkload
{
    internal static readonly string[] Scenarios =
        ["tcp4-existing", "tcp6-existing", "tcp4-short", "tcp6-short", "udp4", "udp6", "tcp4-load", "tcp6-load", "udp4-load", "udp6-load"];

    internal static async Task<int> RunAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var existing = new Dictionary<bool, (TcpClient Client, TcpClient Server, TcpListener Listener)>();
        try
        {
            existing.Add(false, await ConnectAsync(false, timeout.Token));
            if (Socket.OSSupportsIPv6) existing.Add(true, await ConnectAsync(true, timeout.Token));
            Console.WriteLine("ready");
            while (await Console.In.ReadLineAsync(timeout.Token) is { } scenario && scenario != "stop")
            {
                if (!Scenarios.Contains(scenario)) throw new ArgumentException("Unknown workload scenario.");
                var ipv6 = scenario.Contains('6');
                if (ipv6 && !Socket.OSSupportsIPv6)
                {
                    Console.WriteLine(JsonSerializer.Serialize(new WorkloadResult(scenario, 0, 0, "IPv6 unavailable")));
                    continue;
                }
                const int bulkBytes = 16 * 1024 * 1024;
                const int shortBytes = 4096;
                const int connectionCount = 32;
                const int datagrams = 1024;
                const int datagramBytes = 1024;
                long sent;
                if (scenario.EndsWith("existing", StringComparison.Ordinal))
                {
                    var pair = existing[ipv6];
                    await TransferAsync(pair.Client, pair.Server, bulkBytes, timeout.Token);
                    sent = bulkBytes;
                }
                else if (scenario.EndsWith("short", StringComparison.Ordinal))
                {
                    for (var index = 0; index < connectionCount; index++)
                    {
                        var pair = await ConnectAsync(ipv6, timeout.Token);
                        try { await TransferAsync(pair.Client, pair.Server, shortBytes, timeout.Token); }
                        finally { pair.Client.Dispose(); pair.Server.Dispose(); pair.Listener.Stop(); }
                    }
                    sent = shortBytes * connectionCount;
                }
                else if (scenario.EndsWith("load", StringComparison.Ordinal))
                {
                    var started = Stopwatch.GetTimestamp();
                    sent = 0;
                    do
                    {
                        if (scenario.StartsWith("tcp", StringComparison.Ordinal))
                        {
                            var pair = existing[ipv6];
                            await TransferAsync(pair.Client, pair.Server, bulkBytes, timeout.Token);
                            sent += bulkBytes;
                        }
                        else
                        {
                            await DatagramsAsync(ipv6, datagrams, datagramBytes, timeout.Token);
                            sent += datagrams * datagramBytes;
                        }
                    } while (Stopwatch.GetElapsedTime(started).TotalSeconds < 3);
                }
                else
                {
                    await DatagramsAsync(ipv6, datagrams, datagramBytes, timeout.Token);
                    sent = datagrams * datagramBytes;
                }
                // Both socket ends belong to this worker, so each payload is sent and received once.
                Console.WriteLine(JsonSerializer.Serialize(new WorkloadResult(scenario, sent, sent, null)));
            }
            return 0;
        }
        finally
        {
            foreach (var pair in existing.Values)
            {
                pair.Client.Dispose();
                pair.Server.Dispose();
                pair.Listener.Stop();
            }
        }
    }

    private static async Task<(TcpClient Client, TcpClient Server, TcpListener Listener)> ConnectAsync(bool ipv6, CancellationToken token)
    {
        var listener = new TcpListener(ipv6 ? IPAddress.IPv6Loopback : IPAddress.Loopback, 0);
        listener.Start();
        var client = new TcpClient(ipv6 ? AddressFamily.InterNetworkV6 : AddressFamily.InterNetwork);
        try
        {
            await client.ConnectAsync((IPEndPoint)listener.LocalEndpoint, token);
            var server = await listener.AcceptTcpClientAsync(token);
            return (client, server, listener);
        }
        catch { client.Dispose(); listener.Stop(); throw; }
    }

    private static async Task TransferAsync(TcpClient sender, TcpClient receiver, int bytes, CancellationToken token)
    {
        var read = Task.Run(async () =>
        {
            var buffer = new byte[64 * 1024];
            var remaining = bytes;
            while (remaining > 0)
            {
                var count = await receiver.GetStream().ReadAsync(buffer.AsMemory(0, Math.Min(buffer.Length, remaining)), token);
                if (count == 0) throw new IOException("TCP workload closed before all payload bytes arrived.");
                remaining -= count;
            }
        }, token);
        var payload = new byte[64 * 1024];
        for (var remaining = bytes; remaining > 0;)
        {
            var count = Math.Min(payload.Length, remaining);
            await sender.GetStream().WriteAsync(payload.AsMemory(0, count), token);
            remaining -= count;
        }
        await read;
    }

    private static async Task DatagramsAsync(bool ipv6, int count, int bytes, CancellationToken token)
    {
        var address = ipv6 ? IPAddress.IPv6Loopback : IPAddress.Loopback;
        using var sender = new UdpClient(new IPEndPoint(address, 0));
        using var receiver = new UdpClient(new IPEndPoint(address, 0));
        var endpoint = (IPEndPoint)receiver.Client.LocalEndPoint!;
        var payload = new byte[bytes];
        for (var index = 0; index < count; index++)
        {
            await sender.SendAsync(payload, endpoint, token);
            var received = await receiver.ReceiveAsync(token);
            if (received.Buffer.Length != bytes) throw new IOException("UDP workload payload length mismatch.");
        }
    }
}

internal sealed record WorkloadResult(string Scenario, long PayloadSent, long PayloadReceived, string? Skipped);
