#requires -Version 7.0
[CmdletBinding()]
param(
    [ValidateNotNullOrEmpty()][string]$PackageName,
    [ValidateNotNullOrEmpty()][string]$Publisher,
    [ValidateNotNullOrEmpty()][string]$PublisherDisplayName,
    [ValidateNotNullOrEmpty()][string]$DisplayName,
    [string]$OutputDirectory,
    [string]$MakeAppxPath,
    [switch]$DisableNuGetAudit
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$storeIdentity = Import-PowerShellDataFile -LiteralPath (Join-Path $root 'packaging/msix/StoreIdentity.psd1')
foreach ($field in @('PackageName', 'Publisher', 'PublisherDisplayName', 'DisplayName')) {
    if (!$PSBoundParameters.ContainsKey($field)) { Set-Variable -Name $field -Value ([string]$storeIdentity[$field]) }
    if (!(Get-Variable -Name $field -ValueOnly)) { throw "Missing MSIX identity value: $field" }
}
[xml]$props = Get-Content -LiteralPath (Join-Path $root 'Directory.Build.props') -Raw
$version = [string]$props.Project.PropertyGroup.Version
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw 'Release Version must have the form major.minor.patch.' }
$packageVersion = [version]"$version.0"
if ($packageVersion.Major -lt 1 -or $packageVersion.Major -gt 65535 -or $packageVersion.Minor -gt 65535 -or $packageVersion.Build -gt 65535) {
    throw 'MSIX requires major 1..65535, minor and patch 0..65535, and revision 0.'
}
[xml]$applicationManifest = Get-Content -LiteralPath (Join-Path $root 'src/WinMon/app.manifest') -Raw
if ($applicationManifest.assembly.assemblyIdentity.version -ne "$version.0") { throw 'Application manifest version does not match Version.' }
if ($PackageName -notmatch '^[A-Za-z0-9][A-Za-z0-9.-]{2,49}$') { throw 'PackageName must be a valid 3-50 character MSIX identity name.' }
# Validate distinguished-name syntax; the Store value must be copied exactly from Partner Center.
$null = [Security.Cryptography.X509Certificates.X500DistinguishedName]::new($Publisher)
[xml]$packageTemplate = Get-Content -LiteralPath (Join-Path $root 'packaging/msix/AppxManifest.xml') -Raw
if ([version]$packageTemplate.Package.Dependencies.TargetDeviceFamily.MinVersion -lt [version]'10.0.17763.0') {
    throw 'Microsoft Store MSIX packages require Windows 10 version 1809 (10.0.17763.0) or later.'
}
if (!$MakeAppxPath) {
    $sdkBin = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits/10/bin'
    $MakeAppxPath = Get-ChildItem -LiteralPath $sdkBin -Directory |
        Where-Object { $_.Name -match '^10\.0\.\d+\.0$' } |
        Sort-Object { [version]$_.Name } -Descending |
        ForEach-Object { Join-Path $_.FullName 'x64/makeappx.exe' } |
        Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } |
        Select-Object -First 1
}
if (!$MakeAppxPath -or !(Test-Path -LiteralPath $MakeAppxPath -PathType Leaf)) {
    throw 'Install the Windows SDK (MakeAppx x64), or supply -MakeAppxPath.'
}
$MakeAppxPath = (Resolve-Path -LiteralPath $MakeAppxPath).Path
if (!$OutputDirectory) { $OutputDirectory = Join-Path $root "artifacts/msix/$version" }
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $OutputDirectory) { throw "Output directory already exists: $OutputDirectory" }
$work = Join-Path $root "artifacts/msix-build/$([guid]::NewGuid().ToString('N'))"
$payload = Join-Path $work 'payload'
New-Item -ItemType Directory -Path $payload -Force | Out-Null
Push-Location $root
try {
    $publishOptions = @()
    if ($DisableNuGetAudit) { $publishOptions += '-p:NuGetAudit=false' }
    # Separate output prevents StoreBuild from leaking into installer/portable releases.
    # MSIX compresses the directory; no single-file extraction or separately installed .NET runtime is needed.
    dotnet publish src/WinMon/WinMon.csproj -c Release -r win-x64 --self-contained true -m:1 -p:UseSharedCompilation=false -p:StoreBuild=true -p:PublishSingleFile=false --artifacts-path "$work/build" -o $payload @publishOptions
    if ($LASTEXITCODE -ne 0) { throw 'Store publish failed.' }
    if (Test-Path -LiteralPath (Join-Path $payload 'portable.flag')) { throw 'MSIX must not include portable.flag.' }
    Copy-Item -LiteralPath (Join-Path $root 'LICENSE') -Destination $payload
    [xml]$manifest = Get-Content -LiteralPath (Join-Path $root 'packaging/msix/AppxManifest.xml') -Raw
    $manifest.Package.Identity.Name = $PackageName
    $manifest.Package.Identity.Publisher = $Publisher
    $manifest.Package.Identity.Version = "$version.0"
    $manifest.Package.Properties.DisplayName = $DisplayName
    $manifest.Package.Properties.PublisherDisplayName = $PublisherDisplayName
    $manifest.Package.Applications.Application.VisualElements.DisplayName = $DisplayName
    $manifest.Save((Join-Path $payload 'AppxManifest.xml'))

    # Derive package logos from the existing product artwork, rather than maintaining duplicate sources.
    Add-Type -AssemblyName System.Drawing.Common
    $assets = Join-Path $payload 'Assets'
    New-Item -ItemType Directory -Path $assets | Out-Null
    $image = [Drawing.Image]::FromFile((Join-Path $root 'src/WinMon/Assets/WinMon.png'))
    try {
        foreach ($logo in @(@('StoreLogo', 50), @('Square44x44Logo', 44), @('Square150x150Logo', 150))) {
            $size = [int]$logo[1]
            $bitmap = [Drawing.Bitmap]::new($size, $size)
            try {
                $graphics = [Drawing.Graphics]::FromImage($bitmap)
                try {
                    $graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
                    $scale = [Math]::Min($size / $image.Width, $size / $image.Height)
                    $width = [int]($image.Width * $scale)
                    $height = [int]($image.Height * $scale)
                    $graphics.DrawImage($image, [int](($size - $width) / 2), [int](($size - $height) / 2), $width, $height)
                }
                finally { $graphics.Dispose() }
                $bitmap.Save((Join-Path $assets ($logo[0] + '.png')), [Drawing.Imaging.ImageFormat]::Png)
            }
            finally { $bitmap.Dispose() }
        }
    }
    finally { $image.Dispose() }
    $package = Join-Path $work "WinMon-$version-x64.msix"
    # Keep schema and semantic validation enabled. /l only allows the declared language resources.
    $packLog = Join-Path $work 'makeappx.log'
    & $MakeAppxPath pack /d $payload /p $package /l /no *> $packLog
    if ($LASTEXITCODE -ne 0) {
        Get-Content -LiteralPath $packLog -Tail 30 | Write-Host
        throw ('MSIX packaging/validation failed (0x{0:X8}). See {1}' -f $LASTEXITCODE, $packLog)
    }
    New-Item -ItemType Directory -Path $OutputDirectory | Out-Null
    Copy-Item -LiteralPath $package -Destination $OutputDirectory
    '{0}  {1}' -f (Get-FileHash -LiteralPath $package -Algorithm SHA256).Hash.ToLowerInvariant(), [IO.Path]::GetFileName($package) |
        Set-Content -LiteralPath (Join-Path $OutputDirectory 'SHA256SUMS.txt') -Encoding utf8NoBOM
    Write-Host "Unsigned MSIX: $OutputDirectory"
    Write-Host "Build evidence and staged manifest: $work"
    Write-Host 'For Store submission use the exact Partner Center identity. Local installation requires a trusted package signature.'
}
finally { Pop-Location }
