# WinMon screenshot examples

Most gallery images were refreshed October 25, 2026, using WinMon 3.1.0. The two Settings images were updated for WinMon 3.2.1's combined Appearance preview. The main gallery uses the ordinary installer/portable configuration; the separate Store Settings capture uses `StoreBuild=true`. These examples use illustrative process identities and measurements so the screenshots are consistent and do not expose a user's running processes. They are UI examples, not performance measurements or proof of installed MSIX behavior.

| File | View |
| --- | --- |
| [example_individual.png](example_individual.png) | Dark live dashboard with Processes / Services / Usage tabs, individual process rankings and a selected-process action bar. |
| [example_grouped.png](example_grouped.png) | The same recording grouped by executable, with App / Background labels and private-memory totals. |
| [example_services.png](example_services.png) | Services view with a Top N toolbar, clickable metric headers, an expanded worker tree, five-second CPU/I/O averages and a selected-service action bar with Properties, Copy details and service controls. |
| [example_usage.png](example_usage.png) | Usage view with session totals, app rankings, CPU share, the Not attributed row and a selected-app action bar. |
| [example_snapshot.png](example_snapshot.png) | An earlier spike selected in Capture mode, with the bracket-and-dot glyph, Copy capture / Return to live buttons and pinned process rows, while the top readouts remain live. |
| [example_apps_only.png](example_apps_only.png) | Chrome search with App only enabled in Grouped mode. |
| [example_pinned.png](example_pinned.png) | Visual Studio Code and Google Chrome pinned in insertion order above the CPU rankings. |
| [example_pinned_only.png](example_pinned_only.png) | Pinned only enabled, hiding the ranked section. |
| [example_compact.png](example_compact.png) | Dark Compact view with live resource gauges and two-minute peak marks. |
| [example_compact_light.png](example_compact_light.png) | The same gauge cluster in Light appearance. |
| [example_light.png](example_light.png) | Light appearance with the Classic palette. |
| [example_neon.png](example_neon.png) | Neon palette. |
| [example_arcade.png](example_arcade.png) | Arcade palette. |
| [example_tropical.png](example_tropical.png) | Tropical palette. |
| [example_settings.png](example_settings.png) | Installer/portable Appearance section with Neon swatches, a combined theme/chart/gauge preview and an Updates section in the sidebar. |
| [example_settings_store.png](example_settings_store.png) | Store Appearance section with the same combined preview, without an Updates section. |

The process dashboard and compact views use the same illustrative recording. Services and Usage use separate illustrative recordings. Settings uses the real window and controls; its disk choices reflect the capture machine. Screenshots capture the complete Windows-composed window, including native caption buttons, and use the current responsive layout. Generic process icons are used for the illustrative executable paths.

The Services example uses a separate illustrative service recording. Regenerate it with `--services-smoke-output <output-directory>` using the same build. That diagnostic first checks live service discovery without controlling any services, then captures illustrative dark, light, narrow and historical service views. Copy `services-dark.png` to `example_services.png` after inspection. The live diagnostics are machine-specific; only the illustrative capture belongs in this gallery.

The Usage example uses a separate illustrative session ledger. Regenerate it with `--usage-smoke-output <output-directory>` using the ordinary build. Copy `usage-dark.png` to `example_usage.png` after inspection; the diagnostic's other outputs are verification evidence.

If Windows denies foreground activation, Services diagnostics can save separately named `*-content.png` files and `content-capture-notes.txt` for control-layout QA. Those captures omit native caption proof and must not replace the native-composed gallery images.

## Current UI highlights

- The title bar shows version 3.1.0, the Compact view / Full view toggle, and Always on top with a stacked-window glyph and highlighted enabled state.
- Processes, Services and Usage share a divider and a tab strip with heading-sized labels and an accent underline.
- Process rows include thread and handle counts, App / Background labels, grouping, search and pins. Pin accents follow the theme and chart palette.
- Capture mode keeps the live readouts visible while showing an earlier interval and its process rankings; Copy capture exports the selected interval.
- The process action bar offers Properties, Copy details, More and End process/group. More contains file location and eligible memory-dump creation; pinning remains in the row menu.
- The service action bar offers Properties, Copy details, Start, Stop and Restart; the row menu offers View process, Copy details and service controls. Services and Processes retain the action-bar target when it falls out of rank or is filtered out.
- The Usage action bar offers Properties, Copy details and Open file location for the selected app.
- Action buttons share icon styling, height, padding and spacing, including Copy capture and Return to live.
- Installer/portable Settings has an Updates section with GitHub controls; Store Settings omits that section.

### Settings by distribution channel

| Installer / portable | Microsoft Store |
| --- | --- |
| ![Installer and portable Settings with an Updates section in the sidebar](example_settings.png) | ![Store Settings without an Updates section in the sidebar](example_settings_store.png) |

## Regenerate

From the repository root with the SDK pinned in `global.json`:

```powershell
dotnet build src/WinMon/WinMon.csproj -c Release -m:1 -p:UseSharedCompilation=false --artifacts-path artifacts/examples-build
& ./artifacts/examples-build/bin/WinMon/release_win-x64/WinMon.exe --examples-output "$PWD/artifacts/readme-examples"
```

Run in an interactive Windows desktop with enough screen space for the 1000-by-940 dashboard window. The capture windows appear briefly; keep them unobstructed and do not switch applications during capture. Capture fails if its window is not foreground or does not fit on screen. Wait for the app to exit successfully, inspect the `example_*.png` files, then copy them into this directory. A failed capture writes `failure.txt` in the output directory; use a fresh output directory for each run. The diagnostic mode renders in English and does not load or save your normal preferences or copy to the clipboard.

To regenerate the separate Store Settings example, use isolated Store output:

```powershell
dotnet build src/WinMon/WinMon.csproj -c Release -m:1 -p:UseSharedCompilation=false -p:StoreBuild=true --artifacts-path artifacts/examples-store-build
& ./artifacts/examples-store-build/bin/WinMon/release_win-x64/WinMon.exe --examples-output "$PWD/artifacts/readme-examples-store"
```

After checking that capture completed successfully, copy only `artifacts/readme-examples-store/example_settings.png` to `docs/example/example_settings_store.png`. Keep the other gallery files from the ordinary build so its update controls remain documented accurately.
