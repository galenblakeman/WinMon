#requires -Version 7.0
# Exercises real packaging/signature gates with fake build and Azure boundaries. No Azure calls.
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = Split-Path $PSScriptRoot -Parent
$run = Join-Path $root "artifacts/signing-validation/$([guid]::NewGuid().ToString('N'))"
$fixture = Join-Path $run 'fixture'
foreach ($directory in @('tools', 'src/WinMon/Updates', 'src/WinMon/Resources', 'src/WinMon.NetworkService', 'src/WinMon.NetworkSetup', 'packaging', 'plugins/claude-code')) {
    New-Item -ItemType Directory -Path (Join-Path $fixture $directory) -Force | Out-Null
}
Copy-Item -LiteralPath (Join-Path $root 'plugins/claude-code/winmon') -Destination (Join-Path $fixture 'plugins/claude-code') -Recurse
foreach ($name in @('New-Release.ps1', 'New-NetworkServicePackage.ps1', 'Sign-ReleaseFile.ps1', 'New-WinGetManifest.ps1', 'New-UpdateManifest.ps1')) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination (Join-Path $fixture 'tools')
}
Copy-Item -LiteralPath (Join-Path $root 'LICENSE') -Destination $fixture
'<Project><PropertyGroup><Version>9.8.7</Version></PropertyGroup></Project>' |
    Set-Content (Join-Path $fixture 'Directory.Build.props')
'<assembly><assemblyIdentity version="9.8.7.0" /></assembly>' |
    Set-Content (Join-Path $fixture 'src/WinMon/app.manifest')
@'
<root>
  <data name="InstallerCliPathTask"><value>Add WinMon.Cli to PATH for this user</value></data>
  <data name="InstallerCliPathFailed"><value>Could not update the user PATH for WinMon.Cli.</value></data>
  <data name="InstallerCliLegacyPathTask"><value>Remove the legacy installer-owned WinMon CLI PATH entry for this user</value></data>
  <data name="InstallerCliDirectoryConflict"><value>Choose a directory that is not owned by WinMon desktop or MCP.</value></data>
  <data name="InstallerMcpCodexTask"><value>Register WinMon MCP with Codex for this user</value></data>
  <data name="InstallerMcpClaudeTask"><value>Register WinMon MCP with Claude Code for this user</value></data>
  <data name="InstallerMcpRegistrationFailed"><value>WinMon MCP registration did not complete. See the installer log for manual setup.</value></data>
  <data name="InstallerMcpDirectoryConflict"><value>Choose a directory that is not owned by WinMon desktop or CLI.</value></data>
  <data name="InstallerDesktopDirectoryConflict"><value>Choose a directory that is not owned by WinMon CLI or MCP.</value></data>
  <data name="InstallerDowngradeBlocked"><value>An older WinMon version cannot replace a newer installation.</value></data>
  <data name="InstallerMcpRegistrationHelp"><value>Registers a local read-only diagnostics server with the selected client.</value></data>
  <data name="InstallerMcpCleanupSkipped"><value>Registration with {0} was left unchanged. Inspect it manually.</value></data>
