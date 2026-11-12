#requires -Version 7.4
[CmdletBinding()]
param([Parameter(Mandatory)][string]$InstallerPath)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$InstallerPath = (Resolve-Path -LiteralPath $InstallerPath).Path
$uninstallKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{9A78FCFC-CCEE-4D67-9FFB-CA1DA6E1DDE0}_is1'
if (Test-Path $uninstallKey) { throw 'MCP installer validation requires a profile without WinMon MCP. Use a clean VM/CI runner.' }
$evidence = Join-Path $root "artifacts/installer-validation/$([guid]::NewGuid().ToString('N'))"
$directory = Join-Path $evidence 'WinMon MCP'
New-Item -ItemType Directory -Path $evidence -Force | Out-Null
function Invoke-Checked([string]$File, [string[]]$Arguments, [switch]$ExpectFailure) {
    $process = Start-Process -FilePath $File -ArgumentList $Arguments -WindowStyle Hidden -PassThru
    $null = $process.Handle
    if (!$process.WaitForExit(120000)) { throw "Installer timed out: $File" }
    if (($process.ExitCode -eq 0) -eq [bool]$ExpectFailure) { throw "Unexpected installer exit $($process.ExitCode): $File" }
}
$arguments = @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', "/DIR=`"$directory`"")
$installed = $false
try {
    $conflict = Join-Path $evidence 'conflicting desktop'
    New-Item -ItemType Directory -Path $conflict | Out-Null
    Set-Content -LiteralPath (Join-Path $conflict 'WinMon.exe') -Value 'unrelated desktop fixture'
    Invoke-Checked $InstallerPath @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', "/DIR=`"$conflict`"", "/LOG=`"$evidence/conflict.log`"") -ExpectFailure
    if (Test-Path (Join-Path $conflict 'WinMon.Mcp.exe')) { throw 'MCP conflict rejection wrote a payload.' }
    Invoke-Checked $InstallerPath ($arguments + "/LOG=`"$evidence/install.log`"")
    $installed = $true
    $executable = Join-Path $directory 'WinMon.Mcp.exe'
    if (!(Test-Path $executable) -or !(Test-Path $uninstallKey)) { throw 'MCP installation is incomplete.' }
    $registeredVersion = [string](Get-ItemProperty -LiteralPath $uninstallKey -Name DisplayVersion).DisplayVersion
    $installedHash = (Get-FileHash -LiteralPath $executable).Hash
    try {
        Set-ItemProperty -LiteralPath $uninstallKey -Name DisplayVersion -Value '999.0.0'
        Invoke-Checked $InstallerPath ($arguments + "/LOG=`"$evidence/downgrade.log`"") -ExpectFailure
        if ((Get-FileHash -LiteralPath $executable).Hash -ne $installedHash) { throw 'Rejected downgrade modified MCP.' }
    }
    finally { Set-ItemProperty -LiteralPath $uninstallKey -Name DisplayVersion -Value $registeredVersion }
    & (Join-Path $PSScriptRoot 'Test-McpStdio.ps1') -Executable $executable -OutputDirectory (Join-Path $evidence 'stdio')
    Invoke-Checked $InstallerPath ($arguments + "/LOG=`"$evidence/repair.log`"")
    Invoke-Checked (Join-Path $directory 'unins000.exe') @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART')
    $installed = $false
    if ((Test-Path $executable) -or (Test-Path $uninstallKey)) { throw 'MCP uninstall did not remove its owned files.' }
    Write-Host "MCP installer validation passed. Evidence: $evidence"
}
finally { if ($installed -and (Test-Path (Join-Path $directory 'unins000.exe'))) { Invoke-Checked (Join-Path $directory 'unins000.exe') @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART') } }
