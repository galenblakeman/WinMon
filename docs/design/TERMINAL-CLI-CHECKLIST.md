# Full interactive CLI requirements

Scope: read-only Processes, Services, Usage and Connections, desktop measurement/query semantics, selection/details, session pins and available traffic from an already-installed authenticated network service. No process/service mutation, service installer, release publication or WPF rendering dependencies.

November 10 scope extension: traffic remains in Connections with desktop executable grouping. Explicit `network start` and signed `network install` commands manage only the WinMon traffic service; ordinary monitoring remains read-only. The CLI links the existing desktop setup broker instead of adding a second privileged setup implementation. Store builds reject management commands, and builds without signed assets cannot install.

Baseline: repository verification passed before this goal, including 34 CLI tests and 1,389 tests overall. Existing changes from this chat remain uncommitted and are preserved.

| Requirement | Status | Evidence |
| --- | --- | --- |
| Processes five-second averaging and frozen window | Verified | Prior full verification and native PTY smoke |
| Processes grouping, App only, Top/All, complete metrics | Verified | FullInteractiveTests; native process table smoke |
| Tab navigation, per-tab filters/sorting, selection and details | Verified | FullInteractiveTests; native F1/F2/F3/F4 and Enter/Esc smoke |
| Session pins and Pinned only with stable identity | Verified | PID-reuse/selection tests; native P and details smoke |
| Services query/display parity including Workers/status | Verified | ServicesTabTests; native Services smoke; worker detail test |
| Usage query/display parity and cumulative totals | Verified | UsageQueryTests, UsageTabTests, existing WPF UsageViewTests; native Usage smoke |
| Connections query/display parity and traffic periods | Verified | ConnectionsTabTests; native Chrome owner/endpoint smoke |
| Authenticated optional traffic polling/recovery and cancellation | Verified | Six NetworkTrafficClientTests; native 12-second authenticated service probe (external service reports warm/incomplete capacity) |
| Translation resources and user/developer documentation | Verified | Ten resource sets generated; CLI layout/parser tests and documentation refreshed |
| Deterministic query/state/lifecycle tests | Verified | Final Desktop run: 69 CLI tests, 76 Windows adapter tests; shared query and existing WPF tests passed |
| Full verification, relevant native terminal smoke and performance | Verified | Final Desktop: 1,432 passed, three explicit tests not executed; full Store suite passed earlier, final Store CLI rerun passed; native terminal flows and Usage smoke PNGs inspected; measured results in PERFORMANCE-HISTORY.md |

Implementation order: reusable table rows and tab modules, Processes controls, integrate navigation/details/pins, integrate service-backed traffic, then verify complete flows. Each module returns plain columns/rows/details; the single existing terminal owner renders and handles keys. MonCore remains independent of terminal and WPF libraries.

## Native traffic observation

The existing installed service was already running. A bounded read-only probe produced twelve advancing one-second snapshots on one session, each reporting `WarmingUp, Provisional, Incomplete, CapacityExceeded`. This is an external collector-state limitation, not a CLI transport failure. The CLI preserves missing rates and quality instead of fabricating zero. No service installation, repair, removal or permission change was performed. Probe evidence: `artifacts/cli-full-native/network-traffic-client-probe.txt`.

## Verification retry

Desktop gates intermittently hit the unchanged `RevocationTransaction_RollsBackAfterRestartFailureAndStopsCachedServiceFirst` rollback fixture with an atomic file replacement failure. The isolated test passed outside the sandbox, including against the exact verify-built DLL. No NetworkSetup code or tests changed for this goal. The final Desktop run at `artifacts/verify/20261109-235518-145-desktop/tests` passed all 1,432 executed tests, including that fixture; three explicit tests were not executed. The full Store suite passed at `artifacts/verify/20261109-233012-751-store/tests`; the final Store build and 69 CLI tests passed at `artifacts/verify/20261109-235727-023-store/tests`. Earlier failures remain recorded in their TRX files; their external file-lock cause was not established.

## Explicit network setup verification (November 10)

