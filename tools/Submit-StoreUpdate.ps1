#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$PackagePath,
    [ValidatePattern('^[A-Za-z0-9]{12}$')][string]$ProductId = '9P6GPPWHZX15',
    [Parameter(Mandatory)][ValidatePattern('^v\d+\.\d+\.\d+$')][string]$ExpectedTag,
    [ValidateRange(1, 30)][int]$PollTimeoutMinutes = 20
)
$ErrorActionPreference = 'Stop'

function Write-SubmissionSummary([string]$Message) {
    Write-Host $Message
    if ($env:GITHUB_STEP_SUMMARY) { Add-Content -LiteralPath $env:GITHUB_STEP_SUMMARY -Value $Message }
}

function Invoke-StoreRequest([string]$Method, [string]$Path, $Body) {
    $parameters = @{
        Method = $Method
        Uri = "https://manage.devcenter.microsoft.com/v1.0/my/$Path"
        Headers = @{ Authorization = "Bearer $accessToken" }
        TimeoutSec = 120
        MaximumRedirection = 0
    }
    if ($null -ne $Body) {
        $parameters.ContentType = 'application/json; charset=utf-8'
        $parameters.Body = ConvertTo-Json -InputObject $Body -Depth 100 -Compress
    }
    try { Invoke-RestMethod @parameters }
    catch {
        # Do not print request bodies, bearer tokens or upload SAS URLs.
        $code = if ($_.Exception.Response) { [int]$_.Exception.Response.StatusCode } else { 'transport error' }
        throw "Store API $Method $Path failed ($code). Check Partner Center before retrying; mutations are not automatically retried."
    }
}

foreach ($name in @('STORE_TENANT_ID', 'STORE_CLIENT_ID', 'STORE_CLIENT_SECRET')) {
    if ([string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable($name))) { throw "Missing secret: $name" }
}
foreach ($name in @('STORE_TENANT_ID', 'STORE_CLIENT_ID')) {
    $null = [guid]::Parse([Environment]::GetEnvironmentVariable($name))
}
$PackagePath = (Resolve-Path -LiteralPath $PackagePath).Path
if ([IO.Path]::GetExtension($PackagePath) -ine '.msix') { throw 'Expected an MSIX package.' }
$archive = [IO.Compression.ZipFile]::OpenRead($PackagePath)
try {
    $entry = $archive.GetEntry('AppxManifest.xml')
    if (!$entry) { throw 'MSIX has no AppxManifest.xml.' }
    $reader = [IO.StreamReader]::new($entry.Open())
    try { [xml]$manifest = $reader.ReadToEnd() } finally { $reader.Dispose() }
}
finally { $archive.Dispose() }
$identity = $manifest.Package.Identity
$version = [version]$identity.Version
if ($identity.ProcessorArchitecture -ine 'x64') { throw 'Only x64 MSIX updates are supported.' }
if ($version -ne [version]"$($ExpectedTag.Substring(1)).0") { throw 'MSIX version does not match the published release tag.' }

try {
    $token = Invoke-RestMethod -Method Post -Uri "https://login.microsoftonline.com/$env:STORE_TENANT_ID/oauth2/token" -Body @{
        grant_type = 'client_credentials'
        client_id = $env:STORE_CLIENT_ID
        client_secret = $env:STORE_CLIENT_SECRET
        resource = 'https://manage.devcenter.microsoft.com'
    } -TimeoutSec 120 -MaximumRedirection 0
}
catch { throw 'Store authentication failed. Check the Entra tenant, application, secret expiry and Partner Center association.' }
$accessToken = $token.access_token
if (!$accessToken) { throw 'Authentication returned no access token.' }
if ($env:GITHUB_ACTIONS -eq 'true') { Write-Host "::add-mask::$accessToken" }
$appPath = "applications/$ProductId"
$app = Invoke-StoreRequest GET $appPath
if ($app.packageIdentityName -cne $identity.Name -or $app.publisherName -cne $identity.Publisher) {
    throw 'MSIX identity does not match the existing Partner Center app.'
}
if ($app.pendingApplicationSubmission.id) {
    throw "Partner Center already has pending submission $($app.pendingApplicationSubmission.id). Resolve it before retrying; it will not be overwritten."
}
if (!$app.lastPublishedApplicationSubmission.id) { throw 'The app must already have a published submission.' }
$published = Invoke-StoreRequest GET "$appPath/submissions/$($app.lastPublishedApplicationSubmission.id)"
foreach ($package in $published.applicationPackages) {
    if ($package.fileName -match '\.(msixbundle|appxbundle)$' -or $package.architecture -notin @('x64', 'x86', 'ARM', 'ARM64')) {
        throw 'Existing package uses a bundle or unsupported architecture. Review the package replacement strategy manually.'
    }
    if ($package.architecture -ieq 'x64' -and [version]$package.version -ge $version) {
        throw 'The Store already has this x64 version or a newer one. Bump the release version; do not resubmit it.'
    }
}

