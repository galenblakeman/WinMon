#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version,
    [Parameter(Mandatory)][string]$InstallerPath,
    [Parameter(Mandatory)][string]$OutputDirectory
)
$ErrorActionPreference = 'Stop'
$expectedName = "WinMon-$Version-Setup-x64.exe"
if ((Split-Path $InstallerPath -Leaf) -cne $expectedName) { throw "Expected installer named $expectedName" }
$hash = (Get-FileHash -LiteralPath $InstallerPath -Algorithm SHA256).Hash
$repository = 'https://github.com/galenblakeman/WinMon'
$id = 'GalenBlakeman.WinMon'
$directory = Join-Path $OutputDirectory "manifests/g/GalenBlakeman/WinMon/$Version"
New-Item -ItemType Directory -Path $directory -Force | Out-Null
@"
# yaml-language-server: `$schema=https://aka.ms/winget-manifest.version.1.10.0.schema.json
PackageIdentifier: $id
PackageVersion: $Version
DefaultLocale: en-US
ManifestType: version
ManifestVersion: 1.10.0
"@ | Set-Content -LiteralPath (Join-Path $directory "$id.yaml") -Encoding utf8NoBOM
@"
# yaml-language-server: `$schema=https://aka.ms/winget-manifest.defaultLocale.1.10.0.schema.json
PackageIdentifier: $id
PackageVersion: $Version
PackageLocale: en-US
Publisher: Galen Blakeman
PublisherUrl: https://github.com/galenblakeman
PublisherSupportUrl: $repository/issues
PackageName: WinMon
PackageUrl: $repository
License: MIT
LicenseUrl: $repository/blob/v$Version/LICENSE
ShortDescription: A Windows resource monitor for CPU, memory, disk, network and GPU activity.
Tags:
- monitoring
- resource-monitor
- wpf
ReleaseNotesUrl: $repository/releases/tag/v$Version
ManifestType: defaultLocale
ManifestVersion: 1.10.0
"@ | Set-Content -LiteralPath (Join-Path $directory "$id.locale.en-US.yaml") -Encoding utf8NoBOM
@"
# yaml-language-server: `$schema=https://aka.ms/winget-manifest.installer.1.10.0.schema.json
PackageIdentifier: $id
PackageVersion: $Version
InstallerType: inno
Scope: user
MinimumOSVersion: 10.0.14393.0
UpgradeBehavior: install
UnsupportedOSArchitectures:
- arm64
InstallerSwitches:
  Silent: /VERYSILENT /SUPPRESSMSGBOXES /NORESTART
  SilentWithProgress: /SILENT /SUPPRESSMSGBOXES /NORESTART
Installers:
- Architecture: x64
  InstallerUrl: $repository/releases/download/v$Version/$expectedName
  InstallerSha256: $hash
  ProductCode: '{D736D24F-6A1E-4FD4-AC66-790F93DC61B4}_is1'
  AppsAndFeaturesEntries:
  - DisplayName: WinMon version $Version
    Publisher: Galen Blakeman
    ProductCode: '{D736D24F-6A1E-4FD4-AC66-790F93DC61B4}_is1'
ManifestType: installer
ManifestVersion: 1.10.0
"@ | Set-Content -LiteralPath (Join-Path $directory "$id.installer.yaml") -Encoding utf8NoBOM
Write-Output $directory
