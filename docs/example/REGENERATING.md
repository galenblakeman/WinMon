# Regenerate the screenshot gallery

[Back to the gallery](README.md). These commands build and open real diagnostic windows; they do not change normal preferences. Run one capture at a time.

From the repository root with the SDK pinned in `global.json`:

```powershell
dotnet build src/WinMon/WinMon.csproj -c Release -m:1 -p:UseSharedCompilation=false --artifacts-path artifacts/examples-build
$capture = Start-Process ./artifacts/examples-build/bin/WinMon/release_win-x64/WinMon.exe -ArgumentList '--examples-output', "`"$PWD/artifacts/readme-examples`"" -PassThru
$null = $capture.Handle
$capture.WaitForExit()
if ($capture.ExitCode -ne 0) { throw "Example capture failed; inspect the output directory" }
```

Run in an interactive Windows desktop with enough screen space for the 1000-by-940 dashboard window. The capture windows appear briefly; keep them unobstructed and do not switch applications during capture. Capture fails if its window is not foreground or does not fit on screen. Wait for the app to exit successfully, inspect the `example_*.png` files, then copy them into this directory. A failed capture writes `failure.txt` in the output directory; use a fresh output directory for each run. The diagnostic mode renders in English and does not load or save your normal preferences or copy to the clipboard.

To regenerate the separate Store Settings example, use isolated Store output:

```powershell
dotnet build src/WinMon/WinMon.csproj -c Release -m:1 -p:UseSharedCompilation=false -p:StoreBuild=true --artifacts-path artifacts/examples-store-build
$capture = Start-Process ./artifacts/examples-store-build/bin/WinMon/release_win-x64/WinMon.exe -ArgumentList '--examples-output', "`"$PWD/artifacts/readme-examples-store`"" -PassThru
$null = $capture.Handle
$capture.WaitForExit()
if ($capture.ExitCode -ne 0) { throw "Example capture failed; inspect the output directory" }
```

After checking that capture completed successfully, copy only `artifacts/readme-examples-store/example_settings.png` to `docs/example/example_settings_store.png`. Keep the other gallery files from the ordinary build so its update controls remain documented accurately.

## Services, Usage and Connections

Use the same ordinary build for these separate diagnostics. Run Services outside a restricted sandbox so Windows permits SCM enumeration. Services checks live discovery without controlling any services; Connections checks native TCP/UDP ownership without installing or enabling the optional service.

```powershell
$exe = Join-Path $PWD 'artifacts/examples-build/bin/WinMon/release_win-x64/WinMon.exe'
foreach ($mode in @('services', 'usage', 'connections')) {
    $output = Join-Path $PWD "artifacts/readme-examples-$mode"
    $capture = Start-Process $exe -ArgumentList "--$mode-smoke-output", "`"$output`"" -PassThru
    $null = $capture.Handle
    $capture.WaitForExit()
    if ($capture.ExitCode -ne 0) { throw "$mode capture failed; inspect $output" }
}
```

After inspecting the complete, native-composed windows, copy:

| Diagnostic output | Gallery file |
| --- | --- |
| `artifacts/readme-examples-services/services-dark.png` | `example_services.png` |
| `artifacts/readme-examples-usage/usage-dark.png` | `example_usage.png` |
| `artifacts/readme-examples-connections/connections-network-dark.png` | `example_connections.png` |

Keep live, unavailable, partial, translated and narrow diagnostic captures as verification evidence in artifacts. They are not the main feature examples. If Windows denies foreground activation, diagnostics may emit `*-content.png` and `content-capture-notes.txt`; those omit native caption proof and must not replace gallery files.

## Review before publishing

- All windows must be complete and unobstructed, with no failure file or content-only substitute.
- Inspect every image at full size. Check that version, labels, rates and totals agree with its caption.
- The main recording shows Chrome's CPU spike followed by recovery; the selected process CPU is a five-second average. Example diagnostics assert that the spike and recovery exist.
- Connections accumulates received/sent counters across its recording and asserts nonzero period totals. Unavailable network collection has a separate diagnostic image.
- The main examples use installed Chrome, Visual Studio Code and Task Manager icons when available, without changing illustrative identities or paths. Other rows retain generic icons. No application icons are downloaded or bundled.
- Update the gallery's capture version, date and environment after a successful refresh. A screenshot version is provenance, not a claim that the image always matches the latest release.