Full Desktop checks passed at `artifacts/verify/20261110-081837-833-desktop/tests` (1,438 executed tests); full Store checks passed at `artifacts/verify/20261110-081919-013-store/tests` (1,394 executed tests). Each left the same three explicit fixtures unexecuted. Desktop used an exact copy of Verify.ps1 with only repository-root resolution and build-output directory changed because the user's running CLI locked the default apphost. The final CLI-only suite additionally covers setup guidance at 80 columns (76 tests passed). A sandbox testhost connection failure was rerun successfully outside the sandbox.

Native `network start` reached and subscribed to the existing service; `network --help` listed both commands; development `network install` rejected the absent signed bundle before launching a helper. Native Connections smoke displayed grouped rows and incomplete traffic status, and Q restored the terminal with exit 0. No installation, repair, service stop/removal or UAC prompt was performed. Real signed-bundle/UAC acceptance remains unverified; existing broker and setup transaction tests cover their shared implementation. No sampling, ranking or collector hot path changed in this follow-up.

## Terminal color and shortcut verification (November 10)

Full Desktop verification passed at `artifacts/verify/20261110-082840-372-desktop/tests` using the same isolated-output script (80 CLI tests at that point). The final CLI suite additionally verifies multiline help and the existing 2=Memory/3=GPU key mapping (83 tests passed). Store solution/benchmark build and the CLI suite passed; the full Store suite had already passed during the preceding network-command change.

Native PTY checks covered bracketed shortcuts, numbered sorts, reverse-video selection, active tab, green enabled toggles, yellow pins/paused state, help line breaks and Q restoration. One run inherited NO_COLOR and confirmed monochrome text emphasis; a separate child terminal cleared NO_COLOR and used xterm-256color to confirm ANSI colors without changing user environment settings. Render performance is recorded in PERFORMANCE-HISTORY.md. Light-theme contrast, desktop terminal screenshots and screen-reader behavior remain unverified.

## GitHub Dark palette follow-up (November 10)

Replaced the cyan ANSI accents and reverse-video selection with the selected GitHub Dark RGB palette; normal text and terminal background remain untouched. Exact foreground/background values, identical monochrome text, disabled colors and 16/256-color fallbacks are covered by the CLI suite (85 tests). The Store CLI suite passed. Native true-color PTY smoke confirmed blue controls, dark-blue selection, light selection text, green live/enabled state and gold paused/pinned state; Q restored the terminal and exited 0. This change only replaces style values and adds fallback assertions; the previously measured render/query structure is unchanged.

The first full Desktop run at `artifacts/verify/20261110-095533-286-desktop/tests` again hit the existing intermittent installer rollback fixture; all other suites, including 83 CLI tests before the two fallback cases were added, passed. The isolated rollback fixture subsequently passed (28 ms). The final full retry at `artifacts/verify/20261110-095705-611-desktop/tests` passed all other suites, including the final 85 CLI tests, but reproduced the same unchanged rollback file-replacement failure. Full verification therefore remains failed on that unrelated fixture; no installer code or tests were changed for this palette. The running user's normal-bin CLI was left open; builds used the existing isolated verification output. Light-terminal contrast and screen readers remain unverified.

## Adaptive desktop display units (November 10)

Memory and rates now use linked desktop formatters, including an unchanged extracted UsageText.Bytes partial file. CLI fixed-unit headers/suffixes and duplicate byte-formatting loops are removed. Binary memory units, decimal table rates/totals, desktop network-summary byte-rate formatting, null readings, locale-sensitive numbers and unchanged raw snapshot values are tested. The previously stated bits/sec explanation for the desktop network display was incorrect: RateText accepts bits internally but displays decimal bytes/sec.

Full Desktop verification passed at `artifacts/verify/20261110-100857-494-desktop/tests`, including all 100 CLI tests and the previously intermittent installer fixture. Store solution/benchmark builds and the filtered CLI/desktop formatter/Usage suite passed at `artifacts/verify/20261110-101013-411-store/tests` (100 CLI and 21 WPF tests). User-running CLI apphosts were left untouched; verification uses `artifacts/cli-adaptive/build`. Native PTY smoke showed GiB/MiB process and summary memory, decimal KB/s/MB/s disk/network and process I/O, preserved GitHub Dark styling and Q restoration with exit 0. Rendering measurements are recorded in PERFORMANCE-HISTORY.md. No synchronized cross-process capture, new WPF screenshot pass, SSH or screen-reader check was performed; desktop formatting behavior was moved unchanged and its tests passed.

