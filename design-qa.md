# Resource overlay visual QA

## Blue title bar and section headers - 2026-10-19

final result: passed

Scope: apply the second displayed contrast concept's blue title bar and blue section headers to the existing WPF app. Selected reference: `design/contrast-option-2.png`. The existing footer, typography, resource colors, controls and monitoring behavior are retained under this scoped request.

Opened and inspected `artifacts/blue-sections-verified/comparison.png`: reference at left, native composed compact window at right, both normalized to 600 x 840. Both show the compact dark live state; reference numbers are illustrative and implementation numbers are real samples, so data and graph shapes differ intentionally. Title chrome uses #24415B; the system-resources and process-heading bands use #20303D. Native caption buttons have the matching background. The scoped changes have no actionable P0/P1/P2 visual differences.

Fidelity surfaces: existing Segoe UI typography and readable hierarchy retained; full-width header bands align with the window and preserve content insets; blue colors follow the selected concept while resource colors retain their metric meanings; existing application/process icons remain native assets; app labels and live data remain functional text. The native 600 x 840 comparison is readable enough to inspect the title and both section headers directly. Light-theme capture `artifacts/blue-sections-verified/light.png` was also opened: pale blue title/section surfaces use the existing dark text. High-contrast resource mappings are implemented but Windows high-contrast mode was not exercised.

Validation: Release build and publish succeeded with zero warnings/errors; 55 NUnit tests passed. The sandboxed test attempt did not complete and was canceled; the identical command passed outside the sandbox. Published executable: `artifacts/blue-sections-app/WinMon.exe`. Native isolated smoke run outside the sandbox exited 0 and produced `artifacts/blue-sections-verified/smoke.json`, including title drag/client/resize/native-caption hit-region checks, recording, historical selection, expiry, pause/resume and return to live. The smoke run also captured expanded renders at 100/150/200%; these are bitmap render evidence, not actual desktop DPI transition checks. Interactive dragging/maximize and Windows high contrast remain manual checks.

Implementation checklist: shared dynamic theme brushes, matching main-window DWM caption color, blue section bands, light/high-contrast mappings, build/tests, native smoke and compact/light visual comparison completed.

Date: 2026-10-19

final result: passed

## Tooltip contrast fix

The application's light TextBlock foreground was combined with the platform's default white tooltip surface. An application-level ToolTip style now sets background, foreground, border and typography from the existing dynamic theme resources. This covers chart hover and the other tooltips consistently.

Published build: `artifacts/tooltip-app/WinMon.exe`. Native diagnostic captures `artifacts/tooltip-verified/tooltip-dark.png` and `tooltip-light.png` were opened and visually inspected: dark theme has readable light text on a dark surface; light theme has readable dark text on a light surface. Diagnostics open a real WPF ToolTip with the chart's historical content and assert its resolved theme brushes. Release publish and 46 existing NUnit tests passed. The diagnostic popup exercise does not automate physical mouse hover.

## Title-bar integration update

