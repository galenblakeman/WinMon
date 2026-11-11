using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using NUnit.Framework;
using WinMon.Updates;

namespace WinMon.Tests;

[TestFixture]
public sealed class UpdateDownloadTests
{
#if !WINMON_STORE
    private string _root = null!;
    private string _application = null!;
    private string _target = null!;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "WinMon.UpdateDownloadTests", Guid.NewGuid().ToString("N"));
        _application = Path.Combine(_root, "application");
        _target = Path.Combine(_application, "WinMon.exe");
        Directory.CreateDirectory(_application);
        File.WriteAllText(_target, "existing executable");
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    [Test]
    public async Task DownloadAsync_StagesSignedInstallerAfterAnAllowedRedirect()
    {
        var bytes = "signed installer"u8.ToArray();
        using var key = RSA.Create(2048);
        var offer = Offer(key, bytes);
        var untrustedCachedManifest = offer.Manifest with
        {
            Installer = new UpdateAsset("../untrusted", new string('0', 64), 1)
        };
        var progress = new List<int>();
        var requests = new List<Uri>();
        using var client = Client(key, request =>
        {
            requests.Add(request.RequestUri!);
            return requests.Count == 1
                ? new HttpResponseMessage(HttpStatusCode.Found)
                    { Headers = { Location = new Uri("https://github.com/galenblakeman/WinMon/download") } }
                : Bytes(bytes);
        });

        var prepared = await client.DownloadAsync(offer with { Manifest = untrustedCachedManifest },
            new CollectingProgress(progress), CancellationToken.None);
        var job = UpdateFiles.ReadJson<UpdateJob>(Path.Combine(prepared.TransactionDirectory, "job.json"));
        using var current = Process.GetCurrentProcess();

        Assert.Multiple(() =>
        {
            Assert.That(requests, Has.Count.EqualTo(2));
            Assert.That(File.ReadAllBytes(Path.Combine(prepared.TransactionDirectory, offer.Manifest.Installer.Name)),
                Is.EqualTo(bytes));
            Assert.That(File.ReadAllBytes(Path.Combine(prepared.TransactionDirectory, "update-manifest.json")),
                Is.EqualTo(offer.ManifestBytes));
            Assert.That(File.ReadAllText(Path.Combine(prepared.TransactionDirectory, "update-manifest.sig")),
                Is.EqualTo(offer.Signature));
            Assert.That(job.Target, Is.EqualTo(_target));
            Assert.That(job.ParentId, Is.EqualTo(current.Id));
            Assert.That(job.ParentStartTicks, Is.EqualTo(current.StartTime.ToUniversalTime().Ticks));
            Assert.That(job.Version, Is.EqualTo(offer.Version));
            Assert.That(job.Installed, Is.True);
            Assert.That(Guid.TryParseExact(job.Token, "N", out _), Is.True);
            Assert.That(progress, Does.Contain(100));
            Assert.That(File.ReadAllText(_target), Is.EqualTo("existing executable"));
        });
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task DownloadAsync_RejectsWrongLengthOrHashAndCleansTheTransaction(bool wrongLength)
    {
        var expected = "expected installer"u8.ToArray();
        var received = wrongLength ? expected : "untrusted installer"u8.ToArray();
        using var key = RSA.Create(2048);
        var offer = Offer(key, expected);
        using var client = Client(key, _ => Bytes(received, wrongLength ? received.Length + 1 : null));

        await Assert.ThatAsync(() => client.DownloadAsync(offer, null, CancellationToken.None),
            Throws.InstanceOf<InvalidDataException>());

        Assert.Multiple(() =>
        {
            Assert.That(Transactions(), Is.Empty);
            Assert.That(File.ReadAllText(_target), Is.EqualTo("existing executable"));
        });
    }

    [Test]
    public async Task DownloadAsync_RejectsAnInvalidSignatureBeforeRequestingTheAsset()
    {
        using var key = RSA.Create(2048);
        var offer = Offer(key, "installer"u8.ToArray());
        var requests = 0;
        using var client = Client(key, _ =>
        {
            requests++;
            return Bytes([]);
        });

        await Assert.ThatAsync(() => client.DownloadAsync(offer with { Signature = "invalid" }, null,
                CancellationToken.None),
            Throws.InstanceOf<FormatException>());
        var signature = Convert.FromBase64String(offer.Signature);
        signature[0] ^= 1;
        var tampered = offer with { Signature = Convert.ToBase64String(signature) };
        await Assert.ThatAsync(() => client.DownloadAsync(tampered, null, CancellationToken.None),
            Throws.TypeOf<InvalidDataException>());
        Assert.That(requests, Is.Zero);
        Assert.That(Directory.Exists(Path.Combine(_root, "updates")), Is.False);
    }

    [Test]
    public async Task DownloadAsync_PropagatesCancellationAndCleansTheTransaction()
    {
        using var key = RSA.Create(2048);
        var offer = Offer(key, "installer"u8.ToArray());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var requests = 0;
        using var client = Client(key, (_, token) =>
        {
            requests++;
            return Task.FromCanceled<HttpResponseMessage>(token);
        });

        await Assert.ThatAsync(() => client.DownloadAsync(offer, null, cancellation.Token),
            Throws.InstanceOf<OperationCanceledException>());
        Assert.Multiple(() =>
        {
            Assert.That(requests, Is.EqualTo(1));
            Assert.That(Transactions(), Is.Empty);
            Assert.That(File.ReadAllText(_target), Is.EqualTo("existing executable"));
        });
    }

    [Test]
    public async Task DownloadAsync_StagesAndExtractsTheVerifiedPortableArchive()
    {
        var executable = await File.ReadAllBytesAsync(typeof(UpdateClient).Assembly.Location);
        Assert.That(FileVersionInfo.GetVersionInfo(typeof(UpdateClient).Assembly.Location).ProductVersion,
            Is.EqualTo(AppInfo.Version));
        var archive = Archive(executable);
        using var key = RSA.Create(2048);
        var offer = Offer(key, "installer"u8.ToArray(), archive, AppInfo.Version);
        var progress = new List<int>();
        using var client = Client(key, _ => Bytes(archive), installed: false);

        var prepared = await client.DownloadAsync(offer, new CollectingProgress(progress), CancellationToken.None);
        var job = UpdateFiles.ReadJson<UpdateJob>(Path.Combine(prepared.TransactionDirectory, "job.json"));

        Assert.Multiple(() =>
        {
            Assert.That(File.ReadAllBytes(Path.Combine(prepared.TransactionDirectory, offer.Manifest.Portable.Name)),
                Is.EqualTo(archive));
            Assert.That(File.ReadAllBytes(Path.Combine(prepared.TransactionDirectory, "WinMon.exe")), Is.EqualTo(executable));
            Assert.That(File.ReadAllBytes(Path.Combine(prepared.TransactionDirectory, "WinMon.Cli.exe")), Is.EqualTo(executable));
            Assert.That(File.ReadAllBytes(Path.Combine(prepared.TransactionDirectory, "WinMon.Mcp.exe")), Is.EqualTo(executable));
            Assert.That(job.Installed, Is.False);
            Assert.That(job.Target, Is.EqualTo(_target));
            Assert.That(progress, Does.Contain(100));
            Assert.That(File.ReadAllText(_target), Is.EqualTo("existing executable"));
        });
    }

    [TestCase("short", typeof(InvalidDataException))]
    [TestCase("overrun", typeof(InvalidDataException))]
    [TestCase("bad-status", typeof(HttpRequestException))]
    [TestCase("disallowed-redirect", typeof(InvalidDataException))]
    [TestCase("redirect-loop", typeof(HttpRequestException))]
    public async Task DownloadAsync_RejectsUntrustedResponsesAndCleansTheTransaction(string kind,
        Type expectedException)
    {
        var expected = "expected installer"u8.ToArray();
        using var key = RSA.Create(2048);
        var offer = Offer(key, expected);
        var redirects = 0;
        using var client = Client(key, _ => kind switch
        {
            "short" => new HttpResponseMessage(HttpStatusCode.OK)
                { Content = new StreamContent(new NonSeekableReadStream(expected[..^1])) },
            "overrun" => Bytes([.. expected, 0], expected.Length),
            "bad-status" => new HttpResponseMessage(HttpStatusCode.BadGateway),
            "disallowed-redirect" => Redirect("https://example.test/untrusted"),
            "redirect-loop" => ++redirects <= 6
                ? Redirect("https://github.com/galenblakeman/WinMon/download")
                : throw new AssertionException("Too many redirects were followed."),
            _ => throw new AssertionException(kind)
        });

        await Assert.ThatAsync(() => client.DownloadAsync(offer, null, CancellationToken.None),
            Throws.TypeOf(expectedException));
        Assert.Multiple(() =>
        {
            Assert.That(redirects, Is.EqualTo(kind == "redirect-loop" ? 6 : 0));
            Assert.That(Transactions(), Is.Empty);
            Assert.That(File.ReadAllText(_target), Is.EqualTo("existing executable"));
        });
    }

    [Test]
    public async Task DownloadAsync_RejectsMissingOrUnpublishedProcessBeforeNetworkAccess()
    {
        using var key = RSA.Create(2048);
        var offer = Offer(key, "installer"u8.ToArray());
        var requests = 0;
        using var missing = Client(key, _ =>
        {
            requests++;
            return Bytes([]);
        }, processPath: () => null);
        using var unpackaged = Client(key, _ =>
        {
            requests++;
            return Bytes([]);
        }, applicationBaseDirectory: () => AppContext.BaseDirectory);

        await Assert.ThatAsync(() => missing.DownloadAsync(offer, null, CancellationToken.None),
            Throws.InstanceOf<InvalidOperationException>());
        await Assert.ThatAsync(() => unpackaged.DownloadAsync(offer, null, CancellationToken.None),
            Throws.InstanceOf<InvalidOperationException>());
        Assert.That(requests, Is.Zero);
    }

    [Test]
    public async Task DownloadAsync_RejectsLowSpaceAndAStalledDownloadWithoutLeavingATransaction()
    {
        using var key = RSA.Create(2048);
        var offer = Offer(key, "installer"u8.ToArray());
        using var lowSpace = Client(key, _ => throw new AssertionException("No download with low space."),
            availableFreeSpace: _ => 0);
        await Assert.ThatAsync(() => lowSpace.DownloadAsync(offer, null, CancellationToken.None),
            Throws.InstanceOf<IOException>());
        Assert.That(Transactions(), Is.Empty);

        using var stalled = Client(key, _ => new HttpResponseMessage(HttpStatusCode.OK)
                { Content = new StreamContent(new BlockingReadStream()) },
            downloadStallTimeout: TimeSpan.FromMilliseconds(20));
        await Assert.ThatAsync(() => stalled.DownloadAsync(offer, null, CancellationToken.None)
                .WaitAsync(TimeSpan.FromSeconds(5)),
            Throws.InstanceOf<OperationCanceledException>());
        Assert.That(Transactions(), Is.Empty);
    }

    [Test]
    [Category("Integration")]
    public void DownloadAsync_CleanupFailurePreservesTheOriginalErrorAndTarget()
    {
        using var key = RSA.Create(2048);
        var offer = Offer(key, "installer"u8.ToArray());
        var failure = new HttpRequestException("original download failure");
        FileStream? retained = null;
        try
        {
            using var client = Client(key, _ =>
            {
                retained = new FileStream(Path.Combine(Transactions().Single(), "retained.bin"),
                    FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
                throw failure;
            });

            var observed = Assert.ThrowsAsync<HttpRequestException>(() =>
                client.DownloadAsync(offer, null, CancellationToken.None));

            Assert.Multiple(() =>
            {
                Assert.That(observed, Is.SameAs(failure), "Cleanup must not replace the download error.");
                Assert.That(retained, Is.Not.Null);
                Assert.That(Transactions(), Has.Length.EqualTo(1));
                Assert.That(File.Exists(Path.Combine(Transactions().Single(), "retained.bin")), Is.True);
                Assert.That(File.Exists(Path.Combine(Transactions().Single(), "job.json")), Is.False);
                Assert.That(File.ReadAllText(_target), Is.EqualTo("existing executable"));
            });
        }
        finally
        {
            retained?.Dispose();
        }
    }

    [Test]
    public void DownloadAsync_RejectsLowStagingSpaceAfterAcceptingDestinationSpaceBeforeHttp()
    {
        using var key = RSA.Create(2048);
        var bytes = "installer"u8.ToArray();
        var offer = Offer(key, bytes);
        var queried = new List<string>();
        var requests = 0;
        using var client = Client(key, _ =>
        {
            requests++;
            return Bytes(bytes);
        }, availableFreeSpace: path =>
        {
            queried.Add(path);
            return queried.Count == 1 ? long.MaxValue : 0;
        });

        var failure = Assert.ThrowsAsync<IOException>(() => client.DownloadAsync(offer, null, CancellationToken.None));

        Assert.Multiple(() =>
        {
            Assert.That(failure!.Message, Is.EqualTo("Not enough staging space to download the update."));
            Assert.That(queried, Has.Count.EqualTo(2));
            Assert.That(queried[0], Is.EqualTo(_application));
            Assert.That(Path.GetDirectoryName(queried[1]), Is.EqualTo(Path.Combine(_root, "updates")));
            Assert.That(requests, Is.Zero);
            Assert.That(Transactions(), Is.Empty);
            Assert.That(File.ReadAllText(_target), Is.EqualTo("existing executable"));
        });
    }

    private UpdateClient Client(RSA key, Func<HttpRequestMessage, HttpResponseMessage> response, bool installed = true,
        Func<string?>? processPath = null, Func<string>? applicationBaseDirectory = null,
        Func<string, long>? availableFreeSpace = null, TimeSpan? downloadStallTimeout = null)
    {
        return Client(key, (request, _) => Task.FromResult(response(request)), installed, processPath,
            applicationBaseDirectory, availableFreeSpace, downloadStallTimeout);
    }

    private string[] Transactions()
    {
        var updates = Path.Combine(_root, "updates");
        return Directory.Exists(updates) ? Directory.GetDirectories(updates) : [];
    }

    private UpdateClient Client(RSA key,
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> response, bool installed = true,
        Func<string?>? processPath = null, Func<string>? applicationBaseDirectory = null,
        Func<string, long>? availableFreeSpace = null, TimeSpan? downloadStallTimeout = null)
    {
        return new UpdateClient(new HttpClient(new DelegateHandler(response)), key.ExportSubjectPublicKeyInfoPem(),
            Path.Combine(_root, "updates"), processPath ?? (() => _target), applicationBaseDirectory ?? (() => _application),
            _ => installed, availableFreeSpace, downloadStallTimeout);
    }

    private static UpdateOffer Offer(RSA key, byte[] installer, byte[]? portable = null, string? version = null)
    {
        version ??= "9.9.9";
        var manifest = new UpdateManifest(1, version, "win-x64",
            Asset($"WinMon-{version}-win-x64.zip", portable ?? "portable"u8.ToArray()),
            Asset($"WinMon-{version}-Setup-x64.exe", installer));
        var bytes = JsonSerializer.SerializeToUtf8Bytes(manifest, UpdateManifest.JsonOptions);
        var signature = Convert.ToBase64String(key.SignData(bytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
        return new UpdateOffer(version, new Uri($"https://github.com/galenblakeman/WinMon/releases/tag/v{version}"),
            manifest, bytes, signature);
    }

    private static UpdateAsset Asset(string name, byte[] bytes)
    {
        return new UpdateAsset(name, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), bytes.Length);
    }

    private static HttpResponseMessage Bytes(byte[] bytes, long? length = null)
    {
        var content = new ByteArrayContent(bytes);
        if (length is not null) content.Headers.ContentLength = length;
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
    }

    private static HttpResponseMessage Redirect(string location)
    {
        return new HttpResponseMessage(HttpStatusCode.Found) { Headers = { Location = new Uri(location) } };
    }

    private static byte[] Archive(byte[] executable)
    {
        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, true))
        {
            using (var entry = archive.CreateEntry("WinMon.exe").Open()) entry.Write(executable);
            using (var entry = archive.CreateEntry("WinMon.Cli.exe").Open()) entry.Write(executable);
            using (var entry = archive.CreateEntry("WinMon.Mcp.exe").Open()) entry.Write(executable);
        }
        return output.ToArray();
    }

    private sealed class CollectingProgress(List<int> values) : IProgress<int>
    {
        public void Report(int value) => values.Add(value);
    }

    private sealed class NonSeekableReadStream(byte[] bytes) : MemoryStream(bytes, false)
    {
        public override bool CanSeek => false;
    }

    private sealed class BlockingReadStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken token) =>
            Task.Delay(Timeout.InfiniteTimeSpan, token).ContinueWith<int>(_ => 0, token);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default) =>
            new(Task.Delay(Timeout.InfiniteTimeSpan, token).ContinueWith<int>(_ => 0, token));
    }

    private sealed class DelegateHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return response(request, cancellationToken);
        }
    }
#endif
}
