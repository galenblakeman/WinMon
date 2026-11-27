using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using MonCore.Windows.Monitoring;

namespace WinMon.NetworkSetup;

internal static class PawnIoInstaller
{
    internal const string FailurePhase = "pawnio";

    internal static Task EnsureInstalledAsync(string installDirectory, CancellationToken token) =>
        EnsureInstalledAsync(PawnIoPrerequisite.IsInstalled, ct => RunInstallerAsync(installDirectory, ct), token);

    internal static async Task EnsureInstalledAsync(Func<bool> isInstalled, Func<CancellationToken, Task<int>> install,
        CancellationToken token)
    {
        try
        {
            token.ThrowIfCancellationRequested();
            if (isInstalled()) return;
            var exitCode = await install(token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if (exitCode != 0) throw new SetupFailure(FailurePhase, exitCode);
            if (!isInstalled()) throw new InvalidOperationException("PawnIO did not register after installation.");
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            throw SetupFailure.Wrap(FailurePhase, error);
        }
    }

    private static async Task<int> RunInstallerAsync(string installDirectory, CancellationToken token)
    {
        var assembly = typeof(PawnIoInstaller).Assembly;
        using var source = assembly.GetManifestResourceStream("WinMon.PawnIO.Setup.exe")
            ?? throw new InvalidDataException("The signed PawnIO installer is unavailable.");
        using var metadata = assembly.GetManifestResourceStream("WinMon.PawnIO.json")
            ?? throw new InvalidDataException("The PawnIO installer pin is unavailable.");
        using var pin = await JsonDocument.ParseAsync(metadata, cancellationToken: token).ConfigureAwait(false);
        var expectedHash = pin.RootElement.GetProperty("sha256").GetString();
        var stage = SetupService.StagingPath("pawnio", installDirectory);
        ProtectedInstallation.CreateDirectory(stage);
        try
        {
            var path = Path.Combine(stage, "PawnIO_setup.exe");
            await using (var destination = ProtectedInstallation.CreateFile(path, FileMode.CreateNew,
                             FileAccess.Write, FileShare.None, FileOptions.Asynchronous))
                await source.CopyToAsync(destination, token).ConfigureAwait(false);

            using var installer = File.OpenRead(path);
            var hash = await SHA256.HashDataAsync(installer, token).ConfigureAwait(false);
            if (!string.Equals(expectedHash, Convert.ToHexString(hash), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("PawnIO installer SHA-256 mismatch.");

            var start = new ProcessStartInfo(path)
            {
                UseShellExecute = false, WorkingDirectory = stage, CreateNoWindow = true,
                Arguments = "-install -silent"
            };
            start.Environment["TEMP"] = stage;
            start.Environment["TMP"] = stage;
            token.ThrowIfCancellationRequested();
            using var process = Process.Start(start) ?? throw new InvalidOperationException("PawnIO setup did not start.");
            // Keep the setup lock and staging alive while the machine-wide installer is running.
            await process.WaitForExitAsync().ConfigureAwait(false);
            return process.ExitCode;
        }
        finally
        {
            try { Directory.Delete(stage, recursive: true); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            { Trace.TraceWarning("WinMon could not clean up PawnIO setup files: {0}", error); }
        }
    }
}
