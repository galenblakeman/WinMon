#requires -Version 7.4
[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version,
    [Parameter(Mandatory)][ValidateScript({ Test-Path -LiteralPath $_ -PathType Leaf })][string]$PortablePath,
    [Parameter(Mandatory)][ValidateScript({ Test-Path -LiteralPath $_ -PathType Leaf })][string]$InstallerPath,
    [Parameter(Mandatory)][ValidateScript({ Test-Path -LiteralPath $_ -PathType Leaf })][string]$SigningKeyPath,
    [Parameter(Mandatory)][ValidateScript({ Test-Path -LiteralPath $_ -PathType Container })][string]$OutputDirectory,
    [ValidateSet('WinMon', 'WinMon.Cli', 'WinMon.Mcp')][string]$Product = 'WinMon'
)

$ErrorActionPreference = 'Stop'
$maximumAssetBytes = 512MB
$assetPrefix = $Product.Replace('.', '-')
$manifestPrefix = switch ($Product) { 'WinMon' { 'desktop' } 'WinMon.Cli' { 'cli' } 'WinMon.Mcp' { 'mcp' } }

function Get-Asset([string]$path, [string]$expectedName) {
    $item = Get-Item -LiteralPath $path
    if ($item.Name -cne $expectedName) { throw "Expected update asset '$expectedName', found '$($item.Name)'." }
    if ($item.Length -gt $maximumAssetBytes) { throw "Update asset '$($item.Name)' exceeds the 512 MiB limit." }
    [ordered]@{
        name = $item.Name
        sha256 = (Get-FileHash -LiteralPath $item.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        size = [int64]$item.Length
    }
}

$document = [ordered]@{
    schemaVersion = 1
    version = $Version
    runtime = 'win-x64'
    portable = Get-Asset $PortablePath "$assetPrefix-$Version-win-x64.zip"
    installer = Get-Asset $InstallerPath "$assetPrefix-$Version-Setup-x64.exe"
}

# The serialized UTF-8 bytes are the signed object. Do not reformat the file after this point.
$json = $document | ConvertTo-Json -Depth 3 -Compress
$bytes = [Text.UTF8Encoding]::new($false).GetBytes($json)
$manifestPath = Join-Path $OutputDirectory "$manifestPrefix-update-manifest.json"
[IO.File]::WriteAllBytes($manifestPath, $bytes)

$keyPem = [IO.File]::ReadAllText((Resolve-Path -LiteralPath $SigningKeyPath))
$rsa = [Security.Cryptography.RSA]::Create()
try {
    $rsa.ImportFromPem($keyPem)
    if ($rsa.KeySize -lt 2048) { throw 'Update signing keys must be RSA 2048 bits or stronger.' }
    $signature = $rsa.SignData($bytes, [Security.Cryptography.HashAlgorithmName]::SHA256, [Security.Cryptography.RSASignaturePadding]::Pkcs1)
    [IO.File]::WriteAllText((Join-Path $OutputDirectory "$manifestPrefix-update-manifest.sig"), [Convert]::ToBase64String($signature), [Text.UTF8Encoding]::new($false))
}
finally { $rsa.Dispose() }
