using System.Diagnostics;
using System.ComponentModel;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace WinMon.Mcp;

internal enum InstallerRegistrationExitCode
{
    Success = 0,
    Conflict = 10,
    ClientUnavailable = 11,
    Failed = 12,
    InvalidArguments = 64
}

internal sealed record InstallerRegistrationRecord(string Client, string ExecutablePath, string Fingerprint);
internal sealed record CurrentRegistration(string ExecutablePath, string Fingerprint, bool IsExactDefault);

internal sealed class InstallerRegistration
{
    private const string Name = "winmon";
    private static readonly Mutex RegistrationMutex = new(false, @"Local\WinMon.McpInstallerRegistration");
    private readonly Func<string, IReadOnlyList<string>, ProcessResult> _run;
    private readonly string _recordPath;
    private readonly string? _claudeConfigurationPath;
    private readonly bool _usesDefaultConfiguration;

    internal InstallerRegistration(Func<string, IReadOnlyList<string>, ProcessResult>? run = null, string? recordPath = null,
        string? claudeConfigurationPath = null)
    {
        _usesDefaultConfiguration = run is null;
        _run = run ?? ((command, arguments) => ProcessRunner.Run(command, arguments));
        _recordPath = recordPath ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WinMon", "McpInstaller", "registrations.json");
        _claudeConfigurationPath = claudeConfigurationPath;
    }

    internal InstallerRegistrationExitCode Register(string client, string executablePath, TextWriter error) =>
        WithLock(() => RegisterCore(client, executablePath, error));

    private InstallerRegistrationExitCode RegisterCore(string client, string executablePath, TextWriter error)
    {
        if (!TryGetCommand(client, out var command, out var addArguments))
        {
            error.WriteLine("Unsupported MCP client.");
            return InstallerRegistrationExitCode.InvalidArguments;
        }

        if (!Path.IsPathFullyQualified(executablePath))
        {
            error.WriteLine("The installed WinMon MCP executable path is invalid.");
            return InstallerRegistrationExitCode.InvalidArguments;
        }
        var target = Path.GetFullPath(executablePath);
        if (!File.Exists(target))
        {
            error.WriteLine("The installed WinMon MCP executable is missing.");
            return InstallerRegistrationExitCode.InvalidArguments;
        }

        var existing = ReadCurrent(client, command);
        if (existing.Status == RegistrationState.Found)
        {
            if (existing.Value!.IsExactDefault && SamePath(existing.Value.ExecutablePath, target)) return InstallerRegistrationExitCode.Success;
            error.WriteLine("The WinMon MCP entry already exists and was preserved.");
            return InstallerRegistrationExitCode.Conflict;
        }
        if (existing.Status == RegistrationState.Unavailable)
        {
            error.WriteLine("The selected MCP client is not installed.");
            return InstallerRegistrationExitCode.ClientUnavailable;
        }
        if (existing.Status != RegistrationState.Absent)
        {
            error.WriteLine("The existing WinMon MCP entry could not be inspected and was preserved.");
            return InstallerRegistrationExitCode.Failed;
        }

        var added = _run(command, addArguments(target));
        if (added.ExitCode != 0)
        {
            error.WriteLine(added.Unavailable ? "The selected MCP client is not installed." : "The WinMon MCP entry could not be registered.");
            return added.Unavailable ? InstallerRegistrationExitCode.ClientUnavailable : InstallerRegistrationExitCode.Failed;
        }
        var verified = ReadCurrent(client, command);
        if (verified.Status != RegistrationState.Found || !verified.Value!.IsExactDefault || !SamePath(verified.Value.ExecutablePath, target))
        {
            error.WriteLine("The WinMon MCP entry could not be verified.");
            return InstallerRegistrationExitCode.Failed;
        }

        WriteRecord(client, target, verified.Value.Fingerprint);
        return InstallerRegistrationExitCode.Success;
    }

    internal InstallerRegistrationExitCode Unregister(string client, string executablePath, TextWriter error) =>
        WithLock(() => UnregisterCore(client, executablePath, error));

