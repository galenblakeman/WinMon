using NUnit.Framework;
using WinMon.Resources;
using WinMon.Updates;

namespace WinMon.Tests;

[TestFixture]
[Category("Integration")]
public sealed class UpdateNoticeTests
{
    private string _root = null!;
    private string _target = null!;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "WinMon.NoticeTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_root, "app"));
        _target = Path.Combine(_root, "app", "WinMon.exe");
        File.WriteAllText(_target, "owned application");
    }

    [TearDown]
    public void TearDown()
    {
        var parent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "WinMon.NoticeTests"));
        var resolved = Path.GetFullPath(_root);
        if (!string.Equals(Path.GetDirectoryName(resolved), parent, StringComparison.OrdinalIgnoreCase) ||
            !Guid.TryParseExact(Path.GetFileName(resolved), "N", out _))
            throw new InvalidOperationException("Refusing cleanup outside the owned notice fixture.");
        if (Directory.Exists(resolved)) Directory.Delete(resolved, true);
    }

    [TestCase("succeeded")]
    [TestCase("rolled-back")]
    [TestCase("failed")]
    public void CompletedStatesProduceTheExistingLocalizedNoticeAndAreMarkedOnce(string status)
    {
        var directory = Job(status);
        var job = File.ReadAllBytes(Path.Combine(directory, "job.json"));
        var state = File.ReadAllBytes(Path.Combine(directory, "state.json"));
        var calls = 0;

        var text = Invoke(() => { calls++; return [directory]; }, () => _target);

        Assert.Multiple(() =>
        {
            Assert.That(text, Is.EqualTo(Enabled ? Text(status) : null));
            Assert.That(calls, Is.EqualTo(Enabled ? 1 : 0));
            Assert.That(File.Exists(Marker(directory)), Is.EqualTo(Enabled));
            if (Enabled) Assert.That(File.ReadAllBytes(Marker(directory)), Is.Empty);
            Assert.That(File.ReadAllBytes(Path.Combine(directory, "job.json")), Is.EqualTo(job));
            Assert.That(File.ReadAllBytes(Path.Combine(directory, "state.json")), Is.EqualTo(state));
            Assert.That(File.ReadAllText(_target), Is.EqualTo("owned application"));
        });
        Assert.That(Invoke(() => [directory], () => _target), Is.Null);
    }

    [TestCase("replacing")]
    [TestCase("awaiting-ready")]
    [TestCase("installer-running")]
    [TestCase("")]
    [TestCase(null)]
    [TestCase("SUCCEEDED")]
    [TestCase("unknown")]
    public void PendingAndUnrecognizedStatesRemainUnmarked(string? status)
    {
        var directory = Job(status!);
        var state = File.ReadAllBytes(Path.Combine(directory, "state.json"));

        Assert.That(Invoke(() => [directory], () => _target), Is.Null);
        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(Marker(directory)), Is.False);
            Assert.That(File.ReadAllBytes(Path.Combine(directory, "state.json")), Is.EqualTo(state));
        });
    }

    [TestCase("missing-job")]
    [TestCase("invalid-job")]
    [TestCase("null-job")]
    [TestCase("null-target")]
    [TestCase("other-target")]
    [TestCase("missing-state")]
    [TestCase("null-state")]
    [TestCase("invalid-state")]
    [TestCase("already-shown")]
    [TestCase("locked-state")]
    [TestCase("marker-directory")]
    public void IneligibleOrBrokenTransactionsDoNotConsumeALaterNotice(string kind)
    {
        var first = Job("succeeded");
        var next = Job("failed");
        var jobPath = Path.Combine(first, "job.json");
        var statePath = Path.Combine(first, "state.json");
        FileStream? stateLock = null;
        try
        {
            switch (kind)
            {
                case "missing-job": File.Delete(jobPath); break;
                case "invalid-job": File.WriteAllText(jobPath, "{"); break;
                case "null-job": File.WriteAllText(jobPath, "null"); break;
                case "null-target": UpdateFiles.WriteJson(jobPath, NewJob(null!)); break;
                case "other-target": UpdateFiles.WriteJson(jobPath, NewJob(Path.Combine(_root, "other.exe"))); break;
                case "missing-state": File.Delete(statePath); break;
                case "null-state": File.WriteAllText(statePath, "null"); break;
                case "invalid-state": File.WriteAllText(statePath, "{"); break;
                case "already-shown": File.WriteAllText(Marker(first), "existing marker"); break;
                case "locked-state": stateLock = new FileStream(statePath, FileMode.Open, FileAccess.Read, FileShare.None); break;
                case "marker-directory": Directory.CreateDirectory(Marker(first)); break;
            }

            Assert.That(Invoke(() => [first, next], () => _target), Is.EqualTo(Enabled ? Text("failed") : null));
            Assert.Multiple(() =>
            {
                Assert.That(File.Exists(Marker(next)), Is.EqualTo(Enabled));
                if (kind == "already-shown") Assert.That(File.ReadAllText(Marker(first)), Is.EqualTo("existing marker"));
                else Assert.That(File.Exists(Marker(first)), Is.False);
                if (kind == "marker-directory") Assert.That(Directory.Exists(Marker(first)), Is.True);
                Assert.That(File.ReadAllText(_target), Is.EqualTo("owned application"));
            });
        }
        finally { stateLock?.Dispose(); }
    }

    [Test]
    public void OnlyTheFirstEligibleNoticeIsMarkedAndItsEnumeratorIsDisposed()
    {
        var first = Job("rolled-back");
        var next = Job("succeeded");
        var disposed = false;
        var lookups = 0;
        IEnumerable<string> Directories()
        {
            try
            {
                yield return first;
                yield return next;
                throw new AssertionException("Do not enumerate past the first eligible notice.");
            }
            finally { disposed = true; }
        }

        Assert.That(Invoke(Directories, () => { lookups++; return _target; }), Is.EqualTo(Enabled ? Text("rolled-back") : null));
        Assert.Multiple(() =>
        {
            Assert.That(disposed, Is.EqualTo(Enabled));
            Assert.That(lookups, Is.EqualTo(Enabled ? 1 : 0));
            Assert.That(File.Exists(Marker(first)), Is.EqualTo(Enabled));
            Assert.That(File.Exists(Marker(next)), Is.False);
        });
    }

    [Test]
    public void EmptyTransactionsNeverReadTheProcessPath()
    {
        Assert.That(Invoke(() => [], () => throw new AssertionException("No path lookup for an empty sequence.")), Is.Null);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void EnumerationFailuresRetainTheirExistingPropagation(bool duringEnumeration)
    {
        var foreign = Job("failed", Path.Combine(_root, "foreign.exe"));
        IEnumerable<string> BrokenSequence()
        {
            yield return foreign;
            throw new InvalidOperationException("controlled enumeration failure");
        }
        IEnumerable<string> Directories() => duringEnumeration ? BrokenSequence() : throw new InvalidOperationException("controlled enumeration failure");

        if (Enabled)
            Assert.That(() => Invoke(Directories, () => _target),
                Throws.TypeOf<InvalidOperationException>().With.Message.EqualTo("controlled enumeration failure"));
        else Assert.That(Invoke(Directories, () => _target), Is.Null);
        Assert.That(File.Exists(Marker(foreign)), Is.False);
    }

    [Test]
    public void ProcessPathLookupFailureSkipsOnlyItsTransaction()
    {
        var first = Job("succeeded");
        var next = Job("failed");
        var lookups = 0;
        string ProcessPath()
        {
            if (++lookups == 1) throw new IOException("controlled path lookup failure");
            return _target;
        }

        Assert.That(Invoke(() => [first, next], ProcessPath), Is.EqualTo(Enabled ? Text("failed") : null));
        Assert.Multiple(() =>
        {
            Assert.That(lookups, Is.EqualTo(Enabled ? 2 : 0));
            Assert.That(File.Exists(Marker(first)), Is.False);
            Assert.That(File.Exists(Marker(next)), Is.EqualTo(Enabled));
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void TargetComparisonKeepsWindowsPathNormalization(bool upperCase)
    {
        var target = upperCase ? _target.ToUpperInvariant() : Path.Combine(_root, "app", "..", "app", "WinMon.exe");
        var directory = Job("succeeded", target);

        Assert.That(Invoke(() => [directory], () => _target), Is.EqualTo(Enabled ? Text("succeeded") : null));
        Assert.That(File.Exists(Marker(directory)), Is.EqualTo(Enabled));
    }

    [Test]
    public void StorePolicyPrecedesBothDependencyCallbacks()
    {
        IEnumerable<string> Directories() => throw new InvalidOperationException("enumeration was invoked");
        string ProcessPath() => throw new AssertionException("path lookup must not run");

        if (Enabled)
            Assert.That(() => Invoke(Directories, ProcessPath),
                Throws.TypeOf<InvalidOperationException>().With.Message.EqualTo("enumeration was invoked"));
        else Assert.That(Invoke(Directories, ProcessPath), Is.Null);
    }

    private string Job(string status, string? target = null)
    {
        var directory = Path.Combine(_root, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        UpdateFiles.WriteJson(Path.Combine(directory, "job.json"), NewJob(target ?? _target));
        UpdateFiles.WriteJson(Path.Combine(directory, "state.json"), new UpdateState(status));
        return directory;
    }

    private static UpdateJob NewJob(string target) => new(target, 1, 1, "4.2.0", false, Guid.NewGuid().ToString("N"));
    private static string Marker(string directory) => Path.Combine(directory, "notice-shown");
    private static bool Enabled => UpdatePolicy.GitHubUpdatesEnabled;
    private static string Text(string status) => status switch
    {
        "succeeded" => Strings.Format(Strings.UpdateSucceeded, "4.2.0"),
        "rolled-back" => Strings.UpdateRolledBack,
        "failed" => Strings.UpdateFailed,
        _ => throw new ArgumentOutOfRangeException(nameof(status))
    };

    private static string? Invoke(Func<IEnumerable<string>> directories, Func<string> processPath) =>
        UpdateRuntime.ConsumeNotice(directories, processPath);
}
