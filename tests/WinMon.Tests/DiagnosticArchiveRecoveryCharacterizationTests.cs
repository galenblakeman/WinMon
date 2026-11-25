using System.Reflection;
using System.Text;
using System.Windows;
using NUnit.Framework;
using WinMon.Diagnostics;
using WinMon.Monitoring;
using WinMon.Preferences;

namespace WinMon.Tests;

public sealed partial class MainWindowWorkflowCharacterizationTests
{
    private static readonly Func<string, CancellationToken, Task> RecoverArchiveDiagnostic = typeof(WindowDiagnostics)
        .GetMethod("ValidateArchiveRecoveryAsync", BindingFlags.Static | BindingFlags.NonPublic)!
        .CreateDelegate<Func<string, CancellationToken, Task>>();
    private const string InvalidDiagnosticArchive = "{\"SchemaVersion\":2,\"TimeZoneId\":\"UTC\",\"Days\":[]}";
    private const string ArchiveRecoverySuccess = "Invalid and oversized archives allowed recording; rejected loads and writes preserved existing files.";

    [Test, Category("Integration")]
    public void ArchiveRecoveryRepeatsPreserveRejectedInputsAndReleaseAllFixtureLocks()
    {
        using var ui = new UpdateUi(_directory, true);
        var output = Path.Combine(_directory, "archive-diagnostic");
        for (var attempt = 0; attempt < 2; attempt++)
        {
            InvokeArchiveRecovery(output);
            AssertArchiveRecoveryOutputs(output);
            Assert.That(File.ReadAllText(Path.Combine(output, "archive-recovery-passed.txt")), Is.EqualTo(ArchiveRecoverySuccess));
            Assert.That(Directory.GetFiles(output, "*.tmp", SearchOption.AllDirectories), Is.Empty);
        }
    }

    [Test, Category("Integration")]
    public void ArchiveRecoveryPreCancellationPreservesExistingBytesAndDoesNotCreateLaterFixtures()
    {
        using var ui = new UpdateUi(_directory, true);
        var output = Path.Combine(_directory, "cancelled-archive-diagnostic");
        var store = ArchiveDiagnosticStore(output, "invalid-archive");
        Directory.CreateDirectory(Path.GetDirectoryName(store.UsageArchivePath)!);
        File.WriteAllText(store.UsageArchivePath, "owned archive before cancellation");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var error = Assert.Catch<OperationCanceledException>(() => InvokeArchiveRecovery(output, cancellation.Token));

        Assert.That(error!.CancellationToken, Is.EqualTo(cancellation.Token));
        Assert.That(File.ReadAllText(store.UsageArchivePath), Is.EqualTo("owned archive before cancellation"));
        Assert.That(File.Exists(store.UsageArchivePath + ".lock"), Is.False);
        Assert.That(Directory.Exists(Path.Combine(output, "oversized-archive")), Is.False);
        Assert.That(Directory.Exists(Path.Combine(output, "rejected-write")), Is.False);
        Assert.That(File.Exists(Path.Combine(output, "archive-recovery-passed.txt")), Is.False);
    }

