using System.Diagnostics;
using NUnit.Framework;

namespace WinMon.NetworkSetup.Tests;

[TestFixture, Category("Integration")]
public sealed class ServiceProcessExitTests
{
    [Test]
    public async Task ProcessExitWaitKeepsFilesProtectedUntilProcessExitsAndPreservesCallerHandle()
    {
        using var child = StartChild();
        using var handle = NativeService.OpenProcessForExit((uint)child.Id);
        try
        {
            var wait = Task.Run(() => NativeService.WaitForProcessExit(handle, TimeSpan.FromSeconds(10), CancellationToken.None));
            await Task.Delay(100);
            Assert.That(wait.IsCompleted, Is.False, "A live process must prevent the file swap.");
            await child.StandardInput.WriteLineAsync("exit");
            await wait.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.That(child.WaitForExit(5000), Is.True);
            NativeService.WaitForProcessExit(handle, TimeSpan.Zero, CancellationToken.None);
            Assert.That(handle.IsClosed, Is.False);
        }
        finally { Finish(child); }
    }

    [Test]
    public void ProcessExitTimeoutLeavesProcessAliveAndCallerHandleOpen()
    {
        using var child = StartChild();
        using var handle = NativeService.OpenProcessForExit((uint)child.Id);
        try
        {
            Assert.Throws<TimeoutException>(() => NativeService.WaitForProcessExit(handle, TimeSpan.Zero, CancellationToken.None));
            Assert.That(child.HasExited, Is.False);
            Assert.That(handle.IsClosed, Is.False);
        }
        finally { Finish(child); }
    }

    [Test]
    public void ProcessExitCancellationLeavesProcessAliveAndCallerHandleOpen()
    {
        using var child = StartChild();
        using var handle = NativeService.OpenProcessForExit((uint)child.Id);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        try
        {
            Assert.Throws<OperationCanceledException>(() => NativeService.WaitForProcessExit(handle, TimeSpan.FromSeconds(10), cancellation.Token));
            Assert.That(child.HasExited, Is.False);
            Assert.That(handle.IsClosed, Is.False);
        }
        finally { Finish(child); }
    }

    [Test]
    public void MissingProcessIdCannotBeTreatedAsAnExitedService()
    {
        Assert.Throws<InvalidOperationException>(() => NativeService.OpenProcessForExit(0));
    }

    private static Process StartChild()
    {
        var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
            @"WindowsPowerShell\v1.0\powershell.exe"))
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true
        };
        foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-Command",
                     "[Console]::WriteLine('ready'); [Console]::ReadLine() | Out-Null" })
            start.ArgumentList.Add(argument);
        var child = Process.Start(start)!;
        try
        {
            Assert.That(child.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult(), Is.EqualTo("ready"));
            return child;
        }
        catch { Finish(child); child.Dispose(); throw; }
    }

    private static void Finish(Process child)
    {
        if (child.HasExited) return;
        child.StandardInput.WriteLine("exit");
        if (!child.WaitForExit(5000)) child.Kill(); // Only the child created by this test.
    }
}
