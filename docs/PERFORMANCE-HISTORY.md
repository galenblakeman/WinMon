# WinMon performance history

[Back to the developer guide](DEVELOPMENT.md)

Dated measurement and validation records, newest first. Each section describes the build and machine it was measured on, and some name code that has since changed or moved. For how to measure today and the decisions still in force, see [Performance](DEVELOPMENT.md#performance) in the developer guide. The raw data named under `artifacts/` is local and not in the repository.

Add a section at the top for each measured change: what changed, the machine and build, before and after results, and where the raw reports are.

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
