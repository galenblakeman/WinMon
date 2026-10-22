# WinMon screenshot examples

Captured October 22, 2026, using the current WinMon WPF source (version 1.0.3). These examples use illustrative process identities and measurements so the screenshots are consistent and do not expose a user's running processes. They are UI examples, not performance measurements or a claim about an already published release.

| File | View |
| --- | --- |
| [example_individual.png](example_individual.png) | Dark live dashboard with individual processes, Threads / Handles columns, dual disks and centered Live / Snapshot switch. |
| [example_grouped.png](example_grouped.png) | The same recording grouped by executable, with App / Background labels and private-memory totals. |
| [example_snapshot.png](example_snapshot.png) | An earlier spike selected in Snapshot mode, Copy snapshot and pinned process rows, while the top readouts remain live. |
| [example_apps_only.png](example_apps_only.png) | Chrome search with App Only enabled in Grouped mode. |
| [example_pinned.png](example_pinned.png) | Visual Studio Code and Google Chrome pinned in insertion order above the CPU rankings. |
| [example_pinned_only.png](example_pinned_only.png) | Pinned only enabled, hiding the ranked section. |
| [example_compact.png](example_compact.png) | Dark Compact view with live readings and filled two-minute charts in each resource tile. |
| [example_compact_light.png](example_compact_light.png) | The same compact strip in Light appearance. |
| [example_light.png](example_light.png) | Light appearance with the Classic palette. |
| [example_neon.png](example_neon.png) | Neon palette. |
| [example_arcade.png](example_arcade.png) | Arcade palette. |
| [example_tropical.png](example_tropical.png) | Tropical palette. |
| [example_settings.png](example_settings.png) | Settings with language selection, Neon swatches, keyboard shortcut, dual disks and automatic update controls. |

The dashboard and compact views use the same illustrative recording. Settings uses the real window and controls; its disk choices reflect the capture machine. Screenshots capture the complete Windows-composed window, including native caption buttons, and use the current responsive layout. Generic process icons are used for the illustrative executable paths.

## Regenerate

From the repository root with the SDK pinned in `global.json`:

```powershell
dotnet build src/WinMon/WinMon.csproj -c Release -m:1 -p:UseSharedCompilation=false --artifacts-path artifacts/examples-build
& ./artifacts/examples-build/bin/WinMon/release_win-x64/WinMon.exe --examples-output "$PWD/artifacts/readme-examples"
```

Run in an interactive Windows desktop with enough screen space for the 1000-by-940 dashboard window. The capture windows appear briefly; keep them unobstructed and do not switch applications during capture. Capture fails if its window is not foreground or does not fit on screen. Wait for the app to exit successfully, inspect the `example_*.png` files, then copy them into this directory. A failed capture writes `failure.txt` in the output directory; use a fresh output directory for each run. The diagnostic mode renders in English and does not load or save your normal preferences or copy to the clipboard.
