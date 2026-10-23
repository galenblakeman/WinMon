using System.Net;
using System.Net.Http;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NUnit.Framework;
using WinMon.Preferences;
using WinMon.Updates;

namespace WinMon.Tests;

[TestFixture]
public sealed class UpdateTests
{
    private string _directory = null!;

    [SetUp]
    public void SetUp() => _directory = Path.Combine(Path.GetTempPath(), "WinMon.Tests", Guid.NewGuid().ToString("N"));

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    [TestCase("1.0.0")]
    [TestCase("99999.99999.99999")]
    public void ParseVersion_AcceptsReleaseTriplets(string value) =>
        Assert.That(UpdateManifest.ParseVersion(value).ToString(3), Is.EqualTo(value));

    [TestCase("v1.0.0")]
    [TestCase("1.0")]
    [TestCase("1.0.0.0")]
    [TestCase("1.0.0-beta")]
    [TestCase("100000.1.1")]
    [TestCase("1.-1.1")]
    public void ParseVersion_RejectsAnythingExceptReleaseTriplets(string value) =>
        Assert.That(() => UpdateManifest.ParseVersion(value), Throws.InstanceOf<InvalidDataException>());

    [Test]
    public void Verify_AcceptsExactlySignedExpectedManifest()
    {
        using var key = RSA.Create(2048);
        var (bytes, signature) = SignedManifest(key, "1.2.3");

        var manifest = UpdateManifest.Verify(bytes, signature, key.ExportSubjectPublicKeyInfoPem(), "1.2.3");

        Assert.Multiple(() =>
        {
            Assert.That(manifest.Version, Is.EqualTo("1.2.3"));
            Assert.That(manifest.Portable.Name, Is.EqualTo("WinMon-1.2.3-win-x64.zip"));
            Assert.That(manifest.Installer.Name, Is.EqualTo("WinMon-1.2.3-Setup-x64.exe"));
        });
    }

    [Test]
    public void Verify_RejectsTamperingWrongKeyAndWrongExpectedVersion()
    {
        using var key = RSA.Create(2048);
        using var wrongKey = RSA.Create(2048);
        var (bytes, signature) = SignedManifest(key, "1.2.3");
        var tampered = bytes.ToArray();
        tampered[^1] ^= 1;

        Assert.Multiple(() =>
        {
            Assert.That(() => UpdateManifest.Verify(tampered, signature, key.ExportSubjectPublicKeyInfoPem(), "1.2.3"), Throws.InstanceOf<InvalidDataException>());
            Assert.That(() => UpdateManifest.Verify(bytes, signature, wrongKey.ExportSubjectPublicKeyInfoPem(), "1.2.3"), Throws.InstanceOf<InvalidDataException>());
            Assert.That(() => UpdateManifest.Verify(bytes, signature, key.ExportSubjectPublicKeyInfoPem(), "1.2.4"), Throws.InstanceOf<InvalidDataException>());
            Assert.That(() => UpdateManifest.Verify(bytes, signature, "", "1.2.3"), Throws.InstanceOf<InvalidDataException>());
        });
    }

    [TestCase("schemaVersion", 2)]
    [TestCase("runtime", "win-arm64")]
    [TestCase("portable.name", "..\\WinMon.exe")]
    [TestCase("portable.sha256", "not-a-hash")]
    [TestCase("portable.size", 0)]
    [TestCase("installer.size", 536870913)]
    public void Verify_RejectsUnsupportedOrUnsafeFields(string path, object value)
    {
        using var key = RSA.Create(2048);
        var document = ManifestDocument("1.2.3");
        Set(document, path, value);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(document);
        var signature = Convert.ToBase64String(key.SignData(bytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));

        Assert.That(() => UpdateManifest.Verify(bytes, signature, key.ExportSubjectPublicKeyInfoPem(), "1.2.3"), Throws.InstanceOf<InvalidDataException>());
    }