## Saved preferences and Titanium controls (November 10)

Saved controls use separate per-user CLI settings, with temporary argument overrides, explicit false booleans, --no-save, settings show/reset/set, 500 ms debounced writes and final quit/cancellation flushing. Filters, selection, scrolling, details and pause remain temporary. Concurrent sessions merge changed fields and pin additions/removals. JSON is bounded/validated and replaced atomically; malformed files are retained and unknown schema versions are not overwritten. Executable paths and service-group identities restore pins without transferring them on PID reuse or changed service membership. Unknown-path rows remain session-only pins. Key/control accents are Titanium #8795A5; status and selection colors remain unchanged.

Full Desktop gates passed at `artifacts/verify/20261110-102634-068-desktop/tests` and `artifacts/verify/20261110-103820-884-desktop/tests` (114 and 119 CLI tests respectively). Full Store verification passed at `artifacts/verify/20261110-102831-459-store/tests` with 114 CLI tests; the later Store CLI gate at `artifacts/verify/20261110-103833-796-store/tests` passed 119. The final service-host identity guard and all pin/override cases passed the final 120-test CLI suite in both Desktop and Store builds. Three explicit format/elevated fixtures remain unexecuted in full gates, as before.

Native PTY smoke used an artifact-only injected settings path, leaving actual user preferences untouched. First launch saved grouped/App only/Top 5/Memory. A second launch restored those controls while --grouped=false/--top 10 remained temporary; changing GPU sorting saved that control alone, preserving grouped/Top 5 defaults. A third launch with --no-save changed controls and pins without changing the settings file hash. All launches restored the terminal on Q and exited 0; live output showed exact Titanium RGB values. The local harness and profiles are under `artifacts/cli-settings/smoke` and `artifacts/cli-settings/native-settings.json`. Performance and its accepted pinned-query overhead are logged in PERFORMANCE-HISTORY.md. Settings commands and corruption/concurrency/snapshot-isolation cases are covered by CliSettingsTests. Light-theme contrast, screen-reader behavior and force-killed-process saves were not checked; abrupt termination can lose changes inside the debounce interval.

## Resource gauges and process sparklines (November 10)

| Requirement | Status | Evidence |
| --- | --- | --- |
| Current CPU/memory/GPU/disk gauges and fixed-scale resource history | Verified | 120×36 render captures, native wide PTY, chart tests |
| Process History follows all six sort metrics | Verified | CLI chart tests and six metric render captures |
| One shared scale across the complete filtered/Top table, stable across pages | Verified | Paging-scale regression test; desktop builder linked into CLI |
| Grouping, unavailable CPU, gaps and full lifetime identity | Verified | Shared builder tests, explicit compaction/availability/domain regressions |
| Pause freezes the complete displayed minute; resize/paging preserve controls | Verified | Pause/render equality and page-navigation tests; compact and wide PTY smoke |
| Compact/minimum layouts, no-color/ASCII, translated labels | Verified | 80×20, 80×24, 120×36 and 160×45 layout checks; color-text parity; German/Japanese/Chinese layout checks; resource tests |
| Desktop and Store verification | Verified | Desktop 1,505 tests; Store 1,461 tests; 140 CLI tests in each |
| Desktop process/chart smoke and performance comparison | Verified | Search 9 renders, chart 12 renders; PNG inspection; four alternating benchmark runs per variant/layout |

Final Desktop gate: `artifacts/verify/20261110-110111-426-desktop`; final Store gate: `artifacts/verify/20261110-110541-169-store`. Both built without warnings or errors. The usual format-writing fixture and two elevated fixtures remain explicit and unexecuted. The first Store run had an unrelated installer rollback test fail during temporary-file replacement; its immediate focused retry and the complete Store rerun passed. An initial CLI run also saw a transient settings-reset file failure; subsequent full Desktop/Store suites passed without changing that code. Sandbox NuGet/testhost failures were rerun successfully outside the sandbox.

