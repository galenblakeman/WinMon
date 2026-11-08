using System.IO.Pipes;
using System.Security.Principal;
using WinMon.Monitoring;

namespace WinMon.NetworkSetup;

internal static class Program
{
    private static readonly TimeSpan SetupTimeout = TimeSpan.FromSeconds(75);

    [STAThread]
    private static async Task<int> Main(string[] args)
    {
        try
        {
            var invocation = SetupArguments.Parse(args);
            using var identity = WindowsIdentity.GetCurrent();
            if (!new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))
                throw new UnauthorizedAccessException("Setup requires an elevated administrator token.");
            if (invocation.Uninstall)
            {
                SetupPaths.RequireInstalledHelper();
                await SetupService.RemoveAsync(CancellationToken.None).ConfigureAwait(false);
                return SetupService.RestartRequired ? 3010 : 0;
            }

            if (invocation.RevokeUser is { } sid)
            {
                SetupPaths.RequireInstalledHelper();
                await SetupService.RevokeAsync(sid, CancellationToken.None).ConfigureAwait(false);
                return SetupService.RestartRequired ? 3010 : 0;
            }

            await RunHandshakeAsync(invocation).ConfigureAwait(false);
            return 0;
        }
        catch (OperationCanceledException)
        {
            return 1;
        }
        catch (Exception)
        {
            return 1;
        }
    }

    private static async Task RunHandshakeAsync(SetupArguments invocation)
    {
        using var timeout = new CancellationTokenSource(SetupTimeout);
        using var package = OpenOwnPackage();
        await using var pipe = new NamedPipeClientStream(".", invocation.PipeName!,
            PipeAccessRights.ReadData | PipeAccessRights.WriteData | PipeAccessRights.ReadPermissions |
            PipeAccessRights.Synchronize,
            PipeOptions.Asynchronous | PipeOptions.WriteThrough, TokenImpersonationLevel.Identification,
            HandleInheritability.None);
        await pipe.ConnectAsync(timeout.Token).ConfigureAwait(false);
        using var requester = PipeRequesterIdentity.FromServer(pipe);
        var request = await NetworkSetupWire
            .ReadAsync(pipe, NetworkSetupWireJsonContext.Default.NetworkSetupRequest, timeout.Token)
            .ConfigureAwait(false);
        if (request.Version != NetworkSetupWire.Version || !invocation.MatchesToken(request.Token) ||
            request.Operation is not ("install" or "remove"))
            throw new UnauthorizedAccessException("Invalid setup request.");
        requester.VerifyStillSameProcess();
        try
        {
            await using var transaction = await SetupService
                .BeginAsync(request.Operation, requester.Sid, package, timeout.Token).ConfigureAwait(false);
            await NetworkSetupWire.WriteAsync(pipe, new NetworkSetupReply(NetworkSetupWire.Version, "ready"),
                NetworkSetupWireJsonContext.Default.NetworkSetupReply, timeout.Token).ConfigureAwait(false);
            var commit = await NetworkSetupWire
                .ReadAsync(pipe, NetworkSetupWireJsonContext.Default.NetworkSetupCommit, timeout.Token)
                .ConfigureAwait(false);
            requester.VerifyStillSameProcess();
            if (commit.Version != NetworkSetupWire.Version || !commit.Commit) throw new OperationCanceledException();
            await transaction.CommitAsync(timeout.Token).ConfigureAwait(false);
            await NetworkSetupWire.WriteAsync(pipe, new NetworkSetupReply(NetworkSetupWire.Version, "complete"),
                NetworkSetupWireJsonContext.Default.NetworkSetupReply, timeout.Token).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            var failure = SetupFailure.From(exception);
            using var replyTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            try
            {
                await NetworkSetupWire.WriteAsync(pipe,
                    new NetworkSetupReply(NetworkSetupWire.Version, "failed", failure.Phase, failure.ErrorCode),
                    NetworkSetupWireJsonContext.Default.NetworkSetupReply, replyTimeout.Token).ConfigureAwait(false);
            }
            // A failed best-effort reply must not replace the original setup exception.
            // ReSharper disable once EmptyGeneralCatchClause
            catch (Exception)
            {
            }

            throw;
        }
    }

    private static NetworkSetupPackage OpenOwnPackage()
    {
        var directory = SetupPaths.RequireOwnDirectory();
        var path = Path.GetFullPath(Environment.ProcessPath ??
                                    throw new InvalidOperationException("Missing setup executable."));
        if (!string.Equals(path, Path.Combine(directory, SetupPaths.SetupExecutableName),
                StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("Untrusted setup executable name.");
        return NetworkSetupPackage.Open(directory, typeof(Program).Assembly.GetName().Version!.ToString(3));
    }
}