#requires -Version 7.0
[CmdletBinding()]
param(
    [string]$InnoCompiler,
    [string]$OutputDirectory,
    [string]$ExpectedTag,
    [string]$UpdateSigningKeyPath,
    [switch]$CodeSign,
    [string]$SigningEndpoint = 'https://eus.codesigning.azure.net/',
    [string]$SigningAccountName = 'galenwinmonsigning',
    [string]$CertificateProfileName = 'WinMonPublic',
    [string]$ExpectedPublisher = 'Galen Blakeman'
)
$ErrorActionPreference = 'Stop'
if ($CodeSign) { Import-Module ArtifactSigning -RequiredVersion 0.1.20 -ErrorAction Stop }
if ($CodeSign -and !$UpdateSigningKeyPath) { throw 'Signed releases require the update-signing key for the signed network-service package.' }
$signingParameters = @{
    Endpoint = $SigningEndpoint
    AccountName = $SigningAccountName
    CertificateProfileName = $CertificateProfileName
    ExpectedPublisher = $ExpectedPublisher
}
$root = Split-Path $PSScriptRoot -Parent
[xml]$props = Get-Content -LiteralPath (Join-Path $root 'Directory.Build.props') -Raw
$version = [string]$props.Project.PropertyGroup.Version
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw 'Release Version must have the form major.minor.patch.' }
if ($ExpectedTag -and $ExpectedTag -cne "v$version") { throw "Tag $ExpectedTag does not match Version $version." }
[xml]$manifest = Get-Content -LiteralPath (Join-Path $root 'src/WinMon/app.manifest') -Raw
if ($manifest.assembly.assemblyIdentity.version -ne "$version.0") { throw 'Application manifest version does not match Version.' }
[xml]$strings = Get-Content -LiteralPath (Join-Path $root 'src/WinMon/Resources/Strings.resx') -Raw
$cliPathTask = [string](($strings.root.data | Where-Object name -ceq 'InstallerCliPathTask').value)
$cliPathFailed = [string](($strings.root.data | Where-Object name -ceq 'InstallerCliPathFailed').value)
$cliLegacyPathTask = [string](($strings.root.data | Where-Object name -ceq 'InstallerCliLegacyPathTask').value)
$cliDirectoryConflict = [string](($strings.root.data | Where-Object name -ceq 'InstallerCliDirectoryConflict').value)
$mcpCodexTask = [string](($strings.root.data | Where-Object name -ceq 'InstallerMcpCodexTask').value)
$mcpClaudeTask = [string](($strings.root.data | Where-Object name -ceq 'InstallerMcpClaudeTask').value)
$mcpRegistrationFailed = [string](($strings.root.data | Where-Object name -ceq 'InstallerMcpRegistrationFailed').value)
$mcpDirectoryConflict = [string](($strings.root.data | Where-Object name -ceq 'InstallerMcpDirectoryConflict').value)
$desktopDirectoryConflict = [string](($strings.root.data | Where-Object name -ceq 'InstallerDesktopDirectoryConflict').value)
$downgradeBlocked = [string](($strings.root.data | Where-Object name -ceq 'InstallerDowngradeBlocked').value)
$mcpRegistrationHelp = [string](($strings.root.data | Where-Object name -ceq 'InstallerMcpRegistrationHelp').value)
$mcpCleanupSkipped = [string](($strings.root.data | Where-Object name -ceq 'InstallerMcpCleanupSkipped').value)
$mcpCleanupSkipped = $mcpCleanupSkipped.Replace("'", "''")
if (!$cliPathTask -or !$cliPathFailed -or !$cliLegacyPathTask -or !$cliDirectoryConflict -or !$mcpCodexTask -or !$mcpClaudeTask -or !$mcpRegistrationFailed -or !$mcpDirectoryConflict -or !$desktopDirectoryConflict -or !$downgradeBlocked -or !$mcpRegistrationHelp -or !$mcpCleanupSkipped) { throw 'Installer resources are missing.' }
if ($UpdateSigningKeyPath) {
    $publicKeyPath = Join-Path $root 'src/WinMon/Updates/update-public-key.pem'
    if (!(Test-Path -LiteralPath $publicKeyPath -PathType Leaf)) {
        throw "An update-signing key was supplied but the embedded trust key is missing: $publicKeyPath"
    }
    $privateKey = [Security.Cryptography.RSA]::Create()
    $publicKey = [Security.Cryptography.RSA]::Create()
    try {
        $privateKey.ImportFromPem([IO.File]::ReadAllText((Resolve-Path -LiteralPath $UpdateSigningKeyPath)))
        $publicKey.ImportFromPem([IO.File]::ReadAllText((Resolve-Path -LiteralPath $publicKeyPath)))
        if ($privateKey.KeySize -lt 2048 -or $publicKey.KeySize -lt 2048) { throw 'Update signing keys must be RSA 2048 bits or stronger.' }
        if (![Security.Cryptography.CryptographicOperations]::FixedTimeEquals($privateKey.ExportSubjectPublicKeyInfo(), $publicKey.ExportSubjectPublicKeyInfo())) {
            throw 'The update signing key does not match src/WinMon/Updates/update-public-key.pem.'
        }
    }
    finally {
        $privateKey.Dispose()
        $publicKey.Dispose()
    }
}
if (!$InnoCompiler) { $InnoCompiler = Join-Path $root 'artifacts/toolchain/innosetup-6.7.3/ISCC.exe' }
if (!(Test-Path -LiteralPath $InnoCompiler)) { throw 'Run tools/Install-InnoSetup.ps1 first, or supply -InnoCompiler.' }
if (!$OutputDirectory) { $OutputDirectory = Join-Path $root "artifacts/releases/$version" }
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $OutputDirectory) { throw "Output directory already exists. Choose a new -OutputDirectory: $OutputDirectory" }
$work = Join-Path $root "artifacts/packaging/$([guid]::NewGuid().ToString('N'))"
$publish = Join-Path $work 'publish'
$cliPublish = Join-Path $work 'cli-publish'
$mcpPublish = Join-Path $work 'mcp-publish'
$assets = Join-Path $work 'assets'
$networkServiceBundle = Join-Path $work 'network-service-bundle'
New-Item -ItemType Directory -Path $assets -Force | Out-Null
Push-Location $root
try {
    dotnet test WinMon.sln -c Release -m:1 -p:UseSharedCompilation=false --artifacts-path "$work/build" --logger 'trx;LogFilePrefix=release' --results-directory "$work/test-results"
    if ($LASTEXITCODE -ne 0) { throw 'Release tests failed.' }
    if ($CodeSign) {
        & (Join-Path $PSScriptRoot 'New-NetworkServicePackage.ps1') -Version $version -UpdateSigningKeyPath $UpdateSigningKeyPath -OutputDirectory $networkServiceBundle @signingParameters
    }
    $publishArguments = @('publish', 'src/WinMon/WinMon.csproj', '-c', 'Release', '-r', 'win-x64', '-m:1', '-p:UseSharedCompilation=false', "--artifacts-path=$work/build", '-o', $publish)
    if ($CodeSign) { $publishArguments += "-p:NetworkServiceBundleDirectory=$networkServiceBundle" }
    dotnet @publishArguments
    if ($LASTEXITCODE -ne 0) { throw 'Release publish failed.' }
    $cliPublishArguments = @('publish', 'src/WinMon.Cli/WinMon.Cli.csproj', '-c', 'Release', '-r', 'win-x64', '-m:1', '-p:UseSharedCompilation=false', "--artifacts-path=$work/build", '-o', $cliPublish)
    if ($CodeSign) { $cliPublishArguments += "-p:NetworkServiceBundleDirectory=$networkServiceBundle" }
    dotnet @cliPublishArguments
    if ($LASTEXITCODE -ne 0) { throw 'CLI release publish failed.' }
    $mcpPublishArguments = @('publish', 'src/WinMon.Mcp/WinMon.Mcp.csproj', '-c', 'Release', '-r', 'win-x64', '-m:1', '-p:UseSharedCompilation=false', "--artifacts-path=$work/build", '-o', $mcpPublish)
    dotnet @mcpPublishArguments
    if ($LASTEXITCODE -ne 0) { throw 'MCP release publish failed.' }
    if ($CodeSign) {
        & (Join-Path $PSScriptRoot 'Sign-ReleaseFile.ps1') -Path (Join-Path $publish 'WinMon.exe') @signingParameters |
            ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $work 'application-signature.json') -Encoding utf8NoBOM
        & (Join-Path $PSScriptRoot 'Sign-ReleaseFile.ps1') -Path (Join-Path $cliPublish 'WinMon.Cli.exe') @signingParameters |
            ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $work 'cli-signature.json') -Encoding utf8NoBOM
        & (Join-Path $PSScriptRoot 'Sign-ReleaseFile.ps1') -Path (Join-Path $mcpPublish 'WinMon.Mcp.exe') @signingParameters |
            ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $work 'mcp-signature.json') -Encoding utf8NoBOM
    }
    & $InnoCompiler "/DAppVersion=$version" "/DPublishDirectory=$publish" "/DDesktopDirectoryConflict=$desktopDirectoryConflict" "/DDowngradeBlocked=$downgradeBlocked" "/DReleaseDirectory=$assets" "/DNetworkServiceSetup=$([int][bool]$CodeSign)" (Join-Path $root 'packaging/WinMon.iss')
    if ($LASTEXITCODE -ne 0) { throw 'Installer compilation failed.' }
    $installer = Join-Path $assets "WinMon-$version-Setup-x64.exe"
    & $InnoCompiler "/DAppVersion=$version" "/DCliPublishDirectory=$cliPublish" "/DCliPathTask=$cliPathTask" "/DCliPathFailed=$cliPathFailed" "/DCliLegacyPathTask=$cliLegacyPathTask" "/DCliDirectoryConflict=$cliDirectoryConflict" "/DDowngradeBlocked=$downgradeBlocked" "/DReleaseDirectory=$assets" (Join-Path $root 'packaging/WinMon.Cli.iss')
    if ($LASTEXITCODE -ne 0) { throw 'CLI installer compilation failed.' }
    $cliInstaller = Join-Path $assets "WinMon-Cli-$version-Setup-x64.exe"
    & $InnoCompiler "/DAppVersion=$version" "/DMcpPublishDirectory=$mcpPublish" "/DMcpCodexTask=$mcpCodexTask" "/DMcpClaudeTask=$mcpClaudeTask" "/DMcpRegistrationHelp=$mcpRegistrationHelp" "/DMcpRegistrationFailed=$mcpRegistrationFailed" "/DMcpCleanupSkipped=$mcpCleanupSkipped" "/DMcpDirectoryConflict=$mcpDirectoryConflict" "/DDowngradeBlocked=$downgradeBlocked" "/DReleaseDirectory=$assets" (Join-Path $root 'packaging/WinMon.Mcp.iss')
    if ($LASTEXITCODE -ne 0) { throw 'MCP installer compilation failed.' }
    $mcpInstaller = Join-Path $assets "WinMon-Mcp-$version-Setup-x64.exe"
    if ($CodeSign) {
        & (Join-Path $PSScriptRoot 'Sign-ReleaseFile.ps1') -Path $installer @signingParameters |
            ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $work 'installer-signature.json') -Encoding utf8NoBOM
        & (Join-Path $PSScriptRoot 'Sign-ReleaseFile.ps1') -Path $cliInstaller @signingParameters |
            ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $work 'cli-installer-signature.json') -Encoding utf8NoBOM
        & (Join-Path $PSScriptRoot 'Sign-ReleaseFile.ps1') -Path $mcpInstaller @signingParameters |
            ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $work 'mcp-installer-signature.json') -Encoding utf8NoBOM
    }
    else {
        Write-Warning 'Authenticode signing is disabled. This package is for local testing; GitHub release builds require -CodeSign.'
    }
    $portable = Join-Path $work 'portable'
    New-Item -ItemType Directory -Path $portable | Out-Null
    Copy-Item -LiteralPath (Join-Path $publish 'WinMon.exe'), (Join-Path $root 'LICENSE') -Destination $portable
    New-Item -ItemType File -Path (Join-Path $portable 'portable.flag') | Out-Null
    @'
