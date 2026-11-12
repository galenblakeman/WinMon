#requires -Version 7.4
[CmdletBinding()]
param([Parameter(Mandatory)][string]$InstallerPath)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$InstallerPath = (Resolve-Path -LiteralPath $InstallerPath).Path
$uninstallKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{8C69DAA2-DA6F-4952-BBE8-8AD1FDC6E2B9}_is1'
$environmentKey = 'HKCU:\Environment'
$ownershipKey = 'HKCU:\Software\WinMon'
if (Test-Path $uninstallKey) { throw 'CLI installer validation requires a profile without WinMon CLI. Use a clean VM/CI runner.' }
$evidence = Join-Path $root "artifacts/installer-validation/$([guid]::NewGuid().ToString('N'))"
$directory = Join-Path $evidence 'WinMon CLI'
New-Item -ItemType Directory -Path $evidence -Force | Out-Null
function Invoke-Checked([string]$File, [string[]]$Arguments, [switch]$ExpectFailure) {
    $process = Start-Process -FilePath $File -ArgumentList $Arguments -WindowStyle Hidden -PassThru
    $null = $process.Handle
    if (!$process.WaitForExit(120000)) { throw "Installer timed out: $File" }
    if (($process.ExitCode -eq 0) -eq [bool]$ExpectFailure) { throw "Unexpected installer exit $($process.ExitCode): $File" }
}
$arguments = @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', "/DIR=`"$directory`"")
$originalPath = [string](Get-ItemProperty -Path $environmentKey -Name Path -ErrorAction SilentlyContinue).Path
$installed = $false
try {
    $conflict = Join-Path $evidence 'conflicting desktop'
    New-Item -ItemType Directory -Path $conflict | Out-Null
    Set-Content -LiteralPath (Join-Path $conflict 'WinMon.exe') -Value 'unrelated desktop fixture'
    Invoke-Checked $InstallerPath @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', "/DIR=`"$conflict`"", "/LOG=`"$evidence/conflict.log`"") -ExpectFailure
    if (Test-Path (Join-Path $conflict 'WinMon.Cli.exe')) { throw 'CLI conflict rejection wrote a payload.' }
    Invoke-Checked $InstallerPath ($arguments + "/LOG=`"$evidence/install.log`"")
    $installed = $true
    $executable = Join-Path $directory 'WinMon.Cli.exe'
    if (!(Test-Path $executable) -or !(Test-Path $uninstallKey)) { throw 'CLI installation is incomplete.' }
    $registeredVersion = [string](Get-ItemProperty -LiteralPath $uninstallKey -Name DisplayVersion).DisplayVersion
    $installedHash = (Get-FileHash -LiteralPath $executable).Hash
    try {
        Set-ItemProperty -LiteralPath $uninstallKey -Name DisplayVersion -Value '999.0.0'
        Invoke-Checked $InstallerPath ($arguments + "/LOG=`"$evidence/downgrade.log`"") -ExpectFailure
        if ((Get-FileHash -LiteralPath $executable).Hash -ne $installedHash) { throw 'Rejected downgrade modified the CLI.' }
    }
    finally { Set-ItemProperty -LiteralPath $uninstallKey -Name DisplayVersion -Value $registeredVersion }
    if ([string](Get-ItemProperty -Path $environmentKey -Name Path -ErrorAction SilentlyContinue).Path -ne $originalPath) { throw 'Default CLI installation changed PATH.' }
    Invoke-Checked $executable @('snapshot', '--top', '1', '--no-color')
    Invoke-Checked $InstallerPath ($arguments + '/TASKS="clipath"' + "/LOG=`"$evidence/repair.log`"")
    $currentPath = [string](Get-ItemProperty -Path $environmentKey -Name Path -ErrorAction Stop).Path
    if ($currentPath -notmatch [regex]::Escape($directory) -or [string](Get-ItemProperty -Path $ownershipKey -Name CliStandalonePathDirectory -ErrorAction Stop).CliStandalonePathDirectory -ne $directory) { throw 'CLI PATH task did not record its own entry.' }
    Invoke-Checked (Join-Path $directory 'unins000.exe') @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART')
    $installed = $false
    if ((Test-Path $executable) -or (Test-Path $uninstallKey) -or [string](Get-ItemProperty -Path $environmentKey -Name Path -ErrorAction SilentlyContinue).Path -ne $originalPath -or (Get-ItemProperty -Path $ownershipKey -Name CliStandalonePathDirectory -ErrorAction SilentlyContinue)) { throw 'CLI uninstall did not remove its owned files and PATH entry.' }
    Write-Host "CLI installer validation passed. Evidence: $evidence"
}
finally { if ($installed -and (Test-Path (Join-Path $directory 'unins000.exe'))) { Invoke-Checked (Join-Path $directory 'unins000.exe') @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART') } }
