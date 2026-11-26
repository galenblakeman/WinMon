#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateScript({ Test-Path -LiteralPath $_ -PathType Leaf })][string]$Path,
    [Parameter(Mandatory)][string]$Endpoint,
    [Parameter(Mandatory)][string]$AccountName,
    [Parameter(Mandatory)][string]$CertificateProfileName,
    [Parameter(Mandatory)][ValidateNotNullOrEmpty()][string]$ExpectedPublisher
)
$ErrorActionPreference = 'Stop'
Import-Module ArtifactSigning -RequiredVersion 0.1.20 -ErrorAction Stop
$file = (Resolve-Path -LiteralPath $Path).Path
# The runner assertion expires before lengthy tests and builds can finish. Obtain
# it at signing time, when Azure CLI can exchange it for Artifact Signing access.
if ($env:WINMON_SIGNING_GITHUB_OIDC -ceq 'true') {
    if ($env:GITHUB_ACTIONS -cne 'true' -or !$env:ACTIONS_ID_TOKEN_REQUEST_URL -or !$env:ACTIONS_ID_TOKEN_REQUEST_TOKEN -or
        !$env:AZURE_CLIENT_ID -or !$env:AZURE_TENANT_ID -or !$env:AZURE_SUBSCRIPTION_ID) {
        throw 'GitHub OIDC signing requires the runner token endpoint and the Azure application, tenant and subscription IDs.'
    }
    $oidcUrl = $env:ACTIONS_ID_TOKEN_REQUEST_URL + '&audience=' + [uri]::EscapeDataString('api://AzureADTokenExchange')
    $oidcHeaders = @{ Authorization = "Bearer $env:ACTIONS_ID_TOKEN_REQUEST_TOKEN" }
    $assertion = (Invoke-RestMethod -Uri $oidcUrl -Headers $oidcHeaders -MaximumRedirection 0 -TimeoutSec 30).value
    if ($assertion -cnotmatch '^[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+$') { throw 'GitHub returned an invalid OIDC assertion.' }
    Write-Host "::add-mask::$assertion"
    az login --service-principal --username $env:AZURE_CLIENT_ID --tenant $env:AZURE_TENANT_ID --federated-token $assertion --only-show-errors --output none
    if ($LASTEXITCODE -ne 0) { throw 'GitHub OIDC sign-in to Azure CLI failed.' }
    az account set --subscription $env:AZURE_SUBSCRIPTION_ID --only-show-errors
    if ($LASTEXITCODE -ne 0) { throw 'Could not select the Azure signing subscription.' }
}

# Disable every other credential so a workstation/runner identity cannot be used accidentally.
$parameters = @{
    Endpoint = $Endpoint
    CodeSigningAccountName = $AccountName
    CertificateProfileName = $CertificateProfileName
    Files = $file
    FileDigest = 'SHA256'
    TimestampRfc3161 = 'http://timestamp.acs.microsoft.com'
    TimestampDigest = 'SHA256'
    Description = 'WinMon'
    DescriptionUrl = 'https://github.com/galenblakeman/WinMon'
    ExcludeEnvironmentCredential = $true
    ExcludeWorkloadIdentityCredential = $true
    ExcludeManagedIdentityCredential = $true
    ExcludeSharedTokenCacheCredential = $true
    ExcludeVisualStudioCredential = $true
    ExcludeVisualStudioCodeCredential = $true
    ExcludeAzureCliCredential = $false
    ExcludeAzurePowerShellCredential = $true
    ExcludeAzureDeveloperCliCredential = $true
    ExcludeInteractiveBrowserCredential = $true
}
# Provider output belongs in the build log, not in the JSON evidence returned below.
Invoke-ArtifactSigning @parameters | Out-Host
$signature = Get-AuthenticodeSignature -LiteralPath $file
if ($signature.Status -ne 'Valid') { throw "Authenticode verification failed for ${file}: $($signature.Status) ($($signature.StatusMessage))" }
if (!$signature.SignerCertificate -or $signature.SignerCertificate.GetNameInfo([Security.Cryptography.X509Certificates.X509NameType]::SimpleName, $false) -cne $ExpectedPublisher) {
    throw "Unexpected signing publisher for ${file}; expected '$ExpectedPublisher'."
}
if (!$signature.TimeStamperCertificate) { throw "Missing timestamp on $file." }
[pscustomobject]@{
    File = [IO.Path]::GetFileName($file)
    Sha256 = (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant()
    Status = [string]$signature.Status
    Publisher = $signature.SignerCertificate.Subject
    CertificateThumbprint = $signature.SignerCertificate.Thumbprint
    TimestampAuthority = $signature.TimeStamperCertificate.Subject
}
