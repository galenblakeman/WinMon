# WinMon performance history

[Back to the developer guide](DEVELOPMENT.md)

## MCP server collection and concurrent sampling (November 11, 2026)

Windows x64, .NET SDK 10.0.404/runtime 10.0.15. The explicit `PerformanceMeasurement.MeasurePublishedServer` test launches the self-contained server from a path with spaces, warms up for ten seconds, then measures idle collection, 40 snapshots, four concurrent 60-second samples and retained history. `dotnet-counters` 9.0.661903 records runtime allocation/heap counters; `dotnet-gcdump` 9.0.661903 collects two retained-heap snapshots a minute apart after the 120-frame history fills. Commands are in [MCP setup](MCP.md#build-and-verify). Forced GC diagnostics run outside the CPU/latency scenarios. Source-level publication timestamp and error-summary refinements after this profile do not change the collector or retained-history budgets; final protocol/unit regression tests cover those refinements.

| Scenario | Measured seconds | Server CPU ms per elapsed second | Private memory at end |
| --- | ---: | ---: | ---: |
| Idle one-second collection | 20.01 | 20.3 | 39.2 MiB |
| Forty snapshots | 20.27 | 55.5 | 51.4 MiB |
| Four concurrent 60-second samples | 60.72 | 16.2 | 95.9 MiB |
| Following idle retention | 60.01 | 15.4 | 116.7 MiB |
| Idle minute between heap snapshots | 60.00 | 9.6 | 113.3 MiB |

Snapshot latency was 1.50 ms median and 2.98 ms p95 in this run. All four samples completed with about 99.93% CPU interval coverage. Responses were 207,165–207,821 bytes including structured/text representations; the 256 KiB budget retained 30 trend entries and explicitly marked truncation while preserving each full-window summary. Ordinary collection allocated about 0.8 MB/s; serialization creates a transient allocation burst at simultaneous completion. This is one machine/workload, not a capacity guarantee.

The first uncapped profile finished near 141 MiB private memory with a large uncollected heap. Applying the CLI's existing 6 MiB gen0 budget reduced the capped run's final private memory to about 115 MiB. The final post-GC retained heaps were 49,772,966 and 44,232,778 bytes, with 572,893 and 573,124 objects; process private memory after those collections was 109.7 and 106.2 MiB. Live retained data did not grow across the extra minute under the measured process/connection workload. Large temporary serializer/pool allocations explain why instantaneous GC-heap counters and private memory exceed retained object size; no forced collection was added to production code.

Evidence: `artifacts/mcp-performance` (uncapped), `artifacts/mcp-performance-capped`, and `artifacts/mcp-performance-final` (measurements JSON, timeline, runtime counters and both heap reports/dumps). The explicit measurement passed in 3m53s. History bounds, one shared collector and request cancellation are also exercised deterministically.

The existing WPF 850-process live benchmark ran unchanged before and after integration. Baseline: 4.243 ms and 603,252.8 allocated bytes/frame; final repeat: 4.458 ms and 603,230.9 bytes/frame, with identical pixel hashes. An earlier repeat during concurrent release compilation was 4.864 ms; the repeat after compilation narrowed the timing difference to 0.214 ms. No UI/sampling hot-path source changed, and per-batch timing spreads overlap substantially; these runs establish unchanged rendering/allocation, not a precise timing improvement. Evidence: `artifacts/mcp-baseline-bench.*`, `artifacts/mcp-after-bench.*`, `artifacts/mcp-final-ui-bench.*`.

Seven native CLI `snapshot --format json --top 10` runs compared the first packaging build with the final publish. Median elapsed time was 1,545.1 versus 1,543.5 ms; median process CPU was 500.0 ms in both. All snapshots returned schema version 1. This includes startup and the intentional one-second baseline delay, not an interactive terminal-rendering benchmark. Peak working-set readings after exit were unavailable and are not reported. CLI source and shared collector source were unchanged. Evidence: `artifacts/mcp-cli-snapshot-profile.json`.

## Process navigation and final workflow verification (November 10, 2026)

Final Windows x64 Release benchmark after traffic export, capture attribution, period Usage, coverage and navigation. Same 850-process workload, 400 warm-ups and seven batches of 60; original full baseline is artifacts/goal-before.json and final results are artifacts/goal-final-bench.json. The selected/tooltip cases exercise navigation eligibility. Connections cases use 60 processes/480 endpoints; extreme endpoint counts were not load-tested.

| UI scenario | Original full run | Final full run | Final allocated bytes/frame |
| --- | ---: | ---: | ---: |
| Live | 5.364 ms | 4.615 ms | 603,189 |
| Historical | 1.376 ms | 1.348 ms | 180,777 |
| Pinned/selected individual | 5.051 ms | 4.083 ms | 971,860 |
| Pinned/selected grouped | 9.561 ms | 7.738 ms | 778,488 |
| Selected grouped, tooltip open | 12.559 ms | 10.810 ms | 971,856 |
| Connections, retained traffic | 4.191 ms | 3.627 ms | 1,350,328 |

No measured UI timing regression appeared; these single full passes varied with machine conditions and do not establish a general speed improvement. Selected-frame allocations grew by roughly 480 bytes, including captured sample identity and destination checks. Ledger-only recording was 0.0167 ms/32 bytes, and ledger plus bounded Usage history was 0.0452 ms/34,136 bytes per sample. Both 60-minute resource/process persistence cases restored all 1,961 retained samples within their existing budget/deadline. Period Usage remains memory-only.

Final desktop Verify passed with the independently reproduced CLI settings file-access test excluded. Feature (125), Usage (5) and Connections (23) smoke renders passed; new process-menu and both destination screenshots were inspected. Services/coverage smoke passed in the prior increment. Capture smoke's unchanged Settings Monitoring overflow was reproduced in the pre-change binary and remains separate. Store/elevated acceptance and whole-process/native profiling were not rerun for these increments.

## Shared Capture coverage line (November 10, 2026)

The coverage layout adds one collapsed row in Live and one text line in Capture. Full source bounds are formatted only when its tooltip opens; it does not query hidden period totals or copy endpoint history on refresh. The same Release 850-process live benchmark measured 4.499 ms and 603,253 bytes per refresh, within the preceding shared-history run range (3.83–4.52 ms, about 603,231 bytes). This single post-change run does not establish a speed improvement. Source-specific visible behavior is covered by Services (22), Usage (4) and Connections (22) smoke renders; the available and unavailable selected-moment images were inspected. Raw live results: artifacts/goal-item4-live.json.

## CLI whole-process profile and gen0 cap (November 10, 2026)

WinMon CLI 4.0.6, .NET 10.0.14 framework-dependent Release build, SDK 10.0.403, 12 logical processors. `winmon-cli watch --no-save --network=false` ran in a minimized conhost window with saved Top 10 on the Processes tab. It warmed up for 20 seconds, then was measured for 60 seconds. Four alternating runs per build. User applications remained open, with no builds or tests running at the same time.

| Median across four runs | Before | Gen0 cap 6 MiB |
| --- | ---: | ---: |
| Process CPU | 40.0 ms/s | 40.5 ms/s |
| Private bytes (average of 12 readings) | 79.5 MiB | 52.4 MiB |
| Working set at the end | 129.8 MiB | 100.2 MiB |

The CLI now sets the same `System.GC.Gen0MaxBudget` as the desktop app. `watch` allocates about 1.5 MB/s: about 0.9 MB/s from native sampling (half of it the connection table) and about 0.6 MB/s from rendering. With the default L3-sized budget, the gen0 heap filled with garbage between rare collections. CPU did not change measurably.

Where `watch` CPU goes, from per-thread OS times in a 40-second baseline run (31–33 ms/s in total): the thread-pool thread running MonitorSession sampling used about 19 ms/s, and the terminal loop about 5 ms/s, covering rendering once a second and polling input and size 20 times a second. Sampling is the shared MonCore/MonCore.Windows code that the desktop app also runs. A thread-time trace showed the 20 Hz console size reads as most of the loop's wall time. Limiting them to four a second did not change CLI CPU, and conhost stayed at 2–4 ms/s either way. The reads block on the console host rather than spend CPU, so that change was dropped.

`snapshot` takes about 1.7 s wall and 550–720 ms CPU. Most of that is the one-second measurement interval, cold JIT and the first PDH disk counter query (about 0.3 s). `DOTNET_TieredPGO=0` and the OSR settings did not change its CPU measurably. ReadyToRun applies only to published builds, and the CLI is not published yet. Raw results, traces and scripts: `artifacts/cli-perf/watch-results.json`, `host-results.json`, `watch.nettrace`, `alloc.nettrace`, `measure.ps1`, `measure-host.ps1` and `threads.ps1`.

## Recorded-period Usage (November 10, 2026)

WinMon 4.0.6, .NET 10.0.14 Windows x64 Release, SDK 10.0.403. The desktop records uncompacted usage intervals independently of chart compaction; CLI sessions leave this disabled. Immutable owner metadata is shared between consecutive samples. Intervals have a 500,000-observation budget (about 19.1 MiB for the 40-byte interval values, plus owners/queues), and results retain the existing 4,096-app cap plus Other apps.

Four quiet alternating live-frame runs used the existing 850-process benchmark, 400 warm-ups and seven batches of 60. Mean of the two run medians:

| Metric | Before period recording | Shared-metadata recording |
| --- | ---: | ---: |
| UI refresh | 4.155 ms | 4.175 ms |
| Allocated bytes per refresh | 603,257 | 603,231 |

The 0.020 ms difference is small relative to run variation (before 3.99–4.32 ms, after 3.83–4.52 ms). Recording is outside this UI timing. A focused 850-process/120-executable core workload separately measured ledger plus interval recording at 0.135 ms and 34,136 allocated bytes per sample (median of seven 60-sample batches). Nine cold 120–128-second queries had a 5.87 ms median; repeated cached queries averaged 0.000041 ms. Managed heap after filling the observation budget was about 20.9 MB. These are synthetic timings, not native-collection or whole-process measurements.

An initial flat-metadata layout allocated 81,736 bytes per sample; sharing owners reduced that by 58%. Raw runs/harnesses remain in artifacts/goal-before.json, goal-item3-after.json (initial full pass), goal-shared-quiet-*.json, and goal-usage-cost.json. The repository benchmark now includes usage-ledger-and-history-add alongside usage-ledger-add for future full comparisons. Tests cover boundaries, missing/restored observations, gaps, metadata changes, budget eviction, overflow rows and lifetime identity; Usage smoke verifies period/live navigation and the visible partial-coverage note.

## Remove CLI process histories (November 10, 2026)

WinMon CLI 4.0.6, .NET 10 Windows Release, SDK 10.0.403. Removed the wide process History column, scale footer and CLI ProcessGraphData source link. Resource histories remain. The same 61-sample, 850-process, Top 1,000 true-color null-output workload rendered six process rows at 120x36. Four alternating runs per version used 400 warm-ups and seven batches of 60 renders, without concurrent tests/builds or agent-owned live CLI.

| Median across four run medians | Before | Without process history |
| --- | ---: | ---: |
| Time per update | 1.987 ms | 1.955 ms |
| Allocated bytes | 2,321,724 | 1,470,774 |

Removing shared process-series construction reduces allocation by about 0.85 MB per update in this workload; render time is similar. Actual terminal I/O and native collection are excluded. Paging can display one additional row, and numeric columns reclaim the former chart width. Compact rendering is unchanged and was not remeasured. Harness: artifacts/cli-desktop-parity/smoke; baseline and raw measurements: artifacts/cli-no-process-history.

## CLI desktop-style gauge readouts (November 10, 2026)

WinMon CLI 4.0.6, .NET 10 Windows Release, SDK 10.0.403. Compared the saved five-panel summary with colored titles, desktop-style primary/secondary readouts, download-rate network scaling and removal of the two wide detail rows. The synthetic workload used 61 samples, 850 processes, Top 1,000, true-color null output, four process rows at 80x24 and six at 120x36. Four alternating runs per version/layout used 400 warm-ups and seven batches of 60 renders, without concurrent builds, tests or agent-owned live CLI; user applications remained open.

| Median across four run medians | Before | Desktop-style readouts |
| --- | ---: | ---: |
| Compact: time per update | 2.046 ms | 2.191 ms |
| Compact: allocated bytes | 1,122,998 | 1,078,163 |
| Wide: time per update | 2.337 ms | 2.185 ms |
| Wide: allocated bytes | 2,330,521 | 2,318,346 |

Compact rendering now calculates the adapter-specific history scale, adding about 0.145 ms (7%) per update in this sample; the absolute cost is small at the once-per-second refresh rate. Wide timing varied downward; this does not establish a general improvement. Both allocation medians decreased. Shared desktop scale calculations preserve the existing 1-2-5 rule; supporting text uses the same formatter in both frontends. Native collection and actual terminal I/O are excluded. The wide summary is seven rows, freeing two process rows. Harness, baseline and raw results are under `artifacts/cli-desktop-parity`.

## Five CLI resource panels at 120 columns (November 10, 2026)

WinMon CLI 4.0.6, .NET 10 Windows Release, SDK 10.0.403. Compared the four-panel CPU/Memory/Network/GPU summary with five CPU/Memory/Disk/Network/GPU panels, using the existing 61-sample, 850-process, Top 1,000 true-color null-output harness with a 1 Gbit/s link. Both builds rendered four process rows at 80×24 and six at 120×36. Four alternating runs per version used 400 warm-ups and seven batches of 60 renders. No builds, tests or agent-owned live CLI ran during measurement; user applications remained open.

| Median across four run medians | Four panels | Five wide panels |
| --- | ---: | ---: |
| Compact: time per update | 1.550 ms | 1.404 ms |
| Compact: allocated bytes | 1,199,635 | 1,085,539 |
| Wide: time per update | 2.017 ms | 1.987 ms |
| Wide: allocated bytes | 2,292,420 | 2,330,264 |

No material render-time regression was observed. Compact rendering is unchanged; timing/allocation variation does not establish an improvement. The additional wide panel adds about 38 KB per render in this workload. Five 23-column panels and four gaps still occupy 119 cells; resource bars/histories use 19 cells while process-history width and summary height stay unchanged. Native sampling and real terminal I/O are excluded. Results are `artifacts/cli-five-gauges/render-results.json`; the existing harness is under `artifacts/cli-network-gauge/smoke`, with the prior four-panel build under `artifacts/cli-network-gauge/build/bin/WinMon.Cli/release`.

## CLI network gauge and resource order (November 10, 2026)

WinMon CLI 4.0.6, .NET 10 Windows Release, SDK 10.0.403. Compared the colored disk-gauge summary with CPU/Memory/Network/GPU order using the same 61 synthetic one-second samples, 850 processes, Top 1,000 and a 1 Gbit/s network link. Both versions rendered four process rows at 80×24 and six at 120×36 to a true-color ANSI null-output console. Four alternating runs per version used 400 warm-ups and seven batches of 60 renders, without concurrent builds, tests or agent-owned live CLI; user applications remained open.

| Median across four run medians | Disk gauge | Network gauge |
| --- | ---: | ---: |
| Compact: time per update | 1.569 ms | 1.585 ms |
| Compact: allocated bytes | 1,193,438 | 1,182,012 |
| Wide: time per update | 1.979 ms | 1.847 ms |
| Wide: allocated bytes | 2,293,321 | 2,293,248 |

No material regression was observed; timing/allocation differences do not establish a general improvement. The replacement uses the existing derived network percentage and filters history to the displayed adapter. Native sampling and actual terminal I/O are excluded. The harness is under `artifacts/cli-network-gauge/smoke` and results are `artifacts/cli-network-gauge/render-results.json`; comparison assemblies are the prior colored build under `artifacts/cli-colors/build/bin/WinMon.Cli/release` and the verified network-gauge build.

## CLI summary resource colors (November 10, 2026)

WinMon CLI 4.0.6, .NET 10 Windows Release, SDK 10.0.403. Compared the saved spaced-gauge frontend with resource-colored summary spans using 61 synthetic one-second samples, 850 processes and Top 1,000. Both versions received a six-row request: the 80×24 compact layout rendered four process rows, and 120×36 rendered six. The console used true-color ANSI with null output. Four alternating runs per version used 400 warm-ups and seven batches of 60 renders; no builds, tests or agent-owned live CLI ran during measurement. Existing user applications remained open.

| Median across four run medians | Before | Resource colors |
| --- | ---: | ---: |
| Compact: time per update | 1.634 ms | 1.656 ms |
| Compact: allocated bytes | 1,096,088 | 1,102,436 |
| Wide: time per update | 2.163 ms | 2.089 ms |
| Wide: allocated bytes | 2,215,269 | 2,292,945 |

No material render-time regression was observed; timing differences are within ordinary run variability and do not establish a speed improvement. Styled summary spans add about 6 KB per compact render and 78 KB per wide render in this workload. Measurement excludes native sampling and actual terminal I/O. Raw results, the true-color harness and the saved baseline are under `artifacts/cli-colors/render-results.json`, `artifacts/cli-colors/benchmark` and `artifacts/cli-colors/baseline`.

## CLI resource gauges and process history (November 10, 2026)

WinMon CLI 4.0.6, .NET 10 Windows Release, SDK 10.0.403. Compared the saved pre-chart CLI with resource gauges and sort-driven process history using 61 synthetic one-second samples, 850 processes, Top 1,000 and six requested process rows. At 80×24 (compact), the baseline rendered six rows while the gauge layout reserved more summary space and rendered four; at 120×36 (wide), both rendered six. Output went to a non-ANSI null-output console. Four alternating runs per version used 400 warm-ups and seven batches of 60 renders. No verification builds/tests or agent-owned live CLI ran during measurement; existing user applications remained open.

| Median across four run medians | Before | With charts |
| --- | ---: | ---: |
| Compact: time per update | 1.600 ms | 1.806 ms |
| Compact: allocated bytes | 1,102,280 | 1,166,636 |
| Wide: time per update | 1.579 ms | 2.136 ms |
| Wide: allocated bytes | 1,134,096 | 2,178,041 |

The additional compact formatting costs about 0.21 ms; wide charts cost about 0.56 ms and 1.04 MB per update in this workload. Wide process history builds one shared scale over every filtered/Top row, keeping it stable across pages, and formats sparklines only for the current page. Its 60-second per-row values explain most of the extra allocation. This is an explicit cost of the added history at the normal once-per-second cadence, not a claim of zero regression. Values vary with runtime tiering and background activity; these measurements exclude native collection, real terminal I/O and WPF refresh. Raw measurements, the harness and saved baseline are under `artifacts/cli-charts/render-results.json`, `artifacts/cli-charts/benchmark` and `artifacts/cli-charts/baseline`.

## Saved CLI controls and executable pins (November 10, 2026)

WinMon CLI 4.0.6, .NET 10 Windows Release, SDK 10.0.403. Compared the saved adaptive-unit frontend with the settings frontend using five chronological samples, 850 CPU-active synthetic processes with distinct executable paths, Top 1,000, six visible rows and an 80-column non-ANSI null-output console. Both builds were tested without pins and with one process pinned through the P key. Four alternating runs per version/scenario used 400 warm-ups and seven batches of 60 renders, without builds/tests or an agent-owned CLI running alongside. User applications and existing user CLI sessions remained open.

| Median across the four run medians | Before | Saved controls/pins |
| --- | ---: | ---: |
| No pins: time per update | 1.561 ms | 1.644 ms |
| No pins: allocated bytes | about 1,114,718 | 1,180,067 |
| One pin: time per update | 1.504 ms | 2.550 ms |
| One pin: allocated bytes | about 1,375,994 | 1,339,934 |

The additional persistent-identity matching, restored-pin inclusion and current-lifetime checks add about 1.05 ms in the pinned scenario. That expanded work is accepted at the once-per-second cadence; this is not a claim of no regression. Individual timings/allocations varied substantially with runtime tiering and background activity. An initial implementation allocated prefixed path identities and built a full current-process hash set on every render; using existing path strings and checking matching lifetimes reduced median pinned allocation from about 1.51 MB to 1.34 MB. Settings capture/serialization runs after keys, not normal sample deliveries; this benchmark excludes settings I/O, native sampling and actual terminal I/O. Raw initial/final measurements are `artifacts/cli-settings/initial-render-results.json` and `artifacts/cli-settings/render-results.json`, with the harness and saved baseline beside them. The final service-host identity guard affects only a changed/missing service-group case outside these scenarios.

## CLI adaptive desktop units (November 10, 2026)

WinMon CLI 4.0.6, .NET 10 Windows Release, SDK 10.0.403. Compared the saved fixed-unit GitHub Dark frontend with adaptive desktop formatting using the same five-sample, 850-process harness, Top 1,000, six visible rows and an 80-column non-ANSI null-output console. Four alternating runs per version used 400 warm-ups and seven batches of 60 renders. No builds/tests or agent-owned CLI ran concurrently; user applications and the user's existing CLI remained open.

| Median across the four run medians | Fixed units | Adaptive units |
| --- | ---: | ---: |
| Time per update | 1.775 ms | 1.524 ms |
| Allocated bytes per update | 1,165,923 | 1,152,233 |

No material regression was observed. Individual runs varied with runtime tiering and background applications; these values do not establish a general speed improvement. The benchmark excludes native collection and real terminal I/O. Shared desktop formatters add value-specific unit labels and decimal rate formatting without changing query, ranking or retention behavior. Raw results, harness and saved baseline are under `artifacts/cli-adaptive/render-results.json`, `artifacts/cli-adaptive/benchmark` and `artifacts/cli-adaptive/baseline`.

## Interactive CLI control colors (November 10, 2026)

WinMon CLI 4.0.6, .NET 10 Windows Release, SDK 10.0.403. Compared the saved pre-styling frontend with the styled frontend using the existing five-sample, 850-process render harness, Top 1,000, six visible rows and an 80-column non-ANSI null-output console. Both versions received the same six-row viewport. Four alternating runs per version used 400 warm-ups and seven batches of 60 updates; no verification builds/tests or agent-owned live CLI ran during measurement. User applications, including the user's existing CLI, remained open.

| Median across the four run medians | Before | Styled controls |
| --- | ---: | ---: |
| Time per update | 1.405 ms | 1.555 ms |
| Allocated bytes per update | about 1,112,871 | about 1,145,880 |

The extra approximately 0.15 ms and 33 KiB per update are accepted for literal styled segments, key caps, active-tab/selection presentation and the additional numbered sort-guide row at the normal once-per-second cadence. This measures frontend formatting without actual terminal I/O or native collection, and does not establish light-theme contrast or screen-reader behavior. Raw results and the harness are in `artifacts/cli-color/render-results.json` and `artifacts/cli-color/benchmark`; the saved baseline is `artifacts/cli-color/baseline`.

## Full interactive CLI (November 9, 2026)

WinMon CLI **4.0.6**, .NET 10 Windows Release, SDK **10.0.403**, 12 logical processors. The completed frontend adds four tab queries, stable selection/pins, six-metric process rows, details, horizontal columns and optional authenticated service traffic. Cell/detail text is generated only when needed and cached for its immutable row observation; paused live capture does not repeatedly repaint frozen rows.

The saved rolling-average preview and complete frontend were compared with five chronological one-second frames, 850 CPU-active synthetic processes per frame, Top 1,000, an eight-row viewport and an 80-column non-ANSI null-output console. Both builds were asserted to receive five samples. Each run used 400 warm-ups and seven batches of 60 renders; four alternating runs per build, without verification builds/tests or another CLI running alongside, produced:

| Ranking and process-tab formatting | Averaged preview | Full frontend |
| --- | ---: | ---: |
| Median time per update | 0.695 ms | 1.517 ms |
| Median allocated bytes per update | 593,281 | about 1,140,155 |

The added roughly 0.82 ms and 547 KiB per update are accepted for constructing complete row models, selection/pin presentation and additional metric fields at the normal once-per-second sample cadence. This is an expanded workload rather than a claim of no regression. An initial implementation eagerly formatted all details and allocated about 1.90 MB per update; deferred cell/detail formatting reduced that measured allocation by about 40%. Per-run timing varies with runtime tiering and other user applications. No native sampling or actual terminal I/O is included in this microbenchmark. Source, saved baseline and raw results are under `artifacts/cli-full/benchmark`, `artifacts/cli-full/baseline`, `artifacts/cli-full/render-results.json` and `artifacts/cli-full/eager-render-results.json`.

A separate final Release run used the real collector in an 80-column PTY, Processes Top 10, ungrouped, no color, traffic off. After startup, a 20.33-second observation with no builds/tests or other CLI alongside measured **0.186% average total-machine CPU**, **85.0 MiB average working set**, **96.3 MiB maximum working set** and **49.6 MiB average private memory**. User desktop applications remained running. These short-run numbers do not establish full-history memory behavior, default All-mode cost, optional traffic overhead or SSH latency. All mode intentionally retains more process observations, within the existing sample/entry budgets. Raw output: `artifacts/cli-full/live-profile.json`.

The only WPF runtime change delegates Usage filtering/order to the same portable UsageQuery; existing UsageView tests and native Usage smoke passed, and its three dark/light/show-exited PNGs were inspected. No WPF sampling or layout path changed. Native CLI smoke covered all tabs, app/group/filter controls, pins/details, automatic Handles-column reveal, disabled-traffic TCP fallback, Q and Ctrl+C restoration with a live network-service subscription. Active traffic-rate acceptance remains limited by the installed service's observed warm/incomplete/capacity state, documented separately in the CLI checklist.


## CLI desktop ranking averages (November 9, 2026)

The interactive process table now ranks the same five-second window as the desktop, using compacted history with the newest full sample substituted. Pause preserves only that averaging window and its full selected frame. Snapshot commands retain their original one-interval behavior. No portable ranking implementation or desktop path changed.

A .NET 10 Release harness compared the saved pre-fix interactive build with the changed build on the same Windows x64 machine (SDK 10.0.403). It rendered an 80-column, eight-row interactive page into a non-ANSI null-output console, with Top 1,000 and 850 synthetic processes per sample. The baseline used its latest sample; the changed build used five chronological one-second samples. Each run used 400 warm-ups and seven measured batches of 60 renders. Four alternating runs per build, with no verification builds/tests or live CLI running alongside them, gave:

| Ranking and interactive formatting | Before: one interval | After: five intervals |
| --- | ---: | ---: |
| Median time per update | 0.472 ms | 0.950 ms |
| Per-run median range | 0.461–0.495 ms | 0.668–0.996 ms |
| Median allocated bytes per update | 592,249 | about 591,663 |

The roughly 0.48 ms increase is explained by ranking five sets of process observations instead of one. It is accepted to match desktop measurement semantics; the measured update remained below 1 ms at the normal once-per-second sample cadence, with essentially unchanged allocations. This is an isolated ranking/formatting comparison, not an end-to-end CPU or terminal-latency guarantee. Local harness/source, saved baseline and raw measurements are in `artifacts/cli-averaging/benchmark`, `artifacts/cli-averaging/baseline` and `artifacts/cli-averaging/render-results.json`.

NUnit coverage compares CLI ordering/values with `ProcessRanking`, checks spike smoothing, readable-only GPU averages, latest memory/count readings, compacted/full replacement, gap boundaries and frozen-window sorting. Native PTY smoke verified the five-second label, paused timestamp stability while changing sort, and clean Q exit with screen restoration.

## Terminal interactive preview (November 9, 2026)

WinMon CLI **4.0.6**, .NET 10 Release builds, SDK **10.0.403**, Windows x64, **12 logical processors**. No desktop or shared monitoring implementation changed. The existing snapshot table was extracted for reuse by the new `watch` view, so its rendering was compared against the saved pre-interactive preview build.

An isolated console harness generated 850 process observations and ranked/rendered the top 10 into an 80-column, non-ANSI null-output console. Each run warmed up for 400 renders, then measured seven batches of 60. Four alternating baseline/changed runs produced these medians of per-run medians:

| Snapshot ranking and rendering | Before | After |
| --- | ---: | ---: |
| Time per report | 0.5430 ms | 0.5427 ms |
| Allocated bytes per report | 514,833 | 514,833 |

The timing difference is below 0.1%, with no measured allocation regression. This isolates formatting and ranking; it excludes native collection and actual terminal output. The baseline was the locally built, uncommitted snapshot preview, not a released CLI. Local harness/source and raw measurements are under `artifacts/cli-interactive/benchmark`, `artifacts/cli-interactive/baseline` and `artifacts/cli-interactive/render-results.json`.

The new interactive path also ran against the real Windows collector in an 80-column PTY, using default CPU sorting, top 1,000, no color and about 500 observed processes. A 20.29-second observation after startup, with no builds/tests or other CLI instance running alongside it, measured **0.135% average total-machine CPU**, **107.7 MiB average working set**, **112.6 MiB maximum working set**, and **71.1 MiB average private memory**. Other user applications remained running. Raw measurements are in `artifacts/cli-interactive/live-profile.json`.

The live numbers establish an initial short-run baseline, not a before/after claim, full-history memory bound or SSH performance result. History was still filling its 60-second window. Native PTY smoke additionally checked sorting, filtering, paused-frame stability, scrolling, Q exit and Ctrl+C screen/cursor restoration. Desktop terminal screenshots were unavailable because the computer-use service blocked launching PowerShell; no claim of desktop/SSH/screen-reader acceptance is made.

## README refresh profile (November 9, 2026)

WinMon **4.0.6**, self-contained Release build (not single-file published), SDK **10.0.403**, .NET/Windows Desktop **10.0.14**, Windows x64 build **26300.9818**, **12 logical processors**. The existing `--profile-output <dir> 150` diagnostic ran in Full view with isolated default preferences: Top 5 processes, one disk, GPU and adapter monitoring, 30-minute retention, and optional per-process network monitoring off. It warmed up for 20 seconds, then measured 150.45 seconds. No verification builds or tests ran during the profile; the user's existing WinMon instance and other desktop applications remained running.

| Measurement | Result |
| --- | ---: |
| Average CPU, share of the whole machine | 0.248% |
| Average working set | 162.5 MiB |
| Maximum working set | 181.1 MiB |
| Average private memory | 76.4 MiB |
| Maximum private memory | 93.3 MiB |
| Managed allocation | 1,221.9 KiB/s |
| Retained samples at the end | 171 |
| Retained process entries in the latest sample | 129 |

The 129 entries are retained observations, not the machine's total process count. History was still filling, so this run does not establish memory use with a full 30-minute history. These are observations from one short run, not a before/after comparison, a startup measurement or a performance guarantee. It does not measure the optional ETW service. Raw local output is `artifacts/readme-refresh/profile/profile.json`.

Reproduce from a Release build with `WinMon.exe --profile-output <output-directory> 150`; see the [whole-app profiling method](DEVELOPMENT.md#profile-the-whole-app). Run without builds or tests alongside it. The README rounds these results and links here for their conditions; the older October measurements remain below as historical evidence.

## Expired network process history (November 9, 2026)

SDK 10.0.403 / .NET 10.0.14, tiered compilation off. An isolated console probe compiled the before/after ledger and snapshot sources. With a 128-entry history limit and one continuously tracked process, it registered and ended 10,000 distinct processes spaced 40 simulated seconds apart, added traffic to the continuous process and took one snapshot per registration. Each process warmed up 1,000 iterations; four before/after pairs alternated, with no verification build running.

| Per registration, exit, traffic event and snapshot | Before | After |
| --- | ---: | ---: |
| Median time | 1.600 µs | 1.597 µs |
| Allocated | 1,137 B | 1,137 B |

Expired tombstone eviction now skips the false coverage-gap flags and rate-suppression deadline. Allocation is unchanged and timing is within run-to-run noise; no performance improvement is claimed. This isolates collector logic; it does not measure native ETW or UI performance. Probe sources and raw results are under `artifacts/capacity-check/`.

## Per-tick network and connection owners (November 8, 2026)

SDK 10.0.403 / .NET 10.0.14, tiered compilation off. An isolated console probe compiled against the HEAD and changed `MonCore.dll`: 850 processes with image paths, 480 TCP endpoints across 60 of them, network readings for every process, CPU changing every tick. It calls `AttachNetworkOwners` (through reflection) and `Record` per tick, warms up 300 ticks and measures 1,200. Four before/after pairs alternated; allocation was identical across runs of each build, and times are medians of the four runs.

| Per tick | Before | After |
| --- | ---: | ---: |
| `Record` allocated (connection owners) | 412,023 B | 6,951 B |
| `Record` time | 0.270 ms | 0.124 ms |
| `AttachNetworkOwners` allocated | 55,624 B | 6,888 B |
| `AttachNetworkOwners` time | 0.110 ms | 0.074 ms |
| Retained heap, connection history at its entry budget (45 captures) | 12.6 MiB | 2.9 MiB |

`ConnectionOwners` copied every owner into a new `ProcessUsage` each tick, so the connection history held one copy per process per capture. It now reuses the previous tick's copy when the copied metadata is unchanged, which accounts for most of the saving and the retained heap. Both methods also replace LINQ with loops over reused sets; output order and first-observation choice are unchanged. With no connections or network frame, `ConnectionOwners` now returns before scanning processes: in a clean full UI benchmark pair, every live scenario that records a sample per frame with network monitoring off allocated about 7.8 KB less per frame (for example, grouped live 602,293 → 594,613 B). Whole-app profiling was not run.

## Connections traffic-graph data (November 8, 2026)

SDK 10.0.403 / .NET 10.0.14. `ConnectionGraphData.Build` runs on every Connections refresh and walks every capture in the traffic window (300 seconds by default). It created three dictionaries per capture and an upper-cased image key per owner per capture; it now reuses the dictionaries across captures and caches image keys by path. The existing UI benchmark ran HEAD against the changed build:

| Scenario | Before allocated B/frame | After allocated B/frame |
| --- | ---: | ---: |
| Live network rates | 1,376,543 | 1,376,863 |
| Recorded traffic history (121 captures) | 3,488,648 | 1,350,712 |
| Recorded traffic history with pins | 3,491,704 | 1,353,768 |

Allocation is deterministic per build. The live case pays 320 bytes for dictionaries created up front. Timing is inconclusive: other builds and smoke runs shared the machine, and the unchanged live Connections scenario itself moved from 2.49 to 3.20 ms, so no speedup is claimed. Those four pairs' changed build also carried unrelated uncommitted work, so their pixel hashes differed; a later full pair built from HEAD plus only these changes reproduced the allocation figures within 80 bytes and matched the baseline pixel hash.

## Network service ownership probes and GC budget (November 8, 2026)

SDK 10.0.403 / .NET 10.0.14, unelevated, so no LocalSystem service or ETW session ran; each figure comes from an isolated console probe against the service source.

**Ownership probes.** Over 20 passes of the 339 processes readable from this account, a full owner probe (process times, token, SID) took 5.4–5.7 µs and allocated 784 bytes, and a creation-time-only probe took 2.0–2.5 µs and 32 bytes. A packet newer than a known process's last verification previously took the full probe; it now rechecks only the creation time, because a process's token user cannot change. Rejections of another account's processes previously expired after one second and repeated the full probe; they now renew with the creation-time probe until the PID is reused. Rejections for an unknown owner or an event before process creation keep the old one-second expiry. Savings scale with the number of talking processes per second and were not measured end to end.

**GC budget.** A service stand-in (850 lifetimes, 20,000 events and one serialized snapshot per tick, 600 ticks at 20 per second, about 13 MiB allocated) ran twice under each setting:

| Setting | Peak private | Peak working set | Gen0 collections |
| --- | ---: | ---: | ---: |
| Default | 20.7 MiB | 46.6–47.1 MiB | 0 |
| `System.GC.Gen0MaxBudget` 6 MiB | 16.0–16.9 MiB | 41.7–42.8 MiB | 2 |
| 6 MiB, background GC off | 15.8–16.3 MiB | 41.6 MiB | 2 |

With the default budget nothing was collected, so the gap grows with uptime up to the machine's L3-sized budget. The service now uses the app's 6 MiB cap; turning background GC off made no measurable further difference and was not adopted.

**Not adopted: source-generated protocol JSON.** Write plus read of a 512-row snapshot response measured 47.6–49.3 ms cold and 0.271–0.279 ms warm with reflection, against 40.2–44.8 ms cold and 0.265–0.275 ms warm with a `JsonSerializerContext`. Wire bytes were identical. A few milliseconds once per process did not justify the extra code.

## Network service ledger and event loop (November 8, 2026)

SDK 10.0.403 / .NET 10.0.14, tiered compilation off. An isolated console probe compiled against HEAD and the changed `WinMon.NetworkService`: 850 registered lifetimes (the startup seed), 64 of the owner's talking at 200,000 events per simulated second for 40 seconds, 25 short-lived process starts per second and one owner snapshot per second. Each run repeats the workload seven times and reports medians of the last six; four before/after pairs alternated.

| Measurement | Before | After |
| --- | ---: | ---: |
| Seed of 850 registrations | 13.2 ms, 52.7 MiB allocated | 0.38 ms, 383 KiB allocated |
| Per process start (Register + End) | 54 µs | 1.9 µs |
| Snapshot (64 rows) | 0.13 ms, 378 KiB allocated | 0.023 ms, 7.9 KiB allocated |
| `Add` per event | 16.6 ns | 16.6 ns |

`Register` pruned every lifetime's buckets with a LINQ copy on each call, so seeding was quadratic in allocation. Buckets are now pruned only when the retention second advances, without allocating, and snapshots skip rate calculation for lifetimes with no traffic. The collector also drops a write-only per-packet cache update, drains its queue in batches of 4,096 events per lock hold, and reuses its one-second idle timer instead of creating one per queue wake. Those collector changes are outside this probe and were not measured; the native ETW test was not run.

## Network reliability diagnostics (November 8, 2026)

SDK 10.0.403 / .NET 10.0.14. An isolated console probe compared the HEAD ledger/snapshot source with the changed source: 512 process lifetimes, six event-time buckets each, then snapshot construction plus JSON serialization. Each process warmed up 300 iterations and measured 2,000; four before/after pairs alternated with no verification builds running. Tiered compilation was disabled in both payloads to remove the unstable tier-transition effect observed in the initial concurrent run.

| Measurement | Before | After |
| --- | ---: | ---: |
| Median ms/snapshot | 0.328 | 0.325 |
| Run range, ms/snapshot | 0.323–0.333 | 0.324–0.337 |
| Allocated bytes/snapshot | 334,744 | 334,776 |
| Serialized bytes/snapshot | 63,046 | 63,068 |

Timing overlaps and establishes no observed regression in this workload; no speedup is attributed. The bounded reason field adds 32 allocated bytes and 22 wire bytes per snapshot. The initial tiered/concurrent measurements are retained but excluded from the controlled comparison. This probe excludes ETW callbacks, ownership probes, WPF, IPC scheduling and installed-service/whole-system overhead. It does not replace a native sustained-throughput budget. Evidence and isolated original/changed sources: `artifacts/network-reliability-measure/results-no-tiering.json`; the original ledger's fresh-window regression check returned null while the changed one returned the expected 10 bytes/second.

## Per-chart preferences (November 8, 2026)

Per-chart configuration snapshotting runs on chart selection and existing preference validation/save paths. Resource-control visibility uses the existing presentation update. No sampling-loop work or per-sample preference writes were added. Standard and Store app/test-source compilation passed with zero warnings/errors. Tests, UI smokes and performance measurements were not run because the user requested that checks remain stopped; there is no measured before/after performance result for this change.

## Connections monitoring setup strip (November 8, 2026)

SDK 10.0.403 / .NET 10.0.14. Four alternating before/after pairs used frozen payloads, 400 warm-up frames and seven batches of 60. A task-local copy of the existing benchmark measures its four Connections scenarios and skips unrelated measurements; sampling, native collection and logging are disabled. Both payloads include the existing traffic-period work. The baseline is the task-start working checkout rather than clean main; concurrent tooltip/export work also appears in the after checkout, so these are checkout comparisons rather than isolated attribution to this strip. No builds or tests ran alongside the final pairs.

| Scenario | Before ms/frame | After ms/frame | Before allocated B/frame | After allocated B/frame |
| --- | ---: | ---: | ---: | ---: |
| Monitoring disabled, setup strip visible after change | 4.474 | 4.448 | 629,774 | 629,596 |
| Live network rates, strip collapsed | 3.440 | 3.696 | 1,414,024 | 1,413,574 |
| Recorded traffic history, strip collapsed | 3.885 | 3.851 | 3,526,712 | 3,526,128 |
| Recorded traffic history with pins, strip collapsed | 3.847 | 3.807 | 3,529,760 | 3,529,176 |

Allocation is effectively unchanged. Timing is inconclusive: the live network scenario's aggregate median rises 7.4%, but its run medians span 3.288–4.244 ms before and 3.299–4.012 ms after, with the fourth pair substantially faster after. Other scenario medians stay close. This does not establish a speed improvement or a sustained regression. Pixel hashes are stable within each payload, change as expected for the visible strip, and match across payloads for all three enabled-network cases. This measures UI-thread refresh/layout/render submission, excluding the WPF render thread and native collection.

Final sequential `tools/Verify.ps1 -Smoke connections` and `tools/Verify.ps1 -Store` passed without build warnings: 1,257 standard tests, 1,259 Store tests and 20 Connections renders. Dark, Light, Smoked Sapphire, German minimum-width and enabled-network renders were inspected; the setup button's rendered text contrast, placement and focus are smoke assertions. The screenshots use WPF content capture; native window captions were not verified. Native service installation and real Windows high-contrast mode were not exercised. Earlier concurrent translation builds and unrelated setup rollback/native dump tests failed transiently; isolated reruns and both final verification runs passed.

Raw results, focused driver, frozen payloads and runner are under `artifacts/network-prompt/`: `{before,after}-{1..4}.json`, `comparison.json`, `driver/`, `{before,after}/` and `Measure.ps1`. App SHA-256: before `996A6374951931C4C77F24A323DB87BFB908546EB7B6B23340CC7E943334AE3B`; after `EFF82D1A42817894A86786A2677A4F9671513EA10FB5E4D657FAC00820D25790`. The initial full benchmark remains in `baseline-initial.json` and is excluded from the comparison. The first baseline restore hit unavailable NuGet audit endpoints; the final app and benchmark verification builds completed without warnings.

## Connections header count and ranked separator (November 8, 2026)

SDK 10.0.403 / .NET 10.0.14. Four alternating before/after pairs used frozen app payloads and the existing UI benchmark's traffic-history workload: 850 synthetic processes, 60 connection owners, 480 endpoints, 400 warm-up frames and seven batches of 60. A task-local copy of the benchmark skips unrelated measurements and uses one pin so the new ranking separator is visible. Logging is disabled in both payloads. The baseline already includes the header pin-count move and removed footer lines; this comparison measures the additional ranked separator. The final template reuses the existing row grid rather than wrapping every row in another panel.

| Scenario | Before ms/frame | After ms/frame | Before allocated B/frame | After allocated B/frame |
| --- | ---: | ---: | ---: | ---: |
| Traffic history, no pins | 4.834 | 4.494 | 3,244,556 | 3,240,650 |
| Traffic history, one pin and ranked rows | 3.336 | 3.225 | 3,185,836 | 3,185,912 |

Timing ranges overlap, so these results establish no slowdown rather than a speed improvement. The pinned case adds 76 allocated bytes per frame, including the localized ranking-label formatting (about 76 B/s at the normal 1 Hz refresh). Pixel hashes are stable within each build; they differ across builds with the revised row template and visible ranking separator. English/German pinned/ranked, Pinned only, healthy/unavailable network and narrow/scrolled smoke renders were inspected. No builds or tests ran alongside the final benchmark pairs. Native collection and the WPF render thread are outside this measurement.

Raw results, frozen payloads, focused driver and runner: `artifacts/connections-header-performance/{before,after}-{1..4}.json`, `comparison.json`, `{before,after}/`, `driver/` and `Measure.ps1`. App SHA-256: before `CCC7BBD7D3D3B7919EB0CCC0B50B77CF851E29972530A5A7141C4859AE288D8A`; after `42E1DE9533BC2F52CA69EEC88F21D2E98E4DB25C8E027115CC0A3F536CED8351`. The exploratory wrapper/five-pin run remains under `initial-*.json` and is excluded from this comparison. The focused driver build needed execution outside the sandbox for restore; its cached NU1900 audit warnings reflect unavailable vulnerability endpoints, with no dependency changes. Final `tools/Verify.ps1 -Smoke connections` passed the app and benchmark builds without warnings, all 1,234 tests and 16 smoke renders. One preceding run hit an unrelated diagnostic-log test failure; isolated and final full reruns passed.

## 2026-11-08 — Diagnostic file logging

Four alternating logging-disabled/enabled pairs used the existing 850-process live-frame workload, 400 warm-up frames and seven batches of 60 on .NET 10.0.14 with `DOTNET_TieredCompilation=0`. Both modes use the same assembly, preserving the existing working-tree changes and isolating the background log writer; benchmark logs are under the result directory rather than the running app's log folder. Raw JSON/PNGs and runner: `artifacts/logging-performance/{without,with}-{1..4}.{json,png}` and `Measure.ps1`; summary: `comparison.json`. An initial full pre-logging worktree result remains at `artifacts/logging-baseline.json` but is not mixed into this controlled comparison.

| Pair | Logging off ms/frame | Logging on ms/frame | Difference ms/frame |
| --- | ---: | ---: | ---: |
| 1 | 5.152 | 5.325 | +0.173 |
| 2 | 5.099 | 4.950 | -0.149 |
| 3 | 5.276 | 6.329 | +1.053 |
| 4 | 8.662 | 7.502 | -1.160 |

Every run allocated **608,512 B/frame** and produced the same live pixel hash, `6D33EC906EEFFC4EE706362BFFD37F25A3641BDFDA47B2C1F4EB71A903F3DC87`. The median paired difference was +0.012 ms/frame. Aggregate mode medians were 5.214 ms off and 5.827 ms on (+11.8%), but run-to-run ranges were 5.099–8.662 ms off and 4.950–7.502 ms on, with two pairs faster and two slower. Timing is **inconclusive**, not proof of a regression or of zero overhead; repeat on a quiet machine for a release timing gate. This measures healthy UI-thread work with an active writer, not native service collection, a sustained error storm, disk-full hardware behavior or whole-process CPU/memory. Queue/rotation bounds and failure recovery have focused NUnit coverage. A preliminary comparison using the app's normal log directory was discarded after a live app launch prevented the fourth writer from opening; the isolated run verifies `LoggingEnabled` in every report.

Dated measurement and validation records, newest first. Each section describes the build and machine it was measured on, and some name code that has since changed or moved. For how to measure today and the decisions still in force, see [Performance](DEVELOPMENT.md#performance) in the developer guide. The raw data named under `artifacts/` is local and not in the repository.

## Automatic network recovery (November 8, 2026)

SDK 10.0.403 / .NET 10.0.14. The UI benchmark disables `NetworkMonitoringController`, so a task-local console harness compiled the unchanged controller from HEAD and the changed source against the same fake healthy connection. Four alternating before/after pairs retained real one-second delays, discarded the first two intervals and measured six subsequent intervals per process. No builds or tests ran alongside the final pairs. The harness excludes native collection, IPC and UI work; it measures the healthy polling path only. Sources, runner and raw results: `artifacts/network-recovery-perf/{Before.cs,After.cs,Program.cs,RecoveryPerf.csproj,Measure.ps1,before-1.json,after-1.json,...,comparison.json}`.

Median managed allocation was **433.33 bytes per poll in both builds**, including harness overhead. Median intervals were 1,008.62 ms before and 1,008.32 ms after, dominated by the fixed delay and timer scheduling rather than processing time. Process CPU medians were 0 and 2.60 ms per poll; these short runs have coarse CPU-time granularity (two changed runs also reported zero), so they do not establish a CPU cost or improvement. An exploratory run found approximately 48 extra bytes per poll from the added `HasFlag` check; the final polling path uses a bitmask comparison and removes that increase. Exploratory data is retained as `initial-*.json` and excluded from the final comparison.

Recovery adds no healthy-path service requests and uses one tracked task, with a five-second delay between failed connection attempts. The regression tests cover repeated failures and cancellation; sustained native failure/recovery CPU cost and real sleep/resume were not measured. Final standard and Store verification passed 1,222 and 1,224 tests. Connections and capture/Settings smoke checks passed with 15 and 22 renders; representative network states were inspected. The initial standard run hit an unrelated setup rollback test failure; isolated and subsequent full reruns passed.

## Service pins and toolbar order (November 7, 2026)

SDK 10.0.403 / .NET 10.0.14. Four alternating before/after pairs used frozen application assemblies and the existing expanded Services fixture: 850 synthetic processes, 300 services, 60 hosts, 400 warm-up frames and seven batches of 60. The task-local driver copies the existing benchmark source, skips measurement of other scenarios and exits after Services; both payloads use the identical driver. Baseline is the working tree before the service-pin changes, including the pre-existing Connections work. WinMon.dll SHA-256: before `404F0C3CB7D8A0E689C33E2B03386E9CC570350FBEA47F05E2A4E635194B74F6`; after `54CB8E976A5991736CE40A3838D5309D7CB5DE0F2656A1EF811840BF17673C73`. Raw results: `artifacts/services-pins/{before,after}-{1..4}.json`; medians: `artifacts/services-pins/comparison.json`; driver and runner: `artifacts/services-pins/driver/Program.cs` and `Measure.ps1`.

| Services scenario | Before ms/frame | After ms/frame | Before allocated B/frame | After allocated B/frame |
| --- | ---: | ---: | ---: | ---: |
| Expanded, no saved pins | 7.254 | 7.338 | 1,043,006 | 1,113,438 |

The 0.084 ms (1.2%) median timing difference is inside the observed run-to-run spread: before 6.840–7.523 ms, after 7.211–7.619 ms. The additional interactive pin buttons, bindings and pinned/ranked section visuals carry a measured allocation cost of about 70.4 KB/frame (6.8%, approximately 70 KB/s at the normal 1 Hz refresh). No sampling or native collection work was added. The first implementation allocated about 77.6 KB/frame above baseline; notifying only changed cells reduced that overhead by about 7 KB/frame and avoids refreshing pin labels on metric ticks. Its raw comparison is retained as `initial-comparison.json` and `initial-{before,after}-{1..4}.json` in the same directory.

Each build produced one stable Services pixel hash across all four runs. The changed hash also matches the initial implementation, confirming that the notification optimization preserved this rendered fixture. Hashes differ between baseline and changed as intended for the toolbar and pin icons. The initial overlapping full-benchmark attempt was stopped and excluded; both focused comparisons ran after the separate Connections benchmark finished, with no builds or tests running alongside them. These results cover UI refresh/layout/render submission with no saved service pins; pinned ordering, shared hosts and filters are covered by tests and smoke checks rather than a separate performance scenario. Native sampling and the render thread are outside this measurement.

Standard verification passed all 1,213 tests and the 21-render Services smoke; Store verification passed all 1,215 tests. Dark/light pinned overviews, Pinned only, narrow/wrapped and German renders were inspected. Smoke checks exercise actual pin clicks, focus after row moves, and focus recovery when unpinning removes a row. An earlier run hit a shared build-folder lock, so an unchanged copy of `tools/Verify.ps1` redirected its root/output paths to the task-owned verification directory. One unrelated Network Setup rollback test failed once and passed on subsequent full runs. NuGet vulnerability auditing was disabled for these commands because its endpoints were unreachable.

## Connections executable rows and pins (November 7, 2026)

SDK 10.0.403 / .NET 10.0.14. Four sequential before/after pairs used frozen builds, 400 warm-up frames and seven batches of 60. Baseline WinMon.dll SHA-256: `C47BF358A1D2128FCE64AAF1F6B1F1F30168BCA76DB2D7F4752A33268FC12F1D`; changed: `77A51DB553FEDA2969CE327EB7AE806EEB1E1A2F215A4BC442D8DAFDC70FBE35`. Raw results: `artifacts/connections-pins-{before,after}-{1..4}.json`; medians: `artifacts/connections-pins-comparison.json`.

| Connections scenario | Before ms/frame | After ms/frame | Before allocated B/frame | After allocated B/frame |
| --- | ---: | ---: | ---: | ---: |
| Executable rows, monitoring disabled | 2.198 | 1.989 | 920,788 | 554,217 |
| Executable rows, monitoring enabled | 3.265 | 2.709 | 1,477,647 | 1,107,987 |
| Retained traffic history | 3.513 | 3.176 | 3,567,884 | 3,207,664 |
| Retained traffic history, five pins | — | 3.184 | — | 3,210,624 |

The previous expanded-group workloads are compared with the new executable-only rows, with the same endpoint fixture. Removing endpoint children and their unused formatting/identifiers accounts for the lower cost. The five-pin case adds about 0.008 ms and 2,960 bytes per refresh relative to the new unpinned history case. It uses the same retained captures and row population; pins change ordering rather than duplicating ranked rows. Compact pixel hashes match in all four pairs. Connections hashes intentionally change with the removed selector, pin controls and executable-only layout.

The changed snapshot also includes concurrent Services pin work from the shared checkout. These are Connections-specific measurements, not an isolated Services or whole-app comparison; concurrent development also limits timing attribution. Both builds measure UI-thread refresh/layout/render submission, excluding native collection and the render thread. Desktop and Store verification passed; Connections and feature smokes passed with 14 and 113 renders. Pin persistence, executable restart, late path discovery/deduplication, PID reuse, disconnected/unrecorded states and Top/filter behavior are covered. The focused pin smoke checks that the focus outline does not widen a row or introduce horizontal overflow.

## Connections traffic history (November 7, 2026)

SDK 10.0.403 / .NET 10.0.14. Four sequential before/after pairs used frozen builds and the existing 400 warm-up frames, seven batches of 60. No builds or tests ran alongside the measured pairs. Baseline WinMon.dll SHA-256: `5F7A35600672D3D489617A457EE31641B6557CE64237CCAF51B0B546C687C0E7`; changed: `C47BF358A1D2128FCE64AAF1F6B1F1F30168BCA76DB2D7F4752A33268FC12F1D`. Raw results: `artifacts/connections-history-{before,after}-{8..11}.json`; medians: `artifacts/connections-history-comparison.json`. Earlier runs 1–5 used intermediate builds or overlapped verification and are excluded.

| Connections scenario | Before ms/frame | After ms/frame | Before allocated B/frame | After allocated B/frame |
| --- | ---: | ---: | ---: | ---: |
| Grouped, expanded; monitoring disabled | 2.197 | 2.188 | 920,756 | 920,788 |
| Individual; monitoring disabled | 2.273 | 2.277 | 962,309 | 962,341 |
| Grouped, expanded; monitoring enabled | 3.123 | 3.256 | 1,297,777 | 1,477,647 |
| Individual; monitoring enabled | 2.852 | 3.268 | 1,200,024 | 1,451,189 |
| Grouped; retained traffic history | — | 3.480 | — | 3,567,884 |
| Individual; retained traffic history | — | 3.482 | — | 3,017,657 |

Monitoring-disabled refresh cost is effectively unchanged. Enabled rows add the history controls, data arrays and localized help bindings; the small time/allocation increase in the existing enabled cases is the cost of that new column. The additional history cases seed 121 captures with 480 endpoints, 60 endpoint owners and 850 traffic readings per capture. The existing 100,000-entry capture budget retains the last 71 captures; these cases freeze the recorded samples during measurement so warm-up cannot evict the traffic data. They assert that an actual traffic trace is rendered. They measure new work, so there is no baseline history trace to compare.

The first history implementation allocated about 7.5 MB/frame grouped and 7.0 MB/frame individual. Restricting owner/rate lookup tables to the displayed executable paths or lifetimes reduced those workloads to about 3.6 MB and 3.0 MB, without caching another copy of retained captures. Full-view pixel hashes intentionally change with the tab order and history column; Compact hashes match in all four pairs. Measurements cover UI refresh/layout/render submission, excluding native collection and the render thread. Verification passed all 1,215 tests, the 15-render Connections smoke and the feature smoke; dark/light, minimum-width, scrolling, Capture, unavailable and German renders were inspected.

## Connections single-row toolbar (November 7, 2026)

SDK 10.0.403 / .NET 10.0.14. Four sequential before/after pairs used the existing UI benchmark (400 warm-up frames, seven batches of 60). Both payloads include the pre-existing, uncommitted Connections optimizations; this comparison isolates the toolbar/protocol-filter change rather than comparing with `main`. Frozen WinMon.dll hashes: before `CAA1278519B1BD691F9B86F1739B0314793370F89C13A62324CBE8052A2ACA75`, after `4B4ACB0A658DBD695555AF3626C85E41AC8CF8F8225502EFB14E2659D079517C`. The final build subsequently added smoke assertions only. Raw results: `artifacts/connections-layout/{before,after}-{1..4}.json`; medians: `artifacts/connections-layout/row-comparison.json`.

| Connections scenario | Before ms/frame | After ms/frame | Before allocated B/frame | After allocated B/frame |
| --- | ---: | ---: | ---: | ---: |
| Grouped, expanded | 2.668 | 2.435 | 920,972 | 920,780 |
| Individual | 2.921 | 2.900 | 962,525 | 962,333 |
| Network grouped, expanded | 3.946 | 4.059 | 1,302,153 | 1,297,801 |
| Network individual | 3.441 | 3.334 | 1,204,400 | 1,200,048 |

Allocations decreased in all four scenarios. Each Connections scenario had a stable pixel hash within each build; differences between builds are intentional. Timing evidence is limited: another independently running `artifacts/perf-tuning/changed/WinMon.Performance.exe` overlapped these runs, and verification overlapped the first changed run. The network-grouped median increased by 0.113 ms (2.9%), smaller than its observed run-to-run spread. These timings do not establish a speed improvement or isolate a regression; an idle-machine comparison remains unverified.

`tools/Verify.ps1 -Smoke connections` passed both builds, five test suites (1,213 tests), and fifteen inspected renders. English/German minimum-width smoke assertions verify that the toolbar controls fit on one row and that the traffic summary contains no line break.

## Connections row keys computed once (November 7, 2026)

SDK 10.0.403 / .NET 10.0.14. Seven alternating baseline/changed pairs (pairs 5–7 launched the changed build first) used the `WinMon.Performance` driver with 400 warm-up frames and seven 60-frame batches. Baseline WinMon.dll `8F2C597D…` (commit `8d5165c`); changed `5C435211…`. Raw results: `artifacts/perf-tuning/{baseline,changed}-{1..7}.json`.

`ConnectionsView` now builds each endpoint's row key and local-endpoint text once per refresh, in `Entries()`, instead of rebuilding them for each displayed group and again for the stale-row pass. Endpoint rows whose key, owner name, layout, traffic summary and language are unchanged skip rebuilding their text.

| Scenario | Baseline median | Changed median | Allocation per frame |
| --- | ---: | ---: | ---: |
| Connections, grouped and expanded, monitoring off | 2.57 ms | 2.49 ms | 1,076,196 → 921,020 B (-14%) |
| Connections, Individual, monitoring off | 3.11 ms | 2.44 ms | 1,983,685 → 962,573 B (-51%) |
| Connections, grouped and expanded, monitoring on | 3.39 ms | 3.43 ms | 1,457,377 → 1,302,201 B (-11%) |
| Connections, Individual, monitoring on | 3.92 ms | 3.15 ms | 2,225,585 → 1,204,448 B (-46%) |

Every pixel hash matches the baseline. Allocation in the other scenarios is unchanged (within 4 KB/frame). Their timings were bimodal on this machine during the runs: pinned-selected, for example, alternated between about 3.1 and 4.1 ms in both builds while other work ran on the machine. The grouped cases are within that noise; the Individual improvement held in all but one of seven changed runs.

## Connections app-first Capture history (November 7, 2026)

SDK 10.0.403 / .NET 10.0.14. Four strictly sequential baseline/changed pairs used the `WinMon.Performance` driver with 400 warm-up frames and seven 60-frame batches. The original baseline payload was frozen before this work; the changed payload was rebuilt after final test and smoke validation, then frozen before measurement. Baseline WinMon.dll SHA-256: `9046875701E5C241C707E607AEE0D9ADA574923CDB6554689DB214A21AE8E210`; changed: `989C0F30D61C4CF5BC9F593CCF5398D891DAAD153862770E93A3F982198EA5A3`. The final changed build also contains concurrent Settings work, which these scenarios do not exercise. Raw results: `artifacts/connections-redesign/accepted-{baseline,changed}-{1..4}.json`.

The Connections benchmark uses 60 executable groups and 480 endpoint rows. It explicitly selects **All** rather than the new default Top 10 so the workload stays comparable. Connection images intentionally differ between builds; every visual hash was stable within its own four runs. The other twelve visual scenarios match across builds.

| Scenario | Baseline median | Changed median | Allocation per frame |
| --- | ---: | ---: | ---: |
| Connections, grouped and expanded, monitoring off | 2.54 ms | 2.25 ms | 1,495,676 → 1,075,668 B (-420,008 B) |
| Connections, Individual, monitoring off | 2.90 ms | 2.79 ms | 1,824,317 → 1,983,157 B (+158,840 B) |
| Connections, grouped and expanded, monitoring on | 3.08 ms | 3.16 ms | 1,807,684 → 1,456,849 B (-350,835 B) |
| Connections, Individual, monitoring on | 3.06 ms | 3.59 ms | 1,997,880 → 2,225,057 B (+227,177 B) |

The Individual monitoring-on case rose 0.53 ms (17.5%). It formats and retains the owner traffic summary used for the new rate ranking while retaining endpoint detail; the allocation increase is the same visible-mode tradeoff. Grouped app-first refresh is lower in both allocation cases. All non-Connections timed scenarios stayed within 0.04 ms or improved in their aggregate median; their allocations changed by at most 1,197 B/frame. This is a four-pair UI-thread observation, not proof of a universal speedup.

Two 150-second whole-app native profiles measured continuous endpoint capture plus bounded Capture history at about 660 endpoints, with reversed launch order. The changed app averaged +2.60 CPU ms/s, +14.89 MiB private memory and +418.86 KiB/s allocation over the two paired observations. These costs are expected from each-second endpoint collection and retained endpoint/network snapshots. These are app-process figures, not isolated IP Helper or system-wide kernel/service measurements. External traffic variation is uncontrolled, and the signed network service, longer retention and larger endpoint tables were not measured; raw data: `artifacts/connections-redesign/profile-{baseline,changed}{,-reversed}/profile.json`.

## Optional network Connections UI comparison (November 7, 2026)

Four strictly sequential baseline/changed pairs on SDK 10.0.403 / .NET 10.0.14 used 400 warm-up frames and seven 60-frame batches. Both builds came from one frozen source snapshot with the same unrelated gauge edits; the baseline restored only the five Connections/view/benchmark integration files to HEAD. WinMon.dll SHA-256: baseline `7B346CBD…`, changed `2C45BCCF…`. This isolates the UI change: the sampling/session network integration is common to both builds and its disabled-path overhead is not established by this comparison. Raw data: `artifacts/network-connections-performance/accepted-v4-{b,c}{1..4}.json`, process exit records, per-Connections PNGs, and `accepted-v4-summary.json`.

| Scenario | Baseline median | Changed median | Allocation per frame |
| --- | ---: | ---: | ---: |
| Connections, grouped/expanded, monitoring off | 2.947 ms | 2.886 ms | 1,494,380 → 1,494,564 B (+184 B) |
| Connections, individual, monitoring off | 3.174 ms | 2.956 ms | 1,823,021 → 1,823,205 B (+184 B) |
| Network enabled, grouped/expanded | — | 3.087 ms | 1,747,724 B |
| Network enabled, individual | — | 3.034 ms | 1,937,921 B |

Values are medians of the four per-run medians. No shared timed scenario had a higher aggregate median in this comparison, but the wide per-run variation prevents attributing the apparent speedups to this change. The disabled Connections allocation increase is about 0.01%; this path now updates the optional column/width state. The new enabled cases measure synthetic network formatting/layout in the existing narrow left viewport, where the new column can be horizontally clipped; they do not establish full visible-column rendering cost. Native collection and the WPF render thread are excluded.

All fourteen shared visual scenarios were deterministic within each build across all four runs. Twelve match exactly between builds. The two disabled Connections cases differ in exactly 917 header pixels, confined to x21–797/y575–586 in an 836×840 image, by at most one level per RGB/alpha channel. This is the explained text-antialiasing effect of the synchronized header viewport; row content and geometry are identical. Individual before/after PNGs were inspected. A null pixel field on the nonvisual usage-ledger scenario initially inflated the runner's displayed visual count; the final assessment counts only actual hashes. The second pair's guard initially rejected the same one-level alpha rounding, which was independently checked before continuing pairs three/four.

This completes the frozen UI comparison, not the full feature performance gate. Disabled sampler/session overhead, visible network-column cost and signed installed-service/client/whole-system budgets remain open. Signed native installation is now available and has passed functional install/IPC/repair/removal tests; earlier local signing/toolchain blockers below are historical, not the current reason these measurements are missing.

## Configurable native gauge peaks (November 7, 2026)

UI-thread benchmark on .NET 10.0.14 / SDK 10.0.403. Four alternating pairs (four baseline runs and four changed runs) used the same `WinMon.Performance` driver (SHA-256 `3E82936F…`) with 850 synthetic processes, 400 warm-up frames and seven 60-frame batches. The baseline `WinMon.dll` was `2BBF6681…`; the final always-native peak-label build was `03B9CC2D…`. Raw results are `artifacts/peak-before-{1..4}.json`, `peak-after-{1..4}.json` and `peak-benchmark-summary.json`.

| Scenario | Baseline median | Changed median | Allocation change |
| --- | ---: | ---: | ---: |
| Live frame | 4.869 ms | 4.596 ms | +1.3% (600,927 → 608,871 B/frame) |
| Small multiples | 2.816 ms | 2.814 ms | +0.2% |
| Heatmap | 2.962 ms | 3.047 ms | +0.1% |
| Focus | 2.943 ms | 3.125 ms | +1.4% |
| Historical | 1.173 ms | 1.240 ms | +0.7% |
| Grouped live | 5.229 ms | 5.823 ms | +0.2% |
| Services live | 5.282 ms | 5.838 ms | +0.1% |
| Compact | 1.633 ms | 1.698 ms | +2.5% |
| Open metric tooltip | 8.910 ms | 9.319 ms | +4.4% |
| Logical-processors grid | 5.713 ms | 6.331 ms | +0.9% |
| Connections, grouped | 2.965 ms | 4.661 ms | +250.0% (1,493,183 → 5,225,615 B/frame) |
| Connections, individual | 3.138 ms | 3.104 ms | +0.7% |

Live-frame time improved, while grouped (+11.4%), Services (+10.5%) and the logical-processor grid (+10.8%) were slower with substantial per-run variation. Grouped Connections was consistently slower (+57.2%) and allocated much more. This working tree also changed network integration between the baseline and final `WinMon.dll`, so these results establish the combined-build effect and do not attribute those regressions solely to gauge peaks. The peak change retains source samples; formatting and UI updates, rather than that retained value type itself, are the plausible allocation sources. Pixel hashes differ intentionally because the new peak icons and native values replace the previous percentage/scale headers. The hidden/minimized cache-release cleanup is included in the final build but is outside this visible benchmark path.

## Optional network setup and subscription (November 7, 2026)

Signed setup/Settings increment on SDK 10.0.403 / .NET 10.0.14. The feature defaults off; no service connection or IPC polling starts in the disabled branch. An enabled controller maintains one subscription and requests the existing delayed event-time snapshot about once per second, independently of UI visibility. Setup verification, extraction and UAC launch run off the UI thread; Settings locks edits and supports cancellation. These are code-path observations, not overhead measurements.

Installed-path performance comparison remains **blocked**, rather than accepted without data: Azure signing returned 403, no matching local RSA private-key path is known, and NativeAOT publishing lacks a working C++/Windows SDK environment. No signed service was installed. The previous collector/testhost measurements below still describe that earlier experiment; they do not measure the new LocalSystem/helper/client path. No UI hot-path rate calculation or additional history was added in this increment. Before release, measure disabled versus enabled steady-state app/service CPU, allocation/private memory, startup and tab-hidden/tray operation with the real signed installation, alongside the existing UI benchmark. Concurrent gauge changes prevent attributing whole-app differences from this working checkout to network setup alone.

## Network service and authenticated IPC (November 7, 2026)

Release service/IPC foundation on .NET 10.0.14, SDK 10.0.403, Windows build 26300, TraceEvent 3.1.23. Native tests used an elevated NUnit testhost on the approved development box. No LocalSystem service was installed. The app does not invoke this component, so these are collector observations rather than a changed UI benchmark.

The original bounded 65,536-event queue lost 76,872 events in a UDP stress repeat, while ETW itself reported zero loss (`artifacts/network-service/native-test-65536-queue-loss.log`). This explained the byte shortfall and blocked acceptance of that implementation. The replacement queue is bounded at 262,144 compact records (about 6 MiB, versus about 1.5 MiB); this provides burst headroom for the requested 16 MiB ETW buffer budget. Reconciliation also avoids re-querying token ownership for unchanged process creation identities. Both native and internal queue loss remain explicitly reported.

Final report: `artifacts/network-service/native-collector-44979e68e939492c82f348f6df190ae2.json`. All ten TCP/UDP IPv4/IPv6 existing/short-lived/load scenarios matched exact cumulative worker totals, finishing at 7,794,327,552 bytes in each direction. Native ETW loss and service queue drops were zero; event-time rates became available after warmup. An external stop of the test's own trace correctly produced unavailable readings, and the trace was removed.

| Measurement | Observed |
| --- | ---: |
| Idle collector/testhost CPU without tracing, 2 seconds | 46.875 ms |
| Idle collector/testhost CPU with tracing, 2 seconds | 62.5 ms |
| Total collector/testhost CPU | 1,234.375 ms |
| Whole native experiment, including draining/cleanup | 28.59 s |
| Testhost private memory at completion | 87.18 MiB |

These are single-run, coarse CPU observations including NUnit/testhost/JIT/ownership work; they exclude workload CPU and kernel tracing buffers/provider cost. They do not establish a standalone service memory footprint, a numerical production budget, or an apples-to-apples speed ratio against the earlier prototype. The explicitly bounded queue memory increase is a measured correctness tradeoff. Global coverage is still marked incomplete when unrelated ownership probes fail; known process rates are provisional and are no longer continually suppressed by that uncertainty.

The [implementation record](design/PER-PROCESS-NETWORK-IMPLEMENTATION.md) contains the command results and remaining release gates. Normal and Store verification passed, including the service/native pipe tests; actual LocalSystem SCM, second-account, external-link and older-Windows acceptance remain open. No UI/hardware interaction was changed or validated by this increment.

## Chart collapse and aligned disclosure buttons (November 7, 2026)

Release UI benchmark on .NET 10.0.14, SDK 10.0.403. Four alternating pre-change/changed pairs exercised the existing expanded-layout scenarios. Values are medians of the four runs' seven-batch medians. The baseline preserves the workspace from before this change, including the logical-processor work already present. Native collection is disabled during measurement.

| Scenario | Before | After | Allocation before / after |
| --- | ---: | ---: | ---: |
| Live overlay | 4.35 ms | 4.34 ms | 586.73 / 586.81 KiB |
| Historical | 1.19 ms | 1.14 ms | 174.07 / 174.06 KiB |
| Services live | 5.34 ms | 5.35 ms | 980.14 / 980.16 KiB |
| Compact live | 1.64 ms | 1.60 ms | 362.33 / 362.38 KiB |
| Pinned/selected processes | 3.67 ms | 3.84 ms | 946.15 / 946.12 KiB |
| Logical processors, pinned/selected | 6.94 ms | 6.67 ms | 1114.05 / 1114.02 KiB |
| Connections grouped | 3.20 ms | 3.29 ms | 1457.74 / 1457.76 KiB |
| Connections Individual | 3.40 ms | 3.34 ms | 1778.63 / 1778.69 KiB |

No broad timing or allocation increase appeared. The pinned/selected median rose 4.6%, with overlapping run ranges (baseline 3.54–3.84 ms; changed 3.48–3.98 ms); grouped Connections rose 3.1%, also within overlapping ranges. These observations do not establish a feature-caused speedup or slowdown.

**Measurement limitation:** another verification rebuilt the shared changed-output directory between changed runs two and three. The baseline assembly stayed fixed; the changed runs span two assembly hashes, and concurrent work means the machine was not demonstrably quiet. Every rendered scenario nevertheless produced the same pixel hash across all four changed runs; Compact pixels also match the baseline. Full-view pixels intentionally differ from the baseline because of the new chevron and header spacing. This is an observational comparison, not a certified four-pair comparison against one frozen final assembly.

Raw results and renders: `artifacts/chart-collapse-performance/{baseline,changed}-{1,2,3,4}.{json,png}`, with all scenario medians in `summary.json`. Baseline assembly SHA-256: `5DB4065E7A17A098C6E2F75BAC5CA044E1E95702B69249F852927B5F18DA11E7`; changed runs 1–2: `216817DD5F80204845E2CBA495045F3134D8129FC968EA05AF3E00C29389F578`; changed runs 3–4: `9375D5C545C0DE43943FD5D3B48B2584AF37975F1231969B74481ECD8EBA8D00`.

Functional validation passed: `Verify.ps1 -Smoke feature,compact,snapshot` (1,005 tests and 272 renders), plus `Verify.ps1 -Store` (1,007 tests), with warning-free builds. Relevant Dark/Light, minimum-width, expanded, collapsed and snapshot screenshots were inspected. The feature smoke checks the two chevrons' right-edge alignment, focus recovery, independent panel states, saved split, Compact view and persisted restart behavior. Long-running usage and whole-app/native sampling overhead were not measured.

## ETW per-process network prototype (November 7, 2026)

Standalone Release prototype (`tools/WinMon.NetworkProbe`), TraceEvent 3.1.23, .NET 10.0.14, SDK 10.0.403, Windows build 26300. User-authorized native testing ran on the development box with an elevated token. No shipping app code references the prototype, and no Windows service was installed. These are controlled loopback observations, not a UI benchmark or production service acceptance result.

Final report: `artifacts/network-probe/elevated-drain-final.json`. All ten traffic scenarios matched exact worker payload send/receive totals. The session processed 1,788,011 network events with zero reported loss, zero positive-byte worker attribution rejections and zero sampled payload PID mismatches. It checked eight protocol/family/direction payload categories, counted 19,933 zero-byte events separately, and verified its trace session was removed.

| Scenario | Collector CPU | Elapsed, including event drain | Collector private memory |
| --- | ---: | ---: | ---: |
| Idle without tracing | 0 ms | 3.01 s | 9.81 MiB |
| Idle with tracing | 46.875 ms | 3.01 s | 16.34 MiB |
| TCP IPv4 load, 1,375,731,712 bytes each direction | 15.625 ms | 3.79 s | 15.25 MiB |
| TCP IPv6 load, 1,291,845,632 bytes each direction | 15.625 ms | 3.79 s | 15.26 MiB |
| UDP IPv4 load, 426,770,432 bytes each direction | 125 ms | 3.52 s | 15.74 MiB |
| UDP IPv6 load, 439,353,344 bytes each direction | 140.625 ms | 3.51 s | 15.87 MiB |

CPU/allocation measurements cover the collector process, not the worker or Windows kernel/provider overhead. Timer granularity, JIT warmup and unrelated background traffic affect these single-run readings. Active-phase allocations ranged from 0 to 206,064 bytes using the runtime's approximate process-wide counter. The collector requests a 16 MiB ETW buffer budget; kernel buffer cost is not included in private memory. No production overhead budget or full-system regression claim follows from this table.

A previous repeat (`artifacts/network-probe/elevated-final.json`) caught incomplete phase accounting with a fixed two-second drain: final aggregate bytes were correct, but some events arrived after phase snapshots, with zero reported loss. The final prototype waits against known workload byte targets, bounded at ten seconds. This is a test technique; production rates still need timestamp/delivery-aware quality semantics. Sustained load ran for three seconds per case; elapsed values include flushing/draining and must not be interpreted as link throughput.

Eight isolated ledger tests passed. `Verify.ps1` finished with five checks passed and all 1,005 existing tests passed; the benchmark build retried a DLL copy locked by a concurrent benchmark before succeeding. The [implementation checklist](design/PER-PROCESS-NETWORK-IMPLEMENTATION.md) records earlier failures, corrections and remaining native/release gates. External links, multiple users, old Windows versions, forced loss and privileged service installation were not validated.

## Connections display modes and summaries (November 7, 2026)

Release UI benchmark on .NET 10.0.14, SDK 10.0.403. Four alternating baseline/changed pairs used frozen binaries, with only the intended benchmark observed running. Values are medians of the four runs' seven-batch medians. Both builds include the integrated Info changes; the baseline predates this Connections layout/grouping increment. Native collection is disabled during measurement.

| Scenario | Before | After | Allocation before / after |
| --- | ---: | ---: | ---: |
| Live overlay | 3.96 ms | 4.09 ms | 586.75 / 586.77 KiB |
| Historical | 1.05 ms | 0.99 ms | 174.12 / 174.12 KiB |
| Services live | 4.83 ms | 4.74 ms | 980.19 / 980.19 KiB |
| Compact live | 1.48 ms | 1.49 ms | 362.43 / 362.43 KiB |
| Logical processors, pinned/selected | 5.83 ms | 5.32 ms | 1125.33 / 1133.30 KiB |
| Connections grouped, 60 processes / 480 endpoints, one expanded group | 2.34 ms | 2.77 ms | 829.74 / 1468.95 KiB |
| Connections Individual, all 480 endpoints | — | 2.80 ms | — / 1789.91 KiB |

The richer grouped view adds 0.43 ms (18.5%) and 639.22 KiB per complete refresh in this fixture. It now builds executable membership, distinct address/port and TCP-state summaries, and protocol-tagged endpoint tooltips instead of blank group endpoint/state cells. Individual mode materializes all 480 endpoint rows. Both scenarios retain the logical-processor chart and include the complete UI refresh. Grouped runs ranged 2.28–2.51 ms before and 2.69–2.84 ms after; Individual ranged 2.78–3.23 ms. This is a measured feature cost, not a claim of unchanged performance.

An initial implementation allocated 1,918,096.8 bytes per grouped refresh and 2,466,104.27 bytes in Individual mode. Reusing executable keys within each refresh and keeping endpoint keys independent of executable paths reduced those figures to 1,504,208.8 and 1,832,864.27 bytes. The simpler keys also preserve Individual selection when executable metadata arrives late. Other existing scenarios changed by −8.8% to +3.3% in median timing, with allocations unchanged or up by at most 8 KiB. Live overlay ranged 3.74–4.25 ms before and 3.65–4.40 ms after; timing reductions elsewhere should not be read as feature-caused speedups.

All eight runs produced the same earlier dashboard pixel hash. Connections Dark/Light, grouped/Individual and narrow renders were inspected separately. The isolated integrated snapshot passed 1,005 tests and the Connections smoke with zero build warnings/errors. Feature, search, usage and compact smoke modes passed immediately before the final internal key-allocation change (1,004 tests); the final unit tests also cover selection after metadata changes. Native TCP byte exchange and TCP/UDP owner/peer checks passed with `Elevated: false`; OS-reported TCP states remain evidence rather than a smoke-test assumption.

Raw reports: `artifacts/connections-layout/{baseline,changed}-{1..4}.json`, `comparison.json`, `initial-results/`, and `source/artifacts/verify/20261107-135958/connections/`. The five-mode verification is under `source/artifacts/verify/20261107-135004/`. Baseline assembly SHA-256: `8DFF131032FCF7C34D66687871CF667BD947261ADDB323758442956B125C849A`; changed: `276B8F5FDD3560804C9956B3E4A01462B38316FFF3BDFDA4E6AD1A48F916BF97`. UI measurements exclude native collection and the WPF render thread. Long-running real-world usage was not measured.

## Connections activity tab (November 7, 2026)

Release UI benchmark on .NET 10.0.14, SDK 10.0.403. Four alternating baseline/Connections pairs used preserved binaries. Values below are medians of each run's seven-batch medians. The new tab collects no endpoints while another tab is selected; the benchmark disables native collection throughout.

| Scenario | Before | After | Allocation before / after |
| --- | ---: | ---: | ---: |
| Live overlay | 4.51 ms | 4.15 ms | 582.93 / 586.79 KiB |
| Historical | 1.24 ms | 1.11 ms | 174.12 / 174.12 KiB |
| Services live | 6.28 ms | 6.48 ms | 979.04 / 980.19 KiB |
| Compact live | 1.86 ms | 1.63 ms | 362.43 / 362.43 KiB |
| Logical processors, pinned/selected | 6.58 ms | 5.93 ms | 1121.28 / 1129.14 KiB |
| Connections, 60 processes / 480 endpoints, one expanded group | — | 2.44 ms | — / 829.71 KiB |

There is no material timing regression across the existing scenarios. Services' 3.2% median increase is smaller than its changed-run variation (6.10–7.94 ms); lower timings elsewhere should not be read as a feature-caused speedup. Allocations rose by roughly 1–8 KiB in several rendered scenarios with the additional tab/view, while Historical and Compact allocations stayed unchanged. The new Connections scenario keeps the 12-logical-processor chart active and measures the complete UI refresh, not just endpoint row updates.

The separate native smoke read 646 endpoints across both IP families with `Elevated: false`. Its cold capture took 8.85 ms; five subsequent captures had a 2.59 ms median and a 6.05 ms maximum. This is wall time for IP Helper reads plus lifetime probes, not whole-app CPU or a guarantee for other hardware/process counts.

Each build produced one stable pixel hash across its four runs. The hashes differ between builds, as the activity header now contains Connections. Dark/Light/narrow Connections renders were inspected separately. The isolated feature snapshot passed 982 tests and five smoke modes. Concurrent Info/updater edits were excluded from the snapshot. Subsequent Escape-key handling, native-smoke assertions and preservation of nonzero peer addresses do not alter the measured UI refresh paths; they were verified separately in the integrated checkout (990 tests and five smoke modes).

Raw reports: `artifacts/connections/{baseline,changed}-{1..4}.json`, `comparison.json`, and `source/artifacts/verify/20261107-123049/connections/`. Baseline assembly SHA-256: `3CA0A8108452181E4F9F8766BE56D4D6E25C7A2BCBD43A23DE7A7782C115C3BC`; measured Connections assembly: `C9A607B4400555B35BDADCC7C4DE42C6D5459B07577AD25B43B16D620594C725`. UI measurements exclude native collection and the WPF render thread.

## Tabbed Info (November 7, 2026)

Release UI benchmark on .NET 10.0.14, SDK 10.0.403. Four alternating baseline/changed pairs used preserved binaries. Values below are medians of the four reports' seven-batch medians. The baseline preserves the pending working tree before the Info changes, rather than a clean `main`. Info remains closed during these dashboard scenarios; its hardware collection and expandable rows are on demand, outside sampling.

| Scenario | Before | After | Allocation before / after |
| --- | ---: | ---: | ---: |
| Live overlay | 4.16 ms | 4.06 ms | 586.74 / 586.75 KiB |
| Small multiples | 2.76 ms | 2.83 ms | 558.35 / 558.35 KiB |
| Focus | 2.92 ms | 2.83 ms | 572.65 / 572.65 KiB |
| Historical | 1.02 ms | 1.01 ms | 174.10 / 174.10 KiB |
| Services live | 6.00 ms | 5.77 ms | 980.19 / 980.24 KiB |
| Compact live | 1.64 ms | 1.51 ms | 362.38 / 362.38 KiB |

Core allocations are unchanged or nearly identical. Ten of thirteen rendered scenarios produced one pixel hash across all eight runs. Compact and logical-processors scenarios each had two baseline variants; their single changed-build hash also appeared in the baseline. The Connections scenario had different changed-build pixels and 680.73 / 829.71 KiB allocated per frame. Connections source and fixtures were being edited independently between the preserved builds, so that comparison does not isolate the Info change and cannot establish its cause.

Timing is **inconclusive under concurrent activity**: a separate benchmark was observed running from `artifacts/connections/source/artifacts/verify/build/` during the early pairs. The Info change does not add work to sampling or frame refresh. A quiet-machine timing conclusion and an isolated Connections comparison remain unverified.

The final saved standard build passed `Verify.ps1 -Smoke feature` (989 tests); the Store build passed `Verify.ps1 -Store -Smoke feature` (991 tests). Both produced 106 feature renders, with zero build warnings/errors. Info tabs, all five Guide topics, expanded hardware details, Dark/Light styling, minimum-size scrolling and the Store-managed About state were visually inspected. Update actions were exercised with callbacks; a real signed download/restart was not performed.

Raw reports: `artifacts/info-perf/{baseline,changed}-{1..4}.json`, `comparison.json` and `Compare.ps1`. Baseline assembly SHA-256: `657B1D00A86BA1697A4E475B0C41765F14AF86231893AA22CEEA549DABEB9527`; changed: `0F302E5AF7B00738D74E5A2BDCC4B6502403A456D53BE6D027DA06C7486B7E52`. These checks certify the saved binaries; unrelated pending edits were preserved.

## Full-width section headers (November 7, 2026)

Release UI benchmark on .NET 10.0.14, SDK 10.0.403. Four alternating baseline/header-fix pairs used preserved binaries. Values are medians of the four runs' seven-batch medians. The patch replaces negative-margin header overflow with a full-width main grid and activity-view gutter columns.

| Scenario | Before | After | Allocation before / after |
| --- | ---: | ---: | ---: |
| Live overlay | 4.15 ms | 4.36 ms | 586.66 / 584.98 KiB |
| Small multiples | 3.14 ms | 3.00 ms | 558.31 / 558.31 KiB |
| Focus | 3.24 ms | 3.10 ms | 572.61 / 572.61 KiB |
| Historical | 1.19 ms | 1.25 ms | 174.06 / 174.06 KiB |
| Services live | 6.09 ms | 7.59 ms | 979.01 / 979.01 KiB |
| Compact live | 1.75 ms | 2.19 ms | 375.89 / 375.89 KiB |

Timing is **inconclusive under concurrent activity**, rather than evidence of a change-specific regression. Another WinMon build/benchmark was observed running from `artifacts/logical-processors/baseline/` while the later measurements completed. Services ranged 5.52–17.79 ms before and 5.91–13.01 ms after; Compact, whose layout this patch does not change, ranged 1.70–3.46 ms before and 1.68–3.36 ms after. The first two pairs' Compact times were 1.75/1.80 and 1.70/1.68 ms. A quiet-machine timing conclusion remains unverified.

All eight runs produced the same pixel hash, and allocations were unchanged or slightly lower. The saved header-fix build passed `Verify.ps1 -Smoke feature,search,services,usage,compact`: 952 tests and all five smoke modes passed, with zero build warnings/errors. Header captures at 900, 720 and 600 DIPs high across all three tabs, plus representative narrow, translated and collapsed captures, were visually inspected. Unrelated source edits appeared during performance measurement, so these checks certify the saved header-fix build, not the subsequent combined working tree.

Raw reports: `artifacts/perf-headers/{baseline,changed}-{1..4}.json` and `comparison.json`. Baseline assembly SHA-256: `84392602F3073A45EEAFCC462B3AF49F1BD033AA3B139374874233C1994309D6`; header fix: `612FBA8A8E89F25F29BCBCAF733A4683D2F7FEA7F932ACED0951CD3480C5C4E7`. This benchmark measures UI-thread work and excludes native collection and the WPF render thread.

## Logical processor charts (November 7, 2026)

Release builds on the development machine (12 logical processors), SDK 10.0.403 and runtime 10.0.14. Four baseline/final UI benchmark pairs ran alternately from preserved output folders. Values below are medians of the four runs' seven-batch medians; for four values, the middle two are averaged. The baseline captured the pre-feature working tree while other chart/header work was underway, and the final includes that work's completed changes, so visual differences are expected and the comparison is not a perfectly isolated patch measurement.

| Scenario | Before | After | Allocation before / after |
| --- | ---: | ---: | ---: |
| Live overlay | 4.00 ms | 4.26 ms | 586.72 / 586.74 KiB |
| Small multiples | 2.88 ms | 2.84 ms | 558.34 / 558.34 KiB |
| Heatmap | 3.11 ms | 2.93 ms | 602.66 / 602.66 KiB |
| Focus | 2.88 ms | 2.96 ms | 572.64 / 572.64 KiB |
| Historical | 1.16 ms | 1.16 ms | 174.09 / 174.09 KiB |
| Services live | 5.38 ms | 5.43 ms | 979.01 / 979.04 KiB |
| Compact | 1.85 ms | 1.69 ms | 375.89 / 375.92 KiB |
| Pinned and selected | 3.84 ms | 3.69 ms | 968.22 / 968.22 KiB |
| Logical processors, pinned and selected (12 CPUs) | — | 6.03 ms | — / 1,142.94 KiB |

The first live scenario increased by 0.26 ms with normal tiering. A diagnostic baseline/final pair with `DOTNET_TieredCompilation=0` did not reproduce it: live was 4.77/4.72 ms and Focus was 4.71/4.72 ms. This points to the documented JIT/timing sensitivity rather than a reproducible existing-view regression. Allocation is effectively unchanged in existing paths. The new grid adds about 2.34 ms and 174.72 KiB to the pinned/selected workload compared with its ordinary overlay. WinMon refreshes once per second. Each build produced one consistent pixel hash across its four ordinary runs; baseline and final hashes differ with the chart selector/layout changes.

The 60-minute history workload retained 1,961 samples / 499,896 process entries in every run. Update save/read medians were 1,102/783 ms before and 1,102/767 ms after; normal save/read were 1,092/675 ms before and 1,103/680 ms after. Format 3 adds 12 nullable processor readings per synthetic sample: the update file grew from 15,730,541 to 15,845,107 bytes and normal history from 15,718,939 to 15,833,505 bytes. These runs restored all samples within the existing save budget.

Whole-process profiles used the default Overlay view and normal tiering, measuring 60 seconds after 20 seconds of warm-up. Baseline and final ran together; the second pair reversed launch order. CPU milliseconds per second were baseline/final 39.13/43.77 in pair one and 39.71/38.41 in pair two. Private memory averages were 65.16/66.02 MiB and 67.87/67.75 MiB; allocation was 926.23/920.18 KiB/s and 932.40/937.65 KiB/s. The CPU differences changed direction and memory/allocation stayed close, so these short runs do not establish a consistent collector regression. They do not certify long-running memory behavior or physical 64-processor collection; the 64-processor grid was checked with synthetic rendering data.

Raw evidence: `artifacts/logical-processors/performance/baseline-{1..4}.json`, `final-{1..4}.json`, `baseline-tiered-off.json`, `final-tiered-off.json`, `parent-comparison.json` and `profile-pair{1,2}-{baseline,final}/profile.json`. Baseline assembly SHA-256: `CFC7BAFF069052CC598DEFD71947C5A4387D3EFE8E54698602740016AE087A67`; final: `810C59FC20C219F73995E299010A352CCF616A0A7065EA3D200938287E792800`.

## Collapsible activity panel (November 7, 2026)

Release UI benchmark on .NET 10.0.14, built with SDK 10.0.403. Four baseline/final pairs alternated after verification completed. The baseline is a saved artifact of the pre-feature working tree, including the existing unrelated edits, so the comparison isolates this feature. Each value is the median of four runs' seven-batch medians.

| Scenario | Before | After | Allocation before / after |
| --- | ---: | ---: | ---: |
| Live overlay | 3.82 ms | 3.96 ms | 586.58 / 586.71 KiB |
| Small multiples | 2.53 ms | 2.57 ms | 558.31 / 558.34 KiB |
| Focus | 2.60 ms | 2.76 ms | 572.61 / 572.64 KiB |
| Historical | 1.01 ms | 1.02 ms | 174.06 / 174.09 KiB |
| Services live | 5.49 ms | 5.11 ms | 979.01 / 979.01 KiB |
| Compact live | 1.58 ms | 1.53 ms | 362.39 / 362.39 KiB |

Live and Focus aggregate times increased by about 0.15 ms with the additional disclosure button/body layout. Timing variation is larger than that difference: Live ranged 3.73–4.16 ms before and 3.38–4.45 ms after; Focus ranged 2.48–2.78 ms before and 2.55–3.12 ms after. Live was faster in the first two changed runs and slower in the last two. Other paths were stable or faster in aggregate, so these runs do not establish a consistent material UI-time regression. Typical expanded paths add approximately 32 bytes per refresh; the Live median difference was 0.13 KiB, and Services/Compact allocation stayed unchanged.

Each build produced one consistent pixel hash across all four runs. Before/after hashes differ as expected because the tab strip gains a button and four DIPs of height. This benchmark measures UI-thread refresh/layout/render submission in the expanded view and existing scenarios; it does not measure native sampling, the WPF render thread or a dedicated collapsed-panel workload. The collapsed layout was checked separately by the feature smoke.

Raw reports: `artifacts/perf-activity-panel/baseline-quiet-{1..4}.json`, `verified-quiet-{1..4}.json` and `comparison.json`. Baseline assembly SHA-256: `77769A8E410008E4F88D40E0563853AE5AC829AEE0E332F61B87A4ED9BBFFB96`; final: `8ACA48A49FA167FFCA55D85894F6D62A72A6CB3B2B05705C70B3FDF7742E47B0`.

Add a section at the top for each measured change: what changed, the machine and build, before and after results, and where the raw reports are.

## Process file versions (November 7, 2026)

Release benchmark on the development machine (12 logical processors), .NET runtime 10.0.14. Compared the clean baseline at `0ed58ae` with the version-field change using preserved binaries. Four alternating pairs use `artifacts/version-baseline-{1,2,4,5}.json` and `artifacts/version-changed-{1,2,3,4}.json`. UI values are medians of the four runs' seven-batch medians; history values use each run's five-save/read medians. Baseline WinMon assembly SHA-256: `EC8BB72FFAA023792054352DE0AB1DA70821887F108361A19ECFCC18F7335622`; changed: `77769A8E410008E4F88D40E0563853AE5AC829AEE0E332F61B87A4ED9BBFFB96`.

| Scenario | Before | After | Allocation before / after |
| --- | ---: | ---: | ---: |
| Live overlay | 4.50 ms | 4.11 ms | 584.9 / 583.3 KiB |
| Small multiples | 3.29 ms | 2.86 ms | 556.4 / 558.3 KiB |
| Heatmap | 3.21 ms | 2.92 ms | 600.8 / 602.7 KiB |
| Focused chart | 3.02 ms | 2.86 ms | 570.7 / 572.6 KiB |
| Historical | 1.21 ms | 0.99 ms | 174.1 / 174.1 KiB |
| Grouped | 6.57 ms | 6.06 ms | 576.7 / 578.6 KiB |
| Services | 6.01 ms | 5.94 ms | 979.0 / 979.0 KiB |
| Compact | 1.66 ms | 1.65 ms | 362.4 / 362.4 KiB |
| Pinned and selected | 4.18 ms | 3.68 ms | 931.0 / 946.1 KiB |
| Pinned and selected group | 6.88 ms | 7.08 ms | 749.7 / 753.5 KiB |
| Group with CPU tooltip | 10.44 ms | 9.99 ms | 892.9 / 898.5 KiB |
| Usage ledger add | 0.0178 ms | 0.0168 ms | 0.0 / 0.0 KiB |

All scenario pixel hashes matched across these eight reports. The extra two string references explain the added process-copy allocation (roughly 1.9 KiB/frame in ordinary chart modes and 15.2 KiB with pinned/selected processes). Retaining 500,000 process entries adds about 7.6 MiB of object fields, plus cached version strings. This benchmark does not measure native metadata collection; the fields reuse the existing file-version read and path cache.

At the retained 60-minute workload (1,961 samples / 499,896 process entries), update-history median save/read changed from 1,065/792 ms to 1,106/824 ms; normal-history save/read changed from 1,015/628 ms to 1,103/668 ms. Full saves grew from about 15.27 to 15.73 MB (update) and 15.26 to 15.72 MB (normal), as expected from the additional version indices. Both remain within the 1.5-second overall budget on these median measurements. The existing 1.2-second chunk deadline still trims oldest history under load: one changed update report restored 1,680 samples, and one baseline normal report restored 1,200; the other selected reports restored all 1,961. Do not interpret these measurements as a guarantee of full saves on every machine.

Concurrent activity-panel work replaced the shared verification output during the original comparison. The affected third changed report was discarded and rerun from `artifacts/version-changed-frozen`, a preserved copy of the verified version-only build; the fourth changed run also uses that copy. Hashes above were checked for every selected report. Background work means timing differences should not be read as speed improvements. Verification of the version-field build passed 939 tests and feature smoke (68 renders); its dark/light Properties PNGs were inspected.

## Activity tab counts (October 26, 2026)

Release UI benchmark on the development machine (12 logical processors), comparing `6e0be78` with the tab-specific count working tree on .NET 10. Four baseline and four changed runs alternated; values are medians of each run's seven-batch median, then the median of four runs.

| Scenario | Before | After | Allocation before / after |
| --- | ---: | ---: | ---: |
| Live overlay | 3.68 ms | 3.71 ms | 584.8 / 584.9 KiB |
| Services live | 4.62 ms | 4.51 ms | 975.2 / 979.0 KiB |
| Compact live | 1.43 ms | 1.42 ms | 362.4 / 362.4 KiB |

The Services pixel hash changes as expected from the new visible count summary; Live and Compact hashes match. The 3.8 KiB per Services frame allocation increase is the cost of rendering the additional text, under 0.4% of its baseline allocation. Counts and exact tooltips are cached until a value or language changes. UI time did not regress. Raw local reports: `artifacts/perf-activity-counts/baseline-final-1.json` through `baseline-final-4.json` and matching `changed-final-*` reports.

## Startup view and network interface settings (October 26, 2026)

Release UI benchmark on the development machine (12 logical processors), comparing `f10437f` with the Settings and network-selection working tree on .NET 10. Four baseline and four changed runs alternated, followed by one reverse-order pair to examine timing variation. Values are medians of each run's seven-batch median, then the median of four runs.

| Scenario | Before | After | Allocation before / after |
| --- | ---: | ---: | ---: |
| Live overlay | 3.87 ms | 4.00 ms | 536.2 / 534.7 KiB |
| Small multiples | 3.05 ms | 3.12 ms | 507.5 / 507.5 KiB |
| Services live | 6.48 ms | 6.63 ms | 950.6 / 950.6 KiB |
| Compact live | 1.58 ms | 1.67 ms | 362.4 / 362.4 KiB |

All regular-view pixel hashes match. The open-tooltip hash has two variants across runs, consistent with the known pointer-sensitive capture. Timings varied across both builds (baseline Live overlay 3.63–4.42 ms, changed 3.80–5.10 ms); in the reverse pair, the changed build was faster in Small multiples, Services and Compact but slower in Live and Grouped. Allocation is effectively unchanged and there is no consistent UI regression. This UI benchmark stops real sampling, so it does not measure the adapter-selection code in the Windows sampler. Raw local reports: `artifacts/perf-startup-network/baseline-1.json` through `baseline-4.json`, matching `changed-*` reports, and `baseline-reverse.json` / `changed-reverse.json`.

## Memory gauge peak label (October 26, 2026)

Release UI benchmark on the development machine (12 logical processors), comparing `5117bba` with the Memory corner-label working tree on .NET 10. Four baseline and four changed runs alternated after the first baseline run. Values are medians of each run's seven-batch median, then the median of four runs. The Memory label visibly changed, so pixel hashes differ as expected.

| Scenario | Before | After | Allocation before / after |
| --- | ---: | ---: | ---: |
| Live overlay | 5.50 ms | 5.58 ms | 536.4 / 536.2 KiB |
| Small multiples | 3.02 ms | 3.04 ms | 507.4 / 507.4 KiB |
| Services live | 6.12 ms | 6.24 ms | 950.5 / 950.5 KiB |
| Compact live | 1.65 ms | 1.67 ms | 362.3 / 362.3 KiB |

The timing differences are smaller than the run-to-run spread (Live overlay ranged 4.87–5.77 ms before and 5.00–5.99 ms after). No material allocation or UI-time regression was observed. Raw local reports: `artifacts/perf-memory-peak/baseline-1.json` through `baseline-4.json` and `changed-1.json` through `changed-4.json`.

## Gauge corner labels (October 26, 2026)

Release UI benchmark on the development machine (12 logical processors), comparing `c166839` with the gauge corner-label working tree on .NET 10. Four baseline and four changed runs alternated after the first baseline run; values are medians of each run's seven-batch median, then the median of four runs. The new labels are visible in the benchmark renders, so pixel hashes differ as expected.

| Scenario | Before | After | Allocation before / after |
| --- | ---: | ---: | ---: |
| Live overlay | 5.17 ms | 5.06 ms | 535.5 / 536.4 KiB |
| Small multiples | 3.28 ms | 3.57 ms | 506.7 / 507.4 KiB |
| Services live | 7.28 ms | 7.46 ms | 949.8 / 950.5 KiB |
| Compact live | 1.89 ms | 1.83 ms | 361.6 / 362.3 KiB |

Per-run timing ranges overlap; neither Small multiples nor Services increased consistently in the paired runs. The additional 0.7–0.9 KiB per frame is the expected formatting and text update for the corner labels, under 1 KiB per second at the normal 1 Hz refresh. The other UI scenarios likewise showed no consistent time regression. Raw local reports: `artifacts/perf-gauge-corners/baseline-1.json` through `baseline-4.json` and `changed-1.json` through `changed-4.json`.

## Portable process identity on macOS — October 25, 2026

Measured MonCore 3.4.1 against the portable identity changes on Apple Silicon/macOS 26.6 with .NET SDK 10.0.401. The repeatable harness is in the sibling MacMon checkout at `tools/MonCore.Performance`: 850 processes, six observations, 1,000 ranking warmups, seven batches of 500 operations; medians below. Raw local reports are in MacMon's `artifacts/core-benchmark-before.json` and `artifacts/core-benchmark-after.json`.

| Operation | Before | After | Allocation before / after |
| --- | ---: | ---: | ---: |
| Grouped ranking | 165.4 µs | 165.8 µs | 32,416 / 35,652 bytes |
| Usage Add | 13.7 µs | 12.1 µs | 32 / 32 bytes |

The identity retains the old 16-byte key size; in-place dictionary updates offset the extra domain/availability checks. Extra ranking allocation is the portable metadata on cloned result rows, about 3.2 KiB per refresh. The measured ranking difference is under 0.3%; usage time improved. This is a core microbenchmark, not the WPF UI/pixel benchmark. Windows `Verify.ps1`, smoke, Store and full paired UI/performance validation remain outstanding. The package remains a development prerelease until that validation.

## Shared monitoring assembly extraction (October 25, 2026)

Release UI benchmark on the development machine (12 logical processors), comparing the pre-extraction 3.4.1 build with the MonCore/MonCore.Windows working tree. Four baseline and four changed runs alternated launch order on the same machine. Values are medians of each run's seven-batch median, then the median of four runs. Every rendered pixel hash matched across paired runs.

| Scenario | Before | After | Allocation before / after |
| --- | ---: | ---: | ---: |
| Live overlay | 5.19 ms | 5.02 ms | 530.6 / 530.6 KiB |
| Small multiples | 2.49 ms | 2.47 ms | 501.8 / 502.2 KiB |
| Heatmap | 2.44 ms | 2.33 ms | 544.9 / 544.9 KiB |
| Focus | 2.57 ms | 2.44 ms | 514.7 / 514.7 KiB |
| Grouped live | 4.34 ms | 4.05 ms | 542.9 / 542.9 KiB |
| Services live | 4.85 ms | 4.71 ms | 949.8 / 949.8 KiB |
| Compact live | 1.59 ms | 1.57 ms | 361.6 / 361.6 KiB |
| Pinned and selected, grouped | 5.25 ms | 5.21 ms | 708.0 / 708.0 KiB |
| Grouped with CPU tooltip | 7.09 ms | 7.00 ms | 844.6 / 844.6 KiB |

The other timed UI scenarios also showed no regression. Restart-history save means were 919.0 and 922.3 ms before and after; normal-history saves were 925.8 and 913.1 ms. Read means stayed below 0.7 ms. Both builds produced the same benchmark history outcomes. This benchmark exercises synthetic UI and persistence paths; the live sampler was covered by tests and smoke checks, not by this timing comparison. Raw reports: `artifacts/perf-shared/baseline-1.json` through `baseline-4.json` and `changed-1.json` through `changed-4.json`.

## Bug-hunt fixes (October 25, 2026)

Release UI benchmark on the development machine (12 logical processors), comparing `47a9fbb` with the working tree that fixed the capture-rule processor count, local sample times, the update-restart clock offset, the focus crash, heatmap ordering, icon cache eviction, export and history file handling, stalled update downloads and locked settings. Four baseline and four changed runs alternated launch order. With default tiering the first live scenario was bimodal in both builds (3.2–4.1 ms before, 3.9–5.0 ms after) although its render path is unchanged, so four more pairs ran with `DOTNET_TieredCompilation=0`; values below are medians of those run medians.

| Scenario | Before | After | Allocation before/after |
| --- | ---: | ---: | ---: |
| Live overlay | 3.54 ms | 3.54 ms | 530.5 KiB |
| Small multiples | 3.67 ms | 3.70 ms | 510.4 KiB |
| Heatmap | 3.78 ms | 3.75 ms | 553.4 KiB |
| Focus | 3.66 ms | 3.62 ms | 523.3 KiB |
| Historical | 1.44 ms | 1.45 ms | 173.7 KiB |
| Grouped live | 5.80 ms | 5.82 ms | 551.1 KiB |
| Services | 6.73 ms | 6.55 ms | 1039.7 KiB |
| Compact live | 2.21 ms | 2.24 ms | 362.0 KiB |
| Pinned and selected | 4.43 ms | 4.48 ms | 846.7 KiB |
| Pinned and selected, grouped | 6.89 ms | 6.57 ms | 717.2 KiB |
| Same grouped view, CPU tooltip open | 8.95 ms | 8.91 ms | 854.0 KiB |

Allocations were identical and every pixel hash matched. History saves were unchanged (1.23 s before and after with no tiering); the reads were about 5% slower while restoring about 4% more samples from their time-bounded saves.

Four side-by-side 90-second whole-app profiles, swapping launch order, gave median CPU of 25.5 ms/s before and 26.7 ms/s after. The changed build was higher in the first two pairs and lower in the last two, within a spread of 23.8–28.0 ms/s. Private bytes (59.7 and 59.9 MiB), allocation rate (707 and 708 KiB/s), gen0 collections and retained process entries matched. No repeatable regression was measured. Raw reports: `artifacts/perf-bughunt/baseline-*.json`, `changed-*.json`, their `-notier-` counterparts, and `artifacts/perf-bughunt/profile/`.

## Normal-restart history (October 25, 2026)

Release UI benchmark on the development machine, comparing base commit `27700cb` with the normal-restart-history worktree. The paired run with `DOTNET_TieredCompilation=0` gave these medians; allocations were unchanged to the displayed precision. The default live-frame benchmark calls `MonitorSession.Record` directly, so it does not measure the extra timestamp copies made by live drains after a reboot.

| Scenario | Before | After | Allocation before/after |
| --- | ---: | ---: | ---: |
| Live overlay | 3.76 ms | 3.76 ms | 530.5 KiB |
| Small multiples | 4.16 ms | 3.87 ms | 510.4 KiB |
| Heatmap | 3.96 ms | 4.12 ms | 553.4 KiB |
| Focus | 4.00 ms | 4.00 ms | 523.3 KiB |
| Grouped live | 6.66 ms | 6.45 ms | 551.1 KiB |
| Services | 7.30 ms | 7.08 ms | 1039.7 KiB |
| Compact live | 2.30 ms | 2.37 ms | 362.0 KiB |

At the 500,000-process-entry budget, the new normal snapshot saved and restored all 1,961 samples in a 15.3 MB file with default JIT settings. Median save was 0.95 s and read was 0.63 s across five runs. In the same run, the existing update snapshot took 1.02 s and 0.75 s. The no-tiering run took 1.22 s to save and kept the newest 1,740 samples in a 13.5 MB file; this is the designed time-bound truncation. The paired no-tiering update save was 1.22 s before and 1.24 s after. No repeatable default-path allocation increase was measured. Raw reports: `artifacts/normal-history-before-notier.json`, `normal-history-after-notier.json`, and `normal-history-bench-final.json`.

## Separate chart and readout palette shades (October 25, 2026)

Release UI-thread benchmark on the development machine, comparing `18c35e0` with the working-tree build that added chart-only resource brushes and direct chart brush lookup. Four baseline and four changed runs alternated. The first live scenario varied widely with default JIT tiering, so four more pairs ran with `DOTNET_TieredCompilation=0` as the developer guide recommends. Values below are medians of those no-tiering run medians. These binaries were built before later concurrent palette edits in the checkout.

| Scenario | Before | After | Allocation before | Allocation after |
| --- | ---: | ---: | ---: | ---: |
| Live overlay | 3.73 ms | 3.78 ms | 530.8 KiB | 530.5 KiB |
| Small multiples | 3.96 ms | 3.89 ms | 510.8 KiB | 510.4 KiB |
| Heatmap | 4.19 ms | 3.93 ms | 553.8 KiB | 553.4 KiB |
| Focus | 3.94 ms | 3.86 ms | 523.6 KiB | 523.3 KiB |
| Historical | 1.55 ms | 1.55 ms | 174.0 KiB | 173.7 KiB |
| Grouped live | 6.66 ms | 6.81 ms | 551.5 KiB | 551.1 KiB |
| Compact live | 2.26 ms | 2.28 ms | 362.4 KiB | 362.0 KiB |

Other UI scenarios moved by at most 2.5% with no-tiering, apart from the faster heatmap; allocation fell slightly throughout. No repeatable UI-thread regression was measured. Pixel hashes changed in rendered scenarios as expected from the chart colors. Raw reports: `artifacts/perf-palette/baseline-no-tier-1.json` through `-4.json` and `optimized-no-tier-1.json` through `-4.json`; default-tiering comparisons are in the same folder as `baseline-optimized-*.json` and `optimized-*.json`.

## Translucent appearance themes (October 25, 2026)

Release UI-thread benchmark on the development machine, comparing `21fddde` with the translucent-theme working tree. Four baseline and four changed runs alternated launch order; values are medians of the four run medians. The benchmark used the default Dark theme, so it measures whether adding backdrop support affects the normal sampling presentation path. It does not measure desktop compositor cost while an Acrylic or Mica theme is active.

| Scenario | Before | After | Allocation before | Allocation after |
| --- | ---: | ---: | ---: | ---: |
| Live overlay | 4.58 ms | 4.81 ms | 531.0 KiB | 531.0 KiB |
| Small multiples | 2.80 ms | 3.06 ms | 502.3 KiB | 502.2 KiB |
| Heatmap | 2.80 ms | 3.08 ms | 545.3 KiB | 545.3 KiB |
| Focus | 3.29 ms | 2.86 ms | 515.2 KiB | 515.1 KiB |
| Historical | 1.26 ms | 1.44 ms | 173.6 KiB | 173.5 KiB |
| Grouped live | 5.31 ms | 5.77 ms | 543.4 KiB | 543.3 KiB |
| Services | 6.71 ms | 6.01 ms | 950.2 KiB | 950.2 KiB |
| Compact live | 2.00 ms | 1.78 ms | 362.0 KiB | 362.0 KiB |
| Pinned and selected | 3.92 ms | 3.81 ms | 838.2 KiB | 838.1 KiB |
| Pinned and selected, grouped | 6.91 ms | 6.47 ms | 708.5 KiB | 708.4 KiB |
| Same grouped view, CPU tooltip open | 8.97 ms | 9.27 ms | 847.3 KiB | 847.2 KiB |

The timings moved in both directions by up to 15%, including scenarios whose render path is unchanged. Live and grouped results also reversed direction with launch order. Allocation stayed within 0.1 KiB per frame. All pixel hashes matched except the tooltip scenario, which alternated between the same two hashes across both builds. There is no repeatable UI-thread regression in these runs; active-backdrop compositor cost remains unmeasured.

Raw reports: `artifacts/translucence-benchmark/base-1.json` through `base-4.json` and `changed-1.json` through `changed-4.json`.

## Action drawer and activity counts (October 25, 2026)

Release UI-thread benchmark on the development machine, comparing the 3.2.0 compact gauge build (the code of `16f9d77`) with the action drawer and tab-strip counts (the code of `c4b0125` and `2070796`). The machine was not quiet: another agent was building and editing in the same checkout throughout. Each value is the median of four run medians.

Pass 1 alternated baseline first. Pass 2 put the changed build first, and that build also clipped and transformed the bar only while it slid, to test whether the drawer's steady clip cost anything; that variant was not kept.

| Scenario | Pass 1 before | Pass 1 after | Pass 2 before | Pass 2 after | Allocation before | Allocation after |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Live overlay | 4.25 ms | 4.39 ms | 4.31 ms | 4.32 ms | 531.0 KiB | 531.0 KiB |
| Small multiples | 2.78 ms | 2.60 ms | 2.59 ms | 2.79 ms | 502.3 KiB | 502.3 KiB |
| Heatmap | 2.64 ms | 2.75 ms | 2.69 ms | 2.86 ms | 545.3 KiB | 545.3 KiB |
| Focus | 2.79 ms | 2.67 ms | 2.63 ms | 3.14 ms | 515.2 KiB | 515.2 KiB |
| Historical | 1.24 ms | 1.22 ms | 1.18 ms | 1.43 ms | 173.6 KiB | 173.6 KiB |
| Grouped live | 5.75 ms | 5.56 ms | 5.97 ms | 6.30 ms | 543.4 KiB | 543.4 KiB |
| Services | 5.66 ms | 5.34 ms | 6.39 ms | 5.64 ms | 1,013.2 KiB | 950.2 KiB |
| Compact live | 1.66 ms | 1.59 ms | 1.68 ms | 1.71 ms | 362.0 KiB | 362.0 KiB |
| Pinned and selected | 3.48 ms | 3.63 ms | 3.66 ms | 3.82 ms | 838.2 KiB | 838.2 KiB |
| Pinned and selected, grouped | 7.00 ms | 7.45 ms | 6.64 ms | 7.01 ms | 708.4 KiB | 708.4 KiB |
| Same grouped view, CPU tooltip open | 9.93 ms | 10.00 ms | 9.62 ms | 10.38 ms | 845.5 KiB | 847.3 KiB |

- Pixel hashes changed in every Full-view scenario, as intended: the tab strip shows the counts and each action bar ends with the chevron. The compact hash matched.
- Services allocates 63 KiB less per refresh in every run of both passes. The drawer adds a 6-DIP gap above the Services bar, which had none, so the list most likely realizes one fewer row. This was not confirmed by counting rows.
- **Unresolved:** grouped selection with pinned rows was about 0.4 ms (6%) slower after the change in both passes and in seven of eight pairs. Other scenarios, including ones the drawer cannot affect such as Historical, moved by up to 20% between passes, so this run cannot separate the difference from machine noise. Steady clipping was ruled out by pass 2. Rerun on a quiet machine before relying on either reading.

Raw reports: `artifacts/drawer-bench/base-1.json` through `-4.json` and `changed-*.json` (pass 1), and `base2-*.json` and `rest-*.json` (pass 2).

## Compact gauge view (October 25, 2026)

Release UI-thread benchmark on the development machine, comparing `fda5af2` with the compact gauge working tree. Four baseline and four changed runs alternated; each value is the median of the four run medians. Compact mode now renders `MetricReadout` gauges instead of filled `CpuGraph` tiles. The compact pixel hash changed as intended; every noncompact scenario's hash matched.

| Scenario | Before | After | Allocation before | Allocation after |
| --- | ---: | ---: | ---: | ---: |
| Live overlay | 4.41 ms | 5.73 ms | 529.1 KiB | 531.0 KiB |
| Small multiples | 3.26 ms | 3.30 ms | 502.3 KiB | 502.3 KiB |
| Heatmap | 3.11 ms | 3.17 ms | 545.3 KiB | 545.3 KiB |
| Focus | 3.12 ms | 3.32 ms | 515.2 KiB | 515.1 KiB |
| Historical | 1.60 ms | 1.54 ms | 173.6 KiB | 173.5 KiB |
| Grouped live | 6.49 ms | 6.54 ms | 543.4 KiB | 543.3 KiB |
| Services | 6.76 ms | 7.05 ms | 1,013.2 KiB | 1,013.2 KiB |
| Compact live | 2.02 ms | 1.99 ms | 338.5 KiB | 362.0 KiB |
| Pinned and selected | 4.23 ms | 3.94 ms | 840.1 KiB | 838.1 KiB |
| Pinned and selected, grouped | 7.80 ms | 7.75 ms | 709.8 KiB | 708.4 KiB |
| Same grouped view, CPU tooltip open | 11.27 ms | 10.77 ms | 843.6 KiB | 845.5 KiB |

Compact time stayed level while allocation rose 23.5 KiB per refresh from the new gauge readouts. The first live scenario alone had a larger time difference, though its pixels and allocation were stable. As the developer guide recommends for that JIT-sensitive scenario, two more alternating pairs ran with tiered compilation disabled. Their live results were 4.80 → 5.36 and 5.29 → 5.98 ms, but unrelated scenarios also drifted widely in those runs. One more default-tiering pair reversed the order: changed ran first at 4.56 ms, then baseline at 5.54 ms. The first-scenario timing follows run conditions rather than a consistent code difference. Raw reports: `artifacts/compact-baseline-final-1.json` through `-4.json`, `artifacts/compact-changed-final-1.json` through `-4.json`, corresponding `*-notier-1.json` and `*-notier-2.json` files, and `*-reverse-1.json`.

## 3.1.0 baseline (October 25, 2026)

A reference measurement of the 3.1.0 release build (`544e2ba`) on the development machine, now about 640 processes and 8,000 threads, with no code change and no side-by-side baseline. UI-thread benchmark, four consecutive runs; each value is the median of the four run medians:

| Scenario | Per refresh | Allocation |
| --- | ---: | ---: |
| Live overlay | 4.10 ms | 531.0 KiB |
| Small multiples | 2.54 ms | 502.3 KiB |
| Heatmap | 2.73 ms | 545.3 KiB |
| Focus | 2.84 ms | 515.2 KiB |
| Historical | 1.19 ms | 173.6 KiB |
| Grouped live | 5.58 ms | 543.4 KiB |
| Services | 6.08 ms | 1,019.0 KiB |
| Compact live | 1.45 ms | 338.5 KiB |
| Pinned and selected | 3.21 ms | 840.1 KiB |
| Pinned and selected, grouped | 6.56 ms | 709.8 KiB |
| Same grouped view, CPU tooltip open | 8.39 ms | 843.6 KiB |
| `UsageLedger.Add` | 0.01 ms | 0 B |
| Restart history, 60 minutes | save 0.97 s, read 0.62 s | — |

Allocation per refresh matches the gauge-styles run below exactly, and every time difference from earlier sections is within run-to-run spread (pinned and selected, grouped ranged from 5.99 to 8.14 ms). Live, pinned and tooltip scenarios rendered the same pixels in all four runs. In the other chart and view scenarios one run rendered an alternate frame, consistent with the pointer being over the window. Compact view's hash matches the gauge-styles run.

Two whole-app profiles of 150 s after the 20 s warm-up, run one after the other:

| | Run 1 | Run 2 |
| --- | ---: | ---: |
| Total CPU | 31.4 ms/s | 29.2 ms/s |
| Allocation | 732 KiB/s | 707 KiB/s |
| Private bytes, average / peak | 62 / 70 MiB | 63 / 72 MiB |
| Managed heap, average | 17.6 MiB | 17.1 MiB |
| Gen0 / gen1 / gen2 collections over 170 s | 19 / 4 / 1 | 19 / 4 / 1 |

Allocation and collections are below the October 23 profile (795–809 KiB/s, 20–21 gen0). Total CPU is above its 24 ms/s but within the 30–33 ms/s of October 22; without an alternating baseline these runs do not establish a CPU change. Raw reports: `artifacts/perf-analysis-2026-10-25/bench1.json` through `bench4.json`, `profile/profile.json` and `profile2/profile.json`.

## Gauge styles (October 25, 2026)

Release UI-thread benchmark on the development machine, before and after replacing the `MicroGauge.Wpf` rails with WinMon's own `MetricGauge` drawing, measured with the default Bowl style. Four baseline and four changed runs alternated; each value is the median of the four run medians. The baseline is the commit that added the rails. The changed build is the working tree, which also contained the shared chart label rail below. That work reports unchanged allocation, so the allocation differences here are the gauges'. The time differences in the chart modes are partly the label rail's. Full-view hashes changed as intended; Compact view kept its hash.

| Scenario | Before | After | Allocation before | Allocation after |
| --- | ---: | ---: | ---: | ---: |
| Live overlay | 4.42 ms | 4.34 ms | 603.5 KiB | 531.0 KiB |
| Live small multiples | 3.57 ms | 2.96 ms | 578.4 KiB | 502.3 KiB |
| Historical | 1.91 ms | 1.47 ms | 249.8 KiB | 173.6 KiB |
| Grouped live | 6.70 ms | 6.50 ms | 618.9 KiB | 543.4 KiB |
| Compact live | 1.65 ms | 1.73 ms | 337.7 KiB | 338.5 KiB |

Every Full-view scenario allocates about 72–80 KiB less per refresh, which recovers most of the 86–92 KiB the rails added. The gauges redraw only when a mark moves by half a pixel and reuse frozen pens and geometry. No scenario got slower beyond run-to-run variation. An earlier run with the Needle default gave the same allocation saving. Raw reports: `artifacts/gauge-styles-bench/baseline1.json` through `baseline4.json` and `changed1.json` through `changed4.json`.

## Shared chart label rail (October 25, 2026)

Release UI-thread benchmark on the development machine, with four baseline and four changed runs alternated. The baseline used the current working tree with only CpuGraph.cs restored from main; the changed build used the measured label rail. Values are medians of the four run medians. The non-overlay charts gained about 50 DIPs of plot width and measure their visible labels to size the rail.

| Scenario | Before | After | Allocation before | Allocation after |
| --- | ---: | ---: | ---: | ---: |
| Live overlay | 4.53 ms | 3.93 ms | 533.2 KiB | 533.2 KiB |
| Small multiples | 2.56 ms | 2.99 ms | 504.4 KiB | 504.5 KiB |
| Heatmap | 2.50 ms | 2.51 ms | 547.6 KiB | 547.7 KiB |
| Focus | 2.48 ms | 2.70 ms | 516.9 KiB | 517.0 KiB |
| Historical | 1.13 ms | 1.11 ms | 175.9 KiB | 175.9 KiB |
| Compact live | 1.52 ms | 1.47 ms | 338.2 KiB | 338.2 KiB |

The small-multiples and Focus scenarios took 0.44 ms and 0.22 ms more per refresh respectively, consistent with the wider plotted area and label sizing work. Heatmap time was effectively unchanged. Allocation changed by at most 0.1 KiB per frame in these chart modes. Pixel hashes changed only for the three non-overlay modes, as intended. Raw reports: artifacts/label-rail/baseline-1.json through baseline-4.json and changed-1.json through changed-4.json.

## Wide resource gauges (October 25, 2026)

Release UI-thread benchmark on the development machine, before and after adding five live `MicroGauge.Wpf` rails to the benchmark's Full view. Four baseline and four changed runs alternated; each value below is the median of the four run medians. The Full-view cost is the per-frame gauge refresh and drawing. Compact view skips that work, retains its pixel hash, and has unchanged allocation within run variation. Full-view hashes changed as intended.

| Scenario | Before | After | Allocation before | Allocation after |
| --- | ---: | ---: | ---: | ---: |
| Live overlay | 4.53 ms | 5.18 ms | 520.9 KiB | 612.6 KiB |
| Live small multiples | 2.76 ms | 3.10 ms | 492.5 KiB | 578.6 KiB |
| Historical | 1.21 ms | 1.60 ms | 163.9 KiB | 249.9 KiB |
| Compact live | 1.71 ms | 1.56 ms | 337.5 KiB | 337.9 KiB |

The additional Full-view allocation is about 86–92 KiB per refresh, or about 0.09 MiB/s at WinMon's one-second cadence. The measured time cost is below 1 ms per refresh. The source is the added gauge surfaces and peak calculation; no other benchmark scenario or sampling path changed. Raw reports: `artifacts/microgauge-baseline/perf1.json` through `perf4.json` and `artifacts/microgauge-final2/perf1.json` through `perf4.json`.

## Resource chart views (October 24, 2026)

Release UI-thread benchmark on the development machine, using the same 600-sample, 850-process synthetic
recording for each chart mode. This is a same-build comparison against Overlay, not a pre-change code baseline.
The new chart modes do not increase median refresh time; Heatmap allocates about 15 KiB more per frame.

| Full-view chart | Median refresh | Median allocation |
| --- | ---: | ---: |
| Overlay | 4.94 ms | 521 KiB |
| Small multiples | 2.38 ms | 493 KiB |
| Heatmap | 2.40 ms | 536 KiB |
| Focus | 2.36 ms | 504 KiB |

Report: `artifacts/chart-types-benchmark.json`. The benchmark measures UI-thread refresh and render submission;
it does not time the separate WPF render thread. The benchmark project restored with `NU1900` because the
NuGet vulnerability feed was unreachable; compilation and the benchmark completed.

## Usage tab and automatic captures (October 24, 2026)

Both features were measured against the previous run on the same machine. The Usage tab refreshes only while it
is selected, and the refresh scenarios kept their timings; their pixel hashes changed because the tab strip gained
a third tab, while compact view, which has no tab strip, still matches. Automatic captures are off in the
benchmark, as they are by default, and the chart then skips all marker work: allocations per refresh match the
Usage-tab run exactly in every scenario, and timings stayed within run-to-run variation over two runs (live
5.77 and 6.14 ms against 5.67 ms; services 11.81 and 11.93 ms against 11.42 ms). The 671 tests passed, and the
`--usage-smoke-output`, `--capture-smoke-output`, `--services-smoke-output` and `--smoke-output` diagnostics
exited successfully. Reports are `artifacts/performance/usage-tab-after.json` and `capture-after-3.json` and
`capture-after-4.json`.

## Restart history (October 24, 2026)

In-app updates now save history to the update's transaction directory and the new version restores it
(see [the design note](design/UPDATE-RESTART-HISTORY.md)). The benchmark adds a `restart-history-60-minutes`
scenario: 3,600 synthetic 850-process samples are compacted into a 60-minute `MonitorHistory`, which the
500,000-entry budget trims to 1,961 samples (499,896 entries), then saved and read five times with the real
clock and deadline. Release build on the development machine:

| Measurement | Result |
| --- | ---: |
| Save, median of 5 (first run 1.03 s) | 0.94 s |
| Read, median of 5 (first run 0.85 s) | 0.60 s |
| File size | 15.3 MB |
| Samples restored | 1,961 of 1,961 |

This is the worst case the history budget allows; typical histories are smaller. The save stays inside the
1.2 s point where it stops starting new chunks, so nothing was dropped here. A slower disk drops the oldest
chunks first. The refresh scenarios stayed within run-to-run noise of the previous run, and all pixel hashes
match. The 646 tests passed. The report is `artifacts/performance/restart-after.json`.

## Usage ledger (October 24, 2026)

`MonitorSession.Record` now feeds every live full sample to `UsageLedger`, the data behind the planned Usage
tab. The refresh scenarios time `RefreshPresentation` only, not `Record`, so the benchmark adds a separate
`usage-ledger-add` scenario that times one `UsageLedger.Add` per synthetic 850-process sample. `HEAD` before the
change and the updated build were run on the same machine in Release:

| Per refresh or sample | Before | After |
| --- | ---: | ---: |
| Live | 5.91 ms / 564 KiB | 5.48 ms / 564 KiB |
| Grouped live | 4.71 ms / 571 KiB | 4.86 ms / 572 KiB |
| Services live | 11.73 ms / 1,505 KiB | 11.82 ms / 1,506 KiB |
| Pinned, selected, grouped | 5.92 ms / 740 KiB | 5.80 ms / 740 KiB |
| `UsageLedger.Add` | — | 0.014 ms / 32 B |

Every refresh scenario is within run-to-run noise, and all pixel hashes match the baseline. The ledger hashes
an executable path once per process lifetime and allocates only for new lifetimes and apps, so steady-state
samples cost about 14 µs. The 624 tests passed. Full reports are in `artifacts/performance/usage-before.json`
and `usage-after.json`.

## Resource detail tooltips (October 24, 2026)

`WinMon.Performance` was run against `HEAD` before the change and the resource-detail build on the same
machine, in Release with 850 synthetic processes. The original scenarios keep the same order; the updated
benchmark adds an open CPU tooltip after them. Median UI-thread results from one comparable pair:

| Per refresh | Before | After |
| --- | ---: | ---: |
| Live | 4.45 ms / 558 KiB | 6.17 ms / 564 KiB |
| Compact live | 1.53 ms / 325 KiB | 1.73 ms / 326 KiB |
| Pinned, selected, grouped | 5.93 ms / 738 KiB | 6.07 ms / 740 KiB |
| Same grouped view, CPU tooltip open | — | 8.70 ms / 859 KiB |

Closed-view allocations stayed close to baseline. Timing varied between runs on this machine: updated
live medians ranged from 3.84 to 6.45 ms, so this pair does not establish a sustained CPU change.
The open-tooltip scenario includes its popup, ranking and layout; its 2.63 ms / 119 KiB difference from
the adjacent grouped run measures the cost of keeping one CPU tooltip open. The native smoke run and
609 tests passed. Full reports are in `artifacts/performance/hover-before.json` and `hover-after.json`.

## Code structure refactor (October 24, 2026)

The [structure refactor](design/STRUCTURE.md) moved sampling into `MonitorSession`, diagnostics into `Diagnostics/` and the process list into `ProcessesView`, with no intended change to any frame. Same machine; the baseline binary is the benchmark built from `main` before step 1, run alternately with each step. The benchmark itself had to be fixed first: a reflection call had broken when `ProcessRetention.Compact` gained a parameter.

Steps 1–5 (renames, `MonitorSession`, benchmark and diagnostics moves), quiet machine, median of each build's per-run medians, in ms per refresh:

| Build | Live | Historical | Grouped | Services | Compact | Pinned | Pinned grouped |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| Baseline, 7 runs | 3.81 | 0.99 | 4.52 | 6.66 | 1.29 | 3.01 | 5.61 |
| After `MonitorSession`, 5 runs | 3.94 | 1.04 | 4.77 | 6.82 | 1.32 | 3.02 | 5.70 |

The differences are within the baseline's own run-to-run spread (live 3.65–4.57 ms). Allocation per refresh was identical, and every run rendered the baseline's pixels, except two runs whose last two scenarios matched an alternate frame the baseline also produces when the pointer is over the window.

Step 6 (`ProcessesView`) was first measured while the IDE was indexing, and the noise hid any difference; it was measured again on a quiet machine. Eight alternating pairs, baseline first in each pair:

| Build | Live | Historical | Grouped | Services | Compact | Pinned | Pinned grouped |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| Baseline, 8 runs | 3.59 | 1.06 | 5.36 | 6.72 | 1.49 | 2.93 | 5.54 |
| After `ProcessesView`, 8 runs | 3.82 | 0.98 | 4.68 | 6.96 | 1.43 | 2.91 | 5.62 |

Allocation per refresh was identical in every scenario. Only the live frame was consistently slower: the new build lost 7 of 8 pairs, typically by about 0.2 ms. The other scenarios that also refresh the process list (historical, grouped, pinned) showed no consistent difference, and Services, slower in 6 of 8 pairs, stayed within its run-to-run spread (6.2–7.9 ms). Live is the first scenario measured. With tiered compilation off (`DOTNET_TieredCompilation=0`, three pairs), the live difference disappeared (−0.00, −0.87 and +0.39 ms) and no scenario was consistently slower, so the gap comes from how the JIT tiers the first scenario's code rather than from more work per refresh. Pixels matched the baseline except the known pointer-hover frame and one compact-strip frame in 1 of 8 runs that did not recur.

The full smoke (`--smoke-output`) passed after step 6, but its first run under that load hit the mode's 60-second timeout during the final 15-second idle observation. On a quiet machine its measured phase takes about 50 seconds, so the budget is tight.

## Services view and 2.0 refresh paths (October 23, 2026)

Same machine, about 450–850 processes and 309 services, .NET 10.0.13, Release build output.

Version 2.0.0 added service collection every second, the Services tab, the action bar and process pins, and the UI
benchmark exercised none of them. With the Services tab open, one refresh took 13 ms and allocated 4.2 MiB: it
rebuilt a dictionary for each of the 121 retained service frames and recomputed every group's metrics in all of
them. Service collection allocated about 340 KiB/s whether or not the tab was open, and two minutes of service
frames held about 17 MiB, mostly identical copies of the same service names.

`tools/WinMon.Performance` now also measures a grouped live frame, the Services tab (60 shared hosts and 300
services, every group expanded), the compact strip, and pinned plus selected processes, individual and grouped.
It records a pixel hash for each scenario, and baseline runs matched each other.

Changes:
- Service enumeration reuses its buffer, reads the service manager's strings as spans and reuses the previous
  observation of every unchanged service. `ServiceTree` reuses its collections, group keys, service lists and
  unchanged groups. It no longer allocates a visited set per process: each ancestry step moves to a strictly
  older lifetime, so the walk cannot cycle.
- Each service frame builds its key lookup, group metrics and peak CPU once, on first use.
- The Services list moves rows into place instead of resetting the list, which re-created every visible row's
  container whenever the CPU order changed. Rows whose group is still observed are kept for reuse, and a row
  raises a change only when a bound value changed.
- Pins and the selection share one inclusive ranking per refresh, which is the search ranking when a search is
  active. Grouped ranking builds a row only for each group's busiest member instead of copying every process first.
- Compact mode skips the hidden process list and Services tab. History is copied once per change rather than per
  access, and the live and selected rankings wrap it instead of copying it again.
- Pin retention, PID search and row tooltips no longer allocate per process.

Sampling pass, per second (per-step harness; 442 processes before, 646 after):

| | Before | After |
| --- | ---: | ---: |
| Service enumeration | 1.33 ms, 83 KiB | 1.07 ms, 2.5 KiB |
| `ServiceTree.Capture` | 0.31 ms, 259 KiB | 0.16 ms, 12 KiB |
| Retained per service frame | 145 KiB (17 MiB for 121 frames) | 45 KiB (5.3 MiB) |

UI benchmark, four alternating runs of each build (median of the runs' UI-thread time; allocation was identical
across runs):

| Per refresh | Before | After |
| --- | ---: | ---: |
| Live | 3.95 ms, 538 KiB | 3.71 ms, 524 KiB |
| Historical | 1.03 ms, 172 KiB | 0.97 ms, 167 KiB |
| Grouped live | 4.65 ms, 875 KiB | 4.66 ms, 540 KiB |
| Services tab | 12.65 ms, 4,302 KiB | 6.54 ms, 1,263 KiB |
| Compact strip | 2.69 ms, 683 KiB | 1.33 ms, 305 KiB |
| Pinned and selected | 3.01 ms, 1,075 KiB | 2.88 ms, 837 KiB |
| Pinned and selected, grouped | 5.84 ms, 1,712 KiB | 5.19 ms, 699 KiB |

Grouped live time is unchanged within its noise (4.17–5.57 ms before) despite the lower allocation. Every run of
the new build rendered the same pixels as the baseline in every scenario. One baseline run differed in its last
scenario only; the window is on screen, so pointer hover can change a frame.

Two whole-app profiles ran both builds side by side, swapping launch order, for 150 s after the 20 s warm-up so
that service history reaches its two-minute steady state (the default 60 s ends before it does):

| | Before | After |
| --- | ---: | ---: |
| Allocation | 1,206 / 1,207 KiB/s | 809 / 795 KiB/s |
| Private bytes, average / peak | 68 / 85, 71 / 85 MiB | 60 / 68, 63 / 70 MiB |
| Managed heap, average | 23.8 / 25.4 MiB | 16.3 / 17.8 MiB |
| Gen0 / gen1 collections over 170 s | 32 / 8, 31 / 8 | 21 / 4, 20 / 3 |
| Total CPU | 24.6 / 24.1 ms/s | 24.6 / 23.8 ms/s |

Whole-app CPU did not change: it is dominated by the process snapshot, and the profile does not open the Services
tab, where the refresh savings are.

## Chart drawing and refresh (October 22, 2026)

Same machine, now about 850 processes and 10,000 threads, .NET 10.0.13, Release build output.

Where steady-state CPU goes now (OS per-thread CPU, 90 s window from 150 s, 28.6 ms/s in total):

| Thread | ms/s | Main cost |
| --- | ---: | --- |
| Thread-pool sampling pass | ~15 | `NtQuerySystemInformation(SystemProcessInformation)`: 9.6 ms per call |
| UI thread | ~4–7 | WPF text formatting and layout of changed values, then WinMon's refresh and chart drawing |
| WPF render thread | ~6 | Composing the charts, sparklines and changed text |

The process snapshot now costs twice what it did on October 20 because the machine has about 10,000
threads instead of 6,000: the query returns every thread, and its cost follows the thread count (a later
pass with 1,290 processes and 13,400 threads took 17 ms). It is the OS floor for 1 Hz sampling. The rest
of a sampling pass is building process usages (about 1.7 ms), GPU counters (1.3 ms), other resources
(0.3 ms) and compaction (0.2 ms), measured with thread cycle counts by the per-step harness.

An allocation trace of the UI benchmark showed chart drawing, not the refresh logic, as the largest
allocator on the UI thread. Each redraw built a network-source string per sample, cloned a brush and
created a pen per segment, copied each segment's points, and shaped all eleven axis labels again. A CPU
trace showed most of `ProcessGraphData.Build` in `DateTimeOffset.ToFileTime`, which on current Windows
makes a leap-second-aware system call (about 4 µs), once per visible sample per refresh.

Changes:
- `CpuGraph` compares trace sources without building strings, reuses its point buffers, caches frozen pens
  and fill brushes and the shaped axis labels, and freezes its geometries. Pens copy a theme brush rather
  than freeze the shared resource.
- `ProcessGraphData` converts each sample's timestamp once, cached per sample object.
- `MetricReadout.ShowValue` updates its two runs in place, so an unchanged network value no longer
  replaces the label's content and lays it out again.
- Process rows and column headings set a resource reference only when its key changes.
- The sampler sorts process usages in place instead of through a three-key LINQ sort and a copy.

`tools/WinMon.Performance` was rewritten for the current window. It adds one synthetic 850-process sample per
frame, then measures the refresh and the dispatcher's layout, render and channel commit with UI-thread cycle
counts. Sampling is stopped and the rendered window is hashed:

```powershell
dotnet build tools/WinMon.Performance/WinMon.Performance.csproj -c Release
.\tools\WinMon.Performance\bin\Release\net10.0-windows\win-x64\WinMon.Performance.exe D:\Projects\WinMon\artifacts\bench.json D:\Projects\WinMon\artifacts\bench.png
```

Four alternating runs of each build (medians of seven 60-frame batches after 400 warm-up frames):

| Per refresh | Before | After |
| --- | ---: | ---: |
| Live: UI-thread CPU, median of runs | 4.1 ms | 3.4 ms |
| Live: allocated | 706 KiB | 535 KiB |
| Historical: UI-thread CPU, median of runs | 1.57 ms | 1.19 ms |
| Historical: allocated | 295 KiB | 167 KiB |

After was faster in all four pairs, and the rendered window hashes match the baseline exactly. Text
formatting and layout of values that genuinely change are about half of a live frame, and are unchanged.
Giving each value cell a fixed-width slot, so a new width cannot re-lay out its row, made frames about
0.3 ms slower and was dropped.

Two whole-app runs ran both builds side by side, swapping launch order (profile mode, 120 s window from 90 s):

| | Before | After |
| --- | ---: | ---: |
| Allocation | 1,133 / 1,161 KiB/s | 904 / 928 KiB/s |
| Gen0 collections over the 210 s profile | 40 / 41 | 31 / 33 |
| Total CPU | 33.5 / 29.8 ms/s | 30.3 / 31.9 ms/s |
| UI thread | 5.1 / 3.9 ms/s | 3.6 / 5.0 ms/s |

Allocation fell by a fifth in both runs. Whole-app CPU did not move beyond the noise: in both runs the
instance launched first used about 1 ms/s more on its UI thread, and the averages differ by under 2%. The
gain is real on the UI thread but small next to the process snapshot and WPF's own text and composition work.
Raw data, scripts (`ab-app.ps1`, `threads.ps1`, `bench.sh`, `ab.sh`), traces and the per-step harness are in
`artifacts/perf-2026-10-22/`.

## CPU profile and JIT settings (October 20, 2026, later)

Same machine, about 430–520 processes and 5,500–6,300 threads, .NET 10.0.13. Release build output
and the published single-file executable behave the same. No code or build setting changed: nothing
tested lowered both startup and steady-state CPU.

Where steady-state CPU goes (OS per-thread CPU, 60 s window from 82 s, 17.4 ms/s in total):

| Thread | ms/s | Main cost |
| --- | ---: | --- |
| Thread-pool sampling pass | 7.8 | `NtQuerySystemInformation(SystemProcessInformation)` |
| WPF render thread (native) | 3.6 | Composing the values that changed |
| UI thread | 3.4 | WPF text measure and layout of about 35 changing text fields |
| Other | 2.6 | Mostly threads that exited during the window (retired pool workers); finalizer 0.3 |

- The process snapshot costs about 4.8 ms per call at 1 Hz, but 1.6 ms in a tight loop. Between
  calls the CPU caches and clocks go cold, so this is the OS floor for one call per second. The
  buffer size makes no difference.
- Each pass spends about 0.6–0.8 ms on GPU PDH counters (0.2 ms of that is per-process lifetime
  checks), about 0.6 ms on disk, network and memory, 0.1 ms on `EnumWindows` and 0.06 ms on
  retention compaction. Each is below the ±0.5 ms/s difference between repeated runs.
- WinMon's own refresh code (ranking, sparkline data, readouts) is about 0.6 ms/s of the UI
  thread. The rest is WPF formatting text that genuinely changed.

At launch, the first 20 seconds use about 3.1 s of CPU. In the first 100 s the JIT compiled about
5,300 methods at tier 1. Half were instrumented, mostly hot precompiled WPF methods: Tiered PGO
compiles each one twice, first instrumented and then optimized. One-time PDH counter setup and
the first sample's icon and version-info reads come next. Runtime settings were compared on the
same binary, with runs in parallel:

| Setting | CPU 0–20 s | CPU 0–140 s | Steady state from 140 s | Verdict |
| --- | ---: | ---: | ---: | --- |
| Default (4 runs, to 620 s) | 3.13 s | 5.46 s | 17.2 ms/s | Kept |
| `TieredPGO=0` (4 runs, to 620 s) | 1.93 s | 3.77 s | 17.6 ms/s | Breaks even after about 1 hour |
| `TieredCompilation=0` (2 runs, to 320 s) | 1.54 s | 3.52 s | 17.9 ms/s vs 16.2 default | Precompiled code is never re-optimized |
| `TC_QuickJit=0` + `TieredPGO=0` (2 runs, to 320 s) | 1.96 s | 3.83 s | 16.6 ms/s vs 16.2 default | Inside the noise; not adopted |
| `PublishReadyToRun` (published, 3 runs) | 2.90 s vs 3.18 s | 5.54 s vs 5.79 s | Unchanged | +20 MB exe, +3.6 MiB private |

Turning off PGO also saves about 2 MiB of private memory and 6 MiB of working set. It is the
only lever worth reconsidering, if launch cost ever matters more than all-day cost. ReadyToRun
helps little because most tier-1 work is WPF framework code that is already precompiled, not
WinMon's own code. Traces, scripts (`run-configs.ps1`, `measure-startup.ps1`), the per-step
sampling harness and the analyzers are in `artifacts/perf-cpu/`.

The Release build and all 354 tests passed. The added regression covers repeated
aggregation with disappearing engines, invalid and empty samples, and preservation
of previously returned results. Sandboxed VSTest could not connect to its testhost;
the same test command completed successfully outside the sandbox. Package restore
required network access; the test command used `-p:NuGetAudit=false` for the offline
restore, so this validation does not include a package vulnerability audit.

## GPU aggregation scratch storage (October 20, 2026 follow-up)

A fresh Release `dotnet-trace` allocation profile attributed about 168 KiB/s to
GPU per-process aggregation, mostly dictionary backing arrays. Each sampler now
reuses its physical-engine, process-engine and busiest-process dictionaries.
Every pass clears prior readings; returned readings remain independent snapshots.
The GPU engine selection, invalid-reading handling and PID lifetime checks are unchanged.
The follow-up 30-second allocation trace no longer listed the GPU aggregation
dictionary arrays among the leading allocation types. Sampled total allocation was
about 1,008 KiB/s before and 823 KiB/s after; these sampled, uncontrolled trace
windows corroborate removal of the hotspot rather than quantify a sustained app-wide gain.

Matched Release aggregation measurements on .NET 10.0.13 (SDK 10.0.402), using
600 parsed synthetic engine instances across 50 processes, 1,000 warm-up iterations
and seven batches of 1,000 iterations:

| Per aggregation pass | Before | After |
| --- | ---: | ---: |
| Median managed allocation | 146,360 bytes | 3,824 bytes |
| Median elapsed time | 0.0514 ms | 0.0503 ms |

This is a 97.4% allocation reduction for aggregation alone. Both builds produced
identical result values. It excludes native counter collection, parsing, rendering
and other sampling work; it does not establish a whole-app CPU or memory improvement.
The original and changed builds, benchmark source, JSON measurements, runtime
counters and allocation traces are under `artifacts/perf-followup/` (local ignored
artifacts). `run-comparison.ps1` alternates four untraced live runs, each with the
same 20-second warm-up and 60-second measurement window.

| Untraced live run | CPU ms/s | Allocation KiB/s | Private MiB |
| --- | ---: | ---: | ---: |
| Before 1 | 32.6 | 1,021 | 56.4 |
| After 1 | 87.7 | 2,921 | 56.8 |
| After 2 | 33.6 | 952 | 59.3 |
| Before 2 | 30.6 | 982 | 54.7 |

After 1 overlapped test/benchmark verification and is unsuitable for a controlled
comparison. Other machine activity was not controlled either. These short live
runs do not establish overall CPU or private-memory savings; the strong result
is the matched allocation reduction in the aggregation benchmark.
## Allocation, GC and compression (October 20, 2026)

Same machine, now about 1,250 processes and 611 GPU Engine counter instances. Allocation traces showed two main problems:
- GPU counter names went through a regex about 1,800 times per second. That was 58% of all allocation.
- Three per-second collections sized by the process list landed on the large object heap: the CPU baseline dictionary, the snapshot list and the ranking totals. They caused every gen2 collection.

Changes:
- GPU names are parsed once without a regex and cached, and the parsed readings serve both aggregations.
- Those three collections are reused.
- Ranking runs once per refresh.
- The gen0 budget is capped. Workstation GC sizes it from L3 cache, up to 48 MiB here, and at about 1 MB/s of allocation the app committed the whole budget between rare collections.
- Single-file compression is off.

| | Before | After |
| --- | ---: | ---: |
| Published executable, 60 s: working set / private | 288 / 156 MiB | 132 / 53 MiB |
| 11-minute run with full history: working set / private | 184 / 98.5 MiB (build output) | 146 / 60 MiB (published) |
| 11-minute run: allocation | 3,771 KiB/s | 1,102 KiB/s |
| 11-minute run: gen2 collections | 46 | 2 |
| 11-minute run: CPU | 26.6 ms/s (build output) | 30.3 ms/s (published) |

The 11-minute CPU figures are single uncontrolled runs. Alternating 60-second runs showed no CPU change from any of these changes.

Alternating 60-second runs isolate each change:

| Change | Working set | Private bytes | Other |
| --- | --- | --- | --- |
| Allocation fixes (build output) | 157 → 163 MiB | 77.5 → 86 MiB | Allocation −74%; gen2 GCs 7 → 1 per minute |
| Gen0 cap (build output) | 166 → 132 MiB | 89.5 → 53.5 MiB | gen2 GCs 1 → 0 per minute |
| Compression off (published) | 264 → 132 MiB | 131.5 → 53.4 MiB | Startup median 742 → 565 ms; exe 72 → 166 MiB |

`GCgen0size` is honored only from the environment. The runtimeconfig form used here is `System.GC.Gen0MaxBudget`, verified with `GC.GetConfigurationVariables()`. It is undocumented, so re-check it on runtime upgrades. Raw data, traces and analysis tools are in `artifacts/perf-2026-10-20/`.

## Whole-process sampling rewrite (October 19, 2026)

October 19, 2026, this machine (12 logical processors, about 1,000 processes), build output, same tree with and without the change:

| | Before | After |
| --- | ---: | ---: |
| CPU, 60 s run (two runs each) | 68–69 ms/s | 36–38 ms/s |
| CPU, 11-minute run | 65 ms/s | 34 ms/s |
| Working set with a full 10-minute history | 265 MiB | 196 MiB |
| Private bytes with a full 10-minute history | 180 MiB | 111 MiB |
| Retained process entries at 10 minutes | 469,400 | 60,816 |
| Processes that could not be read | ~155 | 0 |

- Processes come from one `NtQuerySystemInformation(SystemProcessInformation)` call instead of `Process.GetProcesses()` plus a handle per process, which also measures access-restricted processes. The query buffer is pinned and reused, and a process name string is reused for the life of the process.
- Network adapters are enumerated only after a Windows network-change event or every 30 seconds; counters are still read every second.
- Retained samples keep only processes with CPU, I/O or GPU readings plus the 32 largest working sets. Dropped idle entries are counted and treated as zero by ranking, the Other processes row and sparklines.

The compressed single-file publish uses about 130 MiB more working set than an uncompressed one (312 vs 182 MiB, private 173 vs 96 MiB), because compressed assemblies are inflated into private memory instead of mapped from the file. Uncompressed, the executable is 173 MB instead of 76 MB. Software rendering made no measurable difference. The UI microbenchmark was rewritten on October 22, 2026 (see [Chart drawing and refresh](#chart-drawing-and-refresh-october-22-2026)).

## UI microbenchmark (October 19, 2026)

October 19, 2026: process sparklines now share one extraction pass over the visible
history, reusing values for scaling and drawing. Hidden/minimized windows continue
recording and updating tray status, defer presentation work, and refresh on restore.
Overlay rendering no longer constructs unused fill geometries or brushes.

Release STA microbenchmark on this machine, SDK 10.0.402 / runtime 10.0.13:

| Work per iteration | Before ms | After ms | Before allocated bytes | After allocated bytes |
| --- | ---: | ---: | ---: | ---: |
| Visible refresh only | 0.303 | 0.382 | 22,784 | 38,264 |
| Visible refresh and six graph draws | 1.669 | 1.032 | 462,906 | 369,778 |
| Historical refresh and six graph draws | 2.266 | 1.353 | 458,208 | 365,112 |
| Hidden refresh | 0.315 | <0.001 | 22,784 | <1 |

Caching moves some work and allocations into refresh preparation, while reducing
combined refresh/drawing time by 38% live and 40% historical and allocations by
about 20%. Live and historical rendered pixel hashes match the baseline exactly.
Hidden measurements use diagnostic mode without a tray icon; actual tray text
updates are excluded. The remaining fraction of a byte is measurement overhead.

The fixture has 600 samples, 500 processes per sample, rotating CPU rankings,
periodic gaps, a 120-second compact window, and a historical selection at sample
480. Sampling is paused. Each case has 30 warmups followed by seven batches of
100 iterations; the table reports the median batch mean. Allocation counts cover
the calling UI thread. `DOTNET_TieredCompilation=0` is used for both variants to
reduce tiering noise; the shipped application retains its normal runtime settings.
Drawing is forced through WPF drawing commands and excludes compositor/GPU time.
These measurements do not establish sustained whole-process CPU or memory savings.

Reproduce against each version, in separate processes:

```powershell
dotnet build tools/WinMon.Performance/WinMon.Performance.csproj -c Release -m:1 -p:UseSharedCompilation=false
$env:DOTNET_TieredCompilation = '0'
./tools/WinMon.Performance/bin/Release/net10.0-windows/win-x64/WinMon.Performance.exe D:\Projects\WinMon\artifacts\performance\result.json
Remove-Item Env:DOTNET_TieredCompilation
```

Preserve the complete benchmark output directory before editing application code
to retain a runnable baseline. The local evidence is in
`artifacts/performance/before.json`, `before-repeat.json`, and `after.json`, with
the frozen baseline in `before-run/`. Each report includes the application assembly
SHA-256, per-batch timing/allocation data, and live/historical pixel hashes.
Native smoke evidence is in `smoke-before/` and `smoke-after/`; the latter also checks
continued recording while hidden/minimized and immediate presentation on restore.
Both native smoke runs exited successfully. Their 15-second quiet observations
reported 0.581% and 0.642% of total machine CPU respectively; these short,
uncontrolled observations do not demonstrate a whole-process CPU reduction.
The Release suite passed all 55 baseline tests and all 58 updated tests.

## Validation notes

The hardware/process/palette increment passed 155 Release tests, including termination of an owned disposable helper and rejection of a mismatched process lifetime. Focused feature smoke (`--feature-smoke-output <directory>`) and the full smoke both exited 0; renders and reports are under `artifacts/hardware-process-palettes-final/` and `artifacts/feature-full-smoke/`. The first full attempt failed its native caption-button check; the final full rerun passed. Tests ran outside the sandbox after its test host failed to connect. Restore used command-scoped `-p:NuGetAudit=false` because the audit endpoint was unreachable; no audit setting was changed in the repository. UI smoke does not invoke process termination; interactive confirmation and actual keyboard/mixed-DPI behavior remain manual checks.

Pinned process rows: 446 Release tests passed outside the sandbox, snapshot/pinning smoke exited 0, and `git diff --check` passed.