Extract all files to a writable folder, then run WinMon.exe. No .NET installation is required.
portable.flag keeps settings.json beside the executable. Preserve both files when updating.
Remove portable.flag and restart to use %LOCALAPPDATA%\WinMon\settings.json instead.
Startup registration and launch shortcuts are machine-specific; disable them before moving the app.
'@ | Set-Content -LiteralPath (Join-Path $portable 'PORTABLE.txt') -Encoding utf8NoBOM
    $zip = Join-Path $assets "WinMon-$version-win-x64.zip"
    Compress-Archive -Path "$portable/*" -DestinationPath $zip
    $cliPortable = Join-Path $work 'cli-portable'
    New-Item -ItemType Directory -Path $cliPortable | Out-Null
    Copy-Item -LiteralPath (Join-Path $cliPublish 'WinMon.Cli.exe'), (Join-Path $root 'LICENSE') -Destination $cliPortable
    @'
Run WinMon.Cli.exe from a terminal. No .NET installation is required.
The optional network install command requires this signed release build and administrator approval.
'@ | Set-Content -LiteralPath (Join-Path $cliPortable 'README.txt') -Encoding utf8NoBOM
    Compress-Archive -Path "$cliPortable/*" -DestinationPath (Join-Path $assets "WinMon-Cli-$version-win-x64.zip")
    $mcpPortable = Join-Path $work 'mcp-portable'
    New-Item -ItemType Directory -Path $mcpPortable | Out-Null
    Copy-Item -LiteralPath (Join-Path $mcpPublish 'WinMon.Mcp.exe'), (Join-Path $root 'LICENSE') -Destination $mcpPortable
    @'
