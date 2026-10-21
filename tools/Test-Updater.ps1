#requires -Version 7.4
[CmdletBinding()]
param(
    [ValidatePattern('^\d+\.\d+\.\d+$')][string]$CurrentVersion = '1.0.2',
    [ValidatePattern('^\d+\.\d+\.\d+$')][string]$UpdateVersion = '1.0.3',
    [switch]$IncludeRollback,
    [switch]$IncludeInstaller
)

<#
.SYNOPSIS
Runs isolated portable updater integration checks.

.DESCRIPTION
The default validates cancellation, signature rejection, and a successful portable update.
IncludeRollback additionally verifies replacement rollback after an updated executable exits.
IncludeInstaller builds and installs isolated per-user fixtures with the pinned Inno Setup compiler.
The run root and transaction logs are retained; only the ephemeral signing private key is removed.
#>

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$run = Join-Path $root ('artifacts/updater-validation/' + [guid]::NewGuid().ToString('N'))
$localUpdateRoot = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)) 'WinMon/Updates'
$parent = $null
$updated = $null
$fixtureTarget = $null
$fixtureUninstaller = $null
$events = [Collections.Generic.List[Threading.EventWaitHandle]]::new()
$evidence = [ordered]@{ run = $run; currentVersion = $CurrentVersion; updateVersion = $UpdateVersion; checks = [ordered]@{} }

function Wait-ForEvent([Threading.EventWaitHandle]$event, [int]$seconds, [string]$description) {
    if (!$event.WaitOne([TimeSpan]::FromSeconds($seconds))) { throw "Timed out waiting for $description." }
}

function Wait-ForExit([Diagnostics.Process]$process, [int]$seconds, [string]$description) {
    if (!$process.WaitForExit($seconds * 1000)) { throw "Timed out waiting for $description." }
}

