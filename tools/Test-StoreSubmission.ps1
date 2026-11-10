#requires -Version 7.0
# Offline contract tests: real MSIX/ZIP handling, fake authentication and Store HTTP boundary.
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$run = Join-Path (Split-Path $PSScriptRoot -Parent) "artifacts/store-submission-validation/$([guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Path $run -Force | Out-Null
$savedEnvironment = @{}
foreach ($name in @('STORE_TENANT_ID', 'STORE_CLIENT_ID', 'STORE_CLIENT_SECRET', 'GITHUB_STEP_SUMMARY', 'GITHUB_ACTIONS')) {
    $savedEnvironment[$name] = [Environment]::GetEnvironmentVariable($name)
}
$state = @{}
function Assert($Condition, [string]$Message) { if (!$Condition) { throw $Message } }
function Invoke-RestMethod {
    param($Method, $Uri, $Body, $Headers, $TimeoutSec, $MaximumRedirection, $ContentType, $InFile)
    $state.Calls.Add("$Method $Uri")
    if ("$Uri" -like 'https://login.microsoftonline.com/*') {
        Assert ($Body.client_secret -ceq 'offline-secret') 'Authentication did not use the secret.'
        return @{ access_token = 'offline-token' }
    }
    if ("$Uri" -like 'https://upload.blob.core.windows.net/*') {
        Assert (!$Headers.Authorization) 'Bearer token sent to upload storage.'
        Assert ($Headers['x-ms-blob-type'] -eq 'BlockBlob') 'Missing BlockBlob header.'
        $zip = [IO.Compression.ZipFile]::OpenRead($InFile)
        try {
            Assert ($zip.Entries.Count -eq 1 -and $zip.Entries[0].FullName -eq 'WinMon.msix') 'Wrong upload ZIP contents.'
            $stream = $zip.Entries[0].Open()
            try {
                $sha = [Security.Cryptography.SHA256]::HashData($stream)
                Assert ([Convert]::ToHexString($sha) -eq (Get-FileHash $state.PackagePath).Hash) 'Upload did not preserve MSIX bytes.'
            } finally { $stream.Dispose() }
        } finally { $zip.Dispose() }
        if ($state.Mode -eq 'upload-error') { throw 'Fake transport error containing secret upload URL' }
        $state.Uploaded = $true
        return
    }
    Assert ($Headers.Authorization -eq 'Bearer offline-token') 'Missing Store bearer token.'
    if ("$Uri" -match '/applications/9P6GPPWHZX15$') {
        return @{
            packageIdentityName = $(if ($state.Mode -eq 'wrong-identity') { 'Other.App' } else { 'Test.WinMon' })
            publisherName = 'CN=Test'
            pendingApplicationSubmission = $(if ($state.Mode -eq 'pending') { @{ id = 'existing' } } else { $null })
            lastPublishedApplicationSubmission = @{ id = 'published' }
        }
    }
    if ("$Uri" -match '/submissions/published$' -or ("$Uri" -match '/submissions$' -and $Method -eq 'POST')) {
        $value = @{
            id = 'new-submission'
            fileUploadUrl = 'https://upload.blob.core.windows.net/container/package.zip?sig=private'
            targetPublishMode = 'Manual'
            listings = @{ 'en-us' = @{ baseListing = @{ description = 'Existing listing'; images = @(@{ id = 'image'; fileStatus = 'Uploaded' }) } } }
            pricing = @{ priceId = 'Free' }
            applicationPackages = @(
                @{ fileName = 'old.msix'; architecture = 'x64'; version = $(if ($state.Mode -eq 'same-version') { '9.8.7.0' } else { '1.0.0.0' }); fileStatus = 'Uploaded' },
                @{ fileName = 'arm.msix'; architecture = 'ARM64'; version = '1.0.0.0'; fileStatus = 'Uploaded' }
            )
        }
        if ($state.Mode -eq 'bundle') { $value.applicationPackages[0].fileName = 'old.msixbundle' }
        if ($state.Mode -eq 'bad-upload-url') { $value.fileUploadUrl = 'https://example.com/private' }
        return $value | ConvertTo-Json -Depth 30 | ConvertFrom-Json
    }
    if ($Method -eq 'PUT') { $state.Update = $Body | ConvertFrom-Json; return }
    if ("$Uri" -match '/commit$') {
        Assert $state.Uploaded 'Committed before upload completed.'
        $state.Committed = $true
        return
    }
    if ("$Uri" -match '/status$') {
        return @{ status = $(if ($state.Mode -eq 'commit-failed') { 'CommitFailed' } else { 'PreProcessing' }) }
    }
    throw "Unexpected HTTP call: $Method $Uri"
}
try {
    $env:STORE_TENANT_ID = '11111111-1111-1111-1111-111111111111'
    $env:STORE_CLIENT_ID = '22222222-2222-2222-2222-222222222222'
    $env:STORE_CLIENT_SECRET = 'offline-secret'
    $env:GITHUB_STEP_SUMMARY = Join-Path $run 'summary.md'
    $env:GITHUB_ACTIONS = 'false'
    $payload = Join-Path $run 'payload'
    New-Item -ItemType Directory -Path $payload | Out-Null
    '<Package><Identity Name="Test.WinMon" Publisher="CN=Test" Version="9.8.7.0" ProcessorArchitecture="x64" /></Package>' |
        Set-Content -LiteralPath (Join-Path $payload 'AppxManifest.xml')
    $packagePath = Join-Path $run 'WinMon.msix'
    [IO.Compression.ZipFile]::CreateFromDirectory($payload, $packagePath)
    $submit = Join-Path $PSScriptRoot 'Submit-StoreUpdate.ps1'
    foreach ($mode in @('valid', 'wrong-tag', 'missing-secret', 'wrong-identity', 'pending', 'same-version', 'bundle', 'bad-upload-url', 'upload-error', 'commit-failed')) {
        $state = @{ Mode = $mode; Calls = [Collections.Generic.List[string]]::new(); PackagePath = $packagePath; Uploaded = $false; Committed = $false; Update = $null }
        $tag = if ($mode -eq 'wrong-tag') { 'v9.8.6' } else { 'v9.8.7' }
        $env:STORE_CLIENT_SECRET = if ($mode -eq 'missing-secret') { '' } else { 'offline-secret' }
        $failure = $null
        try { & $submit -PackagePath $packagePath -ExpectedTag $tag }
        catch { $failure = $_.Exception.Message }
        if ($mode -eq 'valid') {
            Assert (!$failure -and $state.Committed) "Valid submission failed: $failure"
            Assert ($state.Update.targetPublishMode -eq 'Immediate') 'Publication mode was not updated.'
            Assert ($state.Update.applicationPackages.Count -eq 3) 'Packages were lost.'
            Assert ($state.Update.applicationPackages[0].fileStatus -eq 'PendingDelete') 'Old x64 package not replaced.'
            Assert ($state.Update.applicationPackages[1].fileStatus -eq 'Uploaded') 'Other architecture was changed.'
            Assert ($state.Update.applicationPackages[2].fileStatus -eq 'PendingUpload') 'New package not registered.'
            Assert ($state.Update.listings.'en-us'.baseListing.description -eq 'Existing listing') 'Listing not preserved.'
            Assert ($state.Update.pricing.priceId -eq 'Free') 'Pricing not preserved.'
        }
        else {
            $expected = @{
                'wrong-tag' = '*version does not match*'; 'missing-secret' = '*Missing secret*'
                'wrong-identity' = '*identity does not match*'; pending = '*already has pending*'
                'same-version' = '*already has this x64 version*'; bundle = '*bundle or unsupported*'
                'bad-upload-url' = '*unexpected upload URL*'; 'upload-error' = '*Package upload failed*'
                'commit-failed' = '*failed: CommitFailed*'
            }
            Assert ($failure -like $expected[$mode]) "Wrong failure for ${mode}: $failure"
            if ($mode -notin @('bad-upload-url', 'upload-error', 'commit-failed')) {
                Assert (@($state.Calls | Where-Object { $_ -match '^(POST|PUT) https://manage' }).Count -eq 0) "Mutated Store for $mode."
            }
            if ($mode -ne 'commit-failed') { Assert (!$state.Committed) "Committed invalid submission for $mode." }
            Assert ($failure -notmatch 'sig=private|offline-secret|offline-token') 'Credentials leaked into failure message.'
        }
    }
    Write-Host "PASS: MSIX upload bytes, listing preservation, architecture replacement, publication mode, and nine failure cases. Evidence: $run"
}
finally {
    foreach ($name in $savedEnvironment.Keys) { [Environment]::SetEnvironmentVariable($name, $savedEnvironment[$name]) }
}
