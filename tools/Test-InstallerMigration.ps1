#requires -Version 7.4
[CmdletBinding()]
param(
    [string]$InnoCompiler = (Join-Path $PSScriptRoot '../artifacts/toolchain/innosetup-6.7.3/ISCC.exe'),
    [Parameter(Mandatory)][string]$PublishDirectory
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
[xml]$props = Get-Content -LiteralPath (Join-Path $root 'Directory.Build.props') -Raw
$version = [string]$props.Project.PropertyGroup.Version
$InnoCompiler = (Resolve-Path -LiteralPath $InnoCompiler).Path
$PublishDirectory = (Resolve-Path -LiteralPath $PublishDirectory).Path
$run = Join-Path $root "artifacts/installer-migration/$([guid]::NewGuid().ToString('N'))"
$installerDirectory = Join-Path $run 'installer'
$installDirectory = Join-Path $run 'desktop'
$scriptDirectory = Join-Path $run 'packaging'
$script = Join-Path $scriptDirectory 'WinMon.Migration.iss'
$environmentKey = 'HKCU:\Environment'
$ownershipKey = 'HKCU:\Software\WinMon'
if ((Get-ItemProperty -Path $ownershipKey -Name CliPathDirectory -ErrorAction SilentlyContinue) -or
    (Get-ItemProperty -Path $ownershipKey -Name CliStandalonePathDirectory -ErrorAction SilentlyContinue)) {
    throw 'Migration validation requires no existing WinMon CLI PATH ownership records.'
}

New-Item -ItemType Directory -Path $run, $scriptDirectory, $installerDirectory -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $root 'LICENSE') -Destination (Join-Path $run 'LICENSE')
New-Item -ItemType Directory -Path (Join-Path $run 'src/WinMon/Assets') -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $root 'src/WinMon/Assets/WinMon.ico') -Destination (Join-Path $run 'src/WinMon/Assets/WinMon.ico')
$appId = [guid]::NewGuid().ToString().ToUpperInvariant()
$contents = Get-Content -LiteralPath (Join-Path $root 'packaging/WinMon.iss') -Raw
$contents = $contents.Replace('AppId={{D736D24F-6A1E-4FD4-AC66-790F93DC61B4}', ('AppId={{' + $appId + '}'))
$contents = $contents.Replace('AppMutex=Local\WinMon.CpuMonitor', "AppMutex=Local\WinMon.Migration.$appId")
$contents = [regex]::Replace($contents, '(?ms)^\[Icons\].*?^\[Code\]', '[Code]')
Set-Content -LiteralPath $script -Value $contents -Encoding utf8NoBOM

& $InnoCompiler "/DAppVersion=$version" "/DPublishDirectory=$PublishDirectory" '/DDesktopDirectoryConflict=Conflict' '/DDowngradeBlocked=Downgrade' "/DReleaseDirectory=$installerDirectory" '/DNetworkServiceSetup=0' $script | Out-Host
if ($LASTEXITCODE -ne 0) { throw 'Test desktop installer compilation failed.' }
$installer = Join-Path $installerDirectory "WinMon-$version-Setup-x64.exe"
function Invoke-Checked([string]$File, [string[]]$Arguments) {
    $process = Start-Process -FilePath $File -ArgumentList $Arguments -WindowStyle Hidden -PassThru
    $null = $process.Handle
    if (!$process.WaitForExit(120000) -or $process.ExitCode -ne 0) { throw "Process failed: $File" }
}
$environmentRegistry = Get-Item -LiteralPath $environmentKey
$hadPath = $environmentRegistry.GetValueNames() -contains 'Path'
$originalKind = if ($hadPath) { $environmentRegistry.GetValueKind('Path') } else { 'ExpandString' }
$originalPath = [string]$environmentRegistry.GetValue('Path', '', [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)
$standaloneDirectory = Join-Path $run 'standalone-cli'
$seeded = $false
$installed = $false
try {
    Invoke-Checked $installer @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', "/DIR=`"$installDirectory`"", "/LOG=`"$run/install.log`"")
    $installed = $true
    if (!(Test-Path (Join-Path $installDirectory 'WinMon.exe'))) { throw 'Test desktop installer did not install the desktop executable.' }
    $pathItems = @($originalPath, $standaloneDirectory, $installDirectory) | Where-Object { $_ }
    $seededPath = $pathItems -join ';'
    $expectedPath = (@($originalPath, $standaloneDirectory) | Where-Object { $_ }) -join ';'
    if (!(New-Item -ItemType Directory -Path $standaloneDirectory -Force)) { throw 'Could not create standalone CLI fixture directory.' }
    if (!(Test-Path -LiteralPath $ownershipKey)) { New-Item -Path $ownershipKey | Out-Null }
    $seeded = $true
    Set-ItemProperty -Path $environmentKey -Name Path -Value $seededPath -Type ExpandString
    New-ItemProperty -Path $ownershipKey -Name CliPathDirectory -Value $installDirectory -PropertyType String -Force | Out-Null
    New-ItemProperty -Path $ownershipKey -Name CliStandalonePathDirectory -Value $standaloneDirectory -PropertyType String -Force | Out-Null
    Invoke-Checked (Join-Path $installDirectory 'unins000.exe') @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', "/LOG=`"$run/uninstall.log`"")
    $installed = $false
    $pathAfter = [string](Get-ItemProperty -Path $environmentKey -Name Path -ErrorAction SilentlyContinue).Path
    if ($pathAfter -ne $expectedPath) { throw 'Desktop uninstall did not remove exactly its legacy PATH entry.' }
    if (Get-ItemProperty -Path $ownershipKey -Name CliPathDirectory -ErrorAction SilentlyContinue) { throw 'Desktop uninstall left its legacy CLI PATH ownership record.' }
    if ([string](Get-ItemProperty -Path $ownershipKey -Name CliStandalonePathDirectory -ErrorAction Stop).CliStandalonePathDirectory -ne $standaloneDirectory) { throw 'Desktop uninstall modified standalone CLI ownership.' }
    Write-Host "Installer migration PATH validation passed. Evidence: $run"
}
finally {
    if ($installed -and (Test-Path (Join-Path $installDirectory 'unins000.exe'))) {
        try { Invoke-Checked (Join-Path $installDirectory 'unins000.exe') @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART') }
        catch { Write-Warning "Validation fixture cleanup failed: $_" }
    }
    if ($seeded) {
        $currentPath = [string](Get-Item -LiteralPath $environmentKey).GetValue('Path', '', [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)
        if ($currentPath -in @($seededPath, $expectedPath, $originalPath)) {
            if ($hadPath) { Set-ItemProperty -Path $environmentKey -Name Path -Value $originalPath -Type $originalKind }
            else { Remove-ItemProperty -Path $environmentKey -Name Path -ErrorAction SilentlyContinue }
        }
        else { Write-Warning 'PATH changed concurrently; preserved it for manual review.' }
        foreach ($entry in @{ CliPathDirectory = $installDirectory; CliStandalonePathDirectory = $standaloneDirectory }.GetEnumerator()) {
            if ([string](Get-ItemPropertyValue -Path $ownershipKey -Name $entry.Key -ErrorAction SilentlyContinue) -eq $entry.Value) {
                Remove-ItemProperty -Path $ownershipKey -Name $entry.Key -ErrorAction SilentlyContinue
            }
        }
    }
}
