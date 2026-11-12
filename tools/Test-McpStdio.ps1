#requires -Version 7.4
<#
.SYNOPSIS
Checks a published or installed WinMon MCP server without requiring an SDK or agent client.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Executable,
    [Parameter(Mandatory)][string]$OutputDirectory
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$Executable = (Resolve-Path -LiteralPath $Executable).Path
if ((Split-Path $Executable -Leaf) -cne 'WinMon.Mcp.exe') { throw 'Expected WinMon.Mcp.exe.' }
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$OutputDirectory = (Resolve-Path -LiteralPath $OutputDirectory).Path
$process = [Diagnostics.Process]::new()
$process.StartInfo = [Diagnostics.ProcessStartInfo]::new($Executable)
$process.StartInfo.UseShellExecute = $false
$process.StartInfo.CreateNoWindow = $true
$process.StartInfo.WorkingDirectory = Split-Path $Executable -Parent
$process.StartInfo.RedirectStandardInput = $true
$process.StartInfo.RedirectStandardOutput = $true
$process.StartInfo.RedirectStandardError = $true
$process.StartInfo.Environment['DOTNET_ROOT'] = Join-Path $OutputDirectory 'no-dotnet-runtime'
$process.StartInfo.Environment['DOTNET_ROOT_X64'] = Join-Path $OutputDirectory 'no-dotnet-runtime'
$process.StartInfo.Environment['DOTNET_MULTILEVEL_LOOKUP'] = '0'
$started = $false

function Invoke-McpRequest([int]$Id, [string]$Method, [hashtable]$Parameters) {
    $process.StandardInput.WriteLine((@{ jsonrpc = '2.0'; id = $Id; method = $Method; params = $Parameters } |
        ConvertTo-Json -Compress -Depth 10))
    $process.StandardInput.Flush()
    $timeout = [Threading.CancellationTokenSource]::new([TimeSpan]::FromSeconds(15))
    try {
        while ($true) {
            $line = $process.StandardOutput.ReadLineAsync().WaitAsync($timeout.Token).GetAwaiter().GetResult()
            if ($null -eq $line) { throw "MCP server closed stdout before $Method completed." }
            $message = $line | ConvertFrom-Json -AsHashtable
            if (!$message.ContainsKey('id')) { continue }
            if ($message.id -ne $Id) { throw "Unexpected MCP response id for $Method." }
            if ($message.ContainsKey('error')) { throw "MCP request failed: $Method" }
            return $message.result
        }
    }
    finally { $timeout.Dispose() }
}

try {
    if (!$process.Start()) { throw 'Could not start WinMon MCP.' }
    $started = $true
    $stderr = $process.StandardError.ReadToEndAsync()
    $initialization = Invoke-McpRequest 1 'initialize' @{ protocolVersion = '2025-06-18'; capabilities = @{}; clientInfo = @{ name = 'WinMon stdio acceptance'; version = '1.0' } }
    if (!$initialization.protocolVersion) { throw 'MCP initialize response lacks a protocol version.' }
    $process.StandardInput.WriteLine('{"jsonrpc":"2.0","method":"notifications/initialized"}')
    $process.StandardInput.Flush()
    $tools = Invoke-McpRequest 2 'tools/list' @{}
    $names = @($tools.tools | ForEach-Object name)
    if ((@($names | Sort-Object) -join ',') -cne 'get_network_connections,get_process_details,get_process_network_traffic,get_system_snapshot,list_processes,sample_performance') {
        throw 'MCP tool discovery did not match the six intended tools.'
    }
    foreach ($tool in $tools.tools) {
        if ($tool.annotations.readOnlyHint -ne $true -or !$tool.outputSchema) { throw 'MCP tool is missing its read-only annotation or output schema.' }
    }
    $snapshot = Invoke-McpRequest 3 'tools/call' @{ name = 'get_system_snapshot'; arguments = @{} }
    if ($snapshot.isError -eq $true -or $snapshot.structuredContent.schemaVersion -ne 1) { throw 'MCP snapshot response is invalid.' }
    $process.StandardInput.Close()
    if (!$process.WaitForExit(10000)) { throw 'MCP server did not exit after stdin closed.' }
    if ($process.ExitCode -ne 0) { throw "MCP server exited with code $($process.ExitCode)." }
    $initialization | ConvertTo-Json -Depth 10 | Set-Content (Join-Path $OutputDirectory 'initialize.json') -Encoding utf8NoBOM
    $tools | ConvertTo-Json -Depth 30 | Set-Content (Join-Path $OutputDirectory 'tools.json') -Encoding utf8NoBOM
    $snapshot | ConvertTo-Json -Depth 20 | Set-Content (Join-Path $OutputDirectory 'snapshot.json') -Encoding utf8NoBOM
    $stderr.GetAwaiter().GetResult() | Set-Content (Join-Path $OutputDirectory 'stderr.txt') -Encoding utf8NoBOM
    Write-Host "PASS MCP stdio initialization, discovery, snapshot and shutdown. Evidence: $OutputDirectory"
}
finally {
    if ($started -and !$process.HasExited) { $process.Kill($true); $null = $process.WaitForExit(5000) }
    $process.Dispose()
}