# Create the ZIP before making any Partner Center mutations. It contains only the new MSIX.
$work = Join-Path ([IO.Path]::GetTempPath()) "winmon-store-$([guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Path $work | Out-Null
$zipPath = Join-Path $work 'submission.zip'
try {
    $zip = [IO.Compression.ZipFile]::Open($zipPath, [IO.Compression.ZipArchiveMode]::Create)
    try {
        $null = [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $PackagePath, [IO.Path]::GetFileName($PackagePath), [IO.Compression.CompressionLevel]::NoCompression)
    }
    finally { $zip.Dispose() }
    $submission = Invoke-StoreRequest POST "$appPath/submissions"
    if (!$submission.id) { throw 'Store returned no submission ID. Inspect Partner Center before retrying.' }
    $submissionPath = "$appPath/submissions/$($submission.id)"
    Write-SubmissionSummary "Store product $ProductId, release $ExpectedTag, submission $($submission.id) created."
    [uri]$uploadUri = $submission.fileUploadUrl
    if ($uploadUri.Scheme -ne 'https' -or !$uploadUri.Host.EndsWith('.blob.core.windows.net')) {
        throw 'Store returned an unexpected upload URL. Submission was left for inspection.'
    }
    if ($env:GITHUB_ACTIONS -eq 'true') { Write-Host "::add-mask::$uploadUri" }
    # POST clones the last published listing, screenshots, pricing and certification notes.
    foreach ($package in $submission.applicationPackages) {
        if ($package.architecture -ieq 'x64') { $package.fileStatus = 'PendingDelete' }
    }
    $submission.applicationPackages = @($submission.applicationPackages) + @(@{
        fileName = [IO.Path]::GetFileName($PackagePath)
        fileStatus = 'PendingUpload'
        minimumDirectXVersion = 'None'
        minimumSystemRam = 'None'
    })
    $submission.targetPublishMode = 'Immediate'
    $null = Invoke-StoreRequest PUT $submissionPath $submission
    try {
        $null = Invoke-RestMethod -Method Put -Uri $uploadUri -InFile $zipPath -ContentType 'application/zip' -Headers @{
            'x-ms-blob-type' = 'BlockBlob'
            'x-ms-version' = '2021-12-02'
        } -TimeoutSec 600 -MaximumRedirection 0
    }
    catch { throw "Package upload failed for submission $($submission.id). Inspect Partner Center before retrying. Upload credentials were withheld from logs." }
    $null = Invoke-StoreRequest POST "$submissionPath/commit"
    Write-SubmissionSummary 'Commit requested. Publication is configured for after certification; it is not yet confirmed live.'
    $deadline = [DateTime]::UtcNow.AddMinutes($PollTimeoutMinutes)
    do {
        $status = Invoke-StoreRequest GET "$submissionPath/status"
        Write-Host "Submission $($submission.id): $($status.status)"
        if ($status.status -in @('CommitFailed', 'PreProcessingFailed', 'CertificationFailed', 'ReleaseFailed', 'PublishFailed', 'Canceled')) {
            Write-SubmissionSummary "Submission failed: $($status.status). Review details in Partner Center."
            throw "Store submission $($submission.id) failed: $($status.status)."
        }
        if ($status.status -in @('PreProcessing', 'Certification', 'PendingPublication', 'Release', 'Publishing', 'Published')) {
            Write-SubmissionSummary "Submission accepted: $($status.status). Follow certification and publication in Partner Center."
            return
        }
        Start-Sleep -Seconds 30
    } while ([DateTime]::UtcNow -lt $deadline)
    throw "Timed out waiting for submission $($submission.id) to be accepted. Check Partner Center before retrying; it may still complete."
}
finally {
    # Only this invocation's uniquely named temporary ZIP and empty directory are removed.
    if (Test-Path -LiteralPath $zipPath) { Remove-Item -LiteralPath $zipPath -Force }
    Remove-Item -LiteralPath $work
}
