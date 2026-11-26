#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Version,
    [Parameter(Mandatory)][ValidateScript({ Test-Path -LiteralPath $_ -PathType Leaf })][string]$UpdateSigningKeyPath,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [Alias('Endpoint')][string]$SigningEndpoint = 'https://eus.codesigning.azure.net/',
    [Alias('AccountName')][string]$SigningAccountName = 'galenwinmonsigning',
    [string]$CertificateProfileName = 'WinMonPublic',
    [string]$ExpectedPublisher = 'Galen Blakeman'
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = Split-Path $PSScriptRoot -Parent
[xml]$metadata = Get-Content -LiteralPath (Join-Path $root 'Directory.Build.props') -Raw
if ($Version -cne [string]$metadata.Project.PropertyGroup.Version) { throw 'Sensor service bundle version must match Directory.Build.props.' }
$output = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $output) { throw "Sensor service bundle output already exists: $output" }

$trustKey = Join-Path $root 'src/WinMon/Updates/update-public-key.pem'
if (!(Test-Path -LiteralPath $trustKey -PathType Leaf)) { throw "Missing embedded update trust key: $trustKey" }
$privateKey = [Security.Cryptography.RSA]::Create()
$publicKey = [Security.Cryptography.RSA]::Create()
try {
    $privateKey.ImportFromPem([IO.File]::ReadAllText((Resolve-Path -LiteralPath $UpdateSigningKeyPath)))
    $publicKey.ImportFromPem([IO.File]::ReadAllText((Resolve-Path -LiteralPath $trustKey)))
    if ($privateKey.KeySize -lt 2048 -or $publicKey.KeySize -lt 2048 -or ![Security.Cryptography.CryptographicOperations]::FixedTimeEquals($privateKey.ExportSubjectPublicKeyInfo(), $publicKey.ExportSubjectPublicKeyInfo())) { throw 'Sensor service manifest key must be the existing release RSA key (2048 bits or stronger).' }
}
finally { $publicKey.Dispose() }

$work = Join-Path $root "artifacts/packaging/sensor-service-$([guid]::NewGuid().ToString('N'))"
$servicePublish = Join-Path $work 'service'; $helperPublish = Join-Path $work 'helper'
$archive = Join-Path $output 'sensor-service.zip'; $helper = Join-Path $output 'WinMon.SensorSetup.exe'
$signing = @{ Endpoint = $SigningEndpoint; AccountName = $SigningAccountName; CertificateProfileName = $CertificateProfileName; ExpectedPublisher = $ExpectedPublisher }
try {
    New-Item -ItemType Directory -Path $output, $work | Out-Null
    Push-Location $root
    try {
        dotnet publish src/WinMon.SensorService/WinMon.SensorService.csproj -c Release -r win-x64 --self-contained true -m:1 -p:UseSharedCompilation=false -o $servicePublish
        if ($LASTEXITCODE -ne 0) { throw 'Sensor service publish failed.' }
        dotnet publish src/WinMon.SensorSetup/WinMon.SensorSetup.csproj -c Release -r win-x64 --self-contained true -m:1 -p:UseSharedCompilation=false -p:PublishAot=true -o $helperPublish
        if ($LASTEXITCODE -ne 0) { throw 'NativeAOT sensor setup helper publish failed. Install the Visual Studio Desktop C++ workload and Windows SDK.' }
    }
    finally { Pop-Location }
    $service = Join-Path $servicePublish 'WinMon.SensorService.exe'; $publishedHelper = Join-Path $helperPublish 'WinMon.SensorSetup.exe'
    if (!(Test-Path -LiteralPath $service -PathType Leaf) -or !(Test-Path -LiteralPath $publishedHelper -PathType Leaf)) { throw 'Published sensor-service payload is incomplete.' }
    & (Join-Path $PSScriptRoot 'Sign-ReleaseFile.ps1') -Path $service @signing | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $work 'sensor-service-signature.json') -Encoding utf8NoBOM
    & (Join-Path $PSScriptRoot 'Sign-ReleaseFile.ps1') -Path $publishedHelper @signing | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $work 'sensor-setup-signature.json') -Encoding utf8NoBOM
    Copy-Item -LiteralPath $publishedHelper -Destination $helper
    if (Test-Path -LiteralPath (Join-Path $servicePublish 'allowed-users.json')) { throw 'The service authorization policy must not be shipped in the signed archive.' }
    Compress-Archive -Path (Join-Path $servicePublish '*') -DestinationPath $archive
    $manifest = [ordered]@{ SchemaVersion = 1; Version = $Version; Runtime = 'win-x64'; Protocol = 1; Helper = [ordered]@{ Name = 'WinMon.SensorSetup.exe'; Sha256 = (Get-FileHash -LiteralPath $helper -Algorithm SHA256).Hash.ToLowerInvariant(); Size = (Get-Item -LiteralPath $helper).Length }; ServiceArchive = [ordered]@{ Name = 'sensor-service.zip'; Sha256 = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant(); Size = (Get-Item -LiteralPath $archive).Length } }
    $manifestBytes = [Text.Encoding]::UTF8.GetBytes(($manifest | ConvertTo-Json -Compress))
    [IO.File]::WriteAllBytes((Join-Path $output 'sensor-service-manifest.json'), $manifestBytes)
    [IO.File]::WriteAllText((Join-Path $output 'sensor-service-manifest.sig'), [Convert]::ToBase64String($privateKey.SignData($manifestBytes, [Security.Cryptography.HashAlgorithmName]::SHA256, [Security.Cryptography.RSASignaturePadding]::Pkcs1)), [Text.UTF8Encoding]::new($false))
    Copy-Item -LiteralPath (Join-Path $work 'sensor-service-signature.json'), (Join-Path $work 'sensor-setup-signature.json') -Destination $output
    Write-Host "Sensor service bundle: $output"
}
finally { $privateKey.Dispose() }
