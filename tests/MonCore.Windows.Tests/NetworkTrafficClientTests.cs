using System.IO;
using NUnit.Framework;
using WinMon.Monitoring;

namespace WinMon.Tests;

[TestFixture]
public sealed class NetworkTrafficClientTests
{
    [Test]
    public async Task Run_PublishesSnapshotsAndDisposesConnectionWhenCancelled()
    {
        using var cancellation = new CancellationTokenSource();
        var connection = new FakeConnection(
            [Snapshot(ProcessNetworkQuality.WarmingUp), Snapshot(ProcessNetworkQuality.None)], cancellation);
        await using var client = new NetworkTrafficClient(_ => Task.FromResult<INetworkTrafficConnection>(connection));

        await client.Run(cancellation.Token);

        Assert.Multiple(() =>
        {
            Assert.That(client.Sample.SessionId, Is.Not.EqualTo(Guid.Empty));
            Assert.That(client.Sample.Snapshot, Is.Not.Null);
            Assert.That(client.Sample.State, Is.EqualTo(NetworkTrafficState.Active));
            Assert.That(connection.SubscribeCalls, Is.EqualTo(1));
            Assert.That(connection.Disposed, Is.True);
        });
    }

    [Test]
    public async Task Run_RetriesUnavailableServiceWithTheSameSession()
    {
        using var cancellation = new CancellationTokenSource();
        var recovered = new FakeConnection([Snapshot(ProcessNetworkQuality.None)], cancellation);
        var attempts = 0;
        Guid gapSession = Guid.Empty;
        NetworkTrafficClient? client = null;
        client = new NetworkTrafficClient(
            _ => ++attempts == 1
                ? Task.FromException<INetworkTrafficConnection>(new IOException("pipe unavailable"))
                : Task.FromResult<INetworkTrafficConnection>(recovered),
            (_, _) =>
            {
                gapSession = client!.Sample.SessionId;
                return Task.CompletedTask;
            });

        try
        {
            await client.Run(cancellation.Token);
        }
        finally
        {
            await client.DisposeAsync();
        }

        Assert.Multiple(() =>
        {
            Assert.That(attempts, Is.EqualTo(2));
            Assert.That(gapSession, Is.Not.EqualTo(Guid.Empty));
            Assert.That(client.Sample.SessionId, Is.EqualTo(gapSession));
            Assert.That(client.Sample.State, Is.EqualTo(NetworkTrafficState.Active));
            Assert.That(recovered.Disposed, Is.True);
        });
    }

    [Test]
    public async Task Run_StopsOnProtocolMismatchWithoutRetrying()
    {
        var connection = new FakeConnection([], null)
        {
            SubscribeException = new NetworkServiceProtocolMismatchException(2, 1)
        };
        var delays = 0;
        await using var client = new NetworkTrafficClient(
            _ => Task.FromResult<INetworkTrafficConnection>(connection),
            (_, _) =>
            {
                delays++;
                return Task.CompletedTask;
            });

        await client.Run(CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(client.Sample.State, Is.EqualTo(NetworkTrafficState.UpdateRequired));
            Assert.That(client.Sample.Snapshot, Is.Null);
            Assert.That(delays, Is.Zero);
            Assert.That(connection.Disposed, Is.True);
        });
    }

    [Test]
    public async Task Run_TimesOutOperationsAndPublishesUnavailableGap()
    {
        using var cancellation = new CancellationTokenSource();
        await using var client = new NetworkTrafficClient(
            async token =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                throw new AssertionException("The operation timeout should cancel the connection.");
            },
            (_, _) =>
            {
                cancellation.Cancel();
                return Task.FromCanceled(cancellation.Token);
            },
            operationTimeout: TimeSpan.FromMilliseconds(10));

        await client.Run(cancellation.Token);

        Assert.Multiple(() =>
        {
            Assert.That(client.Sample.State, Is.EqualTo(NetworkTrafficState.Unavailable));
            Assert.That(client.Sample.Snapshot, Is.Null);
        });
    }

    [Test]
    public async Task Run_UnavailableSnapshotPublishesGapAndRetries()
    {
        using var cancellation = new CancellationTokenSource();
        var connection = new FakeConnection([Snapshot(ProcessNetworkQuality.Unavailable)], null);
        await using var client = new NetworkTrafficClient(
            _ => Task.FromResult<INetworkTrafficConnection>(connection),
            (_, _) =>
            {
                cancellation.Cancel();
                return Task.FromCanceled(cancellation.Token);
            });

        await client.Run(cancellation.Token);

        Assert.Multiple(() =>
        {
            Assert.That(client.Sample.State, Is.EqualTo(NetworkTrafficState.Unavailable));
            Assert.That(client.Sample.Snapshot, Is.Null);
            Assert.That(connection.Disposed, Is.True);
        });
    }

    [Test]
    public async Task DisposeAsync_WaitsForPollingCleanupAndPreventsLaterRuns()
    {
        var connection = new BlockingConnection();
        var client = new NetworkTrafficClient(_ => Task.FromResult<INetworkTrafficConnection>(connection));
        var run = client.Run(CancellationToken.None);
        await connection.SnapshotStarted.Task;

        await client.DisposeAsync();

        Assert.Multiple(() =>
        {
            Assert.That(run.IsCompletedSuccessfully, Is.True);
            Assert.That(connection.Disposed, Is.True);
            Assert.That(async () => await client.Run(CancellationToken.None),
                Throws.TypeOf<ObjectDisposedException>());
        });
    }

    private static ProcessNetworkSnapshot Snapshot(ProcessNetworkQuality quality)
    {
        return new ProcessNetworkSnapshot(Guid.NewGuid(), 1, 100, 10, 20, 70, quality, 0, 0, [])
        {
            SubscriptionId = Guid.NewGuid()
        };
    }

    private sealed class FakeConnection(IReadOnlyList<ProcessNetworkSnapshot> snapshots,
        CancellationTokenSource? cancellation) : INetworkTrafficConnection
    {
        private int _snapshotIndex;

        public Exception? SubscribeException { get; init; }
        public int SubscribeCalls { get; private set; }
        public bool Disposed { get; private set; }

        public Task SubscribeAsync(CancellationToken token)
        {
            SubscribeCalls++;
            return SubscribeException is null ? Task.CompletedTask : Task.FromException(SubscribeException);
        }

        public Task<ProcessNetworkSnapshot> SnapshotAsync(CancellationToken token)
        {
            if (_snapshotIndex < snapshots.Count) return Task.FromResult(snapshots[_snapshotIndex++]);
            cancellation?.Cancel();
            return Task.FromCanceled<ProcessNetworkSnapshot>(token);
        }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class BlockingConnection : INetworkTrafficConnection
    {
        public TaskCompletionSource SnapshotStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Disposed { get; private set; }

        public Task SubscribeAsync(CancellationToken token) => Task.CompletedTask;

        public async Task<ProcessNetworkSnapshot> SnapshotAsync(CancellationToken token)
        {
            SnapshotStarted.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            throw new AssertionException("Cancellation should end the polling operation.");
        }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }
}