function Assert-OwnProcess([Diagnostics.Process]$process, [string]$target) {
    if ($process.HasExited) { throw 'Expected WinMon process exited unexpectedly.' }
    $path = $process.MainModule.FileName
    if (![string]::Equals([IO.Path]::GetFullPath($path), [IO.Path]::GetFullPath($target), [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to control a process outside the portable test fixture: $path"
    }
}

function Assert-UnderRun([string]$path) {
    $fullPath = [IO.Path]::GetFullPath($path)
    $runPath = [IO.Path]::GetFullPath($run).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    if (!$fullPath.StartsWith($runPath, [StringComparison]::OrdinalIgnoreCase)) { throw "Refusing to modify a fixture outside $runPath" }
}

function Stop-OwnedProcess([Diagnostics.Process]$process, [string]$target) {
    if ($null -eq $process -or $process.HasExited) { return }
    Assert-OwnProcess $process $target
    [void]$process.CloseMainWindow()
    if (!$process.WaitForExit(15000)) {
        Assert-OwnProcess $process $target
        $process.Kill()
        Wait-ForExit $process 15 'owned WinMon process shutdown'
    }
}

function New-Transaction([Diagnostics.Process]$owner, [string]$target, [string]$archive, [string]$manifestSource, [string]$signatureSource, [string]$version, [bool]$Installed = $false, [switch]$Tamper) {
    $token = [guid]::NewGuid().ToString('N')
    $directory = Join-Path $localUpdateRoot $token
    New-Item -ItemType Directory -Path $directory -Force | Out-Null
    Copy-Item -LiteralPath $archive -Destination (Join-Path $directory ([IO.Path]::GetFileName($archive)))
    Copy-Item -LiteralPath $manifestSource -Destination (Join-Path $directory 'update-manifest.json')
    Copy-Item -LiteralPath $signatureSource -Destination (Join-Path $directory 'update-manifest.sig')
    if ($Tamper) { Add-Content -LiteralPath (Join-Path $directory 'update-manifest.sig') -Value 'A' -NoNewline }
    Copy-Item -LiteralPath $target -Destination (Join-Path $directory 'WinMon.Update.exe')
    $job = [ordered]@{
        Target = [IO.Path]::GetFullPath($target)
        ParentId = $owner.Id
        ParentStartTicks = $owner.StartTime.ToUniversalTime().Ticks
        Version = $version
        Installed = $Installed
        Token = $token
    }
    $json = $job | ConvertTo-Json -Compress
    [IO.File]::WriteAllText((Join-Path $directory 'job.json'), $json, [Text.UTF8Encoding]::new($false))
    return [pscustomobject]@{ Directory = $directory; Token = $token }
}

function Find-OwnedProcess([string]$target, [int]$seconds) {
    $deadline = [DateTime]::UtcNow.AddSeconds($seconds)
    do {
        Start-Sleep -Milliseconds 250
        $candidate = Get-Process -Name WinMon -ErrorAction SilentlyContinue | Where-Object { !$_.HasExited } | Select-Object -First 1
        if ($null -ne $candidate) {
            try { Assert-OwnProcess $candidate $target; return $candidate }
            catch { $candidate.Dispose(); throw }
        }
    } while ([DateTime]::UtcNow -lt $deadline)
    throw "Timed out waiting for owned WinMon process $target."
}

function New-Installer([string]$compiler, [string]$version, [string]$publish, [string]$output) {
    New-Item -ItemType Directory -Path $output -Force | Out-Null
    & $compiler "/DAppVersion=$version" "/DPublishDirectory=$publish" "/DReleaseDirectory=$output" (Join-Path $root 'packaging/WinMon.iss') | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "Building $version installer fixture failed." }
    $installer = Join-Path $output "WinMon-$version-Setup-x64.exe"
    if (!(Test-Path -LiteralPath $installer -PathType Leaf)) { throw "Installer fixture was not created: $installer" }
    return $installer
}

function Start-Helper($transaction) {
    $arguments = @('--apply-update', ('"{0}"' -f $transaction.Directory), $transaction.Token)
    Start-Process -FilePath (Join-Path $transaction.Directory 'WinMon.Update.exe') -ArgumentList $arguments -WorkingDirectory $transaction.Directory -WindowStyle Hidden -PassThru
}

function Open-Event([string]$token, [string]$kind) {
    $event = [Threading.EventWaitHandle]::new($false, [Threading.EventResetMode]::ManualReset, "Local\WinMon.Update.$token.$kind")
    $events.Add($event)
    return $event
}

try {
    if (Get-Process -Name WinMon -ErrorAction SilentlyContinue) { throw 'Refusing updater validation while a WinMon process already exists.' }
    $uninstallKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{D736D24F-6A1E-4FD4-AC66-790F93DC61B4}_is1'
    if (Test-Path -LiteralPath $uninstallKey) { throw 'Refusing updater validation while a WinMon installer registration exists.' }
    $startMenuLink = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::Programs)) 'WinMon/WinMon.lnk'
    if ($IncludeInstaller -and (Test-Path -LiteralPath $startMenuLink)) { throw 'Refusing installer updater validation while a WinMon Start menu entry already exists.' }

    New-Item -ItemType Directory -Path $run, $localUpdateRoot -Force | Out-Null
    $privateKey = Join-Path $run 'winmon.update-private.pem'
    $publicKey = Join-Path $run 'winmon.update-public.pem'
    $rsa = [Security.Cryptography.RSA]::Create(2048)
    try {
        [IO.File]::WriteAllText($privateKey, $rsa.ExportPkcs8PrivateKeyPem(), [Text.UTF8Encoding]::new($false))
        [IO.File]::WriteAllText($publicKey, $rsa.ExportSubjectPublicKeyInfoPem(), [Text.UTF8Encoding]::new($false))
    }
    finally { $rsa.Dispose() }

    $build = Join-Path $run 'build'
    $oldPublish = Join-Path $run 'old-publish'
    $newPublish = Join-Path $run 'new-publish'
    foreach ($publish in @($oldPublish, $newPublish)) { New-Item -ItemType Directory -Path $publish -Force | Out-Null }
    Push-Location $root
    try {
        dotnet publish src/WinMon/WinMon.csproj -c Release -r win-x64 -m:1 -p:UseSharedCompilation=false -p:NuGetAudit=false -p:Version=$CurrentVersion -p:UpdatePublicKeyPath=$publicKey --artifacts-path $build -o $oldPublish
        if ($LASTEXITCODE -ne 0) { throw 'Publishing the current portable fixture failed.' }
        dotnet publish src/WinMon/WinMon.csproj -c Release -r win-x64 -m:1 -p:UseSharedCompilation=false -p:NuGetAudit=false -p:Version=$UpdateVersion -p:UpdatePublicKeyPath=$publicKey --artifacts-path $build -o $newPublish
        if ($LASTEXITCODE -ne 0) { throw 'Publishing the updated portable fixture failed.' }
    }
    finally { Pop-Location }

    $portable = Join-Path $run 'portable'
    New-Item -ItemType Directory -Path $portable -Force | Out-Null
    $target = Join-Path $portable 'WinMon.exe'
    Copy-Item -LiteralPath (Join-Path $oldPublish 'WinMon.exe') -Destination $target
    New-Item -ItemType File -Path (Join-Path $portable 'portable.flag') | Out-Null
    $settings = '{"AutomaticallyCheckForUpdates":false,"StartWithWindows":false,"LaunchWithShortcut":false,"ShowShortcut":"None","MinimizeToTray":false}'
    [IO.File]::WriteAllText((Join-Path $portable 'settings.json'), $settings, [Text.UTF8Encoding]::new($false))
    $payload = Join-Path $run 'payload'
    New-Item -ItemType Directory -Path $payload -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $newPublish 'WinMon.exe') -Destination (Join-Path $payload 'WinMon.exe')
    New-Item -ItemType File -Path (Join-Path $payload 'portable.flag') | Out-Null
    $archive = Join-Path $run "WinMon-$UpdateVersion-win-x64.zip"
    Compress-Archive -LiteralPath (Join-Path $payload 'WinMon.exe'), (Join-Path $payload 'portable.flag') -DestinationPath $archive
    $installer = Join-Path $run "WinMon-$UpdateVersion-Setup-x64.exe"
    [IO.File]::WriteAllBytes($installer, [byte[]](0,1,2))
    & (Join-Path $root 'tools/New-UpdateManifest.ps1') -Version $UpdateVersion -PortablePath $archive -InstallerPath $installer -SigningKeyPath $privateKey -OutputDirectory $run

    $parent = Start-Process -FilePath $target -WorkingDirectory $portable -WindowStyle Hidden -PassThru
    Start-Sleep -Seconds 2
    Assert-OwnProcess $parent $target
    $oldHash = (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash

    $cancel = New-Transaction $parent $target $archive (Join-Path $run 'update-manifest.json') (Join-Path $run 'update-manifest.sig') $UpdateVersion
    $ready = Open-Event $cancel.Token 'Ready'; $commit = Open-Event $cancel.Token 'Commit'; $cancelEvent = Open-Event $cancel.Token 'Cancel'
    $cancelHelper = Start-Helper $cancel
    Wait-ForEvent $ready 30 'cancel updater readiness'
    $cancelEvent.Set()
    Wait-ForExit $cancelHelper 30 'cancel updater'
    Assert-OwnProcess $parent $target
    if ((Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash -ne $oldHash) { throw 'Cancellation changed the portable executable.' }
    $evidence.checks.cancel = 'passed'

    $tampered = New-Transaction $parent $target $archive (Join-Path $run 'update-manifest.json') (Join-Path $run 'update-manifest.sig') $UpdateVersion -Tamper
    $tamperReady = Open-Event $tampered.Token 'Ready'; $tamperHelper = Start-Helper $tampered
    Wait-ForExit $tamperHelper 30 'tampered updater rejection'
    if ($tamperReady.WaitOne(0)) { throw 'Tampered signature reached updater readiness.' }
    Assert-OwnProcess $parent $target
    $evidence.checks.tamperedSignature = 'passed'

    if ($IncludeRollback) {
        $rollbackSource = Join-Path $run 'rollback-source'
        $rollbackProject = Join-Path $rollbackSource 'Rollback.csproj'
        $rollbackProgram = Join-Path $rollbackSource 'Program.cs'
        New-Item -ItemType Directory -Path $rollbackSource -Force | Out-Null
        [IO.File]::WriteAllText($rollbackProject, @"
<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0-windows</TargetFramework><AssemblyName>WinMon</AssemblyName><Version>$UpdateVersion</Version><FileVersion>$UpdateVersion</FileVersion><SelfContained>true</SelfContained><RuntimeIdentifier>win-x64</RuntimeIdentifier><PublishSingleFile>true</PublishSingleFile></PropertyGroup></Project>
"@, [Text.UTF8Encoding]::new($false))
        [IO.File]::WriteAllText($rollbackProgram, 'return 1;', [Text.UTF8Encoding]::new($false))
        $rollbackPublish = Join-Path $run 'rollback-publish'
        dotnet publish $rollbackProject -c Release -r win-x64 -m:1 -p:UseSharedCompilation=false -p:NuGetAudit=false --artifacts-path $build -o $rollbackPublish
        if ($LASTEXITCODE -ne 0) { throw 'Publishing rollback fixture failed.' }
        $rollbackAssets = Join-Path $run 'rollback-assets'
        $rollbackPayload = Join-Path $rollbackAssets 'payload'
        New-Item -ItemType Directory -Path $rollbackPayload -Force | Out-Null
        Copy-Item -LiteralPath (Join-Path $rollbackPublish 'WinMon.exe') -Destination (Join-Path $rollbackPayload 'WinMon.exe')
        New-Item -ItemType File -Path (Join-Path $rollbackPayload 'portable.flag') | Out-Null
        $rollbackArchive = Join-Path $rollbackAssets "WinMon-$UpdateVersion-win-x64.zip"
        Compress-Archive -LiteralPath (Join-Path $rollbackPayload 'WinMon.exe'), (Join-Path $rollbackPayload 'portable.flag') -DestinationPath $rollbackArchive
        $rollbackInstaller = Join-Path $rollbackAssets "WinMon-$UpdateVersion-Setup-x64.exe"
        [IO.File]::WriteAllBytes($rollbackInstaller, [byte[]](0,1,2))
        & (Join-Path $root 'tools/New-UpdateManifest.ps1') -Version $UpdateVersion -PortablePath $rollbackArchive -InstallerPath $rollbackInstaller -SigningKeyPath $privateKey -OutputDirectory $rollbackAssets
        $rollback = New-Transaction $parent $target $rollbackArchive (Join-Path $rollbackAssets 'update-manifest.json') (Join-Path $rollbackAssets 'update-manifest.sig') $UpdateVersion
        $rollbackReady = Open-Event $rollback.Token 'Ready'; $rollbackCommit = Open-Event $rollback.Token 'Commit'; [void](Open-Event $rollback.Token 'Cancel')
        $rollbackHelper = Start-Helper $rollback
        Wait-ForEvent $rollbackReady 30 'rollback updater readiness'
        $rollbackCommit.Set()
        Stop-OwnedProcess $parent $target
        Wait-ForExit $rollbackHelper 90 'rollback updater'
        $rollbackState = Get-Content -LiteralPath (Join-Path $rollback.Directory 'state.json') -Raw | ConvertFrom-Json
        if ($rollbackState.Status -ne 'rolled-back') { throw "Rollback completed with state '$($rollbackState.Status)'." }
        if ((Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash -ne $oldHash -or [Diagnostics.FileVersionInfo]::GetVersionInfo($target).ProductVersion -ne $CurrentVersion) {
            throw 'Rollback did not restore the current portable executable.'
        }
        $parent = Find-OwnedProcess $target 20
        $evidence.checks.rollback = 'passed'
    }

    $success = New-Transaction $parent $target $archive (Join-Path $run 'update-manifest.json') (Join-Path $run 'update-manifest.sig') $UpdateVersion
    $successReady = Open-Event $success.Token 'Ready'; $successCommit = Open-Event $success.Token 'Commit'; [void](Open-Event $success.Token 'Cancel')
    $successHelper = Start-Helper $success
    Wait-ForEvent $successReady 30 'successful updater readiness'
    $successCommit.Set()
    Stop-OwnedProcess $parent $target
    Wait-ForExit $successHelper 90 'successful updater'
    $state = Get-Content -LiteralPath (Join-Path $success.Directory 'state.json') -Raw | ConvertFrom-Json
    if ($state.Status -ne 'succeeded') { throw "Updater completed with state '$($state.Status)'." }
    if ([Diagnostics.FileVersionInfo]::GetVersionInfo($target).ProductVersion -ne $UpdateVersion) { throw 'Updated executable version did not match the manifest.' }
    $updated = Find-OwnedProcess $target 20
    Assert-OwnProcess $updated $target
    $saved = Get-Content -LiteralPath (Join-Path $portable 'settings.json') -Raw | ConvertFrom-Json
    if ($saved.AutomaticallyCheckForUpdates -ne $false) { throw 'Portable settings sentinel was not preserved.' }
    $evidence.checks.success = 'passed'
    $evidence.target = $target
    $evidence.transaction = $success.Directory

    if ($IncludeInstaller) {
        Stop-OwnedProcess $updated $target
        $updated = $null
        $compiler = Join-Path $root 'artifacts/toolchain/innosetup-6.7.3/ISCC.exe'
        if (!(Test-Path -LiteralPath $compiler -PathType Leaf)) { throw "Pinned Inno Setup compiler is unavailable: $compiler" }
        $oldInstaller = New-Installer $compiler $CurrentVersion $oldPublish (Join-Path $run 'old-installer')
        $newInstaller = New-Installer $compiler $UpdateVersion $newPublish (Join-Path $run 'new-installer')
        $installDirectory = Join-Path $run 'installed'
        $fixtureUninstaller = Join-Path $installDirectory 'unins000.exe'
        $installArguments = @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/SP-', '/NORESTART', ('/DIR="{0}"' -f $installDirectory))
        $oldSetup = Start-Process -FilePath $oldInstaller -ArgumentList $installArguments -Wait -PassThru -WindowStyle Hidden
        if ($oldSetup.ExitCode -ne 0) { throw "Installing current fixture failed with exit code $($oldSetup.ExitCode)." }
        $installedTarget = Join-Path $installDirectory 'WinMon.exe'
        Assert-UnderRun $installedTarget
        $fixtureTarget = $installedTarget
        if (!(Test-Path -LiteralPath $installedTarget -PathType Leaf)) { throw 'Current installer fixture did not create WinMon.exe.' }
        New-Item -ItemType File -Path (Join-Path $installDirectory 'portable.flag') | Out-Null
        [IO.File]::WriteAllText((Join-Path $installDirectory 'settings.json'), $settings, [Text.UTF8Encoding]::new($false))
        $installedParent = Start-Process -FilePath $installedTarget -WorkingDirectory $installDirectory -WindowStyle Hidden -PassThru
        Start-Sleep -Seconds 2
        Assert-OwnProcess $installedParent $installedTarget
        $installerAssets = Join-Path $run 'installer-assets'
        New-Item -ItemType Directory -Path $installerAssets -Force | Out-Null
        Copy-Item -LiteralPath $archive -Destination (Join-Path $installerAssets ([IO.Path]::GetFileName($archive)))
        Copy-Item -LiteralPath $newInstaller -Destination (Join-Path $installerAssets ([IO.Path]::GetFileName($newInstaller)))
        & (Join-Path $root 'tools/New-UpdateManifest.ps1') -Version $UpdateVersion -PortablePath (Join-Path $installerAssets ([IO.Path]::GetFileName($archive))) -InstallerPath (Join-Path $installerAssets ([IO.Path]::GetFileName($newInstaller))) -SigningKeyPath $privateKey -OutputDirectory $installerAssets
        $installed = New-Transaction $installedParent $installedTarget (Join-Path $installerAssets ([IO.Path]::GetFileName($newInstaller))) (Join-Path $installerAssets 'update-manifest.json') (Join-Path $installerAssets 'update-manifest.sig') $UpdateVersion $true
        $installedReady = Open-Event $installed.Token 'Ready'; $installedCommit = Open-Event $installed.Token 'Commit'; [void](Open-Event $installed.Token 'Cancel')
        $installedHelper = Start-Helper $installed
        Wait-ForEvent $installedReady 30 'installer updater readiness'
        $installedCommit.Set()
        Stop-OwnedProcess $installedParent $installedTarget
        Wait-ForExit $installedHelper 330 'installer updater'
        $installedState = Get-Content -LiteralPath (Join-Path $installed.Directory 'state.json') -Raw | ConvertFrom-Json
        if ($installedState.Status -ne 'succeeded' -or [Diagnostics.FileVersionInfo]::GetVersionInfo($installedTarget).ProductVersion -ne $UpdateVersion) { throw 'Installer updater did not install the requested version.' }
        $registration = Get-ItemProperty -LiteralPath $uninstallKey
        if ($registration.DisplayVersion -ne $UpdateVersion) { throw 'Installer registration version was not updated.' }
        $installedChild = Find-OwnedProcess $installedTarget 20
        Stop-OwnedProcess $installedChild $installedTarget
        $uninstaller = Join-Path $installDirectory 'unins000.exe'
        Assert-UnderRun $uninstaller
        if (!(Test-Path -LiteralPath $uninstaller -PathType Leaf)) { throw 'Fixture uninstaller was not created.' }
        $fixtureUninstaller = $uninstaller
        $uninstall = Start-Process -FilePath $uninstaller -ArgumentList '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART' -Wait -PassThru -WindowStyle Hidden
        if ($uninstall.ExitCode -ne 0) { throw "Fixture uninstall failed with exit code $($uninstall.ExitCode)." }
        if (Test-Path -LiteralPath $uninstallKey) { throw 'Fixture uninstaller left the WinMon registration behind.' }
        $evidence.checks.installer = 'passed'
        $evidence.installerTransaction = $installed.Directory
    }
    $evidence.status = 'passed'
}
catch {
    $evidence.status = 'failed'
    $evidence.error = $_.Exception.Message
    throw
}
finally {
    try { if ($updated) { Stop-OwnedProcess $updated (Join-Path $run 'portable/WinMon.exe') } }
    catch { Write-Warning "Could not close the updated test process: $($_.Exception.Message)" }
    try { if ($parent) { Stop-OwnedProcess $parent (Join-Path $run 'portable/WinMon.exe') } }
    catch { Write-Warning "Could not close the original test process: $($_.Exception.Message)" }
    try {
        if ($fixtureTarget -and (Test-Path -LiteralPath $fixtureTarget)) {
            $fixtureProcess = Get-Process -Name WinMon -ErrorAction SilentlyContinue | Where-Object { !$_.HasExited } | Select-Object -First 1
            if ($fixtureProcess) { Stop-OwnedProcess $fixtureProcess $fixtureTarget }
        }
        if ($fixtureUninstaller -and (Test-Path -LiteralPath $fixtureUninstaller)) {
            Assert-UnderRun $fixtureUninstaller
            [void](Start-Process -FilePath $fixtureUninstaller -ArgumentList '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART' -Wait -PassThru -WindowStyle Hidden)
        }
    }
    catch { Write-Warning "Could not clean up the installer fixture: $($_.Exception.Message)" }
    foreach ($event in $events) { $event.Dispose() }
    if (Test-Path -LiteralPath (Join-Path $run 'winmon.update-private.pem')) { Remove-Item -LiteralPath (Join-Path $run 'winmon.update-private.pem') -Force }
    if (Test-Path -LiteralPath $run) {
        [IO.File]::WriteAllText((Join-Path $run 'evidence.json'), ($evidence | ConvertTo-Json -Depth 5), [Text.UTF8Encoding]::new($false))
        Write-Host "Updater validation evidence: $run"
    }
}