Native PTY checks used real Windows collection at 80×24 and 120×36 with `--no-save`, verifying resource gauges, initial partial history, memory sorting, pause and Q restoration (exit 0). Synthetic Spectre text/PNG captures for all six metrics are under `artifacts/cli-charts/captures`; the saved baseline, benchmark and raw measurements are under `artifacts/cli-charts`. Render cost and the added wide-history allocation are recorded in PERFORMANCE-HISTORY.md. Screen-reader and light-terminal acceptance remain unverified; no settings, service installation or release publication was performed by this change.

## Interactive UTF-8 output (November 10)

Reproduced ASCII resource gauges and dotted process histories in a 120×36 PTY with Windows output code page 437. Program now selects BOM-free UTF-8 before command creation for non-redirected output and restores the inherited encoding in finally. The same legacy-code-page launch then emitted Unicode box borders, block gauges and process sparklines. Native assertions verified exit 0, restoration of the actual Windows console output code page through GetConsoleOutputCP, and valid schema-1 JSON with inherited encoding when stdout was redirected. The local assert-based smoke script is `artifacts/cli-unicode/check.ps1`.

`./tools/Verify.ps1` passed at `artifacts/verify/20261110-114421-510-desktop`: solution and benchmark builds had zero warnings/errors, and 1,505 tests passed including 140 CLI tests. The usual three explicit format/elevated fixtures remained unexecuted. This startup-only fix adds no sampling/render-loop work; no performance rerun was needed. Store-specific verification and additional font/terminal acceptance were not repeated. No terminal profile, Windows locale or user preference was modified.

November 10 gauge-spacing follow-up: added one blank interior row between each wide gauge's current bar and its history, and increased the reserved summary height so table paging/footer remain correct. The Spectre render capture and an assert-based spacer check passed; `artifacts/cli-charts/captures/wide-spaced.png` was inspected. Full verification passed at `artifacts/verify/20261110-114856-765-desktop` with 1,505 tests, including all 140 CLI tests, and zero build warnings/errors. Compact/minimum layouts are unchanged. A separate attempt to refresh the ordinary Release output was blocked by the user's running winmon-cli.exe; quit that session before rebuilding that output. The isolated verification build is current. No Store-specific or performance rerun was needed for this spacing-only edit.

## Summary resource colors (November 10)

Matched the supplied desktop reference: CPU #73FF35, memory #4487FF, disk #FF773D, network #B071D5 and GPU #00F0C8. Applied the colors to current values, bars and resource histories, including compact/numeric summaries and disk/network throughput details. Panel borders/captions and controls retain their prior styling. Five additional tests verify exact RGB-to-value mapping in all three layouts, no-color text equality and 16/256-color fallback. Native 120×36 PTY smoke confirmed all five RGB foreground sequences with live Windows data, Unicode charts, --no-save and Q exit 0. The actual Spectre synthetic render `artifacts/cli-colors/captures/summary-colors.png` was inspected.

Full verification passed at `artifacts/verify/20261110-115854-109-desktop`: 1,510 tests including 145 CLI tests, and solution/benchmark builds without warnings/errors. The first gate again encountered the existing installer rollback temporary-file replacement failure; the complete rerun passed without changing that code. The usual three explicit format/elevated fixtures remained unexecuted. Four alternating true-color render measurements per version/layout showed no material timing regression; results and additional allocations are recorded in PERFORMANCE-HISTORY.md. Store-specific and light-terminal acceptance were not repeated. Existing UTF-8 and gauge-spacing edits were preserved; no user preferences were changed.

## Network gauge and order (November 10)

Replaced the disk gauge/history with network link utilization and set resource order to CPU, Memory, Network, GPU in wide, compact and minimum numeric layouts. Disk utilization/read/write rates and network RX/TX details remain below. The network gauge reuses ResourceSample.NetworkPercent, stays unavailable without link speed, and excludes a different adapter from its history. Added order checks for all three layouts, an adapter-change/unknown-speed regression, and a purple network-percentage assertion. Full verification passed at `artifacts/verify/20261110-120830-814-desktop`: 1,514 tests including 149 CLI tests; builds had zero warnings/errors. The usual three explicit format/elevated fixtures remained unexecuted. The colored synthetic summary at `artifacts/cli-network-gauge/captures/summary.png` was inspected. Four alternating render measurements per version/layout showed no material regression; see PERFORMANCE-HISTORY.md. Store-specific and additional native acceptance were not repeated; sampling and retention are unchanged.

