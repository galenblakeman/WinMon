#requires -Version 7.0
[CmdletBinding()]
param([Parameter(Mandatory)][string]$OutputPath)
$ErrorActionPreference = 'Stop'
$pin = Get-Content -LiteralPath (Join-Path $PSScriptRoot '../packaging/PawnIO.json') -Raw | ConvertFrom-Json
if ($pin.url -cne "https://github.com/namazso/PawnIO.Setup/releases/download/$($pin.version)/PawnIO_setup.exe" -or
    $pin.sha256 -notmatch '^[A-Fa-f0-9]{64}$') { throw 'Invalid pinned PawnIO release.' }
$output = [IO.Path]::GetFullPath($OutputPath)
if (Test-Path -LiteralPath $output) { throw 'PawnIO output already exists.' }
New-Item -ItemType Directory -Path (Split-Path $output -Parent) -Force | Out-Null
try {
    Invoke-WebRequest -Uri $pin.url -OutFile $output
    if ((Get-FileHash -LiteralPath $output -Algorithm SHA256).Hash -ine $pin.sha256) {
        throw 'PawnIO installer SHA-256 does not match the pinned official release.'
    }
}
catch {
    if (Test-Path -LiteralPath $output) { Remove-Item -LiteralPath $output -Force }
    throw
}
