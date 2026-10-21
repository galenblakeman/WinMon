#requires -Version 7.4
[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateScript({ Test-Path -LiteralPath $_ -PathType Leaf })][string]$PublicKeyPath
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$destinationDirectory = Join-Path $root 'src/WinMon/Updates'
$destination = Join-Path $destinationDirectory 'update-public-key.pem'
$pem = [IO.File]::ReadAllText((Resolve-Path -LiteralPath $PublicKeyPath))
$rsa = [Security.Cryptography.RSA]::Create()
try {
    $rsa.ImportFromPem($pem)
    if ($rsa.KeySize -lt 2048) { throw 'Update signing keys must be RSA 2048 bits or stronger.' }
    New-Item -ItemType Directory -Path $destinationDirectory -Force | Out-Null
    [IO.File]::WriteAllText($destination, $rsa.ExportSubjectPublicKeyInfoPem(), [Text.UTF8Encoding]::new($false))
}
finally { $rsa.Dispose() }

Write-Host "Trusted update public key written to $destination"