## Five wide resource panels (November 10)

At 120×32 and larger, resource panels now read CPU, Memory, Disk, Network, GPU. Five 23-cell panels plus four gaps occupy 119 cells; each bar/history has 19 cells. The summary keeps its previous height and spacer, process sparklines remain 24 cells, and compact/minimum layouts are unchanged. Memory shares the total's binary unit so both used/total values fit common capacities; tests cover same/mixed binary units, large RAM, missing readings and color mapping. Disk/network history now explicitly stays with the displayed volume/adapter. The synthetic colored capture `artifacts/cli-five-gauges/captures/summary.png` was inspected.

Full verification passed at `artifacts/verify/20261110-121715-420-desktop`: 1,521 tests including 156 CLI tests, with zero build warnings/errors. Existing CJK/German, ASCII/no-color, paging, pause and bounded-layout checks passed. The usual three explicit format/elevated fixtures remained unexecuted. Four alternating render comparisons per version/layout showed no material timing regression; the wide allocation increase is recorded in PERFORMANCE-HISTORY.md. Store-specific and additional native acceptance were not repeated. Existing pending UTF-8, spacing, color and network-gauge edits were preserved.

## Desktop-style gauge readouts and wide summary cleanup (November 10)

Colored each wide panel title to match its resource. CPU, memory, disk and GPU use percent primary values; Network uses download speed with the desktop 1-2-5 throughput scale over the displayed adapter's last 60 seconds. The previous spacer now contains CPU GHz, used/total GiB, disk MB/s, upload speed and GPU memory or 3D. Shared GaugeReadoutText keeps the CLI and desktop supporting text aligned; portable NetworkChartScale keeps throughput scaling aligned. Added the four shared resource keys to all ten cultures and regenerated the typed resources. Wide panels end after seven box rows: the separate disk/network text rows are removed, freeing two process rows. Compact/minimum layouts retain their detail rows.

| Requirement | Status | Evidence |
| --- | --- | --- |
| Colored titles and desktop primary/secondary units | Verified | Exact RGB/readout tests; inspected Spectre summary PNG |
| Download-rate scale, upload-aware maximum, missing/invalid data and adapter isolation | Verified | CLI and MonCore scale tests; no link speed required |
| Remove redundant wide text rows and preserve paging | Verified | Seven-row summary/control-boundary assertion; bounded-layout tests |
| Shared desktop readouts and translations | Verified | 989 WPF tests including formatter/resource tests; 208 MonCore tests |
| Current CLI sources | Verified | Isolated Release build and all 159 CLI tests passed in artifacts/cli-desktop-parity/final-check |
| Visible acceptance | Verified | Inspected summary PNG and 12 chart-smoke renders; native 120x36 live Windows check, --no-save --network=false, Q exit 0 |
| Full repository gate | Blocked | Known intermittent settings-reset and installer file-replacement failures; both pass isolated retries. Concurrent CLI control changes also invalidated an earlier compiled control-boundary assertion; current isolated CLI suite passes |
| Desktop gauge smoke | Blocked | CPU centering assertion also fails with the saved pre-change desktop executable at 1000 DIPs (83.3 column, 84.3 bowl/value) |

Final full-gate attempt: artifacts/verify/20261110-124434-921-desktop; build and benchmark compiled with zero warnings/errors, chart smoke passed all 12 renders. Prior gates verified Windows 76, NetworkService 53, NetworkSetup 60 and NetworkProbe 8 tests; the final full gate repeated the installer rollback failure, whose isolated retry passed. Earlier full gates repeated only the settings-reset failure; a full isolated CLI rerun passed all current 159 tests. No clean full-gate result is claimed. The three explicit format/elevated fixtures remain unexecuted. Store CLI built, but its suite encountered the same settings-reset failure; full Store validation was not repeated. Performance results and the small compact scaling cost are recorded in PERFORMANCE-HISTORY.md. Artifacts are under artifacts/cli-desktop-parity. Changes remain uncommitted; concurrent network-default/control edits were preserved.

