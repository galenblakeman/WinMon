#requires -Version 7.4
<#
.SYNOPSIS
Run verification under dotnet-coverage and require 80% line coverage in every production assembly.
.EXAMPLE
./tools/Measure-Coverage.ps1 -Smoke
.EXAMPLE
./tools/Measure-Coverage.ps1 -Report artifacts/coverage/run/coverage.cobertura.xml
#>
[CmdletBinding()]
param(
    [switch]$Store,
    [switch]$Smoke,
    [string]$Report
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$verificationExit = 0
Push-Location $root
try {
    if (!$Report) {
        $null = Get-Command dotnet-coverage -ErrorAction Stop
        $variant = if ($Store) { 'store' } else { 'desktop' }
        $run = Join-Path $root "artifacts/coverage/$(Get-Date -Format 'yyyyMMdd-HHmmss-fff')-$variant"
        New-Item $run -ItemType Directory -Force | Out-Null
        $Report = Join-Path $run 'coverage.cobertura.xml'
        $options = @('-NoProfile', '-File', (Join-Path $PSScriptRoot 'Verify.ps1'))
        if ($Store) { $options += '-Store' }
        if ($Smoke) { $options += @('-Smoke', 'all') }
        dotnet-coverage collect -s (Join-Path $PSScriptRoot 'Coverage.settings.xml') -f cobertura -o $Report pwsh @options
        $verificationExit = $LASTEXITCODE
    }

    [xml]$coverage = Get-Content -LiteralPath $Report -Raw
    $projects = @(Get-ChildItem (Join-Path $root 'src') -Recurse -Filter *.csproj -File |
        Select-Object -ExpandProperty BaseName) + 'WinMon.NetworkProbe'
    $failed = $verificationExit -ne 0
    foreach ($project in $projects | Sort-Object) {
        $modules = @($coverage.coverage.packages.package | Where-Object name -ceq $project)
        $rate = 0.0
        if ($modules.Count -ne 1 -or
            ![double]::TryParse($modules[0].GetAttribute('line-rate'), [Globalization.NumberStyles]::Float,
                [Globalization.CultureInfo]::InvariantCulture, [ref]$rate) -or
            ![double]::IsFinite($rate) -or $rate -lt 0 -or $rate -gt 1) {
            Write-Host "FAIL $project`: missing, duplicate or invalid coverage"
            $failed = $true
            continue
        }

        $passed = $rate -ge 0.8
        Write-Host ('{0} {1}: {2:P2} line coverage (required: 80%)' -f
            ($passed ? 'PASS' : 'FAIL'), $project, $rate)
        if (!$passed) { $failed = $true }
    }
    Write-Host "Coverage report: $Report"
    exit ([int]$failed)
}
finally {
    Pop-Location
}