Run WinMon.Mcp.exe only through a compatible MCP client. No .NET installation is required.
Installers can optionally register the server with Codex or Claude Code. Portable copies require manual registration.
'@ | Set-Content -LiteralPath (Join-Path $mcpPortable 'README.txt') -Encoding utf8NoBOM
    Compress-Archive -Path "$mcpPortable/*" -DestinationPath (Join-Path $assets "WinMon-Mcp-$version-win-x64.zip")
    $pluginSource = Join-Path $root 'plugins/claude-code/winmon'
    if (!(Test-Path -LiteralPath $pluginSource -PathType Container)) { throw "Claude Code plugin is missing: $pluginSource" }
    $pluginStage = Join-Path $work 'claude-plugin'
    Copy-Item -LiteralPath $pluginSource -Destination $pluginStage -Recurse
    $pluginManifest = Join-Path $pluginStage '.claude-plugin/plugin.json'
    $pluginMetadata = Get-Content -LiteralPath $pluginManifest -Raw | ConvertFrom-Json -AsHashtable
    $pluginMetadata.version = $version
    $pluginMetadata | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $pluginManifest -Encoding utf8NoBOM
    $pluginFiles = @(Get-ChildItem -LiteralPath $pluginStage -Force)
    if ($pluginFiles.Count -eq 0) { throw 'Claude Code plugin has no files.' }
    Compress-Archive -LiteralPath $pluginFiles.FullName -DestinationPath (Join-Path $assets "WinMon-Claude-Code-$version.zip")
    $winget = Join-Path $work 'winget'
    & (Join-Path $PSScriptRoot 'New-WinGetManifest.ps1') -Version $version -InstallerPath $installer -OutputDirectory $winget | Out-Host
    Compress-Archive -Path (Join-Path $winget 'manifests') -DestinationPath (Join-Path $assets "WinMon-$version-winget.zip")
    if ($UpdateSigningKeyPath) {
        & (Join-Path $PSScriptRoot 'New-UpdateManifest.ps1') -Version $version -PortablePath $zip -InstallerPath $installer -SigningKeyPath $UpdateSigningKeyPath -OutputDirectory $assets
        & (Join-Path $PSScriptRoot 'New-UpdateManifest.ps1') -Product WinMon.Cli -Version $version -PortablePath (Join-Path $assets "WinMon-Cli-$version-win-x64.zip") -InstallerPath $cliInstaller -SigningKeyPath $UpdateSigningKeyPath -OutputDirectory $assets
        & (Join-Path $PSScriptRoot 'New-UpdateManifest.ps1') -Product WinMon.Mcp -Version $version -PortablePath (Join-Path $assets "WinMon-Mcp-$version-win-x64.zip") -InstallerPath $mcpInstaller -SigningKeyPath $UpdateSigningKeyPath -OutputDirectory $assets
    }
    else {
        Write-Warning 'No update-signing key was supplied. This package is not eligible for in-app updates.'
    }
    Get-ChildItem -LiteralPath $assets -File | Sort-Object Name | ForEach-Object {
        '{0}  {1}' -f (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $_.Name
    } | Set-Content -LiteralPath (Join-Path $assets 'SHA256SUMS.txt') -Encoding utf8NoBOM
    New-Item -ItemType Directory -Path $OutputDirectory | Out-Null
    Copy-Item -Path "$assets/*" -Destination $OutputDirectory
    Write-Host "Release assets: $OutputDirectory"
    Write-Host "Build and test evidence: $work"
}
finally { Pop-Location }