</root>
'@ | Set-Content (Join-Path $fixture 'src/WinMon/Resources/Strings.resx')
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
$script = [IO.Path]::GetFileName($args[-1])
if ($script -eq 'WinMon.iss') {
    $payload = Get-Content -LiteralPath (Join-Path $options.PublishDirectory 'WinMon.exe') -Raw
    if ($options.NetworkServiceSetup -cne $(if ($testState.Mode -eq 'unsigned') { '0' } else { '1' })) { throw 'Installer network-service option must be present only in signed packages.' }
    if ($testState.Mode -ne 'unsigned' -and $payload -notmatch 'SIGNED') { throw 'Installer received an unsigned application.' }
    [IO.File]::WriteAllText((Join-Path $options.ReleaseDirectory 'WinMon-9.8.7-Setup-x64.exe'), "INSTALLER:${payload}")
}
elseif ($script -eq 'WinMon.Cli.iss') {
    $payload = Get-Content -LiteralPath (Join-Path $options.CliPublishDirectory 'WinMon.Cli.exe') -Raw
    if ($testState.Mode -ne 'unsigned' -and $payload -notmatch 'SIGNED') { throw 'Installer received an unsigned CLI.' }
    [IO.File]::WriteAllText((Join-Path $options.ReleaseDirectory 'WinMon-Cli-9.8.7-Setup-x64.exe'), "INSTALLER:${payload}")
}
elseif ($script -eq 'WinMon.Mcp.iss') {
    $payload = Get-Content -LiteralPath (Join-Path $options.McpPublishDirectory 'WinMon.Mcp.exe') -Raw
    if ($testState.Mode -ne 'unsigned' -and $payload -notmatch 'SIGNED') { throw 'Installer received an unsigned MCP server.' }
    [IO.File]::WriteAllText((Join-Path $options.ReleaseDirectory 'WinMon-Mcp-9.8.7-Setup-x64.exe'), "INSTALLER:${payload}")
}
else { throw "Unexpected installer script: $script" }
$global:LASTEXITCODE = 0
'@ | Set-Content -LiteralPath $compiler
$testState = @{ Mode = 'valid'; Calls = 0 }
function Import-Module { param($Name, $RequiredVersion, $ErrorAction) }
function dotnet {
    if ($args[0] -eq 'publish') {
        $output = $args[[Array]::IndexOf($args, '-o') + 1]
        New-Item -ItemType Directory -Path $output -Force | Out-Null
        if ($args[1] -like '*WinMon.NetworkService*') {
            [IO.File]::WriteAllText((Join-Path $output 'WinMon.NetworkService.exe'), 'SERVICE')
        }
        elseif ($args[1] -like '*WinMon.NetworkSetup*') {
            [IO.File]::WriteAllText((Join-Path $output 'WinMon.NetworkSetup.exe'), 'SETUP')
        }
        elseif ($args[1] -like '*WinMon.Cli*') {
            [IO.File]::WriteAllText((Join-Path $output 'WinMon.Cli.exe'), 'CLI')
        }
        elseif ($args[1] -like '*WinMon.Mcp*') {
            [IO.File]::WriteAllText((Join-Path $output 'WinMon.Mcp.exe'), 'MCP')
        }
        else {
            [IO.File]::WriteAllText((Join-Path $output 'WinMon.exe'), 'APPLICATION')
        }
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
        Status = $(if ($testState.Mode -eq 'invalid' -or ($testState.Mode -eq 'installer-invalid' -and $testState.Calls -eq 8)) { 'HashMismatch' } else { 'Valid' })
        StatusMessage = 'Simulated Authenticode result'
        SignerCertificate = $certificate
        TimeStamperCertificate = $(if ($testState.Mode -ne 'no-timestamp') { [pscustomobject]@{ Subject = 'CN=Test TSA' } })
    }
}
$release = Join-Path $fixture 'tools/New-Release.ps1'
$output = Join-Path $run 'release'
& $release -CodeSign -InnoCompiler $compiler -OutputDirectory $output -UpdateSigningKeyPath (Join-Path $run 'private.pem')
if ($testState.Calls -ne 8) { throw 'Expected exactly eight signing calls.' }
Expand-Archive (Join-Path $output 'WinMon-9.8.7-win-x64.zip') (Join-Path $run 'portable')
if ((Get-Content (Join-Path $run 'portable/WinMon.exe') -Raw) -cne 'APPLICATION:SIGNED') { throw 'Portable executable was not signed.' }
if (Test-Path (Join-Path $run 'portable/WinMon.Cli.exe')) { throw 'Desktop portable archive unexpectedly bundled CLI.' }
if (Test-Path (Join-Path $run 'portable/WinMon.Mcp.exe')) { throw 'Desktop portable archive unexpectedly bundled MCP.' }
Expand-Archive (Join-Path $output 'WinMon-Cli-9.8.7-win-x64.zip') (Join-Path $run 'cli-portable')
if ((Get-Content (Join-Path $run 'cli-portable/WinMon.Cli.exe') -Raw) -cne 'CLI:SIGNED') { throw 'CLI-only executable was not signed.' }
Expand-Archive (Join-Path $output 'WinMon-Mcp-9.8.7-win-x64.zip') (Join-Path $run 'mcp-portable')
if ((Get-Content (Join-Path $run 'mcp-portable/WinMon.Mcp.exe') -Raw) -cne 'MCP:SIGNED') { throw 'MCP-only executable was not signed.' }
Expand-Archive (Join-Path $output 'WinMon-Claude-Code-9.8.7.zip') (Join-Path $run 'plugin')
if (!(Test-Path (Join-Path $run 'plugin/.claude-plugin/plugin.json')) -or !(Test-Path (Join-Path $run 'plugin/.mcp.json'))) {
    throw 'Claude Code plugin archive is incomplete.'
}
if ((Get-Content (Join-Path $run 'plugin/.claude-plugin/plugin.json') -Raw | ConvertFrom-Json).version -cne '9.8.7') {
    throw 'Claude Code plugin version did not follow the release version.'
}
$installerHash = (Get-FileHash (Join-Path $output 'WinMon-9.8.7-Setup-x64.exe')).Hash
foreach ($line in Get-Content (Join-Path $output 'SHA256SUMS.txt')) {
    $parts = $line -split '  ', 2
    if ((Get-FileHash (Join-Path $output $parts[1])).Hash -ine $parts[0]) { throw 'Release checksum does not match final artifact.' }
}
Expand-Archive (Join-Path $output 'WinMon-9.8.7-winget.zip') (Join-Path $run 'winget')
$installerManifest = Get-ChildItem (Join-Path $run 'winget') -Recurse -Filter '*.installer.yaml'
if ((Get-Content $installerManifest.FullName -Raw) -notmatch $installerHash) { throw 'WinGet hash does not match final installer.' }
$publicKey = [Security.Cryptography.RSA]::Create()
try {
    $publicKey.ImportFromPem([IO.File]::ReadAllText((Join-Path $fixture 'src/WinMon/Updates/update-public-key.pem')))
    foreach ($product in @{ desktop = 'WinMon'; cli = 'WinMon-Cli'; mcp = 'WinMon-Mcp' }.GetEnumerator()) {
        $manifestBytes = [IO.File]::ReadAllBytes((Join-Path $output "$($product.Key)-update-manifest.json"))
        $manifest = [Text.Encoding]::UTF8.GetString($manifestBytes) | ConvertFrom-Json
        if ($manifest.portable.name -cne "$($product.Value)-9.8.7-win-x64.zip" -or
            $manifest.installer.name -cne "$($product.Value)-9.8.7-Setup-x64.exe") { throw 'Update manifest selects another product.' }
        foreach ($asset in @($manifest.portable, $manifest.installer)) {
            $assetPath = Join-Path $output $asset.name
            if ((Get-FileHash $assetPath).Hash -ine $asset.sha256) { throw 'Update manifest hash does not match final artifact.' }
            if ((Get-Item $assetPath).Length -ne $asset.size) { throw 'Update manifest size does not match final artifact.' }
        }
        $signature = [Convert]::FromBase64String((Get-Content (Join-Path $output "$($product.Key)-update-manifest.sig") -Raw).Trim())
        if (!$publicKey.VerifyData($manifestBytes, $signature, [Security.Cryptography.HashAlgorithmName]::SHA256, [Security.Cryptography.RSASignaturePadding]::Pkcs1)) {
            throw 'Update manifest signature is invalid.'
        }
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
    try { & $release -CodeSign -InnoCompiler $compiler -OutputDirectory $failedOutput -UpdateSigningKeyPath (Join-Path $run 'private.pem') }
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
# The fixture key is only needed for this disposable signing-boundary test.
Remove-Item -LiteralPath (Join-Path $run 'private.pem') -Force -ErrorAction SilentlyContinue
Write-Host "PASS: signing order, desktop, CLI and MCP payloads, plugin archive, final hashes, RSA manifest, signing failure gates, and unsigned local packaging. Evidence: $run"
