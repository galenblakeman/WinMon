using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;

namespace WinMon.Updates;

internal sealed class UpdateClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly string _publicKey;
    private readonly string _root;
    private string? _etag;
    private UpdateOffer? _cached;
    internal static string UpdateRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinMon", "Updates");

    internal UpdateClient() : this(new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }), ReadPublicKey(), UpdateRoot) { }
    internal UpdateClient(HttpClient http, string publicKey, string root)
    {
        _http = http;
        _http.Timeout = TimeSpan.FromMinutes(15);
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("WinMon/" + AppInfo.Version);
        _http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        _publicKey = publicKey;
        _root = root;
    }

    internal static string ReadPublicKey()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("WinMon.UpdatePublicKey");
        if (stream is null) return "";
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    internal async Task<UpdateOffer?> CheckAsync(string currentVersion, CancellationToken cancellationToken)
    {
        UpdatePolicy.RequireGitHubUpdates();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/repos/galenblakeman/WinMon/releases/latest");
        if (_etag is not null) request.Headers.TryAddWithoutValidation("If-None-Match", _etag);
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotModified)
            return _cached is not null && UpdateManifest.ParseVersion(_cached.Version) > UpdateManifest.ParseVersion(currentVersion) ? _cached : null;
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        var json = await ReadBoundedAsync(response, 1024 * 1024, timeout.Token).ConfigureAwait(false);
        using var document = JsonDocument.Parse(json);
        var release = document.RootElement;
        if (release.GetProperty("draft").GetBoolean() || release.GetProperty("prerelease").GetBoolean()) return null;
        var tag = release.GetProperty("tag_name").GetString() ?? "";
        var version = tag.StartsWith('v') ? tag[1..] : tag;
        if (UpdateManifest.ParseVersion(version) <= UpdateManifest.ParseVersion(currentVersion)) return null;
        var names = release.GetProperty("assets").EnumerateArray().Select(a => a.GetProperty("name").GetString()).ToHashSet(StringComparer.Ordinal);
        if (!names.Contains("update-manifest.json") || !names.Contains("update-manifest.sig"))
            throw new InvalidDataException("This release is not configured for verified automatic updates.");
        using var manifestResponse = await GetAsync(AssetUrl(version, "update-manifest.json"), timeout.Token).ConfigureAwait(false);
        var bytes = await ReadBoundedAsync(manifestResponse, 65536, timeout.Token).ConfigureAwait(false);
        using var signatureResponse = await GetAsync(AssetUrl(version, "update-manifest.sig"), timeout.Token).ConfigureAwait(false);
        var signature = System.Text.Encoding.UTF8.GetString(await ReadBoundedAsync(signatureResponse, 4096, timeout.Token).ConfigureAwait(false));
        var manifest = UpdateManifest.Verify(bytes, signature, _publicKey, version);
        if (!names.Contains(manifest.Portable.Name) || !names.Contains(manifest.Installer.Name)) throw new InvalidDataException("Incomplete release assets.");
        _cached = new UpdateOffer(version, new Uri($"https://github.com/galenblakeman/WinMon/releases/tag/v{version}"), manifest, bytes, signature);
        _etag = response.Headers.ETag?.ToString();
        return _cached;
    }

    internal async Task<PreparedUpdate> DownloadAsync(UpdateOffer offer, IProgress<int>? progress, CancellationToken cancellationToken)
    {
        UpdatePolicy.RequireGitHubUpdates();
        // Reverify before using cached metadata to select or execute an asset.
        var manifest = UpdateManifest.Verify(offer.ManifestBytes, offer.Signature, _publicKey, offer.Version);
        var target = Environment.ProcessPath ?? throw new InvalidOperationException("Cannot find the running executable.");
        if (!Path.GetFileName(target).Equals("WinMon.exe", StringComparison.OrdinalIgnoreCase) || File.Exists(Path.Combine(AppContext.BaseDirectory, "WinMon.dll")))
            throw new InvalidOperationException("Automatic updates require a published WinMon executable.");
        var installed = UpdateInstallation.IsInstalled(target);
        var asset = installed ? manifest.Installer : manifest.Portable;
        var directory = Path.Combine(_root, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var drive = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(target))!);
            if (drive.AvailableFreeSpace < asset.Size * 4 + new FileInfo(target).Length)
                throw new IOException("Not enough disk space to update WinMon.");
            var stagingDrive = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(directory))!);
            if (stagingDrive.AvailableFreeSpace < asset.Size * 4 + new FileInfo(target).Length)
                throw new IOException("Not enough staging space to download the update.");
            // Probe the destination before asking the application to exit.
            var probe = Path.Combine(Path.GetDirectoryName(target)!, ".winmon-update-" + Guid.NewGuid().ToString("N"));
            using (new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose)) { }
            var download = Path.Combine(directory, asset.Name);
            using (var response = await GetAsync(AssetUrl(offer.Version, asset.Name), cancellationToken).ConfigureAwait(false))
            {
                if (response.Content.Headers.ContentLength is { } length && length != asset.Size) throw new InvalidDataException("Unexpected download size.");
                await using var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                await using var output = new FileStream(download, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous);
                var buffer = new byte[81920];
                long total = 0;
                int read;
                while ((read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    total += read;
                    if (total > asset.Size) throw new InvalidDataException("Download exceeds the expected size.");
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                    progress?.Report((int)(total * 100 / asset.Size));
                }
            }
            await UpdateManifest.VerifyFileAsync(download, asset, cancellationToken).ConfigureAwait(false);
            await File.WriteAllBytesAsync(Path.Combine(directory, "update-manifest.json"), offer.ManifestBytes, cancellationToken).ConfigureAwait(false);
            await File.WriteAllTextAsync(Path.Combine(directory, "update-manifest.sig"), offer.Signature, cancellationToken).ConfigureAwait(false);
            if (!installed) await ExtractExecutableAsync(download, Path.Combine(directory, "WinMon.exe"), offer.Version, cancellationToken).ConfigureAwait(false);
            using var process = Process.GetCurrentProcess();
            var job = new UpdateJob(target, process.Id, process.StartTime.ToUniversalTime().Ticks, offer.Version, installed, Guid.NewGuid().ToString("N"));
            UpdateFiles.WriteJson(Path.Combine(directory, "job.json"), job);
            return new PreparedUpdate(directory);
        }
        catch
        {
            try { Directory.Delete(directory, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            throw;
        }
    }

    internal static async Task ExtractExecutableAsync(string archivePath, string destination, string version, CancellationToken cancellationToken)
    {
        using var archive = ZipFile.OpenRead(archivePath);
        var entries = archive.Entries.Where(e => e.FullName == "WinMon.exe").ToArray();
        if (entries.Length != 1 || entries[0].Length <= 0 || entries[0].Length > UpdateManifest.MaximumAssetBytes)
            throw new InvalidDataException("The portable archive must contain exactly one WinMon.exe.");
        await using (var input = entries[0].Open())
        await using (var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write))
            await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
        ValidateExecutableVersion(destination, version);
    }

    internal static void ValidateExecutableVersion(string path, string version)
    {
        if (FileVersionInfo.GetVersionInfo(path).ProductVersion != version) throw new InvalidDataException("The executable version does not match the release.");
    }

    private static Uri AssetUrl(string version, string name) => new($"https://github.com/galenblakeman/WinMon/releases/download/v{version}/{name}");
    private async Task<HttpResponseMessage> GetAsync(Uri url, CancellationToken cancellationToken)
    {
        for (var redirects = 0; redirects < 6; redirects++)
        {
            if (url.Scheme != "https" || !(url.Host is "github.com" or "api.github.com" || url.Host.EndsWith(".githubusercontent.com", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("Unexpected update download location.");
            var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            if ((int)response.StatusCode is >= 300 and < 400 && response.Headers.Location is { } location)
            {
                url = location.IsAbsoluteUri ? location : new Uri(url, location);
                response.Dispose();
                continue;
            }
            try { response.EnsureSuccessStatusCode(); return response; }
            catch { response.Dispose(); throw; }
        }
        throw new HttpRequestException("Too many update redirects.");
    }

    private static async Task<byte[]> ReadBoundedAsync(HttpResponseMessage response, int limit, CancellationToken token)
    {
        await using var input = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        using var output = new MemoryStream();
        var buffer = new byte[8192];
        int read;
        while ((read = await input.ReadAsync(buffer, token).ConfigureAwait(false)) > 0)
        {
            if (output.Length + read > limit) throw new InvalidDataException("Update metadata exceeds its size limit.");
            output.Write(buffer, 0, read);
        }
        return output.ToArray();
    }

    public void Dispose() => _http.Dispose();
}
