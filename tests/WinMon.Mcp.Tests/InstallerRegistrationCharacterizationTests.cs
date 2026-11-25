using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using NUnit.Framework;

namespace WinMon.Mcp.Tests;

[TestFixture, Category("Integration"), NonParallelizable]
public sealed class InstallerRegistrationCharacterizationTests
{
    private const string Prefix = "WinMon-Registration-Characterization-";
    private const string Preserved = "[{\"Client\":\"other\",\"ExecutablePath\":\"keep\",\"Fingerprint\":\"opaque\"}]";
    private string _directory = null!;
    private string _target = null!;
    private string _record = null!;
    private string _claude = null!;

    [SetUp]
    public void SetUp()
    {
        _directory = Path.Combine(Path.GetTempPath(), Prefix + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        _target = Path.Combine(_directory, "owned target.exe");
        File.WriteAllText(_target, "fixture, never executed");
        _record = Path.Combine(_directory, "records.json");
        _claude = Path.Combine(_directory, "claude.json");
    }

    [TearDown]
    public void TearDown()
    {
        var parent = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())) + Path.DirectorySeparatorChar;
        var target = Path.GetFullPath(_directory);
        Assert.That(target.StartsWith(parent, StringComparison.OrdinalIgnoreCase) && Path.GetFileName(target).StartsWith(Prefix, StringComparison.Ordinal), Is.True);
        if (Directory.Exists(target)) Directory.Delete(target, true);
    }

    private string Entry(string? target = null) => JsonSerializer.Serialize(new
    {
        transport = new { type = "stdio", command = target ?? _target, args = Array.Empty<string>(), env = (object?)null, cwd = (string?)null }
    });
    private static ProcessResult Absent() => new(1, "Error: No MCP server named 'winmon' found.");
    private InstallerRegistration Registration(List<(string Command, string[] Args)> calls, params ProcessResult[] results)
    {
        var queue = new Queue<ProcessResult>(results);
        return new InstallerRegistration((command, arguments) =>
        {
            calls.Add((command, arguments.ToArray()));
            Assert.That(queue, Is.Not.Empty, "Unexpected process operation.");
            return queue.Dequeue();
        }, _record, _claude);
    }
    private void AssertPreserved()
    {
        Assert.That(File.ReadAllText(_record), Is.EqualTo(Preserved));
        Assert.That(File.Exists(_record + ".tmp"), Is.False);
    }

    [TestCase("unsupported")] [TestCase("relative")] [TestCase("missing")] [TestCase("null")]
    public void InvalidRegistrationInputsAreRejectedBeforeInspection(string kind)
    {
        File.WriteAllText(_record, Preserved);
        var calls = new List<(string, string[])>();
        var registration = Registration(calls);
        using var error = new StringWriter(CultureInfo.InvariantCulture);
        if (kind == "null")
        {
            var exception = Assert.Throws<ArgumentNullException>(() => registration.Register("codex", null!, error));
            Assert.That(exception!.ParamName, Is.EqualTo("path"));
            Assert.That(error.ToString(), Is.Empty);
            Assert.That(calls, Is.Empty); AssertPreserved();
            return;
        }
        var result = registration.Register(kind == "unsupported" ? "unknown" : "codex",
            kind == "relative" ? "relative.exe" : kind == "missing" ? Path.Combine(_directory, "absent.exe") : kind == "null" ? null! : _target, error);
        Assert.That(result, Is.EqualTo(InstallerRegistrationExitCode.InvalidArguments));
        Assert.That(error.ToString(), Is.EqualTo((kind == "unsupported" ? "Unsupported MCP client." : kind == "missing"
            ? "The installed WinMon MCP executable is missing." : "The installed WinMon MCP executable path is invalid.") + Environment.NewLine));
        Assert.That(calls, Is.Empty); AssertPreserved();
    }

    private static IEnumerable<TestCaseData> MalformedCodex()
    {
        string[] values = ["{", "null", "[]", "{}", "{\"transport\":null}", "{\"transport\":[]}",
            "{\"transport\":{}}", "{\"transport\":{\"type\":\"sse\"}}", "{\"transport\":{\"type\":null}}",
            "{\"transport\":{\"type\":1}}", "{\"transport\":{\"type\":\"stdio\"}}",
            "{\"transport\":{\"type\":\"stdio\",\"command\":null}}", "{\"transport\":{\"type\":\"stdio\",\"command\":1}}"];
        for (var i = 0; i < values.Length; i++) yield return new TestCaseData(values[i]).SetName("RegistrationMalformedCodex_" + i);
    }
    [TestCaseSource(nameof(MalformedCodex))]
    public void MalformedCodexInspectionPreservesTheEntryAndOwnershipRecord(string output)
    {
        File.WriteAllText(_record, Preserved);
        var calls = new List<(string, string[])>();
        var registration = Registration(calls, new ProcessResult(0, output));
        using var error = new StringWriter(CultureInfo.InvariantCulture);
        Assert.That(registration.Register("codex", _target, error), Is.EqualTo(InstallerRegistrationExitCode.Failed));
        Assert.That(error.ToString(), Is.EqualTo("The existing WinMon MCP entry could not be inspected and was preserved." + Environment.NewLine));
        Assert.That(calls, Has.Count.EqualTo(1)); AssertPreserved();
    }

    [TestCase("args", "null", false)] [TestCase("args", "{}", false)] [TestCase("args", "[\"extra\"]", false)]
    [TestCase("args", "[]", true)] [TestCase("env", "{}", false)] [TestCase("env", "null", true)]
    [TestCase("cwd", "\"\"", false)] [TestCase("cwd", "null", true)]
    public void CodexExactDefaultShapesDetermineConflictWithoutAdoptingManualEntries(string property, string value, bool exact)
    {
        File.WriteAllText(_record, Preserved);
        var node = JsonNode.Parse(Entry())!; node["transport"]![property] = JsonNode.Parse(value);
        var calls = new List<(string, string[])>();
        using var error = new StringWriter(CultureInfo.InvariantCulture);
        Assert.That(Registration(calls, new ProcessResult(0, node.ToJsonString())).Register("codex", _target, error),
            Is.EqualTo(exact ? InstallerRegistrationExitCode.Success : InstallerRegistrationExitCode.Conflict));
        Assert.That(error.ToString(), Is.EqualTo(exact ? "" : "The WinMon MCP entry already exists and was preserved." + Environment.NewLine));
        Assert.That(calls, Has.Count.EqualTo(1)); AssertPreserved();
    }

    [Test]
    public void MissingOptionalCodexPropertiesAndCanonicalCaseKeepTheManualEntry()
    {
        File.WriteAllText(_record, Preserved);
        var output = JsonSerializer.Serialize(new { transport = new { type = "stdio", command = _target.ToUpperInvariant() } });
        var calls = new List<(string, string[])>();
        Assert.That(Registration(calls, new ProcessResult(0, output)).Register("codex",
            Path.Combine(_directory, "unused", "..", Path.GetFileName(_target)), TextWriter.Null), Is.EqualTo(InstallerRegistrationExitCode.Success));
        Assert.That(calls, Has.Count.EqualTo(1)); AssertPreserved();
    }

    [TestCase(false)] [TestCase(true)]
    public void AddFailureRetainsItsExitCodeMessageAndExistingOwnership(bool unavailable)
    {
        File.WriteAllText(_record, Preserved);
        var calls = new List<(string, string[])>();
        using var error = new StringWriter(CultureInfo.InvariantCulture);
        var result = Registration(calls, Absent(), new ProcessResult(-1, "not forwarded", unavailable)).Register("codex", _target, error);
        Assert.That(result, Is.EqualTo(unavailable ? InstallerRegistrationExitCode.ClientUnavailable : InstallerRegistrationExitCode.Failed));
        Assert.That(error.ToString(), Is.EqualTo((unavailable ? "The selected MCP client is not installed." : "The WinMon MCP entry could not be registered.") + Environment.NewLine));
        Assert.That(calls, Has.Count.EqualTo(2));
        Assert.That(calls[1].Item2, Is.EqualTo(new[] { "mcp", "add", "winmon", "--", _target })); AssertPreserved();
    }

    [TestCase("absent")] [TestCase("unavailable")] [TestCase("malformed")] [TestCase("path")] [TestCase("arguments")]
    public void VerificationFailureNeverCreatesOrReplacesAnOwnershipRecord(string kind)
    {
        File.WriteAllText(_record, Preserved);
        var output = kind == "path" ? Entry(Path.Combine(_directory, "other.exe")) : Entry();
        if (kind == "arguments") { var node = JsonNode.Parse(output)!; node["transport"]!["args"] = new JsonArray("edited"); output = node.ToJsonString(); }
        var verified = kind == "absent" ? Absent() : kind == "unavailable" ? new ProcessResult(-1, "", true) : new ProcessResult(0, kind == "malformed" ? "{" : output);
        var calls = new List<(string, string[])>();
        using var error = new StringWriter(CultureInfo.InvariantCulture);
        Assert.That(Registration(calls, Absent(), new ProcessResult(0, "added"), verified).Register("codex", _target, error), Is.EqualTo(InstallerRegistrationExitCode.Failed));
        Assert.That(error.ToString(), Is.EqualTo("The WinMon MCP entry could not be verified." + Environment.NewLine));
        Assert.That(calls, Has.Count.EqualTo(3)); AssertPreserved();
    }

    [Test]
    public void SuccessfulRegistrationKeepsOtherRecordsAndPinsTheRawVerifiedFingerprint()
    {
        File.WriteAllText(_record, Preserved);
        var verified = Entry();
        var calls = new List<(string, string[])>();
        Assert.That(Registration(calls, Absent(), new ProcessResult(0, "", true), new ProcessResult(0, verified)).Register("codex", _target, TextWriter.Null), Is.EqualTo(InstallerRegistrationExitCode.Success));
        var records = JsonSerializer.Deserialize<InstallerRegistrationRecord[]>(File.ReadAllText(_record))!;
        Assert.That(records.Select(record => record.Client), Is.EqualTo(new[] { "other", "codex" }));
        Assert.That(records[0], Is.EqualTo(new InstallerRegistrationRecord("other", "keep", "opaque")));
        Assert.That(records[1].ExecutablePath, Is.EqualTo(_target));
        Assert.That(records[1].Fingerprint, Is.EqualTo(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(verified)))));
        Assert.That(File.Exists(_record + ".tmp"), Is.False);
        Assert.That(calls.Select(call => call.Item1), Is.All.EqualTo("codex"));
    }

    [Test]
    public void OwnershipWriteFailurePropagatesAndCleansItsTemporaryFile()
    {
        Directory.CreateDirectory(_record);
        var calls = new List<(string, string[])>();
        Assert.Throws<UnauthorizedAccessException>(() => Registration(calls, Absent(), new ProcessResult(0, ""), new ProcessResult(0, Entry())).Register("codex", _target, TextWriter.Null));
        Assert.That(Directory.Exists(_record), Is.True);
        Assert.That(File.Exists(_record + ".tmp"), Is.False);
        Assert.That(calls, Has.Count.EqualTo(3));
    }

    [Test]
    public void EmptyExistingCommandRetainsItsPathExceptionWithoutChangingOwnership()
    {
        File.WriteAllText(_record, Preserved);
        var calls = new List<(string, string[])>();
        Assert.Throws<ArgumentException>(() => Registration(calls, new ProcessResult(0, Entry(""))).Register("codex", _target, TextWriter.Null));
        Assert.That(calls, Has.Count.EqualTo(1)); AssertPreserved();
    }

    private static IEnumerable<TestCaseData> ClaudeCases()
    {
        (string Shape, string State)[] cases =
        [
            ("missing-file", "absent"), ("missing-servers", "absent"), ("missing-entry", "absent"),
            ("malformed", "failed"), ("root-null", "failed"), ("root-array", "failed"),
            ("servers-null", "failed"), ("servers-array", "failed"), ("entry-null", "failed"),
            ("type-sse", "failed"), ("type-null", "failed"), ("type-number", "failed"),
            ("command-missing", "failed"), ("command-null", "failed"), ("command-number", "failed"),
            ("ordinary", "exact"), ("type-missing", "exact"), ("args-missing", "exact"),
            ("environment-missing", "exact"), ("environment-empty", "exact"),
            ("args-null", "custom"), ("args-nonempty", "custom"), ("environment-array", "custom"),
            ("environment-edited", "custom"), ("cwd-null", "custom"), ("cwd-empty", "custom")
        ];
        for (var i = 0; i < cases.Length; i++) yield return new TestCaseData(cases[i].Shape, cases[i].State).SetName("RegistrationClaudeShape_" + i);
    }

    [TestCaseSource(nameof(ClaudeCases))]
    public void ClaudeConfigurationShapesPreserveExistingDataAndInspectionRules(string shape, string state)
    {
        File.WriteAllText(_record, Preserved);
        var entry = JsonNode.Parse(Entry())!["transport"]!.DeepClone().AsObject();
        entry.Remove("cwd");
        var document = new JsonObject { ["mcpServers"] = new JsonObject { ["winmon"] = entry, ["other"] = new JsonObject { ["command"] = "keep" } } };
        string? raw = null;
        switch (shape)
        {
            case "missing-file": break;
            case "missing-servers": raw = "{}"; break;
            case "missing-entry": document["mcpServers"]!.AsObject().Remove("winmon"); break;
            case "malformed": raw = "{"; break;
            case "root-null": raw = "null"; break;
            case "root-array": raw = "[]"; break;
            case "servers-null": document["mcpServers"] = null; break;
            case "servers-array": document["mcpServers"] = new JsonArray(); break;
            case "entry-null": document["mcpServers"]!["winmon"] = null; break;
            case "type-sse": entry["type"] = "sse"; break;
            case "type-null": entry["type"] = null; break;
            case "type-number": entry["type"] = 1; break;
            case "command-missing": entry.Remove("command"); break;
            case "command-null": entry["command"] = null; break;
            case "command-number": entry["command"] = 1; break;
            case "type-missing": entry.Remove("type"); break;
            case "args-missing": entry.Remove("args"); break;
            case "environment-missing": entry.Remove("env"); break;
            case "environment-empty": entry["env"] = new JsonObject(); break;
            case "args-null": entry["args"] = null; break;
            case "args-nonempty": entry["args"] = new JsonArray("custom"); break;
            case "environment-array": entry["env"] = new JsonArray(); break;
            case "environment-edited": entry["env"] = new JsonObject { ["EXTRA"] = "private" }; break;
            case "cwd-null": entry["cwd"] = null; break;
            case "cwd-empty": entry["cwd"] = ""; break;
        }
        if (shape != "missing-file") { raw ??= document.ToJsonString(); File.WriteAllText(_claude, raw); }
        var calls = new List<(string, string[])>();
        using var error = new StringWriter(CultureInfo.InvariantCulture);
        var result = Registration(calls, new ProcessResult(-1, "not forwarded")).Register("claude", _target, error);
        Assert.That(result, Is.EqualTo(state == "exact" ? InstallerRegistrationExitCode.Success : state == "custom" ? InstallerRegistrationExitCode.Conflict : InstallerRegistrationExitCode.Failed));
        var expected = state switch { "exact" => "", "custom" => "The WinMon MCP entry already exists and was preserved.",
            "absent" => "The WinMon MCP entry could not be registered.", _ => "The existing WinMon MCP entry could not be inspected and was preserved." };
        Assert.That(error.ToString(), Is.EqualTo(expected.Length == 0 ? "" : expected + Environment.NewLine));
        Assert.That(calls, Has.Count.EqualTo(state == "absent" ? 1 : 0));
        if (shape == "missing-file") Assert.That(File.Exists(_claude), Is.False);
        else Assert.That(File.ReadAllText(_claude), Is.EqualTo(raw));
        AssertPreserved();
    }

    [Test]
    public void LockedClaudeConfigurationIsPreservedWithoutRunningAClient()
    {
        File.WriteAllText(_record, Preserved); File.WriteAllText(_claude, "{\"mcpServers\":{}}");
        var calls = new List<(string, string[])>();
        using (var held = new FileStream(_claude, FileMode.Open, FileAccess.Read, FileShare.None))
            Assert.That(Registration(calls).Register("claude", _target, TextWriter.Null), Is.EqualTo(InstallerRegistrationExitCode.Failed));
        Assert.That(File.ReadAllText(_claude), Is.EqualTo("{\"mcpServers\":{}}"));
        Assert.That(calls, Is.Empty); AssertPreserved();
    }

    [Test]
    public void MalformedOwnershipRecordsRetainTheirExistingReplacementBehavior()
    {
        File.WriteAllText(_record, "malformed ownership fixture");
        var calls = new List<(string, string[])>();
        Assert.That(Registration(calls, Absent(), new ProcessResult(0, ""), new ProcessResult(0, Entry())).Register("codex", _target, TextWriter.Null), Is.EqualTo(InstallerRegistrationExitCode.Success));
        var records = JsonSerializer.Deserialize<InstallerRegistrationRecord[]>(File.ReadAllText(_record))!;
        Assert.That(records, Has.Length.EqualTo(1));
        Assert.That(records[0].Client, Is.EqualTo("codex"));
        Assert.That(File.Exists(_record + ".tmp"), Is.False);
    }

    [TestCase("codex")] [TestCase("claude")]
    public async Task DefaultConfigurationGuardRejectsChildOnlyCustomHomes(string client)
    {
        var result = await RunIsolatedChild(["--installer-registration", client, "--install-path", _target]);
        Assert.That(result.Exit, Is.EqualTo((int)InstallerRegistrationExitCode.Failed), result.Error);
        Assert.That(result.Output, Is.Empty);
        Assert.That(result.Error, Is.EqualTo("The existing WinMon MCP entry could not be inspected and was preserved." + Environment.NewLine));
        Assert.That(Directory.GetFiles(_directory), Is.EqualTo(new[] { _target }));
    }

    [TestCase("arity")] [TestCase("flag")] [TestCase("option")] [TestCase("client")] [TestCase("unregister")]
    public async Task InstallerCallbackRejectsInvalidArgumentsBeforeAnyUserConfigurationAccess(string kind)
    {
        var arguments = kind switch
        {
            "arity" => new[] { "--installer-registration", "codex" },
            "flag" => ["--installer-registration", "codex", "--wrong", _target],
            "option" => ["--installer-registration", "codex", "--install-path", _target, "--wrong"],
            "client" => ["--installer-registration", "unknown", "--install-path", _target],
            _ => ["--installer-registration", "codex", "--install-path", "relative.exe", "--unregister"]
        };
        var result = await RunIsolatedChild(arguments);
        Assert.That(result.Exit, Is.EqualTo((int)InstallerRegistrationExitCode.InvalidArguments), result.Error);
        Assert.That(result.Output, Is.Empty);
        Assert.That(result.Error, Is.EqualTo(kind == "client" ? "Unsupported MCP client." + Environment.NewLine : ""));
        Assert.That(Directory.GetFiles(_directory), Is.EqualTo(new[] { _target }));
    }

    private async Task<(int Exit, string Output, string Error)> RunIsolatedChild(string[] arguments)
    {
        var executable = Path.GetFullPath(Path.Combine(System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory(), "..", "..", "..", "dotnet.exe"));
        Assert.That(File.Exists(executable), Is.True);
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = _directory };
        start.Environment["PATH"] = _directory;
        start.Environment["CODEX_HOME"] = Path.Combine(_directory, "codex-home");
        start.Environment["CLAUDE_CONFIG_DIR"] = Path.Combine(_directory, "claude-home");
        foreach (var argument in new[] { "exec", "--runtimeconfig", Path.Combine(TestContext.CurrentContext.TestDirectory, "WinMon.Mcp.Tests.runtimeconfig.json"),
            "--depsfile", Path.Combine(TestContext.CurrentContext.TestDirectory, "WinMon.Mcp.Tests.deps.json"),
            Path.Combine(TestContext.CurrentContext.TestDirectory, "WinMon.Mcp.dll") }) start.ArgumentList.Add(argument);
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var error = process.StandardError.ReadToEndAsync(timeout.Token);
        try { await process.WaitForExitAsync(timeout.Token); }
        finally { if (!process.HasExited) { process.Kill(true); await process.WaitForExitAsync(); } }
        var standardOutput = await output; var standardError = await error;
        return (process.ExitCode, standardOutput, standardError);
    }
}
