using System.Diagnostics;

namespace WinMon.Updates;

internal static class UpdateLauncher
{
    private static EventWaitHandle? _commit;
    private static EventWaitHandle? _cancel;

    internal static string EventName(string token, string kind)
    {
        return @"Local\WinMon.Update." + token + "." + kind;
    }

    /// <returns>The validated transaction directory and the job's token, which the restart history is saved under.</returns>
    internal static async Task<(string Directory, string Token)> StartAsync(PreparedUpdate update,
        CancellationToken cancellationToken)
    {
        UpdatePolicy.RequireGitHubUpdates();
        var directory = UpdateFiles.ValidateDirectory(update.TransactionDirectory);
        var job = UpdateFiles.ReadJson<UpdateJob>(Path.Combine(directory, "job.json"));
        using var ready = new EventWaitHandle(false, EventResetMode.ManualReset, EventName(job.Token, "Ready"));
        _commit = new EventWaitHandle(false, EventResetMode.ManualReset, EventName(job.Token, "Commit"));
        _cancel = new EventWaitHandle(false, EventResetMode.ManualReset, EventName(job.Token, "Cancel"));
        try
        {
            var helper = Path.Combine(directory, "WinMon.Update.exe");
            File.Copy(Environment.ProcessPath!, helper, false);
            var start = new ProcessStartInfo(helper)
                { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = directory };
            start.ArgumentList.Add("--apply-update");
            start.ArgumentList.Add(directory);
            start.ArgumentList.Add(job.Token);
            using var process = Process.Start(start) ?? throw new IOException("Cannot start the updater.");
            var deadline = DateTime.UtcNow.AddSeconds(30);
            while (!ready.WaitOne(0))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (process.HasExited || DateTime.UtcNow >= deadline)
                    throw new IOException("The updater did not become ready.");
                await Task.Delay(100, cancellationToken).ConfigureAwait(false);
            }

            return (directory, job.Token);
        }
        catch
        {
            Cancel();
            Dispose();
            throw;
        }
    }

    internal static void Commit()
    {
        _commit?.Set();
    }

    internal static void Cancel()
    {
        _cancel?.Set();
    }

    internal static void Dispose()
    {
        _commit?.Dispose();
        _cancel?.Dispose();
        _commit = null;
        _cancel = null;
    }
}
