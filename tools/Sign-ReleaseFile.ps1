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
# azure/login exchanges the GitHub OIDC token into an Azure CLI session.
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
