using System.Text.Json;
using NUnit.Framework;
using WinMon.Updates;

namespace WinMon.Tests;

[TestFixture, NonParallelizable]
public sealed class UpdateLauncherCharacterizationTests
{
    [TestCase(true), TestCase(false)]
    public void UpdateLauncherRejectsNullOrForeignTransactionsBeforeCreatingSignals(bool nullUpdate)
    {
        var update = nullUpdate ? null! : new PreparedUpdate(Path.Combine(Path.GetTempPath(), "WinMon.Foreign." + Guid.NewGuid().ToString("N")));
#if WINMON_STORE
        AssertStoreRejection(update);
#else
        if (nullUpdate)
            Assert.ThrowsAsync<NullReferenceException>(() => UpdateLauncher.StartAsync(update, CancellationToken.None));
        else
            Assert.ThrowsAsync<InvalidDataException>(() => UpdateLauncher.StartAsync(update, CancellationToken.None));
#endif
    }

    [TestCase(false), TestCase(true)]
    public void UpdateLauncherPreservesMissingOrMalformedJobFailureBeforeHelperCopy(bool malformed)
    {
#if WINMON_STORE
        AssertStoreRejection(new PreparedUpdate("not a transaction"));
#else
        using var transaction = new OwnedTransaction();
        if (malformed) File.WriteAllText(transaction.JobPath, "{");
        if (malformed)
            Assert.ThrowsAsync<JsonException>(() => UpdateLauncher.StartAsync(transaction.Update, CancellationToken.None));
        else
            Assert.ThrowsAsync<FileNotFoundException>(() => UpdateLauncher.StartAsync(transaction.Update, CancellationToken.None));
        Assert.That(File.Exists(transaction.Helper), Is.False);
        Assert.That(Directory.Exists(transaction.Helper), Is.False);
        if (malformed) Assert.That(File.ReadAllText(transaction.JobPath), Is.EqualTo("{"));
#endif
    }

    [TestCase(false, false), TestCase(false, true), TestCase(true, false), TestCase(true, true)]
    public void UpdateLauncherCopyFailureSignalsCancelAndPreservesExistingOutputBeforeCancellation(bool directory, bool canceled)
    {
#if WINMON_STORE
        using var stop = new CancellationTokenSource();
        if (canceled) stop.Cancel();
        AssertStoreRejection(new PreparedUpdate("not a transaction"), stop.Token);
#else
        using var transaction = new OwnedTransaction();
        transaction.WriteJob();
        if (directory) Directory.CreateDirectory(transaction.Helper);
        else File.WriteAllText(transaction.Helper, "owned helper sentinel");
        using var ready = new EventWaitHandle(false, EventResetMode.ManualReset, UpdateLauncher.EventName(transaction.Token, "Ready"));
        using var commit = new EventWaitHandle(false, EventResetMode.ManualReset, UpdateLauncher.EventName(transaction.Token, "Commit"));
        using var cancel = new EventWaitHandle(false, EventResetMode.ManualReset, UpdateLauncher.EventName(transaction.Token, "Cancel"));
        using var stop = new CancellationTokenSource();
        if (canceled) stop.Cancel();
        try
        {
            Assert.ThrowsAsync<IOException>(() => UpdateLauncher.StartAsync(transaction.Update, stop.Token));
            Assert.Multiple(() =>
            {
                Assert.That(ready.WaitOne(0), Is.False);
                Assert.That(commit.WaitOne(0), Is.False);
                Assert.That(cancel.WaitOne(0), Is.True, "Copy failure signals the helper cancellation event.");
                Assert.That(File.ReadAllText(transaction.Target), Is.EqualTo("owned application sentinel"));
                Assert.That(File.ReadAllText(transaction.JobPath), Is.EqualTo(transaction.JobContents));
                if (directory) Assert.That(Directory.GetFileSystemEntries(transaction.Helper), Is.Empty);
                else Assert.That(File.ReadAllText(transaction.Helper), Is.EqualTo("owned helper sentinel"));
            });
            cancel.Reset();
            UpdateLauncher.Cancel();
            UpdateLauncher.Commit();
            Assert.That(cancel.WaitOne(0), Is.False, "Failed launch released its static cancel handle.");
            Assert.That(commit.WaitOne(0), Is.False, "Failed launch released its static commit handle.");
        }
        finally { UpdateLauncher.Dispose(); }
#endif
    }

#if WINMON_STORE
    private static void AssertStoreRejection(PreparedUpdate update, CancellationToken token = default)
    {
        var exception = Assert.ThrowsAsync<InvalidOperationException>(() => UpdateLauncher.StartAsync(update, token));
        Assert.That(exception!.Message, Is.EqualTo("Updates for this build are managed by Microsoft Store."));
    }
#else
    // ValidateDirectory intentionally accepts only direct GUID children of the real staging root.
    // Own just this new child: never enumerate, delete or alter another transaction or its parent.
    private sealed class OwnedTransaction : IDisposable
    {
        internal string Token { get; } = Guid.NewGuid().ToString("N");
        internal string DirectoryPath { get; }
        internal string JobPath => Path.Combine(DirectoryPath, "job.json");
        internal string Helper => Path.Combine(DirectoryPath, "WinMon.Update.exe");
        internal string Target => Path.Combine(DirectoryPath, "owned-application.exe");
        internal PreparedUpdate Update => new(DirectoryPath);
        internal string? JobContents { get; private set; }

        internal OwnedTransaction()
        {
            DirectoryPath = Path.Combine(UpdateClient.UpdateRoot, Token);
            Assert.That(Directory.Exists(DirectoryPath), Is.False);
            Directory.CreateDirectory(DirectoryPath);
        }

        internal void WriteJob()
        {
            File.WriteAllText(Target, "owned application sentinel");
            JobContents = JsonSerializer.Serialize(new UpdateJob(Target, 0, 0, "0.0.0", false, Token));
            File.WriteAllText(JobPath, JobContents);
        }

        public void Dispose()
        {
            Assert.That(Path.GetDirectoryName(Path.GetFullPath(DirectoryPath)), Is.EqualTo(Path.GetFullPath(UpdateClient.UpdateRoot)).IgnoreCase);
            Assert.That(Path.GetFileName(DirectoryPath), Is.EqualTo(Token));
            Assert.That(Guid.TryParseExact(Token, "N", out _), Is.True);
            Directory.Delete(DirectoryPath, true);
        }
    }
#endif
}