## Remove the process History column (November 10)

Removed the process History column, scale footer and CLI ProcessGraphData source link. Numeric metrics reclaim the horizontal space; page capacity increases by one row. Resource-gauge histories, desktop process graphs, sampling and retained observations are unchanged. Updated all six sort-render cases to verify numeric columns without history; retained paging, pause, bounded-layout, ASCII/no-color and translation checks. Removed the obsolete process-scale paging test. Current README/developer/design descriptions now reflect the numeric-only process table.

The isolated Release build passed; 159 of 160 CLI tests passed, including all layout/sort cases. The unrelated existing SettingsCommandsShowSetAndResetWithoutSamplingOrInstalling reset failure also reproduced in a focused retry. The full gate was attempted under artifacts/verify/20261110-124850-034-desktop; see its report for final per-suite status. A synthetic 120x36 process-table PNG was inspected at artifacts/cli-no-process-history/processes.png, with six sort captures under artifacts/cli-charts/captures/wide-no-history-capture-*.txt. Four alternating wide benchmarks show about 0.85 MB less allocation per render and similar timing; see PERFORMANCE-HISTORY.md. No Store-specific or new live-terminal smoke was run for this column removal. Changes remain uncommitted.

Final gate completed successfully: artifacts/verify/20261110-124850-034-desktop, 9 checks passed and zero failed; 1,554 tests passed including all 160 CLI tests. App and benchmark builds had zero warnings/errors. The settings-reset failure did not recur in this final full run. Three explicit format/elevated fixtures remained unexecuted.

## Unavailable interactive table metrics (November 10)

Interactive table metric cells convert missing values or the localized Unavailable label to the existing MissingReading dash (—). Conversion happens only during table rendering; original row values, details, snapshot output and resource-gauge labels are unchanged. Added a regression rendering a process with unavailable GPU %, asserting a dash in its row, an unchanged Unavailable gauge label, and unchanged underlying row data. Focused Release build/test passed; the synthetic process-table PNG at artifacts/cli-table-dashes/processes.png was inspected. The first full-gate attempt was temporarily blocked by a concurrently added CliColumnsHint reference before its resources arrived; no unrelated edits were reverted. Final gate results follow below.

Final full verification passed at artifacts/verify/20261110-125834-937-desktop: 9 checks passed, zero failed; 1,564 tests passed including all 170 current CLI tests, and app/benchmark builds had zero warnings/errors. The usual three explicit format/elevated fixtures remain unexecuted. No new live-terminal or Store run was performed for this display-only substitution. Changes remain uncommitted.

## Gold keyboard shortcuts (November 10)

Changed CLI keycaps to #FFD044 using a dedicated Key style. Controls, sort shortcuts, help/navigation and F1-F4 tab keys use it; tab labels, selection backgrounds and green enabled markers retain their styling. The 18 color tests pass, including exact RGB presence, monochrome text parity and 16/256-color fallback. Inspected artifacts/cli-keycolor/keys.png from the actual Spectre capture. Full verification at artifacts/verify/20261110-130055-435-desktop built app/benchmark without warnings/errors and passed all 170 CLI tests; the recurring unrelated installer rollback temporary-file replacement test failed in the full gate and passed its isolated retry. No clean full-gate pass is claimed for this change. No new live-terminal or Store run was performed; changes remain uncommitted.

## Versioned CLI header (November 10)

Interactive and text-snapshot headers now show WinMon followed by the assembly informational version, currently 4.0.6. Shared TerminalChrome.ProductName reads metadata once, using the existing desktop version-resolution pattern and the assembly-version fallback. Added a regression asserting the rendered header matches the actual CLI assembly release version. Inspected artifacts/cli-version-header/header.png and asserted the actual Spectre capture begins with WinMon 4.0.6. Full verification passed at artifacts/verify/20261110-130318-984-desktop: 9 checks passed, zero failed; 1,567 tests including 173 CLI tests passed, and app/benchmark builds had zero warnings/errors. The three explicit format/elevated fixtures remained unexecuted. No new live-terminal or Store run was performed for this header-only change. No version bump or commit was made.
