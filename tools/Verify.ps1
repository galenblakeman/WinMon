#requires -Version 7.4
<#
.SYNOPSIS
Build WinMon and the benchmark, run the tests and any smoke modes, and report one pass or fail line per step.

.DESCRIPTION
Builds into artifacts/verify/build (or build-store with -Store), so normal bin folders are left alone. Each smoke
mode writes to artifacts/verify/<timestamp>/<mode>. Exits 1 if any step failed.

.EXAMPLE
./tools/Verify.ps1 -Smoke feature, services
#>
[CmdletBinding()]
param(
    [ValidateSet('all', 'full', 'feature', 'snapshot', 'search', 'compact', 'services', 'usage', 'capture', 'chart')]
    [string[]]$Smoke = @(),
    [switch]$Store,
    [switch]$SkipTests
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$modes = [ordered]@{
    full = '--smoke-output'
    feature = '--feature-smoke-output'
    snapshot = '--snapshot-smoke-output'
    search = '--search-smoke-output'
    compact = '--compact-smoke-output'
    services = '--services-smoke-output'
    usage = '--usage-smoke-output'
    capture = '--capture-smoke-output'
    chart = '--chart-smoke-output'
}
$Smoke = @(if ($Smoke -contains 'all') { $modes.Keys } else { $Smoke | Select-Object -Unique })
$build = Join-Path $root "artifacts/verify/$(if ($Store) { 'build-store' } else { 'build' })"
$run = Join-Path $root "artifacts/verify/$(Get-Date -Format 'yyyyMMdd-HHmmss')"
$options = @('-c', 'Release', '-m:1', '-p:UseSharedCompilation=false', '--artifacts-path', $build)
if ($Store) { $options += '-p:StoreBuild=true' }
$results = [Collections.Generic.List[object]]::new()

function Add-Result([string]$Step, [bool]$Passed, [string]$Detail) {
    $results.Add([pscustomobject]@{ Step = $Step; Passed = $Passed; Detail = $Detail })
    Write-Host "$(if ($Passed) { 'PASS' } else { 'FAIL' }) $Step$(if ($Detail) { ": $Detail" })" -ForegroundColor ($Passed ? 'Green' : 'Red')
}

Push-Location $root
try {
    dotnet build WinMon.sln @options -v quiet
    Add-Result 'build' ($LASTEXITCODE -eq 0) $(if ($Store) { 'Store variant' })
    # The benchmark is outside the solution; building it catches renamed internal members it uses.
    dotnet build tools/WinMon.Performance/WinMon.Performance.csproj @options -v quiet
    Add-Result 'build benchmark' ($LASTEXITCODE -eq 0) ''
    if ($results.Where({ !$_.Passed }).Count -eq 0) {
        if (!$SkipTests) {
            dotnet test tests/WinMon.Tests/WinMon.Tests.csproj @options --no-build
            Add-Result 'tests' ($LASTEXITCODE -eq 0) ''
        }

        $exe = Join-Path $build 'bin/WinMon/release_win-x64/WinMon.exe'
        foreach ($name in $Smoke) {
            $output = Join-Path $run $name
            Write-Host "Running $name smoke..."
            $process = Start-Process -FilePath $exe -ArgumentList $modes[$name], "`"$output`"" -PassThru
            # Reading Handle keeps it open, so ExitCode is still available after the process exits.
            $null = $process.Handle
            # Each mode stops itself after 60 seconds; this only catches a hang.
            if (!$process.WaitForExit(120000)) {
                $process.Kill()
                Add-Result "$name smoke" $false "did not exit within 120 s; output in $output"
                continue
            }

            $failure = Join-Path $output 'failure.txt'
            if ($process.ExitCode -eq 0) {
                $renders = @(Get-ChildItem -LiteralPath $output -Filter *.png -ErrorAction SilentlyContinue).Count
                Add-Result "$name smoke" $true "$renders renders in $output"
            }
            elseif (Test-Path -LiteralPath $failure) {
                Add-Result "$name smoke" $false "exit $($process.ExitCode); $failure"
                Get-Content -LiteralPath $failure -TotalCount 5 | ForEach-Object { Write-Host "    $_" }
            }
            else {
                Add-Result "$name smoke" $false "exit $($process.ExitCode) without failure.txt; output in $output"
            }
        }
    }
}
finally {
    Pop-Location
}

$failed = $results.Where({ !$_.Passed }).Count
Write-Host ''
Write-Host "$($results.Count - $failed) passed, $failed failed" -ForegroundColor ($failed ? 'Red' : 'Green')
exit ($failed ? 1 : 0)
