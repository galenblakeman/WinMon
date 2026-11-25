using WinMon.Updates;
using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;
using NUnit.Framework;

namespace WinMon.Tests;

[Category("Integration")]
public abstract class ToolInstallationDetectionTestsBase
{
    private protected abstract ToolUpdateProduct Template { get; }

    [TestCase("missing-key", false)]
    [TestCase("missing-value", false)]
    [TestCase("number", false)]
    [TestCase("empty-location", false)]
    [TestCase("space-location", false)]
    [TestCase("exact", true)]
    [TestCase("case", true)]
    [TestCase("normalized", true)]
    [TestCase("relative", true)]
    [TestCase("expanded", true)]
    [TestCase("different-directory", false)]
    [TestCase("different-executable", false)]
    [TestCase("null-executable", false)]
    [TestCase("empty-executable", false)]
    [TestCase("space-executable", false)]
    [TestCase("invalid-executable", false)]
    public async Task InstallationSelectionRespectsRegistryAndBuildPolicy(string kind, bool installed)
    {
        if (await AssertStoreBoundaryIfDisabled()) return;
        using var probe = new Probe(Template);
        var directory = Path.Combine(probe.Root, "install");
        object? location = kind == "missing-value" ? null : kind == "number" ? 42 : kind == "empty-location" ? "" : kind == "space-location" ? " \t " : directory;
        probe.Executable = Path.Combine(directory, Template.ExecutableName);
        if (kind == "case") probe.Executable = probe.Executable.ToUpperInvariant();
        if (kind == "normalized") location = Path.Combine(probe.Root, "unused", "..", "install");
        if (kind == "relative") { location = "."; probe.Executable = Path.GetFullPath(Template.ExecutableName); }
        if (kind == "expanded") { location = @"%TEMP%\WinMon.InstallationDetection"; probe.Executable = Path.Combine(Environment.ExpandEnvironmentVariables((string)location), Template.ExecutableName); }
        if (kind == "different-directory") probe.Executable = Path.Combine(probe.Root, "elsewhere", Template.ExecutableName);
        if (kind == "different-executable") probe.Executable = Path.Combine(directory, "other.exe");
        if (kind == "null-executable") probe.Executable = null;
        if (kind == "empty-executable") probe.Executable = "";
        if (kind == "space-executable") probe.Executable = " \t ";
        if (kind == "invalid-executable") probe.Executable += "\0";
        if (kind != "missing-key") probe.Configure(location, kind == "number" ? RegistryValueKind.DWord : kind == "expanded" ? RegistryValueKind.ExpandString : RegistryValueKind.String);
        await probe.AssertSelection(installed);
        Assert.That(probe.ProcessPathCalls, Is.EqualTo(1));
        Assert.That(probe.OverrideCalls, Is.Zero);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task ExplicitDetectionOverridePrecedesMissingExecutableOrStorePolicy(bool installed)
    {
        if (await AssertStoreBoundaryIfDisabled()) return;
        using var probe = new Probe(Template) { Executable = null };
        await probe.AssertSelection(installed, installed);
        Assert.That(probe.ProcessPathCalls, Is.EqualTo(1));
        Assert.That(probe.OverrideCalls, Is.EqualTo(1));
    }

    [Test]
    public async Task CheckOnlyOrStoreBuildNeverInvokesInstallationDetection()
    {
        if (await AssertStoreBoundaryIfDisabled()) return;
        using var probe = new Probe(Template);
        var result = await probe.Run(ToolUpdateAction.Check);
        Assert.That(result, Is.Zero);
        Assert.That(probe.Requests.Count, Is.EqualTo(3));
        Assert.That(probe.AssetRequests, Is.Empty);
        Assert.That(probe.ProcessPathCalls, Is.Zero);
        Assert.That(probe.Launches, Is.Zero);
        Assert.That(Directory.Exists(probe.Root), Is.False);
    }

    [TestCase("draft", 0, 1)]
    [TestCase("prerelease", 0, 1)]
    [TestCase("stale", 0, 1)]
    [TestCase("not-found", 0, 1)]
    [TestCase("missing-metadata", 1, 1)]
    [TestCase("missing-signature", 1, 1)]
    [TestCase("invalid-version", 1, 1)]
    [TestCase("missing-portable", 1, 3)]
    [TestCase("missing-installer", 1, 3)]
    [TestCase("redirect-relative", 0, 4)]
    [TestCase("redirect-cdn", 0, 4)]
    [TestCase("redirect-http", 1, 2)]
    [TestCase("redirect-foreign", 1, 2)]
    [TestCase("redirect-apex", 1, 2)]
    [TestCase("redirect-loop", 1, 7)]
    [TestCase("redirect-no-location", 1, 2)]
    public async Task OfferAndRedirectPoliciesPrecedeDetectionOrRespectStorePolicy(string mode, int expected, int requests)
    {
        if (await AssertStoreBoundaryIfDisabled()) return;
        using var probe = new Probe(Template) { Mode = mode };
        Assert.That(await probe.Run(ToolUpdateAction.Check), Is.EqualTo(expected));
        Assert.That(probe.Requests.Count, Is.EqualTo(requests));
        Assert.That(probe.AssetRequests, Is.Empty);
        Assert.That(probe.ProcessPathCalls, Is.Zero);
        Assert.That(probe.OverrideCalls, Is.Zero);
        Assert.That(probe.Launches, Is.Zero);
        Assert.That(probe.Requests.Select(value => new Uri(value).Scheme), Is.All.EqualTo("https"));
        Assert.That(probe.Requests.Select(value => new Uri(value).Host), Is.All.Matches<string>(host => host is "api.github.com" or "github.com" or "objects.githubusercontent.com"));
        Assert.That(Directory.Exists(probe.Root), Is.False);
    }

    [TestCase("portable", false, 0, 0, true)]
    [TestCase("started", true, 0, 1, true)]
    [TestCase("not-started", true, 1, 1, false)]
    [TestCase("launch-throws", true, 1, 1, false)]
    public async Task VerifiedInertPayloadsPreserveRetentionAndFakeLauncherCleanup(string mode, bool installed, int expected, int launches, bool retained)
    {
        if (await AssertStoreBoundaryIfDisabled()) return;
        using var probe = new Probe(Template) { Mode = "payload-" + mode };
        var directory = Path.Combine(probe.Root, "registered");
        probe.Executable = Path.Combine(directory, Template.ExecutableName);
        if (installed) probe.Configure(directory, RegistryValueKind.String);
        Assert.That(await probe.Run(ToolUpdateAction.Install), Is.EqualTo(expected));
        Assert.That(probe.Launches, Is.EqualTo(launches));
        Assert.That(probe.AssetRequests, Is.EqualTo(new[] { probe.AssetName(installed) }));
        var files = Directory.GetFiles(probe.Root, "*", SearchOption.AllDirectories);
        Assert.That(files.Length, Is.EqualTo(retained ? 1 : 0));
        if (retained) Assert.That(await File.ReadAllBytesAsync(files[0]), Is.EqualTo(Probe.Payload));
        else Assert.That(Directory.GetDirectories(probe.Root), Is.Empty);
        if (installed)
        {
            Assert.That(probe.LaunchPath, Does.StartWith(probe.Root + Path.DirectorySeparatorChar));
            Assert.That(Path.GetFileName(probe.LaunchPath), Is.EqualTo(probe.AssetName(true)));
            Assert.That(probe.UseShellExecute, Is.True);
        }
    }

    [Test]
    public async Task CallerCancellationReturns130WithoutDetectionOrStagingOrRespectsStorePolicy()
    {
        if (await AssertStoreBoundaryIfDisabled()) return;
        using var probe = new Probe(Template); using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        Assert.That(await probe.Run(ToolUpdateAction.Install, cancellationToken: cancellation.Token), Is.EqualTo(130));
        Assert.That(probe.Requests.Count, Is.EqualTo(1));
        Assert.That(probe.RequestCancellations, Is.EqualTo(new[] { true }));
        Assert.That(probe.ProcessPathCalls, Is.Zero);
        Assert.That(probe.Launches, Is.Zero);
        Assert.That(Directory.Exists(probe.Root), Is.False);
    }

    [TestCase("header-length", "Unexpected download size.")]
    [TestCase("chunk-oversize", "Download exceeds the expected size.")]
    [TestCase("checksum", "The update checksum is invalid.")]
    public async Task RejectedPayloadsNeverInvokeTheLauncherAndCleanOwnedStaging(string kind, string expectedError)
    {
        if (await AssertStoreBoundaryIfDisabled()) return;
        using var probe = new Probe(Template) { Mode = "payload-" + kind };
        Assert.That(await probe.Run(ToolUpdateAction.Install), Is.EqualTo(1));
        Assert.That(probe.AssetRequests, Is.EqualTo(new[] { probe.AssetName(false) }));
        Assert.That(probe.Error.ToString(), Does.Contain(expectedError));
        Assert.That(probe.Launches, Is.Zero);
        Assert.That(Directory.GetFiles(probe.Root, "*", SearchOption.AllDirectories), Is.Empty);
        Assert.That(Directory.GetDirectories(probe.Root), Is.Empty);
    }

    private async Task<bool> AssertStoreBoundaryIfDisabled()
    {
        if (UpdatePolicy.GitHubUpdatesEnabled) return false;
        using var probe = new Probe(Template);
        var result = await probe.Run(ToolUpdateAction.Install, true);
        Assert.That(result, Is.EqualTo(1));
        Assert.That(probe.Requests, Is.Empty);
        Assert.That(probe.ProcessPathCalls, Is.Zero);
        Assert.That(probe.OverrideCalls, Is.Zero);
        Assert.That(probe.Launches, Is.Zero);
        Assert.That(probe.Error.ToString(), Is.Not.Empty);
        Assert.That(Directory.Exists(probe.Root), Is.False);
        return true;
    }

    private sealed class Probe : IDisposable
    {
        private const string RegistryPrefix = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\";
        private const string IdentityPrefix = "WinMon.CrapTests.Installation.";
        private const string Version = "99999.99999.99999";
        internal static readonly byte[] Payload = Encoding.UTF8.GetBytes("owned inert test payload");
        private readonly string _identity = Guid.NewGuid().ToString("N");
        private bool _createdKey;
        private readonly ToolUpdateProduct _product;
        private readonly string _registryPath;
        internal string Root { get; }
        internal string? Executable { get; set; }
        internal string Mode { get; set; } = "abort";
        internal string? LaunchPath { get; private set; }
        internal bool UseShellExecute { get; private set; }
        internal List<string> Requests { get; } = [];
        internal List<bool> RequestCancellations { get; } = [];
        internal List<string> AssetRequests { get; } = [];
        internal StringWriter Error { get; } = new();
        internal int ProcessPathCalls { get; private set; }
        internal int OverrideCalls { get; private set; }
        internal int Launches { get; private set; }
        internal Probe(ToolUpdateProduct template)
        {
            _product = template with { UninstallKey = IdentityPrefix + _identity };
            _registryPath = RegistryPrefix + _product.UninstallKey + "_is1";
            Root = Path.Combine(Path.GetTempPath(), "WinMon.InstallationDetectionTests", _identity);
        }
        internal void Configure(object? location, RegistryValueKind kind)
        {
            _createdKey = true;
            using var key = Registry.CurrentUser.CreateSubKey(_registryPath, writable: true);
            if (location is not null) key.SetValue("InstallLocation", location, kind);
        }
        internal async Task AssertSelection(bool installed, bool? detectionOverride = null)
        {
            Assert.That(await Run(ToolUpdateAction.Install, detectionOverride), Is.EqualTo(1));
            var expected = _product.AssetStem + "-" + Version + (installed ? "-Setup-x64.exe" : "-win-x64.zip");
            Assert.That(AssetRequests, Is.EqualTo(new[] { expected }));
            Assert.That(Requests.Count, Is.EqualTo(4));
            Assert.That(Error.ToString(), Does.Contain("404"));
            Assert.That(Launches, Is.Zero);
            Assert.That(Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories), Is.Empty);
            Assert.That(Directory.GetDirectories(Root), Is.Empty);
        }
        internal string AssetName(bool installed) => _product.AssetStem + "-" + Version + (installed ? "-Setup-x64.exe" : "-win-x64.zip");
        internal async Task<int> Run(ToolUpdateAction action, bool? detectionOverride = null, CancellationToken cancellationToken = default)
        {
            using var key = RSA.Create(2048);
            var portable = _product.AssetStem + "-" + Version + "-win-x64.zip";
            var installer = _product.AssetStem + "-" + Version + "-Setup-x64.exe";
            var manifest = JsonSerializer.SerializeToUtf8Bytes(new { schemaVersion = 1, version = Version, runtime = "win-x64", portable = Asset(portable), installer = Asset(installer) });
            var signature = Encoding.UTF8.GetBytes(Convert.ToBase64String(key.SignData(manifest, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)));
            using var handler = new Handler((request, token) =>
            {
                var path = request.RequestUri!.AbsolutePath; Requests.Add(request.RequestUri.AbsoluteUri); RequestCancellations.Add(token.IsCancellationRequested);
                if (path.EndsWith("/latest", StringComparison.Ordinal))
                {
                    if (Mode == "not-found") return new HttpResponseMessage(HttpStatusCode.NotFound);
                    var names = new[] { _product.ManifestName, _product.SignatureName, portable, installer }.Where(name => Mode switch
                    {
                        "missing-metadata" => name != _product.ManifestName,
                        "missing-signature" => name != _product.SignatureName,
                        "missing-portable" => name != portable,
                        "missing-installer" => name != installer,
                        _ => true
                    });
                    return Bytes(JsonSerializer.SerializeToUtf8Bytes(new { draft = Mode == "draft", prerelease = Mode == "prerelease", tag_name = Mode == "invalid-version" ? "invalid" : Mode == "stale" ? "v0.0.0" : "v" + Version, assets = names.Select(name => new { name }) }));
                }
                if (path.EndsWith("/fixture/manifest", StringComparison.Ordinal)) return Bytes(manifest);
                if (path.EndsWith("/" + _product.ManifestName, StringComparison.Ordinal))
                {
                    if (!Mode.StartsWith("redirect", StringComparison.Ordinal)) return Bytes(manifest);
                    var redirect = new HttpResponseMessage(HttpStatusCode.Found);
                    if (Mode != "redirect-no-location") redirect.Headers.Location = Mode switch
                    {
                        "redirect-relative" => new Uri("/fixture/manifest", UriKind.Relative),
                        "redirect-cdn" => new Uri("https://objects.GitHubUserContent.com/fixture/manifest"),
                        "redirect-http" => new Uri("http://github.com/fixture/manifest"),
                        "redirect-foreign" => new Uri("https://example.invalid/fixture/manifest"),
                        "redirect-apex" => new Uri("https://githubusercontent.com/fixture/manifest"),
                        _ => request.RequestUri
                    };
                    return redirect;
                }
                if (path.EndsWith("/" + _product.SignatureName, StringComparison.Ordinal)) return Bytes(signature);
                AssetRequests.Add(Path.GetFileName(path));
                if (Mode == "payload-header-length") return Bytes(Payload.Append((byte)1).ToArray());
                if (Mode == "payload-chunk-oversize") return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new NonSeekableMemoryStream(Payload.Append((byte)1).ToArray())) };
                if (Mode == "payload-checksum") { var changed = Payload.ToArray(); changed[0] ^= 1; return Bytes(changed); }
                if (Mode.StartsWith("payload-", StringComparison.Ordinal)) return Bytes(Payload);
                return new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new ByteArrayContent([]) };
            });
            var dependencies = new ToolUpdateDependencies
            {
                Handler = handler, PublicKey = key.ExportSubjectPublicKeyInfoPem(), StagingRoot = Root,
                ProcessPath = () => { ProcessPathCalls++; return Executable; },
                IsInstalled = detectionOverride is null ? null : (product, executable) =>
                {
                    OverrideCalls++; Assert.That(product.UninstallKey, Is.EqualTo(_product.UninstallKey));
                    Assert.That(executable, Is.EqualTo(Executable)); return detectionOverride.Value;
                },
                StartInstaller = (ProcessStartInfo start) =>
                {
                    Launches++; LaunchPath = start.FileName; UseShellExecute = start.UseShellExecute;
                    if (Mode == "payload-launch-throws") throw new InvalidOperationException("Controlled fake launcher failure.");
                    return Mode == "payload-started";
                }
            };
            using var output = new StringWriter();
            return await ToolUpdateCommand.RunAsync(_product, action, output, Error, cancellationToken, dependencies);
        }
        public void Dispose()
        {
            if (_registryPath != RegistryPrefix + IdentityPrefix + _identity + "_is1") throw new InvalidOperationException("Refusing registry cleanup outside the owned test identity.");
            if (_createdKey) Registry.CurrentUser.DeleteSubKeyTree(_registryPath, throwOnMissingSubKey: false);
            var expected = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "WinMon.InstallationDetectionTests", _identity));
            if (!Path.GetFullPath(Root).Equals(expected, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Refusing cleanup outside the owned test directory.");
            if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
            Error.Dispose();
        }
        private static object Asset(string name) => new { name, sha256 = Convert.ToHexString(SHA256.HashData(Payload)), size = Payload.Length };
        private static HttpResponseMessage Bytes(byte[] bytes) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
    }
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(respond(request, cancellationToken));
    }
    private sealed class NonSeekableMemoryStream(byte[] bytes) : MemoryStream(bytes, writable: false)
    {
        public override bool CanSeek => false;
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    }
}
