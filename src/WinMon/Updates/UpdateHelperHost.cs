using System.Diagnostics;

namespace WinMon.Updates;

// Keep process identity, OS handles and time at the edge; the helper owns validation and file transactions.
internal class UpdateHelperHost
{
    internal virtual string StagingRoot => UpdateClient.UpdateRoot;
    internal virtual string ProcessPath => Environment.ProcessPath!;
    internal virtual string PublicKey => UpdateClient.ReadPublicKey();
    internal virtual string CurrentVersion => AppInfo.Version;
    internal virtual DateTime UtcNow => DateTime.UtcNow;
    internal virtual bool IsInstalled(string target) => UpdateInstallation.IsInstalled(target);
    internal virtual bool IsProcessRunning(int id, long ticks) => UpdateRuntime.IsProcessRunning(id, ticks);
    internal virtual IUpdateHelperProcess OpenProcess(int id) => new HelperProcess(Process.GetProcessById(id));
    internal virtual IUpdateHelperProcess? Start(ProcessStartInfo start)
    {
        var process = Process.Start(start);
        return process is null ? null : new HelperProcess(process);
    }

    internal virtual Mutex CreateMutex() => new(false, UpdateRuntime.UpdateMutexName);

    internal virtual EventWaitHandle Signal(string token, string name, bool create = false) => create
        ? new EventWaitHandle(false, EventResetMode.ManualReset, UpdateLauncher.EventName(token, name))
        : EventWaitHandle.OpenExisting(UpdateLauncher.EventName(token, name));

    internal virtual void WriteState(string directory, UpdateState state) =>
        UpdateFiles.WriteJson(Path.Combine(directory, "state.json"), state);

    internal virtual void Sleep(int milliseconds) => Thread.Sleep(milliseconds);

    private sealed class HelperProcess(Process process) : IUpdateHelperProcess
    {
        public bool HasExited => process.HasExited;
        public int ExitCode => process.ExitCode;
        public long StartTicks => process.StartTime.ToUniversalTime().Ticks;
        public string FileName => process.MainModule!.FileName;
        public bool WaitForExit(int milliseconds) => process.WaitForExit(milliseconds);
        public void WaitForExit() => process.WaitForExit();
        public void CloseMainWindow() => process.CloseMainWindow();
        public void Kill() => process.Kill();
        public void Dispose() => process.Dispose();
    }
}

internal interface IUpdateHelperProcess : IDisposable
{
    bool HasExited { get; }
    int ExitCode { get; }
    long StartTicks { get; }
    string FileName { get; }
    bool WaitForExit(int milliseconds);
    void WaitForExit();
    void CloseMainWindow();
    void Kill();
}
