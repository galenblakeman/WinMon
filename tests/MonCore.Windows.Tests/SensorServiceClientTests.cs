using System.Buffers.Binary;
using System.Diagnostics;
using MonCore.Windows.Monitoring;
using NUnit.Framework;

namespace MonCore.Windows.Tests;

[TestFixture]
public sealed class SensorServiceClientTests
{
    [Test]
    public async Task Subscription_IsIdempotentAndDisposeUnsubscribesOnce()
    {
        using var pipe = await ScriptedPipe.Responses(new(1, "ok"), new(1, "ok"));
        var client = new SensorServiceClient(pipe);
        await client.SubscribeAsync(CancellationToken.None);
        await client.SubscribeAsync(CancellationToken.None);
        await client.DisposeAsync();
        await client.DisposeAsync();
        Assert.That(await pipe.Operations(), Is.EqualTo(new[] { "subscribe", "unsubscribe" }));
        Assert.That(pipe.Disposed, Is.True);
    }

    [Test]
    public async Task Snapshot_RequiresAnActiveSubscriptionBeforeWriting()
    {
        using var pipe = await ScriptedPipe.Responses();
        await using var client = new SensorServiceClient(pipe);
        Assert.That(async () => await client.SnapshotAsync(CancellationToken.None),
            Throws.TypeOf<InvalidOperationException>());
        Assert.That(await pipe.Operations(), Is.Empty);
    }

    [Test]
    public async Task Snapshot_ReturnsValidatedCurrentSensorMetadata()
    {
        var now = Stopwatch.GetTimestamp();
        var expected = new SensorSnapshot(Guid.NewGuid(), 4, now, Stopwatch.Frequency,
            SensorServiceQuality.Complete, new("cpu/0", "CPU", "cpu/0/temp", "CPU Package", 50,
                "LibreHardwareMonitorLib 0.9.6", now));
        using var pipe = await ScriptedPipe.Responses(new(1, "ok"), new(1, "ok", expected), new(1, "ok"));
        await using var client = new SensorServiceClient(pipe);
        await client.SubscribeAsync(CancellationToken.None);
        Assert.That(await client.SnapshotAsync(CancellationToken.None), Is.EqualTo(expected));
    }

    [Test]
    public async Task InvalidSnapshot_ClosesThePoisonedConnection()
    {
        var invalid = new SensorSnapshot(Guid.Empty, 1, 1, 0, SensorServiceQuality.Complete, null);
        using var pipe = await ScriptedPipe.Responses(new(1, "ok"), new(1, "ok", invalid));
        await using var client = new SensorServiceClient(pipe);
        await client.SubscribeAsync(CancellationToken.None);
        Assert.That(async () => await client.SnapshotAsync(CancellationToken.None),
            Throws.TypeOf<InvalidDataException>());
        Assert.That(pipe.Disposed, Is.True);
    }

    [Test]
    public async Task InvalidStatusSnapshot_IsValidatedAndClosesTheConnection()
    {
        var invalid = new SensorSnapshot(Guid.Empty, -1, 1, 0, SensorServiceQuality.Idle, null);
        using var pipe = await ScriptedPipe.Responses(new SensorServiceResponse(1, "ok", invalid));
        await using var client = new SensorServiceClient(pipe);
        Assert.That(async () => await client.StatusAsync(CancellationToken.None),
            Throws.TypeOf<InvalidDataException>());
        Assert.That(pipe.Disposed, Is.True);
    }

    [Test]
    public async Task Dispose_MalformedUnsubscribeResponseStillClosesWithoutThrowing()
    {
        using var pipe = await ScriptedPipe.Responses(new SensorServiceResponse(1, "ok"));
        var invalidLength = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(invalidLength, int.MaxValue);
        pipe.AppendRaw(invalidLength);
        var client = new SensorServiceClient(pipe);
        await client.SubscribeAsync(CancellationToken.None);
        await client.DisposeAsync();
        Assert.That(pipe.Disposed, Is.True);
        Assert.That(await pipe.Operations(), Is.EqualTo(new[] { "subscribe", "unsubscribe" }));
    }

    [Test]
    public async Task CancelledRequest_DoesNotSendOrSubscribe()
    {
        using var pipe = await ScriptedPipe.Responses();
        await using var client = new SensorServiceClient(pipe);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        Assert.That(async () => await client.SubscribeAsync(cancelled.Token), Throws.InstanceOf<OperationCanceledException>());
        Assert.That(await pipe.Operations(), Is.Empty);
    }

    private sealed class ScriptedPipe : Stream
    {
        private readonly MemoryStream _responses = new();
        private readonly MemoryStream _requests = new();
        public bool Disposed { get; private set; }
        public static async Task<ScriptedPipe> Responses(params SensorServiceResponse[] responses)
        {
            var pipe = new ScriptedPipe();
            foreach (var response in responses)
                await SensorServiceProtocol.WriteAsync(pipe._responses, response, CancellationToken.None);
            pipe._responses.Position = 0;
            return pipe;
        }
        public void AppendRaw(byte[] bytes)
        {
            var previous = _responses.Position;
            _responses.Position = _responses.Length;
            _responses.Write(bytes);
            _responses.Position = previous;
        }
        public async Task<string[]> Operations()
        {
            using var requests = new MemoryStream(_requests.ToArray());
            var operations = new List<string>();
            while (requests.Position < requests.Length)
                operations.Add((await SensorServiceProtocol.ReadAsync<SensorServiceRequest>(requests,
                    CancellationToken.None)).Operation);
            return operations.ToArray();
        }
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default)
        {
            ObjectDisposedException.ThrowIf(Disposed, this);
            return _responses.ReadAsync(buffer, token);
        }
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken token = default)
        {
            ObjectDisposedException.ThrowIf(Disposed, this);
            return _requests.WriteAsync(buffer, token);
        }
        public override Task FlushAsync(CancellationToken token) => Task.CompletedTask;
        protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
        public override bool CanRead => !Disposed;
        public override bool CanSeek => false;
        public override bool CanWrite => !Disposed;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() => throw new NotSupportedException();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
