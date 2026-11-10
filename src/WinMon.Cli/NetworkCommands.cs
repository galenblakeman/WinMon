using System.CommandLine;
using System.ComponentModel;
using System.Diagnostics;
using WinMon.Monitoring;
using WinMon.Resources;

namespace WinMon.Cli;

public static class NetworkCommands
{
    public static Command Create(TextWriter output, TextWriter error,
        Func<string, CancellationToken, Task>? run = null)
    {
        var command = new Command("network", Strings.NetworkProcessMonitoringHeading);
        foreach (var operation in new[] { "start", "install" })
        {
            var action = new Command(operation, operation == "start" ? Strings.CliNetworkStart : Strings.CliNetworkInstall);
            action.SetAction(async (_, token) =>
            {
                try
                {
                    if (run is null)
                    {
                        if (!NetworkSetupBroker.Supported)
                        {
                            await error.WriteLineAsync(Strings.NetworkProcessMonitoringStoreUnavailable);
                            return 1;
                        }
                        if (operation == "install" && !NetworkSetupBroker.IsAvailable)
                        {
                            await error.WriteLineAsync(Strings.CliNetworkBundleRequired);
                            return 1;
                        }
                        if (operation == "install")
                        {
                            await output.WriteLineAsync(Strings.NetworkProcessMonitoringNote);
                            await new NetworkSetupBroker().RunAsync("install", VerifyAsync, token);
                        }
                        else await VerifyAsync(token);
                    }
                    else await run(operation, token);
                    await output.WriteLineAsync(Strings.CliNetworkReady);
                    return 0;
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { return 130; }
                catch (Win32Exception exception) when (exception.NativeErrorCode == 1223)
                {
                    await error.WriteLineAsync(Strings.NetworkProcessMonitoringCancelled);
                    return 1;
                }
                catch (Exception exception)
                {
                    Trace.TraceError("CLI network setup failed: {0}", exception);
                    await error.WriteLineAsync(Strings.NetworkProcessMonitoringFailed);
                    await error.WriteLineAsync(Strings.CliNetworkSetupHint);
                    return 1;
                }
            });
            command.Add(action);
        }
        return command;
    }

    private static async Task VerifyAsync(CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        await using var client = await NetworkServiceClient.ConnectAsync(timeout.Token);
        await client.SubscribeAsync(timeout.Token);
        var snapshot = await client.SnapshotAsync(timeout.Token);
        if (snapshot.Quality.HasFlag(ProcessNetworkQuality.Unavailable))
            throw new InvalidOperationException("Network collection is unavailable.");
    }
}
