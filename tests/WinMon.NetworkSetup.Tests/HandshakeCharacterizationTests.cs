using System.IO.Pipes;
using System.Reflection;
using NUnit.Framework;
using WinMon.Monitoring;
using Win32Exception = System.ComponentModel.Win32Exception;

namespace WinMon.NetworkSetup.Tests;

[TestFixture]
[Category("Integration")]
public sealed class HandshakeCharacterizationTests
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(10);
    private const string Token = "abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789";

    [TestCase("version")]
    [TestCase("mismatch")]
    [TestCase("short")]
    [TestCase("nonhex")]
    [TestCase("operation")]
    [TestCase("uppercase-operation")]
    [TestCase("null-operation")]
    public async Task InvalidRequestsCloseWithoutAReplyOrPreparingTheHost(string invalid)
    {
        await using var run = new Handshake();
        var request = new NetworkSetupRequest(NetworkSetupWire.Version, Token, "install");
        request = invalid switch
        {
            "version" => request with { Version = NetworkSetupWire.Version + 1 },
            "mismatch" => request with { Token = new string('0', 64) },
            "short" => request with { Token = Token[..63] },
            "nonhex" => request with { Token = new string('z', 64) },
            "operation" => request with { Operation = "revoke" },
            "uppercase-operation" => request with { Operation = "INSTALL" },
            "null-operation" => request with { Operation = null! },
            _ => throw new AssertionException("Unknown invalid request.")
        };
        await run.Send(request);
        var error = await run.Failure();
        Assert.Multiple(() =>
        {
            Assert.That(error, Is.TypeOf<UnauthorizedAccessException>());
            Assert.That(error.Message, Is.EqualTo("Invalid setup request."));
            Assert.That(run.PrepareCalls, Is.Zero);
        });
        await run.AssertEofAndDisposed();
    }

    [Test]
    public async Task NullTokenPreservesTheExistingNullReferenceFailureBeforeHostPreparation()
    {
        await using var run = new Handshake();
        await run.Send(new(NetworkSetupWire.Version, null!, "install"));
        Assert.That(await run.Failure(), Is.TypeOf<NullReferenceException>());
        Assert.That(run.PrepareCalls, Is.Zero);
        await run.AssertEofAndDisposed();
    }

    [TestCase("install", "win32")]
    [TestCase("remove", "win32")]
    [TestCase("install", "staged")]
    [TestCase("remove", "aggregate")]
    [TestCase("install", "managed")]
    public async Task PreparationFailuresSendStructuredDetailsAndRethrowTheOriginal(string operation, string kind)
    {
        Exception original = kind switch
        {
            "win32" => new Win32Exception(5, "Owned failure"),
            "staged" => new SetupFailure("start", 123),
            "aggregate" => new AggregateException(new InvalidOperationException("first"),
                new AggregateException(new SetupFailure("registration", 456))),
            "managed" => new IOException("Owned failure"),
            _ => throw new AssertionException("Unknown failure.")
        };
        await using var run = new Handshake(() => throw original);
        // Hex casing is accepted by the existing fixed-time token comparison.
        await run.Send(new(NetworkSetupWire.Version, Token.ToUpperInvariant(), operation));
        var reply = await run.ReadReply();
        Assert.Multiple(() =>
        {
            Assert.That(reply.Version, Is.EqualTo(NetworkSetupWire.Version));
            Assert.That(reply.Phase, Is.EqualTo("failed"));
            Assert.That(reply.FailurePhase, Is.EqualTo(kind switch
            {
                "staged" => "start", "aggregate" => "registration", _ => "unknown"
            }));
            Assert.That(reply.ErrorCode, Is.EqualTo(kind switch
            {
                "win32" => 5, "staged" => 123, "aggregate" => 456, _ => original.HResult
            }));
        });
        Assert.That(await run.Failure(), Is.SameAs(original));
        Assert.That(run.PrepareCalls, Is.EqualTo(1));
        await run.AssertEofAndDisposed();
    }

    [TestCase("install")]
    [TestCase("remove")]
    public async Task PreparationCancellationKeepsItsIdentityAndDoesNotSendAFailedReply(string operation)
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var original = new OperationCanceledException("Owned cancellation", cancellation.Token);
        await using var run = new Handshake(() => throw original);
        await run.Send(new(NetworkSetupWire.Version, Token, operation));
        var actual = await run.Failure();
        Assert.That(actual, Is.SameAs(original));
        Assert.That(((OperationCanceledException)actual).CancellationToken, Is.EqualTo(cancellation.Token));
        Assert.That(run.PrepareCalls, Is.EqualTo(1));
        await run.AssertEofAndDisposed();
    }

    [Test]
    public async Task DisconnectedFailureReplyDoesNotReplaceTheOriginalSetupException()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        var original = new IOException("Owned preparation failure");
        await using var run = new Handshake(() =>
        {
            entered.SetResult();
            if (!release.Wait(Limit)) throw new AssertionException("Preparation gate timed out.");
            throw original;
        });
        try
        {
            await run.Send(new(NetworkSetupWire.Version, Token, "install"));
            await entered.Task.WaitAsync(Limit);
            await run.Server.DisposeAsync();
        }
        finally
        {
            release.Set();
        }
        Assert.That(await run.Failure(), Is.SameAs(original));
        Assert.That(run.PrepareCalls, Is.EqualTo(1));
        run.AssertPackageDisposed();
    }

    [Test]
    public async Task PackageOpenFailureIsRethrownBeforeConnecting()
    {
        var original = new InvalidDataException("Owned invalid package");
        var calls = 0;
        var invocation = SetupArguments.Parse(["--pipe", "WinMon.NetworkSetup." + Guid.NewGuid().ToString("N"),
            "--token", Token]);
        Exception? actual = null;
        try
        {
            await Program.RunHandshakeAsync(invocation, () => { calls++; throw original; }).WaitAsync(Limit);
        }
        catch (Exception exception) { actual = exception; }
        Assert.That(actual, Is.SameAs(original));
        Assert.That(calls, Is.EqualTo(1));
    }

    private sealed class Handshake : IAsyncDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "WinMon-handshake-" + Guid.NewGuid().ToString("N"));
        private readonly CancellationTokenSource _limit = new(Limit);
        private readonly NetworkSetupPackage _package;
        private readonly Task _connected;
        private readonly Task _run;
        internal NamedPipeServerStream Server { get; }
        internal int PrepareCalls { get; private set; }

        internal Handshake(Action? prepare = null)
        {
            Directory.CreateDirectory(_root);
            var helper = Path.Combine(_root, "helper");
            var archive = Path.Combine(_root, "archive");
            File.WriteAllText(helper, "owned helper");
            File.WriteAllText(archive, "owned archive");
            var manifest = new NetworkSetupManifest(1, typeof(SetupService).Assembly.GetName().Version!.ToString(3),
                "win-x64", NetworkSetupWire.Version, new(SetupPaths.SetupExecutableName, "", 1),
                new("network-service.zip", "", 1));
            // As in SetupHostTests, the injected package is only a disposable owned fixture.
            // No publisher verification bypass is added to the production entry point.
            _package = (NetworkSetupPackage)typeof(NetworkSetupPackage)
                .GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic).Single()
                .Invoke([manifest, File.OpenRead(helper), File.OpenRead(archive)]);
            var name = "WinMon.NetworkSetup." + Guid.NewGuid().ToString("N");
            Server = new NamedPipeServerStream(name, PipeDirection.InOut, 1,
                PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            _connected = Server.WaitForConnectionAsync(_limit.Token);
            var host = new SetupHost
            {
                InstallDirectory = Path.Combine(_root, "never-created-install"),
                PrepareParent = () =>
                {
                    PrepareCalls++;
                    if (prepare is not null) prepare();
                    throw Unexpected();
                },
                EnsureOwnedOrAbsent = () => throw Unexpected(),
                CaptureState = () => throw Unexpected(),
                StopOwnedIfRunning = _ => throw Unexpected(),
                CreateOrUpdateOwned = _ => throw Unexpected(),
                StartAndVerify = _ => throw Unexpected(),
                RestoreService = (_, _) => throw Unexpected(),
                StopAndDeleteOwned = _ => throw Unexpected(),
                CaptureRegistration = () => throw Unexpected(),
                WriteRegistration = () => throw Unexpected(),
                RestoreRegistration = _ => throw Unexpected(),
                VerifyPublisher = _ => throw Unexpected(),
                ProvisionLogs = _ => throw Unexpected()
            };
            var invocation = SetupArguments.Parse(["--pipe", name, "--token", Token]);
            _run = Program.RunHandshakeAsync(invocation, () => _package, host);
        }

        private static AssertionException Unexpected() => new("Unexpected setup host boundary.");

        internal async Task Send(NetworkSetupRequest request)
        {
            // Surface an early client failure instead of masking it with a server timeout.
            if (await Task.WhenAny(_connected, _run).WaitAsync(Limit) == _run) await _run;
            await _connected.WaitAsync(Limit);
            await NetworkSetupWire.WriteAsync(Server, request,
                NetworkSetupWireJsonContext.Default.NetworkSetupRequest, _limit.Token);
        }

        internal async Task<NetworkSetupReply> ReadReply() => await NetworkSetupWire.ReadAsync(Server,
            NetworkSetupWireJsonContext.Default.NetworkSetupReply, _limit.Token);

        internal async Task<Exception> Failure()
        {
            try { await _run.WaitAsync(Limit); }
            catch (Exception exception) { return exception; }
            throw new AssertionException("Handshake unexpectedly succeeded.");
        }

        internal async Task AssertEofAndDisposed()
        {
            Assert.That(await Server.ReadAsync(new byte[1], _limit.Token), Is.Zero);
            AssertPackageDisposed();
        }

        internal void AssertPackageDisposed()
        {
            Assert.Multiple(() =>
            {
                Assert.That(_package.HelperStream.CanRead, Is.False);
                Assert.That(_package.ArchiveStream.CanRead, Is.False);
                Assert.That(Directory.Exists(Path.Combine(_root, "never-created-install")), Is.False);
            });
        }

        public async ValueTask DisposeAsync()
        {
            await Server.DisposeAsync();
            try { await _connected; }
            catch (OperationCanceledException) { }
            catch (ObjectDisposedException) { }
            // Observe the existing task even if an assertion failed; never abandon a connected helper.
            try { await _run; }
            catch (Exception) { }
            _package.Dispose();
            _limit.Dispose();
            Directory.Delete(_root, true);
        }
    }
}
