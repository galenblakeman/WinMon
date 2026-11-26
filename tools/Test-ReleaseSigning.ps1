#requires -Version 7.0
# Exercises real packaging/signature gates with fake build and Azure boundaries. No Azure calls.
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = Split-Path $PSScriptRoot -Parent
$run = Join-Path $root "artifacts/signing-validation/$([guid]::NewGuid().ToString('N'))"
$fixture = Join-Path $run 'fixture'
foreach ($directory in @('tools', 'src/WinMon/Updates', 'src/WinMon/Resources', 'src/WinMon.NetworkService', 'src/WinMon.NetworkSetup', 'src/WinMon.SensorService', 'src/WinMon.SensorSetup', 'packaging', 'plugins/claude-code')) {
    New-Item -ItemType Directory -Path (Join-Path $fixture $directory) -Force | Out-Null
}
Copy-Item -LiteralPath (Join-Path $root 'plugins/claude-code/winmon') -Destination (Join-Path $fixture 'plugins/claude-code') -Recurse
foreach ($name in @('New-Release.ps1', 'New-NetworkServicePackage.ps1', 'New-SensorServicePackage.ps1', 'Get-PawnIoInstaller.ps1', 'Sign-ReleaseFile.ps1', 'New-WinGetManifest.ps1', 'New-UpdateManifest.ps1')) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination (Join-Path $fixture 'tools')
}
Copy-Item -LiteralPath (Join-Path $root 'LICENSE') -Destination $fixture
Copy-Item -LiteralPath (Join-Path $root 'packaging/PawnIO.json') -Destination (Join-Path $fixture 'packaging')
# Substitute only the downloaded bytes/hash in the disposable fixture; real releases use the upstream pin.
$pawnIoBytes = [Text.Encoding]::UTF8.GetBytes('OFFICIAL-PAWNIO-FIXTURE')
$pawnIoPinPath = Join-Path $fixture 'packaging/PawnIO.json'
$pawnIoPin = Get-Content $pawnIoPinPath -Raw | ConvertFrom-Json
$pawnIoPin.sha256 = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($pawnIoBytes))
$pawnIoPin | ConvertTo-Json | Set-Content $pawnIoPinPath
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
  <data name="InstallerSensorServiceTask"><value>Install hardware sensors including PawnIO if missing</value></data>
  <data name="InstallerSensorServiceSetupFailed"><value>Hardware sensors could not be enabled.</value></data>
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
    foreach ($companion in @('WinMon.Cli.exe', 'WinMon.Mcp.exe')) {
        $tool = Get-Content -LiteralPath (Join-Path $options.PublishDirectory $companion) -Raw
        if ($testState.Mode -ne 'unsigned' -and $tool -notmatch 'SIGNED') { throw 'Desktop compatibility installer received an unsigned companion.' }
    }
    if ($options.NetworkServiceSetup -cne $(if ($testState.Mode -eq 'unsigned') { '0' } else { '1' })) { throw 'Installer network-service option must be present only in signed packages.' }
    if ($options.SensorServiceSetup -cne $options.NetworkServiceSetup) { throw 'Installer sensor option must be present only in signed packages.' }
    if ($testState.Mode -ne 'unsigned') {
        if ((Get-FileHash $options.PawnIoInstaller).Hash -ine $options.PawnIoSha256) { throw 'Installer received an unverified PawnIO package.' }
    } elseif (Test-Path $options.PawnIoInstaller) { throw 'Unsigned installer must not download PawnIO.' }
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
    $plugin = Join-Path $options.McpPublishDirectory 'claude-code-plugin'
    if (!(Test-Path (Join-Path $plugin '.mcp.json')) -or
        (Get-Content (Join-Path $plugin '.claude-plugin/plugin.json') -Raw | ConvertFrom-Json).version -cne '9.8.7') {
        throw 'MCP installer did not receive the versioned Claude Code plugin.'
    }
    if ($testState.Mode -ne 'unsigned' -and $payload -notmatch 'SIGNED') { throw 'Installer received an unsigned MCP server.' }
    [IO.File]::WriteAllText((Join-Path $options.ReleaseDirectory 'WinMon-Mcp-9.8.7-Setup-x64.exe'), "INSTALLER:${payload}")
}
else { throw "Unexpected installer script: $script" }
$global:LASTEXITCODE = 0
'@ | Set-Content -LiteralPath $compiler
$testState = @{ Mode = 'valid'; Calls = 0; OidcRequests = 0; AzureLogins = 0; SubscriptionSelections = 0 }
function Invoke-WebRequest {
    param($Uri, $OutFile)
    if ($Uri -cne 'https://github.com/namazso/PawnIO.Setup/releases/download/2.2.0/PawnIO_setup.exe') { throw 'Unexpected PawnIO URL.' }
    [IO.File]::WriteAllBytes($OutFile, $(if ($testState.Mode -eq 'pawnio-mismatch') { [byte[]]@(1, 2, 3) } else { $pawnIoBytes }))
}
function Import-Module { param($Name, $RequiredVersion, $ErrorAction) }
function Invoke-RestMethod {
    param($Uri, $Headers, $MaximumRedirection, $TimeoutSec)
    if ($Uri -cne 'https://fixture.test/oidc?api-version=2.0&audience=api%3A%2F%2FAzureADTokenExchange' -or
        $Headers.Authorization -cne 'Bearer fixture-request' -or $MaximumRedirection -ne 0) {
        throw 'Unexpected OIDC request.'
    }
    $testState.OidcRequests++
    @{ value = "fixture.payload.$($testState.OidcRequests)" }
}
function az {
    if ($args[0] -ceq 'login') {
        if (($args -join ' ') -cne "login --service-principal --username fixture-client --tenant fixture-tenant --federated-token fixture.payload.$($testState.OidcRequests) --only-show-errors --output none") {
            throw 'Azure CLI did not receive the fresh fixture assertion.'
        }
        if ($testState.Mode -eq 'oidc-error') { $global:LASTEXITCODE = 1; return }
        $testState.AzureLogins++
    }
    elseif (($args -join ' ') -ceq 'account set --subscription fixture-subscription --only-show-errors') {
        $testState.SubscriptionSelections++
    }
    else { throw 'Unexpected Azure CLI command.' }
    $global:LASTEXITCODE = 0
}
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
        elseif ($args[1] -like '*WinMon.SensorService*') {
            [IO.File]::WriteAllText((Join-Path $output 'WinMon.SensorService.exe'), 'SENSOR-SERVICE')
        }
        elseif ($args[1] -like '*WinMon.SensorSetup*') {
            [IO.File]::WriteAllText((Join-Path $output 'WinMon.SensorSetup.exe'), 'SENSOR-SETUP')
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
    if ($env:WINMON_SIGNING_GITHUB_OIDC -ceq 'true' -and
        ($testState.OidcRequests -ne $testState.Calls -or $testState.AzureLogins -ne $testState.Calls -or
            $testState.SubscriptionSelections -ne $testState.Calls)) {
        throw 'Each signing call must obtain a fresh assertion and select the subscription first.'
    }
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
        Status = $(if ($testState.Mode -eq 'invalid' -or ($testState.Mode -eq 'installer-invalid' -and $testState.Calls -eq 10)) { 'HashMismatch' } else { 'Valid' })
        StatusMessage = 'Simulated Authenticode result'
        SignerCertificate = $certificate
        TimeStamperCertificate = $(if ($testState.Mode -ne 'no-timestamp') { [pscustomobject]@{ Subject = 'CN=Test TSA' } })
    }
}
$release = Join-Path $fixture 'tools/New-Release.ps1'
$output = Join-Path $run 'release'
$fixtureEnvironment = @{
    WINMON_SIGNING_GITHUB_OIDC = 'true'
    GITHUB_ACTIONS = 'true'
    ACTIONS_ID_TOKEN_REQUEST_URL = 'https://fixture.test/oidc?api-version=2.0'
    ACTIONS_ID_TOKEN_REQUEST_TOKEN = 'fixture-request'
    AZURE_CLIENT_ID = 'fixture-client'
    AZURE_TENANT_ID = 'fixture-tenant'
    AZURE_SUBSCRIPTION_ID = 'fixture-subscription'
}
$originalEnvironment = @{}
try {
    foreach ($entry in $fixtureEnvironment.GetEnumerator()) {
        $originalEnvironment[$entry.Key] = [Environment]::GetEnvironmentVariable($entry.Key)
        [Environment]::SetEnvironmentVariable($entry.Key, $entry.Value)
    }
    & $release -CodeSign -InnoCompiler $compiler -OutputDirectory $output -UpdateSigningKeyPath (Join-Path $run 'private.pem')
    $testState.Mode = 'oidc-error'
    $signingError = try {
        & (Join-Path $fixture 'tools/Sign-ReleaseFile.ps1') -Path (Join-Path $output 'WinMon-9.8.7-Setup-x64.exe') `
            -Endpoint 'https://eus.codesigning.azure.net/' -AccountName 'galenwinmonsigning' `
            -CertificateProfileName 'WinMonPublic' -ExpectedPublisher 'Galen Blakeman'
    }
    catch { $_.Exception.Message }
    if ($signingError -cne 'GitHub OIDC sign-in to Azure CLI failed.') { throw 'Signing must stop when OIDC sign-in fails.' }
}
finally {
    foreach ($entry in $originalEnvironment.GetEnumerator()) {
        [Environment]::SetEnvironmentVariable($entry.Key, $entry.Value)
    }
    $testState.Mode = 'valid'
}
if ($testState.Calls -ne 10) { throw 'Expected exactly ten signing calls, including both service bundles.' }
Expand-Archive (Join-Path $output 'WinMon-9.8.7-win-x64.zip') (Join-Path $run 'portable')
if ((Get-Content (Join-Path $run 'portable/WinMon.exe') -Raw) -cne 'APPLICATION:SIGNED') { throw 'Portable executable was not signed.' }
if ((Get-Content (Join-Path $run 'portable/WinMon.Cli.exe') -Raw) -cne 'CLI:SIGNED') { throw 'Legacy desktop update archive is missing the signed CLI.' }
if ((Get-Content (Join-Path $run 'portable/WinMon.Mcp.exe') -Raw) -cne 'MCP:SIGNED') { throw 'Legacy desktop update archive is missing the signed MCP.' }
Expand-Archive (Join-Path $output 'WinMon-Cli-9.8.7-win-x64.zip') (Join-Path $run 'cli-portable')
if ((Get-Content (Join-Path $run 'cli-portable/WinMon.Cli.exe') -Raw) -cne 'CLI:SIGNED') { throw 'CLI-only executable was not signed.' }
Expand-Archive (Join-Path $output 'WinMon-Mcp-9.8.7-win-x64.zip') (Join-Path $run 'mcp-portable')
if ((Get-Content (Join-Path $run 'mcp-portable/WinMon.Mcp.exe') -Raw) -cne 'MCP:SIGNED') { throw 'MCP-only executable was not signed.' }
$plugin = Join-Path $run 'mcp-portable/claude-code-plugin'
foreach ($file in @('.claude-plugin/plugin.json', '.mcp.json', 'skills/diagnose-performance/SKILL.md', 'README.md')) {
    if (!(Test-Path (Join-Path $plugin $file))) { throw "MCP ZIP plugin is missing $file." }
}
if ((Get-Content (Join-Path $plugin '.claude-plugin/plugin.json') -Raw | ConvertFrom-Json).version -cne '9.8.7') {
    throw 'Claude Code plugin version did not follow the release version.'
}
if (Get-ChildItem $output -Filter 'WinMon-Claude-Code-*.zip') { throw 'Standalone plugin ZIP must not be emitted.' }
$installerHash = (Get-FileHash (Join-Path $output 'WinMon-9.8.7-Setup-x64.exe')).Hash
foreach ($line in Get-Content (Join-Path $output 'SHA256SUMS.txt')) {
    if ($line -like '*-winget.zip') { throw 'Public checksums must exclude Actions-only WinGet material.' }
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
foreach ($extension in @('json', 'sig')) {
    if ((Get-FileHash (Join-Path $output "update-manifest.$extension")).Hash -cne
        (Get-FileHash (Join-Path $output "desktop-update-manifest.$extension")).Hash) {
        throw 'Legacy desktop metadata must exactly match the signed desktop metadata.'
    }
}
# Run the workflow's publication block with a recording gh boundary; nothing is published.
Copy-Item (Join-Path $root 'packaging/RELEASE-NOTES.md') (Join-Path $fixture 'packaging')
New-Item -ItemType Directory -Path (Join-Path $fixture 'artifacts') -Force | Out-Null
Copy-Item $output (Join-Path $fixture 'artifacts/release') -Recurse
function gh { $testState.ReleaseArgs = @($args); $global:LASTEXITCODE = 0 }
$workflow = Get-Content (Join-Path $root '.github/workflows/release.yml') -Raw
$publication = ($workflow.Substring($workflow.LastIndexOf('        run: |')) -split '\r?\n' | Select-Object -Skip 1) -replace '^          ', ''
$savedEnvironment = @{}
foreach ($name in @('RELEASE_TAG', 'RELEASE_REPOSITORY', 'RUNNER_TEMP')) { $savedEnvironment[$name] = [Environment]::GetEnvironmentVariable($name) }
Push-Location $fixture
try {
    $env:RELEASE_TAG = 'v9.8.7'; $env:RELEASE_REPOSITORY = 'example/WinMon'; $env:RUNNER_TEMP = $run
    & ([scriptblock]::Create($publication -join "`n"))
    $published = @($testState.ReleaseArgs | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf })
    # The notes file is an argument, not an uploaded asset.
    $published = @($published | Where-Object { [IO.Path]::GetExtension($_) -ne '.md' })
    if ($published.Count -ne 15 -or ($published | Where-Object { $_ -like '*-winget.zip' -or $_ -like '*Claude-Code*' })) { throw 'Expected 15 public release assets without WinGet/plugin ZIPs.' }
    foreach ($line in Get-Content (Join-Path $output 'SHA256SUMS.txt')) {
        if (($line -split '  ', 2)[1] -notin @($published | ForEach-Object { Split-Path $_ -Leaf })) { throw 'Checksums refer to an unpublished asset.' }
    }
    $notesIndex = [Array]::IndexOf($testState.ReleaseArgs, '--notes-file')
    if ($notesIndex -lt 0 -or '--generate-notes' -notin $testState.ReleaseArgs) { throw 'Release must include download notes and generated changes.' }
    $notes = Get-Content -LiteralPath $testState.ReleaseArgs[$notesIndex + 1] -Raw
    $links = [regex]::Matches($notes, 'https://github.com/example/WinMon/releases/download/v9\.8\.7/([^\)]+)')
    if ($links.Count -ne 6 -or $notes -match '<version>|<repository>') { throw 'Download table must contain six resolved links.' }
    foreach ($link in $links) {
        if ($link.Groups[1].Value -notin @($published | ForEach-Object { Split-Path $_ -Leaf })) { throw 'Download table links to an unpublished asset.' }
    }
}
finally {
    Pop-Location
    foreach ($name in $savedEnvironment.Keys) { [Environment]::SetEnvironmentVariable($name, $savedEnvironment[$name]) }
}
$expectedErrors = @{
    invalid = 'Authenticode verification failed'
    'installer-invalid' = 'Authenticode verification failed'
    'wrong-publisher' = 'Unexpected signing publisher'
    'no-timestamp' = 'Missing timestamp'
    'provider-error' = 'Simulated Azure signing failure'
    'pawnio-mismatch' = 'PawnIO installer SHA-256 does not match'
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
if (Test-Path (Join-Path $run 'unsigned/update-manifest.json')) { throw 'Unsigned release emitted legacy update metadata.' }
# The fixture key is only needed for this disposable signing-boundary test.
Remove-Item -LiteralPath (Join-Path $run 'private.pem') -Force -ErrorAction SilentlyContinue
Write-Host "PASS: signing order, desktop, CLI and MCP payloads, bundled plugin, 15 public assets, download table, public checksums, RSA manifest, signing failure gates, and unsigned local packaging. Evidence: $run"