    [Test]
    public async Task VerifyFileAsync_RequiresExactSizeAndHash()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "asset.bin");
        var bytes = Encoding.UTF8.GetBytes("update bytes");
        await File.WriteAllBytesAsync(path, bytes);
        var asset = new UpdateAsset("asset.bin", Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), bytes.Length);

        await UpdateManifest.VerifyFileAsync(path, asset, CancellationToken.None);
        await Assert.ThatAsync(() => UpdateManifest.VerifyFileAsync(path, asset with { Size = bytes.Length + 1 }, CancellationToken.None), Throws.InstanceOf<InvalidDataException>());
        await Assert.ThatAsync(() => UpdateManifest.VerifyFileAsync(path, asset with { Sha256 = new string('0', 64) }, CancellationToken.None), Throws.InstanceOf<InvalidDataException>());
    }

    [Test]
    public async Task AutomaticCheckPreference_DefaultsAndRoundTrips()
    {
        Directory.CreateDirectory(_directory);
        var store = new PreferenceStore(Path.Combine(_directory, "settings.json"));

        Assert.That((await store.LoadAsync()).AutomaticallyCheckForUpdates, Is.True);
        await store.SaveAsync(new MonitorPreferences { AutomaticallyCheckForUpdates = false });
        Assert.That((await store.LoadAsync()).AutomaticallyCheckForUpdates, Is.False);
    }

    [Test]
    public void PortableUpdate_ApplyAndRestorePreserveAdjacentPortableFiles()
    {
        Directory.CreateDirectory(_directory);
        var target = Path.Combine(_directory, "WinMon.exe");
        var staged = Path.Combine(_directory, ".WinMon-new.exe");
        var backup = Path.Combine(_directory, ".WinMon-backup.exe");
        var settings = Path.Combine(_directory, "settings.json");
        var flag = Path.Combine(_directory, "portable.flag");
        File.WriteAllText(target, "old");
        File.WriteAllText(staged, "new");
        File.WriteAllText(settings, "settings");
        File.WriteAllText(flag, "");

        PortableUpdate.Apply(staged, target, backup);

        Assert.Multiple(() =>
        {
            Assert.That(File.ReadAllText(target), Is.EqualTo("new"));
            Assert.That(File.ReadAllText(backup), Is.EqualTo("old"));
            Assert.That(File.ReadAllText(settings), Is.EqualTo("settings"));
            Assert.That(File.Exists(flag), Is.True);
        });

        PortableUpdate.Restore(target, backup);
        Assert.Multiple(() =>
        {
            Assert.That(File.ReadAllText(target), Is.EqualTo("old"));
            Assert.That(File.Exists(backup), Is.False);
            Assert.That(File.ReadAllText(settings), Is.EqualTo("settings"));
            Assert.That(File.Exists(flag), Is.True);
        });
    }

    [Test]
    public void PortableUpdate_RejectsFilesOutsideTheApplicationDirectory()
    {
        var application = Path.Combine(_directory, "app");
        var other = Path.Combine(_directory, "other");
        Directory.CreateDirectory(application);
        Directory.CreateDirectory(other);
        var target = Path.Combine(application, "WinMon.exe");
        var staged = Path.Combine(other, "WinMon.exe");
        var backup = Path.Combine(application, "backup.exe");
        File.WriteAllText(target, "old");
        File.WriteAllText(staged, "new");

        Assert.That(() => PortableUpdate.Apply(staged, target, backup), Throws.InstanceOf<InvalidDataException>());
        Assert.That(File.ReadAllText(target), Is.EqualTo("old"));
    }

    [Test]
    public void InstalledLocation_MatchesOnlyTheRegisteredDirectory()
    {
        var target = Path.Combine(_directory, "WinMon", "WinMon.exe");
        Assert.Multiple(() =>
        {
            Assert.That(UpdateInstallation.MatchesInstallDirectory(target, Path.Combine(_directory, "WinMon")), Is.True);
            Assert.That(UpdateInstallation.MatchesInstallDirectory(target, Path.Combine(_directory, "Other")), Is.False);
            Assert.That(UpdateInstallation.MatchesInstallDirectory(target, null), Is.False);
        });
    }

    [Test]
    public async Task WriteJson_ConcurrentWritersLeaveACompleteReadableDocument()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "state.json");

        await Task.WhenAll(Enumerable.Range(0, 64).Select(index => Task.Run(() =>
            UpdateFiles.WriteJson(path, new UpdateState("writing", index.ToString())))));

        var state = UpdateFiles.ReadJson<UpdateState>(path);
        Assert.Multiple(() =>
        {
            Assert.That(state.Status, Is.EqualTo("writing"));
            Assert.That(int.TryParse(state.Error, out var writer), Is.True);
            Assert.That(writer, Is.InRange(0, 63));
            Assert.That(Directory.GetFiles(_directory, "*.tmp"), Is.Empty);
        });
    }

    [TestCase("Other.exe")]
    [TestCase("../WinMon.exe")]
    public async Task ExtractExecutableAsync_RejectsArchiveWithoutExactExecutable(string entryName)
    {
        var archive = CreateArchive(entryName);
        var destination = Path.Combine(_directory, "output", "WinMon.exe");

        await Assert.ThatAsync(() => UpdateClient.ExtractExecutableAsync(archive, destination, "1.2.3", CancellationToken.None), Throws.InstanceOf<InvalidDataException>());
        Assert.That(File.Exists(destination), Is.False);
    }

    [Test]
    public async Task ExtractExecutableAsync_RejectsDuplicateExactExecutable()
    {
        var archive = CreateArchive("WinMon.exe", "WinMon.exe");
        var destination = Path.Combine(_directory, "output", "WinMon.exe");

        await Assert.ThatAsync(() => UpdateClient.ExtractExecutableAsync(archive, destination, "1.2.3", CancellationToken.None), Throws.InstanceOf<InvalidDataException>());
        Assert.That(File.Exists(destination), Is.False);
    }

    [Test]
    public void CompleteInterruptedTransaction_FinalizesOnlyMatchingInactiveHealthyReplacement()
    {
        var (directory, target, job) = PrepareRecovery("1.2.3", "awaiting-ready");
        var backup = UpdateFiles.BackupPath(job);
        var settings = Path.Combine(Path.GetDirectoryName(target)!, "settings.json");
        File.WriteAllText(backup, "old executable");
        File.WriteAllText(settings, "portable settings");

        UpdateRuntime.CompleteInterruptedTransaction(directory, target, "1.2.3", activeToken: null);

        Assert.Multiple(() =>
        {
            Assert.That(UpdateFiles.ReadJson<UpdateState>(Path.Combine(directory, "state.json")).Status, Is.EqualTo("succeeded"));
            Assert.That(File.Exists(backup), Is.False);
            Assert.That(File.ReadAllText(settings), Is.EqualTo("portable settings"));
        });
    }

    [Test]
    public void CompleteInterruptedTransaction_LeavesLiveHelperAndWrongVersionPending()
    {
        var (liveDirectory, liveTarget, liveJob) = PrepareRecovery("1.2.3", "awaiting-ready");
        var (oldDirectory, oldTarget, oldJob) = PrepareRecovery("1.2.2", "awaiting-ready");
        File.WriteAllText(UpdateFiles.BackupPath(liveJob), "live backup");
        File.WriteAllText(UpdateFiles.BackupPath(oldJob), "old backup");

        UpdateRuntime.CompleteInterruptedTransaction(liveDirectory, liveTarget, "1.2.3", liveJob.Token);
        UpdateRuntime.CompleteInterruptedTransaction(oldDirectory, oldTarget, "1.2.3", activeToken: null);

        Assert.Multiple(() =>
        {
            Assert.That(UpdateFiles.ReadJson<UpdateState>(Path.Combine(liveDirectory, "state.json")).Status, Is.EqualTo("awaiting-ready"));
            Assert.That(File.Exists(UpdateFiles.BackupPath(liveJob)), Is.True);
            Assert.That(UpdateFiles.ReadJson<UpdateState>(Path.Combine(oldDirectory, "state.json")).Status, Is.EqualTo("awaiting-ready"));
            Assert.That(File.Exists(UpdateFiles.BackupPath(oldJob)), Is.True);
        });
    }

    [Test]
    public void CompleteInterruptedTransaction_RemovesEarlierSessionsAbandonedDownloadsAndUnappliedJobs()
    {
        var interrupted = Path.Combine(_directory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(interrupted);
        File.WriteAllText(Path.Combine(interrupted, "WinMon-1.2.3-win-x64.zip"), "partial download");
        var (unapplied, target, _) = PrepareRecovery("1.2.3", "unused");
        File.Delete(Path.Combine(unapplied, "state.json"));
        File.WriteAllText(Path.Combine(unapplied, "WinMon.Update.exe"), "helper");
        var current = Path.Combine(_directory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(current);
        Age(interrupted);
        Age(unapplied);

        foreach (var directory in new[] { interrupted, unapplied, current })
            UpdateRuntime.CompleteInterruptedTransaction(directory, target, "1.2.2", activeToken: null, sessionStartUtc: DateTime.UtcNow.AddMinutes(-1));

        Assert.Multiple(() =>
        {
            Assert.That(Directory.Exists(interrupted), Is.False);
            Assert.That(Directory.Exists(unapplied), Is.False);
            Assert.That(Directory.Exists(current), Is.True, "A download this session may have started must not be removed.");
            Assert.That(File.Exists(target), Is.True);
        });
    }

    [Test]
    public void CompleteInterruptedTransaction_KeepsTheActiveTransactionAndTheResultNoticeOfAFailedOne()
    {
        var (active, activeTarget, activeJob) = PrepareRecovery("1.2.3", "awaiting-ready");
        File.Delete(Path.Combine(active, "state.json"));
        var (failed, target, _) = PrepareRecovery("1.2.3", "rolled-back");
        File.WriteAllText(Path.Combine(failed, "WinMon.Update.exe"), "helper");
        File.WriteAllText(Path.Combine(failed, "verified.exe"), "extracted");
        Age(active);
        Age(failed);
        var start = DateTime.UtcNow.AddMinutes(-1);

        UpdateRuntime.CompleteInterruptedTransaction(active, activeTarget, "1.2.2", activeJob.Token, start);
        UpdateRuntime.CompleteInterruptedTransaction(failed, target, "1.2.2", activeToken: null, start);

        Assert.Multiple(() =>
        {
            Assert.That(Directory.Exists(active), Is.True);
            Assert.That(File.Exists(Path.Combine(failed, "WinMon.Update.exe")), Is.False);
            Assert.That(File.Exists(Path.Combine(failed, "verified.exe")), Is.False);
            Assert.That(UpdateFiles.ReadJson<UpdateState>(Path.Combine(failed, "state.json")).Status, Is.EqualTo("rolled-back"),
                "The result notice reads the state, so it stays until the notice has been shown.");
        });

        File.WriteAllText(Path.Combine(failed, "notice-shown"), "");
        UpdateRuntime.CompleteInterruptedTransaction(failed, target, "1.2.2", activeToken: null, start);

        Assert.That(Directory.Exists(failed), Is.False);
    }

    [Test]
    public void CompleteInterruptedTransaction_KeepsTheBackupWhileTheTargetIsMissing()
    {
        var (directory, target, job) = PrepareRecovery("1.2.3", "failed");
        File.Delete(target);
        File.WriteAllText(UpdateFiles.BackupPath(job), "only copy");
        File.WriteAllText(Path.Combine(directory, "notice-shown"), "");
        Age(directory);

        UpdateRuntime.CompleteInterruptedTransaction(directory, target, "1.2.2", activeToken: null, DateTime.UtcNow.AddMinutes(-1));

        Assert.Multiple(() =>
        {
            Assert.That(File.ReadAllText(UpdateFiles.BackupPath(job)), Is.EqualTo("only copy"));
            Assert.That(Directory.Exists(directory), Is.True);
        });
    }

    [Test]
    public void RecoverIncompleteReplace_RestoresTheOriginalOnlyWhenTheTargetIsMissing()
    {
        Directory.CreateDirectory(_directory);
        var target = Path.Combine(_directory, "WinMon.exe");
        var backup = Path.Combine(_directory, ".WinMon-token.backup.exe");
        File.WriteAllText(backup, "original");

        Assert.That(PortableUpdate.RecoverIncompleteReplace(target, backup), Is.True);
        Assert.That(File.ReadAllText(target), Is.EqualTo("original"));

        File.WriteAllText(backup, "second backup");
        Assert.Multiple(() =>
        {
            Assert.That(PortableUpdate.RecoverIncompleteReplace(target, backup), Is.False);
            Assert.That(File.ReadAllText(target), Is.EqualTo("original"));
            Assert.That(File.Exists(backup), Is.True);
        });
    }

    [Test]
    public void AvailableFreeSpace_ReadsLocalDirectoriesAndNetworkShares()
    {
        Directory.CreateDirectory(_directory);
        Assert.That(UpdateClient.AvailableFreeSpace(_directory), Is.GreaterThan(0));
        var root = Path.GetPathRoot(Path.GetFullPath(_directory))!;
        // An administrative share stands in for a network share where one is available.
        var share = $@"\\localhost\{root[0]}$";
        if (!Directory.Exists(share)) Assert.Ignore("No administrative share is available to read through UNC.");
        Assert.That(UpdateClient.AvailableFreeSpace(share), Is.GreaterThan(0));
    }

    private static void Age(string directory) => Directory.SetCreationTimeUtc(directory, DateTime.UtcNow.AddHours(-1));

    [Test]
    public async Task CheckAsync_ReturnsOnlySignedNewerEligibleRelease_AndUsesEtagCache()
    {
        using var key = RSA.Create(2048);
        var (manifest, signature) = SignedManifest(key, "1.2.3");
        var calls = new List<HttpRequestMessage>();
        using var client = new UpdateClient(new HttpClient(new DelegateHandler(request =>
        {
            calls.Add(request);
            if (request.RequestUri!.AbsolutePath.EndsWith("/latest"))
                return calls.Count(r => r.RequestUri!.AbsolutePath.EndsWith("/latest")) == 1
                    ? JsonResponse(Release("v1.2.3"), "\"release-1\"")
                    : new HttpResponseMessage(HttpStatusCode.NotModified);
            if (request.RequestUri.AbsolutePath.EndsWith("update-manifest.json")) return BytesResponse(manifest);
            if (request.RequestUri.AbsolutePath.EndsWith("update-manifest.sig")) return BytesResponse(Encoding.UTF8.GetBytes(signature));
            throw new AssertionException(request.RequestUri.AbsolutePath);
        })), key.ExportSubjectPublicKeyInfoPem(), _directory);

        var first = await client.CheckAsync("1.0.2", CancellationToken.None);
        var second = await client.CheckAsync("1.0.2", CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(first?.Version, Is.EqualTo("1.2.3"));
            Assert.That(second?.Version, Is.EqualTo("1.2.3"));
            Assert.That(calls.Last(r => r.RequestUri!.AbsolutePath.EndsWith("/latest")).Headers.IfNoneMatch.Single().Tag, Is.EqualTo("\"release-1\""));
        });
    }

    [TestCase(true, false)]
    [TestCase(false, true)]
    public async Task CheckAsync_IgnoresDraftAndPrerelease(bool draft, bool prerelease)
    {
        using var client = new UpdateClient(new HttpClient(new DelegateHandler(_ => JsonResponse(Release("v9.0.0", draft, prerelease)))), "unused", _directory);
        Assert.That(await client.CheckAsync("1.0.2", CancellationToken.None), Is.Null);
    }

    [Test]
    public void CheckAsync_RejectsUnsignedAndOversizedReleaseMetadata()
    {
        using var unsigned = new UpdateClient(new HttpClient(new DelegateHandler(_ => JsonResponse(Release("v1.2.3", assets: [])))), "unused", _directory);
        Assert.ThatAsync(() => unsigned.CheckAsync("1.0.2", CancellationToken.None), Throws.InstanceOf<InvalidDataException>());

        var excessive = new string('x', 1024 * 1024 + 1);
        using var oversized = new UpdateClient(new HttpClient(new DelegateHandler(_ => BytesResponse(Encoding.UTF8.GetBytes(excessive)))), "unused", _directory);
        Assert.ThatAsync(() => oversized.CheckAsync("1.0.2", CancellationToken.None), Throws.InstanceOf<InvalidDataException>());
    }

    private static (byte[] Bytes, string Signature) SignedManifest(RSA key, string version)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(ManifestDocument(version));
        return (bytes, Convert.ToBase64String(key.SignData(bytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)));
    }

    private string CreateArchive(params string[] names)
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, Guid.NewGuid().ToString("N") + ".zip");
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var name in names)
        {
            var entry = archive.CreateEntry(name);
            using var stream = entry.Open();
            stream.WriteByte(1);
        }
        return path;
    }

    private (string Directory, string Target, UpdateJob Job) PrepareRecovery(string version, string status)
    {
        var directory = Path.Combine(_directory, Guid.NewGuid().ToString("N"));
        var application = Path.Combine(_directory, "app-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        Directory.CreateDirectory(application);
        var target = Path.Combine(application, "WinMon.exe");
        File.WriteAllText(target, "new executable");
        var job = new UpdateJob(target, 1, 1, version, false, Guid.NewGuid().ToString("N"));
        UpdateFiles.WriteJson(Path.Combine(directory, "job.json"), job);
        UpdateFiles.WriteJson(Path.Combine(directory, "state.json"), new UpdateState(status));
        return (directory, target, job);
    }

    private static Dictionary<string, object> ManifestDocument(string version) => new()
    {
        ["schemaVersion"] = 1,
        ["version"] = version,
        ["runtime"] = "win-x64",
        ["portable"] = Asset($"WinMon-{version}-win-x64.zip"),
        ["installer"] = Asset($"WinMon-{version}-Setup-x64.exe")
    };

    private static Dictionary<string, object> Asset(string name) => new() { ["name"] = name, ["sha256"] = new string('a', 64), ["size"] = 128L };

    private static void Set(Dictionary<string, object> document, string path, object value)
    {
        var split = path.Split('.');
        if (split.Length == 1) document[path] = value;
        else ((Dictionary<string, object>)document[split[0]])[split[1]] = value;
    }

    private static object Release(string tag, bool draft = false, bool prerelease = false, string[]? assets = null) => new
    {
        draft,
        prerelease,
        tag_name = tag,
        assets = (assets ?? ["update-manifest.json", "update-manifest.sig", $"WinMon-{tag[1..]}-win-x64.zip", $"WinMon-{tag[1..]}-Setup-x64.exe"]).Select(name => new { name })
    };

    private static HttpResponseMessage JsonResponse(object value, string? etag = null)
    {
        var response = BytesResponse(JsonSerializer.SerializeToUtf8Bytes(value));
        if (etag is not null) response.Headers.ETag = new System.Net.Http.Headers.EntityTagHeaderValue(etag);
        return response;
    }

    private static HttpResponseMessage BytesResponse(byte[] bytes) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };

    private sealed class DelegateHandler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(response(request));
    }
}
