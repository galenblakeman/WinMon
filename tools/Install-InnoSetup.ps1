#requires -Version 7.0
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$toolDirectory = Join-Path $root 'artifacts/toolchain/innosetup-6.7.3'
$compiler = Join-Path $toolDirectory 'ISCC.exe'
if (Test-Path -LiteralPath $compiler) { Write-Output $compiler; return }
$download = Join-Path $root 'artifacts/toolchain/innosetup-6.7.3.exe'
New-Item -ItemType Directory -Path (Split-Path $download) -Force | Out-Null
Invoke-WebRequest 'https://github.com/jrsoftware/issrc/releases/download/is-6_7_3/innosetup-6.7.3.exe' -OutFile $download
$expected = '9c73c3bae7ed48d44112a0f48e66742c00090bdb5bef71d9d3c056c66e97b732'
if ((Get-FileHash -LiteralPath $download -Algorithm SHA256).Hash -ne $expected) { throw 'Inno Setup download checksum mismatch.' }
if ((Get-AuthenticodeSignature -LiteralPath $download).Status -ne 'Valid') { throw 'Inno Setup download signature is not valid.' }
$process = Start-Process -FilePath $download -ArgumentList @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/CURRENTUSER', '/NOICONS', "/DIR=`"$toolDirectory`"") -WindowStyle Hidden -Wait -PassThru
if ($process.ExitCode -ne 0 -or !(Test-Path -LiteralPath $compiler)) { throw "Inno Setup installation failed: $($process.ExitCode)" }
Write-Output $compiler
