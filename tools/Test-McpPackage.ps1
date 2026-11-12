#requires -Version 7.4
<#
.SYNOPSIS
Exercises a published WinMon MCP executable from a source-free path containing spaces.

.DESCRIPTION
Copies the self-contained single-file executable to a fresh external test directory, initializes it
through JSON-RPC over stdio, discovers its tools and invokes get_system_snapshot. The server child
alone receives a nonexistent DOTNET_ROOT. The existing SDK integration test then reruns the same
real server through WINMON_MCP_TEST_EXE. This cannot replace acceptance on a machine without a .NET
SDK/runtime.
#>
[CmdletBinding()]
param(
    [string]$Executable,
    [string]$PublishDirectory,
    [string]$TestProject = 'tests/WinMon.Mcp.Tests/WinMon.Mcp.Tests.csproj',
    [string]$PluginArchive,
    [switch]$ValidatePlugin,
    [switch]$SkipSdkIntegration
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = Split-Path $PSScriptRoot -Parent
if ([bool]$Executable -eq [bool]$PublishDirectory) {
    throw 'Specify exactly one of -Executable or -PublishDirectory.'
}
if ($PublishDirectory) { $Executable = Join-Path $PublishDirectory 'WinMon.Mcp.exe' }
$Executable = (Resolve-Path -LiteralPath $Executable -ErrorAction Stop).Path
if ((Split-Path $Executable -Leaf) -cne 'WinMon.Mcp.exe' -or (Get-Item -LiteralPath $Executable).Length -lt 1kb) {
    throw "Expected a published WinMon.Mcp.exe: $Executable"
}
$TestProject = (Resolve-Path -LiteralPath (Join-Path $root $TestProject) -ErrorAction Stop).Path
$runId = [guid]::NewGuid().ToString('N')
$evidence = Join-Path $root "artifacts/mcp-package-validation/$runId"
$run = Join-Path ([IO.Path]::GetTempPath()) "WinMon MCP Package $runId"
$serverDirectory = Join-Path $run 'path with spaces'
$server = Join-Path $serverDirectory 'WinMon.Mcp.exe'
New-Item -ItemType Directory -Path $serverDirectory -Force | Out-Null
New-Item -ItemType Directory -Path $evidence -Force | Out-Null
Set-Content -LiteralPath (Join-Path $evidence 'external-package-path.txt') -Value $run -Encoding utf8NoBOM
Copy-Item -LiteralPath $Executable -Destination $server
$plugin = Join-Path $run 'plugin'
if ($PluginArchive) {
    $PluginArchive = (Resolve-Path -LiteralPath $PluginArchive -ErrorAction Stop).Path
    Expand-Archive -LiteralPath $PluginArchive -DestinationPath $plugin
}
else {
    New-Item -ItemType Directory -Path $plugin -Force | Out-Null
    $pluginFiles = @(Get-ChildItem -LiteralPath (Join-Path $root 'plugins/claude-code/winmon') -Force)
    if ($pluginFiles.Count -eq 0) { throw 'Claude Code plugin source has no files.' }
    Copy-Item -LiteralPath $pluginFiles.FullName -Destination $plugin -Recurse
}
if (!(Test-Path -LiteralPath (Join-Path $plugin '.claude-plugin/plugin.json')) -or
    !(Test-Path -LiteralPath (Join-Path $plugin '.mcp.json'))) {
    throw "Claude Code plugin is incomplete: $plugin"
}

function Invoke-IsolatedClient([string]$Script, [string[]]$Arguments, [hashtable]$Environment) {
    $client = [Diagnostics.Process]::new()
    $client.StartInfo = [Diagnostics.ProcessStartInfo]::new((Join-Path $PSHOME 'pwsh.exe'))
    $client.StartInfo.UseShellExecute = $false
    $client.StartInfo.CreateNoWindow = $true
    $client.StartInfo.WorkingDirectory = $serverDirectory
    $client.StartInfo.RedirectStandardOutput = $true
    $client.StartInfo.RedirectStandardError = $true
    $client.StartInfo.ArgumentList.Add('-NoProfile')
    $client.StartInfo.ArgumentList.Add('-File')
    $client.StartInfo.ArgumentList.Add($Script)
    foreach ($argument in $Arguments) { $client.StartInfo.ArgumentList.Add($argument) }
    foreach ($entry in $Environment.GetEnumerator()) { $client.StartInfo.Environment[$entry.Key] = $entry.Value }
    try {
        if (!$client.Start()) { throw "Could not start $Script." }
        $stdout = $client.StandardOutput.ReadToEndAsync()
        $stderr = $client.StandardError.ReadToEndAsync()
        if (!$client.WaitForExit(30000)) {
            $client.Kill($true)
            throw "$Script did not exit within 30 seconds."
        }
        $output = $stdout.GetAwaiter().GetResult() + $stderr.GetAwaiter().GetResult()
        if ($client.ExitCode -ne 0) { throw "$Script $($Arguments -join ' ') failed: $output" }
        return $output
    }
    finally { $client.Dispose() }
}

function Save-ClientOutput([string]$Name, [string]$Output) {
    Set-Content -LiteralPath (Join-Path $evidence $Name) -Value $Output -Encoding utf8NoBOM
}

$savedExecutable = [Environment]::GetEnvironmentVariable('WINMON_MCP_TEST_EXE', 'Process')
try {
    & (Join-Path $PSScriptRoot 'Test-McpStdio.ps1') -Executable $server -OutputDirectory $evidence

    if (!$SkipSdkIntegration) {
        [Environment]::SetEnvironmentVariable('WINMON_MCP_TEST_EXE', $server, 'Process')
        Push-Location $root
        try {
            dotnet test $TestProject -c Release -m:1 -p:UseSharedCompilation=false --no-build --filter 'FullyQualifiedName~StdioTests.RealServerInitializesDiscoversOnlyReadOnlyToolsAndInvokesSnapshotOverStdio' -- NUnit.ExplicitMode=None
            if ($LASTEXITCODE -ne 0) { throw 'Published MCP stdio validation failed.' }
        }
        finally { Pop-Location }
    }
    $codexHome = Join-Path $run 'codex home'
    New-Item -ItemType Directory -Path $codexHome -Force | Out-Null
    $codex = (Get-Command codex -CommandType ExternalScript | Select-Object -First 1).Source
    $codexEnvironment = @{ CODEX_HOME = $codexHome }
    $codexAdded = Invoke-IsolatedClient $codex @('mcp', 'add', 'winmon', '--', $server) $codexEnvironment
    $codexListed = Invoke-IsolatedClient $codex @('mcp', 'list') $codexEnvironment
    Save-ClientOutput 'codex-add.txt' $codexAdded
    Save-ClientOutput 'codex-list.txt' $codexListed
    if ($codexAdded -notmatch 'winmon' -or $codexListed -notmatch 'winmon') { throw 'Codex did not retain the isolated MCP registration.' }

    $claudeDirectory = Join-Path $run 'claude config'
    New-Item -ItemType Directory -Path $claudeDirectory -Force | Out-Null
    $claude = (Get-Command claude -CommandType ExternalScript | Select-Object -First 1).Source
    $claudeEnvironment = @{ CLAUDE_CONFIG_DIR = $claudeDirectory; WINMON_MCP_EXE = $server }
    $claudeAdded = Invoke-IsolatedClient $claude @('mcp', 'add', '--scope', 'user', 'winmon', '--', $server) $claudeEnvironment
    $claudeListed = Invoke-IsolatedClient $claude @('mcp', 'list') $claudeEnvironment
    $claudeHealth = Invoke-IsolatedClient $claude @('mcp', 'get', 'winmon') $claudeEnvironment
    Save-ClientOutput 'claude-add.txt' $claudeAdded
    Save-ClientOutput 'claude-list.txt' $claudeListed
    Save-ClientOutput 'claude-get.txt' $claudeHealth
    if ($claudeAdded -notmatch 'winmon' -or $claudeListed -notmatch '(?m)^winmon:.*[√✓]\s*Connected' -or
        $claudeHealth -notmatch '(?m)^winmon:' -or $claudeHealth -notmatch '(?m)^\s*Status:\s*[√✓]\s*Connected') {
        throw 'Claude Code did not retain or health-check the isolated MCP registration.'
    }
    if ($ValidatePlugin) {
        $pluginDirectory = Join-Path $run 'claude plugin config'
        New-Item -ItemType Directory -Path $pluginDirectory -Force | Out-Null
        $pluginEnvironment = @{ CLAUDE_CONFIG_DIR = $pluginDirectory; WINMON_MCP_EXE = $server }
        $pluginValidation = Invoke-IsolatedClient $claude @('plugin', 'validate', '--strict', $plugin) $pluginEnvironment
        $pluginListed = Invoke-IsolatedClient $claude @('--plugin-dir', $plugin, 'mcp', 'list') $pluginEnvironment
        $pluginHealth = Invoke-IsolatedClient $claude @('--plugin-dir', $plugin, 'mcp', 'get', 'plugin:winmon:winmon') $pluginEnvironment
        Save-ClientOutput 'claude-plugin-validate.txt' $pluginValidation
        Save-ClientOutput 'claude-plugin-list.txt' $pluginListed
        Save-ClientOutput 'claude-plugin-get.txt' $pluginHealth
        if ($pluginListed -notmatch '(?m)^plugin:winmon:winmon:.*[√✓]\s*Connected' -or
            $pluginHealth -notmatch '(?m)^plugin:winmon:winmon:' -or $pluginHealth -notmatch '(?m)^\s*Status:\s*[√✓]\s*Connected') {
            throw 'Claude Code did not discover and health-check the isolated plugin MCP server.'
        }
    }
    Write-Host "PASS: published MCP server initialized, discovered six tools and served a tool from '$serverDirectory' without the host DOTNET_ROOT. Evidence: $evidence"
}
finally {
    [Environment]::SetEnvironmentVariable('WINMON_MCP_TEST_EXE', $savedExecutable, 'Process')
}
