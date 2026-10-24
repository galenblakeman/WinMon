#requires -Version 7.0
# Exercises real packaging/signature gates with fake build and Azure boundaries. No Azure calls.
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = Split-Path $PSScriptRoot -Parent
$run = Join-Path $root "artifacts/signing-validation/$([guid]::NewGuid().ToString('N'))"
$fixture = Join-Path $run 'fixture'
foreach ($directory in @('tools', 'src/WinMon/Updates', 'packaging')) {
    New-Item -ItemType Directory -Path (Join-Path $fixture $directory) -Force | Out-Null
}
foreach ($name in @('New-Release.ps1', 'Sign-ReleaseFile.ps1', 'New-WinGetManifest.ps1', 'New-UpdateManifest.ps1')) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination (Join-Path $fixture 'tools')
}
Copy-Item -LiteralPath (Join-Path $root 'LICENSE') -Destination $fixture
'<Project><PropertyGroup><Version>9.8.7</Version></PropertyGroup></Project>' |
    Set-Content (Join-Path $fixture 'Directory.Build.props')
'<assembly><assemblyIdentity version="9.8.7.0" /></assembly>' |
    Set-Content (Join-Path $fixture 'src/WinMon/app.manifest')
$key = [Security.Cryptography.RSA]::Create(2048)
try {
    [IO.File]::WriteAllText((Join-Path $run 'private.pem'), $key.ExportPkcs8PrivateKeyPem())
    [IO.File]::WriteAllText((Join-Path $fixture 'src/WinMon/Updates/update-public-key.pem'), $key.ExportSubjectPublicKeyInfoPem())
}
finally { $key.Dispose() }
$compiler = Join-Path $run 'compiler.ps1'
@'
$options = @{}
foreach ($argument in $args) {
    if ($argument -match '^/D([^=]+)=(.*)$') { $options[$Matches[1]] = $Matches[2] }
}
$payload = Get-Content -LiteralPath (Join-Path $options.PublishDirectory 'WinMon.exe') -Raw
if ($testState.Mode -ne 'unsigned' -and $payload -notmatch 'SIGNED') { throw 'Installer received an unsigned application.' }
[IO.File]::WriteAllText((Join-Path $options.ReleaseDirectory 'WinMon-9.8.7-Setup-x64.exe'), "INSTALLER:$payload")
$global:LASTEXITCODE = 0
'@ | Set-Content -LiteralPath $compiler
$testState = @{ Mode = 'valid'; Calls = 0 }
function Import-Module { param($Name, $RequiredVersion, $ErrorAction) }
function dotnet {
    if ($args[0] -eq 'publish') {
        $output = $args[[Array]::IndexOf($args, '-o') + 1]
        New-Item -ItemType Directory -Path $output -Force | Out-Null
        [IO.File]::WriteAllText((Join-Path $output 'WinMon.exe'), 'APPLICATION')
    }
    $global:LASTEXITCODE = 0
}
function Invoke-ArtifactSigning {
    param($Endpoint, $CodeSigningAccountName, $CertificateProfileName, $Files,
        $FileDigest, $TimestampRfc3161, $TimestampDigest, $Description, $DescriptionUrl,
        $ExcludeEnvironmentCredential, $ExcludeWorkloadIdentityCredential,
        $ExcludeManagedIdentityCredential, $ExcludeSharedTokenCacheCredential,
        $ExcludeVisualStudioCredential, $ExcludeVisualStudioCodeCredential,
        $ExcludeAzureCliCredential, $ExcludeAzurePowerShellCredential,
        $ExcludeAzureDeveloperCliCredential, $ExcludeInteractiveBrowserCredential)
    if ($CodeSigningAccountName -cne 'galenwinmonsigning' -or $CertificateProfileName -cne 'WinMonPublic' -or
        $Endpoint -cne 'https://eus.codesigning.azure.net/' -or $FileDigest -cne 'SHA256' -or
        $TimestampDigest -cne 'SHA256' -or $TimestampRfc3161 -cne 'http://timestamp.acs.microsoft.com' -or
        $ExcludeAzureCliCredential -or !$ExcludeInteractiveBrowserCredential -or !$ExcludeEnvironmentCredential) {
        throw 'Unexpected signing configuration.'
    }
    $testState.Calls++
    if ($testState.Mode -eq 'provider-error') { throw 'Simulated Azure signing failure.' }
    [IO.File]::AppendAllText($Files, ':SIGNED')
}
function Get-AuthenticodeSignature {
    param($LiteralPath)
    $certificate = [pscustomobject]@{ Subject = 'CN=Galen Blakeman'; Thumbprint = 'TEST' }
    $certificate | Add-Member ScriptMethod GetNameInfo { param($Type, $Issuer) 'Galen Blakeman' }
    if ($testState.Mode -eq 'wrong-publisher') {
        $certificate | Add-Member ScriptMethod GetNameInfo { param($Type, $Issuer) 'Other Publisher' } -Force
    }
    [pscustomobject]@{
        Status = $(if ($testState.Mode -eq 'invalid' -or ($testState.Mode -eq 'installer-invalid' -and $testState.Calls -eq 2)) { 'HashMismatch' } else { 'Valid' })
        StatusMessage = 'Simulated Authenticode result'
        SignerCertificate = $certificate
        TimeStamperCertificate = $(if ($testState.Mode -ne 'no-timestamp') { [pscustomobject]@{ Subject = 'CN=Test TSA' } })
    }
}
$release = Join-Path $fixture 'tools/New-Release.ps1'
$output = Join-Path $run 'release'
try { & $release -CodeSign -InnoCompiler $compiler -OutputDirectory $output -UpdateSigningKeyPath (Join-Path $run 'private.pem') }
# The fixture key is only needed for the signed release; remove it so no private key outlives the run.
finally { Remove-Item -LiteralPath (Join-Path $run 'private.pem') -Force -ErrorAction SilentlyContinue }
if ($testState.Calls -ne 2) { throw 'Expected exactly two signing calls.' }
Expand-Archive (Join-Path $output 'WinMon-9.8.7-win-x64.zip') (Join-Path $run 'portable')
if ((Get-Content (Join-Path $run 'portable/WinMon.exe') -Raw) -cne 'APPLICATION:SIGNED') { throw 'Portable executable was not signed.' }
$installerHash = (Get-FileHash (Join-Path $output 'WinMon-9.8.7-Setup-x64.exe')).Hash
foreach ($line in Get-Content (Join-Path $output 'SHA256SUMS.txt')) {
    $parts = $line -split '  ', 2
    if ((Get-FileHash (Join-Path $output $parts[1])).Hash -ine $parts[0]) { throw 'Release checksum does not match final artifact.' }
}
Expand-Archive (Join-Path $output 'WinMon-9.8.7-winget.zip') (Join-Path $run 'winget')
$installerManifest = Get-ChildItem (Join-Path $run 'winget') -Recurse -Filter '*.installer.yaml'
if ((Get-Content $installerManifest.FullName -Raw) -notmatch $installerHash) { throw 'WinGet hash does not match final installer.' }
$manifestBytes = [IO.File]::ReadAllBytes((Join-Path $output 'update-manifest.json'))
$manifest = [Text.Encoding]::UTF8.GetString($manifestBytes) | ConvertFrom-Json
# Check both payload hashes and the real updater RSA signature after packaging.
foreach ($asset in @($manifest.portable, $manifest.installer)) {
    $assetPath = Join-Path $output $asset.name
    if ((Get-FileHash $assetPath).Hash -ine $asset.sha256) { throw 'Update manifest hash does not match final artifact.' }
    if ((Get-Item $assetPath).Length -ne $asset.size) { throw 'Update manifest size does not match final artifact.' }
}
$publicKey = [Security.Cryptography.RSA]::Create()
try {
    $publicKey.ImportFromPem([IO.File]::ReadAllText((Join-Path $fixture 'src/WinMon/Updates/update-public-key.pem')))
    $signature = [Convert]::FromBase64String((Get-Content (Join-Path $output 'update-manifest.sig') -Raw).Trim())
    if (!$publicKey.VerifyData($manifestBytes, $signature, [Security.Cryptography.HashAlgorithmName]::SHA256, [Security.Cryptography.RSASignaturePadding]::Pkcs1)) {
        throw 'Update manifest signature is invalid.'
    }
}
finally { $publicKey.Dispose() }
$expectedErrors = @{
    invalid = 'Authenticode verification failed'
    'installer-invalid' = 'Authenticode verification failed'
    'wrong-publisher' = 'Unexpected signing publisher'
    'no-timestamp' = 'Missing timestamp'
    'provider-error' = 'Simulated Azure signing failure'
}
foreach ($mode in $expectedErrors.Keys) {
    $testState.Mode = $mode
    $testState.Calls = 0
    $failedOutput = Join-Path $run $mode
    $failed = $false
    try { & $release -CodeSign -InnoCompiler $compiler -OutputDirectory $failedOutput }
    catch {
        if ($_.Exception.Message -notlike "*$($expectedErrors[$mode])*") { throw }
        $failed = $true
    }
    if (!$failed -or (Test-Path $failedOutput)) { throw "Release did not fail closed for $mode." }
}
$testState.Mode = 'unsigned'
$testState.Calls = 0
& $release -InnoCompiler $compiler -OutputDirectory (Join-Path $run 'unsigned')
if ($testState.Calls -ne 0) { throw 'Unsigned local build contacted signing provider.' }
Write-Host "PASS: signing order, portable payload, final hashes, RSA manifest, five signing failure gates, and unsigned local packaging. Evidence: $run"