    private InstallerRegistrationExitCode UnregisterCore(string client, string executablePath, TextWriter error)
    {
        if (!Path.IsPathFullyQualified(executablePath)) return InstallerRegistrationExitCode.InvalidArguments;
        var target = Path.GetFullPath(executablePath);
        var record = ReadRecords().SingleOrDefault(candidate => candidate.Client == client);
        if (record is null) return InstallerRegistrationExitCode.Success;
        if (!string.Equals(record.ExecutablePath, target, StringComparison.OrdinalIgnoreCase))
        {
            error.WriteLine("The recorded WinMon MCP entry does not match this installation.");
            return InstallerRegistrationExitCode.Conflict;
        }
        if (!TryGetCommand(client, out var command, out _)) return InstallerRegistrationExitCode.ClientUnavailable;

        var current = ReadCurrent(client, command);
        if (current.Status != RegistrationState.Found || !current.Value!.IsExactDefault ||
            !SamePath(current.Value.ExecutablePath, target) || current.Value.Fingerprint != record.Fingerprint)
        {
            error.WriteLine("The WinMon MCP entry changed and was preserved.");
            return InstallerRegistrationExitCode.Conflict;
        }

        var removed = _run(command, RemoveArguments(client));
        if (removed.ExitCode != 0)
        {
            error.WriteLine(removed.Unavailable ? "The selected MCP client is not installed." : "The WinMon MCP entry could not be removed.");
            return removed.Unavailable ? InstallerRegistrationExitCode.ClientUnavailable : InstallerRegistrationExitCode.Failed;
        }
        DeleteRecord(client);
        return InstallerRegistrationExitCode.Success;
    }

    private InstallerRegistrationExitCode WithLock(Func<InstallerRegistrationExitCode> operation)
    {
        try
        {
            if (!RegistrationMutex.WaitOne(TimeSpan.FromSeconds(15))) return InstallerRegistrationExitCode.Failed;
            try { return operation(); }
            finally { RegistrationMutex.ReleaseMutex(); }
        }
        catch (AbandonedMutexException)
        {
            try { return operation(); }
            finally { RegistrationMutex.ReleaseMutex(); }
        }
    }

    private static bool TryGetCommand(string client, out string command, out Func<string, IReadOnlyList<string>> addArguments)
    {
        command = client;
        addArguments = client switch
        {
            "codex" => target => ["mcp", "add", Name, "--", target],
            "claude" => target => ["mcp", "add", "--scope", "user", "--transport", "stdio", Name, "--", target],
            _ => null!
        };
        return addArguments is not null;
    }

    private static IReadOnlyList<string> GetArguments(string client) => client == "codex"
        ? ["mcp", "get", Name, "--json"] : ["mcp", "get", Name];

    private static IReadOnlyList<string> RemoveArguments(string client) => client == "codex"
        ? ["mcp", "remove", Name] : ["mcp", "remove", "--scope", "user", Name];

    private enum RegistrationState { Absent, Found, Unavailable, Failed }
    private sealed record CurrentResult(RegistrationState Status, CurrentRegistration? Value = null);