The heading, application icon, Pin and Expand now occupy a single 48-DIP caption row using WPF WindowChrome. The former duplicate content header is removed. The DWM-owned button region stays unpainted, preserving native Minimize/Maximize/Close visuals and input. Caption settings follow [.NET 10 WindowChrome documentation](https://learn.microsoft.com/dotnet/api/system.windows.shell.windowchrome?view=windowsdesktop-10.0).

Published build: `artifacts/titlebar-app/WinMon.exe`. Final native capture, opened and visually inspected: `artifacts/titlebar-verified/titlebar-window.png`. It shows the integrated heading/actions and native caption buttons without overlap. Native diagnostics verify caption dragging hit region, client hit regions for Pin/Expand, the resize border and all three native caption-button hit regions. The published build's existing recording/history smoke checks passed, exit 0. Release build succeeded with zero warnings/errors; 46 existing tests passed. Actual interactive dragging, maximize/restore, system-menu and mixed-DPI checks remain manual; hit-region assertions are not end-to-end mouse automation.

## Target and evidence

- Target: first displayed resource-overlay mockup, saved as `design/resource-overlay-selected.png`.
- Combined comparison: `artifacts/resources-final/design-comparison.png`, opened and visually inspected. Reference normalized to 600 × 820 and WPF root rendered at 600 × 820, both compact/dark with four traces and five process rows. Native window chrome is outside the captured WPF root.
- `design-compact.png`, `design-compact-150.png`, `design-compact-200.png`: explicitly labeled illustrative data for full-history layout comparison; not native measurement evidence.
- `compact.png`, `investigate.png`, `investigate-150.png`, `investigate-200.png`, `light.png`: native live/selected/light/scaled evidence in the same folder. `smoke.json`: native diagnostic exit 0.

## Review

Final comparison: no P0/P1/P2 findings. Header, four color-keyed readouts with hide/show checks and separators, shared percentage graph, CPU-ranked table, dark palette, spacing and footer follow the selected layout. Four traces remain distinguishable without opaque fills. Labels and controls fit. Native captures show real resource measurements and a readable light palette. Historical state has a timestamp and selected resource summary while top readouts remain live.

Earlier render review led to shorter process rows to preserve chart/table proportions, colored toggles and readout separators. The final combined comparison was captured after these refinements.

## Adaptations and P3 polish

- Illustrative trace shapes differ from the mockup; actual native captures have a short recording history.
- GiB/MiB identify binary memory units. Network shows Rx/Tx and link normalization. Process I/O is labeled accurately rather than implying disk-only attribution.
- Process names retain a text presentation without executable logos; native ProgressBar styling remains. Optional visual polish, not a monitoring blocker.

## Limits

Scaled bitmaps do not verify actual desktop DPI changes or mixed-DPI monitors. Keyboard/mouse automation, actual OS high-contrast interaction, tray/startup and sustained overhead remain manual/environment checks. CPU retains the existing single-active-processor-group limitation; other resources can continue if CPU is unavailable. All 46 NUnit tests passed outside the sandbox; initial sandbox host-connection failures are inconclusive.

## Inline process count — 2026-10-19

final result: passed

Target: `design/process-count-selected.png`, the first displayed mockup. Implementation evidence: `artifacts/process-count-final/`. The opened `design-comparison.png` combines the reference normalized to 600 × 840 with the 600 × 820 WPF fixture and its separately captured native dropdown composited beside the selector. This is a comparison montage, not a desktop screenshot of a single interaction. The source has a short live history; the fixture uses illustrative traces and process names. These sample-data differences are intentional.

Typography, copy, spacing, colors, and asset quality were reviewed: the inline `Top [5] processes by CPU` heading and four choices match the selected interaction; existing Segoe UI, blue headers, resource colors, graph, process columns and icons are retained. The existing application dropdown uses a green outline instead of the mockup's blue selection fill/checkmark. Reusing the application's existing focus and selection style is an accepted adaptation. Controls and text are sharp at the rendered scale. No new raster assets are required for the functional control.

Earlier checks caught footer overflow at minimum height, including the extra historical-detail line. The graph minimum and scrollable process viewport were adjusted. Final `process-count-20.png` and `process-count-history.png` were opened and reviewed: the list scrolls, headers align, and complete footer buttons remain visible. Native `light.png` was opened and reviewed for theme compatibility. There are no remaining P0/P1/P2 findings for this change.

Validation: final Release build succeeded with zero warnings/errors; 66 NUnit cases passed outside the sandbox. The sandboxed test run had no final result and was stopped as inconclusive. Final native smoke exited 0, with all count choices, historical sample preservation, menu rendering, minimum-size live/historical footer visibility and scrolling assertions passing. `smoke.json` records successful completion only after the fixture assertions. Earlier failed runs remain in their separate artifact folders.

Limits: preference serialization and fallback were tested; physical keyboard/mouse operation, actual OS DPI transitions and restart persistence through a real user session were not automated. The count is saved through the existing normal-exit preference lifecycle.

## Bold chart colors — option 2 — 2026-10-19

final result: passed

Scope: apply the second displayed mockup's saturated dark resource palette, heavier chart traces, restrained halo and transparent area washes to the existing WPF monitor. Selected reference: `design/bold-chart-option-2.png`.

Evidence: opened `artifacts/bold-chart-verified/comparison.png`, with reference at left and implementation at right, normalized to 600 x 820 each. Both show illustrative two-minute resource histories and five process rows. The application retains its current compact caption, range controls, sort indicators, table geometry and flat graphite surfaces; these existing product details are intentional adaptations outside the requested chart-color change. No actionable P0/P1/P2 differences in the scoped chart treatment.

Required fidelity surfaces: existing Segoe UI hierarchy and small text remain readable; existing spacing, responsive layout and controls are retained; CPU #73FF35, memory #4285FF, disk #FFB000, network #F657E5 and GPU #00F0C8 are shared by dark readouts/toggles/traces. Main traces are 2.5 DIP with narrow translucent halo strokes and 8% washes painted before all traces. Existing icons remain sharp native assets; no new bitmap UI assets are required. Resource names, units, separate network scaling, illustrative-data labeling and process content retain their existing meanings. The full comparison is readable at the compact viewport and directly exposes the chart colors and crossings, so no additional focused crop is necessary.

Opened `artifacts/bold-chart-verified/light.png`: existing darker light-theme colors remain readable. High contrast skips all main-chart washes and halos and retains differentiated dash styles; actual Windows high-contrast mode was not exercised.

Verification: isolated Release build completed with zero warnings/errors. Native smoke exited 0; `artifacts/bold-chart-verified/smoke.json` records series toggles, chart range, wide layout, historical inspection/expiry, pause/resume gaps, return to live, process sorting/count and tooltip checks. Full history fixture and 150%/200% bitmap renders were captured. Native disk/GPU readings were unavailable in this run; full five-series visual review uses the labeled fixture. Actual desktop DPI transitions and sustained rendering cost were not measured.

Tests: 110 passed, zero failed/skipped, TRX at `artifacts/bold-chart-tests/bold-chart.trx`. The sandboxed runner stalled and was canceled, so that attempt is inconclusive. The identical outside-sandbox retry failed on missing restore target; rerunning with restore enabled passed. The default build output was locked by a running app; isolated output `artifacts/bold-chart-build/` built successfully.

Checklist: selected reference saved; matching resource colors; fills under traces; subtle halo; high-contrast safeguards; Release build; 110 tests; native smoke; dark/light visual review completed.