    [TestCase("first-folder"), TestCase("first-file-locked"), TestCase("second-folder"), TestCase("final-marker")]
    [Category("Integration")]
    public void ArchiveRecoveryIoFailuresPreserveOwnedDataAndCloseEveryCreatedFixture(string failure)
    {
        using var ui = new UpdateUi(_directory, true);
        var output = Path.Combine(_directory, "failed-archive-diagnostic");
        Directory.CreateDirectory(output);
        var initialStore = ArchiveDiagnosticStore(output, "invalid-archive");
        var blocked = failure switch
        {
            "first-folder" => Path.Combine(output, "invalid-archive"),
            "second-folder" => Path.Combine(output, "oversized-archive"),
            "first-file-locked" => initialStore.UsageArchivePath,
            "final-marker" => Path.Combine(output, "archive-recovery-passed.txt"),
            _ => throw new AssertionException("Unknown failure partition.")
        };
        if (failure == "final-marker") Directory.CreateDirectory(blocked);
        else
        {
            Directory.CreateDirectory(Path.GetDirectoryName(blocked)!);
            File.WriteAllText(blocked, "owned blocking data");
        }
        using var locked = failure == "first-file-locked" ? new FileStream(blocked, FileMode.Open,
            FileAccess.Read, FileShare.Read) : null;

        var error = Assert.Catch<Exception>(() => InvokeArchiveRecovery(output));

        Assert.That(error, failure == "final-marker" ? Is.TypeOf<UnauthorizedAccessException>() : Is.TypeOf<IOException>());
        if (failure != "final-marker") Assert.That(File.ReadAllText(blocked), Is.EqualTo("owned blocking data"));
        if (failure is "first-folder" or "first-file-locked")
        {
            Assert.That(Directory.Exists(Path.Combine(output, "oversized-archive")), Is.False);
            Assert.That(File.Exists(initialStore.UsageArchivePath + ".lock"), Is.False);
        }
        if (failure == "second-folder") AssertRejectedDiagnosticArchive(output, false);
        if (failure == "final-marker")
        {
            AssertArchiveRecoveryOutputs(output);
            Assert.That(Directory.Exists(blocked), Is.True);
            Assert.That(Directory.GetFileSystemEntries(blocked), Is.Empty);
        }
        else
        {
            Assert.That(Directory.Exists(Path.Combine(output, "rejected-write")), Is.False);
            Assert.That(File.Exists(Path.Combine(output, "archive-recovery-passed.txt")), Is.False);
        }
        Assert.That(Directory.GetFiles(output, "*.tmp", SearchOption.AllDirectories), Is.Empty);
    }

    [Test]
    public void ArchiveRecoveryNullOutputRetainsArgumentFailureBeforeCancellation()
    {
        using var ui = new UpdateUi(_directory, true);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var error = Assert.Throws<ArgumentNullException>(() => InvokeArchiveRecovery(null!, cancellation.Token));
        Assert.That(error!.ParamName, Is.EqualTo("path1"));
    }

    private static PreferenceStore ArchiveDiagnosticStore(string output, string folder) =>
        new(Path.Combine(output, folder, "settings.json"));

    private static void InvokeArchiveRecovery(string output, CancellationToken token = default)
    {
        var before = Application.Current.Windows.Cast<Window>().ToArray();
        try { FinishWindowCheck(RecoverArchiveDiagnostic(output, token)); }
        finally
        {
            var remaining = Application.Current.Windows.Cast<Window>().Except(before).ToArray();
            foreach (var window in remaining.OfType<MainWindow>()) window.CloseForFixture();
            Assert.That(remaining, Is.Empty, "The diagnostic must close every fixture on success and failure.");
        }
    }

    private static void AssertArchiveRecoveryOutputs(string output)
    {
        AssertRejectedDiagnosticArchive(output, false);
        AssertRejectedDiagnosticArchive(output, true);
        var store = ArchiveDiagnosticStore(output, "rejected-write");
        var load = UsageArchive.LoadAsync(store.UsageArchivePath);
        FinishWindowCheck(load);
        var archive = load.GetAwaiter().GetResult();
        Assert.That(archive.Export().Days, Is.Empty, "The rejected infinite GPU total must not replace the saved empty archive.");
        using var archiveFile = File.Open(store.UsageArchivePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        using var writerLock = File.Open(store.UsageArchivePath + ".lock", FileMode.Open, FileAccess.ReadWrite, FileShare.None);
    }

    private static void AssertRejectedDiagnosticArchive(string output, bool oversized)
    {
        var store = ArchiveDiagnosticStore(output, oversized ? "oversized-archive" : "invalid-archive");
        var bytes = File.ReadAllBytes(store.UsageArchivePath);
        if (oversized)
        {
            Assert.That(bytes.LongLength, Is.EqualTo(UsageArchive.MaximumFileBytes + 1));
            Assert.That(bytes.All(value => value == 0), Is.True);
        }
        else Assert.That(bytes, Is.EqualTo(Encoding.UTF8.GetBytes(InvalidDiagnosticArchive)));
        using var archiveFile = File.Open(store.UsageArchivePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        using var writerLock = File.Open(store.UsageArchivePath + ".lock", FileMode.Open, FileAccess.ReadWrite, FileShare.None);
    }
}