    private CurrentResult ReadCurrent(string client, string command)
    {
        if (_usesDefaultConfiguration && ((client == "codex" && UsesCustomCodexHome(Environment.GetEnvironmentVariable("CODEX_HOME"))) ||
            (client == "claude" && !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR")))))
            return new CurrentResult(RegistrationState.Failed);
        if (client == "claude") return ReadClaudeConfiguration();
        var result = _run(command, GetArguments(client));
        if (result.Unavailable) return new CurrentResult(RegistrationState.Unavailable);
        if (result.ExitCode != 0)
            return new CurrentResult(result.Output.Contains("No MCP server named 'winmon' found.", StringComparison.Ordinal)
                ? RegistrationState.Absent : RegistrationState.Failed);
        try
        {
            using var json = JsonDocument.Parse(result.Output);
            var transport = json.RootElement.GetProperty("transport");
            if (!transport.TryGetProperty("type", out var type) || type.GetString() != "stdio" ||
                !transport.TryGetProperty("command", out var executable) || executable.GetString() is not { } path)
                return new CurrentResult(RegistrationState.Failed);
            var exact = IsEmptyArray(transport, "args") && IsNull(transport, "env") && IsNull(transport, "cwd");
            return new CurrentResult(RegistrationState.Found, new CurrentRegistration(path, Fingerprint(json.RootElement), exact));
        }
        catch (JsonException) { return new CurrentResult(RegistrationState.Failed); }
        catch (KeyNotFoundException) { return new CurrentResult(RegistrationState.Failed); }
        catch (InvalidOperationException) { return new CurrentResult(RegistrationState.Failed); }
    }

    private CurrentResult ReadClaudeConfiguration()
    {
        var path = _claudeConfigurationPath ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude.json");
        if (!File.Exists(path)) return new CurrentResult(RegistrationState.Absent);
        try
        {
            using var json = JsonDocument.Parse(File.ReadAllText(path));
            if (!json.RootElement.TryGetProperty("mcpServers", out var servers)) return new CurrentResult(RegistrationState.Absent);
            if (servers.ValueKind != JsonValueKind.Object) return new CurrentResult(RegistrationState.Failed);
            if (!servers.TryGetProperty(Name, out var entry)) return new CurrentResult(RegistrationState.Absent);
            if (entry.TryGetProperty("type", out var type) && type.GetString() != "stdio")
                return new CurrentResult(RegistrationState.Failed);
            if (!entry.TryGetProperty("command", out var executable) || executable.GetString() is not { } target)
                return new CurrentResult(RegistrationState.Failed);
            var exact = IsEmptyArray(entry, "args") && IsEmptyEnvironment(entry) && !entry.TryGetProperty("cwd", out _);
            return new CurrentResult(RegistrationState.Found, new CurrentRegistration(target, Fingerprint(entry), exact));
        }
        catch (JsonException) { return new CurrentResult(RegistrationState.Failed); }
        catch (IOException) { return new CurrentResult(RegistrationState.Failed); }
        catch (InvalidOperationException) { return new CurrentResult(RegistrationState.Failed); }
    }

    private static bool IsEmptyArray(JsonElement value, string property) =>
        !value.TryGetProperty(property, out var array) || (array.ValueKind == JsonValueKind.Array && array.GetArrayLength() == 0);
    private static bool IsNull(JsonElement value, string property) =>
        !value.TryGetProperty(property, out var item) || item.ValueKind == JsonValueKind.Null;
    private static bool IsEmptyEnvironment(JsonElement value) => !value.TryGetProperty("env", out var environment) ||
        environment.ValueKind == JsonValueKind.Null || (environment.ValueKind == JsonValueKind.Object && !environment.EnumerateObject().Any());
    private static string Fingerprint(JsonElement element) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(element.GetRawText())));
    private static bool SamePath(string first, string second) => string.Equals(Path.GetFullPath(first), Path.GetFullPath(second), StringComparison.OrdinalIgnoreCase);
    internal static bool UsesCustomCodexHome(string? codexHome)
    {
        if (string.IsNullOrWhiteSpace(codexHome)) return false;
        if (!Path.IsPathFullyQualified(codexHome)) return true;
        var standard = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");
        return !SamePath(codexHome, standard);
    }

    private List<InstallerRegistrationRecord> ReadRecords()
    {
        try
        {
            return File.Exists(_recordPath)
                ? JsonSerializer.Deserialize<List<InstallerRegistrationRecord>>(File.ReadAllText(_recordPath)) ?? []
                : [];
        }
        catch (JsonException)
        {
            return [];
        }
        catch (IOException)
        {
            return [];
        }
    }

    private void WriteRecord(string client, string target, string fingerprint)
    {
        var records = ReadRecords().Where(record => record.Client != client).Append(new InstallerRegistrationRecord(client, target, fingerprint)).ToList();
        var directory = Path.GetDirectoryName(_recordPath)!;
        Directory.CreateDirectory(directory);
        var temporary = _recordPath + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(records));
            File.Move(temporary, _recordPath, true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private void DeleteRecord(string client)
    {
        var records = ReadRecords().Where(record => record.Client != client).ToList();
        if (records.Count == 0)
        {
            if (File.Exists(_recordPath)) File.Delete(_recordPath);
            return;
        }
        File.WriteAllText(_recordPath, JsonSerializer.Serialize(records));
    }
}

internal sealed record ProcessResult(int ExitCode, string Output, bool Unavailable = false);

internal static class ProcessRunner
{
    internal static ProcessResult Run(string command, IReadOnlyList<string> arguments, TimeSpan? timeout = null)
    {
        var executable = Resolve(command);
        if (executable is null) return new ProcessResult(-1, "", true);
        using var process = new Process { StartInfo = CreateStartInfo(executable, arguments) };
        using var cancellation = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(15));
        try
        {
            process.Start();
            var output = process.StandardOutput.ReadToEndAsync(cancellation.Token);
            var error = process.StandardError.ReadToEndAsync(cancellation.Token);
            process.WaitForExitAsync(cancellation.Token).GetAwaiter().GetResult();
            var text = Task.WhenAll(output, error).WaitAsync(cancellation.Token).GetAwaiter().GetResult();
            return new ProcessResult(process.ExitCode, text[0] + text[1]);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(true); } catch (InvalidOperationException) { }
            process.WaitForExit(2000);
            return new ProcessResult(-1, "");
        }
        catch (Win32Exception)
        {
            return new ProcessResult(-1, "", true);
        }
        catch (IOException)
        {
            return new ProcessResult(-1, "", true);
        }
    }

    private static ProcessStartInfo CreateStartInfo(string executable, IReadOnlyList<string> arguments)
    {
        var isPowerShellScript = executable.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase);
        var info = new ProcessStartInfo
        {
            FileName = isPowerShellScript
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe")
                : executable,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        if (isPowerShellScript)
        {
            info.ArgumentList.Add("-NoProfile");
            info.ArgumentList.Add("-File");
            info.ArgumentList.Add(executable);
        }
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        return info;
    }

    private static string? Resolve(string command)
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        foreach (var extension in new[] { ".exe", ".ps1" })
        {
            var root = directory.Trim('"');
            if (!Path.IsPathFullyQualified(root)) continue;
            var candidate = Path.Combine(root, command + extension);
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }
}
