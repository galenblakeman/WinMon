using System.IO.Pipes;
using System.Diagnostics;
using System.Security.Principal;
using MonCore.Windows.Monitoring;
using NUnit.Framework;

namespace WinMon.SensorService.Tests;

[TestFixture]
public sealed class SensorPipeServerTests
{
    [Test]
    [Category("Integration")]
    public async Task AllowedSid_CanSubscribeReadAndUnsubscribeWithOneLease()
    {
        var released = 0;
        await using var server = new ServerScope([CurrentSid()], (_, _) =>
            Task.FromResult<IAsyncDisposable>(new CallbackDisposable(() => Interlocked.Increment(ref released))));
        await using var client = await server.ConnectAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        Assert.That((await ExchangeAsync(client, "subscribe", timeout.Token)).Status, Is.EqualTo("ok"));
        var snapshot = (await ExchangeAsync(client, "snapshot", timeout.Token)).Snapshot!;
        Assert.Multiple(() =>
        {
            Assert.That(snapshot.Quality, Is.EqualTo(SensorServiceQuality.Complete));
            Assert.That(snapshot.CpuTemperature!.Celsius, Is.EqualTo(51));
        });
        Assert.That((await ExchangeAsync(client, "unsubscribe", timeout.Token)).Status, Is.EqualTo("ok"));
        Assert.That(released, Is.EqualTo(1));
    }

    [Test]
    [Category("Integration")]
    public async Task UnlistedSid_CannotIssueRequestsOnTheProtectedPipe()
    {
        await using var server = new ServerScope(["S-1-5-21-1-2-3-1001"]);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var client = new NamedPipeClientStream(".", server.Name, PipeDirection.InOut, PipeOptions.Asynchronous);
        await client.ConnectAsync(timeout.Token);
        // Rejection can close the connection before the write completes or while the client awaits a reply.
        Assert.That(async () => await ExchangeAsync(client, "status", timeout.Token),
            Throws.InstanceOf<IOException>());
    }

    [Test]
    [Category("Integration")]
    public async Task IncompatibleVersion_ReportsVersionAndDoesNotLease()
    {
        var subscribed = false;
        await using var server = new ServerScope([CurrentSid()], (_, _) =>
        {
            subscribed = true;
            return Task.FromResult<IAsyncDisposable>(new CallbackDisposable(() => { }));
        });
        await using var client = await server.ConnectAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await SensorServiceProtocol.WriteAsync(client,
            new SensorServiceRequest(SensorServiceProtocol.Version + 1, "subscribe"), timeout.Token);
        var response = await SensorServiceProtocol.ReadAsync<SensorServiceResponse>(client, timeout.Token);
        Assert.Multiple(() =>
        {
            Assert.That(response.Status, Is.EqualTo("version-mismatch"));
            Assert.That(response.Version, Is.EqualTo(SensorServiceProtocol.Version));
            Assert.That(subscribed, Is.False);
        });
    }

    [Test]
    [Category("Integration")]
    public async Task ControlRequestLimit_ClosesOnlyTheOffendingConnection()
    {
        await using var server = new ServerScope([CurrentSid()]);
        await using var client = await server.ConnectAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        for (var index = 0; index < 16; index++)
            Assert.That((await ExchangeAsync(client, "status", timeout.Token)).Status, Is.EqualTo("ok"));
        Assert.That(async () => await ExchangeAsync(client, "status", timeout.Token), Throws.InstanceOf<EndOfStreamException>());
        await using var next = await server.ConnectAsync();
        Assert.That((await ExchangeAsync(next, "status", timeout.Token)).Status, Is.EqualTo("ok"));
    }

    [Test]
    [Category("Integration")]
    public async Task SnapshotRateLimit_ClosesOnlyTheConnectionThatRequestsTwiceWithinOneSecond()
    {
        await using var server = new ServerScope([CurrentSid()]);
        await using var client = await server.ConnectAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        Assert.That((await ExchangeAsync(client, "subscribe", timeout.Token)).Status, Is.EqualTo("ok"));
        Assert.That((await ExchangeAsync(client, "snapshot", timeout.Token)).Status, Is.EqualTo("ok"));
        Assert.That(async () => await ExchangeAsync(client, "snapshot", timeout.Token),
            Throws.InstanceOf<EndOfStreamException>());

        await using var next = await server.ConnectAsync();
        Assert.That((await ExchangeAsync(next, "status", timeout.Token)).Status, Is.EqualTo("ok"));
    }

    [Test]
    [Category("Integration")]
    public async Task DisconnectAfterSubscription_ReleasesTheLeaseWithoutAnExplicitUnsubscribe()
    {
        var released = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var server = new ServerScope([CurrentSid()], (_, _) =>
            Task.FromResult<IAsyncDisposable>(new CallbackDisposable(() => released.TrySetResult())));
        var client = await server.ConnectAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        Assert.That((await ExchangeAsync(client, "subscribe", timeout.Token)).Status, Is.EqualTo("ok"));

        await client.DisposeAsync();
        await released.Task.WaitAsync(timeout.Token);
        Assert.That(released.Task.IsCompletedSuccessfully, Is.True);
    }

    private static async Task<SensorServiceResponse> ExchangeAsync(Stream stream, string operation, CancellationToken token)
    {
        await SensorServiceProtocol.WriteAsync(stream, new SensorServiceRequest(SensorServiceProtocol.Version, operation), token);
        return await SensorServiceProtocol.ReadAsync<SensorServiceResponse>(stream, token);
    }

    private static string CurrentSid()
    {
        using var identity = WindowsIdentity.GetCurrent(TokenAccessLevels.Query);
        return identity.User!.Value;
    }

    private sealed class ServerScope : IAsyncDisposable
    {
        private readonly CancellationTokenSource _stop = new();
        private readonly Task _running;
        internal string Name { get; } = "WinMon-SensorPipeTest-" + Guid.NewGuid().ToString("N");

        internal ServerScope(IReadOnlyCollection<string> users,
            Func<string, CancellationToken, Task<IAsyncDisposable>>? subscribe = null)
        {
            var snapshot = new SensorCollectorSnapshot(Guid.NewGuid(), 1, Stopwatch.GetTimestamp(), Stopwatch.Frequency,
                SensorCollectorQuality.Complete, new SensorReading("cpu/0", "CPU", "package", "CPU Package", 51,
                    "test", Stopwatch.GetTimestamp()));
            var server = new SensorPipeServer(users,
                subscribe ?? ((_, _) => Task.FromResult<IAsyncDisposable>(new CallbackDisposable(() => { }))), _ => snapshot, Name);
            _running = server.RunAsync(_stop.Token);
        }

        internal async Task<NamedPipeClientStream> ConnectAsync()
        {
            var client = new NamedPipeClientStream(".", Name,
                PipeAccessRights.ReadData | PipeAccessRights.WriteData | PipeAccessRights.ReadPermissions |
                PipeAccessRights.Synchronize, PipeOptions.Asynchronous, TokenImpersonationLevel.Identification,
                HandleInheritability.None);
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await client.ConnectAsync(timeout.Token);
                return client;
            }
            catch { client.Dispose(); throw; }
        }

        public async ValueTask DisposeAsync()
        {
            _stop.Cancel();
            try { await _running.WaitAsync(TimeSpan.FromSeconds(10)); }
            catch (OperationCanceledException) { }
            finally { _stop.Dispose(); }
        }
    }

    private sealed class CallbackDisposable(Action release) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() { release(); return ValueTask.CompletedTask; }
    }
}
