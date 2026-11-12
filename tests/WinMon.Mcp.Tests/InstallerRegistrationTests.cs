using System.Diagnostics;
using NUnit.Framework;
using WinMon.Mcp;

namespace WinMon.Tests;

[TestFixture]
[NonParallelizable]
public sealed class InstallerRegistrationTests
{
    private string _directory = null!;
    private string _target = null!;
    private string _recordPath = null!;

    [SetUp]
    public void SetUp()
    {
        _directory = Path.Combine(Path.GetTempPath(), "WinMon-McpRegistration-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        _target = Path.Combine(_directory, "WinMon.Mcp.exe");
        File.WriteAllText(_target, "test");
        _recordPath = Path.Combine(_directory, "records.json");
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }

    [Test]
    public void RegisterAddsAbsentCodexEntryAndRecordsOnlyTheInstalledPath()
    {
        var calls = new List<IReadOnlyList<string>>();
        var registration = new InstallerRegistration((_, arguments) =>
        {
            calls.Add(arguments);
            return arguments[1] != "get" ? new ProcessResult(0, "added") : calls.Count == 1
                ? new ProcessResult(1, "Error: No MCP server named 'winmon' found.")
                : new ProcessResult(0, CodexEntry(_target));
        }, _recordPath);

        var result = registration.Register("codex", _target, TextWriter.Null);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(InstallerRegistrationExitCode.Success));
            Assert.That(calls, Has.Count.EqualTo(3));
            Assert.That(calls[1], Is.EqualTo(new[] { "mcp", "add", "winmon", "--", _target }));
            Assert.That(File.ReadAllText(_recordPath), Does.Contain(_target.Replace("\\", "\\\\")));
        });
    }

    [Test]
    public void RegisterPreservesExistingEntry()
    {
        var calls = 0;
        var registration = new InstallerRegistration((_, _) =>
        {
            calls++;
            return new ProcessResult(0, CodexEntry(Path.Combine(_directory, "other.exe")));
        }, _recordPath);

        var result = registration.Register("codex", _target, TextWriter.Null);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(InstallerRegistrationExitCode.Conflict));
            Assert.That(calls, Is.EqualTo(1));
            Assert.That(File.Exists(_recordPath), Is.False);
        });
    }

    [Test]
    public void RegisterLeavesAnEquivalentManualEntryAlone()
    {
        var calls = 0;
        var registration = new InstallerRegistration((_, _) =>
        {
            calls++;
            return new ProcessResult(0, CodexEntry(_target));
        }, _recordPath);

        var result = registration.Register("codex", _target, TextWriter.Null);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(InstallerRegistrationExitCode.Success));
            Assert.That(calls, Is.EqualTo(1));
            Assert.That(File.Exists(_recordPath), Is.False);
        });
    }

    [Test]
    public void RegisterReportsMissingClientWithoutRecordingOwnership()
    {
        var registration = new InstallerRegistration((_, _) => new ProcessResult(-1, "", true), _recordPath);

        var result = registration.Register("codex", _target, TextWriter.Null);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(InstallerRegistrationExitCode.ClientUnavailable));
            Assert.That(File.Exists(_recordPath), Is.False);
        });
    }

    [Test]
    public void UnregisterRemovesOnlyAnOwnedUnchangedEntry()
    {
        File.WriteAllText(_recordPath, "[{\"Client\":\"codex\",\"ExecutablePath\":\"" + _target.Replace("\\", "\\\\") + "\",\"Fingerprint\":\"" + Fingerprint(_target) + "\"}]");
        var calls = new List<IReadOnlyList<string>>();
        var registration = new InstallerRegistration((_, arguments) =>
        {
            calls.Add(arguments);
            return new ProcessResult(0, CodexEntry(_target));
        }, _recordPath);

        var result = registration.Unregister("codex", _target, TextWriter.Null);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(InstallerRegistrationExitCode.Success));
            Assert.That(calls[1], Is.EqualTo(new[] { "mcp", "remove", "winmon" }));
            Assert.That(File.Exists(_recordPath), Is.False);
        });
    }

    [Test]
    public void UnregisterPreservesAnOwnedEntryChangedAfterInstallation()
    {
        File.WriteAllText(_recordPath, "[{\"Client\":\"codex\",\"ExecutablePath\":\"" + _target.Replace("\\", "\\\\") + "\",\"Fingerprint\":\"old\"}]");
        var calls = 0;
        var registration = new InstallerRegistration((_, _) =>
        {
            calls++;
            return new ProcessResult(0, CodexEntry(_target));
        }, _recordPath);

        var result = registration.Unregister("codex", _target, TextWriter.Null);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(InstallerRegistrationExitCode.Conflict));
            Assert.That(calls, Is.EqualTo(1));
            Assert.That(File.Exists(_recordPath), Is.True);
        });
    }

    [Test]
    public void ClaudeRegistrationUsesUserScopeAndAcceptsItsEmptyEnvironmentShape()
    {
        var config = Path.Combine(_directory, ".claude.json");
        var calls = new List<IReadOnlyList<string>>();
        var registration = new InstallerRegistration((_, arguments) =>
        {
            calls.Add(arguments);
            if (arguments[1] == "add")
                File.WriteAllText(config, ClaudeConfiguration(_target, "{}"));
            return new ProcessResult(0, "ok");
        }, _recordPath, config);

        var result = registration.Register("claude", _target, TextWriter.Null);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(InstallerRegistrationExitCode.Success));
            Assert.That(calls[0], Is.EqualTo(new[] { "mcp", "add", "--scope", "user", "--transport", "stdio", "winmon", "--", _target }));
            Assert.That(File.Exists(_recordPath), Is.True);
        });
    }

    [Test]
    public void ClaudeUnregisterPreservesAnEntryWithEditedEnvironment()
    {
        var config = Path.Combine(_directory, ".claude.json");
        File.WriteAllText(config, ClaudeConfiguration(_target, "{\"EXTRA\":\"changed\"}"));
        File.WriteAllText(_recordPath, "[{\"Client\":\"claude\",\"ExecutablePath\":\"" + _target.Replace("\\", "\\\\") + "\",\"Fingerprint\":\"old\"}]");
        var calls = 0;
        var registration = new InstallerRegistration((_, _) =>
        {
            calls++;
            return new ProcessResult(0, "ok");
        }, _recordPath, config);

        var result = registration.Unregister("claude", _target, TextWriter.Null);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(InstallerRegistrationExitCode.Conflict));
            Assert.That(calls, Is.Zero);
            Assert.That(File.Exists(_recordPath), Is.True);
        });
    }

    [TestCase("{")]
    [TestCase("{\"mcpServers\":null}")]
    [TestCase("{\"mcpServers\":[]}")]
    public void ClaudeRegistrationRefusesMalformedConfiguration(string contents)
    {
        var config = Path.Combine(_directory, ".claude.json");
        File.WriteAllText(config, contents);
        var registration = new InstallerRegistration((_, _) => new ProcessResult(0, "ok"), _recordPath, config);

        Assert.That(registration.Register("claude", _target, TextWriter.Null), Is.EqualTo(InstallerRegistrationExitCode.Failed));
    }

    [TestCase(null, false)]
    [TestCase("relative", true)]
    [TestCase("C:\\different", true)]
    public void CodexHomeAllowsOnlyTheStandardUserLocation(string? value, bool expected)
    {
        var home = value == "C:\\different" ? Path.Combine(Path.GetTempPath(), "different") : value;
        Assert.That(InstallerRegistration.UsesCustomCodexHome(home), Is.EqualTo(expected));
    }

    [Test]
    public void CodexHomeAllowsStandardUserLocation()
    {
        var standard = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");
        Assert.That(InstallerRegistration.UsesCustomCodexHome(standard), Is.False);
    }

    [Test]
    public void ProcessRunnerBoundsAnUnresponsiveClient()
    {
        var script = Path.Combine(_directory, "hold.ps1");
        File.WriteAllText(script, "Start-Sleep -Seconds 10\r\n");
        var originalPath = Environment.GetEnvironmentVariable("PATH");
        Environment.SetEnvironmentVariable("PATH", _directory + Path.PathSeparator + originalPath);
        try
        {
            var started = Stopwatch.GetTimestamp();
            var result = ProcessRunner.Run("hold", [], TimeSpan.FromMilliseconds(250));
            var elapsed = Stopwatch.GetElapsedTime(started);
            Assert.Multiple(() =>
            {
                Assert.That(result.ExitCode, Is.EqualTo(-1));
                Assert.That(elapsed, Is.LessThan(TimeSpan.FromSeconds(4)));
            });
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", originalPath);
        }
    }

    private static string CodexEntry(string path) => "{\"transport\":{\"type\":\"stdio\",\"command\":\"" + path.Replace("\\", "\\\\") + "\",\"args\":[],\"env\":null,\"cwd\":null}}";
    private static string Fingerprint(string path)
    {
        using var sha = System.Security.Cryptography.SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(CodexEntry(path))));
    }
    private static string ClaudeConfiguration(string path, string environment) =>
        "{\"mcpServers\":{\"winmon\":{\"command\":\"" + path.Replace("\\", "\\\\") + "\",\"args\":[],\"env\":" + environment + "}}}";
}
