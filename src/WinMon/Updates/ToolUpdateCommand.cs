using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;
using WinMon.Resources;

namespace WinMon.Updates;

/// <summary>Explicit signed update commands for standalone WinMon tools.</summary>
internal static class ToolUpdateCommand
{
    private const string LatestReleaseUrl = "https://api.github.com/repos/galenblakeman/WinMon/releases/latest";

    internal static async Task<int> RunAsync(ToolUpdateProduct product, ToolUpdateAction action, TextWriter output,
        TextWriter error, CancellationToken cancellationToken, ToolUpdateDependencies? dependencies = null)
    {
        ArgumentNullException.ThrowIfNull(product);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);
        dependencies ??= new ToolUpdateDependencies();
        try
        {
            if (!UpdatePolicy.GitHubUpdatesEnabled)
            {
                await error.WriteLineAsync(Strings.ToolUpdateStore).ConfigureAwait(false);
                return 1;
            }
            UpdatePolicy.RequireGitHubUpdates();
            using var http = dependencies.CreateHttpClient();
            var current = CurrentVersion();
            var offer = await CheckAsync(product, current, http, dependencies.PublicKey ?? ReadPublicKey(), cancellationToken)
                .ConfigureAwait(false);
            if (offer is null)
            {
                await output.WriteLineAsync(string.Format(Strings.ToolUpdateCurrent, product.DisplayName, current)).ConfigureAwait(false);
                return 0;
            }
            await output.WriteLineAsync(string.Format(Strings.ToolUpdateAvailable, product.DisplayName, offer.Version, current))
                .ConfigureAwait(false);
            if (action == ToolUpdateAction.Check) return 0;

            var installed = IsInstalled(product, dependencies.ProcessPath(), dependencies);
            var asset = installed ? offer.Manifest.Installer : offer.Manifest.Portable;
            var downloaded = await DownloadAsync(product, offer.Version, asset, http, dependencies, cancellationToken).ConfigureAwait(false);
            if (!installed)
            {
                // shortcut: portable copies receive a ZIP; add a detached helper for in-place replacement.
                await output.WriteLineAsync(string.Format(Strings.ToolUpdatePortableDownloaded, downloaded)).ConfigureAwait(false);
                return 0;
            }
            bool started;
            try
            {
                started = dependencies.StartInstaller(new ProcessStartInfo(downloaded) { UseShellExecute = true });
            }
            catch
            {
                TryDelete(Path.GetDirectoryName(downloaded)!);
                throw;
            }
            if (!started)
            {
                TryDelete(Path.GetDirectoryName(downloaded)!);
                throw new InvalidOperationException("Could not start the update installer.");
            }
            await output.WriteLineAsync(string.Format(Strings.ToolUpdateInstallerStarted, product.DisplayName)).ConfigureAwait(false);
            return 0;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return 130;
        }
        catch (Exception exception)
        {
            await error.WriteLineAsync(string.Format(Strings.ToolUpdateFailed, exception.Message)).ConfigureAwait(false);
            return 1;
        }
    }

    private static async Task<ToolUpdateOffer?> CheckAsync(ToolUpdateProduct product, string current, HttpClient http,
        string publicKey, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseUrl);
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        using var release = JsonDocument.Parse(await ReadBoundedAsync(response, 1024 * 1024, timeout.Token).ConfigureAwait(false));
        var root = release.RootElement;
        if (root.GetProperty("draft").GetBoolean() || root.GetProperty("prerelease").GetBoolean()) return null;
        var tag = root.GetProperty("tag_name").GetString() ?? "";
        var version = tag.StartsWith('v') ? tag[1..] : tag;
        if (UpdateManifest.ParseVersion(version) <= UpdateManifest.ParseVersion(current)) return null;
        var names = root.GetProperty("assets").EnumerateArray().Select(a => a.GetProperty("name").GetString() ?? "")
            .ToHashSet(StringComparer.Ordinal);
        if (!names.Contains(product.ManifestName) || !names.Contains(product.SignatureName))
            throw new InvalidDataException("This release is not configured for verified updates.");
        using var manifestResponse = await GetAsync(http, AssetUrl(version, product.ManifestName), timeout.Token).ConfigureAwait(false);
        var bytes = await ReadBoundedAsync(manifestResponse, 65536, timeout.Token).ConfigureAwait(false);
        using var signatureResponse = await GetAsync(http, AssetUrl(version, product.SignatureName), timeout.Token).ConfigureAwait(false);
        var signature = Encoding.UTF8.GetString(await ReadBoundedAsync(signatureResponse, 4096, timeout.Token).ConfigureAwait(false));
        var manifest = UpdateManifest.Verify(bytes, signature, publicKey, version, product.AssetStem);
        if (!names.Contains(manifest.Portable.Name) || !names.Contains(manifest.Installer.Name))
            throw new InvalidDataException("Incomplete release assets.");
        return new ToolUpdateOffer(version, manifest);
    }

    private static async Task<string> DownloadAsync(ToolUpdateProduct product, string version, UpdateAsset asset, HttpClient http,
        ToolUpdateDependencies dependencies, CancellationToken cancellationToken)
    {
        var directory = Path.Combine(dependencies.StagingRoot ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinMon", product.AssetStem,
            "Updates"), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, asset.Name);
        try
        {
            using var totalTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            totalTimeout.CancelAfter(dependencies.DownloadTimeout);
            using var response = await GetAsync(http, AssetUrl(version, asset.Name), totalTimeout.Token).ConfigureAwait(false);
            if (response.Content.Headers.ContentLength is { } length && length != asset.Size)
                throw new InvalidDataException("Unexpected download size.");
            await using (var input = await response.Content.ReadAsStreamAsync(totalTimeout.Token).ConfigureAwait(false))
            await using (var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920,
                             FileOptions.Asynchronous))
            {
                var buffer = new byte[81920];
                long total = 0;
                int count;
                using var stalled = CancellationTokenSource.CreateLinkedTokenSource(totalTimeout.Token);
                stalled.CancelAfter(dependencies.DownloadStallTimeout);
                while ((count = await input.ReadAsync(buffer, stalled.Token).ConfigureAwait(false)) > 0)
                {
                    stalled.CancelAfter(dependencies.DownloadStallTimeout);
                    total += count;
                    if (total > asset.Size) throw new InvalidDataException("Download exceeds the expected size.");
                    await file.WriteAsync(buffer.AsMemory(0, count), totalTimeout.Token).ConfigureAwait(false);
                }
                await file.FlushAsync(totalTimeout.Token).ConfigureAwait(false);
            }
            await UpdateManifest.VerifyFileAsync(path, asset, totalTimeout.Token).ConfigureAwait(false);
            return path;
        }
        catch
        {
            try { Directory.Delete(directory, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            throw;
        }
    }

    private static void TryDelete(string directory)
    {
        try { Directory.Delete(directory, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private static bool IsInstalled(ToolUpdateProduct product, string? executable, ToolUpdateDependencies dependencies)
    {
        if (dependencies.IsInstalled is not null) return dependencies.IsInstalled(product, executable);
        if (string.IsNullOrWhiteSpace(executable)) return false;
        using var key = Registry.CurrentUser.OpenSubKey($@"Software\Microsoft\Windows\CurrentVersion\Uninstall\{product.UninstallKey}_is1");
        if (key?.GetValue("InstallLocation") is not string directory || string.IsNullOrWhiteSpace(directory)) return false;
        try { return Path.GetFullPath(Path.Combine(directory, product.ExecutableName)).Equals(Path.GetFullPath(executable), StringComparison.OrdinalIgnoreCase); }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException) { return false; }
    }

    private static string CurrentVersion() => Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";
    private static string ReadPublicKey()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("WinMon.UpdatePublicKey")
            ?? throw new InvalidDataException("Trusted update signing is not configured.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
    private static Uri AssetUrl(string version, string name) => new($"https://github.com/galenblakeman/WinMon/releases/download/v{version}/{name}");
    private static async Task<HttpResponseMessage> GetAsync(HttpClient http, Uri url, CancellationToken cancellationToken)
    {
        for (var redirects = 0; redirects < 6; redirects++)
        {
            if (url.Scheme != "https" || !(url.Host is "github.com" or "api.github.com" || url.Host.EndsWith(".githubusercontent.com", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("Unexpected update download location.");
            var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            if ((int)response.StatusCode is >= 300 and < 400 && response.Headers.Location is { } location)
            { url = location.IsAbsoluteUri ? location : new Uri(url, location); response.Dispose(); continue; }
            try { response.EnsureSuccessStatusCode(); return response; } catch { response.Dispose(); throw; }
        }
        throw new HttpRequestException("Too many update redirects.");
    }
    private static async Task<byte[]> ReadBoundedAsync(HttpResponseMessage response, int limit, CancellationToken cancellationToken)
    {
        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var output = new MemoryStream(); var buffer = new byte[8192]; int count;
        while ((count = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        { if (output.Length + count > limit) throw new InvalidDataException("Update metadata exceeds its size limit."); output.Write(buffer, 0, count); }
        return output.ToArray();
    }
}

internal enum ToolUpdateAction { Check, Install }
internal sealed record ToolUpdateProduct(string DisplayName, string AssetStem, string ManifestName, string ExecutableName, string UninstallKey)
{
    internal string SignatureName => Path.ChangeExtension(ManifestName, ".sig");
    internal static ToolUpdateProduct Cli { get; } = new("WinMon CLI", "WinMon-Cli", "cli-update-manifest.json", "WinMon.Cli.exe", "{8C69DAA2-DA6F-4952-BBE8-8AD1FDC6E2B9}");
    internal static ToolUpdateProduct Mcp { get; } = new("WinMon MCP", "WinMon-Mcp", "mcp-update-manifest.json", "WinMon.Mcp.exe", "{9A78FCFC-CCEE-4D67-9FFB-CA1DA6E1DDE0}");
}
internal sealed class ToolUpdateDependencies
{
    internal HttpMessageHandler? Handler { get; init; }
    internal string? PublicKey { get; init; }
    internal string? StagingRoot { get; init; }
    internal TimeSpan DownloadTimeout { get; init; } = TimeSpan.FromMinutes(15);
    internal TimeSpan DownloadStallTimeout { get; init; } = TimeSpan.FromSeconds(60);
    internal Func<string?> ProcessPath { get; init; } = () => Environment.ProcessPath;
    internal Func<ToolUpdateProduct, string?, bool>? IsInstalled { get; init; }
    internal Func<ProcessStartInfo, bool> StartInstaller { get; init; } = startInfo => Process.Start(startInfo) is not null;
    internal HttpClient CreateHttpClient()
    {
        var client = new HttpClient(Handler ?? new HttpClientHandler { AllowAutoRedirect = false }, Handler is null) { Timeout = TimeSpan.FromMinutes(15) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("WinMon/" + Assembly.GetExecutingAssembly().GetName().Version?.ToString(3));
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }
}
internal sealed record ToolUpdateOffer(string Version, UpdateManifest Manifest);
