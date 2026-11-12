using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NUnit.Framework;
using WinMon.Updates;

namespace WinMon.Cli.Tests;

[TestFixture, Category("Integration")]
public sealed class ToolUpdateTests
{
    private string _root = null!;

    [SetUp]
    public void SetUp() => _root = Path.Combine(Path.GetTempPath(), "WinMon.ToolUpdateTests", Guid.NewGuid().ToString("N"));

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    [Test]
    public async Task Check_NewSignedCliOffer_ReportsItWithoutWritingFiles()
    {
        RequireGitHubUpdates();
        using var key = RSA.Create(2048);
        var responses = Responses(key, ToolUpdateProduct.Cli, "9.0.0", "installer");
        var output = new StringWriter();

        var result = await Run(ToolUpdateAction.Check, responses, key, output);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.Zero);
            Assert.That(output.ToString(), Does.Contain("9.0.0"));
            Assert.That(Directory.Exists(_root), Is.False, "A check must not create the staging directory.");
        });
    }

    [Test]
    public async Task Check_StaleRelease_ReportsCurrentVersion()
    {
        RequireGitHubUpdates();
        using var key = RSA.Create(2048);
        var output = new StringWriter();
        var result = await Run(ToolUpdateAction.Check, Responses(key, ToolUpdateProduct.Cli, "1.0.0", "installer"), key, output);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.Zero);
            Assert.That(output.ToString(), Does.Contain("up to date"));
            Assert.That(Directory.Exists(_root), Is.False);
        });
    }

    [Test]
    public async Task Check_RejectsTamperedSignatureAndProductMismatch()
    {
        RequireGitHubUpdates();
        using var key = RSA.Create(2048);
        var tampered = Responses(key, ToolUpdateProduct.Cli, "9.0.0", "installer") with { Signature = "invalid" };
        var error = new StringWriter();
        Assert.That(await Run(ToolUpdateAction.Check, tampered, key, new StringWriter(), error), Is.EqualTo(1));

        var mismatch = Responses(key, ToolUpdateProduct.Mcp, "9.0.0", "installer") with { Product = ToolUpdateProduct.Cli };
        error = new StringWriter();
        Assert.That(await Run(ToolUpdateAction.Check, mismatch, key, new StringWriter(), error), Is.EqualTo(1));
        Assert.That(Directory.Exists(_root), Is.False);
    }

    [Test]
    public async Task Check_RejectsRedirectOutsideGitHubOrigins()
    {
        RequireGitHubUpdates();
        using var key = RSA.Create(2048);
        var responses = Responses(key, ToolUpdateProduct.Cli, "9.0.0", "installer");
        responses.RedirectManifestTo = new Uri("https://example.test/manifest");

        Assert.That(await Run(ToolUpdateAction.Check, responses, key, new StringWriter(), new StringWriter()), Is.EqualTo(1));
    }

    [Test]
    public async Task Check_Cancellation_ReturnsFailureWithoutMutation()
    {
        RequireGitHubUpdates();
        using var key = RSA.Create(2048);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        var result = await Run(ToolUpdateAction.Check, Responses(key, ToolUpdateProduct.Cli, "9.0.0", "installer"), key,
            new StringWriter(), new StringWriter(), cancelled.Token);

        Assert.That(result, Is.EqualTo(130));
        Assert.That(Directory.Exists(_root), Is.False);
    }

    [Test]
    public async Task Install_InstalledCopy_VerifiesAndLaunchesMatchingInstaller()
    {
        RequireGitHubUpdates();
        using var key = RSA.Create(2048);
        var responses = Responses(key, ToolUpdateProduct.Cli, "9.0.0", "installer bytes");
        string? launched = null;
        var dependencies = new ToolUpdateDependencies
        {
            Handler = new Handler(responses.Handle), PublicKey = key.ExportSubjectPublicKeyInfoPem(), StagingRoot = _root,
            IsInstalled = (_, _) => true, StartInstaller = info => { launched = info.FileName; return true; }
        };

        var result = await ToolUpdateCommand.RunAsync(ToolUpdateProduct.Cli, ToolUpdateAction.Install, new StringWriter(),
            new StringWriter(), CancellationToken.None, dependencies);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.Zero);
            Assert.That(launched, Does.EndWith("WinMon-Cli-9.0.0-Setup-x64.exe"));
            Assert.That(launched, Is.Not.Null);
            Assert.That(File.Exists(launched!), Is.True);
        });
    }

    [Test]
    public async Task Install_PortableCopy_VerifiesMatchingArchiveWithoutLaunchingInstaller()
    {
        RequireGitHubUpdates();
        using var key = RSA.Create(2048);
        var responses = Responses(key, ToolUpdateProduct.Cli, "9.0.0", "installer");
        var output = new StringWriter();
        var result = await Run(ToolUpdateAction.Install, responses, key, output);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.Zero);
            Assert.That(output.ToString(), Does.Contain("WinMon-Cli-9.0.0-win-x64.zip"));
            Assert.That(Directory.GetFiles(_root, "*.zip", SearchOption.AllDirectories), Has.Length.EqualTo(1));
        });
    }

    [Test]
    public async Task Install_CorruptPayload_DoesNotLaunchAndCleansStaging()
    {
        RequireGitHubUpdates();
        using var key = RSA.Create(2048);
        var responses = Responses(key, ToolUpdateProduct.Cli, "9.0.0", "expected installer") with
        {
            Installer = Encoding.UTF8.GetBytes("tampered installer")
        };
        var launched = false;
        var dependencies = new ToolUpdateDependencies
        {
            Handler = new Handler(responses.Handle), PublicKey = key.ExportSubjectPublicKeyInfoPem(), StagingRoot = _root,
            IsInstalled = (_, _) => true, StartInstaller = _ => { launched = true; return true; }
        };

        var result = await ToolUpdateCommand.RunAsync(ToolUpdateProduct.Cli, ToolUpdateAction.Install, new StringWriter(),
            new StringWriter(), CancellationToken.None, dependencies);

        Assert.That(result, Is.EqualTo(1));
        Assert.That(launched, Is.False);
        Assert.That(Directory.GetFiles(_root, "*", SearchOption.AllDirectories), Is.Empty);
    }

    [Test]
    public async Task Install_StalledPayload_TimesOutWithoutLaunching()
    {
        RequireGitHubUpdates();
        using var key = RSA.Create(2048);
        var responses = Responses(key, ToolUpdateProduct.Cli, "9.0.0", "installer") with { BlockInstaller = true };
        var launched = false;
        var dependencies = new ToolUpdateDependencies
        {
            Handler = new Handler(responses.Handle), PublicKey = key.ExportSubjectPublicKeyInfoPem(), StagingRoot = _root,
            IsInstalled = (_, _) => true, StartInstaller = _ => { launched = true; return true; },
            DownloadStallTimeout = TimeSpan.FromMilliseconds(10)
        };

        var result = await ToolUpdateCommand.RunAsync(ToolUpdateProduct.Cli, ToolUpdateAction.Install, new StringWriter(),
            new StringWriter(), CancellationToken.None, dependencies);

        Assert.That(result, Is.EqualTo(1));
        Assert.That(launched, Is.False);
        Assert.That(Directory.GetFiles(_root, "*", SearchOption.AllDirectories), Is.Empty);
    }

    [Test]
    public async Task Install_OversizedChunkedPayload_DoesNotLaunchOrRetainIt()
    {
        RequireGitHubUpdates();
        using var key = RSA.Create(2048);
        var responses = Responses(key, ToolUpdateProduct.Cli, "9.0.0", "installer") with { OversizedInstaller = true };
        var launched = false;
        var dependencies = new ToolUpdateDependencies
        {
            Handler = new Handler(responses.Handle), PublicKey = key.ExportSubjectPublicKeyInfoPem(), StagingRoot = _root,
            IsInstalled = (_, _) => true, StartInstaller = _ => { launched = true; return true; }
        };

        var result = await ToolUpdateCommand.RunAsync(ToolUpdateProduct.Cli, ToolUpdateAction.Install, new StringWriter(),
            new StringWriter(), CancellationToken.None, dependencies);

        Assert.That(result, Is.EqualTo(1));
        Assert.That(launched, Is.False);
        Assert.That(Directory.GetFiles(_root, "*", SearchOption.AllDirectories), Is.Empty);
    }

    [Test]
    public async Task StoreBuild_RejectsBeforeContactingGitHub()
    {
        if (UpdatePolicy.GitHubUpdatesEnabled) Assert.Ignore("This assertion only applies to the Store build.");
        var contacted = false;
        var dependencies = new ToolUpdateDependencies
        {
            Handler = new Handler(_ => { contacted = true; throw new AssertionException("Network must not be used."); })
        };

        var result = await ToolUpdateCommand.RunAsync(ToolUpdateProduct.Cli, ToolUpdateAction.Check, new StringWriter(),
            new StringWriter(), CancellationToken.None, dependencies);

        Assert.That(result, Is.EqualTo(1));
        Assert.That(contacted, Is.False);
    }

    private static void RequireGitHubUpdates()
    {
        if (!UpdatePolicy.GitHubUpdatesEnabled) Assert.Ignore("GitHub updates are disabled for Microsoft Store builds.");
    }

    private Task<int> Run(ToolUpdateAction action, ResponseSet responses, RSA key, StringWriter output, StringWriter? error = null,
        CancellationToken cancellationToken = default) => ToolUpdateCommand.RunAsync(ToolUpdateProduct.Cli, action, output,
        error ?? new StringWriter(), cancellationToken, new ToolUpdateDependencies
        {
            Handler = new Handler(responses.Handle), PublicKey = key.ExportSubjectPublicKeyInfoPem(), StagingRoot = _root,
            IsInstalled = (_, _) => false
        });

    private static ResponseSet Responses(RSA key, ToolUpdateProduct product, string version, string installerContents)
    {
        var installer = Encoding.UTF8.GetBytes(installerContents);
        var portable = Encoding.UTF8.GetBytes("portable bytes");
        var document = new { schemaVersion = 1, version, runtime = "win-x64", portable = Asset(product.AssetStem, version, "win-x64.zip", portable), installer = Asset(product.AssetStem, version, "Setup-x64.exe", installer) };
        var manifest = JsonSerializer.SerializeToUtf8Bytes(document);
        return new ResponseSet(product, version, manifest, Convert.ToBase64String(key.SignData(manifest, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)), installer, portable);
    }

    private static object Asset(string stem, string version, string suffix, byte[] bytes) => new { name = $"{stem}-{version}-{suffix}", sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), size = bytes.Length };

    private sealed record ResponseSet(ToolUpdateProduct Product, string Version, byte[] Manifest, string Signature, byte[] Installer, byte[] Portable)
    {
        internal Uri? RedirectManifestTo { get; set; }
        internal bool BlockInstaller { get; set; }
        internal bool OversizedInstaller { get; set; }
        internal HttpResponseMessage Handle(HttpRequestMessage request)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/latest")) return Json(new { draft = false, prerelease = false, tag_name = "v" + Version, assets = new[] { Product.ManifestName, Product.SignatureName, $"{Product.AssetStem}-{Version}-win-x64.zip", $"{Product.AssetStem}-{Version}-Setup-x64.exe" }.Select(name => new { name }) });
            if (path.EndsWith(Product.ManifestName)) return RedirectManifestTo is null ? Bytes(Manifest) : new HttpResponseMessage(HttpStatusCode.Found) { Headers = { Location = RedirectManifestTo } };
            if (path.EndsWith(Product.SignatureName)) return Bytes(Encoding.UTF8.GetBytes(Signature));
            if (path.EndsWith("Setup-x64.exe"))
                return BlockInstaller ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new BlockingStream()) }
                    : OversizedInstaller
                        ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new NonSeekableMemoryStream(Installer.Append((byte)1).ToArray())) }
                        : Bytes(Installer);
            if (path.EndsWith("win-x64.zip")) return Bytes(Portable);
            throw new AssertionException(path);
        }
        private static HttpResponseMessage Json(object value) => Bytes(JsonSerializer.SerializeToUtf8Bytes(value));
        private static HttpResponseMessage Bytes(byte[] bytes) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
    }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> handle) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            cancellationToken.IsCancellationRequested
                ? Task.FromCanceled<HttpResponseMessage>(cancellationToken)
                : Task.FromResult(handle(request));
    }

    private sealed class BlockingStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => 0; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        }
    }

    private sealed class NonSeekableMemoryStream(byte[] buffer) : MemoryStream(buffer, writable: false)
    {
        public override bool CanSeek => false;
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    }
}
