using System.ComponentModel;
using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;
using NUnit.Framework;
using WinMon.Diagnostics;
using WinMon.Monitoring;
using WinMon.NetworkService;

namespace WinMon.Tests;

public sealed partial class NetworkPipeTests
{
    private static string CurrentSid()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return identity.User!.Value;
    }

    [Test]
    [NUnit.Framework.Category("Integration")]
    public async Task UnprovisionedToken_CannotSubscribeEvenWhenItCanOpenTestServer()
    {
        var subscribed = false;
        await using var server = new ServerScope(["S-1-5-21-1-2-3-1001"], (_, _) =>
        {
            subscribed = true;
            return Task.FromResult<IAsyncDisposable>(new CallbackDisposable(() => { }));
        });
        await using var client = await server.ConnectAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        Assert.That(async () => await ExchangeAsync(client, "subscribe", timeout.Token),
            Throws.InstanceOf<IOException>());
        Assert.That(subscribed, Is.False);
    }

    [TestCase(1, "terminate")]
    [TestCase(1, "snapshot")]
    [NUnit.Framework.Category("Integration")]
    public async Task InvalidRequest_ClosesOnlyThatConnection(int version, string operation)
    {
        await using var server = new ServerScope([CurrentSid()]);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using (var client = await server.ConnectAsync())
        {
            Assert.That(async () =>
            {
                await NetworkServiceProtocol.WriteAsync(client, new NetworkServiceRequest(version, operation),
                    timeout.Token);
                await NetworkServiceProtocol.ReadAsync<NetworkServiceResponse>(client, timeout.Token);
            }, Throws.InstanceOf<IOException>());
        }

        await using var next = await server.ConnectAsync();
        Assert.That((await ExchangeAsync(next, "status", timeout.Token)).Status, Is.EqualTo("ok"));
    }

    [Test]
    [NUnit.Framework.Category("Integration")]
    public async Task IncompatibleVersion_ReportsServerVersionThenClosesWithoutSubscribing()
    {
        var subscribed = false;
        await using var server = new ServerScope([CurrentSid()], (_, _) =>
        {
            subscribed = true;
            return Task.FromResult<IAsyncDisposable>(new CallbackDisposable(() => { }));
        });
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using (var client = await server.ConnectAsync())
        {
            await NetworkServiceProtocol.WriteAsync(client,
                new NetworkServiceRequest(NetworkServiceProtocol.Version + 1, "subscribe"), timeout.Token);
            var reply = await NetworkServiceProtocol.ReadAsync<NetworkServiceResponse>(client, timeout.Token);
            Assert.Multiple(() =>
            {
                Assert.That(reply.Version, Is.EqualTo(NetworkServiceProtocol.Version));
                Assert.That(reply.Status, Is.EqualTo("version-mismatch"));
                Assert.That(reply.Snapshot, Is.Null);
                Assert.That(subscribed, Is.False);
            });
            Assert.That(
                async () => await NetworkServiceProtocol.ReadAsync<NetworkServiceResponse>(client, timeout.Token),
                Throws.InstanceOf<IOException>());
        }

        await using var next = await server.ConnectAsync();
        Assert.That((await ExchangeAsync(next, "status", timeout.Token)).Status, Is.EqualTo("ok"));
    }

    [Test]
    [NUnit.Framework.Category("Integration")]
    public async Task AbandonedClient_ReleasesLeaseWithoutUnsubscribe()
    {
        var released = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var server = new ServerScope([CurrentSid()], (_, _) =>
            Task.FromResult<IAsyncDisposable>(new CallbackDisposable(() => released.TrySetResult())));
        await using (var client = await server.ConnectAsync())
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await ExchangeAsync(client, "subscribe", timeout.Token);
        }

        await released.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Test]
    [NUnit.Framework.Category("Integration")]
    public async Task OversizedRequest_IsRejectedBeforeWaitingForItsPayload()
    {
        await using var server = new ServerScope([CurrentSid()]);
        await using var client = await server.ConnectAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await client.WriteAsync(BitConverter.GetBytes(NetworkServiceProtocol.MaximumRequestBytes + 1), timeout.Token);
        Assert.That(async () => await NetworkServiceProtocol.ReadAsync<NetworkServiceResponse>(client, timeout.Token),
            Throws.InstanceOf<IOException>());
    }

    [Test]
    [NUnit.Framework.Category("Integration")]
    public async Task ExistingPipeName_IsNotTakenOverAndSpoofServerIsRejected()
    {
        await using var first = new ServerScope([CurrentSid()]);
        var collision = new NetworkPipeServer([CurrentSid()],
            (_, _) => Task.FromResult<IAsyncDisposable>(new CallbackDisposable(() => { })),
            _ => EmptySnapshot(), first.Name);
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        Assert.ThrowsAsync<UnauthorizedAccessException>(() => collision.RunAsync(stop.Token));
        await using var client = await first.ConnectAsync();
        Assert.That(() => NetworkServiceIdentity.Verify(client.SafePipeHandle), Throws.Exception);
        Assert.That((await ExchangeAsync(client, "status", stop.Token)).Status, Is.EqualTo("ok"));
    }

    [Test]
    [NUnit.Framework.Category("Integration")]
    public async Task ClaimedSid_CannotReplaceAuthenticatedToken()
    {
        string? actualSid = null;
        await using var server = new ServerScope([CurrentSid()], (sid, _) =>
        {
            actualSid = sid;
            return Task.FromResult<IAsyncDisposable>(new CallbackDisposable(() => { }));
        });
        await using var client = await server.ConnectAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await NetworkServiceProtocol.WriteAsync(client, new { Version = 1, Operation = "subscribe", Sid = "S-1-5-18" },
            timeout.Token);
        Assert.That((await NetworkServiceProtocol.ReadAsync<NetworkServiceResponse>(client, timeout.Token)).Status,
            Is.EqualTo("ok"));
        Assert.That(actualSid, Is.EqualTo(CurrentSid()));
    }

    [Test]
    [NUnit.Framework.Category("Integration")]
    public async Task RestrictedPipeAcl_DeniesServerCreationAndAllowsDataOnlyConnector()
    {
        var name = $"WinMon-NetworkAclTest-{Guid.NewGuid():N}";
        var sid = CurrentSid();
        Assert.That(ConvertStringSecurityDescriptorToSecurityDescriptor(
            $"D:P(A;;GA;;;SY)(D;;0x4;;;{sid})(A;;0x12019B;;;{sid})",
            1, out var descriptor, out _), Is.True);
        SafePipeHandle handle;
        try
        {
            var attributes = new SecurityAttributes
                { Length = Marshal.SizeOf<SecurityAttributes>(), Descriptor = descriptor };
            handle = CreateNamedPipe($"\\\\.\\pipe\\{name}", 0x40080003, 0x8, 2, 4096, 4096, 10000, ref attributes);
            if (handle.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
            using var second = CreateNamedPipe($"\\\\.\\pipe\\{name}", 0x40000003, 0x8, 2, 4096, 4096, 10000,
                ref attributes);
            var deniedError = Marshal.GetLastWin32Error();
            Assert.That(second.IsInvalid, Is.True, "The data-only SID must not create another server instance.");
            Assert.That(deniedError, Is.EqualTo(5));
        }
        finally
        {
            _ = LocalFree(descriptor);
        }

        await using var server = new NamedPipeServerStream(PipeDirection.InOut, true, false, handle);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var connected = server.WaitForConnectionAsync(timeout.Token);
        await using var client = await NetworkServiceClient.ConnectProtectedPipeAsync(name, timeout.Token);
        await connected;
        Assert.That(GetNamedPipeServerProcessId(client.SafePipeHandle, out var serverPid), Is.True,
            "The restricted client handle must support server identity verification.");
        Assert.That(serverPid, Is.EqualTo((uint)Environment.ProcessId));
        await NetworkServiceProtocol.WriteAsync(client, new NetworkServiceRequest(1, "status"), timeout.Token);
        Assert.That((await NetworkServiceProtocol.ReadAsync<NetworkServiceRequest>(server, timeout.Token)).Operation,
            Is.EqualTo("status"));
        string? actual = null;
        server.RunAsClient(() =>
        {
            using var identity = WindowsIdentity.GetCurrent();
            actual = identity.User!.Value;
        });
        Assert.That(actual, Is.EqualTo(sid));
    }

    [TestCase(-1)]
    [TestCase(0)]
    public void Protocol_RejectsNonPositiveLength(int length)
    {
        using var stream = new MemoryStream(BitConverter.GetBytes(length));
        Assert.ThrowsAsync<InvalidDataException>(async () =>
            await NetworkServiceProtocol.ReadAsync<NetworkServiceRequest>(stream, CancellationToken.None));
    }

    [Test]
    public void Protocol_RejectsTruncatedPayloadAndInvalidJson()
    {
        using var truncated = new MemoryStream([5, 0, 0, 0, 123]);
        Assert.ThrowsAsync<EndOfStreamException>(async () =>
            await NetworkServiceProtocol.ReadAsync<NetworkServiceRequest>(truncated, CancellationToken.None));
        using var invalid = new MemoryStream([1, 0, 0, 0, 123]);
        Assert.ThrowsAsync<InvalidDataException>(async () =>
            await NetworkServiceProtocol.ReadAsync<NetworkServiceRequest>(invalid, CancellationToken.None));
    }

    [Test]
    public async Task Protocol_CanCarryMaximumBoundedLedger()
    {
        var rows = Enumerable.Range(1, 8192).Select(pid => new ProcessNetworkReading(pid, long.MaxValue, long.MaxValue,
            long.MaxValue,
            double.MaxValue, double.MaxValue)).ToArray();
        using var stream = new MemoryStream();
        await NetworkServiceProtocol.WriteAsync(stream,
            new NetworkServiceResponse(1, "ok", EmptySnapshot() with { Processes = rows }), CancellationToken.None);
        Assert.That(stream.Length, Is.LessThanOrEqualTo(NetworkServiceProtocol.MaximumMessageBytes + 4));
        stream.Position = 0;
        Assert.That(
            (await NetworkServiceProtocol.ReadAsync<NetworkServiceResponse>(stream, CancellationToken.None)).Snapshot!
            .Processes, Has.Count.EqualTo(8192));
    }

    private static async Task<NetworkServiceResponse> ExchangeAsync(Stream stream, string operation,
        CancellationToken token)
    {
        await NetworkServiceProtocol.WriteAsync(stream, new NetworkServiceRequest(1, operation), token);
        return await NetworkServiceProtocol.ReadAsync<NetworkServiceResponse>(stream, token);
    }

    [Test]
    [NonParallelizable]
    [NUnit.Framework.Category("Integration")]
    public async Task RapidSnapshotRequests_LogRateLimitAndKeepListenerHealthy()
    {
        var folder = Path.Combine(TestContext.CurrentContext.WorkDirectory,
            "network-rate-" + Guid.NewGuid().ToString("N"));
        using (var log = new DiagnosticLog(folder, "Service"))
        {
            Trace.Listeners.Add(log);
            await using var server = new ServerScope([CurrentSid()]);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await using (var client = await server.ConnectAsync())
            {
                await ExchangeAsync(client, "subscribe", timeout.Token);
                await ExchangeAsync(client, "snapshot", timeout.Token);
                Assert.ThrowsAsync<EndOfStreamException>(async () =>
                    await ExchangeAsync(client, "snapshot", timeout.Token));
            }

            await using var next = await server.ConnectAsync();
            Assert.That((await ExchangeAsync(next, "status", timeout.Token)).Status, Is.EqualTo("ok"));
        }

        var text = File.ReadAllText(Path.Combine(folder, "winmon.log"));
        Assert.That(text, Does.Contain("Network.PipeSnapshotRateExceeded intervalMs="));
        Assert.That(text, Does.Contain("operation=Snapshot stage=Snapshot subscribed=True"));
        TestContext.AddTestAttachment(Path.Combine(folder, "winmon.log"));
    }

    [Test]
    [NonParallelizable]
    [NUnit.Framework.Category("Integration")]
    public async Task ClientPolling_RespectsServerSnapshotIntervalWithoutDisconnecting()
    {
        var folder = Path.Combine(TestContext.CurrentContext.WorkDirectory,
            "network-poll-" + Guid.NewGuid().ToString("N"));
        using var log = new DiagnosticLog(folder, "Service");
        Trace.Listeners.Add(log);
        TestContext.AddTestAttachment(Path.Combine(folder, "winmon.log"));
        await using var server = new ServerScope([CurrentSid()]);
        await using var client = new NetworkServiceClient(await server.ConnectAsync());
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await client.SubscribeAsync(timeout.Token);
        var completed = 0L;
        for (var index = 0; index < 8; index++)
        {
            if (completed != 0)
            {
                await Task.Delay(985, timeout.Token);
                // Leave a fractional millisecond: the old one-shot Task.Delay truncated it to zero.
                while (Stopwatch.GetElapsedTime(completed).TotalMilliseconds < 999.1)
                    Thread.SpinWait(32);
            }

            Assert.That((await client.SnapshotAsync(timeout.Token)).Sequence, Is.EqualTo(index + 1));
            completed = Stopwatch.GetTimestamp();
        }
    }

    private static ProcessNetworkSnapshot EmptySnapshot()
    {
        return new ProcessNetworkSnapshot(Guid.NewGuid(), 1, 12, 10, 1, 9, ProcessNetworkQuality.Provisional, 0, 0, []);
    }

    private sealed class ServerScope : IAsyncDisposable
    {
        private readonly CancellationTokenSource _stop = new();
        private readonly Task _running;
        internal string Name { get; } = $"WinMon-NetworkPipeTest-{Guid.NewGuid():N}";

        internal ServerScope(IReadOnlyCollection<string> users,
            Func<string, CancellationToken, Task<IAsyncDisposable>>? subscribe = null)
        {
            var snapshot = EmptySnapshot();
            var server = new NetworkPipeServer(users,
                subscribe ?? ((_, _) => Task.FromResult<IAsyncDisposable>(new CallbackDisposable(() => { }))),
                _ => snapshot, Name);
            _running = server.RunAsync(_stop.Token);
        }

        internal async Task<NamedPipeClientStream> ConnectAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            return await NetworkServiceClient.ConnectProtectedPipeAsync(Name, timeout.Token);
        }

        public async ValueTask DisposeAsync()
        {
            _stop.Cancel();
            try
            {
                await _running.WaitAsync(TimeSpan.FromSeconds(10));
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                _stop.Dispose();
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SecurityAttributes
    {
        public int Length;
        public IntPtr Descriptor;
        public IntPtr Inherit;
    }

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ConvertStringSecurityDescriptorToSecurityDescriptor(string sddl, uint revision,
        out IntPtr descriptor, out uint size);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafePipeHandle CreateNamedPipe(string name, uint openMode, uint pipeMode, uint instances,
        uint output, uint input, uint timeout, ref SecurityAttributes attributes);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint processId);
}
