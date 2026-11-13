#requires -Version 7.4
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$path = Join-Path ([IO.Path]::GetTempPath()) ("WinMon-coverage-" + [guid]::NewGuid().ToString('N') + '.xml')
$projects = @(Get-ChildItem (Join-Path $root 'src') -Recurse -Filter *.csproj -File |
    Select-Object -ExpandProperty BaseName) + 'WinMon.NetworkProbe'
try {
    foreach ($case in @('passing', 'below', 'missing', 'duplicate', 'invalid')) {
        $packages = foreach ($project in $projects) {
            if ($case -eq 'missing' -and $project -eq 'WinMon') { continue }
            $rate = if ($project -ne 'WinMon') { '0.8' }
                elseif ($case -eq 'below') { '0.79999' }
                elseif ($case -eq 'invalid') { 'NaN' }
                else { '0.8' }
            '<package name="{0}" line-rate="{1}" />' -f $project, $rate
        }
        if ($case -eq 'duplicate') { $packages += '<package name="WinMon" line-rate="1" />' }
        Set-Content -LiteralPath $path -Value "<coverage><packages>$($packages -join '')</packages></coverage>"
        $output = & pwsh -NoProfile -File (Join-Path $PSScriptRoot 'Measure-Coverage.ps1') -Report $path
        $expected = if ($case -eq 'passing') { 0 } else { 1 }
        if ($LASTEXITCODE -ne $expected) { throw "Coverage self-check failed for $case`: $output" }
    }
    Write-Host 'PASS coverage self-check'
}
finally {
    Remove-Item -LiteralPath $path -ErrorAction SilentlyContinue
}
