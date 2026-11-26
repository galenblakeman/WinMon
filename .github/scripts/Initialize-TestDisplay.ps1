$ErrorActionPreference = 'Stop'

# ServerCore's display cmdlet runs in Windows PowerShell.
powershell.exe -NoProfile -NonInteractive -Command 'Set-DisplayResolution -Width 1920 -Height 1080 -Force'
if ($LASTEXITCODE -ne 0) { throw 'Could not configure the runner display.' }

Add-Type -AssemblyName PresentationFramework
$workArea = [System.Windows.SystemParameters]::WorkArea
Write-Host "Runner work area: $($workArea.Width) x $($workArea.Height) DIPs"
if ($workArea.Width -lt 1440 -or $workArea.Height -lt 980) {
    throw 'The runner work area is too small for the native window diagnostics.'
}
