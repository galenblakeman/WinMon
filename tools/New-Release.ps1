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
$assets = Join-Path $work 'assets'
New-Item -ItemType Directory -Path $assets -Force | Out-Null
Push-Location $root
try {
    dotnet test tests/WinMon.Tests/WinMon.Tests.csproj -c Release -m:1 -p:UseSharedCompilation=false --artifacts-path "$work/build" --logger "trx;LogFileName=release.trx" --results-directory "$work/test-results"
    if ($LASTEXITCODE -ne 0) { throw 'Release tests failed.' }
    dotnet publish src/WinMon/WinMon.csproj -c Release -r win-x64 -m:1 -p:UseSharedCompilation=false --artifacts-path "$work/build" -o $publish
    if ($LASTEXITCODE -ne 0) { throw 'Release publish failed.' }
    if ($CodeSign) {
        & (Join-Path $PSScriptRoot 'Sign-ReleaseFile.ps1') -Path (Join-Path $publish 'WinMon.exe') @signingParameters |
            ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $work 'application-signature.json') -Encoding utf8NoBOM
    }
    & $InnoCompiler "/DAppVersion=$version" "/DPublishDirectory=$publish" "/DReleaseDirectory=$assets" (Join-Path $root 'packaging/WinMon.iss')
    if ($LASTEXITCODE -ne 0) { throw 'Installer compilation failed.' }
    $installer = Join-Path $assets "WinMon-$version-Setup-x64.exe"
    if ($CodeSign) {
        & (Join-Path $PSScriptRoot 'Sign-ReleaseFile.ps1') -Path $installer @signingParameters |
            ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $work 'installer-signature.json') -Encoding utf8NoBOM
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
    $winget = Join-Path $work 'winget'
    & (Join-Path $PSScriptRoot 'New-WinGetManifest.ps1') -Version $version -InstallerPath $installer -OutputDirectory $winget | Out-Host
    Compress-Archive -Path (Join-Path $winget 'manifests') -DestinationPath (Join-Path $assets "WinMon-$version-winget.zip")
    if ($UpdateSigningKeyPath) {
        & (Join-Path $PSScriptRoot 'New-UpdateManifest.ps1') -Version $version -PortablePath $zip -InstallerPath $installer -SigningKeyPath $UpdateSigningKeyPath -OutputDirectory $assets
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
