#requires -Version 7.0
[CmdletBinding()]
param([Parameter(Mandatory)][string]$InstallerPath)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$InstallerPath = (Resolve-Path -LiteralPath $InstallerPath).Path
$uninstallKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{D736D24F-6A1E-4FD4-AC66-790F93DC61B4}_is1'
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$programs = [Environment]::GetFolderPath('Programs')
$launchShortcut = Join-Path $programs 'WinMon.lnk'
$installerShortcut = Join-Path $programs 'WinMon/WinMon.lnk'
if ((Test-Path $uninstallKey) -or (Test-Path $launchShortcut) -or (Test-Path (Join-Path $programs 'WinMon')) -or
    (Get-ItemProperty -Path $runKey -Name WinMon -ErrorAction SilentlyContinue)) {
    throw 'Installer lifecycle validation requires a profile without WinMon installation, startup entry or shortcuts. Use a clean VM/CI runner.'
}
$testDirectory = Join-Path $root "artifacts/installer-validation/$([guid]::NewGuid().ToString('N'))"
$installDirectory = Join-Path $testDirectory 'app'
New-Item -ItemType Directory -Path $testDirectory -Force | Out-Null
function Invoke-CheckedProcess([string]$File, [string[]]$Arguments, [switch]$ExpectFailure) {
    $process = Start-Process -FilePath $File -ArgumentList $Arguments -WindowStyle Hidden -PassThru
    if (!$process.WaitForExit(90000)) {
        $process.Kill()
        throw "Timed out: $File"
    }
    if (($process.ExitCode -eq 0) -eq [bool]$ExpectFailure) { throw "Unexpected exit code $($process.ExitCode): $File" }
}
$arguments = @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', "/DIR=`"$installDirectory`"")
$installed = $false
try {
    $mutex = [Threading.Mutex]::new($false, 'Local\WinMon.CpuMonitor')
    try {
        Invoke-CheckedProcess $InstallerPath ($arguments + "/LOG=`"$testDirectory/blocked.log`"") -ExpectFailure
        if (Test-Path $uninstallKey) { throw 'Installer ignored the running-app mutex.' }
    }
    finally { $mutex.Dispose() }
    Invoke-CheckedProcess $InstallerPath ($arguments + "/LOG=`"$testDirectory/install.log`"")
    $installed = $true
    $executable = Join-Path $installDirectory 'WinMon.exe'
    if (!(Test-Path $executable) -or !(Test-Path $installerShortcut) -or !(Test-Path $uninstallKey)) { throw 'Installation is incomplete.' }
    if (Test-Path (Join-Path $installDirectory 'portable.flag')) { throw 'Installer unexpectedly enabled portable mode.' }
    $registration = Get-ItemProperty $uninstallKey
    $expectedVersion = [string]([xml](Get-Content (Join-Path $root 'Directory.Build.props') -Raw)).Project.PropertyGroup.Version
    if ($registration.DisplayVersion -ne $expectedVersion) { throw 'Uninstall registration has the wrong version.' }
    $shell = New-Object -ComObject WScript.Shell
    if ($shell.CreateShortcut($installerShortcut).TargetPath -ne $executable) { throw 'Installer shortcut has the wrong target.' }
    Invoke-CheckedProcess $executable @('--smoke-output', "`"$testDirectory/smoke`"")
    if (!(Test-Path (Join-Path $testDirectory 'smoke/smoke.json'))) { throw 'Application smoke evidence is missing.' }
    # A same-version repair exercises the upgrade path without changing the release version.
    Set-Content (Join-Path $installDirectory 'settings.json') '{"Theme":"Light"}'
    New-Item -ItemType File -Path (Join-Path $installDirectory 'portable.flag') | Out-Null
    Invoke-CheckedProcess $InstallerPath ($arguments + "/LOG=`"$testDirectory/repair.log`"")
    if ((Get-Content (Join-Path $installDirectory 'settings.json') -Raw).Trim() -ne '{"Theme":"Light"}') { throw 'Repair changed portable preferences.' }
    if (!(Test-Path $runKey)) { New-Item -Path $runKey | Out-Null }
    New-ItemProperty -Path $runKey -Name WinMon -Value "`"$executable`"" -PropertyType String -Force | Out-Null
    $link = $shell.CreateShortcut($launchShortcut)
    $link.TargetPath = $executable
    $link.Save()
    Invoke-CheckedProcess (Join-Path $installDirectory 'unins000.exe') @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', "/LOG=`"$testDirectory/uninstall.log`"")
    $installed = $false
    if ((Test-Path $executable) -or (Test-Path $uninstallKey) -or (Test-Path $installerShortcut) -or (Test-Path $launchShortcut) -or
        (Get-ItemProperty -Path $runKey -Name WinMon -ErrorAction SilentlyContinue)) { throw 'Uninstall left application files or owned registration behind.' }
    if (!(Test-Path (Join-Path $installDirectory 'settings.json')) -or !(Test-Path (Join-Path $installDirectory 'portable.flag'))) { throw 'Uninstall removed portable preferences.' }
    Write-Host "Installer validation passed. Evidence: $testDirectory"
}
finally {
    if ($installed -and (Test-Path (Join-Path $installDirectory 'unins000.exe'))) {
        Invoke-CheckedProcess (Join-Path $installDirectory 'unins000.exe') @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART')
    }
}
