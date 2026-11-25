using MonCore.Monitoring;
using NUnit.Framework;
using WinMon.Updates;

namespace WinMon.Tests;

[TestFixture]
[Category("Integration")]
public sealed class UpdateRestartIdentityTests
{
    private string _root = null!;
    private string _staging = null!;
    private string _target = null!;
    private const string Token = "abcdef1234567890abcdef1234567890";

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "WinMon.RestartIdentityTests", Guid.NewGuid().ToString("N"));
        _staging = Path.Combine(_root, "staging");
        Directory.CreateDirectory(_staging);
        _target = Path.Combine(_root, "WinMon.exe");
        File.WriteAllText(_target, "owned application");
    }

    [TearDown]
    public void TearDown()
    {
        var resolved = Path.GetFullPath(_root);
        var parent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "WinMon.RestartIdentityTests"));
        if (!string.Equals(Path.GetDirectoryName(resolved), parent, StringComparison.OrdinalIgnoreCase) ||
            !Guid.TryParseExact(Path.GetFileName(resolved), "N", out _))
            throw new InvalidOperationException("Refusing cleanup outside the owned restart fixture.");
        if (Directory.Exists(resolved)) Directory.Delete(resolved, true);
    }

    [TestCase("null")]
    [TestCase("empty")]
    [TestCase("one")]
    [TestCase("two")]
    [TestCase("four")]
    [TestCase("wrong-flag")]
    [TestCase("case-flag")]
    public void UnsupportedArgumentShapesLeaveIdentityAndDependenciesUntouched(string shape)
    {
        var runtime = new RuntimeProbe();
        string[]? args = shape switch
        {
            "null" => null,
            "empty" => [],
            "one" => ["--update-result"],
            "two" => ["--update-result", "missing"],
            "four" => ["--update-result", "missing", Token, "extra"],
            "wrong-flag" => ["--other", "missing", Token],
            "case-flag" => ["--UPDATE-RESULT", "missing", Token],
            _ => throw new ArgumentOutOfRangeException(nameof(shape))
        };
        var lookups = 0;
        var opens = 0;

        runtime.Accept(args!, _staging, () => { lookups++; return _target; }, _ => { opens++; return null; });

        Assert.Multiple(() =>
        {
            Assert.That(runtime.Expected, Is.False);
            Assert.That(runtime.History, Is.Null);
            Assert.That(lookups, Is.Zero);
            Assert.That(opens, Is.Zero);
        });
    }

    [TestCase("outside-root")]
    [TestCase("nonguid-directory")]
    [TestCase("missing-directory")]
    [TestCase("null-directory")]
    [TestCase("missing-job")]
    [TestCase("invalid-job")]
    [TestCase("null-job")]
    [TestCase("token-mismatch")]
    [TestCase("token-case-mismatch")]
    [TestCase("target-mismatch")]
    [TestCase("null-target")]
    public void UntrustedOrUnreadableJobsCannotSetRestartIdentity(string kind)
    {
        var runtime = new RuntimeProbe();
        var directory = Job();
        var token = Token;
        switch (kind)
        {
            case "outside-root": directory = Path.Combine(_root, Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory); break;
            case "nonguid-directory": directory = Path.Combine(_staging, "not-a-guid"); Directory.CreateDirectory(directory); break;
            case "missing-directory": Directory.Delete(directory, true); break;
            case "null-directory": directory = null!; break;
            case "missing-job": File.Delete(Path.Combine(directory, "job.json")); break;
            case "invalid-job": File.WriteAllText(Path.Combine(directory, "job.json"), "{"); break;
            case "null-job": File.WriteAllText(Path.Combine(directory, "job.json"), "null"); break;
            case "token-mismatch": token = "different"; break;
            case "token-case-mismatch": token = Token.ToUpperInvariant(); break;
            case "target-mismatch": SaveJob(directory, Token, Path.Combine(_root, "other.exe")); break;
            case "null-target": SaveJob(directory, Token, null!); break;
        }
        var opens = 0;
        var lookups = 0;

        runtime.Accept(["--update-result", directory, token], _staging,
            () => { lookups++; return _target; }, _ => { opens++; return new EventWaitHandle(false, EventResetMode.ManualReset); });

        Assert.Multiple(() =>
        {
            Assert.That(runtime.Expected, Is.False);
            Assert.That(runtime.History, Is.Null);
            Assert.That(opens, Is.Zero);
            if (kind is "token-mismatch" or "token-case-mismatch") Assert.That(lookups, Is.Zero);
            Assert.That(File.ReadAllText(_target), Is.EqualTo("owned application"));
        });
    }

    [TestCase("missing")]
    [TestCase("io-error")]
    [TestCase("access-denied")]
    public void MissingOrFailingHealthySignalDoesNotAuthenticateARestart(string outcome)
    {
        var runtime = new RuntimeProbe();
        var directory = Job();
        var names = new List<string>();
        EventWaitHandle? Open(string name)
        {
            names.Add(name);
            return outcome switch
            {
                "missing" => null,
                "io-error" => throw new IOException("controlled event failure"),
                "access-denied" => throw new UnauthorizedAccessException("controlled event access"),
                _ => throw new ArgumentOutOfRangeException(nameof(outcome))
            };
        }

        runtime.Accept(["--update-result", directory, Token], _staging, () => _target, Open);

        Assert.Multiple(() =>
        {
            Assert.That(runtime.Expected, Is.False);
            Assert.That(runtime.History, Is.Null);
            Assert.That(names, Is.EqualTo(Enabled ? new[] { @"Local\WinMon.Update." + Token + ".Healthy" } : []));
        });
    }

    [TestCase("direct")]
    [TestCase("segments")]
    [TestCase("target-case")]
    public void SuccessfulIdentityIsCanonicalAndCommittedAfterHealthyHandleDisposal(string shape)
    {
        var runtime = new RuntimeProbe();
        var directory = Job();
        if (shape == "target-case") SaveJob(directory, Token, _target.ToUpperInvariant());
        var argument = shape == "segments" ? Path.Combine(directory, "..", Path.GetFileName(directory)) : directory;
        var original = File.ReadAllBytes(Path.Combine(directory, "job.json"));
        bool? expectedAtDispose = null;
        using var healthy = new TrackingSignal(() => expectedAtDispose = runtime.Expected);
        var handle = healthy.SafeWaitHandle;
        var names = new List<string>();

        runtime.Accept(["--update-result", argument, Token], _staging, () => _target,
            name => { names.Add(name); return healthy; });

        Assert.Multiple(() =>
        {
            Assert.That(runtime.Expected, Is.EqualTo(Enabled));
            Assert.That(runtime.History, Is.EqualTo(Enabled ? (Path.Combine(Path.GetFullPath(directory), RestartHistory.FileName), Token) : ((string, string)?)null));
            Assert.That(handle.IsClosed, Is.EqualTo(Enabled));
            Assert.That(expectedAtDispose, Is.EqualTo(Enabled ? false : (bool?)null));
            Assert.That(names, Is.EqualTo(Enabled ? new[] { @"Local\WinMon.Update." + Token + ".Healthy" } : []));
            Assert.That(File.ReadAllBytes(Path.Combine(directory, "job.json")), Is.EqualTo(original));
        });
    }

    [TestCase("bad-arguments")]
    [TestCase("token-mismatch")]
    [TestCase("missing-event")]
    [TestCase("lookup-failure")]
    public void RejectedLaterHandoffRetainsThePreviouslyAcceptedIdentity(string rejection)
    {
        var runtime = new RuntimeProbe();
        var first = Job();
        using var firstSignal = new EventWaitHandle(false, EventResetMode.ManualReset);
        runtime.Accept(["--update-result", first, Token], _staging, () => _target, _ => firstSignal);
        var accepted = runtime.History;
        var next = Job("next-token");
        var args = rejection == "bad-arguments" ? Array.Empty<string>() : new[] { "--update-result", next, rejection == "token-mismatch" ? "wrong-token" : "next-token" };

        runtime.Accept(args, _staging,
            () => rejection == "lookup-failure" ? throw new IOException("controlled lookup failure") : _target, _ => null);

        Assert.Multiple(() =>
        {
            Assert.That(runtime.Expected, Is.EqualTo(Enabled));
            Assert.That(runtime.History, Is.EqualTo(accepted));
        });
        using var nextSignal = new EventWaitHandle(false, EventResetMode.ManualReset);
        runtime.Accept(["--update-result", next, "next-token"], _staging, () => _target, _ => nextSignal);
        Assert.That(runtime.History, Is.EqualTo(Enabled ? (Path.Combine(next, RestartHistory.FileName), "next-token") : ((string, string)?)null));
    }

    [TestCase("not-a-guid")]
    [TestCase("")]
    [TestCase(null)]
    public void MatchingTokenFormatsRetainTheirExistingIdentityBehavior(string? token)
    {
        var runtime = new RuntimeProbe();
        var directory = Job(token!);
        using var healthy = new EventWaitHandle(false, EventResetMode.ManualReset);
        var handle = healthy.SafeWaitHandle;
        var names = new List<string>();

        runtime.Accept(["--update-result", directory, token!], _staging, () => _target,
            name => { names.Add(name); return healthy; });

        Assert.Multiple(() =>
        {
            Assert.That(runtime.Expected, Is.EqualTo(Enabled && token is not null));
            Assert.That(runtime.History, Is.EqualTo(Enabled && token is not null ? (Path.Combine(directory, RestartHistory.FileName), token) : ((string, string)?)null));
            Assert.That(handle.IsClosed, Is.EqualTo(Enabled));
            Assert.That(names, Is.EqualTo(Enabled ? new[] { @"Local\WinMon.Update." + token + ".Healthy" } : []));
        });
    }

    [Test]
    public void MatchingNullTokenRetainsItsExistingIdentityClearingBehavior()
    {
        var runtime = new RuntimeProbe();
        var first = Job();
        using var firstSignal = new EventWaitHandle(false, EventResetMode.ManualReset);
        runtime.Accept(["--update-result", first, Token], _staging, () => _target, _ => firstSignal);
        Assert.That(runtime.Expected, Is.EqualTo(Enabled));
        var next = Job(null!);
        using var nextSignal = new EventWaitHandle(false, EventResetMode.ManualReset);
        var handle = nextSignal.SafeWaitHandle;
        var names = new List<string>();

        runtime.Accept(["--update-result", next, null!], _staging, () => _target,
            name => { names.Add(name); return nextSignal; });

        Assert.Multiple(() =>
        {
            Assert.That(runtime.Expected, Is.False);
            Assert.That(runtime.History, Is.Null);
            Assert.That(handle.IsClosed, Is.EqualTo(Enabled));
            Assert.That(names, Is.EqualTo(Enabled ? new[] { @"Local\WinMon.Update..Healthy" } : []));
        });
    }

    [Test]
    public void StorePolicyPrecedesAllRestartDependencies()
    {
        var runtime = new RuntimeProbe();
        var lookups = 0;
        var opens = 0;
        runtime.Accept(["--update-result", "invalid-directory", Token], _staging,
            () => { lookups++; throw new IOException("unexpected lookup"); }, _ => { opens++; throw new IOException("unexpected event"); });
        Assert.Multiple(() =>
        {
            Assert.That(runtime.Expected, Is.False);
            Assert.That(runtime.History, Is.Null);
            Assert.That(lookups, Is.Zero);
            Assert.That(opens, Is.Zero);
        });
    }

    private string Job(string token = Token)
    {
        var directory = Path.Combine(_staging, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        SaveJob(directory, token, _target);
        return directory;
    }

    private static void SaveJob(string directory, string token, string target) =>
        UpdateFiles.WriteJson(Path.Combine(directory, "job.json"), new UpdateJob(target, 1, 1, "4.2.0", false, token));
    private static bool Enabled => UpdatePolicy.GitHubUpdatesEnabled;

    private sealed class TrackingSignal(Action beforeDispose) : EventWaitHandle(false, EventResetMode.ManualReset)
    {
        private bool _observed;
        protected override void Dispose(bool explicitDisposing)
        {
            if (explicitDisposing && !_observed) { _observed = true; beforeDispose(); }
            base.Dispose(explicitDisposing);
        }
    }

    private sealed class RuntimeProbe
    {
        private string? _token;
        private string? _directory;
        internal bool Expected => UpdateRestartIdentity.IsExpectedRestart(_token);
        internal (string Path, string Token)? History => UpdateRestartIdentity.RestartHistory(_token, _directory);
        internal void Accept(string[] args, string root, Func<string> processPath, Func<string, EventWaitHandle?> openHealthy) =>
            UpdateRuntime.AcceptResultArguments(args, ref _token, ref _directory, root, processPath, openHealthy);
    }
}
