# WinMon performance history

## GPU clock and utilization peak (November 26, 2026)

The Release live-frame benchmark used synthetic thermal data, 400 warm-up frames, and seven batches of 60 measured frames per run. The two baseline medians were 4.150 and 5.129 ms per frame, compared with 4.980 and 3.294 ms with the new clock readout. Median UI-thread allocation ranged from 551,725 to 552,297 bytes before and from 551,661 to 552,081 bytes after. Other verification work ran during the comparison, so timing varies substantially between runs. The benchmark excludes native driver sampling. Results and renders are under `artifacts/gpu-gauge/`.

## Focus temperature overlay (November 26, 2026)

The same Release WPF fixture rendered 120 synthetic observations at 830 × 280 with CPU, Disk, GPU and Memory Focus. Each run warmed up 300 frames and measured seven batches of 60. In before/after/after/before order, the median render times ranged from 2.70–3.54 to 3.55–3.82 ms for CPU, 2.06–3.39 to 2.47–3.23 ms for Disk, and 2.09–2.24 to 3.18–3.37 ms for GPU. Drawing the extra trace, legend and axis added about 7–8 KiB of UI-thread allocation per render. Memory Focus, which has no temperature overlay, allocated 26,347 bytes per frame in all four runs. The measurements include layout and fresh bitmap rendering, exclude native sampling, and ran alongside verification, which limits wall-time comparisons. Results are in `artifacts/focus-thermal-verify/bench/results.txt`.

## Automatic temperature chart scale (November 26, 2026)

The Release temperature-chart benchmark used the same harness for four runs per variant, with 400 warmup frames and seven batches of 60 frames per run. The median of the run medians was 4.667 ms before and 4.573 ms after. Baseline runs ranged from 4.437 to 4.782 ms, and candidate runs ranged from 4.452 to 4.765 ms. Median allocation rose from 562,593 to 563,777 bytes per frame (0.21%). The synthetic 42–83 °C readings drew seven labeled grid lines on the automatic 30–90 °C axis, compared with four on the fixed 0–150 °C axis. These runs used the default chart height and preceded the minimum label spacing for shorter charts. The benchmark measures UI-thread refresh, layout and rendering with synthetic samples. It excludes native sampling and render-thread CPU. Raw runs and screenshots are in `artifacts/temperature-axis/`.

## Multiple scoped rules and disk latency (November 26, 2026)

A Release console fixture compared the prior trend build with the scoped-rule and latency build. It used 850 named processes, two warm-up passes and seven measured passes of 600 observations. One application CPU rule took 52.6–54.8 µs before and 48.1–49.2 µs after. Eight evaluation rows took 101.5–129.6 µs before and 114.7–120.2 µs after. The earlier engine could evaluate those rows but shared their rule state. The new engine tracks each independently. Allocations with eight rules were 1,664 to 1,728 bytes per sample.

Aggregating two volume response measurements took 3.24–3.45 µs per sample, compared with 2.66–3.35 µs before those fields were archived. Allocation rose from 1,009 to 1,075 bytes per sample. These measurements exclude WPF rendering and native collection and ran alongside verification. The harness and raw results are in `artifacts/verify/scoped-latency-performance/`.

## Peak evidence in Longer trends (November 26, 2026)

A Release console fixture compared the prior MonCore archive with schema 3 in baseline, candidate, candidate, baseline order. After two warm-up passes, seven batches of 10,000 synthetic resource frames measured medians of 1.70–3.21 µs per sample before and 1.60–1.90 µs after. Median allocation increased from 1,153.6 to 1,156.0 bytes per sample. Concurrent builds and the spread limit timing conclusions.

With 10,080 populated minutes, snapshot time was 17.7–20.5 ms before and 16.5–17.0 ms after. The compressed fixture grew from 782,268 to 1,080,339 bytes with the peak timestamps. Save time increased from 387–424 ms to 468–573 ms, and load time ranged 311–413 ms before and 372–474 ms after. These measurements use one reading per minute for persistence and exclude WPF rendering and native collection. The script and raw outputs are in `artifacts/verify/trend-peaks-performance/`.

## Automatic incident saving (November 26, 2026)

The Release `--live-only` harness used 400 warm-up frames and seven batches of 60. Two runs against `3427660` measured 5.09–5.21 ms per frame and 551.8–552.2 kB allocated. Two runs with automatic incident saving measured 4.46–5.78 ms and 552.6–553.0 kB. The timing ranges overlap. Concurrent verification and other working-tree changes limit attribution. This workload measures ordinary synthetic UI frames and does not fire automatic captures or measure native collection. The capture smoke verifies the separate trigger and save path. Raw results are under `artifacts/verify/automatic-incidents-performance/`.

## Temperature history and captures (November 26, 2026)

Compared preserved pre-change and candidate Release assemblies in baseline, candidate, candidate, baseline order. Ordinary live-frame medians ranged 4.71–5.15 ms before and 4.33–4.73 ms after, using 400 warmups and seven batches of 60 frames. Allocations were about 552.2–552.8 kB before and 552.9–553.0 kB after. No consistent rendering slowdown appeared. Concurrent verification and other working-tree work limit attribution; this is a sanity check.

The portable-core harness on PowerShell's .NET 10.0.14 used 1,000 warmups and seven batches of 10,000 additions. No-sensor timings ranged 2.47–6.16 µs before and 2.06–2.69 µs after, with about 24 added bytes per observation. With CPU/GPU/disk temperatures and limits supplied, additions took 0.86–0.87 µs before (omitting those metrics) and 1.22–1.66 µs after, with about 400 added bytes per observation. New projections, CPU identities and larger minute slots explain that cost; the no-sensor spread limits timing conclusions.

A 3,600-observation restart fixture with 32 processes per observation and CPU limit percentage/flags measured mean save medians of 258.7 ms before / 341.9 ms after, and read medians of 154.7 / 234.2 ms. Compressed files grew from about 423.2 to 445.6 kB. Every save met the existing deadline and every read restored all 3,600 observations. Bounded sensor JSON explains the added serialization and storage work. This is not the full 500,000-process-entry budget or a rich multi-sensor fixture.

Raw evidence and preserved binaries: `artifacts/thermal-history/`. Measurements precede the final service-visibility guard. The user stopped tests, so idle repeats, populated chart-gallery review and final acceptance were not completed. Native acquisition and render-thread work are excluded.

## Collapsible History Events sidebar (November 26, 2026)

The focused `--history-layout-only` workload selects synthetic retained observations, lays out the History dialog and renders WPF content. Each process runs 400 warm-up frames and seven batches of 60 frames. Four alternating runs per variant gave a median of run medians of 18.603 ms before and 19.124 ms after (+0.521 ms, 2.8%); run medians ranged from 17.484–20.302 ms before and 18.098–20.184 ms after. Median allocations increased from 2,866,352 to 2,870,922 bytes per selection/render (+4,570 bytes, 0.16%), consistent with the additional disclosure controls and sidebar layout. This is a small interaction-render cost, not a sampling-path change. Timing ranges overlap; concurrent desktop verification and other working-tree changes limit attribution, so these figures are a UI sanity check rather than an isolated causal benchmark. Native collection and compositor work are excluded. Preserved binaries, all eight alternating results and the summary are under `artifacts/history-sidebar/`; initial baseline-only runs are retained separately.

## CPU tooltip readability (November 26, 2026)

Measured the frozen baseline and updated Release production assemblies with identical WinMon.Performance harness bytes, using synthetic temperature readings, 400 warmup frames and seven batches of 60 frames per run. Sequential live-only runs used baseline, candidate, candidate, baseline order. The mean of the two live-frame medians was 4.647 ms before and 4.376 ms after; median allocations ranged from 552.8 to 561.4 kB before and 553.1 to 554.7 kB after. The broader open-tooltip scenario measured 13.796 ms / 1,032,326 bytes before and 11.872 ms / 972,726 bytes after. No regression was observed in the repeated live workload. Concurrent work made the first single live comparison noisy, so it was not used for acceptance. These UI-thread measurements exclude native collection and render-thread CPU, and the production snapshots include existing working-tree changes. Raw outputs and frozen binaries are under artifacts/tooltip-review; the four live-* directories contain the repeated results.

## Devices GPU selection (November 26, 2026)

Measured 600 parsed engine counters using the same isolated reflection/expression harness against preserved original and candidate Release assemblies (5,000 warmup calls; seven batches of 10,000 calls). Automatic median was 0.028163 ms before and 0.028261 ms after (+0.35%); allocation remained 40 bytes per call. Manual selection across the two-adapter fixture took 0.015011 ms. This measures engine aggregation only; native discovery is cached for 30 seconds and is absent from Automatic sampling. Raw results and harness are under `artifacts/devices-baseline`, `artifacts/devices-focused` and `artifacts/devices-aggregation-benchmark.ps1`. Native physical multi-GPU/reboot/hot-plug acceptance remains open.

## Disk temperature and board fans (November 26, 2026)

Compared the immediately preceding Store assemblies with the drive and fan candidate using the same Release WinMon.Performance harness. Ran the synthetic UI live-frame thermal case twice per variant in baseline, candidate, candidate, baseline order. Each process used 400 warmup frames and seven batches of 60 frames. Baseline medians were 4.638 and 4.812 ms per frame. Candidate medians were 4.897 and 4.552 ms. The mean of the two medians was 4.725 ms on both sides. Median allocations were about 561.4 kB per frame in all four runs. This is a small UI sanity check. It excludes native storage queries, service polling and real devices. An initial comparison against the older gauge subtitle build overlapped verification and was not used for acceptance. Raw results are in `artifacts/verify/drive-fan-performance`.


[Back to the developer guide](DEVELOPMENT.md)

## Network collector batch fairness and lifetime bounds (November 26, 2026)

Compared the preserved pre-fix collector and corrected Release collector with the same compiled timing probe on .NET 10, with tiered compilation disabled. Four before/after pairs alternated launch order. Each process warmed up one completed-queue drain, then measured seven drains of 32,769 synthetic stop events for an untracked PID. Fixture construction, event enqueueing and disposal were outside the measured interval; the manual clock's read-list capacity was reserved beforehand. The queue and fake clock are the existing service-test fixtures, and the measured consumer is production `ConsumeAsync`.

| Completed-queue drain | Before | After |
| --- | ---: | ---: |
| Median of four run medians | 1.176 ms | 1.171 ms |
| Run median range | 1.151–1.300 ms | 1.160–1.190 ms |
| Median managed allocation per drain | 576 bytes | 1,152 bytes |

No timing regression was observed in this bounded probe; the small difference does not establish a speedup. Returning through the existing async wait loop between batches adds 576 bytes per 32,769-event drain (about 0.018 bytes/event). That small bounded cost buys cancellation and reconciliation progress without waiting for an empty queue. The lifetime-bound change is outside the ordinary per-packet path. This probe excludes native ETW delivery, ownership I/O, endpoint formatting and whole-service performance under sustained traffic; it does not establish native acceptance.

Harness, preserved assemblies and raw measurements: `artifacts/network-collector-fixes/`. See [network collector corrections](design/NETWORK-COLLECTOR-CORRECTIONS.md).

## CLI connection report projection (November 26, 2026)

Compared preserved baseline and corrected Release `WinMon.Cli` assemblies on .NET 10 with SDK 10.0.404. A prepared fixture uses 1,000 process owners and 100, 10,000 or 30,000 TCP endpoints belonging to the final owner, with name sorting, no filter and Top 1. Each separate PowerShell process disables tiered compilation, warms each case three times, then measures five report constructions. The table gives medians; allocations include the common reflection/PowerShell invocation overhead. Fixture construction, native collection, serialization, terminal rendering and whole-process memory are excluded.

| Endpoints | Before | After | Allocated before | Allocated after |
| --- | ---: | ---: | ---: | ---: |
| 100 | 0.694 ms | 0.049 ms | 100,408 B | 48,816 B |
| 10,000 | 66.867 ms | 3.180 ms | 9,676,448 B | 484,440 B |
| 30,000 | 218.953 ms | 2.718 ms | 29,196,464 B | 1,364,456 B |

The 30,000-endpoint case is about 81 times faster with 95% less allocation. These short prepared measurements include scheduling/GC noise; they establish the reduction in repeated work, not a whole-command latency guarantee. Serialized JSON plus selected table-cell fingerprints match in all three cases. Deterministic tests separately enforce one owner-list pass and no formatting beyond Top, along with lifetime identity, filtering, stable ordering and count/truncation metadata. The probe, raw measurements and preserved baseline are under ignored `artifacts/cli-audit-fix/` (`Measure-Connections.ps1`, `before.json`, `after.json`, `baseline/`).

## Executable-path recovery (November 26, 2026)

Compared the preserved pre-fix adapter with the corrected Release adapter in four alternating before/after pairs. Each process used 850 fixed process lifetimes, 1,000 warm-up captures and seven batches of 4,000 captures per case. The clock used real monotonic time; injected lookup delegates returned either a stable path or an unavailable path. These cases measure cached successful lookups and the failed-lookup backoff between retries, excluding native queries and full UI/sampling cost.

| Cached path case | Before, ms/capture | After, ms/capture | Before / after run-median range, ms |
| --- | ---: | ---: | --- |
| All readable | 0.01320 | 0.01489 | 0.01305–0.01455 / 0.01426–0.01749 |
| All unavailable, within backoff | 0.01336 | 0.01867 | 0.01325–0.01349 / 0.01850–0.02932 |

Values are medians of the four run medians. Added cost is about 1.7 microseconds for 850 readable entries and 5.3 microseconds for 850 unavailable entries per capture, from the timestamped cache and elapsed-time checks. Mean measured allocation remained below one byte per capture in both cases; no per-entry allocation was added. The cache stores one additional timestamp per retained lifetime. Native retry cost is deliberately paid only after the 30-second backoff and is not measured here. This bounded synthetic increase is accepted to restore recovery; these results do not establish whole-app overhead or a speedup.

Harness, preserved assemblies, raw pairs and summary: `artifacts/audit-fixes/perf/`. See [sampling recovery corrections](design/SAMPLING-RECOVERY-CORRECTIONS.md).

## Optional counter recovery and shared startup tests (November 26, 2026)

Compared preserved baseline `82f30c5` and corrected Release Windows adapter builds using the existing native resource-sampler probe. Four pairs alternated launch order after both test hosts had exited. Each process warmed up for 30 reads, then measured 200 back-to-back resource samples with zero process rows.

| Native resource sampler | Before | After |
| --- | ---: | ---: |
| Median of four run medians | 1.476 ms | 1.438 ms |
| Run median range | 1.433–1.603 ms | 1.343–1.465 ms |
| Managed bytes/sample, every run | 203,400 | 203,400 |

No regression was observed in this probe; the small timing difference does not establish a speedup. A preliminary four-pair run while tests were active measured 1.840 versus 1.852 ms with the same allocation; the quiet repeat removed that load uncertainty. The GPU counter boundary adds one owned object per query rather than per sample. Service startup's owned scope is outside the sampling loop; its native install/start cost was not measured.

The probe uses reflection and excludes process enumeration, real one-second scheduling, whole-app/render-thread work and supported physical GPU fault injection. The baseline probe reproduced the null thermal-zone failure under sandbox counter restrictions and completed outside the sandbox. Raw measurements are `artifacts/audit-fixes/adapter-quiet-*.json`; both Release builds, preliminary measurements and verification evidence remain alongside them. Desktop verification passed 11,381 tests and the 59-render gauges and 23-render connections smokes; the focused Store target passed 102 tests. See [optional counter recovery](design/OPTIONAL-COUNTER-RECOVERY.md).

## MonCore accounting corrections (November 26, 2026)

Compared the original MonCore source at task start with the fixed Release library using the same compiled, dependency-free measurement host on .NET 10.0.15. Each scenario warmed up 400 calls, then ran seven batches of 60 calls. Three before/after pairs alternated launch order; the table reports the median of the three run medians. Ranking uses five observations of 850 processes grouped into 85 executables; usage uses 850 processes and network accounting uses 80 stable flows. These synthetic timings exclude native collection, desktop rendering and scheduling.

| Operation | Before, ms/call | After, ms/call | Before / after allocated bytes/call |
| --- | ---: | ---: | ---: |
| Windows grouped ranking | 0.334 | 0.341 | 32,464 / 32,464 |
| Portable grouped ranking | 0.329 | 0.548 | 73,264 / 73,264 |
| Windows usage ledger | 0.092 | 0.095 | 32 / 32 |
| Portable usage ledger | 0.127 | 0.136 | 40,832 / 40,832 |
| Usage history | 0.121 | 0.122 | 34,104 / 34,104 |
| Network, 80 flows | 0.055 | 0.062 | 25,400 / 25,416 |

Portable grouping now hashes and compares executable identity as well as process lifetime across every interval, preserving activity on both sides of an executable replacement. Its measured additional cost is about 0.22 ms per ranking. Reused scratch tables and member lists keep allocations level; internal ExactPaths keys avoid allocating the public path prefix per interval. The original Windows ranking path remains in place. Flow accounting adds one dictionary lookup per current flow and 16 bytes per frame for the rate-quality closure. The small remaining differences are not evidence of improvements or regressions beyond these identified costs.

One isolated pair with the existing real-window live-only benchmark (400 warm-up frames, seven batches of 60) measured 4.347 ms before and 4.007 ms after, with matching 560,901 bytes/frame and identical pixel hashes. One pair does not establish a speedup. Both runs used the same benchmark/dependency files, replacing only MonCore.dll. The earlier allocation-heavy candidate was rejected before final validation.

Raw results and the three-pair comparison are under `artifacts/moncore-audit-fixes/` (`core-*-pair*.json`, `performance.csv`, `live-before.json`, `live-after-final.json`). See [the correction note](design/MONCORE-ACCOUNTING-CORRECTIONS.md).

## Separate CPU temperature service (November 26, 2026)

Compared the preserved phase 1 Release benchmark with the CPU-service implementation on the development VM. The baseline contains the built-in GPU/thermal-zone presentation and performance-limit markers, but no CPU service reading or CPU temperature peaks. Eight UI pairs alternated launch order in two sets; the second set reversed the first set's starting order. Each run used the existing 850-process live-frame fixture, 400 warm-up frames and seven batches of 60. Four additional runs included CPU temperature 52 °C alongside the existing illustrative GPU, thermal-zone and performance-limit inputs.

| Live UI frame | Phase 1, 8 runs | CPU service, absent, 8 runs | Temperature fixture, 4 runs |
| --- | ---: | ---: | ---: |
| Median of run medians | 4.217 ms | 4.387 ms | 4.505 ms |
| Run median range | 4.023–5.126 ms | 4.220–4.992 ms | 4.240–4.903 ms |
| Median allocated bytes/frame | 560,927 | 560,925 | 561,194 |

The first four pairs reported +11.4% by the ratio of run medians; the independent repeat reported −8.2%. Across all eight pairs, the median paired difference was +0.089 ms (+2.1%), with individual differences from −17.7% to +22.7%. The initial slowdown did not reproduce consistently, and the small combined difference is below this VM's observed run variation. These measurements establish neither zero overhead nor a speedup. Absent-sensor pixel hashes match the baseline in every run, and fixture hashes are stable. Fixture allocation includes the illustrative sensor records; it is not an isolated measurement of CPU service polling.

Four alternating native resource-sampler pairs used 30 warm-up and 200 back-to-back reads with zero process rows. Median time was 1.750 ms before and 1.801 ms after; run medians ranged 1.627–1.959 ms and 1.627–2.880 ms. Median allocation was 222,664 versus 219,892 bytes/sample, with overlapping ranges (212,984–222,664 and 213,080–222,760). Allocations vary between runs; the lower median does not establish an allocation improvement. Sampling consumes a cached optional CPU reading and never waits for the service. The harness excludes real one-second scheduling, service polling, supported-device acquisition and whole-app/render-thread cost.

The exact CPU-only LibreHardwareMonitorLib 0.9.6 provider also completed six two-second observations with registered PawnIO 2.1.0.0. Every reading was unavailable (probe exit 4). Open took 242.8 ms; updates ranged 4.3–53.8 ms (mean 24.9 ms), working set 32.8–37.4 MiB, and process CPU deltas were below timer resolution. These are diagnostic observations of this unavailable-sensor VM path, not physical AMD/Intel accuracy, supported-hardware cost, or a LocalSystem service measurement. No service or driver was installed by the checks.

Raw evidence: `artifacts/cpu-service/performance/`, `performance-repeat/`, `provider-native.log` and `verification.md`. The UI/sampler measurements cover the final functional code; the subsequent content-only capture fallback correction runs only in diagnostics. See [the hardware sensor design](design/HARDWARE-SENSORS.md). Physical accuracy, HVCI/coexistence and signed installation acceptance remain separate gates.

## Built-in temperatures and performance limits (November 25, 2026)

Compared the preserved pre-change Release benchmark with the final desktop build after tests and smokes stopped. Four pairs alternated launch order on the development VM, using the existing 850-process live-frame benchmark (400 warm-up frames, seven batches of 60). Four additional changed-build runs enabled `--temperature`, with a paired GPU temperature, a thermal zone and a confirmed CPU performance limit.

| Live UI frame | Before | After, sensors absent | After, temperature fixture |
| --- | ---: | ---: | ---: |
| Median of four run medians | 4.599 ms | 4.646 ms | 4.315 ms |
| Run median range | 4.022–4.954 ms | 4.207–4.797 ms | 3.949–4.684 ms |
| Median allocated bytes/frame | 560,421 | 560,973 | 561,021 |

Absent-sensor pixel hashes match the baseline in every run. Temperature-fixture hashes are stable across all four runs. The small timing difference is within the overlapping run variation; the lower fixture median is not evidence of a speedup. Added nullable sensor presentation and marker work costs about 552–600 managed bytes per synthetic frame (roughly 0.1%).

A separate native resource-sampler harness alternated the same four build pairs, with 30 warm-up and 200 timed back-to-back reads per run. Median sampling time was 1.401 ms before and 1.416 ms after; run medians were 1.382–1.408 ms and 1.407–1.561 ms. Median allocation rose from 201,648 to 204,030 bytes/sample (changed range 203,304–208,144), consistent with the additional optional counter results and thermal-reading state in the shared PDH query. No additional PDH query or sampling loop was added. This VM returned no thermal zones or GPU temperatures, so native measurements cover graceful absence, not supported-device acquisition cost, adapter wake behavior or the optional CPU library.

The native harness uses reflection and zero process rows; the UI benchmark excludes collection and the WPF render thread. These are focused measurements, not whole-app profiling. Evidence and helper scripts: `artifacts/temperature-implementation/performance/summary.json`, the individual JSON/PNG files, `Measure-Final.ps1` and `sampler/`. The CPU LibreHardwareMonitor probe remains separate and its service has not been implemented or benchmarked. See [the temperature design](design/TEMPERATURE.md).

## Effective CPU clock (November 25, 2026)

The CPU clock now comes from `\Processor Information(_Total)\% Processor Performance`, added to the existing memory-pressure PDH query, instead of `CurrentMhz` alone. Compared the `HEAD` and changed `MonCore.Windows` Release builds with a focused probe of `WindowsResourceSampler.Sample` (the resource half of each tick). The probe used 850 synthetic process rows, 30 warm-up samples and 200 timed back-to-back samples per run. Six pairs alternated launch order on the Hyper-V development VM (24 logical processors, Ryzen 9 9950X3D host), after verification had finished.

| Resource sampler | Before | After |
| --- | ---: | ---: |
| Median of six run medians | 1.392 ms | 1.877 ms |
| Run median range | 1.336–1.425 ms | 1.687–2.273 ms |
| Allocated bytes/sample | 178,549 | 178,549 |
| Clock reported | 4,300 MHz, fixed | null first, then 5,159–5,543 MHz |

The cost is real and explained: to produce `_Total`, the Processor Information provider gathers data for every logical processor. Interleaved collects of the memory query with and without the counter took 0.015 ms and 0.27–0.32 ms in three quiet runs, and up to 1.3 ms in an earlier run while other builds loaded the machine. A single-processor instance costs about 0.01 ms but measures one core, so it was rejected. At one sample per second this is roughly 0.5 ms of sampling-worker time per second, off the UI thread, with no added allocation, query or process open. It scales with logical processor count. Whole-app CPU was not profiled; the change is well below that measurement's noise. Evidence: `artifacts/effective-cpu-clock/results/*.json` and the probes in `artifacts/effective-cpu-clock/probe/` in the main checkout. See [the design note](design/EFFECTIVE-CPU-CLOCK.md).

## Crash summaries: bounded History loading (November 25, 2026)

Compared the original reader from `HEAD` with the implemented reader in a Release harness, using the same frozen 30-day local event-log range and alternating order across six pairs. Both returned 500 events, reached the existing History cap, and reported Application Complete / System Unknown. Reads and message formatting stayed on the calling worker thread. Median read time was 1,476.6 ms before and 1,461.2 ms after; this small native-I/O difference is not evidence of a speedup. The first grouping call after each current read added a median 7.78 ms. Read allocations rose from 16,144,908 to 16,366,620 bytes; grouping added 85,832 bytes. The additional 307,544 bytes per capped load (about 1.9%) retain structured signatures, version counts and group members. No sampling-path work was added.

Non-crash events skip structured-field collection. These measurements cover bounded event loading and grouping, not whole-app rendering, collection or long-running heap retention. Native timing depends on the local event log and filesystem cache; the unchanged five-second budget remains the bound. Raw evidence: `artifacts/crap/history-event-xml/crash-summary-native-*.json` and `crash-summary-native-summary.json`; the source-projection harness is in `artifacts/crap/history-event-xml/bench/`. See [the feature note](design/CRASH-SUMMARIES.md).

## Resource gauge centering: cosmetic layout fix (November 25, 2026)

Full-size and compact resource readouts now use symmetric 9/9 horizontal margins instead of 10/8. The 18-DIP total inset, content width, gauge geometry and sampling work are unchanged. This is a constant-only cosmetic layout fix;no algorithm,additional allocation or new I/O was introduced, and no timing benchmark or performance improvement is claimed. Previous performance gates and withdrawn candidates remain unchanged.

The new 48-case geometry matrix records 24 expected full-size failures and 24 compact passes against the original margins, then 48 passes after the fix. Both variants pass 93 focused tests each and repository Verify application/solution plus benchmark builds,with zero warnings/errors. Gauges and compact smokes pass in both variants, 51 and 106 PNGs respectively. Default captures and differing Store captures were visually reviewed; 146 same-name renders are byte-identical. Store native backdrops use the documented content-capture fallback and do not verify its native caption. Full test suites were not rerun. Evidence: artifacts/gauge-centering-fix/;design: docs/design/GAUGE-CENTERING.md.

## Remaining CRAP feasibility audit: no production change (November 25, 2026)

Analysis only:142 remaining identities joined to inherited whole-method metrics;113 complexity floors exceed30,19 of32 above50 have floors above50. Thirty monitored production copies remain unchanged. No tests, builds, timing samples or performance claims were added. Existing benchmark gates and rejected candidates remain authoritative. Exact threshold bounds are recorded in artifacts/crap/remaining-feasibility-2026-11-25/current-audit.json and method-audit.json; the bounds are not evidence of reachable safe tests or behavioral equivalence. See docs/CRAP-REMAINING-FEASIBILITY-2026-11-25.md.

## Missing setup-bundle guard: unchanged production (November 25, 2026)

Six new shared cases plus12 existing reply cases pass18 in eachof4 desktop/CLI Default/Store runs,72 executions,zero failures/skips. Four final narrow test builds have zero warnings/errors. All30 production copies unchanged;729 prior inputs727 unchanged plus2 test-project Compile-only additions,andone new shared file730 current. CompiledApp226→227D/225→226S,CLI44→45D/43→44S,zero changed/one added document each;eight PDB hashes match. Both RunAsync copies930→894.48/CC30/1.3line3.3branch remainabove50. Compiled metadata omits allsignedassets;onlyguardline executes,no verify/starttrace/package/pipe/elevation/service work. Five whole fresh observations5873 inherited,no union/global claim. No production edit,benchmark rerun or speedup claim. Normalorchestration andallprevious performance/appearance blockers remain. Evidence: artifacts/crap/broker-unavailable-bundle/verification.json,current-audit.json and compiled-bundle-observations.json.

## Service process selection lifetimes: unchanged production (November 25, 2026)

Nine new synthetic-frame cases plus24 existing refresh/selection cases pass33 Default/33 Store,zero failures/skips. Two final narrow no-incremental test builds have zero warnings/errors. All30 monitored production copies and728 prior inputs unchanged;one fixture added. Original DLL/PDB pairs retained;compiled225→226D/224→225S,zero changed/one added;four PDB hashes match. RefreshActionSelection30.29→28/CC28 reaches100%line/branch in each fresh variant run. Initial29-case observation was incomparable(lineimprovement/branchdecrease),retained but not adopted;expanded related scope yields whole nondowngrading observation,no coverage union. One fresh method5877 inherited. No native service action,real process lookup,production edit,benchmark rerun,speedup claim or visible change. Existing performance/appearance blockers retained. Evidence: artifacts/crap/service-process-selection/verification.json,current-audit.json and worker-only-evidence.

## Native stopped-state and CLI entry characterization: unchanged production (November 25, 2026)

One new native case plus24 guards pass25;five CLI entry cases pass5Default/5Store,zero failures/skips. Three narrow test-only no-incremental builds have zero warnings/errors. All30 monitored production copies and726 prior inputs unchanged;two fixtures added. Original DLL/PDB pairs retained;compiledNative28→29,CLI43→44D/42→43S,zero changed/one added each;eight PDB hashes match. Stop35→23.62/CC8/37.5line12.5branch covers query-only already-stopped idempotence,control source line0hits. CLIentry42→6/CC6/100line50branch covers redirected help/errors;hardware/settingsactions0covered. Two whole fresh observations5876 inherited,no union/global claim. Normal stop/defaultwatch/interactive encoding remain open. No production edit,benchmark rerun or speedup claim. Evidence: artifacts/crap/stop-and-cli-entry/verification.json,entry-and-stop-open-verification.json,current-audit.json.

## Native security guards and query-only waits: unchanged production (November 25, 2026)

Eighteen new cases plus37 existing native guard/pure service cases pass55focused,zero failures/skips. Final test-only no-incremental build has zero warnings/errors. All24 production copies and725 prior inputs unchanged;helper baseline/test-host copy exact. One fixture added,compiled27→28,zero changed/one added;two PDB hashes match. SetSecurity42→6.04/CC6/90line83.3branch;Wait44.3→16.4/CC10/60line50branch;Start42→30.12/CC6 remains>30,pre-cancelonly. Security calls use null/closed/zero handles;waits use query-status-only EventLog/RemoteRegistry handles with same before/after states. No service control or valid-handle permission write;successful security/start/transition/timeout behavior remains open. Five whole fresh observations5873 inherited,no union/global claim. No production edit,benchmark rerun,speedup claim or native-AOT publish. Evidence: artifacts/crap/native-security-invalid-handles/verification.json,permission-and-wait-open-verification.json,current-audit.json.

## Guarded window network initialization: unchanged production (November 25, 2026)

Four new window startup cases plus3 injected controller regression tests pass7 Default/7 Store,zero failures/skips;two final narrow builds have zero warnings/errors. All24 production copies and724 prior inputs unchanged;one fixture added. Original DLL/PDB pairs retained;compiled224→225D/223→224S,zero changed/one added;two PDB hashes match. Default initialization42→6/CC6 reaches100%line/branch;Store focused42→24.43/20line50branch covers suppression only. Enabled real-controller startup is pre-canceled at its linked gate;fresh concrete ConnectAsync/BrokerRunAsync coverage remainszero. No live restore/service mutation/user settings write/sampling/window activation. Four whole Default observations5874 inherited,no cross-variant union/global claim. Normal enabled live restore and Store secondary coverage remain explicit. No production refactor,benchmark rerun,speedup claim or visible change;existing performance/appearance blockers retained. Evidence: artifacts/crap/network-initialization/verification.json,live-restore-open-verification.json,current-audit.json.

## Flight Recorder event-read failure handling: unchanged production (November 25, 2026)

Eight new injected-reader cases plus8 existing event tests pass16 Default/16 Store,zero failures/skips;two final narrow builds have zero warnings/errors. All24 production copies and723 prior inputs unchanged;one fixture added. Original DLL/PDB pairs retained;compiled223→224D/222→223S,zero changed/one added;two PDB hashes match. ReadEventsAsync41.66→28/CC28 reaches100%line85.7%branch;CoverageStatus4/100line100branch. New cases use completed/faulted/canceled tasks and synchronous reentrant refresh callbacks,no sleeps/polling/native event reads. Actual five-second expiry and ignored-cancellation late-fault cleanup remain explicitly open. Two whole fresh observations5876 inherited,no union/global claim. No production refactor,timing/allocations change,benchmark rerun,speedup claim or visible change. Existing performance/appearance blockers remain. Evidence: artifacts/crap/recorder-event-failures/verification.json,deadline-open-verification.json,current-audit.json.

## Capture notification and unavailable report guards: unchanged production (November 25, 2026)

Fourteen new cases plus17 existing tray-status cases pass31 Default/31 Store,zero failures/skips;two final narrow builds have zero warnings/errors. All24 production copies and722 prior inputs unchanged;one fixture added. Original DLL/PDB pairs retained;compiled222→223D/221→222S,zero changed/one added;two PDB hashes match. NotifyCapture42→6/CC6 reaches100%line/branch. OpenCaptureReport42→28.67/CC6 covers only its guard,14.3%line50%branch;success/error shell launching unverified. Hidden windows/icons,owned files,samplingnull,no displayed balloon/click/reportlaunch. Invalidbody rejection after attemptstateadvance preserved separately. Two whole fresh observations5876 inherited,no union/global claim. No production refactor,benchmark rerun,speedup claim or visible change.34 open scopes retain cooldown/display/click/launch gaps;existing performance/appearance blockers remain. Evidence: artifacts/crap/capture-notification-guards/verification.json,current-audit.json and open-verification/edge-observation artifacts.

## CPU Control-arrow navigation: unchanged production (November 25, 2026)

Nineteen new routed-key cases plus44 existing keyboard cases pass63 Default/63 Store,zero failures/skips. Two final test builds have zero warnings/errors. All24 production copies and721 prior inputs unchanged;one fixture added. Original DLL/PDB pairs retained;compiled221→222D/220→221S,zero changed/one added;two PDB hashes match. OnKeyDown44.79→34/CC34 reaches100%line/branch,remains>30floor. Tests use saved/restored calling-thread keyboard table with byte-for-byte restoration assertions,no desktop input/focus/message pumping. WPF Windows-bit observation preserved separately;physical shortcut behavior unverified. Two whole fresh observations5876 inherited,no union/global claim. No production refactor,benchmark rerun,speedup claim or visible change. Existing performance/appearance blockers remain. Evidence: artifacts/crap/cpu-control-navigation/verification.json,platform-observation.json,current-audit.json.

## Flight Recorder trend navigation: unchanged production (November 25, 2026)

Six new button-navigation cases plus34 trend-detail cases pass40 Default/40 Store, zero failures/skips;two final narrow builds have zero warnings/errors. All24 production copies and720 prior inputs unchanged;one test fixture added. Original DLL/PDB pairs preserved;compiled inputs220→221D/219→220S,zero changed/one added;two PDB hashes match. MoveTrend42→6/CC6 reaches100% line/50% branch;defensive nonempty/no-selection flow remains a separate open scope. Four whole fresh observations5874 inherited,no union/global claim. No structural/hot-path production edit,benchmark rerun,speedup claim,native event read or visible product change. Existing performance/appearance blockers remain. Evidence: artifacts/crap/trend-navigation/verification.json,no-selection-open-verification.json,current-audit.json.

## Service expansion clicks: unchanged production (November 25, 2026)

Five new characterization cases plus20 existing refresh cases pass25 Default/25 Store, zero failures/skips. Two final narrow test builds have zero warnings/errors. All24 prior production copies and719 prior inputs remain unchanged; one test file added. Four PDB source hashes match. Preserved pre-properties compiled-input comparison has zero changed entries and two known test-file additions; immediate original test PDB was not archived, so no direct immediate-baseline comparison is claimed. OnExpand42→6/CC6 reaches100% line/branch in both variants. No structural/hot-path edit, benchmark rerun, speedup claim, native service action or visible product change. One whole fresh observation5877 inherited; no coverage union/global claim. Existing performance and appearance blockers remain. Evidence: artifacts/crap/service-expansion-click/verification.json and current-audit.json.

## Native service read/acquisition guards: unchanged production (November 25, 2026)

Three new restricted-token/access-denied cases plus13 pure descriptor/configuration tests pass16focused,zero failures/skips. Three relevant baseline/final builds clean;managed helper IL only,no executable/UAC/native-AOT publish. Existing service staysRunning/DemandStart;no control/create/delete/permission-write call. Incomplete restore usesExists=true/null-descriptor,which cannot reach mutation. All24 prior production DLL copies and718 prior inputs unchanged;fresh helper baseline/test-host copy exact,compiledtest26→27,newtest/helperPDBhashes match. No benchmark rerun or timing improvement claim. CaptureState72→35/CC8 meets revised50 but remains>30 at25%line0%branch;Restore72→29.24/CC8 has30.8%line12.5%branch. Both successes and security/start/stop restoration remain unverified in an explicit new scope. Five whole nondowngrading observations5873inherited,no union/globalcoverage. Evidence: artifacts/crap/native-service-read-access-denied/verification.json,state-and-restore-open-verification.json,service snapshots and current-audit.json.

## Network probe worker entry: unchanged production (November 25, 2026)

Eight new worker-entry cases plus three existing guards pass11focused tests;three narrow baseline/final builds clean. All24 prior production copies and717 prior inputs remain unchanged;new current tool baseline remains byte-identical and its test-host copy matches. One test file added,compiledtest9→10,4PDBsourcehashes match. No timing benchmark rerun or speedup claim. Tests use owned loopback sockets,one131072-byte shortTCP scenario and scoped nonparallel console streams;noETW/elevation/collector child or sustainedload. Main1944.91→1710.77/CC50,12.7%line30%branch remains above50;one whole nondowngrading observation5877inherited,no union/globalcoverage. No remediation count increase. Evidence: artifacts/crap/network-probe-worker-entry/verification.json,trace-capture-gap.json and current-audit.json.

## Process properties read lifecycle: unchanged production (November 25, 2026)

Twenty new cases use the existing reader delegate;22 focused Default/22 Store pass,zero failures/skips. Two final test builds clean,all24 production DLL copies identical,all716 prior inputs unchanged plus one test file. No new performance timing is required or improvement claimed. Controlled completion signals/UI barriers verify pending,ready and post-close behavior without incidental waits;default/null-task fallback reads PID zero only,no positive process metadata or clipboard access. ReadAsync45.04→8/CC8 reaches100%line/branch;constructor branch75→100. Five complete nondowngrading observations selected,5873 inherited,no union/global coverage. Pending/ready PNGs match across variants andDefaultbothviewed;local test brushes/native title chrome/full appearance limits retained. Discarded faulted/canceled read-task handling remains a separate open scope despite complete line/branch coverage. Evidence: artifacts/crap/process-properties-read/verification.json,read-failure-open-verification.json and current-audit.json.

## Usage reset and sampling guards: unchanged production (November 25, 2026)

Tests-only batch:15 new cases,final52 focused Default/52 Store pass;two final affected test builds zero warnings/errors. All24 monitored production DLL copies remain byte-identical and all715 prior inputs unchanged,with one test source added. No timing benchmark rerun is needed,and test durations are not production performance evidence. Actual owned WPF confirmation tests cover clear/cancel/close,unchanged history and visible-row refresh/resumed recording;Default/Store PNGs match andDefault was viewed. Windows sampler never starts. ResetUsage42→6/CC6 reaches100%line/branch;OnSamplesRecorded42→10.5/CC6 reaches50%line83.3%branch. Live healthy readiness and real updater transaction cleanup remain unexecuted in a new open scope;no full appearance or native-startup claim. Three complete nondowngrading observations selected,one incomparable MessageDialog.Show retained,no coverage union or fresh global coverage. Evidence: artifacts/crap/usage-reset-sampling-guards/verification.json,live-ready-open-verification.json and current-audit.json.

## Constructor-supplied dump I/O: withdrawn (November 25, 2026)

A constructor-supplied two-operation boundary replaced the previously rejected ambient-registry approach;the existing constructor retained direct native defaults. A cached static closing handler saved56 bytes/construction after accounting for the captured I/O reference. Original80 and candidate99 focused cases per variant pass;19 new cases are archived. Candidate callback12/CC12/100%line91.7%branch was not adopted.

Eight frozen pairs/sixteen fresh workers,four scenarios,500ms warmup/seven batches,tiering off. Source mirrors substitute the same two native endpoints;native chooser/dump performance is not measured. All98 inputs and448 latency/allocation samples verify. No owned verification overlaps timing;no timed retries/exclusions/expansion or relaxed gate. Constructor upper95=1.1008680021949735 exceeds fixed1.10;other scenarios pass. No lower95 latency/allocation bound exceeds1,so this is inconclusive constructor latency rather than a statistically detected slowdown.

| Scenario | Original µs | Candidate µs | Latency ratio 95% CI | Allocated bytes/cycle |
|---|---:|---:|---:|---:|
| cancel | 1.895 | 1.559 | 0.740232–1.052273 | 2088.00 → 2088.00 |
| constructor | 141.121 | 129.168 | 0.859740–1.100868 | 18672.00 → 18616.00 |
| ineligible | 1.082 | 1.030 | 0.840885–1.029973 | 112.00 → 112.00 |
| write | 278.373 | 271.453 | 0.879520–1.066025 | 5606.60 → 5604.76 |

Four reparented content images match byte-for-byte;completed-write image visually inspected. These do not establish native-popup/full appearance acceptance. All715 prior source inputs,24 production DLL copies and four app/test hashes restored exactly;original80-per-variant results reused. Four candidate affected builds,two final workers and four restoration builds clean. Final callback remains79.48/CC10 at11.4%line/30%branch. Both dump boundary candidates are parked;accepted inventory remains native-history-yield. Evidence: artifacts/crap/dump-dialog-constructor-io/performance-summary.json,performance-raw-verification.json,withdrawal.json and verification.json.

## Process menu allocation/control candidate: withdrawn (November 25, 2026)

Array removal plus navigation/control composition reduced current-thread allocations128bytes/base menu and96bytes/other menu. Eight fixed pairs/sixteen fresh workers,six scenarios,500ms warmup and seven batches, tiering off;445 frozen inputs,672 latency and allocation samples verify,matching recursive menu/callback outcomes. No timed retries/exclusions/expansion or owned build/test/coverage overlap. All six latency upper95 ratios exceeded the predeclared1.10 gate; no lower95 exceeded1. This is inconclusive latency evidence, not a statistically detected slowdown. The candidate was withdrawn without relaxing the gate. The generic summarizer exception is clarified by withdrawal.json. No cause is assigned to the wide intervals.

| Scenario | Original µs/menu | Candidate µs/menu | Latency ratio 95% CI | Bytes saved/menu |
|---|---:|---:|---:|---:|
| base | 271.54 | 293.94 | 0.824–1.278 | 128 |
| external | 718.45 | 688.65 | 0.775–1.144 | 96 |
| historical | 666.49 | 733.84 | 0.838–1.430 | 96 |
| live | 668.19 | 690.94 | 0.742–1.191 | 96 |
| owned | 753.07 | 751.44 | 0.693–1.440 | 96 |
| priority | 538.08 | 590.29 | 0.734–1.478 | 96 |

Original and candidate271 focused cases each Default/Store pass;all12 rendered original/candidate menu PNGs match byte-for-byte and Store matches. Ten candidate/worker/restoration builds clean. Restored715 source inputs,24 production DLL copies and four app/test hashes match originals exactly; original passing test evidence is reused. Final CreateMoreMenu remains68/CC68 at100%line/branch;candidate14/6/42 scores are not adopted. Isolated warm-menu CPU/current-thread allocation evidence excludes popup painting,native process actions and full application appearance. Evidence: artifacts/crap/process-action-menu-allocation/performance-raw-verification.json,performance-summary.json,withdrawal.json,verification.json.

## Native history yield characterization: unchanged production (November 25, 2026)

Three new integration cases exercise an existing curated Application event at exact inclusive boundaries, cancellation after yield and early-disposal/re-enumeration. Focused history/XML/message tests pass131 Default and131 Store, zero failures/skips; two affected test-project builds have zero warnings/errors. Production reader source and all24 recorded DLL copies remain byte-identical; four new test-host dependency copies match canonical assemblies. All714 prior inputs unchanged plus one test file. No performance benchmark rerun is required for this tests-only batch, and elapsed test durations are not performance measurements. ReadLog CRAP82.50→23.23 with CC22 unchanged; collateral RenderXml42→13.78 with CC6 unchanged. Native error/timeout/malformed-handle paths and exact leak counts remain outside this evidence. No log writes/clears, event-source creation or forced workloads. Evidence: artifacts/crap/native-history-yield/verification.json, passing variant TRX/coverage, PDB proofs and current-audit.json.

## Main diagnostic extraction: complete workflow comparison (November 25, 2026)

Main coordinator complexity108→8/CRAP136.08Default and130.08Store→8.00;seven cohesive helpers retain original waits,stall,measurements,IO/event order and failure behavior. Ten unchanged cases pass per variant;four affected app/test and two worker builds clean. Four frozen pairs/eight fresh workers,one warmup/one timed complete asserted workflow each,tiering off;1472 hashes verify. Median64280.945→63398.127ms;latency ratio0.985301,95% CI0.963874–1.007205;allocation median1986235684→1985065136bytes,95% ratio CI0.998266–1.000856. The unchanged fixed gate passed (reject latency lower95>1,allocation lower95>1 or latency upper95>1.10),with no timed retries,exclusions or expansion. Initial/snapshot/hidden/minimized waits,3.5-second UI stall+1.5-second settling,15-second quiet observation,all nested workflows,rendering/settings/report IO and assertions remain included. Progress output is outside timing. Small changes inside the intervals are unresolved;no broad performance or native/full appearance claim. Synthetic queued frames and real read-only hardware/native frame integration retain their documented limits. No owned verification overlaps timing;external MSBuild/binlog MCP process observations retained. All19 affected reported methods meet30,but secondary line/branch gaps remain explicit. Evidence: `artifacts/crap/main-scenarios-extraction/performance-protocol.json`,raw workers, `performance-summary.json` and `current-audit.json`.

## Disk scenario extraction: complete changed-path comparison (November 25, 2026)

Disk coordinator complexity52→10/CRAP67.92 canonical (55.71 after direct characterization)→10.08;five cohesive helpers retain original target/error/IO/restoration behavior. Eleven original/candidate cases pass per variant,and one complete Default smoke caller passes. Four affected app/test and two worker builds are clean. Four frozen pairs/eight fresh workers,one warmup/one timed complete disk workflow each,tiering off;1471 hashes verify. Median6592.284→6568.667ms;latency ratio0.999315,95% CI0.991664–1.007024;allocation median47732552→47790596bytes,95% ratio CI0.998806–1.003118. The unchanged gate passed (reject latency lower95>1,allocation lower95>1 or latency upper95>1.10),without timed retries/exclusions/expansion. The modified disk path includes all original waits,owned dialogs,read-only drive discovery,PNG/report IO and assertions;unmodified full-smoke stages are verified by the62-second caller test instead of repeated in every timing worker. Small regressions within these intervals remain unresolved. No owned build/test/profile job overlaps timing;longstanding external MSBuild/binlog MCP processes are recorded without a machine-idle claim. Synthetic target frames do not establish actual disk-counter performance or full appearance. All12 affected methods meet30;branch/series-validation line gaps remain explicit. Evidence: `artifacts/crap/disk-scenarios-extraction/performance-protocol.json`,raw workers, `performance-summary.json` and `current-audit.json`.

## Main smoke characterization: unchanged production (November 25, 2026)

Original full smoke passes with an independent fake IFrameSource/MonitorSession queue bridged into explicit window records and refreshes;the window's Windows sampler never starts. The original8/2-second snapshot waits,hidden/minimized waits,3.5-second UI stall and15-second quiet observation remain unchanged. Default reuses its exact62-second full canary and adds nine fresh35-second boundary cases;Store's combined ten cases pass in95 seconds. Both final test builds clean,all24 production DLL copies identical. No timing benchmark rerun or improvement claim. These elapsed test durations are not before/after production performance measurements. Native counters/full appearance remain outside this synthetic orchestration proof;read-only hardware info/native frame integration still run. MainCRAP136.08Default/130.08Store anddisk67.92 retain complexity floors108/52 above50. Evidence: `artifacts/crap/diagnostic-main-workflow/verification.json`, passing variant TRX/coverage and `current-audit.json`.

## Feature scenario extraction: fixed full-workflow comparison (November 25, 2026)

Feature parent complexity134→4/CRAP134.56→4.00;nine cohesive helpers retain guards,waits,IO/event order and dialog/menu/preference finally. Thirteen unchanged cases pass per variant;four final affected builds/two final workers clean. Four frozen pairs/eight workers,one warmup/one timed complete asserted workflow each,tiering off;1468 hashes verify. Median15477.067→15466.541ms;latency ratio1.004090,95% CI0.941768–1.070536;allocation median1379802928→1379178308bytes,95% ratio CI0.996716–1.002529. Fixed gate passed (reject latency lower95>1,allocation lower95>1 or latency upper95>1.10),without timed retries/exclusions/expansion. The workload includes live read-only hardware queries/native frame capture and all PNG/settings/report operations. Small changes within these intervals remain unresolved;no native/full appearance or broad performance claim.

Two untimed worker-host failures are retained:neutral System.Management facade during warmup,then an ineffective preload. No timed sample existed. Corrected artifact-only workers reference the existing Windows10.0.15 runtime DLL and exclude the neutral facade;SHA256/MVID/path match the NUnit Windows asset. Both passed untimed full checks before the unchanged frozen protocol. No package installation or production manifest edit. Longstanding external MSBuild/binlog MCP processes remained present;no owned verification job overlapped timing. Process-menu helper38.25 remains above30,all fresh responsibilities meet50. Evidence: `artifacts/crap/feature-scenarios-extraction/performance-protocol.json`, `windows-runtime-provenance.json`, raw worker outputs, `performance-summary.json` and `current-audit.json`.

## Feature workflow characterization: unchanged production (November 25, 2026)

Thirteen original-implementation feature workflow cases pass per Default/Store;both final affected test builds are clean. All24 tracked production DLL copies remain identical,with only a test source added. No timing rerun or performance improvement is claimed. Complete owned UI/report/settings orchestration now has a passing characterization workload;read-only live Windows hardware queries and native background-window capture remain integration/environment dependencies. Future paired measurements must include this variability rather than claiming an isolated hardware unit benchmark. ParentCRAP17902.31→134.56,CC134 unchanged;still above50. Evidence: `artifacts/crap/diagnostic-feature-workflow/verification.json`, passing TRX/coverage and `current-audit.json`.

## Compact scenario extraction: fixed full-workflow comparison (November 25, 2026)

Compact parent complexity116→4/CRAP123.32→4.00;ten cohesive helpers/promoted local functions preserve original statements, output order, real recovery waits and fixture lifetime. Six cases pass unchanged per variant, all affected/worker builds clean. Four pairs/eight fresh workers,one warmup plus one timed complete asserted workflow each,tiering off;1466 frozen source/runtime/worker inputs verify. No timed retries, exclusions or adaptive expansion. Median9730.639→9545.873ms;latency ratio0.982431,95% CI0.951409–1.014464;allocation median441810332→441822288bytes,95% ratio CI0.999930–1.000100. Fixed gate passed (reject latency lower95>1,allocation lower95>1 or latency upper95>1.10). No owned builds/tests/profilers overlap timing. Longstanding external MSBuild/binlog MCP processes remained present;observations retained, not a machine-wide idle claim. Intervals limit conclusions about small regressions;this is synthetic compact diagnostics, not native sampling/full appearance proof. Native-peak helper36.07 remains above30, all fresh methods are at most50. Evidence: `artifacts/crap/compact-scenarios-extraction/performance-protocol.json`, `performance-inputs.json`, raw worker outputs, `performance-summary.json` and `current-audit.json`.

## Compact diagnostic characterization: unchanged production (November 25, 2026)

Six original-implementation compact workflow cases pass per Default/Store, including actual position-recovery waits and owned preference/PNG/JSON operations. Both affected test builds are clean. All24 tracked production DLL copies remain byte-identical;only a test source was added. No new timing run or performance improvement is claimed. The complete synthetic workload is now available for a fixed paired latency/allocation check before accepting structural changes. Native sampling and full appearance acceptance remain outside these tests. Evidence: `artifacts/crap/diagnostic-compact-workflow/verification.json` and passing variant TRX/build logs.

## App diagnostic-host characterization: unchanged production (November 25, 2026)

Twelve new isolated diagnostic lifecycle cases plus seven unchanged exit cases pass in each Default/Store variant; four final affected host/test no-incremental builds are warning-free. App.RunDiagnosticsAsync210→18.40/CC14 and RunServicesDiagnosticsAsync72→18.17/CC8 through output/report failure cleanup and shutdown tests. Success/native-workload branches remain uncovered;line71.8%/45.8%,branch100%/0% are explicit limitations. Child app MVID/path matches the parent, while product startup/logging/activation/mutex ownership/sampling/settings writes remain absent.

Production source and primary/test-reference DLLs remain identical.22 tracked copies are unchanged and2 stale host WinMon.dll references are refreshed to current tested bytes;12 PDB source hashes and706 source/project/resource inputs verify only test-host routing/two added test files. Existing212 test source/resource entries are preserved. No production timing benchmark was required or speedup claimed. Canonical5849/163>30/39>50/124band,counter175;7 fresh whole observations/5842 inherited,no union or fresh global coverage. Reporting failure replacing the original task exception is characterized as existing behavior, not silently fixed. Evidence: `artifacts/crap/app-diagnostic-host/verification.json` and focused final results/metrics.

## Auto-capture scenario extraction: fixed comparison passed (November 25, 2026)

Four fixed paired comparisons/eight fresh workers, one warmup and one timed complete AutoCapture workflow per worker, tiering disabled. Median 50103.60→49852.14 ms; paired latency ratio 0.996933, 95% CI 0.990219–1.003692. Managed allocation medians 240008056→239974484 bytes, paired ratio 95% CI 0.999232–1.000371. The unchanged gate passed: no detected latency/allocation increase and latency upper95≤1.10. No timed retries, discarded samples, adaptive expansion or gate relaxation. This Default UI/filesystem macrobenchmark has explicit uncertainty; it does not prove zero regression in every workload or Store performance.

Parent145.53 Default/263.49 Store/CC126→6.00/CC6 in both variants; eight cohesive scenarios and a stable promoted toolbar guard. All21 Default affected methods≤30,all21 Store≤50; unsupported Store busy helper42/0% remains a separate original30 finding. Fresh28 Default/27 Store candidate cases pass, including complete/early/recorder failures, late settings/cancellation/final-JSON failures and19 geometry cases; all assertions preserved. Original baseline22/21 fresh plus6 reused workflows per variant has explicit provenance. Four final affected app/test builds and both harness builds are warning-free. Source scope704 inputs: parent changed/helper added/test binding changed;162 app/212 test source-resource entries,10 PDB hashes and1851 frozen inputs verify, with original/candidate app/test/snapshot/benchmark links. Canonical5849/165>30/41>50/124band,counter173;21 fresh observations/5828 inherited,eight Store findings/29 open scopes. No coverage union or fresh global coverage. Guard coverage gaps, native foreground fallback, minimal styles and inherited theme blocker remain;no full appearance acceptance. Evidence: `artifacts/crap/auto-capture-scenarios-extraction/performance-summary.json`, protocol,freeze,provenance and verification files.

## Auto-capture late failure characterization: unchanged production (November 25, 2026)

Two new test methods add three Default/two Store late-failure cases, including pending synthetic network-save cancellation with controls/status checked before settings close and a final JSON-write failure after source shutdown. Final22 Default/21 Store focused cases pass; both affected no-incremental test builds are warning-free. Six unchanged prior parent workflow cases per variant are reused through explicit source/PDB/runtime hash links. All24 production DLL copies remain identical;703 recorded source/project/resource inputs differ only in the characterization file, with212 compiled test source/resource entries and ten PDB source hashes verified. No timing benchmark was required or speedup claimed.

Prior complete-workflow parent CRAP145.53 Default/263.49 Store,CC126, remains the retained observation. Deliberate failure-only runs measure150.83/282.21 because they omit two normal completion points; no coverage union or numeric regression/remediation is inferred for unchanged production bytes. Canonical5841/166>30/42>50/124band,counter172 is unchanged. Parent structural work still requires preserved ownership/order and a frozen comparative performance gate. Evidence: `artifacts/crap/auto-capture-late-failures/verification.json`, `reused-workflow-evidence.json` and final focused results/metrics.

## Flight recorder scenario extraction: fixed comparison passed (November 25, 2026)

Four fixed paired comparisons/eight fresh workers, one warmup and one timed complete AutoCapture workflow per worker, tiering disabled. Median 50025.02→50128.97 ms; paired latency ratio 1.001624, 95% CI 0.993459–1.009856. Managed allocation medians 239990756→239910548 bytes, paired ratio 95% CI 0.998302–1.000950. The unchanged gate passed: no detected latency/allocation increase and latency upper95≤1.10. No timed retries, discarded samples, adaptive expansion or gate relaxation. This is a Default UI/filesystem macrobenchmark with explicit uncertainty, not proof of zero regression in every workload or Store performance.

Parent209.31/CC168→6.00/CC6; six cohesive scenario methods plus a named comparison builder, all20 affected current methods≤50 in both variants. Four helpers remain30–50 and secondary guard coverage limitations are retained. All14 original/final cases pass per variant, including three image-failure cleanup cases and eight comparison boundaries; all assertions preserved. Four final affected app/test builds and both harness builds are warning-free. Source scope703 inputs: parent changed/helper added/test binding changed;161 app/212 test source-resource entries,10 PDB hashes, original/candidate app/test/snapshot/benchmark links and1848 frozen inputs verify. Canonical5841/166>30/42>50/124band,counter172;20 fresh observations/5821 inherited,no coverage union or fresh global coverage. Minimal styles/native foreground fallback and inherited theme blocker remain;no full appearance acceptance. Evidence: `artifacts/crap/flight-recorder-extraction/performance-summary.json`, protocol,freeze,provenance and verification files.

## Flight recorder failure characterization: unchanged production (November 24, 2026)

Three added failure-cleanup cases lock native and content-fallback image outputs at populated, empty and lifecycle recorder stages. All14 focused cases pass in each Default/Store variant, including unchanged complete workflow/cancellation/toolbar and eight comparison-boundary cases. Both final no-incremental test builds have zero warnings/errors. All24 tracked production DLL copies remain identical;702 recorded source/project/resource inputs differ only in the characterization file, and eight PDB source hashes verify. No performance timing was needed for this tests-only change and no speedup is claimed. CaptureFlightRecorderAsync remains209.31/CC168 with88.6% line/54.2% branch coverage; an extraction has not been accepted. Future structural work still requires the fixed performance gate. Initial single-path lock failures are preserved separately; only corrected dual-path passing runs are acceptance evidence. Evidence: `artifacts/crap/flight-recorder-scenarios/characterization-verification.json` and focused original metrics/results.

## Shutdown transitions: unchanged production (November 24, 2026)

Five new test methods add seven Default/three Store cases for owned update-close commit/cancel/retry/cancellation order and normal sampling/preference-task failures. All23 Default/19 Store cases pass; both final affected no-incremental test builds are warning-free. Existing16 cases/helpers are preserved exactly. All212 test source/resource inputs differ only in the shutdown characterization file. Production source and all24 tracked production DLL copies remain identical, with six production/two test PDB source hashes verified; no timing benchmark was run and no performance improvement is claimed.

OnClosing Default96.86→40,CC40 unchanged,73/73 lines100%,29/40 branches72.5%. Store96.86→63.99,55/73 lines75.3%,26/40 branches65%; Default-only synthetic post-ready update cases are excluded under Store policy, and its remaining>50 finding is retained. Owned GUID-named events and temporary files cover coordination, not real updater launch/authentication/install. Null tray/hotkey and suspension-dialog paths remain unforced; no full equivalence claim. Inventory5834 methods/163>30/43>50/120band; six fresh whole observations/5828 inherited, counter171,29 scopes, seven Store findings and inherited theme blocker remain. Evidence: `artifacts/crap/shutdown-close-transitions/verification.json`, `current-audit.json`, original-test preservation/input/source proofs and final results.

## Connections scenarios: isolated current-dependency comparison passed (November 24, 2026)

Both original and candidate use the independently updated WER-fix dependencies in isolated artifact roots.91 focused Connections cases pass per variant before/after; four final affected no-incremental app/test builds have zero warnings/errors. Source scope is exactly the parent/helper and three private test bindings; all456 dependency/source inputs are stable, with16 PDB hashes and explicit app/test/snapshot/benchmark DLL links. Original sandbox audit-feed failures were resolved outside the sandbox with audit enabled, without warning suppression. The earlier shared-output experiment below remains archived and is not acceptance evidence.

Fixed four alternating pairs/eight fresh .NET10.0.15 workers, tiering disabled, one warmup and one timed complete23-capture native/UI invocation per worker. The unchanged complete characterization test includes owned TCP/UDP/native-table reads,121 synthetic samples, reports/copy-event sink/navigation/quality/pins and owned cleanup; optional artifact retention is disabled equally. Median24,778.14025→24,930.66420ms; geometric ratio1.0079984308, paired t95(df3) interval0.9940294609–1.0221637050. Median total allocation184,207,748→183,847,792bytes; allocation interval0.9936805422–1.0013736620. The unchanged latency/allocation gate passes. The median latency increase and four-pair uncertainty remain explicit; intervals include1, so no speedup or zero-regression proof. No timed retries, discarded samples or adaptive expansion.

All901 frozen inputs verify. Four isolated WinMon copies change;20 other isolated production copies match. Parent164/CC164→24/CC24, all nine Default helpers≤50; setup40/traffic34 retain original>30 findings. Store setup exact50.00145794 remains>50 despite rounded50.00 because Store skips desktop-only checks; no metric-only split or unsupported-path bypass. Several helper branch targets remain unmet.23 PNGs per variant decode/reopen, with native-caption/content/minimal-style and native-clipboard limitations. The early listener cleanup gap is unchanged.

Current mixed inventory5834 methods/163>30/44>50/119band;39 fresh whole observations/5795 inherited. An additional16 existing shutdown cases per variant refresh the independently changed OnClosing record to96.86/CC40,49/73 lines,25/40 branches, without further production edits. Counter171,29 scopes, six Store findings and the inherited theme blocker remain. All212 test inputs differ only in three binding files; raw IL8416→8421/6270 differing keys is not equivalence evidence. Evidence: `artifacts/crap/connections-scenarios-rebased/performance-protocol.json`, `performance-freeze.json`, all eight workers, `performance-summary.json`, source/provenance/IL/block proofs, `shutdown-observations.json`, `verification.json` and `current-audit.json`.

## Connections comparison: numerical gate passed, acceptance withheld (November 24, 2026)

This earlier experiment is superseded by the isolated rebased comparison above; its raw observations are preserved and were not used for acceptance.

The pending candidate separates five UI scenarios and promotes four existing local helpers while preserving native acquisition/lifetimes/listener-stop and fixture-close cleanup.91 focused cases pass against both original and candidate per Default/Store; four affected builds are warning-free. Parent164/CC164→24/CC24, all nine Default helpers≤50. Store setup's rounded50.00 is exact50.00145794 and remains an above50 finding; no metric-only split or unsupported-path bypass. Three test files change private bindings only, with exact assertion/source reversal; raw test IL differs and is not equivalence proof.

Four alternating pairs/eight fresh .NET10.0.15 workers, tiering disabled, one warmup/one timed complete native23-capture workflow per worker. Median25,470.90015→25,278.31180ms; paired geometric ratio0.9953410305, t95(df3) interval0.9649782821–1.0266591336. Median total allocation184,618,108→184,255,296bytes; allocation interval0.9949926941–1.0011963322. No timed retries, discarded raw samples, adaptive expansion or gate changes. Numerical gates pass; speedup/zero regression are not established.

Adoption detects five shared-output DLL changes among510 frozen inputs. Benchmark snapshots and selected sources remain unchanged, but independent WER-fix Default/Store full verification/smoke overlaps and replaces outputs. Current binaries differ from the measured snapshot in `MainWindow.xaml.cs`; current tests also differ in `NormalShutdownCharacterizationTests.cs`. Preserve that unrelated work and the running processes. This experiment is retained as historical evidence and is ineligible for current-workspace acceptance. Canonical inventory remains5829/162>30/45>50; counter170, five Store findings and inherited blockers are unchanged. Next establish the current dependency baseline in isolated outputs after the live runs finish; do not replay the identical frozen experiment. Evidence: `artifacts/crap/connections-scenarios-extraction/pending-verification.json`, drift/input/process observations, `performance-summary.json`, protocol/manifest and eight raw workers.

## Chart scenario extraction: fixed comparison passed (November 24, 2026)

Three private helpers separate themed help/menu capture, chart control/disclosure/accessibility and each chart's focus/logical/snapshot checks. Language changes/German legend finally stay in the parent. Literal reconstruction preserves original predicates, event order, messages and cleanup. Parent191.62/CC132→40.35/CC38; helpers26.44/CC26,49.57/CC36 and34.27/CC32 meet50. All18 focused cases pass in Default/Store, including three new accessibility cases verified on restored original builds before the same extraction was reapplied. Four final affected builds have zero warnings/errors. All212 test inputs and8,415 test method IL bodies are identical; only the intended app source and added helper differ.

Eight alternating pairs/16 fresh .NET10.0.15 workers, tiering disabled, one warmup and three timed complete invocations per worker, median within worker. The unchanged test executes all12 content captures, chart/help/menu/focus/German assertions and owned cleanup; optional retained-artifact copying is disabled equally. No overlapping owned builds/tests/profilers, timed retries, sample discards, adaptive expansion or gate changes. Median1,283.65515→1,224.25695ms; paired geometric ratio0.9651440217, t95(df7) interval0.9096293396–1.0240467651. Median total allocation54,784,868→54,143,840bytes; allocation ratio95% interval0.9647087240–1.0160035379. The frozen latency/allocation gate passed. Intervals include1, so there is no established speedup or proof of zero regression; failure paths are functionally tested rather than individually timed.

All504 frozen hashes verify; four WinMon DLL copies change and76 other production copies remain identical.12 PNGs per variant decode/reopen, German legend inspected, with minimal-style/content-only/native-caption limitations. Inventory5829 methods/162>30/45>50/117band;parent/two helpers>30 remain findings,11 fresh whole observations/5818 inherited, seven callbacks moved and three cohesive helpers added. Counter170,29 scopes, five Store findings and the inherited theme blocker remain. Parent branch68.4%/Configure line78.1% secondary targets are unmet. Evidence: `artifacts/crap/chart-scenarios-extraction/performance-protocol.json`, `performance-freeze.json`, all16 worker files, `performance-summary.json`, source/IL/block proofs, `verification.json` and `current-audit.json`.

## Chart workflow characterization: unchanged production (November 24, 2026)

15 original chart-workflow cases pass in each Default/Store; both final affected no-incremental test builds have zero warnings/errors. Seven test methods cover sample/menu guards, logical-reading partitions, cancellation, locked-capture I/O and the full chart/legend/focus/German workflow. All80 production DLL copies are identical, so no timing benchmark was run and no latency/allocation improvement is claimed. Six production/eight test PDB source hashes verify. All209 test source/resource inputs from the accepted Services snapshot are unchanged, with only the Connections and Charts characterization files added.

Default CRAP17,556→191.62,CC132 unchanged; both final variants124/146 lines84.9%,94/132 branches71.2%. The parent remains above50.12 content PNGs per variant decode/reopen; German legend inspected, with minimal-style/native-caption limitations and no full-product appearance claim. Initial missing theme-resource fixture failures are retained; original source unchanged. Inventory5826 methods/160>30/46>50/114band;20 fresh whole observations/5806 inherited, no coverage union, counter170.29 scopes, five Store findings and the inherited theme blocker remain. Evidence: `artifacts/crap/diagnostic-charts-workflow/verification.json`, `current-audit.json`, `test-inputs-vs-services.json`, source/production proofs and focused results. Structural work still requires a frozen before/after comparison.

## Connections workflow characterization: unchanged production (November 24, 2026)

Three original Connections workflow/cancellation/I/O cases pass in each of Default and Store; both final affected test builds have zero warnings/errors. All80 production DLL copies match baseline, so no timing benchmark was run and no latency/allocation improvement is claimed. Six production/eight test PDB source hashes and all210 test inputs verify; the sole added input is the characterization test file. This batch reuses unchanged production evidence rather than repeating full suites or performance timing.

Default parent CRAP27,060→164,CC164 unchanged,433/433 lines and104/164 branches; Store164.91,419/433 lines and99/164 branches. The parent remains above50 and below the70% branch target.23 PNGs/21 content-capture limitation notes per variant; native caption/full appearance and native clipboard behavior remain unverified. The early-startup listener cleanup finding is unchanged and was not runtime reproduced. Inventory5826 methods/161>30/46>50/115band;64 fresh whole observations,5762 inherited, no coverage union. Counter170,29 scopes, five Store findings and the inherited theme blocker remain. Evidence: `artifacts/crap/diagnostic-connections-workflow/verification.json`, `current-audit.json`, production/source/input proofs and corrected test results.

## Services scenario extraction: fixed comparison passed (November 24, 2026)

Seven cohesive scenario blocks plus three promoted private helpers preserve the original service dataset, predicates/messages, recursion, menu/dialog handling and final close. The parent retains native reads/two process samples, the1100ms interval, sampler using-lifetime, live caller capture, German/English changes and original window finally. ParentCRAP151.30→32.01,CC134→32; all ten helpers≤50, with parent/pin preparation40.72/toolbar validator36 retained>30 findings. All64 workflow/toolbar cases pass per Default/Store;57 toolbar cases also passed on the original first. Four affected no-incremental builds pass with zero warnings/errors. Literal block/binding-source proofs and all209 test source/resource entries cover the intended changes; four workflow test method bodies have identical raw IL, but whole test IL is not claimed identical.

Four alternating pairs/eight fresh .NET10.0.15 workers were fixed upfront to bound repeated work for the approximately31-second native/UI workflow. Tiering disabled, one warmup and one timed full invocation per worker. The actual unchanged test executes native SCM and self-process sampling,22 captures, pin/filter/history/dialog/deferred-close/locale assertions and owned cleanup; optional artifact-retention copying is disabled equally. No overlapping owned builds/tests/profilers, timed retries, discarded samples, adaptive expansion or gate changes. Paired log-ratio t95 uses df3=3.182446. Median27,869.3575→28,742.4433ms; geometric ratio1.0284260505,95%CI0.9656451993–1.0952885616. Median total allocation150,703,056→150,713,536bytes; allocation ratio95%CI0.9988159700–1.0007296338. The original lower95/upper95 gates passed. The median increase and relatively wide four-pair uncertainty remain visible; no zero-regression or speedup claim. Failure/boundary paths are functionally tested, not separately timed.

All399 frozen hashes verify after adoption; four WinMon DLL copies change and76 other production copies match baseline.22 captures per variant decode/reopen, with actual relevant styles but otherwise minimal-resource/content-only/native-caption limitations. Inventory5826 methods/164>30/46>50/118band; original toolbar36 finding mapped to its named method, no suppression/coverage union. Counter170,29 scopes, five Store findings and the inherited theme blocker remain. Evidence: `artifacts/crap/services-scenarios-extraction/performance-protocol.json`, `performance-freeze.json`, eight worker files, `performance-summary.json`, `verification.json`, `copied-block-proof.json` and `current-audit.json`.

## Services workflow characterization: unchanged production (November 24, 2026)

Seven original Services cases pass in Default/Store: invalid live-frame partitions, pre-cancellation, locked native report and the full22-capture service/pin/history/dialog/deferred-close workflow. Three later read-only language-state assertions pass in a single full-case recheck per variant; exact archived-source comparison permits reuse of the original passing production coverage. No repeated coverage collection for those assertions. Two latest affected no-incremental test builds have zero warnings/errors.

CRAP18,090→151.30,CC134 unchanged,219/243 lines90.1% and93/134 branches69.4%; the parent remains above50. All80 production DLLs and six production/six test PDB source hashes verify. No production change or timing benchmark, and no speed/allocation improvement claim. Native SCM/process sampling is read-only, with an owned synthetic caller frame and no ongoing sampler loop or service actions; the source's1100ms sampling delay is preserved.22 final PNGs per variant decoded/reopened, German toolbar content inspected; all22 have explicit content-only/native-caption-unverified notes. Actual base/action/title/pin/caption styles correct the test fixture without relaxing source checks; other minimal-resource appearance limits remain.

An initial test-only synchronous-dispatcher wait deadlock was diagnosed from the owned host's stack and corrected using the existing dispatcher pump; only that verified host was stopped. Initial missing pin-template failure and test compile attempts are retained separately. Evidence: `artifacts/crap/diagnostic-services-workflow/verification.json`, `coverage-reuse-proof.json`, `production-before.json`, `current-audit.json`, original/current source proofs and `owned-hang-stop.json`. Next structural work requires a frozen before/after comparison and preservation of the native sampler's original disposal timing, locale effects and window cleanup.47 findings remain above50;29 open scopes, five Store findings and the inherited theme blocker remain.

## Design scenario extraction: fixed comparison passed (November 24, 2026)

The characterized design fixture delegates nine chart/process/disk/snapshot scenarios, preserving construction/seeding/order/messages/output names and existing window retention. Original local layout/containment bodies become named private helpers. ParentCRAP209.03→8,CC190→8; all eleven helpers≤50, with ranking31.32 and snapshot interactions32.59 retained>30 findings. All50 focused cases pass in Default/Store, including47 geometry cases verified on the original first. Four final affected app/test builds pass with zero warnings/errors. Exact block/assertion/source-resource proofs cover the intended production/test-binding changes; three workflow test method bodies have identical raw IL, but whole test IL is not claimed identical.

One frozen comparison:8 alternating pairs/16 fresh .NET10.0.15 workers, tiering disabled, one warmup and three timed full-workflow repetitions per worker, median within worker. The actual unchanged test executes57 captures and chart/filter/disk/ranking/count/snapshot/copy/expiry assertions plus owned cleanup. Optional retained-artifact copying is disabled in both benchmark workers. No concurrent owned builds/tests/profilers, timed retries, discarded samples or gate changes. Median5321.1765→5152.8076ms; paired geometric ratio0.9798904921,95%CI0.9228423727–1.0404652028. Median total allocation322,795,648→322,850,036bytes; allocation ratio95%CI0.9998785827–1.0003973887. The frozen latency/allocation gate passed; no statistically detected regression, with no claim of proven speedup or zero regression. UI/filesystem variance remains; failure/geometry paths are functionally tested rather than independently timed.

All399 frozen hashes verify after adoption. Four WinMon DLL copies change;76 other production copies remain identical.57 PNGs per variant decoded/reopened; actual three button styles/caption and otherwise minimal-resource limitations remain. Inventory5819 methods/163>30/47>50, with two new retained>30 helpers; all29 open scopes, five Store findings and the inherited theme appearance blocker remain. Evidence: `artifacts/crap/design-scenarios-extraction/performance-protocol.json`, `performance-freeze.json`,16 raw worker files, `performance-summary.json`, `copied-block-proof.json`, `all-test-inputs-proof.json`, `verification.json` and `current-audit.json`.

## Design fixture characterization: unchanged production (November 24, 2026)

Three original-workflow tests pass in Default/Store: complete57-PNG chart/process/disk/snapshot workflow, pre-cancellation, and locked first capture. Two final affected no-incremental test builds pass with zero warnings/errors. CRAP36,290→209.03,CC190 unchanged,421/458 lines91.9% and117/190 branches61.6%; the parent remains above50. All80 production DLLs match baseline and six production/two test PDB source hashes match, so no new timing benchmark was run and no speed/allocation improvement is claimed.

The fixture imports the actual three button styles from current `App.xaml`; otherwise minimal styles and native appearance limitations remain. Initial missing-style height failure and dispatcher cancellation-token expectation were corrected in tests without changing production guards. Existing source window retention on completion/cancellation/I/O is characterized and preserved; test-owned cleanup remains separate.57 PNGs per variant decoded/reopened, Light held-snapshot content inspected. Evidence: `artifacts/crap/diagnostic-design/verification.json`, `production-before.json`, `design-window-lifetime.json` and `current-audit.json`. Next structural work requires a fixed before/after performance comparison against this characterized implementation.

## Usage scenario extraction: fixed comparison passed (November 24, 2026)

`CaptureUsageDiagnosticsAsync` now delegates ranking, navigation and archive scopes, with captured-period reports/copy checks in a synchronous helper. ParentCRAP134.46→6,CC116→6; helper scores46.18/31.41/31.41/16.15, all≤50, with three retained>30 findings. The same two characterization tests pass in Default/Store; four final affected builds have zero warnings/errors.8,303 compiled test method bodies are identical; exact block reconstruction and embedded source/resource proofs cover the intended extraction. Four WinMon DLL copies changed;76 other production copies are identical.

One protocol frozen before timing:8 alternating pairs/16 fresh .NET10.0.15 workers, tiering disabled, one full-workflow warmup plus one timed full invocation per worker. Actual unchanged test assertions cover archive recovery, ledger/ranking/navigation/period/day/week, nine captures and cleanup. No concurrent owned builds/tests/profilers, timed retries, discarded samples or changed gates. Median duration11,199.87485→11,471.29350ms; paired geometric ratio1.0171415,95%CI0.9923893–1.0425111. Median total allocated bytes212,194,224→212,166,516; allocation ratio95%CI0.9986929–1.0005955. The frozen latency/allocation gate passed; no statistically detected regression, and no claim of zero regression or improved speed. Macro UI/filesystem/native-fallback variance remains, failure paths are functionally tested but not separately timed, and minimal-style screenshots do not establish full product appearance.

All396 frozen input hashes were verified after the run. An unordered variant-key serialization during verification was restored to the exact frozen hash and made deterministic; no runtime/source input, observation value or protocol changed. Evidence: `artifacts/crap/usage-scenarios-extraction/performance-protocol.json`, `performance-freeze.json`,16 raw worker files, `performance-summary.json`, `observation-order-proof.json` and `verification.json`. Original30 findings,29 open scopes, five Store findings and the inherited theme appearance blocker remain.

## Usage diagnostic characterization: unchanged production (November 24, 2026)

CaptureUsageDiagnosticsAsync13572→134.46 both variants, CC116 unchanged;136/153 lines88.9%,76/116 branches65.5%. Two new Default/two Store cases pass; two affected no-incremental builds0warnings/errors. Original Default baseline build/run reused as final because no source correction followed. Two fast existing automatic-capture helper regressions pass per variant after the shared test fixture gains an operation overload and readonly window predicate; its unchanged long success case was not repeated.

The actual owned workflow exercises archive recovery,120samples/sixapps, live/exited navigation, period/copy/session/day/week reports and capture cleanup. Locked native/content first-output alternatives preserve both sentinels/archive inputs and close the fixture. Nine PNGs per variant decoded/reopened/retained; Dark Usage content inspected; all nine source notes per variant mark native caption unverified. Minimal styles are not product appearance acceptance. All80 production DLLs match baseline; six production/four test PDB source hashes match. No production change, timing benchmark or performance/allocation improvement claim.

Four navigation/date callbacks cross30;64 fresh complete observations/5743 inherited without union. The parent remains above50; branch/critical coverage goals unmet. Inventory161>30/49>50; counter170,29 scopes/five Store findings and prior theme/native/withdrawal limits retained. Evidence: `artifacts/crap/diagnostic-usage/verification.json`, `production-before.json`, `helper-regression-verification.json` and `current-audit.json`.

## Automatic-capture and History diagnostics: unchanged production (November 24, 2026)

CaptureAutoCaptureDiagnosticsAsync16002→145.53 Default/263.49 Store, CC126 unchanged; Default200/224 lines93/126 branches, Store178/224 and84/126. Nested CaptureFlightRecorderAsync28392→209.31 both variants, CC168 unchanged;203/229 lines91/168 branches. Three Default/three Store original-workflow cases pass; two final affected no-incremental test builds0warnings/errors. Complete report/marker/card/History/incident/comparison/Settings/shutdown behavior plus pre-cancellation and locked first screenshot are characterized. Source appends a refresh sample121 intentionally; cancellation can complete synchronously after reports trigger. Initial test/observer assumptions corrected, failed evidence archived and not adopted; all three cases retained.

All80 production DLLs match baseline; six production and two test PDB source proofs match. No production change, new dependency or timing benchmark; no performance/allocation improvement claim. The three later read-only window-absence assertions pass in all three cases per variant; exact source comparison verifies unchanged workflow/fixture operations, so earlier passing coverage/images were reused (`coverage-reuse-proof.json`).49 Default/43 Store PNGs decoded/reopened/retained; Dark capture-card content inspected.45/39 explicit source content-fallback notes retain native-caption limitations; other minimal styles are not product appearance acceptance. No source foreground guard bypass or real service/setup action. Complete large methods remain above50; secondary coverage targets partly unmet. Four collateral compiler callbacks cross30;116 fresh complete observations/5691 inherited, one incomparable TrendSupplement entire prior record retained without union. Inventory165>30/49>50;29 scopes/five Store findings/theme/withdrawal limits retained. Evidence: `artifacts/crap/diagnostic-auto-capture/verification.json`, `production-before.json`, raw/selected metrics, retained outputs and `current-audit.json`.

## Gauge centering failure characterization: unchanged production (November 24, 2026)

CaptureGaugeDiagnosticsAsync10100→3448.98, CC100 unchanged;66/216 lines30.6%,24/100 branches24%. One Default/one Store characterization case passes; two final affected no-incremental test builds0warnings/errors. The actual original workflow fails its first Dark/Bowl CPU/value centering check at1000 DIPs before pre-canceled capture, with actual offset>0.5 verified. It seeds120 fake samples and preserves peak/missing-memory/recovery data, unshown fixture lifetime and absent sampler/output. The existing failure is documented, not fixed or bypassed; later workflow and coverage targets remain unverified/unmet. Historical theme appearance blocker remains.

All80 production DLLs and six production/two test PDB source proofs match baseline/current inputs. No production code, layout tolerance or dependency changed. No timing benchmark was run for test-only changes and no performance/allocation improvement is claimed. Five fresh complete observations/5802 inherited, no union/globalcoverage;169>30/49>50 retained. Evidence: `artifacts/crap/diagnostic-gauge-centering/verification.json`, `production-before.json`, `existing-defect.json` and `current-audit.json`.

## Pins scenario extraction withdrawn: latency uncertainty (November 24, 2026)

Candidate separated pin presentation invariants, interactions and divider captures, promoting existing local drawing/drag/tree helpers with explicit inputs. Parent106.14/CC96 would become12/CC12, with new responsibility scores36.85/CC36,45.60/CC38,10.29/CC10. These withdrawn scores are not current improvements. Original Default14 cases passed before editing; candidate14 Default/14 Store pass, four final app/test builds0warnings/errors. All8239 test bodies identical; six copied source bodies preserve statement/read/message order modulo signatures/helper calls/indentation. Across155 original app source/resource entries, only the intended source changed and one helper was added.

Fixed16 alternating pairs/32 fresh workers, one warmup+three timed repetitions per actual unchanged Default workflow, tiering disabled. Inputs/protocol/harness frozen before timings, no build/test/profiler overlap, retries, discarded samples or gate relaxation. Gate rejects latency/allocation lower95>1 or latency upper95>1.10.

| Workload | Before median ms | Candidate median ms | Latency ratio95%CI | Allocation ratio95%CI |
|---|---:|---:|---:|---:|
| Full7PNG/JSON pins/filter/group/snapshot/divider/row-button workflow | 722.601 | 613.247 | 0.83785–1.11433 | 0.999941–1.000258 |
| Missing-template failure/cleanup | 219.630 | 201.555 | 0.80451–1.12256 | 0.999995–1.000011 |

No statistically detected latency/allocation regression; both latency upper bounds fail1.10, so preservation is uncertain and no speedup is claimed. One audit of existing raw pair dispersion attributed no cause and made no new measurements. Original source/all80 DLLs restored exactly; helper archived/removed. Four restoration app/test builds0warnings/errors; three Default/three Store restoration cases pass. Current pins106.14/CC96 and inventory169>30/49>50 retained. No candidate counts promoted; prior scopes/theme/Store limits remain. Do not repeat identical timings or weaken the gate. Evidence: `artifacts/crap/pins-scenarios-extraction/restoration-verification.json`, frozen protocol/inputs/32 workers, `performance-summary.json` and `variance-audit.json`.

## Completed-update notice closing guard: unchanged production (November 24, 2026)

ShowCompletedUpdateNoticeAsync CRAP72→35 in both variants, CC8 unchanged;3/12 lines25%,5/8 branches62.5%. Four Default/four Store cases pass, both final affected no-incremental builds0warnings/errors. Closing bypasses ordinary/expected-restart retry loops with active/canceled lifetimes; private Task and public caller preserve the existing notice/control state and complete synchronously. Raw coverage confirms ConsumeNotice and both waits are not executed. No real updater transactions consumed or marked; nonclosing workflow remains an explicit verification gap, and the method remains above30 with coverage targets unmet.

All80 production DLLs match the baseline; six production and two test PDB source proofs match current inputs. No production edit or timing benchmark, and no allocation/speedup claim.29 open scopes, five Store findings and prior native/theme/withdrawal limits retained. Evidence: `artifacts/crap/update-notice-closing/verification.json`, `production-before.json`, `open-verification.json` and `current-audit.json`.

## Updater launch failure characterization: unchanged production (November 24, 2026)

UpdateLauncher.StartAsync Default CRAP72→17.99, CC8 unchanged;12/26 lines46.2%,0/8 branches. Store policy rejection gives64.90/1of26 lines; its Default-only body is inaccessible in Store and the finding remains explicit. Eight Default/eight Store cases pass; two final affected no-incremental builds0warnings/errors. All80 production DLLs match baseline, with six production/two test PDB source proofs. No timing benchmark was run for this test-only batch; no performance/allocation improvement is claimed.

Tests preserve null/outside-path, missing/malformed-job and occupied-helper copy failures, cancellation ordering, named Cancel signaling and handle release. Only new owned GUID staging children are touched/removed; existing transactions and parent directory are not deleted. No helper process starts. Initial incorrect directory-copy exception expectation archived/corrected; all eight cases retained and failed coverage unused. Readiness success, process launch/exit, timeout and in-wait cancellation remain unverified; coverage targets unmet despite numeric Default CRAP below30.28 open scopes, five Store findings and inherited theme/withdrawal limitations retained. Evidence: `artifacts/crap/update-launcher-copy-guards/verification.json`, `production-before.json` and `open-verification.json`.

## Pins fixture characterization: unchanged production (November 24, 2026)

CapturePinsFixtureAsync CRAP9312→106.14, CC96 unchanged;139/155 lines89.7%,66/96 branches68.8%. Fourteen Default and14 Store focused cases pass; two final affected no-incremental builds have0 warnings/errors. Six characterization methods cover pins/filter/group/snapshot/divider/row-button behavior, cancellation, null output, eight locked-output failures and two template failures. All80 monitored production DLLs match the pre-batch hashes; six production and two test PDB source proofs and the copied current App.xaml fixture establish the tested inputs. No benchmark was run for this test-only batch, and no timing, allocation or speedup claim is made.

Seven PNGs per variant retained; Dark pins inspected. Actual CaptionButton style is loaded from App.xaml; other minimal styles are not full product appearance proof. Original unshown-window lifetime is retained and cleaned up by the test caller. Return to Live resets the copy timer before the later await; no timer leak is claimed. Initial fixture/assertion failures archived, all14 final cases retained, failed coverage not adopted. Sixteen lines remain unverified; score remains above50 and branch coverage below70%. Prior native, Store, theme and withdrawal limitations remain. Evidence: `artifacts/crap/diagnostic-pins-fixture/verification.json`, `production-before.json` and retained images.

## Activity-panel extraction withdrawn: guard latency interval too broad (November 24, 2026)

Candidate outer127.79/CC116→32.16/CC32 with cohesive tab14.51/chart45.60/activity27.41 scenarios.21 characterization+22 header tests pass per variant;4candidatebuilds0warnings/errors. Exact statement/read/exception ordering proof,8184unchangedtestbodies,155appsource/resourceentriesunchangedexceptintendedsource/newhelper. Candidate was not adopted.

Frozen8alternatingpairs/16workers,1warmup+3timedrepetitions,2actualcompiledDefaultworkflow workloads; no overlap/timedretry/discard/gaterelaxation. Reject latency/allocation lower95>1 or latencyupper95>1.10. Full flow before1677.225ms/candidate1586.511ms,pairedratio0.99282,CI0.92201–1.06908; allocationCI0.99267–1.00077. Collapse-guard before916.468ms/candidate859.797ms,ratio0.95916,CI0.81985–1.12213; allocationCI0.98795–1.01075. No statistically detected regression, but guard upper1.12213 fails1.10 uncertainty gate. One audit of existing pair dispersion made no new timing runs and established no root cause. Do not repeat identical timings or loosen the gate.

Original source and all80 monitored DLLs restored exactly;helper archived/removed.4restoredapp/testbuild0warnings/errors;3Default/3Store restoration checks pass. CurrentCRAP127.79/CC116 retained. Fullsuite/completeappearance/Storetimingnotclaimed; prior scopes/theme blocker unchanged. Evidence: `artifacts/crap/activity-panel-extraction/restoration-verification.json`, `performance-protocol.json`, frozen inputs/workers, `performance-summary.json` and `variance-audit.json`.

## Activity-panel characterization: unchanged production (November 24, 2026)

CaptureActivityPanelAsync13572→127.79, CC116 unchanged;189/209 lines90.4%,86/116 branches74.1%.Still above50; floor116 requires meaningful responsibility refactoring.21 Default/21 Store focused cases pass,2 final affected no-incremental builds0warnings/errors. Original full baseline passed after supplying the owned preference-readiness flag (OnLoaded remains bypassed); pre-ready rejection is separately characterized. Initial locked settings test expectedIOException but original atomic replace throwsUnauthorizedAccessException; exact assertion corrected, failed evidence archived, no failed coverage adopted.

Four starting tabs cover all16 images/3 settings files/pass marker and original tab/preferences/size/window ownership. Additional cases cover pre/after-header cancellation, early/middle/final IO failures and11 tab/collapse validation faults. No live sampler/process/service/user-store changes or forced foreground.16 representative PNGs per variant retained; both-collapsed Dark inspected, minimal styles not product appearance acceptance. All80 production DLLs identical,6 production/2 test PDB source proofs,3485 app shapes unchanged. No timing benchmark, allocation change or speedup claim.40 complete fresh observations/5767 inherited; no union/globalcoverage.20 source lines and several defensive layout/restoration failures remain unverified; prior27 scopes/theme blocker retained. Evidence: `artifacts/crap/diagnostic-activity-panel/verification.json`, `production-before.json` and `unverified-lines.json`.

## Example workflow cancellation: unchanged production (November 24, 2026)

CaptureExamplesAsync272→57.90, CC16 unchanged,53/117 lines45.3%,10/16 branches62.5%.Still above50; later native capture/64lines unverified.3 Default/3 Store focused cases pass,2 final affected builds0warnings/errors; original baseline passed after correcting initial fixture caption resources (failed evidence archived). Tests directly inspect actual120sample/7process/spike-recovery/anchor/resource data and window/timer/CTS cleanup on cancellation, plus null output and original English mutation. No completed screen capture or sampler/process action/user preference write. All80 production DLLs unchanged;6 production PDB source proofs,2 test PDB proofs and3485 unchanged app method shapes. No benchmark timing or speedup claim for this test-only change. Existing native foreground scope remains unresolved; no forced activation or bypass.26 whole-method observations adopted,5781 inherited; prior incomparable icon observation retained as a complete record, no union/globalcoverage. Evidence: `artifacts/crap/diagnostic-examples-cancellation/verification.json` and `production-before.json`.

## Language workflow/validation separation: frozen paired measurement (November 24, 2026)

CaptureLanguageSettingsDiagnosticsAsync CRAP73.54→41.03, CC72→40; new private localized-state validator CRAP/CC32. Parent53/58 lines28/40 branches; validator19/19 lines32/32 branches. Both≤50, both retained>30. Unchanged21 Default/21 Store tests pass; four affected app/test builds0warnings/errors. Exact condition/read/short-circuit/exception-message order and original cancellation/save/finally effects preserved. All8135 test method bodies are identical between benchmark snapshots; rebuilt assembly metadata differs. App source/resource proof changes only the intended file plus the new helper across154 original entries.

Eight frozen alternating process pairs,16 fresh workers,1 warmup+3 timed repetitions per workload, tiering disabled. Actual compiled Default workflows through unchanged behavior tests; owned windows/settings/PNG IO and read-only device discovery included. No profiler/build/test overlap, discarded samples, timed retry or gate relaxation. Reject latency/allocation lower95>1 or latency upper95>1.10.

| Workload | Before median ms | After median ms | Paired ratio95%CI | Allocation ratio95%CI |
|---|---:|---:|---:|---:|
| All11 language choices, save/cancel/system restoration and4PNGs | 3671.884 | 3705.915 | 0.97197–1.02851 | 0.99712–1.00270 |
| Stale-query rejection and restoration | 439.117 | 430.252 | 0.88552–1.05983 | 0.98167–1.01193 |

Gate passed with no statistically detected latency/allocation regression. Macro/native/filesystem variance remains; no speedup or zero sub-resolution overhead claim. Known5 parent throw paths, specific history-removal/sampling-task faults and full product appearance remain unverified. Four expected WinMon DLL copies changed,76 other production copies identical; all27 prior scopes/theme blocker retained. Evidence: `artifacts/crap/language-validation-extraction/performance-protocol.json`, immutable workers, `performance-inputs.json`, `performance-summary.json`, `test-il-proof.json` and `all-app-inputs-proof.json`.

## Live-language Settings diagnostic: unchanged production (November 24, 2026)

CaptureLanguageSettingsDiagnosticsAsync5256→73.54, CC72 unchanged;70/75 lines93.3%,60/72 branches83.3%.Still above50 because floor72. Collateral ApplyLanguage42→6/full23lines6branches and DiscoverNetworkInterfaces42→6/full4lines6providerbranches (host observation, not exhaustive native faults).21 Default/21 Store focused cases pass, two final affected no-incremental builds0warnings/errors; original full workflow baseline passed first. All80 monitored production DLLs and6 production source/PDB proofs match baseline;2323 app method shapes unchanged. No production refactoring, execution/allocation change, benchmark timing or speedup claim. Tests cover11 actual modal language saves/cancel/system restoration,13 stale UI guards,cancellation and4 image failures/missing directory on an owned settings file. No sampler/hotkey/startup/service/process change. Four PNGs per variant retained; Japanese representative inspected, minimal styles not product appearance acceptance. Completed saves survive cancellation/IO while original window state restores; existing behavior preserved. Five parent and two modal callback throw paths remain unverified.26 complete fresh observations/4619 inherited; no union/globalcoverage. Evidence: `artifacts/crap/diagnostic-language-settings/verification.json` and `production-before.json`.

## Example Draw: unchanged production (November 24, 2026)

CaptureExamplesAsync local Draw506→22.28, CC22 unchanged;22/24 lines91.7%,19/22 branches86.4%.14 Default/14 Store focused cases pass, two final affected no-incremental builds0warnings/errors; original baseline passed first. Original grouped-row assertion error in the first expanded13pass/1fail run corrected; all14 cases retained and only passing coverage adopted. All80 monitored production DLLs and6 embedded-PDB source proofs match baseline;2323 app method shapes unchanged. No production execution/allocation change, timing benchmark or speedup claim. Owned windows with synthetic frames exercise chart/focus/selection/retention/hidden-show/compact/grouped/pinned state before cancelled native capture, plus invalid dimensions. No sampler or process action, no new PNG/product appearance proof. Native successful capture and chart mismatch guard remain unverified; parent272 and prior foreground scope retained. Whole prior asynchronous icon-helper observation retained because focused coverage is incomparable; no coverage union. Evidence: `artifacts/crap/diagnostic-example-draw/verification.json` and `production-before.json`.

## Action-drawer diagnostic: unchanged production (November 24, 2026)

CaptureActionDrawerAsync2352→49.27, CC48 unchanged;56/61 lines (91.8%) and42/48 branches (87.5%).26 Default/26 Store focused tests pass, two affected no-incremental builds0warnings/errors. Original success baseline passed first. Two ineffective synthetic count-fault cases were withdrawn after the initial26pass/2fail run; failed evidence archived and excluded from final coverage. Inner count-error and hide-usability paths remain unverified. No production refactoring: all80 monitored DLL hashes and4 embedded-PDB source proofs match baseline,2323 app method shapes unchanged. Production execution/allocation paths therefore have no code change; no benchmark timing or speedup claim. Original500ms waits preserved. Owned fake-process windows use minimal fixture styles with an explicit App.xaml collapse command setter, no sampler or process actions. Three PNGs retained per variant; representative collapsed Dark inspected, no whole-app appearance acceptance. Evidence: `artifacts/crap/diagnostic-action-drawer/verification.json` and `production-before.json`.

## Native position-recovery diagnostic: unchanged production (November 24, 2026)

CheckWindowPositionRecoveryAsync600→24.03/CC24 unchanged,26/27lines21/24branches; RememberWindowPosition42→6/CC6 unchanged,6/6lines6/6 provider branches.15native cases pass per variant (~72–73seconds) with the original three-second timer and500/2700/600/3300ms awaits. Four fast guard cases pass per variant (<1second). All runs first-pass, zero failures/skips; four affected no-incremental builds have zero warnings/errors. Owned HWND position changes, native pixel tolerances, exact user-choice guards, cancellation and final IO are characterized without display/power reconfiguration or native foreground activation.

The unchanged timed native groups were reused after adding independent guard tests. One complete Remember method record is selected from the guard run; all others remain from the native run, with raw-report hashes and no coverage union or aggregate coverage claim. Eleven fresh observations/4634 inherited yield172>30/54>50/118band, counter168→170. Eight embedded-PDB proofs,2323 matching app shapes and all80 identical production DLLs establish unchanged production bytes; no new benchmark. Timed tests retain behavior under test and are not product performance measurements.

Fixtures explicitly clean position hooks/power/state subscriptions/timers and queued callbacks; pre-cancellation's original move/independent queued recovery remains documented. Owned PNGs are retained and reopen exclusively. Native waiting/read/SetPos failure, Win-key and actual topology/power/multi-DPI states remain unforced under the existing native position scope. All27 prior scopes/four Store findings/theme blocker remain. Evidence: `artifacts/crap/diagnostic-position-recovery/verification.json`, separate native/guard reports, whole-method selector and behavior notes.

## Update-footer presentation diagnostic: unchanged production (November 24, 2026)

CaptureUpdateDiagnosticsAsync420→20, CC20 unchanged,38/38lines20/20branches.29new cases pass per variant,29Default/29Store, zero failures/skips (~9–10seconds); final two no-incremental affected builds have zero warnings/errors. Dark/Light presentation, real60/50/30DIP size boundaries, overlap0.5DIP/overflow-row overlay, short-window containment, four locked image stages, missing folders, cancellation and original preference/height/color restoration are characterized. No update action/download/launch/network/service operation occurs.

Two initial baseline cases passed; expanded Default26pass/3failure came only from recording cached transform bounds before a fixture layout flush. Original validator outcomes/thresholds remain unchanged, failed artifacts retained, Store only ran corrected fixtures. Eight embedded-PDB checks,2323 matching app shapes and all80 identical production DLLs establish unchanged production bytes; no benchmark. Four fresh observations/4641 inherited yield174>30/55>50/119band, counter167→168. Collateral SetBackdrop29.72→27.1 remains partial15/17lines17/26branches and does not resolve native backdrop scopes. Four-image outputs per starting state/variant are retained; minimal fixture styles are not product appearance acceptance. All27 prior scopes/four Store findings/theme blocker remain. Evidence: `artifacts/crap/diagnostic-update-footer/verification.json`, existing-behavior notes and final variant artifacts.

## Search diagnostic workflow: unchanged production (November 24, 2026)

CaptureSearchDiagnosticsAsync1056→32, CC32 unchanged,47/47lines32/32branches; revised50 maximum met, original30 finding retained.26new cases pass per variant,26Default/26Store, zero failures/skips (~7–8seconds); final two no-incremental affected builds have zero warnings/errors. Complete nine-image workflows at1000/1400DIP, sample/filter/idle/sort/header guards, locked capture/marker failures, missing directories and pre-cancellation are characterized using owned synthetic samples/windows/files. No native foreground capture or live sampling occurs.

The initial baseline's two complete workflows failed only a new incorrect final-grouping expectation; actual grouped completion is preserved and explained by the existing historical preference guard. Width886 remaining after wide-image IO failure is separately recorded as an existing diagnostic cleanup risk, without a behavior change. Failed source/log/TRX/XML retained. Eight embedded-PDB proofs,2323 matching app shapes and all80 identical production DLLs establish unchanged production bytes; no benchmark is needed. Ten fresh observations/4635 inherited yield175>30/56>50/119band, counter167 unchanged. PNG widths/96DPI/disposal are verified and images retained; minimal fixture styles are not product rendering acceptance. All27 prior scopes/four Store findings/theme blocker remain. Evidence: `artifacts/crap/diagnostic-search/verification.json`, `existing-behavior.json` and final variant artifacts.

## Partial native capture contracts: unchanged production (November 24, 2026)

CaptureExampleWindowAsync156→32.7/CC12 unchanged,10/21lines6/12branches; CaptureWindowFrame42→6.02/CC6 unchanged,12/13lines4/6branches. Nine deterministic cases pass per variant,9Default/9Store, zero failures/skips (~2seconds); final two no-incremental affected builds have zero warnings/errors. Example foreground rejection/cancellation and owned background PrintWindow/file failure/resource disposal are characterized. The original200ms composition delay/five attempts remain intact.

Windows denied foreground activation in the initial18-case candidate (7pass11fail, including one incorrect missing-directory exception expectation). Entire candidate/results retained; foreground-dependent cases were withdrawn as unverified setup, without suppressions/conditional passes or adopting failed-run coverage. Native foreground success/screen containment/example save and frame foreground-copy/native failure paths remain open. Example line47.62/branch50 and frame branch66.67 do not meet secondary targets; characterized-original counter167 unchanged, new27th open scope explicit. Inventory175>30/57>50/118band.

Four embedded-PDB checks,2323 matching app shapes and all80 identical production DLLs establish unchanged production bytes; no benchmark. Background frame PNGs match native dimensions, reopen exclusively and are retained for both variants; representative owned synthetic frame inspected. These do not establish composed caption/product styling/global performance acceptance. Evidence: `artifacts/crap/diagnostic-native-capture/verification.json`, `native-foreground-verification.json`, final variants and failed candidate archive.

## Native title-bar diagnostic: unchanged production (November 24, 2026)

ValidateTitleBar2162→46, CC46 unchanged,38/38lines46/46 provider branches.49new cases pass per variant,49Default/49Store, zero failures/skips (~8–9seconds); final two no-incremental affected builds have zero warnings/errors. Owned windows receive native hit-test queries only, covering normal title/pin/caption/resize regions, client actions, ancestry, exact action/order boundaries and chart/process header limits. No native caption actions, injected input or sampling occur.

Initial45pass/4failure came from intersecting fixture constraints;28DIP-high actions isolate vertical edges and Export isolates the right edge without Info ordering conflicts. Failed artifacts retained; Store only ran corrected fixtures. Four embedded-PDB proofs,2323 matching app shapes and all80 identical production DLLs establish unchanged production bytes; no new benchmark. Inventory176>30/58>50/118band, counter167 unchanged; target46 remains an original above30 finding. Provider coverage does not establish independent native Maximize/Close failure modes or all DPI/OS/theme states. All26 prior scopes/four Store findings/theme blocker remain. Evidence: `artifacts/crap/diagnostic-titlebar/verification.json` and native baseline/final variant artifacts.

## Metric detail diagnostic workflow: unchanged production (November 24, 2026)

32 new cases pass per variant,32Default/32Store, zero failures/skips (~13–14seconds each); both final no-incremental affected builds have zero warnings/errors. Per-metric helper156→12/CC12,21/21lines12/12branches; full diagnostic workflow2162→46/CC46,30/30lines46/46branches. The parent meets revised50 but remains an original above30 finding at its fully covered complexity floor. Five collateral callbacks become fully covered. All eight output names, real popup/focus/tab-order behavior, validation guards, cancellation, IO and restoration are characterized.

Initial Default31pass/1failure came from normal compact refresh restoring an invalid fixture accessible name. A temporary fixture-only property observer preserves that input and is removed in finally; failed artifacts retained. A test import ambiguity was fixed before execution. Four embedded-PDB source checks,2323 matching app shapes and all80 identical production DLLs establish unchanged production bytes; no benchmark is needed. Inventory176>30/59>50/117band, counter166→167. PNG format/IO checks use minimal application resources without the product MetricDetails template, so no styled-rendering or global performance claim. All26 prior scopes/four Store findings/theme blocker remain. Evidence: `artifacts/crap/diagnostic-metric-capture/verification.json` and final variant artifacts.

## Memory-pressure diagnostic capture: unchanged production (November 24, 2026)

CaptureMemoryDetails72→8, CC8 unchanged,19/19lines8/8branches.12new cases pass per variant,12Default/12Store, zero failures/skips (~1second each); both final no-incremental affected builds have zero warnings/errors. Available/missing values, ordered labels/count, mixed-state failures, pre-cancellation, missing folders, locked existing file, empty name and finally cleanup are characterized using owned state/paths. Successful PNGs decode at96DPI and reopen exclusively.

Four embedded-PDB source checks,2323 matching app shapes and all80 identical production DLLs establish unchanged production bytes. No benchmark is needed; test durations are not product performance measurements. Four fresh whole-method observations/4641 inherited yield177>30/61>50/116band, counter165→166. The minimal test Application omits App.xaml's MetricDetails DataTemplate; capture format/IO does not prove styled product rendering. No parent compact workflow, native sampling, shown window, full suite or global performance claim. Evidence: `artifacts/crap/diagnostic-memory-details/verification.json` and final variant artifacts.

## Gauge/recorder diagnostic callbacks: unchanged production (November 24, 2026)

26 new cases pass per variant,26Default/26Store, zero failures/skips (~1second each); both final no-incremental affected builds have zero warnings/errors. Gauge corner validator110→10/CC10,7/7lines10/10branches; recorder comparison-sample factory420→20/CC20,11/11lines20/20branches. Complexity is unchanged. All seven gauge styles, label/tooltip/visibility/icon partitions, recorder phase boundaries and retained record state are asserted without invoking parent workflows.

Initial Default20pass/6failure came from supplying80%to non-arc fixture labels instead of the original empty input. Fixture corrected without production changes; failed artifacts retained and Store did not repeat them. Four embedded-PDB checks,2323 matching baseline app shapes and all80 identical production DLLs establish unchanged production bytes. No new benchmark; test durations are not product performance measurements. Inventory178>30/62>50/116band; counter163→165, prior26scopes/four Store findings/theme blocker retained. No actual rendering/full-workflow/global-performance claim. Evidence: `artifacts/crap/diagnostic-gauge-recorder/verification.json` and final variant artifacts.

## Snapshot containment diagnostic: unchanged production (November 24, 2026)

Original AssertSnapshotContained110→10, CC10 unchanged,11/11lines10/10branches.46new cases cover plot/panel edges, action heights24DIP, detached visuals and unchanged geometry;47tests pass per variant including one existing controller test, zero failures/skips (~3–4seconds). Both final no-incremental affected builds have zero warnings/errors. An initial test-only conditional-type compilation error was corrected before executing tests; failed artifacts retained.

Four embedded-PDB checks,2323 matching baseline app shapes and all80 identical production DLLs establish unchanged production bytes. No new benchmark: this is test-only, and test durations are not product performance measurements. Owned unshown synthetic layouts do not prove actual responsive rendering or full capture behavior. Inventory180>30/64>50/116band, counter162→163;26prior scopes/four Store findings/theme blocker remain. Evidence: `artifacts/crap/diagnostic-snapshot-containment/verification.json` and final variant artifacts.

## Connection diagnostic toolbar/endpoint checks: unchanged production (November 24, 2026)

74 focused cases pass per variant,74Default/74Store, zero failures/skips, with clean final no-incremental affected builds. Toolbar156→12/CC12; TCP listener42→6/CC6, peers210→14/CC14, UDP42→6/CC6; geometry42→6/CC6. All five have full line/branch coverage and unchanged complexity. Actual controls exercise owned unshown geometry; pure endpoint objects exercise ownership/port/null rules without sockets or native tables.

Four embedded-PDB source checks,2322 unchanged app method shapes and all80 identical production DLLs establish unchanged production bytes. This is a test-only batch; no performance timing or whole-product rendering claim. Initial47 geometry cases and final combined74 each passed both variants; raw reports remain separate. Source/signature inspection corrected endpoint-versus-geometry callback attribution before adoption. Inventory181>30/65>50/116band; counter157→162. Evidence: `artifacts/crap/diagnostic-connection-toolbar/verification.json` and final combined variant artifacts.

## Captured toolbar/pin diagnostic helpers: unchanged production (November 24, 2026)

33 focused cases pass per variant,33Default/33Store, zero failures/skips (~2–3seconds each), with clean final no-incremental affected test builds. Existing captured local helpers are bound to owned closure state without invoking parent network/capture workflows. Actual header controls retain their content in unshown roomy layouts; exact containment, inclusive selector contact and detached failures are verified. Pin lookup uses actual ConnectionDisplayRows and synthetic lifetime-checked icon inputs, preserving identity matching, depth-first order and existing name/flag-independent selection.

CheckInvestigationToolbar72→8/CC8,6/6lines8/8branches; FindPinButton156→12/CC12,6/6lines12/12branches. Bounds helper2→1/CC1 fully covers two lines. Three exact observations/4642 inherited yield186>30/67>50/119band, counter155→157. Initial Default13pass/20failure came from undersized fixture containers and a missing unavailable status on an ownerless row; inputs corrected without changing diagnostic rules, all failed artifacts retained. Store did not repeat known invalid fixtures.

Four embedded-PDB source checks,2322 unchanged app shapes and all80 identical production DLLs establish unchanged production bytes; no new benchmark is warranted. No shown window, live sampling, network/service/capture parent operation or whole-product minimum-width/rendering/full-suite/global performance claim. Evidence: `artifacts/crap/diagnostic-captured-helpers/verification.json`, exact target/variant results and existing-behavior/fixture limits.

## Diagnostic service toolbar/pin traversal: unchanged production (November 24, 2026)

57new focused cases pass per variant,57Default/57Store, zero failures/skips (~7seconds each), with clean final no-incremental affected test builds. Real ServicesView/one synthetic service row live in an owned transparent, non-activating window so pin IsVisible is exercised without service enumeration/actions, sampling or native capture. Responsive layouts, order/containment/overlap tolerances, pin/identity/summary size thresholds and original hidden-pin/null/detached exceptions are verified; geometry snapshots remain unchanged.

VerifyToolbar1332→36, CC36 unchanged,31/31lines36/36branches; revised50 ceiling met, original above30 finding retained at its complexity floor. ServicePinButtons72→8/CC8,6/6lines8/8branches. Six LINQ callbacks become fully covered; ServicesView.ArrangeToolbar stays14 with branches10/14→12/14. Nine exact observations/4636 inherited yield188>30/69>50/119band, counter154→155 for pin traversal only.

Initial Default51pass/6failure came from intrinsic WPF text sizes; fixture-only fonts allow real12DIP/1DIP boundary inputs without changing diagnostic thresholds. A later56pass/1failure was an incorrect added period in the new original-message assertion. All artifacts retained; Store did not repeat known failing fixtures. Four embedded-PDB source checks,2322 app shapes and all80 identical production DLLs establish unchanged production bytes, so no regression benchmark is needed. No full Services workflow/native DPI/whole-app/full-suite/global performance claim. Evidence: `artifacts/crap/diagnostic-service-toolbar/verification.json`, target observations, variant results and fixture/source proofs.

## Diagnostic header span/clipping: unchanged production (November 24, 2026)

22 new cases pass per variant,22Default/22Store, zero failures/skips (~1second each). Owned unshown Border fixtures exercise the original activity-header local validator's full-width/horizontal-position tolerance, real left/right ancestor layout clips at0.5DIP boundaries, accepted vertical-only clipping and original null/detached failures. ClipToBounds remains false; actual geometry preconditions and read-only snapshots are verified. No production edit, window shown or native sampler.

CheckHeader156→12, CC12 unchanged,14/14lines12/12branches. One exact observation/4644 inherited yields189>30/71>50/118band, counter153→154. Initial test fixture compilation assumed Grid instead of the actual FullView Border; four CS1061 errors per variant were corrected using Child. Failed-build test attempts ran no cases. Both final no-incremental affected builds have zero warnings/errors, and the first runnable executions pass all22 cases.

Two embedded-PDB checks,2322 unchanged app shapes and all80 identical production DLLs support unchanged production performance; no benchmark is warranted. No whole ActivityPanel workflow/native DPI/appearance/full-suite/global coverage claim. Evidence: `artifacts/crap/diagnostic-header-layout/verification.json`, focused results, failed-attempt logs and pinned helper/configuration/source proofs.

## Diagnostic process toolbar: unchanged production (November 24, 2026)

21new cases pass per variant,21Default/21Store, zero failures/skips (~2seconds each), with clean final no-incremental affected test builds. Original ArrangeToolbar/actual controls at1400/1600DIP exercise disabled guard, ordered overlap, row wrapping, left/right/top/bottom0.5DIP inclusive tolerances, stale minimum search width and detached-visual exceptions. Layout snapshots remain unchanged. No windows shown, native sampling, foreground capture, production seam or refactor.

ValidateProcessToolbar272→16, CC16 unchanged,19/19lines16/16branches. One exact observation/4644 inherited yields190>30/72>50/118band, counter152→153. Initial15pass/6failure per variant came from transforms not applied until layout and an incorrect WPF exception-type expectation; original failures/source remain archived. Corrected fixtures explicitly flush and verify transformed bounds without relaxing limits.

Four embedded-PDB checks,2322 app shapes and all80 identical production DLLs establish unchanged production bytes; no new benchmark is warranted. Shared configuration-driven evidence checks reuse the existing focused gates, with a pinned per-batch snapshot. No native DPI/whole-app rendering/full-suite/global performance claim. Evidence: `artifacts/crap/diagnostic-process-toolbar/verification.json`, focused results and unchanged-source proofs.

## Diagnostic profile contracts: unchanged production (November 24, 2026)

Seven new focused cases pass first execution in both variants,7Default/7Store, zero failures/skips and clean affected test builds. Four independent original profile calls overlap their real20-second warmups: empty/populated owned histories with one-second sampling, plus existing zero/negative-duration aggregation failures. The grouped case takes21.1687seconds Default/21.1760seconds Store. No production timer is modified; current-process memory/CPU reads and GC counters are real. Reports verify17 fields, finite values, CPU-unit/memory invariants, exact history counts, exclusive file access and cancellation/validation precedence without live sampling or shown windows.

CaptureProfileAsync110→10, CC10 unchanged,44/44lines and provider10/10branches; two simple profile callbacks2→1. Native memory-query false outcomes are not deliberately exercised despite raw line109 reporting100%(2/2); provider totals do not establish that behavior. The26th open scope also retains later cancellation/final-write errors; independent counter152 unchanged. Public CLI clamps durations10..3600; internal zero/negative failures are recorded and preserved.

Three exact observations/4642 inherited yield191>30/73>50/118band. Four embedded-PDB source checks,2322 app shapes and all80 identical production DLLs establish unchanged production bytes; no new regression benchmark. Overlapping profile calls share process CPU/GC and are functional tests, not comparative performance measurements. No full-suite/global coverage or whole-app acceptance claim. Evidence: `artifacts/crap/diagnostic-profile/verification.json`, source proofs, first-pass variant runs and raw provider branch evidence.

## Diagnostic tooltip capture: unchanged production (November 24, 2026)

Twelve focused cases pass per variant,12Default/12Store, zero failures/skips (~4seconds each), with zero warnings/errors in both final affected test builds. Original WPF capture uses owned windows/history/files and detached OnLoaded; no native sampling. Tests distinguish latest CPU42%/earlier7%, equal colors on different brushes, all four theme-check arms, resource-cast failures, empty history, canceled render dispatch and two write failures. Popups close, original tooltip references return and existing file sentinels survive. Four retained PNGs are identical, decode with expected dimensions/96DPI and a representative image is visually readable/unclipped.

CaptureToolTipAsync110→10/CC10 unchanged,22/22lines10/10branches. CaptureVisualAsync6→2/CC2,11/11lines2/2branches; three simple setup/render callbacks2→1. Five exact observations/4640 inherited yield192>30/74>50/118band, counter151→152. Initial11pass/1failure per variant came from expecting caller-token identity; existing WPF TaskCanceledException carries CancellationToken.None, now characterized without production change. Failed evidence retained.

Four embedded-PDB source checks,2322 matching app shapes and all80 identical production DLLs establish unchanged production bytes; no benchmark is needed. The tooltip image check does not clear the inherited whole-app theme blocker. No full-suite/global performance or coverage claim. Evidence: `artifacts/crap/diagnostic-tooltip/verification.json`, existing-cancellation behavior, variant runs and PNG/source proofs.

## Diagnostic archive recovery: unchanged production (November 24, 2026)

Seven new cases pass first execution in each variant, plus11 directly related DailyUsage cases per variant,18Default/18Store total, zero failures/skips. The original static diagnostic runs twice on owned preference paths, rejects schema2/32MiB+1 loads and infinite-GPU writes, preserves archive bytes and permits recording. Owned pre-cancellation, null output and four I/O failures verify original exceptions, no leaked unshown MainWindow fixtures and independently reacquired file/writer locks. No user data, shown windows or native sampling.

ValidateArchiveRecoveryAsync110→10.06, CC10 unchanged,33/36lines91.7% and9/10branches90%; the three unexecuted lines are defensive diagnostic assertion throws. SaveUsageArchiveForFixtureAsync2→1 collateral. Two exact fresh observations/4643 inherited yield193>30/75>50/118band; the original target method meets secondary coverage targets and counter150→151. Both final affected test builds have zero warnings/errors; six embedded-PDB source checks,2322 app shapes and all80 identical production DLLs support unchanged production performance. No new benchmark, rendering or full-suite/global claim. Evidence: `artifacts/crap/diagnostic-archive-recovery/verification.json`, original-source snapshot and focused variant results.

## Setup entry cancellation: unchanged production (November 24, 2026)

28 new cases across four existing setup entries plus16 existing policy/entry rejection cases pass44/44 on first execution, zero failures/skips in191ms. Fail-closed SetupHost callbacks, pre-cancelled tokens and GUID-owned marker folders verify validation/preparation/cancellation ordering without protected lock creation, service changes or package use. Three portable-PDB source checks and all80 identical production DLLs establish unchanged production bytes;187 helper method shapes match. The final affected test build has zero warnings/errors. No production change or fresh benchmark.

BeginAsync137.43→117.98/CC14 and RevokeAsync156→97.35/CC12 remain above50. RemoveAsync42→21.19/CC6 crosses numerically with only25%line/16.7%branch coverage; AcquireLockAsync6→3.69/CC2 covers cancellation only. None is claimed fully remediated; counter150 stays unchanged. Four exact fresh observations/4641 inherited give194>30/76>50/118band, with25 open scopes. No Store/full-suite/global performance or coverage claim. Evidence: `artifacts/crap/setup-entry-cancellation/verification.json` and retained first-pass results/source proofs.

## Dump-dialog I/O testability candidate: withdrawn (November 24, 2026)

The lazy owner-scoped chooser/write hook passed35 focused Default and35 Store cases and clean app/test/performance-friend builds. Candidate create-callback CRAP79.48→14,100%lines/85.7%branches, CC10→14. No new window/closure/state-machine fields were introduced; the original default body reconstructs exactly and the original Task.Run writer expression remains unchanged. These observations are archived candidate evidence, not adopted metrics.

Eight fixed alternating pairs/16 distinct process-start identities measured six original-source/candidate-source WPF workloads with common no-op native effects, seven batches,500ms warmup,40ms target, minimum16 operations. No build/test/coverage overlap, native save chooser, real dump or user-file write. Every operation checks controls/status/task ownership; all paired outcomes match. Populated-registry cancel ratio1.01993,95%CI1.00492–1.03516 detects increased latency; constructor upper1.15340 and populated-write upper1.13973 also fail the frozen1.10 uncertainty limit. Default cancel CI0.97758–1.05764; write0.94317–1.08580; ineligible0.93924–1.02616. Constructor18,672B, cancel2,088B and guard112B are unchanged; worker-allocation intervals include1.

Candidate withdrawn without timing retries/follow-ups/sample removal/gate changes. Analysis-only correction flattens all48 workload rows instead of assuming one workload in each of eight files; original analysis and all raw samples retained. Original source and all80 DLLs restored exactly, with9Default/9Store restoration cases passing and clean final test builds. Adopted callback remains79.48; canonical195>30/76>50 unchanged. Evidence: `artifacts/crap/dump-dialog-io/performance-summary.json`, `analysis-correction.json`, `restoration-verification.json` and `verification.json`. This does not establish native chooser/dump latency or global acceptance.

## Dump dialog guards and task wait: unchanged production (November 24, 2026)

Eight new guard/closing/eligibility-error/task-drain cases plus one existing WPF chooser-cancellation case pass in each variant,9Default/9Store,0failure/0skip (972/960ms). Owned unshown dialogs and TaskCompletionSources verify busy short circuit, original exception placement, closing, completed/faulted/canceled writes and resnapshotting a late pending write. No native Save chooser or process dump is opened/written. Initial unshown Owner assignment fails four fixture setups in each variant; removing that unnecessary assignment corrects the fixture with expectations intact, and all failed artifacts remain.

Create callback110→79.48/CC10 unchanged,4/35lines3/10branches, still above50; write-wait10.5→6/CC6 unchanged,4/4lines6/6branches. No original>30 method crosses the target, so canonical195>30/76>50/119band and counter150 stay unchanged.24open scopes retain hardwired native chooser/write, positive-path UI restoration and native dump outcomes. Both final no-incremental affected test builds have zero warnings/errors (~5seconds); four embedded-PDB checks,2322 matching app shapes and all80 identical production DLLs establish unchanged production bytes. No new benchmark or full-suite/global acceptance claim. Evidence: artifacts/crap/dump-dialog-guards/verification.json and retained variant runs.

## Restart handoff: owned archives, unchanged production (November 24, 2026)

13Default cases and two supported Store guards pass first execution,0failure/0skip (~1second/539ms). Existing process-input strings are scoped only in a dedicated nonparallel host and restored before awaiting the source tuple captured by the original method. Real owned archives verify guards, rejected/locked file consumption, samples/investigation/usage/offset restoration and consumed-file replay without live readiness or double counting. No production seam, updater, actual transaction enumeration, sampling or notice handling. Store never receives unsupported positive restart input.

Default restoration67.87→11.56/CC10 unchanged,9/12lines/provider10/10branches. The outer catch remains unexecuted;75%line misses80/critical90 despite branch counts. Store remains67.87/2of12lines3of10branches, retained separately as the fourth supplemental Store finding. Counter150 unchanged; canonical195>30/76>50/119band,23open scopes.

Two first no-incremental affected test builds succeed with zero warnings/errors (~4–5seconds),14 current app/core PDB source checks and all80 production DLLs identical.2322 app/723 core shapes match; omitted Strings initializer remains inherited. No performance timing is warranted for identical production bytes; no new full-suite/global acceptance claim. Real startup/authorization, future live offset application and outer failure handling remain open. Evidence: artifacts/crap/restart-handoff/verification.json and first-pass variant reports.

## Shortcut retry: direct original callback, unchanged production (November 24, 2026)

Six new cases plus three existing GlobalHotkeyTests pass first execution in each Default/Store variant,9/9 each,~1second. Owned hidden windows temporarily hold an unused combination, create an actual conflict, then verify the ten-attempt limit, recovery before cutoff, closing and nullable retry-reference guard. Original Tick callback is invoked directly on the STA; configured interval remains one second and no FireTick scheduling internals or physical input are used. Timer/hotkey cleanup and released availability are asserted. No persistent shortcut/settings change.

Retry callback110→10/CC10 unchanged,4/4lines10/10branches both variants; parent6→2/CC2,7/7lines1/2branches and status20→4.37/CC4,5/7lines2/4branches are partial collateral. Counter150 promotes callback decision logic only. Actual scheduler/Explorer/native failure integration remains open. Both final affected no-incremental test builds pass with zero warnings/errors (~4–5seconds); initial test-only missing Strings using is fixed before any test run and retained. Four embedded-PDB source checks,2322 matching app shapes and all80 identical production DLLs establish unchanged production bytes; one omitted Strings initializer stays inherited. No new latency/allocation benchmark or full-suite/global acceptance. Inventory196>30/77>50/119band,22open scopes. Evidence: artifacts/crap/shortcut-retry/verification.json and focused variant artifacts.

## Native drive discovery: unchanged production, both variants (November 24, 2026)

DiscoverDrives156→14.73/CC12 unchanged in Default and Store,11/15lines (73.3%); raw provider branch12/12 is retained without inferring unready/empty-label/error coverage. One new explicit read-only native case passes in each variant (36/30ms), with four existing asynchronous loading/closing/failure caller cases passing separately in each. Exact native enumeration order/names/labels, independent result arrays and stable metadata are asserted; no disk/mount/label/preferences change or Settings window construction in the new case.

Combined selection initially runs only the ordinary cases. Exact Explicit FQN then reveals a test-only NUnit Distinct iterator Count assertion error, corrected without changing the uniqueness expectation; all initial artifacts remain. Two final no-incremental affected test builds pass with zero warnings/errors (~5seconds each), verified against four embedded-PDB source checks. Source/all80 production DLLs match;2322 loaded app shapes unchanged, one omitted Strings initializer retains its prior observation. No timing experiment is needed for identical production bytes. Inventory197>30/78>50/119band; counter149 unchanged,21open scopes. Native exception/unready/empty-label behavior and secondary line coverage goals remain open. No full-suite/global acceptance claim. Evidence: artifacts/crap/native-drive-discovery/verification.json and retained variant reports.

## Probe accounting: passive-session tests, unchanged production (November 24, 2026)

29new cases plus13 existing count/ledger cases pass,42total/0failure/0skip,132ms. TraceEvent3.2.8 IsActive documentation confirms passive construction until provider enablement; normal captures stay inactive without consumers. Owned synthetic records/payloads/fixed UTC clock exercise Add, including four finite tasks/1000callbacks. No Start/Source/EnableProvider, native capture or network workload. One inactive Snapshot query verifies existing COM HResult80071069, without inventing EventsLost data.

Initial fixture failures are retained: cached userData needs its own pointer and Snapshot requires an active session. Corrected tests observe original Add state directly under its gate and separately assert native Snapshot failure. Accounting expectations are preserved. Add210→14/CC14 unchanged,17/17lines14/14branches; passive Dispose42→6.17/CC6,5/6lines3/6branches. Active native snapshot, drain/start and consumer disposal waits remain open. Counter149 promotes Add only; inventory198>30/79>50/119band,20open scopes.

Final no-incremental affected test-only build succeeds with zero warnings/errors (~3seconds), using verified references. Two source/PDB checks,59 unchanged probe shapes, pinned TraceEvent hashes and all80 identical production DLLs establish unchanged production bytes. No new benchmark, native throughput, Store/full-suite/global acceptance claim. Evidence: artifacts/crap/probe-accounting/verification.json and both retained fixture runs.

## Install preparation: unchanged production, no-change rollback (November 24, 2026)

16new identity/version/cancellation/rollback cases plus27 existing non-explicit transaction cases pass first execution,43total/0failure/0skip,390ms. All valid-preparation cases use pre-canceled tokens before extraction. Only injected registration restoration records state or throws an owned exception; all native mutation callbacks fail closed. Original version precedence, borrowed package lifetime, caller lease ownership before transaction start, restore-before-release order and aggregate exception identity are preserved. No protected staging, service/registry write, elevation or installed executable is used.

Install110→10/CC10,30/30lines8/10branches; no-change rollback110→10/CC10,7/7lines4/10branches. Deploy600→552.03/CC24 with only1/35lines0/24branches; full outer-method line coverage is not successful deployment or changed-state rollback coverage. Four source/PDB checks,187 unchanged helper shapes and all80 identical production DLLs establish preserved production bytes; no benchmark is needed. Final affected test-only no-incremental verified-reference build passes first execution, zero warnings/errors (~3seconds). Inventory200>30/80>50/120band; counter148 unchanged,19open scopes. Successful installation and changed-state rollback remain unverified; no Store/full-suite/global acceptance. Evidence: `artifacts/crap/install-preparation/verification.json` and focused artifacts.

## Setup cleanup guards: unchanged production (November 24, 2026)

Nine new ordinary cases plus15 existing non-explicit tree/inventory cases pass first execution,24total/0failure/0skip,339ms. Owned temporary fixtures prove no-op for non-directory tree paths, exact installation argument/missing-root errors, and root trust validation before locked malformed inventory access. Full relative-path/content/ACL/attribute/write-time snapshots and RestartRequired remain unchanged. No installed directory, service or deferred-reboot API is touched.

DeleteTree72→21.82/CC8 unchanged,2/5lines2/8branches;DeleteInstallation156→125.26/CC12 unchanged,1/13lines0/12branches. These are guard observations, not successful deletion coverage. Three current source/PDB checks,187 unchanged helper shapes and all80 production DLL hashes establish unchanged production behavior/performance; no timing experiment is needed. Final test-only no-incremental verified-reference build is warning-free (~3seconds). Inventory202>30/82>50/120band; counter148 unchanged,18open scopes. Successful protected deletion, IO failures and reboot scheduling remain unverified; no Store/full-suite/global acceptance. Evidence: `artifacts/crap/setup-cleanup-guards/verification.json` and focused artifacts.

## Installed registration capture: unchanged production (November 24, 2026)

One new explicit installed-registration case verifies a read-only capture, exact values/types, independent result dictionaries and unchanged registry values/security/caller-owned handle. It passes both initial and strengthened executions (final27ms); two existing pure descriptor cases pass separately. No service AllAccess/control, registry write, elevation or installation. The actual installed4.1.0 version is preserved rather than replaced by repository4.2.0.

Capture CRAP72→8.12, CC8 unchanged;7/8lines5/8branches (87.5%/62.5%). Missing/untrusted/mismatched/omitted-value paths remain open. One explicit report only supplies fresh metrics; ordinary security coverage is not merged. Source/PDB checksum and all80 production DLLs match;187 helper method shapes unchanged. A final affected test-only no-incremental build reuses verified references and passes with zero warnings/errors (~3seconds). Initial test-only RegistryKey property-name compile error is fixed and retained. No production performance experiment is needed for identical production bytes. Inventory203>30/83>50/120band; counter148 unchanged;17open scopes. This is installed-machine evidence, with no new Store/full-suite/global acceptance. Evidence: `artifacts/crap/installed-registration/verification.json` and retained focused artifacts.

## Setup handshake: tests only, owned pipe failure paths (November 24, 2026)

No production source or DLL changes: all80 monitored paths match, and Program/SetupService/SetupHost/PipeRequesterIdentity source checksums match their portable PDB. Performance is preserved by identical production bytes, so no timing experiment is warranted. The final no-incremental test-only build reuses verified references and succeeds in~3seconds with zero warnings/errors.

17 new cases in six test methods plus nine existing argument/identity cases pass outside the sandbox:26/0failure/0skip,159ms. The initial restricted run fails17 cases including the existing identity test; its reports remain separate. No Main/elevation, service/registry/protected-install mutation or successful setup transaction is executed. Handshake CRAP420→32.75/CC20 unchanged,28/41lines12/20branches; collateral BeginAsync210→137.43/CC14,3/21lines5/14branches. The handshake meets revised50 but remains strict>30 and misses secondary coverage targets. Successful transactions, commit/rollback, package trust and process-death boundaries remain open. Current inventory204>30/84>50/120band; full-characterization counter148 unchanged. No new Store/full-suite/global acceptance claim. Evidence: `artifacts/crap/setup-handshake/verification.json` and retained focused reports.

## Native service guards: tests only, verified-reference build (November 24, 2026)

24new parameterized cancellation/invalid-handle cases plus13existing service-security/configuration cases pass first execution,37total/0failure/0skip. Stop72→35, Wait110→44.3, WaitRemoved72→11.08, complexity8/10/8 unchanged. All meet revised≤50; Stop/Wait remain original>30 findings. Partial line25%/30%/63.6% and branch0%/0%/37.5% coverage does not establish valid state/control/polling/timeout behavior. Counter148 stays unchanged; fifteenth open scope retains those gaps.

No production change: source and all80 monitored DLLs match. Existing helper source matches its referenced portable PDB; `BuildProjectReferences=false` limits the no-incremental build to the changed tests (~3seconds), with zero final warnings/errors. Initial Category ambiguity is fixed in tests via a Win32Exception alias. Native calls receive only zero/closed/null handles, and pre-canceled paths perform no native query; no real service or registry is changed. Production performance is preserved by identical bytes, with no new benchmark, Store/full-suite repetition or global coverage claim. Inventory204above30/85above50/119through50. Evidence: `artifacts/crap/native-service-failure-guards/verification.json` and focused reports.

## Hardware refresh: owner selector, original native worker retained (November 24, 2026)

RefreshAsync now selects a reader before its original Task.Run; default selection returns the original cached WindowsHardwareInfo.Read. An owner-keyed weak table is created only by tests; no window/constructor/async field, per-call delegate or Task is added. The original static cache field moves to the selector with identical type. Clock/UI/closed/log/finally behavior is unchanged.61Desktop/40Store focused tests pass, including16new cases; primary CRAP272→guard248.38→14 with63/63lines14/14branches. CC16→14 reflects relocation of two compiler cache branches; added selector/scope12CC means combined16→26, not business-decision removal.

The first call-through wrapper is rejected/superseded. Native MTA readonly before/after reads have186984B/op medians and latency227947.85→254073.35µs; ratio1.14469/95%CI0.83699–1.56550 fails the frozen upper1.10 certainty requirement. All8pairs/16PID-start and112time/112allocation samples remain. Untimed96-byte JIT shows an extra native-call frame; the single design follow-up removes it and retains the exact original worker delegate. Native readonly preflight returns the same seven section labels without recording private hardware details; normal/double-dispose restoration was verified for the first wrapper, while final selector unit tests verify fallback by native Method/Target without invoking hardware.

The separately frozen follow-up measures original C#native-methodgroup selection versus owner selection, each with the same Task.Run/noop/completion and identity/work-count validation.8alternatingpairs/16freshPID-start,500mswarmup, seven40ms-target batches/min16operations, tieringoff, all112time/112allocation samples retained. Paired ratio0.74261/CI0.56870–0.96971; allocationCI0.92082–1.00774, process-median allocations161.568→157.907B/op. Fixed lower95>1 rejection and upper95≤1.10 certainty gates pass. Scheduler-inclusive intrinsic workload only: no claimed production speedup/allocation reduction, WMI wait, UI repaint, cold startup or whole-app equivalence. No identical retry/drop/gate relaxation or build/test/coverage/profiler overlap; the first native result remains separate.

149 original PDB documents/resources/two core dependencies/window and async field sets match;8expected app copies change,72other DLLs unchanged. Final test/app/friend builds are warning-free; friends compile against already verified current references instead of rebuilding dependencies. Native clipboard, update handoff, guide navigation and full shown-window live-device races remain open. Inventory205above30/88above50/117through50; counter148. Evidence: `artifacts/crap/info-refresh-reader/` and `selector-followup/` (source/layout/PDB proofs, both raw experiments, frozen protocols and final verification).

## Info status and refresh guard: tests only, unchanged production (November 24, 2026)

Original owned modal Info workflow is exercised with a scoped initial disabled-window style: Loaded takes the original refresh guard, then the test enables the window and asserts real status, availability, busy controls, event transitions/check-without-client and subscription cleanup.10new Desktop/4shared Store cases;45Desktop/24Store focused tests pass first execution, no failures/skips. No WMI/native inventory read, clipboard/updater launch, external HTTP request or user preferences are used. Initial test-only delegate-assertion nullable warnings are corrected; two final affected test builds are warning-free.

MainWindow status callback72→8/CC8/full6lines8branches(Default); caller42→6/CC6/full13lines but3/6branches, so owner guide navigation remains open. Native RefreshAsync272→248.38/CC16 with only2/63lines1/16branches; positive reading/render/error/Ready paths remain unverified. Store follows legal idle/unavailable policy without injected offers; its callback branch coverage4/8 is separate. Only the independently characterized Default callback promotes the full-method counter to147.

All80 monitored production DLLs and both owning sources match baseline bytes;3529 unrelated loaded method shapes match. Production performance is preserved by identity; no timing run, speedup, separate unchanged app/friend rebuild, full suite/global coverage or PNG/whole-app acceptance. Inventory206above30/89above50/117through50;14open scopes,3Storefindings and all prior rejections/theme/export63 retained. Evidence: `artifacts/crap/info-update-callback/verification.json`, focused reports and `open-verification.json`.

## Network setup extraction: tests only, production bytes unchanged (November 24, 2026)

One shared six-case fixture executes owned-directory Windows ACL, cancellation/order, file-root failure, missing-resource and no-overwrite contracts against GUI/CLI Extract copies.146GUI/140CLI focused cases pass first execution, zero failures/skips; two no-incremental affected test builds have zero warnings/errors. Both methods72→8.44/CC8unchanged,17/21lines5/8branches. Branch62.5% and successful-copy/critical90% gaps remain because no signed assets are embedded in these developer builds; no helper is executed and no product resources/behavior change.

Actual source and all80 monitored production DLL copies match the baseline byte-for-byte. Production performance is preserved by identity; no new timing experiment, speedup/throughput assertion, separate app/friend/Store build or full suite was needed. Store excludes the native fixture through its compile condition; no new Store coverage claim. Two numeric findings removed, full-characterization count146 unchanged, signed-bundle verification pointer retained. Inventory208above30/90above50/118through50; prior export withdrawal and other risks remain. Evidence: `artifacts/crap/network-setup-extraction/verification.json`, `production-before.json`, `embedded-assets-observed.json` and focused reports.

## Export capture withdrawn: separate UI-preparation measurement (November 24, 2026)

The single targeted diagnostic follow-up is complete; the extraction is withdrawn. Original MainWindow.Export.cs and all80 monitored DLL paths are restored byte-identically after four clean affected test/friend builds. Existing adopted test evidence is reused for those same bytes; candidate70/70 export cases also passed. RootCRAP63 remains and no count is promoted.

A separate frozen protocol measures actual ExportButton.RaiseEvent until suspension, including chooser/save construction and snapshot/Task.Run work. Completion is outside that interval but every real controlled export is gated, completed and checked. Same original-compiled bridge on both sides,12workloads/sixkinds×retained-range;8pairs/16freshPID-start,500mswarmup, seven40ms-wall-target batches/min16operations, tieringoff. All12 fingerprints match. All1344preparation times/allocations and1344completion times/cycle allocations retained, as are all original end-to-end observations. No identical retry/drop/gate relaxation or build/test/coverage/profiler overlap.

Preparation medians~44–51µs; exact matching allocation medians944–1576B/op. The predeclared stricter upper95≤1.10 gate fails six workloads, max **1.65519** (NetworkTraffic/range). Original lower95 latency/preparation-allocation rejection is not triggered. Full-cycle Connections/retained UI allocation lower951.00000588 is above1 (2544.24291→2544.69505B/op medians), another failed check retained without claiming a production root cause; maximum cycle allocation upper951.00073595. Untimed1328-byte FullOpts factory JIT does not establish preservation. No performance acceptance/speedup claim.

Follow-up limit reached under the faster workflow: archive the candidate and move on, retaining OnExport63 and native Save/positive CSV integration gaps. Current source has no pending candidate. Evidence: `artifacts/crap/window-export-capture/ui-preparation/` and `restoration-verification.json`.

## Export capture historical checkpoint: end-to-end timing inconclusive (November 24, 2026)

Historical pending checkpoint, superseded by withdrawal above; no canonical remediation/count promotion. OnExport63→30 plus cohesive CsvExportCapture.Create35; total parent/factory CC63→65.70 identical existing characterization cases pass each variant, both explicit test/app and benchmark-friend builds are clean, reverse inlining reconstructs the source, same11 worker fields/no Window capture,149 original PDB inputs/resources/two dependencies unchanged. Worker field order changes, recorded separately.

A single frozen8-pair/16-fresh-PID-and-start experiment uses one bridge compiled against the adopted seam baseline. Actual ExportButton.RaiseEvent drives six kinds ×retained/range, four fixed real samples/captures including exact cutoff and neighboring Stopwatch ticks. Controlled picker returns an owned never-created destination; worker computes a payload digest. A reusable gate forces handler suspension; after it returns, IsClosing suppresses only the notice, then the worker is released and async-void completion/empty tracked writes are required before restoring IsClosing. No native Save, CSV write, hardware query, sampling, user preferences or copied capture algorithm. All12 original/candidate fingerprints match.

.NET10.0.15x64, tiering disabled,500ms warmup, seven40ms-target batches/min16operations per scenario. All1344latency/1344UI-thread allocation observations and calibration counts are retained. No timed retry/drop/gate change or owned build/test/coverage/profiler overlap. Fixed statistical rejection gate passes. Allocation medians are effectively equal (2320–2952B/op), maximum paired allocation upper95%1.00011331; background-process allocations are outside that measurement.

**Performance acceptance remains pending.** Maximum latency upper95%1.81636 (Services/retained), Usage/retained1.78831. Original Services pair4 median4718.056µs and Usage pair1 median5222.541µs contrast with typical~200µs. End-to-end scheduling/wait noise is a hypothesis, not an established external cause; all slow results remain. These bounds cannot support reliable preservation or a speedup claim. Investigate a separately frozen decomposition of synchronous UI capture versus worker/dispatcher waiting rather than retry this identical timing. Native positive I/O, selected/archive timing, cold startup and whole-app performance remain unmeasured.

Evidence: `artifacts/crap/window-export-capture/performance-protocol.json`, `performance-inputs.json`, `worker-{before,after}-0..7.json`, `perf-0..7.json`, `performance-summary.json`, `performance-raw-verification.json`. Current source/build candidate is explicitly identified in inventory-context.json.PendingCandidate; adopted inventory remains210above30/92above50/118through50.

## Export I/O gateway, default dialog/write forwarding (November 24, 2026)

The internal scoped gateway preserves real chooser/save/writer defaults and existing worker fields. Full controlled producer characterization lowers OnExport2602.86→63, with CC65→63 because two chooser conditions move into the gateway; the parent still exceeds50. Seven new methods add14CC, making the measured family75→87. No snapshot extraction is adopted yet. Default execution creates no override provider/slot; FullOpts disassembly shows the writer tail-calling the original CsvExport.Write.

One frozen8-pair/16fresh-PID-and-start protocol on .NET10.0.15/x64/TieredCompilation=0 uses500ms warmup, seven40ms target batches and at least16operations. The same bridge compiled against the original app drives both builds; actual writer targets/app paths/MVIDs are checked. Real chooser paths use the existing internal ExportButton routed click, hidden windows with activation disabled in properties, Send-priority cancellation/accepted-null, and dispatcher draining. They stop before the native Save dialog. The invalid NUL-path writer case stops before file creation. All336latency/336allocation samples and outcome fingerprints are retained; no retry, discarded sample, changed gate or owned build/test/coverage/profiler overlap.

| Default workload | Before median µs/op | After median µs/op | Paired ratio |95% CI| Before/after B/op |
|---|---:|---:|---:|---|---:|
| Chooser cancel |60507.42|60668.72|1.00607|0.95617–1.05856|586700.25/586625.50|
| Chooser true/null choice |61035.89|59836.74|0.96320|0.91912–1.00938|581362.75/581249.25|
| Native writer invalid path |4.1322|4.1246|1.01855|0.94327–1.09983|1160/1160|

Ratios are geometric paired-process estimates, not ratios of separate medians. The unchanged gate rejects a lower95%latency/allocation bound above1; all three pass. No statistically detected regression. Latency uncertainty reaches roughly9.98% in the small writer workload and5.86% for cancel; WPF allocation varies, with maximum upper95%allocation bound1.001057 (roughly0.106%). Do not interpret variable chooser medians as exact allocation identity or a speedup.

Measured scope is warm default validation and chooser cancellation/null choice. Successful native Save/CSV writing/throughput, producer extraction, cold startup, focus/appearance and whole-app behavior remain unmeasured. Final213Desktop/184Store cases pass; native ShowSave's default branch remains unexecuted. Source/PDB/resource/state/capture proofs and eight expected app-copy changes versus72 unchanged production copies accompany the run. Raw inputs/protocol/workers: `artifacts/crap/window-export-io/`. A subsequent snapshot extraction must benchmark the actual controlled producer on both versions using this seam-only baseline.

## Export-entry characterization, production bytes unchanged (November 24, 2026)

Ten new original-behavior cases characterize export selection/guards/cancellation without reaching the native Save dialog or CSV writer. Focused153Desktop/124Store workflow/dialog cases pass, with clean no-incremental affected test/app/helper builds. The shared test-only async-void context is renamed at two references; its body is unchanged. OnExport's CRAP decreases4290→2602.86 through coverage alone, with CC65 unchanged; it remains above50.

MainWindow.Export.cs, all3527 loaded production method shapes and all80 monitored production DLL copies match the retained baseline. This is production-byte identity evidence, with no timing benchmark, speedup or whole-app performance claim. A future picker/writer seam and cohesive snapshot extraction must establish new before/after performance evidence. Inputs, original/final TRX, variant coverage, identity verifier and remaining write-path gaps: `artifacts/crap/window-export-entry/`.

## Service-action default forwarding, scoped I/O seam (November 24, 2026)

The service-action handler changes only its native execute/start calls to an internal scoped forwarder. Control flow, worker captures, Task.Run, exception filters and helper lifetime code remain unchanged. The AsyncLocal override slot is created only by the internal test hook; default production execution allocates no provider/scope object. Six new methods have CRAP1–2; the handler moves1056→84.47→32.64 with CC32 unchanged, meeting revised50 and retaining original30.

One frozen protocol measures original static APIs versus default forwarders in8alternating pairs/16fresh PID-and-start lifetimes on .NET10.0.15/x64, TieredCompilation=0. Workloads have500ms warmup and seven40ms target batches. All336latency/336allocation samples and fingerprints are retained; the same compiled bridge drives both versions. No timed retry, dropped sample, gate adjustment or owned build/test/coverage/profiler overlap. Workloads target both changed forwarders with deterministic validation failures before native I/O, selected before timing.

| Default workload | Before median µs/op | After median µs/op | Paired ratio |95% CI| Before/after B/op |
|---|---:|---:|---:|---|---:|
| Invalid service action |3.1287|3.1179|1.00336|0.98046–1.02680|1160/1160|
| Missing helper filename |1.7721|1.7815|1.00467|0.98872–1.02088|752/752|
| Null helper request |2.1216|2.1418|1.00203|0.97976–1.02482|656/656|

Ratios are geometric paired-process estimates, rather than ratios of the separately reported medians. The unchanged gate rejects any lower95%latency/allocation bound above1; all three pass. No statistically detected regression; the largest upper bound allows roughly2.68%latency uncertainty. FullOpts disassembly shows the default start wrapper tail-calling Process.Start; no provider allocation occurs on the uninitialized default path. Original validation-exception allocation stays equal.

This measures warm pre-I/O forwarding with no override. Successful native service/elevation, process startup/wait/exit/disposal, cold JIT and whole-app performance remain unmeasured. Controlled tests substitute completion/errors and launch null/throws without starting a process. Broader143Desktop/114Store caller cases pass; final strengthened44/44 service runs and affected builds are clean. PDB/source/resource/closure proofs accompany eight expected app-copy changes versus72 unchanged production copies. Inputs, raw workers, verifiers and native gaps: `artifacts/crap/window-service-action-io/`.

## Service-action characterization, production bytes unchanged (November 24, 2026)

Thirteen new service-action characterization cases and112Desktop/83Store focused regression cases pass against unchanged production. Both affected no-incremental test-project builds, including app/helper dependencies, have zero warnings/errors. The service action's CRAP falls1056→84.47 through coverage alone, with CC32 unchanged; it remains above50. Successful SCM/elevated-helper behavior is still unverified.

All80 monitored production DLL paths and3521 loaded production method shapes match the retained baseline; MainWindow.Services.cs is byte-identical. No production algorithm, dispatch, allocation, dependency or native operation is added. This is byte-identity evidence, with no timing benchmark, speedup, whole-app performance acceptance or native/UAC claim. Before/after measurements remain required if a subsequent I/O seam changes production. Raw builds, original/final TRX, variant coverage and the identity verifier are under `artifacts/crap/window-service-action/`; fixture failures and the sandbox registry-denial run are retained.

## Settings modifiers, native dispatch inlined (November 24, 2026)

The minimum per-call modifier seam initially adds a dispatch hop to the native-default event. The first fixed8-pair/3-scenario run rejects busy/non-Tab ratios1.1218/1.0986 (lower95%1.0769/1.0370), despite zero allocation. Disassembly identifies a12-byte register/tail-jump thunk. An inlining hint on the new core removes that hop in FullOpts.NET10.0.15: the event contains the default logic directly,307bytes versus323original. This changed candidate is measured separately; the rejected samples remain intact under `artifacts/crap/settings-navigation/`.

The corrected8-pair/16-worker run retains all336latency/336allocation samples, with distinct PID/start lifetimes,500ms warmup, seven40ms target batches and the same lower95%ratio>1 rejection rule. No timed retry, sample deletion, gate change or build/test/coverage overlap. The inherited template metadata field SummaryCallsPerOperation in the first run denotes one dispatch invocation; frozen benchmark source proves no terminal summary call. The corrected run names it DispatchCallsPerOperation before freezing inputs.

| Native-default path | Median before/after | Ratio | 95% interval | Bytes/op |
|---|---|---:|---|---:|
| Busy Tab |1.9239/1.9204ns|0.9776|0.9298–1.0279|0/0|
| Other key |1.9336/1.9396ns|1.0308|0.9638–1.1024|0/0|
| Plain Tab |2.7742/2.6267µs|0.9528|0.8202–1.1069|0/0|

All state fingerprints match and no latency/allocation regression is statistically detected. The largest upper bound leaves10.7%uncertainty. Scope is warm default guard dispatch on an unshown window/inactive keyboard, not native held-Ctrl/focus UI operations, cold JIT or whole-app equivalence. No physical keys or system input overrides are injected.

Eight original guard cases pass both variants before production editing;15 controlled cases then cover per-call Ctrl/Shift direction/wrap/extra flags and realized/unrealized sidebar behavior. Final248Desktop/196Store shared Settings cases pass with clean affected app/test/helper/benchmark builds. Two shared-STA keyboard precondition failures are corrected by owned STA isolation without relaxing assertions; raw failures are retained. Event156→1/core16, full9reported lines and15/16branches, combined CC12→17: a testability cost, not complexity reduction. Native Shift fallback remains unverified; counter146 stays unchanged. Source reconstruction is exact;147/148Default PDB documents, resources/two dependencies,27nested layouts and3519unrelated loaded shapes remain unchanged. Eight app copies change and72other monitored copies match. No full-suite/global coverage/new PNG acceptance. Evidence: `artifacts/crap/settings-navigation-inline/` and the remediation report.

## Detached chart mouse characterization, unchanged production (November 24, 2026)

CpuGraph.OnMouseMove272→16, CC16 unchanged,22/22reported lines and14/16branches in separate Desktop/Store measurements. Seventeen new cases use real routed events on detached visuals, asserting a fixed origin before every event; guards, marker precedence, selected-history clearing, exact live tooltip text, inclusive nearest-time boundary and missing-data states are verified without global pointer movement or a product seam. Initial15/17 failures were two incorrectly clocked marker fixtures; corrected17/17 Desktop and first17/17 Store pass. Final135Desktop/135Store focused chart tests pass, zero failures/skips; both final no-incremental affected test builds have zero warnings/errors.

CpuGraph source, all3520 loaded WPF/shared shapes and all80 monitored production DLL copies match the current accepted CLI-refactor baseline. Performance is preserved by identical production bytes; no new timing benchmark, speedup or whole-app equivalence claim. Two interior logical-processor tooltip arms remain unexecuted despite100%reported line coverage; native/high-DPI/arbitrary-position behavior remains open, so fully characterized count stays146 and a ninth verification pointer retains the gap. No full-suite/global coverage or new pixel/native input acceptance. Evidence: `artifacts/crap/cpu-mouse-move/` and the remediation report.

## Terminal panel composition, allocation escape corrected (November 24, 2026)

The earlier ComposePanels candidate remains rejected for64extra bytes/op. FullOpts.NET10.0.15 disassembly identifies a five-string label array moving from stack to heap when passed to the helper. An untimed ReadOnlySpan<string> probe still escapes; the adopted helper instead consumes five stack-allocated integer widths. Label-array stack allocation is restored, the composition body is otherwise unchanged, and all five generated closure layouts match.

One new fixed8-pair/16-worker run uses the same six workloads,500ms warmup, seven40ms batches and lower95%ratio>1 rejection gate. Workers have distinct PID/start-time identities; all672latency and672allocation samples and six full true-color fingerprints are retained. No build/test/coverage overlap, timing retry, sample deletion or gate change.

| Scenario | After/before latency ratio | 95% interval | Bytes/op, both builds |
|---|---:|---|---:|
| Short ASCII |0.9796|0.9319–1.0297|14256|
| Tall Unicode |0.9954|0.9822–1.0087|23128|
| Wide Unicode |0.9723|0.9367–1.0092|114936|
| Wide ASCII |1.0047|0.9787–1.0314|115848|
| Missing readings |0.9997|0.9800–1.0199|109000|
|501-point history |1.0027|0.9718–1.0345|174936|

Allocations match exactly and no latency regression is statistically detected. The widest upper bound allows roughly3.45%slowdown in that workload; no universal speedup/equivalence claim. Current234 original tests pass both variants before refactoring; final246Desktop/246Store focused tests pass with zero failures/skips and clean no-incremental affected app/test builds. Summary56→42 and helper14, all89family lines/71branches covered, combined CC75 unchanged; parent42 stays above the original30 target. CLI PDBs show only TerminalCharts.cs changed among38documents,37unchanged; resources/four dependencies and all1638 unrelated loaded shapes match. Four CLI copies change and76other monitored production copies remain identical. No full-suite/global coverage or WPF appearance acceptance. Evidence: `artifacts/crap/terminal-summary-allocation/`; prior rejection under `terminal-summary/` remains intact.

## Own-window recovery characterization, unchanged production (November 24, 2026)

RecoverWindowPosition420→20.4/CC20 unchanged,9/10lines18/20branches; WindowPositionHook210→21.26/CC14 unchanged,8/12lines6/14branches. Twelve shared new cases use an owned HWND and real GetWindowRect/SetWindowPos to verify guards, clamping, missing-target fallback, unchanged dimensions, user-move ownership and remembered location. Initial original10/12 failures were native-zero assertion type mismatches; corrected12/12 Desktop and first12/12 Store pass. Final63Desktop/34Store focused tests pass, zero failures/skips; both final no-incremental affected test builds have zero warnings/errors.

Production source, shared fixture, all3520 loaded production shapes and all80 monitored production DLL copies are byte-identical. No production structural/hot-path change to benchmark; performance is preserved by identity, with no speedup or whole-app equivalence claim. Test windows do not activate, no display setting or global input changes. Win-key/deferred handling, native move-failure warning and actual detach/reconnect/multi-DPI behavior remain unverified; hook secondary coverage targets remain unmet. Both numeric findings resolve but fully characterized count stays146; a new open-verification pointer retains the gaps. No full-suite/global coverage or new PNG acceptance. Evidence: `artifacts/crap/window-position-native/`.

## Interactive-loop characterization, unchanged production (November 24, 2026)

The CLI live callback falls2484.99→302.86 with CC56 unchanged,28/49lines and35/56branches; sampling-options callback42→6 with CC6 unchanged,3/3lines and3/6branches. Twelve new shared Desktop/Store cases exercise actual Spectre rendering and loop input, delivery, quit/cancellation, settings save/no-save, failure cleanup and bounded input batching. All original executions and final29Desktop/29Store focused cases pass, zero failures/skips; corrected no-incremental affected test builds have zero warnings/errors. Initial two test-only compile mistakes and raw failed build are preserved in the remediation report.

InteractiveMonitor source, all1646 loaded CLI/shared production shapes and all80 monitored production DLL copies are identical. Production performance is preserved by those bytes; no timing benchmark or speedup claim. Real50ms loop waits remain; elapsed test duration is not a performance measurement. Hardware discovery and network capture lifecycle are intentionally unexercised, with the callback still302.86above50 and the options network branches incomplete. Fully characterized count stays146; no full-suite/global coverage, native lifecycle or new appearance acceptance. Evidence: `artifacts/crap/interactive-loop/`.

## Settings-entry characterization, unchanged production (November 24, 2026)

MainWindow.OnSettings702→26, CC26 unchanged,38/38lines and19/26branches in separate Desktop/Store measurements. Eight shared new cases exercise real modal results, Light/Dark owned-file saves, generic write failure, lifetime cancellation, section memory, control recovery and stopping a fixture-owned retry timer. Final105Desktop/49Store focused cases pass with zero failures/skips; both final no-incremental affected test builds have zero warnings/errors. Earlier fixture/assertion/sandbox failures and their corrections are retained in the remediation report and raw artifacts.

Source, all3520 loaded production shapes and all80 monitored production DLL copies are byte-identical. Performance is preserved by that identity; no timing benchmark or speedup claim. MainWindow UI tests now share one STA fixture; prior update cases still pass. Diagnostic mode and owned discovery/persistence boundaries exclude native startup writes, hotkey/controller integration and live enumeration. Those gaps keep the fully characterized counter146 unchanged despite the numeric resolution. No full-suite/global coverage or new appearance acceptance. Evidence: `artifacts/crap/settings-entry/`.

## Update-entry pre-staging characterization, unchanged production (November 24, 2026)

Default OnUpdateNow110→10/CC10 unchanged,8/8lines10/10branches; DownloadAndLaunchAsync272→16.86/CC16 unchanged,34/40lines15/16branches; LatestOfferAsync20→4/CC4 unchanged,9/9lines4/4branches. Thirteen Desktop/two Store new cases cover actual entry guards, queued busy/reentry/withdrawal, completed retry, signed newest/fallback policy, owned/foreign cancellation, closing and semaphore ownership. All first original runs and final46Desktop/13Store focused tests pass, zero failures/skips; both final no-incremental affected test builds have zero warnings/errors.

The real UpdateClient verifies signed metadata over in-memory HTTP; a supplied unpublished process path stops attempts before staging. A fixture-owned installation-lookup counter is checked after cleanup outside the product's catch-all, and every candidate directory stays absent. No payload, staging, native helper/UAC/browser or actual restart. Test-only optional factory inputs preserve prior defaults, with a strict helper diff guard. Tracked coordinator tasks and the async-void UI tail finish before cleanup; leases and pending cancellation resources are owned and released.

Source, all3520 loaded production shapes and all80 monitored production DLL copies are byte-identical. Production performance is preserved by that identity; no new timing benchmark, speedup or whole-app equivalence claim. Successful handoff/progress remains unverified and the coordinator's85%line coverage misses the critical90%target, so only the entry increments the fully characterized counter. Separate Store observations34.41/272 remain findings because ordinary policy supplies no offer; neither is suppressed. No full-suite/global coverage or new appearance acceptance. Evidence: `artifacts/crap/update-entry/` and the remediation report.

## Update-text refresh characterization, unchanged production (November 24, 2026)

Default RefreshUpdateText72→8/CC8 unchanged,4/4lines8/8branches; separate Store observation9/CC8,3/4lines7/8branches, with its positive offer branch unavailable through ordinary Store policy. Seven Desktop/four Store new cases cover three guards, no-offer notice, retained signed-offer formatting, stable actions and the actual ApplyLanguage caller. Final focused33Desktop/11Store tests and both final no-incremental affected test builds pass, zero failures/skips and compiler warnings/errors.

The initial six/four cases pass first execution. The added full-caller case reveals stale cross-STA language subscriptions in hidden test windows; explicit Unloaded cleanup for their four views corrects the test helper. The failed TRX remains; no assertion or production code changed. Actual ApplyLanguage reproduces the existing capture-notice overwrite with update actions still hidden; it is documented and preserved. The test restores resource/thread/default-thread UI cultures. No physical language/capture flow or native integration claim.

Both owning source files, all3520 loaded production shapes and all80 monitored production DLL copies remain byte-identical. Production performance is preserved by that identity; no timing benchmark, speedup or whole-app equivalence claim. No external network, download, staging, helper/UAC/browser launch, updater directory, user files, full suite or global coverage claim. Evidence: `artifacts/crap/update-text-refresh/` and the remediation report.

## Window update-check characterization, unchanged production (November 24, 2026)

CheckForUpdatesAsync1332→36/CC36 unchanged,35/35lines34/36branches; original>30 finding retained at its complexity floor while meeting the revised50 maximum. Presentation helpers ShowUpdateFailure20→4 and ShowUpdateOffer6→2 become fully covered; ShowUpdateStatus4.03 remains7/8lines3/4branches. New19Desktop/4Store cases (20unique/10source methods) cover actual signed offers over in-memory HTTP, guards, no-offer policy, dismissal/version changes, withdrawal/capture-footer precedence, closing, gate ownership/serialization, cancellation, generic failures and preserved retry actions. All original first executions and final focused26Desktop/7Store tests pass, zero failures/skips; conditional identities are audited. Both final no-incremental affected test builds have zero warnings/errors.

No production code or updater policy changes. Source, all3520 loaded production shapes and all80 monitored production DLL copies are byte-identical. Production performance is preserved by that identity; no timing benchmark or speedup claim. Tests use hidden startup-detached windows, scoped UI resources/context, owned TCS/CTS/semaphore cleanup, actual UpdateClient signature verification and existing signed fixtures. Private-name coupling is limited to the callback/client/downloading/gate boundaries. No external network, download, staging, helper/UAC/browser launch or updater-directory creation. Subscribers, exact clock notification, native/background integration, full suites and global/Store coverage percentages remain unverified. Evidence: `artifacts/crap/window-update-check/` and the remediation report.

## Daily-usage recording characterization, unchanged production (November 24, 2026)

RecordDailyUsage CRAP72→8/CC8 unchanged,6/6lines8/8branches. CheckpointUsageArchive6.84→6/CC6 unchanged,7/7lines6/6branches. Eleven new cases cover exact minute boundaries, backwards sample time, diagnostic/archive guards, existing null behavior, accumulation during pending I/O, failed-checkpoint logging/recovery and unchanged invalid-observation scheduling. All first-pass original cases and46 focused Desktop/Store persistence/shutdown cases pass, zero failures/skips; both no-incremental affected test builds have zero warnings/errors.

The existing temporary PreferenceStore and real archive lock isolate all file activity. Test-only reflection binds the private sampling callback and controls/awaits its tracked Task; no production fixture seam, field or hot-path edit. Source, all3520 loaded production method shapes and all80 monitored production DLL copies remain identical. Production performance is preserved by byte identity; no new timing benchmark, speedup or whole-app equivalence claim. Actual live-sampling event delivery, native collection, user archives, full suites and global/Store coverage percentages were not verified. Evidence: `artifacts/crap/daily-usage-recording/` and the remediation report.

## Usage file-location characterization, unchanged production (November 24, 2026)

UsageView.OnOpenFileLocation CRAP110→10/CC10 unchanged,4/4lines10/10branches. Its OpenLocation dependency20→5.5/CC4 unchanged,6/11lines1/4branches; native Explorer launch/disposal and other launch failures remain unverified. Ten new cases exercise six selection guards and four nonexistent-path notices through the real button/modal/Close action. All first-pass original cases and47 focused Desktop/Store cases pass, zero failures/skips. Final fixtures prewarm their read-only icon tasks before row binding; both final no-incremental affected test builds and47-case runs pass with zero compiler warnings/errors.

Source, all3520 loaded production method shapes and all80 monitored production DLL copies remain identical. No production structural/hot-path change exists to time; no new benchmark or speedup claim. All candidate paths fail File.Exists; none are created and Explorer is never launched. No settings/service mutation, native input, PNG, full suite/global coverage or whole-app profiling claim. Native file-location launch remains a separate open verification scope. Evidence: `artifacts/crap/usage-file-location/` and the remediation report.

## Example-row icon characterization, unchanged production (November 24, 2026)

ProcessRow.SetExampleIconAsync CRAP156→12,CC12 unchanged,7/7lines and12/12branches; its existing idle callback2→1/CC1,1/1line. Eleven new cases cover no usage/null path, dispatcher-idle ordering, missing-path fallback, PID/creation changes, same-lifetime metadata/grouping and nullable identity after an Update failure. Initial nine cases and105 focused cases per variant pass; two additional partial-state cases pass with all callers107/107 in Desktop and Store, zero failures/skips. Both final no-incremental affected test builds have zero warnings/errors. Original first-pass evidence is retained; no test expectation or production behavior changed.

The source, all3520 tracked loaded production method shapes and all80 monitored production DLL copies are unchanged. No structural/hot-path production edit exists to time: production performance is preserved by byte identity; no new benchmark, speedup or whole-app equivalence claim. New tests use hidden rows, frozen image sentinels, invalid synthetic PIDs, prewarmed provider tasks and a bounded real dispatcher-idle handoff. Existing focused provider tests exercise current-process icon extraction. Actual delayed icon I/O, example captures, native input and full-suite/global coverage were not verified. Evidence: `artifacts/crap/example-icon/` and the remediation report.

## Application-exit characterization, unchanged production (November 24, 2026)

App.OnExit CRAP110→10,CC10 unchanged,10/10lines and4/10branches in separately collected Desktop/Store coverage. Seven new cases pass their first original execution and both focused variant runs; four final no-incremental host/test builds have zero warnings/errors. A test-only child executable runs actual WPF shutdown while bypassing product startup; exit codes, subscriber code mutation, reentrant shutdown, original exception identity and trace sequencing are verified. There are no production changes.

All74 previously monitored production DLL paths and the App source are byte-identical to baseline. Six additional production dependency copies in the two host output folders match their corresponding tested assemblies;80 unique production paths are now monitored. All3520 loaded production shapes match. Production performance is preserved by source/assembly identity; no benchmark or speedup is claimed. Owned activation/mutex/log and active updater cleanup remain unverified;40% branch coverage misses the target and the fully characterized counter stays141. No real startup/resource ownership, full-suite, native/pixel or whole-app profiling acceptance. Evidence: `artifacts/crap/app-exit/` and the remediation report.

## Settings service-removal characterization, unchanged production (November 24, 2026)

Desktop handler CRAP210→14/CC14 unchanged,23/23lines14/14branches. New16Desktop/2Store cases (17unique names, shared no-callback case) use only injected providers and actual owned confirmations: guarded entry, six failure states, exceptions, busy/reentry/save/status guards, cancellation/late-success precedence, retry tokens and success-save baseline. Initial cases and focused Desktop62/Store39 pass, zero failures/skips; both no-incremental test builds have zero compiler warnings/errors. Existing modal fixture helpers are reused; conditional name differences are audited.

No production source change: all74 unique monitored DLL copies and Settings source are byte-identical,3520loaded method shapes match. No hot-path/structural/allocation change, so production performance is preserved by identity rather than another timing run. Shown Settings parents use injected discovery and synthetic service callbacks; no registry/service/UAC/uninstall, native input, pixels or whole-app profiling. A removal-status wording issue is recorded without changing it.

The separately measured Store handler still reports CRAP107.95/5of23lines3of14branches: Supported is always false there, so its retained removal body is unreachable through legitimate Store behavior. This finding remains explicit and unmodified, not blended into the Default inventory or suppressed. No global/whole-variant coverage acceptance. Evidence: `artifacts/crap/settings-remove-service/validation-summary.json`, `store-target-observation.json`, exact metrics, raw TRX/build logs and original source/DLL hashes. All prior failed timing/allocation observations remain recorded.
## Settings clear-history characterization, unchanged production (November 24, 2026)

Eleven new NUnit4 cases/six methods cover real modal confirmation/dismissal, no subscribers, invocation snapshots, sequential async UI callbacks, closing while pending, synchronous/faulted/canceled/deferred errors and generic notices. Handler CRAP72→8, CC8 unchanged;11/11lines8/8branches. All11 pass original-first; focused Desktop46/Store37 pass with zero failures/skips and two no-incremental test builds zero compiler warnings/errors. The existing Settings-save conditional network cases account for the variant-count difference; all new names match.

Tests-only: Settings source and all74 unique monitored production DLL copies are byte-identical;3520loaded production method shapes match. No structural/hot-path change or new allocation site, so no additional timing run/app/UI-benchmark rebuild. Real modal message windows are logically answered against a hidden standalone Settings parent with fake callbacks; no archive file/deletion, native input, service setup, pixel/full-appearance or global coverage acceptance. Final performance preservation follows source/assembly identity. All prior rejected timing/allocation observations remain recorded. Evidence: `artifacts/crap/settings-clear-history/validation-summary.json`, raw TRX/build logs, exact coverage and production hashes.
## CLI watch-state extraction rejected (November 24, 2026)

Retain25new NUnit4 cases/five methods and unchanged production. Final122focused cases pass Desktop/Store; original watch CC54/CRAP60.64,33/38lines38/54branches (86.8%/70.4%). Final CLI app/test restoration and no-incremental test builds have zero compiler warnings/errors. All74production DLL copies and owning source are restored byte-for-byte. No state helper or generated alias adopted.

Initial candidate moved loading into the local helper and lost the captured warning-delegate cache field. Six created-command workloads/eight pairs yielded8B lower creation allocation and a passing statistical gate, but omitted repeated-command lifecycle and had wide latency intervals. Raw identity guard rejected15distinct PIDs across16worker files (PID57840 reused with different app MVIDs); no original startup stamps exist. Preserve this incomplete identity audit and all measurements; no initial-candidate timing repeat or adoption.

Revision preserves the original Load/lambda/cache and extracts state construction only: archived parent29.21/CC24 plus helper30, totalCC54same. Exact source, nine generated type/field-signature identities via nine explicit renamings,37/38PDBinputs/resources/five dependencies validate. Revised protocol:12workloads (six inputs × created/reused roots),16fresh default-context processes/eight pairs,500mswarmup, seven batches/40mstarget/min16operations, tiering disabled. Full real async CLI Create/Parse/InvokeAsync operation or reused-root Parse/InvokeAsync, controlled settings reads, common original-compiled runner hook, monochrome; full outcomes/input files outside timing. GC.GetTotalAllocatedBytes(precise:true) includes async-thread allocations. PID/start/run identity recorded; all1344latency/1344allocation samples and frozen inputs validate. UTC06:42:14.933–06:45:04.239; no owned build/test/coverage/profiler overlap.

| Revised workload | Before µs | After µs | Paired ratio |95% latency interval | Before/after B |
|---|---:|---:|---:|---|---|
| Hardware, command created |398.951|406.628|1.043912|**1.005328–1.083977**|94040 /94040 |

The preset paired gate rejects that latency observation; cause unisolated. All12 allocation medians and intervals match; other latency intervals include1 (maximum upper1.136071). No source change, cached-allocation proof or outcome equality excuses the failed latency gate. Both candidate versions and every sample are retained; no omission, tolerance change, same-candidate repeat or third attempt. Native interactive monitor, colored/real terminal, cancellation, Store timing and whole-app profiling are outside measurement. The final tests-only production has original bytes, so final performance is unchanged by identity. Further extraction is blocked until the latency observation is explained. Details: `artifacts/crap/watch-state/withdrawal.json`, initial data/identity limitation and `preserved-warning-cache/performance-summary.json`.
## Connection-owner navigation characterization, unchanged production (November 24, 2026)

Twelve new NUnit4 cases/four methods exercise the actual Connections Show in processes button, historical/live owner precedence, path/group versus PID filtering, sample-order representative selection and six stale-lifetime cases. The window callback CRAP72→8/CC8 unchanged,8/8lines8/8branches; with two owner predicates10/10lines8/8branches.180 focused tests pass each Desktop/Store, zero failures/skips; both affected no-incremental test builds have zero compiler warnings/errors.

Tests-only: owning source and all74 unique monitored production DLLs are identical to baseline;3520tracked loaded-module shapes match. Existing hidden STA windows/synthetic session history, sampling startup detached; no new production seam or control operation. Production performance preservation follows source/DLL identity, so no new timing run, app/benchmark rebuild or PNG is required for this test addition. Hidden RefreshPresentation fast path and direct process-frame regression tests do not establish visible-window/hardware/full-appearance acceptance. No full suite/global coverage/native input check; all inherited blockers and the rejected64B panel-allocation result remain recorded. Evidence: `artifacts/crap/connection-owner-navigation/`.
## Storage formatting characterization, unchanged production (November 24, 2026)

Thirty-six shared NUnit4 cases/six methods characterize physical-disk and volume details in both GUI/CLI copies. CLI disk lambda CRAP72→8; both volume lambdas42→6, complexity unchanged. Eight fresh Desktop method observations now cover8/8lines52/52branches; full focused CLI190/GUI187 cases pass in each Desktop/Store variant. Four no-incremental affected test builds report zero warnings/errors.

This is tests-only: formatter source and all74 unique monitored production DLL copies are byte-identical to baseline;3951loaded production method shapes match. No hot-path/structural change or new allocation site, so no additional timing run was needed. Performance preservation follows unchanged production source/assembly identity. No actual device/WMI/storage/terminal, new PNG, cold startup, full suite/global coverage or whole-app profiling acceptance. The preceding rejected panel extraction and its64B allocation increase remain recorded. Evidence: `artifacts/crap/storage-formatting/validation-summary.json`, frozen production hashes, focused TRX/build logs and exact metrics.
## Terminal summary extraction rejected (November 24, 2026)

`TerminalCharts.Summary` candidate CC56→42 plus private panel-row composition14; total56unchanged. Exact source reconstruction, five capture/cache layouts and all234 focused tests in Desktop/Store pass. Full actual true-color render fingerprints match for six workloads. The fixed performance gate nevertheless rejects the candidate: each wide layout allocates64B more per construction. Original source and all74monitored production DLLs are restored byte-for-byte; all234 focused tests pass again in both variants. Final CRAP remains56 with72/72lines56/56branches. Sixteen new cases/six methods retained; no production helper adopted.

Actual original/candidate CLI DLLs, common original-compiled bridge,16fresh default-context processes/eight alternating pairs; short ASCII, tall Unicode, wide Unicode/ASCII, missing and501-entry-history inputs.500mswarmup, seven batches,40mstarget/min16operations, tiering disabled; one actual Summary construction per operation, complete styled rendering outside timing. UTC06:00:51.375–06:02:17.434 November24 (localNovember24). No owned build/test/coverage/profiler overlap; all672latency and672allocation samples/frozen hashes/configurations/MVIDs validate. No retries, omissions, changed tolerances or second candidate. Times below are medians of process medians; intervals use paired log ratios with fixed t7=2.364624.

| Workload | Before µs | After µs | Paired95% latency interval | Before/after B |
|---|---:|---:|---|---|
| Short ASCII |15.98|16.10|0.994006–1.020656|14256 /14256 |
| Tall Unicode |32.33|32.12|0.949913–1.032383|23128 /23128 |
| Wide Unicode |129.04|129.59|0.976421–1.099501|114936 /115000 |
| Wide ASCII |123.54|123.65|0.978869–1.013012|115848 /115912 |
| Missing wide |122.36|122.13|0.976250–1.045830|109000 /109064 |
|501-entry history |192.96|196.31|0.997043–1.030131|174936 /175000 |

The four wide allocation ratios are1.000365848–1.000587156, with degenerate paired intervals strictly above1; rejection is deterministic across all pairs. No statistically detected latency regression, but intervals do not establish equivalence. Source/capture proofs do not explain the extra64B; allocation cause is unisolated. Retain this failed result and require allocation evidence before another structural attempt. Final tests-only production performance is unchanged by source/DLL identity. No actual terminal device, cold startup, separate Store timing, native/hardware rendering, new PNG or whole-app profiling acceptance. Details: `artifacts/crap/terminal-summary/withdrawal.json`, `performance-summary.json`, `performance-raw-verification.json` and raw workers.
## CLI tab normalization policies (November 23, 2026)

`CliTabPreferences.Normalize` CC/CRAP56→26 plus ordering20/tab-restriction10; combined56 unchanged. Including four pin lambdas, whole responsibilityCC77/max26,24/24lines76/76reportedbranches.54new original-first cases/12methods and88focused original/final Desktop/Store cases pass; final CLI app/tests and bridge/worker builds zero warnings/errors. Exact source reconstruction and four closure/cache layouts preserve field/delegate/schema/order semantics.37/38PDBdocuments/resources unchanged; two managed benchmark dependencies identical. Four CLI copies change among74 unique monitored paths;70 others unchanged. No WPF app/UI benchmark rebuild needed.

Actual original/candidate CLI DLLs, one original-compiled common bridge,16fresh default-context processes/eight alternating pairs, six fixed input sets,500ms warmup, seven measured batches,40ms target/min16operations, tiering disabled. Every operation resets nine properties from prebuilt data then invokes Normalize once; reset cost is included in both sides. Full normalized/input JSON fingerprints are checked outside timing. UTC05:45:57.724–05:47:23.252 on November24 (localNovember23). No owned build/test/coverage/profiler overlap, native/filesystem/WPF work, retries, omitted samples or threshold changes. All672latency/672allocation samples/configurations/MVIDs/frozen inputs validate. Times are medians of process medians; ratios paired geometric estimates.

| Input | Before µs | After µs | Ratio | Paired95% interval | Before/after B |
|---|---:|---:|---:|---|---|
| Default |0.087|0.087|0.985196|0.952845–1.018645|88/88|
| Hardware |0.087|0.087|0.997679|0.981204–1.014432|88/88|
| Invalid scalars/null pins |0.086|0.087|1.010917|0.993085–1.029069|88/88|
| Legacy64pins |4.223|4.221|1.002982|0.990161–1.015970|7,024/7,024|
| Cap520raw pins |15.776|15.684|0.990269|0.971887–1.009000|29,672/29,672|
| Service tree |0.438|0.438|1.004337|0.989897–1.018987|776/776|

Fixed gate rejects paired latency/allocation95% lower bound>1 or newly introduced zero-path allocations. All six pass with allocation intervals exactly1. No statistically detected regression; bounds permit2.91% additional time, not equivalence/speedup proof. Warm synchronous normalization plus identical input reset only; no cold startup, real settings durability, actual CLI rendering, full suites/global/Store coverage or separate Store timing. Existing isolated settings integration tests pass; no public schema/visible output change. Historical snapshot-process-table wording corrected to78 hash entries/74 unique paths (four duplicate CLI paths); underlying hashes/scores/tests unchanged, raw evidence retained. Artifacts: `artifacts/crap/cli-tab-normalization/`; existing native/appearance/withdrawn-menu limits remain.


## Snapshot text process-table extraction (November 23, 2026)

`SnapshotText.Format` CC/CRAP56→22 with cohesive process-table helper34; the34-point finding is retained. Local row/disk functions become static helpers with explicit builder arguments, no extra allocation sites; whole-class CC79 unchanged. Whole class77/77lines78/78reportedbranches.48new original characterization cases/15methods,145focused original/candidate tests pass each Desktop/Store variant; final app/test/consumer and bridge/worker builds zero warnings/errors. Exact source reconstruction, removed stack-only capture, identical cleaner delegate cache,147/148PDBdocuments/resources/486other Desktop drop files unchanged. Two local-function identity aliases, net one method. Existing raw-CPU availability behavior is characterized/documented separately, not fixed.

Actual before/after DLLs, one original-compiled common friend bridge,16fresh processes/eight alternating pairs, six immutable report workloads,500ms warmup, seven measured batches,40ms calibration target/min16operations, tiering disabled. One Format call/operation; full strings/input preparation outside timing; no WPF application/control/dispatcher or native I/O. UTC05:07:56.513–05:09:21.096 on November24 (localNovember23). No owned build/test/coverage/profiler overlap, retries, omissions or threshold changes. All672latency/672allocation samples/configurations/MVIDs/inputs validate, six complete output strings match. Times are medians of process medians; ratios are paired geometric estimates.

| Workload | Before µs | After µs | Ratio | Paired95% interval | Before/after B |
|---|---:|---:|---:|---|---|
| Empty |6.99|6.98|1.008458|0.979469–1.038305|9,144/9,144|
| Escaped128rows |130.97|130.58|0.994193|0.964069–1.025258|223,520/223,520|
| Grouped64rows |68.47|67.12|0.972714|0.928048–1.019531|79,816/79,816|
| Individual64rows |62.33|61.79|0.982614|0.950606–1.015700|63,992/63,992|
| Missing64rows |50.28|50.09|0.995824|0.978980–1.012957|53,728/53,728|
| State-masked64rows |59.05|58.52|0.978181|0.951259–1.005864|62,784/62,784|

Fixed gate rejects paired latency/allocation95% lower bound>1 or new allocations on a formerly zero-allocation path. All six pass, allocation intervals exactly1. No statistically detected regression; intervals permit3.83% additional latency and do not prove equivalence or a speedup. Warm synchronous formatter CPU/current-thread allocations only; no actual clipboard, whole-app/native/hardware or separate Store performance measurement.145focused tests each variant protect formatter/copy/saved-report contracts; full suites/global/Store percentages were not rerun. No new PNGs needed for byte-identical text and unchanged layout/resources. Artifacts: `artifacts/crap/snapshot-text/`; inherited native/appearance scopes and the prior withdrawn-menu limitation remain.


## Process action menu candidate withdrawn (November 23, 2026)

Final production is unchanged: `ProcessActionBar.CreateMoreMenu` remains CRAP68/CC68;37 new cases raise reported branch coverage61/68→68/68 at57/57lines.271 focused original/candidate/restored tests pass each Desktop/Store variant; final restored app/test/consumer builds zero warnings/errors. All74 monitored production DLL copies and four sources restore byte-for-byte. The candidate parent40/helpers12/16 (combinedCC68) is archived, not adopted. Five generated closure/state-machine layouts match exactly; no explicit allocation site/native query added.36 standalone reparented actual menu-item renders matched, with no native popup opened; contact/full-size output reviewed. No full suites/global coverage/product-wide hardware acceptance.

Two separately labeled, unrepeated protocols each use actual original/candidate assemblies, one original-compiled common bridge, eight alternating pairs/16fresh processes, six fixed menu states,500ms warmup, seven batches, tiering disabled and one menu call plus seven-counter reset per operation. Full recursive item/geometry/callback-count fingerprints match outside timing; no native control operation or owned build/test/coverage/profiler overlap. The first target40ms/min16 run was UTC04:37:49.128–04:39:24.438 on November24 (localNovember23). Its wide confidence intervals motivated a disclosed sensitivity protocol, UTC04:42:20.744–04:46:21.978, draining the isolated blank WPF dispatcher outside timing (including each32 warmup operations) with200ms target/min64batches. No forcedGC, samples dropped, gate changes or third attempt. Diagnostics did not establish the cause of variance; they confirmed no native windows/product startup. All32workers/1,344latency/1,344allocation samples retained and audited.

| State | First ratio | First paired95% interval | Drained ratio | Drained paired95% interval |
|---|---:|---|---:|---|
| Base |1.017647|0.838081–1.235686|1.076212|0.752637–1.538900|
| External suspension |0.935835|0.814608–1.075102|1.002184|0.783176–1.282434|
| Historical |1.119957|0.835604–1.501075|1.048391|0.979865–1.121710|
| Live |0.988978|0.788164–1.240956|1.076642|0.950168–1.219950|
| Owned suspension |1.086702|0.834111–1.415783|0.975979|0.811609–1.173637|
| Priority only |0.921383|0.788283–1.076957|1.133905|0.989983–1.298749|

Both pass the unchanged gate rejecting paired latency/allocation95% lower bounds>1. First-protocol allocation medians match exactly (base44,888B;priority82,336B;live/owned108,056B;external108,104B;historical108,176B). Drained medians have small fractional differences and allocation intervals spanning1; do not report them as identical. No statistically detected regression, but latency bounds permit50.11%/53.89% additional time and do not establish equivalence. The candidate is withdrawn because the performance conclusion is not stable enough for adoption. Preserve its full evidence and establish a repeatable WPF menu benchmark before reconsidering. Current tests-only production performance is unchanged by exact binary identity. Artifacts: `artifacts/crap/process-action-menu/` and `dispatcher-performance/`; original>50 finding and inherited native/appearance gaps remain.


## Process action identity presentation (November 23, 2026)

`ProcessActionBar.Update` CC/CRAP64→48 plus private identity helper16; total CC64 unchanged. Original32 new cases pass first execution;234 focused cases pass original/candidate Desktop and Store. Final app/test/consumer builds zero warnings/errors. Source reconstructs exactly;147/148compilation documents unchanged, managed resources/486other Desktop drop files identical. One helper, no additional explicit allocation sites or icon/native query calls.36 standalone software renders match original/Desktop/Store byte-for-byte; fixture colors/padding, contact sheet and two full-size samples reviewed. This does not replace product-wide/hardware appearance acceptance.

Actual before/after assemblies, one original-compiled friend bridge,16fresh processes/eight alternating pairs, six fixed selection states,500ms warmup, seven batches,40ms target/min16operations, tiering disabled. One Update call/operation; fallback icon cache/fingerprints prepared outside timing. UTC04:23:06.518→04:24:38.423 on November24 (localNovember23). No owned build/test/coverage/profiler overlap, retries, omissions or gate changes. All672latency/672allocation samples/configuration/MVIDs/fingerprints validate. Ratios are paired geometric estimates; times are medians of process medians.

| State | Before µs | After µs | Ratio | Paired95% interval | Before/after B |
|---|---:|---:|---:|---|---|
| Empty |4.95|4.85|0.970920|0.938423–1.004543|608/608|
| Grouped |5.50|5.58|1.013911|0.986581–1.041998|872/872|
| Historical |5.17|5.23|1.018873|0.994587–1.043753|848/848|
| Live |5.51|5.54|1.006084|0.991154–1.021239|848/848|
| Suspended |5.58|5.57|1.000976|0.987554–1.014582|848/848|
| Unavailable |5.10|5.07|0.998199|0.976725–1.020145|848/848|

Fixed gate rejects a latency or allocation paired95% lower bound>1, or new allocations on a formerly zero-allocation path. All six pass; allocation intervals are exactly1. No statistically detected regression, but latency bounds permit4.38% additional time and do not prove equivalence. Warm Update CPU/current-thread allocations only: no cold/native icon querying, hardware painting, whole-app profiling, full suites or fresh global/Store percentages. Native high contrast was false; three original branch outcomes remain. Artifacts: `artifacts/crap/process-action-update/`; CRAP report/inventory retain parent48 and the existing appearance/native review gaps.


## CLI snapshot table characterization (November 23, 2026)

Tests only: `SnapshotReport.BuildProcessTable` CRAP64.25→32/CC32 unchanged,19/19lines32/32reportedbranches; above30 retained. Nineteen new cases and48focused tests pass each Desktop/Store variant; final affected test builds zero warnings/errors. All74unique production DLL paths/two owning sources unchanged (78recorded hash entries included four duplicate CLI paths; count corrected November23), so no shipped performance or allocation change is introduced. No new timings/full suites/global coverage/native-terminal or GUIhardware acceptance. Plain table/cell output, culture, optional columns, missing/zero metrics, text sanitization and input immutability are asserted directly. Initial test API compile issue and one culture-sensitive assertion false positive were corrected and retained in artifacts; no production fix. Evidence: `artifacts/crap/snapshot-process-table/` and CRAP report; inherited appearance/native gaps remain.


## Chart type selection characterization (November 23, 2026)

Tests only: `MainWindow.OnChartTypeChanged` CRAP57.05→10 at unchanged CC10;9/9 lines and10/10 reported branches. Twenty new characterization cases pass original Desktop/Store behavior;56 focused tests pass each variant. Final test builds have zero warnings/errors. All74 monitored production DLL copies and three owning source hashes are unchanged, so this batch introduces no shipped performance or allocation change. No new timing run, full suite or global/Store coverage measurement. Two owned transparent-window assertions verify reading/graph refresh, not hardware/PNG acceptance. Existing full-appearance/native-theme/preference/shortcut gaps remain. Evidence: `artifacts/crap/chart-type-selection/` and the CRAP report.


## Connection leader report phases (November 23, 2026)

CaptureReportText.ConnectionLeaders CC118/CRAP118.71 becomes parent28 plus private selection32/status12/group48. Combined89/89lines and118/120reportedbranches; totalCC118→120 (explicit extra-trigger rendering decision), maximum48. Both32/48 remain findings above30.57new original-first cases/20methods and135original/candidate focused cases pass each variant; app/tests/consumer both builds zero warnings/errors. No dependencies/public API/UI layout/native I/O change. Original389lines reconstruct; rendering/query/budget text retained, with documented pure-local eligibility/count sequencing.14compiler lambda aliases preserve shapes and closure outer3/group2 fields;147/148source PDB documents unchanged, managed resources/486other Desktop drop files identical.

Frozen eight paired runs alternate process order/reverse workload order on odd pairs;16fresh processes/default contexts load actual original/candidate apps and one original-compiled bridge. Six fixed captured-data scenarios: unavailable,64owners/ranked,uncapped pins/forced trigger,search-trigger escape,merged executable/endpoints/quality,301-frame period with disabled network grouping. Each operation calls the actual report API once.500mswarmup/seven batches/tieringoff, minimum16 operations and adaptive40ms target chosen before timing. No owned heavy overlap, timed retry/omission/tolerance change. All full report strings and672latency/672allocation samples pass provenance checks.

Gate: reject a paired log-ratio95% lower bound>1 for latency/allocation, or introduced allocation on zero paths; t7=2.364624 over paired process medians. Ordinary path intervals span1: no detected regression, not equivalence. Tiny fractional allocation differences are shown. The unavailable path saves40bytes/call (792→752) by delaying its closure until after the availability guard. Standalone medians can differ in direction from the paired geometric ratio. CPU/current-thread allocations only, not cold startup, hardware rendering or whole-app performance.

| Workload | Before µs | After µs | Ratio | Latency95% | Bytes before/after | Allocation95% |
|---|---:|---:|---:|---|---:|---|
| merged | 82.052 | 80.391 | 0.979756 | 0.946896–1.013757 | 130689.9409 / 130689.9272 | 0.999992557–1.000004381 |
| period-disabled | 467.239 | 458.111 | 0.982934 | 0.956224–1.010391 | 606571.1379 / 606571.2062 | 0.999996942–1.000005969 |
| pinned | 300.672 | 294.958 | 0.983830 | 0.953834–1.014769 | 486095.1312 / 486095.0519 | 0.999992164–1.000001501 |
| ranked | 417.118 | 419.077 | 0.993359 | 0.958242–1.029764 | 577625.9167 / 577625.9167 | 0.999999669–1.000000630 |
| trigger | 592.907 | 592.672 | 0.998490 | 0.982597–1.014640 | 885926.0000 / 885926.0000 | 0.999998181–1.000002614 |
| unavailable | 0.217 | 0.207 | 0.959051 | 0.938018–0.980555 | 792.0000 / 752.0000 | 0.949494949–0.949494949 |

Actual run: 11/24/2026 03:53:39 to 11/24/2026 03:55:05.

Relevant report/copy/view integration and exact text checks pass; no extra PNG/hardware/native-clipboard/full-suite run. Source/model input purity and existing invalid-top/early-return behavior are protected. Existing native-theme/preferences/shortcut review scopes and full appearance=false remain. Evidence: artifacts/crap/connection-leaders/ (source/assembly/closure/alias proofs, original/candidate TRX/coverage, raw workers/pairs/protocol/input hashes/summary). Mixed checkpoint231>30/116>50/115middle,4622methods,18fresh/4604inherited; global/Store coverage unmeasured. Goal active.


## Chart range checked-event characterization (November 23, 2026)

Tests only: `MainWindow.OnRangeChecked` CRAP72→8, unchangedCC8, lines0/5→5/5 and reported branches0/8→8/8. Eighteen NUnit4 cases/five methods pass first behavior execution in both variants;36 focused cases per variant include18 existing chart preference/snapshot/activity-button tests. Both final nonincremental test builds have zero compiler warnings/errors. Test accessor/import compilation errors were corrected before first behavior execution; no behavior test failed or was weakened.

Real wired range Checked events characterize expansion versus effective range, raw fallback storage, invalid tags, closing and snapshot/unrelated-preference preservation. Two transparent owned IsVisible-window cases assert the live CPU label refreshes from a retained synthetic31% sample for both same120 and changed600 ranges, with no sampling;16 hidden cases cover guards/state. No private reflection, production API/logic change, mocks, actual mouse/key injection or foreground/hardware acceptance.

All74 production DLL copies and three owning sources remain identical to the adopted appearance-preview-update baseline (DesktopDC4359EE0459F0D124F07BD4AC0EF312DACEB8652DFF865157F7361931F64378;Store4DA9EE964827E048CEF2CA0E6A3395B62CA8853969048C5EAE20BCDA3CD6070B). No introduced shipped-code performance/allocation change; no new performance timing, speedup or complete-equivalence claim. Prior preview24/CaptureSettings141 renders are same-production historical evidence, not newly repeated. No redundant app/benchmark/full-suite runs. Native theme, preference persistence and shortcut verification gaps/full appearance=false remain open. Evidence: artifacts/crap/chart-range-selection/; mixed inventory230>30/117>50/113middle,4619methods,one fresh/4618inherited; global/Store coverage unmeasured. Goal active.


## Appearance preview resource phases (November 23, 2026)

Private extraction preserves the original146 source lines, resource assignment/native-read order and six readout updates. CC82 becomes14/2/38/28 (total82); CRAP193.91 becomes14.05 with helpers6/38/28. The normal-theme phase remains above30. Combined focused coverage39/53 lines (73.6%) and79/82 branches (96.3%) is disclosed; native high-contrast execution remains uncovered.

Eight paired measurements alternate process order, with reversed scenario order on odd pairs;16 fresh default-context processes load the actual frozen original/candidate app with one identical bridge. Six workloads each perform two actual Update calls.500ms warmup/seven batches, tiering disabled, minimum1024 operations and adaptive40ms target; the minimum dominates, producing seconds-long batches. The unchanged frozen run took45minutes (01:55:50–02:40:58 UTC November24). No timed retries/omissions or owned build/test/coverage/profiler overlap. All672 latency/672 allocation samples and every matrix/outcome pass provenance checks under native HC=false/system-light=false.

Numbers below are per two-update operation. Latency and allocation gates reject a paired log-ratio95% lower bound>1, or introduced allocation on a zero-allocation path. The latency interval uses paired process-level seven-batch medians, t7=2.364624; the standalone before/after columns are medians across eight processes and need not agree in direction with the paired geometric ratio. All allocation intervals equal1 and medians are unchanged. Small higher latency estimates remain within intervals spanning1: no statistically detected regression, not proof of equivalence. This measures warm call CPU/current-thread allocations, not cold startup, hardware painting or whole-app performance.

| Workload | Before µs | After µs | Paired ratio | 95% interval | Bytes before/after |
|---|---:|---:|---:|---|---:|
| brushed | 4457.08 | 4408.00 | 1.016806 | 0.985840–1.048745 | 1147029.75 / 1147029.75 |
| dark-light | 4033.13 | 3972.82 | 1.005488 | 0.935874–1.080281 | 1075861.50 / 1075861.50 |
| glass | 3809.21 | 3766.05 | 1.033896 | 0.972260–1.099439 | 999237.00 / 999237.00 |
| palette | 1367.48 | 1368.42 | 1.008878 | 0.973079–1.045994 | 721232.00 / 721232.00 |
| style | 2608.81 | 2629.23 | 1.013784 | 0.967122–1.062699 | 687251.50 / 687251.50 |
| system | 3368.94 | 3357.17 | 1.010209 | 0.974060–1.047699 | 921976.91 / 921976.91 |

All24 direct component previews match original bytes; Capture/Settings smoke49/49/43 images passes and all165 images were reviewed on14 sheets plus six full-size Appearance pages. Source checksums verify147/148 compilation documents unchanged; only AppearancePreview.xaml.cs changes, with identical managed resources/486 other Desktop drop files. Six WinMon DLL copies change;68 other copies are identical. Scoped app/test/consumer builds and58 selected tests pass both variants. No full suites. Next-batch test-code accessor/import compilation errors were corrected before final clean builds; no preview behavior test failed. The original artifact bridge namespace error was fixed before any timing, with failed build log retained.

Native HC/system-light changes, hardware composition and cold startup remain unverified; inherited original/candidate gauge centering failure/full appearance=false is retained. The native-theme review joins the existing preference/shortcut open scopes. Installer ACL/deletion methods remain156 with confirmed nonadmin/controlled-reboot-delete blockers, unchanged production. Evidence: artifacts/crap/appearance-preview-update/ (raw workers/perf pairs, inputs/protocol/run, summary/raw verification, source/line/shape/assembly proofs, tests, renders/review/manifests). Current mixed inventory231>30/118>50/113middle,4619methods,13fresh/4606inherited; global/Store coverage unmeasured. The goal remains active.



## Flight-recorder entry characterization (November 23, 2026)

Test-only remediation of `MainWindow.OnFlightRecorder`: CRAP156→12, unchangedCC12, lines0/17→17/17and branches0/12→12/12. Twelve original-first NUnit4 cases in six methods protect real routed entry, owned-window empty/reuse/restore/close/reopen state, frozen versus refreshed resource/detail snapshots, full lifecycle inventories and network owners, pending reader cancellation/late isolation and sync/faulted-reader failures. Final61selected Desktop/Store tests each pass (12new+49existing), zero failures/skips/fixture corrections; affected nonincremental test builds have zero compiler warnings/errors. Refresh/closed callbacks also gain focused coverage; native-reader/save callback bodies are not newly covered.

All74production DLL copies remain byte-identical to frozen baseline: Desktop3AE04277F3878BC48ED96472C46DC6F41DE6199033B538BA42624982A08CF374, StoreC9DF1AA2AFD0193279E36D4BDD331C4681AB5A7B6CC777192872D157E3867730. Four owning source inputs unchanged, no production refactoring, dependency, additional shipped method/field/allocation/I/O or API change. No new product performance change is introduced; no new timing/allocation/profiling/render or speedup/equivalence measurement is claimed. Existing verified production references reused; no redundant production/benchmark-consumer rebuild/full suite. Prior Capture/Settings renders remain historical/same-binary evidence, not fresh acceptance.

Owned STA integration windows and real WindowState transitions are tested; native foreground success/populated Windows logs/global export-tracker membership/shutdown waiting are not newly asserted. Default event-reader selection uses empty history only, so no native Windows event query. Injected reader handles all populated cases; diagnostic owner cannot start sampling, pending reads completed/drained and resources/windows/views restored/closed. Relative recent archive minute keeps trend assertions away from retention boundaries; no artificial clock delays. No global hotkey/native service operation/preference write or new test filesystem write.

Inventory4616methods,231above30/119above50/112middle; threefresh/4613inherited, zero aliases/additions/suppressions. Parent original≤30counter132; untested child callbacks get no credit. Global/Store percentages unmeasured. Preserve preferences lifecycle/shortcut native-input review scopes and inherited theme/ordinary-hardware/full-working-tree appearance limits. Needed-tests policy skips full suites; no commit or push. Evidence: `artifacts/crap/flight-recorder-open/validation-summary.json`, TRX/coverage/metrics, exact one-finding overlay, source/74-copy manifests and characterization review. LocalNovember23; binlogsUTCNovember24.

## Settings shortcut value boundary (November 23, 2026)

Native key/System-key translation and the real Keyboard.Modifiers read remain ordered in the existing routed handler; a single new internal CaptureShortcut method receives those values and contains the verbatim original capture logic. No new field, constructor/provider/delegate or production allocation site. The per-key closure still has one ModifierKeys field. Business CC/CRAP26, native wrapperCC2/CRAP6 (zero NUnit coverage), unchanged mapping8 and validation6. Combined focused wrapper/core20/24lines83.3%,26/28branches92.9%; full controlled business coverage is not a physical native-key test.77new cases/11methods plus30existing pass107in both variants; only8new reset/availability cases ran against unmodified original before extraction (38total withexisting).69controlled cases require the value seam. No failures/skips/fixture corrections, warnings/errors or full suites.

Eight pairs/16fresh default-context processes, original/candidate order alternating by pair and five-workload order reversed on odd pairs; tiering disabled. Warm500ms; adaptive40ms/min1024operations, seven measured batches. Two actual WPF PreviewKeyDown events per operation; arguments/window construction and fingerprint/setup/disposal outside timing. The same explicit Keyboard.Modifiers=None guard is inside timing on both sides. This measures guarded routed-default workflows, not pure handler latency; no private reflection in timing. Original baseline796A7F67B0C66A43286DDF291C47FA03A8508DC977F87E3DB56CB3B4A231DD17. Same bridge built once against original, identical before/after bytes, actual app path/MVID/runtime10.0.15 checked. No owned build/test/coverage/profiler overlap or timed retries.

| Native-default workload | Before µs/2 events | After µs/2 events | Paired ratio (95%CI) | Bytes before→after |
|---|---:|---:|---:|---:|
| Dialog Tab/Escape |16.712|17.037|0.9440 (0.7840–1.1366)|48→48 |
| Clear Back/Delete |14.046|13.264|0.9598 (0.8871–1.0384)|48→48 |
| Modifier-only keys |14.189|15.286|1.1204 (0.9387–1.3372)|912→912 |
| Invalid A/Enter |16.055|15.520|0.9387 (0.8126–1.0845)|1112→1112 |
| Constructed System/None metadata |14.108|15.028|1.0521 (0.9320–1.1876)|1072→1072 |

Times are medians of eight worker medians; paired ratios are geometric log-ratio estimates, so they need not equal the displayed median-time ratio. All allocation95%ratio intervals1.0000–1.0000. Fixed gate rejects latency/allocation95%lower>1, newly positive zero-path allocation or any outcome mismatch; all five matrices and workload stable fingerprints match. Gate passes, but modifier-only point+12.04%/upper+33.72% and System point+5.21%/upper+18.76% leave substantial uncertainty. No speedup, zero-regression or equivalence claim. No resampling, gate/workload omission/tolerance changes. Actual held-modifier/valid-key latency, physical Alt/System dispatch, cold Settings construction, Store timing, layout/render cost and whole-app/global-hotkey performance are not measured. Native guard/WPF routing cost is included and can mask small pure-handler costs.

Affected production/test and friend benchmark-consumer builds pass Desktop/Store with zero warnings/errors.74production copies: six WinMon changed,68others identical. All1464original source lines reconstruct;121→122compiled file methods, two explicit LINQ aliases,22→24wrapper/core points and28branches retained. The extraction exists to make ambient-input business logic testable; no algorithm/decision elimination is claimed. Existing public/internal constructor signatures and native default wiring remain unchanged.

Original/Desktop/Store Capture/Settings smoke exits0,49/49/43PNGs, report/shutdown completion asserted.141images reviewed via11contact sheets and six full-size General pages. Shortcut control text/layout remains consistent in Dark/Light; Store feature omissions/restrictions remain. General/Appearance/Monitoring/Network overflow0; AutomaticCaptures345DIPs intentionally scrolls. Desktop23hashes match/26differ, Store3match/40differ due clocks/paths/captions/focus/variants. Original/candidate3000-DIP trend content fallback and inherited theme/full-working-tree appearance blocker remain; no ordinary hardware/physical-key/manual screen-reader acceptance.

Inventory4616methods,232above30/120above50/112middle, sixfresh/4610inherited; global/Store percentages unmeasured. Original-characterization counter conservatively stays131. Native-input-verification.json records uncovered wrapper, physical-key/focus and newly captured edit lifecycle limits for required goal-completion review; preserve the prior preferences lifecycle scope too. Evidence: `artifacts/crap/settings-shortcut-key/performance-protocol.md`, inputs/16workers/eightpaired joins/summary, source/compiled/closure proofs,74-copy manifests, focused TRX/coverage, visual/native-input reviews and validation-summary.json. LocalNovember23, UTCNovember24binlogs.

## Services display-row phases (November 23, 2026)

`ServiceDisplayRow.Update` now separates detail formatting, three metric labels and selective notification publication into private phases. The parent retains identity/status/expansion and original phase order. CRAP/complexity92→48; private phases10/6/28, all100% focused line/branch coverage. The original >30 finding remains; revised maximum50 is met. All115focused original/final Desktop/Store cases pass, including42new in12methods. Affected builds and benchmark consumer pass both variants without warnings/errors; six WinMon DLL copies change and68other production copies remain identical.

Fresh original baseline is8A621E9CE2685FD7D4DF22BA4EAB7F4092868B19F0C893529BD7F671EF2387C0, not the older service-row-refresh original. Candidate Desktop796A7F67B0C66A43286DDF291C47FA03A8508DC977F87E3DB56CB3B4A231DD17; StoreA3047E900FF6B1F8860141B65D76AA767A0FB74EAEDBB1F6F043A000DE5B2465. Same common probe bridge compiled once against fresh original, identical bytes in both app drops. Reused unchanged workload/worker sources and fixed protocol; actual default-context app paths/MVIDs/runtime10.0.15 recorded for every worker. No Store timing or layout/render-thread measurement.

Eight pairs/16fresh processes alternate original/candidate process order and reverse workload order on odd pairs. Each workload warms500ms, calibrates toward40ms batches (minimum1024operations), then records seven batches. One operation performs two real ServicesView.Update calls including identical trace scaffolding; construction/frame indexing/fingerprint serialization/disposal are outside timing. Actual minimum-count batches may exceed40ms. Tiering disabled, no owned build/test/coverage/profiler overlap. All nine outcome snapshots, six workload fingerprints and within-worker stable cycles match. The first completed pair exposed a slash-sensitive harness path comparison; correct canonicalization reused its two raw workers and continued only seven missing pairs. No timed retries or gate/workload changes.

| Workload | Before µs/2 updates | After µs/2 updates | Paired ratio (95%CI) | Bytes before→after |
|---|---:|---:|---:|---:|
| Stable12dedicated, collapsed |87.117|89.144|1.0103 (0.9375–1.0887)|34496.25→34496.25 |
| Changing CPU rank |154.768|158.017|1.0206 (0.9248–1.1263)|65504.25→65504.25 |
| Expanded shared hosts/workers |232.551|240.052|1.0354 (0.9483–1.1304)|111072.50→111072.50 |
| Expanded hosts, workers excluded |87.551|93.815|1.0868 (0.9937–1.1886)|41536.25→41536.25 |
| Pinned/filter/memory rank |139.684|140.630|1.0239 (0.9679–1.0831)|70152.25→70152.25 |
|121-frame history/gaps |114.947|112.647|0.9931 (0.9315–1.0587)|21552→21552 |

Times shown are medians of eight worker medians; paired ratios are geometric log-ratio estimates, not ratios of displayed medians. All allocation95%ratio intervals equal1.0000–1.0000 in these observations. Fixed rejection gate: latency/allocation95%lower bound>1, introduced allocation on zero path, or any outcome mismatch. Gate passes; higher point estimates and broad upper bounds remain. Host-only point estimate is+8.68%, upper bound+18.86%; the data cannot establish zero slowdown or equivalence. No timing resampling, omitted workload, dependency upgrade, hint or tolerance adjustment was used to pass. Source review introduces no new allocations, loops or I/O, but call/tuple-copy/JIT cost remains a performance uncertainty outside the observed gate.

Fresh original/Desktop/Store Services smoke uses the same SoftwareOnly host. Each exits0, enumerates313services/130hosts read-only, executes no service action, and produces22PNGs. All66images reviewed via six contact sheets plus three full-size Services views. Four hashes match and18differ per candidate; live process/readings/clock/focus/native captions make whole-window equality inappropriate. Row labels/status/metrics, expanded children, pins/ranking, action strip, menus, localization/narrow/header wrapping and history/error notices remain visually consistent. This is Services software/client/native-discovery acceptance only. Inherited theme gauge-centering/full-working-tree appearance limitation remains; no new ordinary hardware-rendering pass, native service-control latency, manual screen-reader pass, Store timing, cold-start or whole-app profiling claim.

Inventory4615methods,235above30/122above50/113middle; fourfresh/4611inherited observations. Global and Store coverage percentages unmeasured; independent original≤30 counter remains131. Preserve preferences startup/rollback/in-flight open-verification and all prior blockers. No full suites under the needed-tests policy. Evidence: `artifacts/crap/service-display-row/performance-protocol.md`, `performance-inputs.json`,16worker JSON files, eightpaired joins, `performance-summary.json`, source/compiled maps,74-copy manifests, four focused TRX reports and `validation-summary.json`. Local report dateNovember23; binlogs UTCNovember24.


## Preferences persistence characterization (November 23, 2026)

Test-only partial characterization: `MainWindow.ApplyPreferencesAsync` CRAP156→13.42, unchangedCC12, lines0/28→22/28 (78.6%), branches0/12→8/12 (66.7%). Twelve original-first cases in six methods protect actual owned-file persistence, confirmed/live setting merge and reset, history cleanup, trend continuity, early/late write errors, pre-cancellation and diagnostic null input. Both16-case focused desktop/Store selections pass with zero failures/skips; affected nonincremental test builds have zero compiler warnings/errors.

All74production DLL copies are byte-identical to the frozen baseline:default8A621E9CE2685FD7D4DF22BA4EAB7F4092868B19F0C893529BD7F671EF2387C0, Store004A776636CA62628330F9E0544EF360C6B8ACBF935CFBED7B789BE3BAB99B76. Four source inputs match. No production-code performance change is introduced. No new timing/allocation/speedup/profiling/render claim; integration tests' filesystem/theme activity is not an application benchmark. Diagnostic mode avoids native startup changes, hotkey registration, sampling and user preference paths. Unique owned temporary files/resources/cultures/views/window are cleaned up.

This is not complete all-mode remediation:secondary80%line/70%branch and90%critical-mode coverage are unmet/unproven. Static native startup/rollback paths, exact in-flight edits, mid-/post-write cancellation and saved-snapshot/live-choice durability require further controlled lifecycle work. A persistent open-verification.json/context pointer retains that below-threshold risk. Existing snapshot/live difference is documented as an observation, not silently fixed or asserted to be a whole-lifecycle defect. No method credit is given merely because the score drops below30.

Inventory235above30/123above50/112middle,4612methods,onefresh/4611inherited,zeroaliases/additions/suppressions. Fully-characterized-original counter stays131. Global/Store percentages remain null/unmeasured,80%global line unmet/unproven. No full suites/new renders under needed-tests policy. Inherited theme/hardware limitations and false whole-working-tree appearance acceptance remain. Evidence: `artifacts/crap/preferences-apply/validation-summary.json`, TRX/coverage, source/production manifests, characterization review and open-verification/behavior-observation records. Local report date is November23; binlogs carry UTC November24.


## Title-bar pin characterization (November 23, 2026)

Test-only remediation: `MainWindow.UpdatePinButton` CRAP72→8, unchangedCC8, lines0/10→10/10 and branches0/8→8/8. Eight original-first NUnit4 cases preserve click toggle/observer order, saved preferences/content binding, live selected theme colors and unselected style inheritance, localized names/tooltips and no-offer capture-notice preservation during ApplyLanguage. Both focused desktop/Store runs pass12tests (eightnew/fourexisting), no failures/skips; affected nonincremental test builds have zero compiler warnings/errors.

All74production DLL copies are byte-identical to the frozen baseline:default8A621E9CE2685FD7D4DF22BA4EAB7F4092868B19F0C893529BD7F671EF2387C0, Store004A776636CA62628330F9E0544EF360C6B8ACBF935CFBED7B789BE3BAB99B76. Four source inputs match, including XAML. No production-code performance change is introduced. No timing/allocation/speedup/render/profiler claim is added; the window is unshown, so native stacking, focus and rendered/hardware appearance remain unverified. Tests use controlled local resources, not a pixel-level theme acceptance claim, and restore culture/resources/subscriptions/window.

Only Pin's record is adopted as fresh; raw ApplyLanguage6.02 and RefreshUpdateText9 collateral observations remain separate with inherited canonical records. That preserves uncharacterized tray/offer/download/dismissal risk explicitly; a controlled updater dependency fixture is still required. No private-field mutation, unsafe native action or metric-only production seam/refactor is added. Review is inferred source/assertion reasoning, not an empirical mutation score.

Exact inventory236above30/124above50/112middle,4612methods,onefresh/4611inherited,zeroaliases/additions/suppressions. Independent original≤30tracking130→131. Global/Store percentages remain null/unmeasured and80%line unmet/unproven; no full suites/new renders under needed-tests policy. Inherited theme/hardware limitations and false whole-working-tree appearance acceptance remain. Evidence: `artifacts/crap/title-pin/validation-summary.json`, TRX/coverage, source/production manifests, raw-collateral record and characterization review. Dates use the repository's local November23 date; build binlogs may carry UTC November24 timestamps.


## Settings acceptance characterization (November 23, 2026)

Test-only batch: `SettingsWindow.SaveAsync` CRAP992.96→44, unchangedCC44, lines11/52→52/52 and branches11/44→44/44. Related `ChangeNetworkMonitoringAsync`48→16, unchangedCC16, lines14/28→28/28 and branches5/16→15/16 (93.8%). These are desktop measurements. Store-unavailable policy has separate direct tests, not a fresh coverage percentage.

33distinct new case identities (32save/policy plus one fixture regression) execute31desktop/22Store with20shared/11desktop-only/2Store-only. Complete focused selection79desktop/70Store passes, including48existing relevant cases each; zero failures/skips. Final nonincremental affected test-project builds have zero warnings/errors. Normal modal acceptance, current selectors/fallbacks/flags, preserved main/chart preferences, validation/folder-probe order, network health/errors/cancellation/concurrent-save guards and retry are characterized through existing callbacks/controls. Unique owned temp folders validate probe cleanup; no recursive deletion. Unexpected WPF notices are dismissed/reported by a scoped idle observer with a regression case and cleanup.

All74production DLL copies remain byte-identical to the frozen baseline:default8A621E9CE2685FD7D4DF22BA4EAB7F4092868B19F0C893529BD7F671EF2387C0, Store004A776636CA62628330F9E0544EF360C6B8ACBF935CFBED7B789BE3BAB99B76. Four production source inputs match. Existing verified references are reused; no production-code performance change is introduced. No new latency, allocation, speedup, profiling or render claim. Test-fixture activity is not an application benchmark. Native service/UAC/network discovery/startup/update actions and full keyboard-focus/appearance acceptance are outside scope.

Initial five unsupported-top25 fixture expectations and two Store missing-Updates-page fixture failures remain with all successful77then78/69then79/70runs. Tests use supported20 and preserve the hidden Store flag, respecting existing channel policy; production code remains unchanged. No findings/gates/assertions are suppressed or weakened. Review is inferred source/assertion reasoning, not an empirical mutation score.

Inventory237above30/125above50/112middle,4612compiled methods;twofresh/4610inherited,zeroaliases/additions. Save44remainsabove30; helper16resolvesoneoriginalfinding, tracking129→130. Global percentages remain null/unmeasured; historical71.7%line/74.6%branch is not reused as fresh evidence and80%line remains unmet/unproven. No full suites/new renders under needed-tests policy. Whole-working-tree appearance acceptance remains false with the inherited theme blocker. Evidence: `artifacts/crap/settings-save/validation-summary.json`, complete TRX/coverage, source/production manifests, characterization review and command log.


## Historical capture copy characterization (November 23, 2026)

Test-only remediation: `MainWindow.CopySnapshot` CRAP272→16, unchangedCC16, reported lines0/28→28/28 and branches0/16→15/16 (93.8%).43new NUnit4 cases cover historical selection/retention guards, current-tab report composition, missing connection history, connection filters/trigger/limits/sort/period, callback ordering, feedback reset, exception identity, warnings/notice and retry.88selected tests pass on desktop and Store; final nonincremental affected test-project builds finish with zero warnings/errors.

All74production DLL copies are byte-identical to the frozen previous batch:default8A621E9CE2685FD7D4DF22BA4EAB7F4092868B19F0C893529BD7F671EF2387C0, Store004A776636CA62628330F9E0544EF360C6B8ACBF935CFBED7B789BE3BAB99B76. Snapshot/Internal and SnapshotText/CaptureReportText source hashes remain unchanged. Existing verified production references are reused, so this introduces no production-code performance change. No new timing, allocation, latency, speedup, hardware/software-render or full-app profiling claim is made. Four scoped WPF notices are observed through the callback error path, without changing the system clipboard.

The unexecuted native clipboard branch shares a source sequence point with the callback branch;100%line coverage does not establish native clipboard acceptance. Real timer expiry is not forced; explicit start/reset/binding and exception state are asserted. Future-only data is absent from exports, but downstream history scoping limits what that observation proves about the caller predicate. Initial compile/declaration/search/period-fixture failures and all37/82then43/88reports remain. Tests were corrected to use the real view Load API and existing search semantics; production code is untouched. Review is reasoned pseudo-mutation analysis, not an executed mutation score.

Exact inventory238above30/126above50/112middle,4612compiled methods;onefresh/4611inherited,zeroaliases/additions/suppressions. One original finding resolves≤30; independent tracking128→129. Fresh global coverage remains null/unmeasured,80%line unmet/unproven; historical global metrics are not reused as a current claim. Full suites/new renders are skipped under the user's needed-tests policy; inherited appearance/theme limitations remain open. Evidence: `artifacts/crap/snapshot-copy/validation-summary.json`, TRX/coverage, source/production manifests, characterization review and command log.


## Automatic capture settings characterization (November 23, 2026)

Test-only batch: `AutoCaptureSection.Read` CRAP94.91→50, unchangedCC50; line31/42→42/42 and branch40/50→50/50.99new NUnit cases preserve metric bounds/units, parsing/error-field precedence, targeted rules, disabled states, culture, enable/notify flags and folder normalization/default/error retention.142selected tests pass on desktop and Store. Final nonincremental test builds have zero warnings/errors; no production project rebuild is needed because production inputs are unchanged and existing verified references are reused.

All74production DLL copies across the two variants remain byte-identical to the frozen Services-refresh baseline, including default8A621E9CE2685FD7D4DF22BA4EAB7F4092868B19F0C893529BD7F671EF2387C0 and Store004A776636CA62628330F9E0544EF360C6B8ACBF935CFBED7B789BE3BAB99B76. Source hashes for AutoCaptureSection, its Settings save caller and CaptureRules remain unchanged. Therefore this batch introduces no production-code performance change. No timing, allocation, speedup, new hardware/software render, native-action or whole-app profiling claim is made; previous measured performance and appearance limitations remain historical.

Coverage includes the existing invalid-folder retention contract: normalization exceptions preserve typed folder text, with enabled chosen-folder writability checked later by SaveAsync. No production fix is silently included. The final test-quality review strengthens accepted rule flags/durations and distinguishes grouping policy from out-of-range rejection. Original95case/138selected and expanded/final99case/142selected successful reports remain, along with the pre-compilation Store argument-splat failure and corrected explicit build. No timed retries/gate changes exist because no new timing experiment runs.

Exact inventory239above30/127above50/112middle,4612compiled methods;onefreshrecord/4611inherited,zeroaliases/additions/suppressions. Read50remainsabove30. Global percentages are null/not-remeasured-focused; historical71.7%line/74.6%branch does not establish current global coverage and80%line remains unmet/unproven. Full suites are skipped under the user's needed-tests policy; whole-working-tree appearance acceptance remains false. Evidence: `artifacts/crap/capture-settings-read/validation-summary.json`, final TRX/coverage, source and production manifests, characterization review and command log.


## Services row refresh phase separation (November 23, 2026)

`ServicesView.RefreshRows` complexity/CRAP108→36, with `PublishRows`26, `RefreshStatus`20 and direct local `AddExpandedRows`26. Existing row/graph builder42 is unchanged. All four new/coordinator units have full line coverage and≥97.2% branch coverage; three phases have100% branches. Parent36 and row builder42 remain original>30findings. Exact source reconstruction/all918original file lines and118→121compiled method mappings account for eight aliases, three added methods and six added call/void-return points. Total target branches remain108; cached rows, graphs, callback ordering and finally behavior are preserved.

20new characterization cases pass first on the original. All81focused Services/ranking/pin/menu cases pass on original and candidate desktop/Store, with zero skipped/failed cases. Affected app/test and benchmark-consumer builds have zero warnings/errors; no full suites run. A wrong original test graph expectation was corrected before refactoring, with its failure evidence retained.

Direct-update measurements use the same bridge DLL compiled against originalC636, copied identically to actual original/candidate app drops. WPF BAML lookup fails in the initial separate-ALC preflight, before timing. The revised protocol uses one default-context app per fresh worker:8pairs/16processes, alternating version order and reversed workload order on odd pairs. All nine complete outcomes and all per-workload fingerprints match. Seven batches follow500ms warmup, with adaptive40ms target/minimum1024operations and tiering disabled. Batches may exceed40ms; versions are not interleaved within a worker. One operation is two real Update calls, including identical event-trace scaffolding. Construction, serialization and disposal are outside timing. No owned build/test/coverage/profiler overlaps23:01:23.113–23:04:52.163UTC timing.

| Workload | Before µs/operation | After µs/operation | Paired geometric ratio (95% CI) | Before / after allocated bytes |
|---|---:|---:|---|---:|
| Stable12groups |101.934033|104.351611|1.092903 (0.718090–1.663353)|35264.25 /34496.25|
| CPU rank moves |167.606787|173.894775|0.981501 (0.736523–1.307963)|66272.25 /65504.25|
| Expanded shared/worker rows |252.250537|263.299902|1.005622 (0.812727–1.244300)|111072.50 /111072.50|
| Expanded host-only rows |105.648389|105.857959|1.007289 (0.796901–1.273222)|41536.25 /41536.25|
| Pins/filter/memory ranking |175.840967|169.497314|0.989084 (0.836007–1.170189)|70600.25 /70152.25|
|121-frame historical gaps |143.557715|146.358105|1.030966 (0.788071–1.348724)|21808.00 /21552.00|

Times are medians of seven-batch process medians; geometric ratios/intervals use paired log ratios across eight pairs (t7critical2.364624). All latency/allocation95% lower bounds stay≤1, passing the unchanged rejection gate. There are no timed retries, threshold changes or omitted workloads. Latency estimates are mixed, with wide intervals allowing substantial slowdown, particularly stable-row upper1.663353. Do not call this equivalence, a speedup or proof of zero regression. Cold startup/JIT, file I/O/native enumeration timing, Store, layout/render-thread and whole-app performance are not separately measured.

Allocations are unchanged for expanded/host-only rows and lower by768/448/256bytes in the collapsed/ranked/pinned/history workloads. Generated root/group/process closure field sets match by role; reductions are consistent with deferring per-group closure creation until expansion, without isolated profile attribution. The frozen protocol's outside-timing reset wording is clarified separately:per-cycle trace clear/recording are identical timed scaffolding. The worker’s invalid preflight duplicate-lifetime fixture was corrected before timing; failed preflights remain in the artifacts.

Read-only SCM/Services smoke passes outside the sandbox on original/default/Store using one identical software-rendering host:313services/130mapped hosts each, no service actions.66images, six sheets and three full-size dark views were reviewed with no new row/graph/status/layout difference observed. Dynamic live readings, time/PID/focus/popup and native/content caption variants remain, without whole-window equality. Ordinary hardware rendering and the inherited theme issue remain unverified/unresolved.

Candidate desktop8A621E9CE2685FD7D4DF22BA4EAB7F4092868B19F0C893529BD7F671EF2387C0; Store004A776636CA62628330F9E0544EF360C6B8ACBF935CFBED7B789BE3BAB99B76. Artifacts: `artifacts/crap/service-row-refresh/`, including all raw worker records, source/alias/closure proofs, protocol and scope clarification, exact outcomes, TRX, builds and images. The inventory remains239above30, now128above50/111middle, with explicit fresh/inherited coverage and no fresh global percentage.


## Preference validation policy consolidation (November 23, 2026)

`PreferenceStore.Validate` complexity/CRAP84→44 with four private normalization helpers: count2, enum2, column4 and service-sort8. Parent and helpers have100% line/branch coverage. Shared policy implementations reduce the scope's total branch count84→60, with no added allocation expression, record clone, persisted write, query or dependency. All13 expression substitutions reconstruct the original source; property evaluation order and the existing focus quirk are preserved.

119 new cases and selected existing caller tests pass. Initial366-case runs pass on original/candidate desktop/Store. Expanded384-case runs pass on original desktop and candidate desktop/Store; original Store's recorded scope remains366. Both variants' affected app/test and benchmark-consumer builds finish with zero warnings/errors. No full suites run, and no fresh repository-wide coverage is claimed.

One common friend bridge is compiled against original260F and copied identically to original/candidate probes. Actual WinMon assemblies execute in separate load contexts on STA. Six declared workloads run in eight fresh paired processes, with reversed load/warm order,500ms warmup, seven alternating batches, adaptive40ms batches and tiering disabled. Each operation invokes Validate once; construction and serialized outcome snapshots stay outside timing. All nine complete outcome/null-guard/repeated-drive snapshots match per process. No owned build/test/coverage/profiler overlaps22:12:32.516–22:13:57.324UTC timing. There are no timed retries or omitted scenarios.

| Workload | Before µs | After µs | Paired geometric ratio (95% CI) | Before / after allocated bytes |
|---|---:|---:|---|---:|
| Defaults |1.686339|1.732610|0.994540 (0.959486–1.030876)|5488.133 /5488.133|
| Valid mixed choices |1.759857|1.747737|0.993426 (0.962342–1.025514)|5528.147 /5528.131|
| Invalid enums/counts/columns |1.690224|1.681592|1.001245 (0.974810–1.028397)|5488.147 /5488.127|
| Nonfinite values/invalid drives |1.882595|1.884486|1.001938 (0.979255–1.025146)|6920.187 /6920.164|
| Legacy GPU/duplicate drive |1.695148|1.689513|0.996685 (0.988042–1.005403)|5664.101 /5664.097|
| Pins/chart normalization |2.434166|2.443155|0.997102 (0.974770–1.019946)|8472.231 /8472.250|

Times are medians of process medians; paired log-ratio intervals use eight processes, so displayed time quotients need not equal geometric ratios. Every latency and allocation95% lower bound stays≤1, passing the unchanged rejection gate. Allocation intervals all include1 (largest upper1.000005842); the tiny fractional differences are retained without claiming exact equality or isolating their cause. Some latency estimates are higher, and the largest upper bound permits about3.1% slowdown. This is no detected regression under this protocol, not a speedup or proof of zero slowdown. Cold startup/JIT, file I/O, whole-app behavior and Store performance are not separately measured.

This changes validation policy structure, with no rendering or UI-flow change; focused Settings chart-reset/caller tests are included, without another render suite. Earlier hardware-rendering and theme limitations remain. The duplicate/missing-secondary focus non-idempotence is verified on the original and documented separately, with no silent behavior fix.

Candidate desktopC63662092932D3E7CEE7AF2DAB18410BF48DD609100018E76656F1903F717A7B; StoreC8817CEB78124F1F8727FC66D293C6ECF6113C92DE1238F2F3AB185A251EE9BD. Artifacts: `artifacts/crap/preference-validation/`, including all48raw workload results, matrix snapshots, input hashes, source proof, TRX and build logs. Inventory retains239above30, now129above50/110middle; parent44 stays an original>30 finding. A previously absent authored-file static-constructor record (CRAP1) is explicitly backfilled, with no finding suppressed and no original-method remediation credit.


## Connection row label separation (November 23, 2026)

`ConnectionDisplayRow.UpdateGroup` complexity/CRAP64→40, with its contiguous name/detail/protocol presentation block moved verbatim into private `UpdateLabels`24. Both now have100% line/branch coverage; the original parent reached100% branches before extraction. Parent40 remains an original>30 finding. No new predicate, loop, allocation expression, native query, dependency or public API is introduced.

Focused original-first characterization adds27 NUnit4 cases. All104 relevant cases pass against original and candidate desktop/Store builds; affected app/tests and benchmark consumers build with zero warnings/errors. No full suites were run. Fresh coverage is limited to changed methods; unchanged inventory coverage is inherited and no fresh global percentage is claimed.

Direct row updates use a common friend bridge compiled once against originalB494 and copied identically to original/candidate probes. Separate assembly load contexts on STA execute the actual app DLLs. Eight fresh paired processes reverse load/warm order; six predeclared workloads use500ms warmup and seven alternating batches, targeting40ms per batch with adaptive operation counts. Tiering is disabled. Each operation performs two row updates; fixture construction, icon prewarm and serialized snapshots are outside timing. No owned build/test/coverage/profiler overlaps timing. Seven outcome/partial-failure snapshots match per process. There are no timed retries or omitted scenarios.

| Workload | Before µs/operation | After µs/operation | Paired geometric ratio (95% CI) | Allocated bytes, both sides |
|---|---:|---:|---|---:|
| Stable live row |0.627537|0.631251|0.997354 (0.983051–1.011865)|432|
| Group member detail |0.784105|0.793086|1.011622 (0.995597–1.027904)|640|
| Historical executable detail |0.715361|0.705859|0.995237 (0.975721–1.015143)|768|
| Status/network transitions |4.255199|4.267694|0.995804 (0.982897–1.008881)|4424|
| Pin transitions |0.619872|0.622954|0.999495 (0.982730–1.016546)|480|
| Ownerless endpoints |0.718804|0.728232|1.010322 (0.999202–1.021565)|504|

Times are medians of per-process medians; ratios/intervals use paired log ratios across eight processes, so the displayed time quotient need not equal the geometric ratio. The unchanged gate rejects latency or allocation95% lower bounds>1, a new allocation on a zero-allocation path, or outcome mismatches. All six workloads pass. Grouped/ownerless estimates are higher, with upper bounds allowing roughly2.8%/2.2% slowdown: this is no detected regression under this protocol, not proof of zero slowdown. Allocation intervals are exactly1. The test does not measure whole-app scheduling, render-thread latency or concurrent/reentrant row access.

Ordinary hardware-rendered fixture captures are blank on the unchanged original, under both hidden and visible launch. They are retained as invalid visual evidence. The same diagnostic software-rendering host runs all three actual app entry points for a separate69-image connections comparison and passing native/fixture assertions. All six contact sheets and three full-size network views were inspected; no new row label/layout difference was observed. PID/time/focus, capture-format and icon/fallback variations remain; the icon variation is not independently isolated, and no whole-window equality is claimed. It does not alter production or the performance protocol. Hardware-composited appearance remains unverified for this batch, independently of the pre-existing theme blocker.

Artifacts: `artifacts/crap/connection-row-labels/` (`perf-0.json` through `perf-7.json`, `performance-summary.json`, frozen input hashes, matrix snapshots, TRX, builds and render attempts). Candidate desktop assembly260F80184348B38FE0F7A2A1FD4C1B2F244D943CC9E827EBB32C9B18452AD1A8; Store4DED90DFE1405DCAFFC6C3567143C4276500691CB94DFC704F347A6535C3E2E6. Original/candidate source hashes and exact extraction proof are retained. The scope is label maintenance and direct row-update reliability, with no claim that the entire dirty working tree is accepted.


## Connection graph aggregation (November 23, 2026)

ConnectionGraphData.Build CRAP82.430336→34, complexity82→34,96%→100%lines/100%branches. Two cohesive private phases: ResolveCaptureOwnersAndReadings16(12/12lines16/16branches), AccumulateOwnerRates32(13/13lines31/32branches96.9%). Parent34andratehelper32remainabove30; all3unitsmeetrevised50. Originalpredicate/loops/order/arithmeticreconstructexactlyapartindentation/EOFtrivia;4explicitcompileraliases/all68oldpointsaccountedfor,4newcall/voidreturnpoints. No addedallocationexpression/pass/API/dependency/nativeoperation.

48newcases passFIRSToriginal;77combinedoriginalandcandidatecasespassbothTFMs. Sixexplicitcleanbuilds0warnings/errors. Userchangedpolicytofocusedneededtests/affectedbuilds; no routinefullsuitepermethod. Desktopfullalreadyunderwaycompleted8,800tests/all11+5readonlychecks/34exclusions andcoverage. Storefullstartedbeforeguardandtookowned-treecancellation;INTERRUPTED,NOTPASSED,notrestarted. Relevant77StorecasesandStorebuildspass. Connections-onlynativeoriginal/default/Store3runs69renderspass;all6sheets+3fullsizedarknetworkviewsinspected/no newgraph/layoutdifferenceobserved. No whole-windowequalityclaim;variantcapture/PID/path/icon/focusdifferencesretained.

**Performance passes the unchanged predeclared gates**, without retries, tolerance changes or scenario omission:4pairs/8processes,29scenarios/26timed, no latency/allocation95%lower bound above1, all100raw-buffer and16exported-guide PNG comparisons match. The original benchmark exe/DLL and inputs are identical on both sides, runtime10.0.15; no owned build/test/coverage/profiler overlaps timing. A summary dependency-order error was corrected using the existing raw data, with no timed rerun.

| Connection-history workload | Original ms | Candidate ms | Paired latency ratio95%CI | Original / candidate bytes per frame |
|---|---:|---:|---|---:|
| Executable |3.323192|3.134599|0.9514(0.7666–1.1809)|1,258,792 /1,256,424|
| Pinned |3.254668|3.273665|0.9629(0.6822–1.3590)|1,261,848 /1,259,480|

Allocation medians are2,368B/frame lower in both target workloads; allocation-ratio intervals are0.99796–0.99834/0.99796–0.99835. Mechanism is not independently attributed. The pinned simple latency median is slightly higher despite its lower geometric paired ratio. Four pairs, fixed order and shared-desktop variation leave wide intervals; **no speedup or zero-regression proof** is claimed. Higher point estimates remain visible: focus-live1.0751(CI0.7637–1.5135), pinned-selected1.0661(0.7985–1.4236),24-hour resource-trend1.1882(0.9700–1.4554), small-multiple guides1.0784(0.9180–1.2668). All26rows and allraw observations are in performance-review.md/JSON.

Six supplementary history save/read streams pass unchanged timing gates, but **three original-only partial restorations** limit comparability: pair0restart1860/1961, pair0normal1680/1961, pair1investigation540/600; all12candidate observations restore the full requested count. The first two pairs also have different file-byte counts, preserved in history-comparability-review.json. Not all24side observations perform equal complete work; no full-history throughput/equivalence inference is made. Budget1.5seconds and MonCorebytes are unchanged; causes remain unisolated. Cold startup, sustained load, native collection/concurrency, other DPI/native High Contrast, render-thread CPU and whole-process native allocation remain unmeasured.

All4,601original logical methods remain accounted for:4,597literal matches+4explicit compiler aliases, with2newprivate helpers yielding4,603compiled methods. No unrelated method shape, unknown score or unexpected finding is introduced. Exact inventory becomes **239above30:131above50 and108between30and50**. The original parent34and newratehelper32 remain above30; the added helper increases the original-target inventory by one while methodsabove50 decrease by one. Zero original>30findings resolve; independently characterized original-method-now≤30 count stays128. Source normalization20,952/29,202lines71.7%,17,247/23,109branches74.6%; raw21,471/30,002 and17,664/23,926 remain separate. Four call/void-return points explain line-denominator growth; branch denominators unchanged. This is completed desktop coverage, not a fresh global Store claim. All10method deltas,9nonstructural point differences and5mapped structural coverage gains retain source/XML review. Related rowUpdateGroup gains2branches but remainsCRAP64; six unrelated native/MCP/service runtime observations include gains and losses with unisolated causes and no selective resampling. Global line80% remains unmet.

DefaultappB494B1D1…/Store85DCE7E5…consistentacross3copieseachexpectedchanged;68otherproductionDLLcopiesunchanged. ConnectionsViewC41…/processgraphs7474…/benchmark4340…sourcesunchanged, onlyConnectionGraphData402…adopted. Whole-treeappearancefalse/inheritedthemeblockerandallpriorbehavior/native-dump/rejectedperformanceobservationsremainopen. Evidence: artifacts/crap/connection-graphs/.

## Palette description characterization (November 23, 2026)

Test-only characterization: `ChartPalettes.Description` CRAP992→31, CC31 unchanged,11/11lines100%,26/31branches83.9%,43cases. Meets revised≤50 but remains an original>30 finding. Tests preserve canonical English outputs, aliases/control whitespace, null/unknown fallback, existing-choice localization/shared selected-language precedence and getter purity. Five compiled string-dispatch gaps are retained; the nineteen allowed normalized outputs are characterized, without an IL-level unreachability claim or artificial refactor.

Full verification passes **8,752 desktop tests and8,672 Store tests**, all11steps plus5additional installed-service read-only checks each, with34/52expected exclusions unchanged. Two explicit nonincremental test builds have zero warnings/errors; Verify builds app/tests/benchmark. All43 case identities pass original-first/focused desktop/Store/coverage and both full variants.

All74production DLL copies remain identical to accepted graph baseline(default07B7970C…/Store5C4772B9…), with palette/model/all10resource sets/designer/culture implementation, graph7474… and benchmark4340… sources unchanged. No fresh timing/render campaign, speedup claim, or retry/relaxation of rejected gates. Prior graph performance intervals/higher point estimates/scope limits and existing appearance/behavior/native-dump/rejected-performance observations remain open.

All **4,601 original literal method identities and shapes match**, zero aliases/additions/unknown scores/unexpected findings. Exact accounting proves only one above50 crossing: Description992→31. **All238 above30 findings remain:132 above50 and106 between30 and50**; the original-method-now≤30 counter stays128. Source coverage is20,944/29,198lines(71.7%) and17,230/23,109branches(74.6%); raw21,463/29,998 and17,647/23,926 remain separate. Denominators unchanged; global line80% is unmet. All8method/23point changes are reviewed with source/XML: target11lines/26branches, caller getter1line, plus six unrelated pipe/native/MCP observations including losses whose runtime causes remain unisolated. No selective resampling/suppression; raw floating-threshold findings remain distinct from exact rational inventory accounting. The target's five compiled dispatch gaps and score31 remain explicitly reported.

Evidence: `artifacts/crap/palette-description/`.


## CPU marker hit characterization (November 23, 2026)

Test-only remediation: `CpuGraph.MarkerAt` CRAP72→8, CC8 unchanged,4/4lines and8/8branches100%,42windowless STA explicit-point cases. Grid/process/empty guards, timeline and pixel boundaries, nearest/stable coincident ties, five layouts/resize, series/window/marker replacement and no-side-effect identity/state contracts are characterized. Initial41/42floating-tie assumption is preserved; corrected coincident-moment ties pass without production changes or epsilon relaxation. Direct query evidence does not claim native mouse routing or rendered placement acceptance.

Full verification passes **8,709 desktop tests and8,629 Store tests**, all11steps plus5additional installed-service read-only checks each, with34/52expected exclusions unchanged. Two explicit nonincremental test builds have zero warnings/errors; Verify builds app/tests/benchmark. All42corrected case identities pass original-first/focused desktop/Store/coverage and both full variants.

All74 production DLL copies byte-identical to accepted graph baseline(default07B7970C…/Store5C4772B9…), CpuGraph9AF04C21…/graph7474…/benchmark4340… sources unchanged. No fresh timing/render campaign, speedup claim, or retry/relaxation of rejected gates. Prior graph intervals/higher point estimates and limits apply to the same executables; existing appearance/behavior/native-dump/rejected-performance observations remain open.

All **4,601 original literal method identities and shapes match**, zero aliases/additions/unknown scores/unexpected findings. Only MarkerAt resolves an original finding: **238 above30, including133 above50 and105 between30 and50** remain; independently characterized original-method-now≤30 tracking rises127→128. Source coverage is20,937/29,198lines(71.7%) and17,208/23,109branches(74.5%); raw21,456/29,998 and17,625/23,926 remain separate. Denominators fixed; global line80% is unmet. All12method/20point deltas are reviewed with source/XML: target and related marker/filter/distance/key branches, a full-run heatmap gain and six native sampling/pipe/cache changes including losses. Runtime causes for the latter observations remain unisolated; no selective resampling/suppression. WindowsGpuMemory.Attach remains CRAP48.238678… and retained above30. Shared parent/lambda source-point normalization explains raw-versus-normalized gains; raw float-threshold findings remain distinct from exact rational inventory accounting.

Evidence: `artifacts/crap/marker-hit-testing/`.


## Priority eligibility characterization (November 23, 2026)

Test-only remediation: `ProcessesView.CanSetProcessPriority` CRAP72→8, CC8 unchanged,2/2lines and8/8branches100%,19 real-binding menu cases. The hidden owned child supplies exact native lifetime/priority; tests query only, never click a choice/change priority, and verify unchanged priority/lifetime/alive state after every case. Protected/aggregate/stale records and view/snapshot safety states remain characterized, including controlled no-frame and historical/public-Load fixture combinations. No production/XAML/API/dependency change.

Full desktop8,667/Store8,587 tests, all11steps plus5 installed read-only checks each,34/52 exclusions unchanged.19new cases pass original-first/focused desktop/Store/coverage/full;2 explicit nonincremental builds zero warnings/errors. All74 production DLL copies byte-identical to graph baseline(default07B7970C…/Store5C4772B9…), graphsource7474…/benchmarksource4340… unchanged. No fresh timing/render campaign, speedup claim, or retry/relaxation of rejected gates. Prior graph performance intervals and limits apply to the same executables.

Current inventory239above30/134above50/105middle,4,601 unchanged original literal identities/shapes, zero aliases/additions/unexpected findings; original-method-now≤30 tracking126→127. Source coverage20,928/29,198lines71.7%,17,199/23,109branches74.4%; raw21,446/29,998 and17,614/23,926 separate. Global line80 remains unmet. All6method/8point changes retained: related priority callback/native lifetime plus unrelated MCP truncation/pipe cancellation observations with runtime causes unisolated. Initial copied wrapper class-lookup guard failure stopped before tests/builds, corrected and preserved. Existing appearance/behavior/native-dump/rejected-performance observations remain open. Evidence: `artifacts/crap/priority-eligibility/`.


## Usage menu routing characterization (November 23, 2026)

Test-only remediation: `UsageView.OnMenuOpening` CRAP 156→12, CC 12 unchanged, 11/11 lines and 11/12 branches (91.7%). Eighteen actual WPF Apps-key routing cases preserve placeholder replacement, routed owner versus selection, malformed/cleared contexts, aggregate/navigation and available/archive policy, app/frame snapshots and exceptions/retry. Copy/navigation are subscribed events; no clipboard/shell/native input operation is invoked. One mouse-coordinate fallback remains unexecuted. No production/XAML/API/dependency change.

All 18 new plus 21 existing direct-menu cases pass the first original run; all 18 pass focused desktop/Store/coverage and both full suites. Desktop passes 8,648 tests, Store 8,568, all 11 steps plus five installed-service read-only checks each, with 34/52 expected exclusions. Two explicit clean test builds have zero warnings/errors; Verify builds app/tests/benchmark. All 74 production DLL copies remain identical to the graph baseline (default 07B7970C…/Store 5C4772B9…), as do graph source 7474… and benchmark source 4340…. No new timing or rendering campaign, speedup claim, or retry/relaxation of any rejected gate is needed for unchanged executable bits. Prior graph performance intervals and scope limitations remain applicable.

Current inventory: 240 above 30, including 135 above 50 and 105 between 30 and 50, across 4,601 unchanged literal method identities, zero aliases/additions/unexpected findings. The original-method-now≤30 tracking count rises 125→126. Source coverage is 20,925/29,198 lines (71.7%) and 17,188/23,109 branches (74.4%); raw 21,442/29,998 and 17,603/23,926 remain separate. Denominators are fixed; global line 80% is unmet. Related Usage row/period branches gain coverage; the row update remains CRAP 32. Native snapshot forwarding records a hit while native image-path query loses one branch; causes remain unisolated and are not attributed to the new cases. Existing appearance/behavior/native-dump and prior rejected performance observations remain open. Evidence: `artifacts/crap/usage-menu-routing/`.

## Process graph aggregation (November 23, 2026)

`ProcessGraphData.Build` and `BuildGrouped` fall from CRAP/CC 64 and 80 to 22 each. Three verbatim phase extractions isolate individual sample population, group aggregation and compacted group idle fill. Their scores are 42, 38 and 20, all fully line-covered; the first two remain findings above 30. Total graph branch outcomes remain 144, with 140 covered. No new allocation/native/async expression or public/internal API change.

All 51 new and 30 existing graph cases pass against original desktop/Store builds before refactoring, and pass candidate focused/full verification. Six explicit clean builds report zero warnings/errors; full desktop passes 8,630 tests and Store 8,550, all 11 steps plus five installed-service read-only checks each. All 68 non-app production copies are unchanged; candidate WinMon copies agree per variant (default 07B7970C…/Store 5C4772B9…). All 219 original/default/Store native snapshot/search PNGs were reviewed. Fixed fixtures show no new layout or graph difference; live native measurements, capture clocks/PIDs and Store controls prevent a whole-window equality claim.

Four fresh original/candidate process pairs use the identical original benchmark executable (source 4340…) and unchanged inputs/gates. All 29 scenarios/26 timed scenarios pass; no paired latency or allocation 95% lower bound exceeds 1. All 100 raw pixel comparisons and 16 guide PNG pairs match. The following medians and paired intervals cover the principal affected UI paths; all 26 rows and raw batch distributions are retained in the evidence.

| Scenario | Original ms | Final ms | Paired latency ratio (95% CI) | Original/final allocated bytes |
|---|---:|---:|---|---:|
| live-frame | 4.3471 | 4.5129 | 1.0067 (0.8234–1.2308) | 560443.7333 / 560383.7333 |
| grouped-live-frame | 6.2365 | 6.0519 | 0.9550 (0.8106–1.1251) | 555573.4667 / 551845.4667 |
| pinned-selected-live-frame | 3.3664 | 3.3792 | 1.0169 (0.9544–1.0835) | 934861.8667 / 931103.7333 |
| pinned-selected-grouped-live-frame | 7.4410 | 7.2797 | 0.9721 (0.9164–1.0311) | 735119.6000 / 731423.6000 |
| pinned-selected-grouped-tooltip-open | 9.8969 | 10.0297 | 1.0193 (0.9311–1.1159) | 933952.0000 / 930257.8667 |

All six supplementary history save/read streams pass without flags; every run restores all 1,961 restart/normal-history samples and 600 investigation samples, with matching corresponding byte counts under the unchanged 1.5-second budget. Equal sizes are not file hashes. No scenario omission, selective retry or gate relaxation.

These measurements do not prove a speedup or zero regression. Four fixed-order pairs leave broad intervals: archive latency ratio 1.1410 (0.8854–1.4703); 24-hour resource-trend latency 1.1720 (0.8801–1.5606) and allocation 1.1140 (0.7901–1.5707). Their higher point estimates and unisolated variation remain reported. Native collection, render-thread CPU, cold startup, sustained load and other DPI are unmeasured. Prior rejected performance candidates and the inherited theme appearance blocker remain unresolved.

Current inventory: 241 above 30, including 136 above 50 and 105 between 30 and 50, across 4,601 compiled methods (all 4,598 original literal identities plus three helpers, zero aliases). The tracking counter of independently characterized original methods now≤30 rises from 123 to 125 for the two remediated methods; new helpers are excluded. Source coverage is 20,913/29,198 lines (71.6%) and 17,174/23,109 branches (74.3%); raw 21,430/29,998 and 17,589/23,926 remain separate. Six new call/return line observations explain the line denominator increase; unrelated native/cache/scheduling observations explain the six covered branch gains, with causes unisolated. Global line 80% remains unmet. Evidence: `artifacts/crap/process-graphs/`.

## Window state characterization (November 23, 2026)

Test-only remediation: MainWindow.OnWindowStateChanged110→10(CC10), ShowRequested156→12(CC12), RestoreFromTray6→2(CC2), complexity unchanged. Twenty real WPF Integration cases cover state/presentation/tray prerequisites, closing guards, normal/maximized restore, inactive activation and owned-window focus. Before first show and while hidden, programmatic state notifications defer; last visible restore state is preserved. Both initial mistaken Max-restore expectations/failure reports remain, final20assert observed original behavior. No production/XAML/API/dependency change.

All20focused desktop/Store/coverage and full8,579desktop/8,499Store pass, all11steps plus five installed read-only checks each,34/52expected exclusions. Two explicit nonincremental test builds have zero warnings/errors; Verify builds app/tests/benchmark. All74 product DLL copies and benchmark source4340… remain identical to baseline(default340DC1D1…/Store4BB585CC…). No new timing campaign, speedup claim, render campaign or retry/relaxation of the rejected Connections performance gate is used for this unchanged executable.

Exact current241>30/138>50/103middle,4,598literal identities/zero aliases/additions/unexpected findings; original≤30counter123. Source normalization20,907/29,192lines71.6%,17,168/23,109branches74.3%; raw21,424/29,992and17,583/23,926separate. Shared predicate1082first-observation normalization explains raw+26versus normalized+23branches. Four unrelated forwarding/icon/length/sequence coverage observations retain unisolated causes. Global line80% and inherited appearance/behavior/native-dump/rejected performance blockers remain unresolved. Evidence: artifacts/crap/window-state/.

## Service menu routing characterization (November 23, 2026)

Test-only remediation: ServicesView.OnServiceMenuOpening CC10 unchanged, CRAP110→10,0%→100%line and90%branch coverage through12 actual WPF Apps-key input/menu cases. Installed10.0.15 route was checked against versioned primary source and tested on owned windows, not a private event-argument factory. Cases preserve placeholder replacement, source/selection/snapshot identity, null/unavailable/malformed rejection, historical control states, reopening and observer failure/retry. No production/XAML/API/seam/reflection/dependency/native-service action change. Popup/window/styles/language subscriptions are cleaned up; copy uses an event subscription, not the clipboard. Mouse-coordinate fallback remains outside this keyboard-focused batch.
Full desktop verification passes8,559tests and Store8,479, all11steps plus five installed read-only checks each,34/52expected exclusions unchanged. Explicit nonincremental test-project builds across both TFMs have zero warnings/errors; Verify also builds app/tests/benchmark. All12 focused case identities pass both full variants. All74 production copies remain byte-identical, default340DC1D1…/Store4BB585CC…, with unchanged Services source/XAML and benchmark source4340…. Exact original production identity establishes no production-code performance change from these retained tests; no new timing/speedup claim, benchmark retry or relaxation of the rejected Connections gate is used.

All4,598 original literal identities and shapes remain, no aliases/additions/unknown scores/unexpected findings. Only menu routing crosses30: current243/140/103, independently characterized original≤30counter122. Source normalization20,884/29,192lines71.5%,17,145/23,109branches74.2%; raw21,401/29,992 and17,557/23,926 separate, denominators fixed/globalLine80unmet. Related CreateServiceMenu/RefreshRows branch gains and unrelated TCP6/GPU/network-cache/static-initializer observations remain in coverage review; no exact runtime cause or initializer nonexecution is inferred from a missing hit. Existing appearance/behavior/native-dump issues and the rejected Connections archive slowdown remain unresolved. Evidence: artifacts/crap/services-menu-routing/.

## Service pin routing characterization (November 23, 2026)

Test-only remediation: ServicesView.OnPin CC8 unchanged, CRAP72→8,0%→100%line/branch coverage through13 actual row-template routed-click cases. Eleven windowless cases and two owned-window keyboard-focus Integration cases preserve group/shared-service/history pins, invalid/stale contexts, pinned-only order, callback/handled exception state and focus recovery. No production/XAML/API/dependency/native-service action changes.

All13 pass focused default/Store/coverage and full verification8,547desktop/8,467Store, all11steps plus five installed read-only checks each,34/52expected exclusions. Two explicit nonincremental test builds have zero warnings/errors; Verify builds app/tests/benchmark too. All74 production DLL copies remain byte-identical to the accepted baseline (default340DC1D1…/Store4BB585CC…), with unchanged benchmark source4340…. No new timing campaign, speedup claim or retry/relaxation of the rejected Connections performance gate is used for unchanged production bytes. No new product rendering behavior is claimed.

Exact inventory244/141/103 across4,598unchanged literal method identities, independently characterized original≤30counter121; global line71.5% remains below80%, branches74.2%. ServiceDisplayRow.Update gains full branch coverage but remainsCC/CRAP92. Unchanged MCP usage length-flag branch variation3→2/4 is retained with unisolated cause. Existing appearance/behavior/native-dump observations and the rejected archive slowdown remain unresolved. Evidence: artifacts/crap/services-pin-routing/.

## Connections row extraction rejected (November 23, 2026)

A cohesive ConnectionsView.RefreshRows candidate lowered CC/CRAP130→50, with helpers8/10/14/16/24/12/26. Seven private methods replaced two separately reported locals, five net additions,36explicit mappings. All32 new characterization cases passed original default/Store first; candidate90focused/full8,534desktop/8,454Store, six clean explicit builds and69reviewed native renders also passed. The candidate is nevertheless **rejected**, and only the32cases remain.

All eight fresh benchmark processes completed, four pairs/29scenarios/26timed, identical frozen original harness and unchanged inputs against original340DC1D1…/candidateA1F1550F…. The unchanged gate flags resource-trend-archive: paired ratio1.211812,95%CI1.113892–1.318340, median-of-process medians4.41→5.40microseconds perAdd (about+0.99microseconds). All four candidate medians exceed their paired originals. Archive allocation1084.6667bytes is identical; no allocation interval rejects. All100raw-buffer comparisons and16guide PNG pairs match. No tolerance, selective retry, scenario removal, gate relaxation or unrelated production optimization is used.

Archive aggregation is timed independently of WPF with QueryThreadCycleTime/per-process calibration. Leaf MonCore.dll and harness bytes match; all eight runs retain/restore10079buckets and125011file bytes. Calibration values are not exported and cause is unisolated; neither a causal Connections regression nor an environmental cause is established. Full26intervals,56archive batch observations, allocation deltas and provenance remain in artifacts/crap/connections-rows/performance-review.md and adjacent JSON. Connections intervals include1 and mostly save80bytes, which does not waive the broader rejection. The 24-hour trend/pinned-grouped intervals also permit meaningful slowdowns.

Post-failure analysis (no new measurements) retains six history save/read streams without flagged lower bounds or empty restoration, but four original rows restore partial data (1560/1961,1680/1961,540/600,480/600) versus full candidates; three file sizes differ. Equal-work/complete-restoration claims are not made. Native1.5-second budget is unchanged. Fixed order/shared-desktop noise and UI-thread-only scope remain; native collection, render-thread CPU, cold startup, sustained load, other hardware/DPI and native High Contrast are not established. Native69images were reviewed without whole-window equality claims; inherited appearance/behavior/native-dump issues remain.
Fresh restored full desktop verification passes8,534tests and Store8,454, all11steps plus five installed read-only tests each,34/52expected exclusions. All90 original focused case identities pass both full variants. Six explicit nonincremental app/test/benchmark builds across both TFMs have zero warnings/errors, including friend consumers. All74 production copies match original exactly: default340DC1D1…/Store4BB585CC…, original sourceC41D5841…, unchanged benchmark source4340…. The failed structural source427F10D3… is separately archived. No additional performance measurement or visual change is claimed after restoring identical production bytes.

Original source restored guard427F10D3…→C41D5841…. Fresh restored coverage confirms parentCC/CRAP130,82/82lines,129/130branches (before127/130); all4,598original identities, unchanged245/142/103inventory, source line71.5% below80%, counter120unchanged. Rejected code/metrics/timings and initial sandbox failures remain archived. Investigate archive timing/calibration under controlled diagnostics before another structural attempt; retain all gate failures. Current evidence: artifacts/crap/connections-rows/restored/.

## Process list frame responsibilities (November 23, 2026)

Update CC/CRAP164→50 through four cohesive private responsibilities38/22/32/22; root50 and helpers38/32 remain original>30 findings. All89 cases pass original/candidate implementations. Exact source reconstruction retains callback/order/cache/trigger semantics; the sort-label lookup remains in the parent to avoid a new capturing closure. Explicit33 compiler aliases include one literal-name collision; all4,594 original methods plus four helpers are accounted for. Full desktop verification passes8,502 tests and Store passes8,422, each with all11 steps and five additional read-only installed checks. Expected exclusions remain34/52. All89 original case identities appear and pass in both full variants and five original/candidate focused stages. Six explicit nonincremental app/test/benchmark builds pass with zero warnings/errors, including both friend consumers; the already-completed successful default app build is reused as evidence rather than repeated. All37 production copies per variant are consistent: three app copies changed,34 unrelated copies/seven modules unchanged. Tested app identities are default340DC1D1… and Store4BB585CC…. Benchmark source4340… is unchanged; the identical frozen original harness drives originalBDF87A04… and the full-tested candidate.

Four fresh before/after process pairs pass all29 existing scenarios/26 timed scenarios, with no latency or allocation95% paired log-ratio lower bound above1. The unchanged gate rejects any such regression or pixel difference; no tolerance, selective retry or scenario exclusion is used. All100 raw-buffer comparisons match (23 scenarios plus top-level live/Usage, across four pairs); all16 direct-guide PNG pairs also have matching encoded hashes. The byte-identical original harness and unchanged inputs measure frozen originalBDF87A04… against fully tested candidate340DC1D1….

| Scenario | Original ms | Candidate ms | Paired latency ratio (95% CI) | Original/candidate bytes | Paired allocation ratio (95% CI) |
|---|---:|---:|---|---:|---|
| live-frame | 5.4125 | 4.5226 | 0.8537 (0.5330–1.3673) | 560404.7333 / 560380.5333 | 1.0013 (0.9969–1.0058) |
| small-multiples-live-frame | 3.3553 | 2.5798 | 0.8476 (0.6974–1.0302) | 519551.3333 / 519647.3333 | 1.0000 (0.9997–1.0003) |
| heatmap-live-frame | 3.7703 | 3.0826 | 0.8676 (0.6080–1.2380) | 564186.4000 / 564282.4000 | 1.0000 (0.9998–1.0003) |
| focus-live-frame | 3.4352 | 3.0781 | 0.8933 (0.8001–0.9974) | 538442.1333 / 538538.1333 | 1.0000 (0.9998–1.0003) |
| historical-frame | 1.0863 | 0.9961 | 0.9847 (0.8448–1.1478) | 144651.4000 / 144774.4000 | 1.0004 (0.9982–1.0027) |
| grouped-live-frame | 6.8457 | 6.8549 | 0.9924 (0.9009–1.0932) | 551773.4667 / 551869.4667 | 1.0000 (0.9998–1.0003) |
| services-live-frame | 6.4783 | 6.7052 | 0.9967 (0.9163–1.0840) | 1023996.4000 / 1023996.4000 | 1.0000 (0.9987–1.0013) |
| compact-live-frame | 1.6532 | 1.5032 | 0.9445 (0.8197–1.0883) | 346444.2667 / 346444.2667 | 1.0000 (1.0000–1.0000) |
| pinned-selected-live-frame | 3.4960 | 3.4516 | 0.9958 (0.8711–1.1385) | 930967.7333 / 931127.7333 | 1.0001 (0.9998–1.0003) |
| pinned-selected-grouped-live-frame | 7.6579 | 7.9195 | 1.0298 (0.8964–1.1830) | 731287.6000 / 731447.6000 | 1.0001 (0.9998–1.0003) |
| pinned-selected-grouped-tooltip-open | 10.9189 | 10.6926 | 0.9803 (0.7672–1.2525) | 930121.8667 / 930281.8667 | 1.0001 (0.9998–1.0003) |
| usage-ledger-add | 0.0197 | 0.0182 | 0.9463 (0.8097–1.1060) | 32.0000 / 32.0000 | 1.0000 (1.0000–1.0000) |
| usage-ledger-and-history-add | 0.0500 | 0.0513 | 1.0227 (0.8550–1.2232) | 34136.0000 / 34136.0000 | 1.0000 (1.0000–1.0000) |
| resource-trend-seven-day-render-selection | 3.7515 | 3.6187 | 0.9401 (0.6896–1.2815) | 800379.6000 / 800379.6000 | 1.0000 (1.0000–1.0000) |
| resource-trend-24-hour-wide-render-selection | 3.2603 | 3.3010 | 0.9718 (0.7919–1.1927) | 1088043.6000 / 1088043.6000 | 1.0000 (1.0000–1.0000) |
| resource-trend-archive | 0.0052 | 0.0051 | 0.9272 (0.5543–1.5511) | 1084.6667 / 1084.6667 | 1.0000 (1.0000–1.0000) |
| logical-processors-pinned-selected-live-frame | 6.3119 | 6.1120 | 0.9531 (0.8178–1.1108) | 1051532.8000 / 1051692.8000 | 1.0000 (0.9998–1.0002) |
| connections-executable-live-frame | 2.4813 | 2.4254 | 0.9812 (0.9463–1.0173) | 510885.8667 / 510885.8667 | 1.0000 (1.0000–1.0000) |
| connections-network-executable-live-frame | 3.4504 | 3.0508 | 0.8930 (0.7994–0.9976) | 1285286.5333 / 1285286.5333 | 1.0000 (1.0000–1.0000) |
| connections-network-history-executable-frame | 3.0814 | 3.0146 | 0.9488 (0.7398–1.2168) | 1258712.0000 / 1258872.0000 | 1.0001 (0.9999–1.0002) |
| connections-network-history-pinned-frame | 3.4551 | 3.2457 | 0.9579 (0.7229–1.2692) | 1261768.0000 / 1261928.0000 | 1.0001 (0.9999–1.0002) |
| usage-live-frame | 1.5226 | 1.3111 | 0.8691 (0.6218–1.2146) | 356536.0000 / 356536.0000 | 1.0000 (1.0000–1.0000) |
| cpu-guides-overlay-render | 3.7595 | 3.6122 | 1.0033 (0.9060–1.1110) | 39880.0000 / 39880.0000 | 1.0000 (1.0000–1.0000) |
| cpu-guides-focus-network-render | 1.7683 | 1.7943 | 1.0093 (0.8866–1.1490) | 30328.0000 / 30328.0000 | 1.0000 (1.0000–1.0000) |
| cpu-guides-small-multiples-render | 1.6075 | 1.5939 | 1.0018 (0.8779–1.1433) | 27480.0000 / 27480.0000 | 1.0000 (1.0000–1.0000) |
| cpu-guides-heatmap-render | 2.3653 | 2.2804 | 1.0284 (0.8453–1.2512) | 110168.0000 / 110168.0000 | 1.0000 (1.0000–1.0000) |

Passing these gates does not prove zero regression or a speedup. Four pairs, fixed before/after order and shared-desktop noise limit inference; slowdowns remain possible within the reported intervals. All raw process medians, seven/five batch distributions, allocation observations and intervals remain in ui-scenario-summary.json and measured-ui-*.json. Equal allocation medians do not prove byte-identical allocation distributions. Native collection, render-thread CPU, cold startup, sustained-load responsiveness, other DPI and native High Contrast are not measured by these synthetic runs. The UI-thread intervals include actual row update/layout and synchronous bitmap rendering; Usage verifies120 active rows and the enabled network columns remain visible. No new source allocation expression, native operation, state snapshot or async guard is introduced by the extraction.

Largest positive per-frame timing points are pinned-selected-grouped-live1.0298 (95%0.8964–1.1830), cpu-guides-heatmap1.0284 (0.8453–1.2512) and usage-ledger-and-history-add1.0227 (0.8550–1.2232). Live allocation ratio1.001339 (0.996855–1.005843) is the largest allocation point. These intervals permit meaningful slowdowns and exact causes are not isolated; no speedup/zero-regression claim is made. allocation-review.md retains the mixed-sign process deltas and matching source-pattern counts; no allocation-stack/IL proof is claimed.

Supplementary history review covers all three existing five-save/five-read workloads across the same four process pairs, prompted by the prior row batch's history-budget failure. It does not replace or relax the26-scenario gate. None of six save/read95% paired log-ratio lower bounds exceeds1; all reported results restore samples and the1.5-second native budget is unchanged. Raw time arrays, paired intervals, file sizes and restored counts remain in history-timing-summary.json and history-restoration-summary.json. Investigation read ratio1.0384 (95%0.9361–1.1519) and save1.0260 (0.8705–1.2092) remain uncertain. Existing budget-driven partial restoration is not silently normalized.


All six original/default/Store snapshot/search modes pass. All219 images were viewed in18 labeled contact sheets, with no new process-view layout issue observed. Thirty-nine desktop and37 Store snapshot PNGs match original hashes; remaining25/27 snapshot images and all nine search images per candidate differ. Native sampling, clocks, process inventories and existing Store surfaces limit interpretation. No masking, whole-window equality or individually explained pixel-difference claim is used. The controlled raw-buffer comparisons complement, rather than replace, these native visual checks; visual-review.md retains their scope and the inherited appearance blocker.

Exact inventory245/142/103, counter120 unchanged, global line71.5% below80%. Existing historical trigger/remainder double count is characterized/documented and deliberately unfixed; the initial85-pass/1-fail report is retained. First candidate compiler error is retained and corrected only in the private sort parameter. Pipe/native/async coverage variation, prior native dump observation and inherited theme appearance blocker remain. Full evidence: artifacts/crap/processes-update/.

## Process row extraction rejected (November 23, 2026)

The private pin/history/resource-column extraction reached Update CC/CRAP46 (original98/100.27), helpers14/18/20,65 passing characterization cases, full desktop8,413/Store8,333 tests and219 reviewed snapshot/search images. Its performance gate is incomplete and the extraction is rejected. Two original processes and one candidate completed; the second candidate stopped with `Investigation history did not survive its save/read budget.` Only one of four required pairs exists. All25 raw-buffer comparisons from that pair match, but they do not establish the required four-pair timing/allocation/pixel acceptance. The generic exception combines save, sample-restoration and usage-restoration conditions; logging was disabled and exact cause is unproven. No selective retry, increased budget, scenario exclusion or row-caused-regression claim is made.

Only ProcessRow.cs was restored to exact original source3519AB4F…; the rejected source7AF8E188…, tested candidate94604BDD…, shared original harnesses, failed log and all successful candidate reports/images are preserved in artifacts/crap/process-row/. The65 characterization cases remain and pass restored full desktop8,413/Store8,333 verification plus five read-only installed checks each. Four clean builds have zero warnings/errors; all74 production copies match originalBDF87A04…/CC150A61…. Restored full coverage measures Update97/97 lines;97/98 branches,CC98 unchanged and CRAP100.27→98, still above50. Exact inventory243/143/100; no structural remediation is accepted. Earlier timing/visual evidence applies to those identical original production bytes with all confidence/native limits retained. The first restored Store run had one native dump failure (0x80070018); four diagnostic variants and the full Store recheck then pass without source/test changes. Earlier failure is retained and exact cause remains unproven. The inherited appearance blocker remains. Restored evidence: artifacts/crap/process-row/restored/.

## Gauge peak characterization (November 23, 2026)

Test-only characterization reduces GaugePeakTooltip CRAP138.52→20, CC20 unchanged,18/18 lines and20/20 reported branches. All37 final focused cases pass original code and both full variants: desktop8,348/Store8,268 plus five read-only installed checks each. Four nonincremental app/test builds pass without warnings/errors. All eight production modules/all37 copies per variant and benchmark source match the preceding rendering batch (desktopBDF87A04…,StoreCC150A61…). No new timings/native smoke renders are claimed; prior paired timing/pixel/visual evidence applies to identical bytes with its confidence limits retained. Full/compact resource units, peak native values, missing/zero transitions, window boundaries and age formatting are characterized. An existing tooltip-before-gauge ordering issue remains unfixed and is asserted/documented: open details can show the preceding state until reopened. Initial17-pass/20-fail reports and subsequent characterization corrections are retained. Native path-query/pipe coverage variation and inherited appearance blocker remain. Global line71.3% is below80%. Evidence: artifacts/crap/gauge-peak/.

## Trend detail characterization (November 23, 2026)

Test-only characterization reduces TrendSampleValue CRAP91.48→18.74 with CC17 unchanged,9/11 lines and10/17 branches. All34 focused cases pass original code and both full variants: desktop8,311/Store8,231 plus five read-only installed checks each. Four nonincremental app/test builds pass without warnings/errors. All eight production modules/all37 copies per variant and benchmark source match the preceding rendering batch (desktopBDF87A04…,StoreCC150A61…). No fresh timings or native smoke renders are claimed; prior timing/pixel/visual evidence applies to identical bytes with all confidence limits retained. Tests exercise actual six-metric eligibility, missing/zero/upload-only values, UTC minute boundaries, gap/tie selection and recording/live transitions; no window is shown or native query issued. Dormant Send/default arms remain untested; method branch58.8% and global line71.3% remain below their targets. Native coverage variation and inherited appearance blocker are retained. Evidence: artifacts/crap/trend-detail/.

## CPU chart language characterization (November 23, 2026)

Test-only characterization reduces RefreshLanguage CRAP272→16 with CC16 unchanged,14/14 reported lines and6/16 branches. Ten focused cases pass original code and both full variants: desktop8,277/Store8,197 plus five read-only installed checks each. Four nonincremental app/test builds have zero warnings/errors. All eight production modules/all37 copies per variant and the benchmark source match the preceding verified rendering batch (defaultBDF87A04…,StoreCC150A61…). No new timings or native smoke renders were run or claimed; prior rendering timing/pixel evidence applies to identical production bytes with its confidence limits retained. Native-hover branches remain untested, so100% reported lines/CRAP16 do not prove full behavior. Global line71.2% remains below80%; inherited appearance blocker remains. Evidence: artifacts/crap/cpu-language/.

## CPU rendering responsibilities (November 23, 2026)

OnRender CC/CRAP106→50, full35 lines/48of50 branches. New cohesive DrawVisibleSeries24 and DrawSelection32 are fully covered; root50 and selection32 remain original>30 findings. Original/candidate145 focused cases include21 new original-code cases. Full desktop8,267/Store8,187 plus five explicit read-only installed checks each pass, with zero failures/compiler warnings/errors. Exact source/field/resource/clip-order proof, two new private helpers and20 explicit compiler aliases account for all4,592 baseline methods. All unrelated production copies remain unchanged. Native original/candidate chart smoke12 renders each passes,36 images reviewed; native data differences remain unmasked.

Four fresh broader pairs pass29 scenarios/26 timed and100 raw-buffer comparisons; four additional fixed-history pairs pass all seven timing/allocation gates and28 raw-buffer comparisons. The56 target PNGs form seven groups of eight encoded-identical images; all seven original0/candidate3 cases were visually reviewed. No existing measurement from an earlier production assembly is reused for acceptance. Identical source4340… harness bytes drive frozen originalE95F… and fully tested candidateBDF87A04…; all raw measurements are retained.

| Fixed-history scenario | Original ms | Candidate ms | Paired ratio (95% CI) | Original/candidate bytes |
|---|---:|---:|---|---:|
| cpu-metric-brush-selected-overlay-render | 4.361908 | 4.287613 | 0.9809 (0.9415–1.0220) | 46805.7 / 46833.4 |
| cpu-metric-brush-tile-0-render | 2.218039 | 2.144563 | 0.9814 (0.9153–1.0522) | 6931.2 / 6931.2 |
| cpu-metric-brush-tile-1-render | 0.837845 | 0.866834 | 1.0350 (0.9392–1.1405) | 6832.0 / 6832.0 |
| cpu-metric-brush-tile-2-render | 0.621578 | 0.639403 | 1.0598 (0.9210–1.2194) | 6832.0 / 6832.0 |
| cpu-metric-brush-tile-3-render | 0.963895 | 1.015593 | 1.0316 (0.8789–1.2108) | 6944.0 / 6944.0 |
| cpu-metric-brush-tile-4-render | 0.678916 | 0.730017 | 1.0284 (0.8264–1.2798) | 6832.0 / 6832.0 |
| cpu-metric-brush-tile-5-render | 0.484196 | 0.518241 | 1.0839 (0.9071–1.2950) | 6832.0 / 6832.0 |

The unchanged predeclared gate rejects any latency or allocation95% paired log-ratio lower bound above1, using four process pairs/df3 t intervals. No selective retry or tolerance change is used. Passing does not prove zero regression or speedup. Selected-overlay ratio 0.9809 (95%0.9415–1.0220). Largest positive timing point estimates: usage-ledger-add 1.1952 (0.7255–1.9688); cpu-metric-brush-tile-5-render 1.0839 (0.9071–1.2950); connections-network-history-pinned-frame 1.0777 (0.9923–1.1705). Meaningful slowdowns remain possible wherever intervals permit; causes are not isolated. All process medians, allocation distributions and intervals remain in the raw summaries. Four pairs, fixed before/after order and shared-desktop noise limit inference. UI-thread CPU and allocation include SetData/layout/new bitmap/synchronous rendering, while native collection, render-thread CPU, cold startup, sustained-load wall-time responsiveness, other DPI and native High Contrast remain unverified. No byte-identical allocation claim is inferred from equal medians. Largest allocation point estimate: live-frame 1.0108 (95%0.9994–1.0225); its raw process medians are retained and the cause is not isolated.

The extraction adds a no-op private selection-check call on unselected sparklines; broader live/compact/process scenarios measure that path. Fixed-history target cases keep all six outlines eligible and cover six tiles; the unchanged historical-frame selection may expire and is not sufficient target evidence by itself. Exact metrics retain246 above30/146 above50/100 through50, counter120 unchanged, global line coverage71.1% remains below80%. Inherited theme appearance blocker and existing one-bin/shutdown/native-keyboard limitations remain. Full evidence: artifacts/crap/cpu-render-pipeline/.

## CPU metric brush reuse (November 23, 2026)

OnRender CC118→106/CRAP118.025254→106 remains>50; two mappings now use existing MetricBrush (CC6). Original39 new/56 trace and original/candidate124 focused pass. Desktop8,246/Store8,166 plus five read-only installed checks each, zero failures/compiler warnings/errors. Original/candidate chart smoke12 renders each passes, including Store;36 native images reviewed with native-data differences preserved.

Four fresh broader pairs (29 scenarios/26 timed) and four target pairs (seven scenarios) pass unchanged strict95% lower-bound>1 latency/allocation rejection gates. All100 broader and28 target buffers match. The new --metric-brush-only mode keeps index61 eligible in120 immutable observations for820 renders, checking six fixed outline colors and each tile trace. Existing historical-frame can expire its selection; its workload/data remain unchanged and are insufficient target evidence. A default-mode compatibility probe preserves29 scenarios and25 snapshots, not additional paired timing evidence. Both expanded variants build without changing production bytes; each comparison uses one byte-identical harness and retains versioned sources/original verification manifest.

| Fixed-history scenario | Original ms | Candidate ms | Paired ratio (95% CI) | Original/candidate bytes |
|---|---:|---:|---|---:|
| cpu-metric-brush-selected-overlay-render | 4.307731 | 4.353235 | 1.0175 (0.9909–1.0448) | 46847.7 / 46751.7 |
| cpu-metric-brush-tile-0-render | 2.196820 | 2.272705 | 1.0432 (0.9780–1.1126) | 6931.2 / 6931.2 |
| cpu-metric-brush-tile-1-render | 0.849847 | 0.927330 | 1.0513 (0.9268–1.1924) | 6832.0 / 6832.0 |
| cpu-metric-brush-tile-2-render | 0.656487 | 0.621442 | 0.9712 (0.8973–1.0513) | 6832.0 / 6832.0 |
| cpu-metric-brush-tile-3-render | 0.921361 | 0.946619 | 1.0376 (0.9861–1.0917) | 6944.0 / 6944.0 |
| cpu-metric-brush-tile-4-render | 0.675021 | 0.685627 | 1.0128 (0.9359–1.0960) | 6832.0 / 6832.0 |
| cpu-metric-brush-tile-5-render | 0.494060 | 0.511828 | 1.0431 (0.9747–1.1164) | 6832.0 / 6832.0 |

No zero-regression/speedup claim: selected-overlay ratio1.0175 (95%0.9909–1.0448), memory tile1.0513 (0.9268–1.1924); broader compact/Usage1.0797/1.0745 (0.7901–1.4755/0.7516–1.5361). Meaningful increases remain possible, causes unproven, no selective retries. Selected allocation ratio0.9988 (0.9968–1.0009) varies; no byte-equality/improvement claim. Includes SetData/layout/new bitmap/synchronous rendering CPU and allocated bytes on the UI thread; excludes render-thread CPU, native collection, cold startup, sustained-load wall time and other DPI/High Contrast. Four pairs/fixed order/shared-desktop variation limit inference. Parent106 and inherited theme appearance blocker remain. Full results/reviews: artifacts/crap/cpu-metric-brush/ and the remediation report.

## CPU guide responsibility extraction (November 23, 2026)

OnRender CC146→118/CRAP146.021316→118.025254 remains>50; new private DrawGraphGuides CC28/CRAP28,20/20 lines/28/28 branches. Exact guarded-body move preserves all read/label/clip order and public signatures; ShowGrid guard stays at call site, so compact/process sparklines add no helper call. Original10 and original/candidate85 focused cases pass; Default8,207/Store8,127,all11 steps+5explicit read-only installed each,zero failures/compiler warnings/errors.

Fresh identical-harness four pairs:29 scenarios/26 timed, four direct real800×240 chart modes appended after prior25;120 observations,400 warmups/seven60-frame batches. No latency/allocation95% lower bound exceeds1; all92 scenario+4live+4Usage pixel pairs match, exported guide PNG pairs match, representative actual images reviewed. Direct workload includes bitmap construction/synchronous render UI-thread work and allocations, excludes render-thread CPU/native collection. Four pairs/fixed order/noise and all per-scenario intervals limit claims. Seven-day trend selection ratio1.093550 (95%0.958718–1.247346) has an unexplained positive point estimate; the gate does not detect a regression but a meaningful increase remains possible, retained without selective retries.

| Scenario | Original ms | Candidate ms | Paired ratio (95% CI) | Original/candidate bytes |
|---|---:|---:|---|---:|
| cpu-guides-overlay-render | 3.692231 | 3.616775 | 0.9907 (0.9367–1.0479) | 39880.0 / 39880.0 |
| cpu-guides-focus-network-render | 1.798638 | 1.760737 | 0.9811 (0.9225–1.0434) | 30328.0 / 30328.0 |
| cpu-guides-small-multiples-render | 1.649529 | 1.654875 | 1.0085 (0.9092–1.1187) | 27480.0 / 27480.0 |
| cpu-guides-heatmap-render | 2.477289 | 2.328152 | 0.9611 (0.8243–1.1206) | 110168.0 / 110168.0 |

Separately rebuilt harness DLL differed despite frozen source; preserved that build and used exact frozen original exe/DLL/PDB for both versions after unchanged-API/deps/runtime proof, never unequal-harness timings. Original app0FC98D/tested candidate9D0F,Store2A85DC;seven unaffected modules34copiespervariant identical,3appcopiesconsistent. Exact4591originals+1helper=4592,19explicitcompileraliases;inventory245/147/98,counter120unchanged. Source union20772/29190lines16956/23121branches;global80%linesunmet. Two unchanged native/sequence coverage declines retained with exact XML/uncertainty,no percentage-forcing reruns. Existing compact/shutdown/theme issues and baseline dense Heatmap label clipping remain; whole-tree appearancefalse,last accepted286/187/99separate. See [guide extraction](CRAP-REMEDIATION-2026-11-21.md#cpu-guide-responsibility-extraction) and `artifacts/crap/cpu-guides-extraction/`.

## Trace segment test-only verification (November 23, 2026)

`CpuGraph.DrawSegment` CRAP51.3853→44,CC44 unchanged,32/32 lines/44/44 reported outcomes. Seventeen original-code NUnit4 actual-render cases cover continuous vertices/raster, gaps,2DIP averaging/wash baseline,singleton/unavailable,all six styles,secondary disk default,tile styles and immutable redraw retention. Corrected observer uses documented .NET10 WPF path conversion; original flattening failure evidence retained. Existing one-bin compact missing-stroke defect reproduced with100DIP visible/101DIP no-segment controls, documented separately and unchanged. No production refactor/seam/dependency. Bin42→6 is collateral; target remains>30,independent original≤30 count120.

Clean builds verify all eight production modules/37 copies per variant byte-identical; Default0FC98DACCE14B812BB198548E09DDC57B011DCA266C90C4853B3493C174C89AD matches measured Usage candidate. Reuse its four-pair25-scenario/22-timed evidence with original limitations; no fresh timing/smoke for unchanged executable code. Default8,197/Store8,117,all11 steps plus5explicit read-only installed each,zero failures/compiler warnings/errors. Exact4,591 identities/shapes,inventory245/147/98. Source union20765/29188lines16948/23121branches; global80% lines unmet. Sole unchanged process-image query branch decline retained with exact XML/unproven cause,no percentage-forcing retry. OnRender146.021316 remains>50. Native HighContrast/DPI/live defect incidence not independently proved; keyboard gap/shutdown defect/theme blocker remain. Last accepted286/187/99 separate. See [trace segment characterization](CRAP-REMEDIATION-2026-11-21.md#cpu-trace-segment-characterization) and `artifacts/crap/cpu-trace-segments/`.

## Logical processor historical cursor test-only verification (November 23, 2026)

`CpuGraph.DrawLogicalProcessorSelection` CRAP72→8, CC8 unchanged,9/9 lines/8/8 branches. Twenty-two original-code NUnit4 cases verify actual per-cell dashed geometry and raster pixels:1/4/64 CPUs at timeline boundaries/midpoint, exact2DIP dense plots, visible-history maximum count, gaps/missing selected readings, hide/restore/eligibility/unavailability, small cells, monotonic placement and replacement. Positive controls prevent empty-renderer passes. No production refactor, seam or dependency.

Clean builds verify all eight production modules/37 copies per variant byte-identical; Default0FC98DACCE14B812BB198548E09DDC57B011DCA266C90C4853B3493C174C89AD matches the measured Usage candidate. Reuse its four-pair25-scenario/22-timed evidence with original limitations; no fresh timing/smoke for unchanged executable code. Default8,180/Store8,100,all11 steps plus5explicit read-only installed each,zero failures/compiler warnings/errors. Exact4,591 identities/shapes,inventory246/148/98,counter120. Source union20742/29188lines16917/23121branches; global80% lines unmet. OnRender153.311388/DrawSegment51.3853 remain>50. One unchanged native forwarding hit declines1→0,CRAP1→2, retained with exact XML/unproven observation cause and no percentage-forcing rerun. Physical processor-group collection/DPI/theme/composition variants are not independently proved. Existing keyboard gap/shutdown defect/theme appearance blocker remain; last accepted286/187/99 separate. See [logical processor selection](CRAP-REMEDIATION-2026-11-21.md#cpu-logical-processor-selection-characterization) and `artifacts/crap/cpu-logical-selection/`.

## Alternative historical selection test-only verification (November 23, 2026)

`CpuGraph.DrawAlternativeSelection` CRAP420→20, CC20 unchanged,20/20 lines/branches. Twenty-four original-code NUnit4 cases verify actual WPF drawing positions/colors and raster pixels: lane order, chosen/fallback focus, missing/zero/clamped values, network scaling, eligibility and replacement. Negative cases require populated traces/heat pixels; visibility clearing has a populated marker control. Initial observer failure confused filled trace points with outlined markers; retained evidence, corrected observer, unchanged app. No production refactor/seam/dependency.

Clean builds prove all eight production modules/37 copies per variant byte-identical; Default0FC98DACCE14B812BB198548E09DDC57B011DCA266C90C4853B3493C174C89AD matches the measured Usage candidate. Reuse its four-pair25-scenario/22-timed evidence with original limitations; no fresh timing/smoke for unchanged executable code. Default8,158/Store8,078,all11 steps plus5explicit read-only installed each,zero failures/compiler warnings/errors. Exact4,591 identities/shapes; inventory247/149/98,counter119. DrawHeatmap198.8724→20 is a collateral crossing with contracts still incompletely characterized. OnRender156.913792/DrawSegment51.3853 remain>50. Source union20732/29188lines16896/23121branches; global80% lines unmet. Five unchanged native/concurrent declines preserved with exact XML/uncertainty, no percentage-forcing retries. DPI/theme/composition variants not independently proved; existing keyboard limit/shutdown defect/theme appearance blocker remain. Last accepted286/187/99 separate. See [alternative selection](CRAP-REMEDIATION-2026-11-21.md#cpu-alternative-selection-characterization) and `artifacts/crap/cpu-alternative-selection/`.

## CPU graph keyboard test-only verification (November 23, 2026)

`CpuGraph.OnKeyDown` CRAP1190→44.7864, CC34 unchanged,15/19 lines and29/34 branches. Forty-four original-code NUnit4 windowless routed-event cases characterize inclusive time/reference boundaries, gaps, guards, host-owned selection feedback, callback exceptions/order and reentrant replacement. Native Ctrl+arrow remains uncovered:78.9% lines misses80%/critical90%;85.3% branches exceeds70%. Retain the original>30 finding and independent original≤30 count118; no production refactor/seam/dependency.

Clean pre-test/final builds prove all eight production modules/37 copies per variant byte-identical; Default0FC98DACCE14B812BB198548E09DDC57B011DCA266C90C4853B3493C174C89AD matches the measured Usage candidate. Reuse its fresh four-pair25-scenario/22-timed latency/allocation/pixel evidence with original limitations; no new timing/smoke for unchanged executable code. Default8,134/Store8,054,all11 steps plus5explicit read-only installed each,zero failures/compiler warnings/errors. All4,591 identities/shapes match; inventory249>30 =151>50 +98middle. Source union20689/29188lines16830/23121branches; global80% lines unmet. Two unchanged native/concurrent branch declines retained with exact XML, no percentage-forcing retries. Shutdown defect/theme appearance blocker remain unresolved; last accepted286/187/99 separate. See [CPU graph keyboard](CRAP-REMEDIATION-2026-11-21.md#cpu-graph-keyboard-characterization) and `artifacts/crap/cpu-graph-keyboard/`.

## Usage frame-control extraction (November 23, 2026)

`UsageView.RefreshRows` CRAP56.4229→30/CC56→30, new cohesive UpdateFrameControls CRAP26/CC26. Sixteen original-production cases characterize null/availability/capture/archive, filtering/reset/zero totals and reentrant read order;56 focused candidate cases and Default8,090/Store8,010 pass, no warnings/errors. Full target line coverage,29/30 and26/26 branches. All4,590 originals match with six explicit compiler aliases; one added helper. Inventory249/152/97, independent original≤30 count118. Exact source move preserves assignments and side effects.

The old macro did not activate Usage. Updated identical harnesses append120 visible apps after existing24 scenarios, checking visibility/rows and saving Usage PNGs. Fresh four process pairs /25 scenarios /22 timed, runtime10.0.15, loggingoff,600×850 synthetic inputs,400 warmups/seven60-frame batches (trends retain five). No latency/allocation95% lower bound exceeds1. Active Usage aggregate process median **1.592046→1.300486ms/frame**, paired geometric ratio **0.812447,95%0.724584–0.910965**, allocation **356,536→356,536bytes/frame**. Fixed order/four pairs/noise limit inference; unrelated scenarios retain their broad intervals. Original B654 and tested candidate0FC98DACCE14B812BB198548E09DDC57B011DCA266C90C4853B3493C174C89AD use identical harness bytes. The preliminary24-scenario run is excluded setup evidence.

All76 scenario pixel comparisons plus4 live/4 Usage comparisons match;16 saved PNGs have two view-specific hashes. Representative actual original/candidate live and Usage images reviewed. Seven unaffected modules/34 copies per variant remain byte-identical. No native collection, WPF render-thread or whole-app profiling. Existing shutdown defect/theme centering failure remain unresolved; whole-tree appearance acceptancefalse, last accepted286/187/99 retained. Global80% line coverage unmet; unchanged pipe coverage decline preserved without retries. See [Usage frame controls](CRAP-REMEDIATION-2026-11-21.md#usage-frame-controls-characterization-and-extraction) and `artifacts/crap/usage-frame-controls/` for full raw results, intervals, source/identity/input proofs and limitations.

## Usage sorting test-only verification (November 23, 2026)

`UsageView.OnRankHeader` drops **72→8**, CC8 unchanged, **6/6 lines /8/8 branches**. Seven original-production NUnit4 Integration cases use actual bound headers to verify five rank mappings, complete order, retained filters/selection/row identity, settings publication, active feedback, idempotence and choices retained across first/later frames. Empty executable paths use the completed fallback; no native icon lookup. No production logic, seam or dependency change; independent original≤30 count116→117.

Forced clean pre-test and final builds prove **all eight production modules /37 copies per variant byte-identical**, Default app B654AF8ECEF6EDEC9ED2D5F38E16B10A09428DB0CCF9B68F7F2FDA66ED99D9D9. Existing capture-presentation four-pair /24-scenario /21-timed evidence and confidence-interval limits apply unchanged; no new timing or smoke. Default **8,074** / Store **7,994**, all 11 steps plus five explicit read-only installed checks each, zero failures/compiler warnings/errors. Current **250 >30 =153 >50 +97 middle**, all4,590 identities/shapes unchanged. Source union20,669/29,186 lines and16,792/23,121 branches; global80% lines remains unmet. The sole async icon branch decline is retained with exact XML and uncertainty, without rerunning to improve percentages. **Shutdown defect and inherited theme appearance blocker remain unresolved**; last fully accepted286/187/99 stays separate. No RefreshRows extraction is included; any future structural change needs fresh before/after evidence. See [Usage sorting characterization](CRAP-REMEDIATION-2026-11-21.md#usage-sorting-characterization).

## Usage menu test-only verification (November 23, 2026)

`UsageView.CreateMenu` drops **210→14**, CC14 unchanged, **11/11 lines /14/14 branches**. Twenty-one original-production NUnit4 Integration cases cover menu state, captured app/frame/eligibility, exact copied live/archived details, aggregate missing readings, subscriber lifecycle and exception propagation. Detached menus never open; no clipboard, folder launch or icon lookup. No production logic, seam or dependency change; only the composition target increments independent original≤30 count115→116. Shell execution/hit testing and parent-window navigation revalidation remain outside this boundary.

Forced clean pre-test and final builds prove **all eight production modules /37 copies per variant byte-identical**, Default app B654AF8ECEF6EDEC9ED2D5F38E16B10A09428DB0CCF9B68F7F2FDA66ED99D9D9. Existing capture-presentation four-pair /24-scenario /21-timed evidence and confidence-interval limits apply unchanged; no new timing or smoke. Default **8,067** / Store **7,987**, all11 steps plus five explicit read-only installed checks each, zero failures/compiler warnings/errors. Current **251 >30 =154 >50 +97 middle**, all4,590 identities/shapes unchanged. Source union20,662/29,186 lines and16,779/23,121 branches; global80% lines remains unmet. Two concurrent/native coverage declines are retained with exact XML and uncertainty, without percentage-forcing reruns. **Shutdown defect and inherited theme appearance blocker remain unresolved**; last fully accepted286/187/99 stays separate. See [Usage menu characterization](CRAP-REMEDIATION-2026-11-21.md#usage-menu-characterization) and batch evidence.

## Normal shutdown test-only verification (November 23, 2026)

`MainWindow.OnClosing` drops **1228.3712→82.0543**, CC36 unchanged, **49/73 lines /24/36 branches**, and **remains above50**. Sixteen original-production NUnit4 Integration cases cover owned awaited shutdown, persistence/error paths, history policy, lock release, guards and pending captures. An existing immediate-completion reentrant Close exception was discovered in a normal opt-out case and deterministically characterized in diagnostic mode; original failing evidence is retained separately and production behavior is unchanged. Successful-close cases explicitly require awaited work. Independent original≤30 count stays115; five helpers cross below30 through partial caller coverage, with no independent remediation claims.

Forced clean pre-test and final builds prove **all eight production modules /37 copies per variant byte-identical**, Default app B654AF8ECEF6EDEC9ED2D5F38E16B10A09428DB0CCF9B68F7F2FDA66ED99D9D9. Existing capture-presentation four-pair /24-scenario /21-timed evidence and confidence-interval limits apply unchanged; no new timing or smoke. Default **8,046** / Store **7,966**, all11 steps plus five explicit read-only installed checks each, zero failures/compiler warnings/errors. Current **252 >30 =155 >50 +97 middle**, all4,590 identities/shapes unchanged. Source union20,642/29,186 lines and16,756/23,121 branches; global80% lines remains unmet. MCP projection branch decline is retained without rerunning to improve percentages. **Inherited theme appearance acceptance remains false**, last accepted286/187/99 separate. See [partial shutdown characterization](CRAP-REMEDIATION-2026-11-21.md#normal-shutdown-characterization--target-remains-above50), coverage review and existing-defect evidence; update/private pending-task contracts and live-window frequency remain unverified.

## CSV dialog test-only verification (November 23, 2026)

`CsvExportDialog.OnChoiceChanged` drops **702→26**, CC26 unchanged, **23/23 lines /21/26 branches**; partial-control guard and reported switch outcomes remain. `get_Kind` drops **110→10**, CC10, **5/5 lines /10/10 branches**; `OnSave` **72→8**, CC8, **9/9 lines /8/8 branches**. Forty original-production NUnit4 Integration cases exercise six choices/fallback, notes, range/name transitions, invariant date syntax and real transparent owned modal saves, invalid-name correction and close. No production logic, seam or dependency change. Only getter/save increment independent original≤30 count113→115.

Forced clean pre-test and final builds prove **all eight production modules /37 copies per variant byte-identical**; Default app remains B654AF8ECEF6EDEC9ED2D5F38E16B10A09428DB0CCF9B68F7F2FDA66ED99D9D9. Reuse capture-presentation four-pair /24-scenario /21-timed performance evidence and its confidence-interval limits; no new timing or smoke run. Default **8,030** / Store **7,950**, all11 steps plus five explicit read-only installed checks each, no failures/compiler warnings/errors. Current **257 >30 =156 >50 +101 middle**, all4,590 method identities/shapes unchanged. Source union20,532/29,186 lines and16,714/23,121 branches; global80% lines remains unmet. Four native/live coverage declines are recorded with exact XML and uncertainty, without rerunning to improve percentages. **Inherited theme appearance acceptance remains false**; last accepted286/187/99 remains separate. See [CSV dialog characterization](CRAP-REMEDIATION-2026-11-21.md#csv-dialog-characterization) and batch artifacts. Dialog tests perform no file writes and do not establish native file-picker or end-to-end export equivalence.

## Sampling and chart-series test-only verification (November 23, 2026)

`CurrentSamplingOptions` drops **110→10**, CC10 unchanged, **11/11 lines /7/10 branches**; native non-null snapshot/session propagation remains uncharacterized. `OnSeriesChanged` drops **238.6536→16**, CC16 unchanged, **22/22 lines /16/16 branches**, including direct observation that restoring toggles never publishes partial preferences. Twenty-two original-production NUnit4 Integration cases cover stored/automatic targets, count semantics, immutable options/pin retention, all six bits, focus/secondary rules and indeterminate toggles. No production logic, seam or dependency changes; only the fully characterized series target increments the independent original≤30 count112→113.

Forced clean pre-test and final builds prove **all eight production modules /37 copies per variant byte-identical**; Default app remains B654AF8ECEF6EDEC9ED2D5F38E16B10A09428DB0CCF9B68F7F2FDA66ED99D9D9. Reuse capture-presentation four-pair /24-scenario /21-timed performance evidence and its confidence-interval limits; no new timing or smoke run. Default **7,990** / Store **7,910**, all11 steps plus five explicit read-only installed checks each, no failures/compiler warnings/errors. Current **260 >30 =159 >50 +101 middle**, all4,590 method identities/shapes unchanged. Source union20,484/29,186 lines and16,672/23,121 branches. Four native/live coverage declines (GPU/adapter cache refresh, sampler static initialization and MCP projection) are recorded with exact XML and uncertainty, without rerunning to improve percentages. **Inherited theme appearance acceptance remains false**; last accepted286/187/99 remains separate. See [sampling/series characterization](CRAP-REMEDIATION-2026-11-21.md#sampling-and-chart-series-characterization) and batch artifacts for remaining native attribution gaps.

## Session-end persistence test-only verification (November 23, 2026)

`SaveForSessionEnd` drops **506→26.7624**, CC22 unchanged, **22/28 lines /19/22 branches**; secondary80% line/90% critical coverage remains unmet and it is not counted as independent full-method remediation. `SaveResourceTrendsForSessionEnd` drops **110→10**, CC10 unchanged, **10/10 lines /10/10 branches**, including its actual three-second deadline through the existing pending-task hook. Nineteen original-production NUnit4 Integration cases cover owned archive/history/preferences roundtrips, guards, cancellation, optional ownership and independent failures. No production code, seam or dependency changes.

Forced clean pre-test and final builds prove **all eight production modules /37 copies per variant byte-identical**; Default app remains B654AF8ECEF6EDEC9ED2D5F38E16B10A09428DB0CCF9B68F7F2FDA66ED99D9D9. Reuse the capture-presentation four-pair /24-scenario /21-timed performance evidence and its confidence-interval limits; no new timing or smoke run. Default **7,968** / Store **7,888**, all11 steps plus five explicit read-only installed checks each, no failures/compiler warnings/errors. Current inventory **263 >30 =162 >50 +101 middle**, all4,590 method identities/shapes unchanged; independent original≤30 count112 includes only the fully covered trend target. Source union20,450/29,186 lines and16,647/23,121 branches. The sole native process-image branch decline is recorded without percentage-forcing rerun. **Inherited theme appearance acceptance remains false**; last accepted286/187/99 remains separate. See [session-end characterization](CRAP-REMEDIATION-2026-11-21.md#session-end-persistence-characterization) and batch artifacts for remaining deadline/native lifecycle gaps.

## Capture observation test-only verification (November 23, 2026)

`MainWindow.ObserveForCaptures` drops **CRAP 110→10**, unchanged complexity **10**, **12/12 lines / 10/10 branches**. Twenty NUnit 4 Integration cases pass on original production code and both full suites: rule-state continuity/reset, equal-value settings, exact cooldown, gap/disable handling, simultaneous CPU/memory ordering and bounded real report writes/failures. Existing dispatcher completion events wait for actual marker outcomes before cleanup; no production seam or logic change. `WriteCaptureAsync` also reaches **42→6**, 14/14 lines / 6/6 branches through the pipeline, without a separate independent shutdown/lifecycle claim. The original-method independent ≤30 counter becomes **111**.

Forced clean builds and final checks prove **all eight production modules / 37 copies per variant byte-identical**. Default app remains `B654AF8ECEF6EDEC9ED2D5F38E16B10A09428DB0CCF9B68F7F2FDA66ED99D9D9`, matching the four-pair capture-presentation candidate below. Existing 24-scenario / 21-timed latency/allocation/pixel evidence and its confidence-interval limits apply unchanged; no new timing or smoke run was performed. Full Default **7,949** / Store **7,869**, all 11 steps plus five explicit read-only installed checks each, zero failures/compiler warnings/errors. Current **267 >30 = 165 >50 + 102 >30 and ≤50**, all 4,590 baseline method identities/shapes unchanged. Source union **20,331/29,186 lines / 16,582/23,121 branches**; global 80% line target remains unmet. Native/live-scheduling coverage declines are retained with uncertainty rather than hidden by rerunning. **Inherited theme appearance acceptance remains false**, and last accepted **286/187/99** stays separate. See [observer characterization](CRAP-REMEDIATION-2026-11-21.md#capture-observation-characterization) and batch artifacts.

## Capture menu test-only verification (November 23, 2026)

`MainWindow.CreateCapturesMenu` drops **CRAP 156 → 12**, unchanged complexity 12, **47/47 lines / 11/12 branches**. All25 NUnit 4 Integration cases pass on original code and both full suites, covering watcher/resources, entry order/icons/accessibility, recognized-report counts on an owned dispatcher/folder, retained-peak callbacks and range boundaries. `OpenCapture` additionally moves156 → 12.6667 (15/18 lines/9/12 branches), with its native report/dialog fallback explicitly uncharacterized. Only menu composition increments the independent original≤30 count to 110. No production changes or new dependencies.

Forced clean builds and final hashes prove **all eight production modules / 37 copies per variant byte-identical**. Default app remains `B654AF8ECEF6EDEC9ED2D5F38E16B10A09428DB0CCF9B68F7F2FDA66ED99D9D9`, matching the four-pair capture-presentation performance candidate below. Existing24-scenario/21-timed latency/allocation/pixel evidence and its uncertainty apply unchanged; no new timings or smokes were run for test-only files. Full Default **7,929** / Store **7,849**, all 11 steps plus five explicit read-only installed checks each, zero failures/compiler warnings/errors. Current **269 >30 = 166 >50 + 103 >30 and ≤50**, all 4,590 baseline methods/shapes matched, no additions/aliases. Exact coverage/deltas remain in batch artifacts; global 80% line target remains unmet. **Inherited theme appearance acceptance remains false**, last accepted 286/187/99 stays separate. See [menu characterization](CRAP-REMEDIATION-2026-11-21.md#capture-menu-characterization) and `artifacts/crap/capture-menu/`.

## Capture presentation characterization (November 23, 2026)

`MainWindow.RefreshCaptures` moves from **CRAP 1,070.6536 to 40**, complexity unchanged at **40**, with **44/44 lines / 39/40 branches**. Eight cases pass before production edits; 33 pass on the unchanged original body and both full suites. A single internal fixture member seeds marker state without live report writes/notifications; source projection proves no other Internal edit and byte-identical capture implementation. No new fields/dependencies/public APIs or runtime calls. The method remains >30; the fixture score of 1 does not inflate independent remediation.

Four UI process pairs / 24 scenarios / 21 timed scenarios pass the existing 95% latency/allocation gate, with 72 rendered-scenario buffer comparisons + four live images equal. Original timing precedes the source edit; no builds/tests/profilers overlap timings. Times are process medians aggregated by median; intervals use paired log ratios. No regression detected in sampled paths, without claiming equivalence or whole-app performance.

| Scenario | Before | After | Paired ratio | 95% interval |
|---|---:|---:|---:|---:|
| live-frame | 4.4819 ms | 4.6423 ms | 1.0636 | 0.9842–1.1494 |
| compact-live-frame | 1.6557 ms | 1.6311 ms | 0.9839 | 0.7750–1.2492 |

Original app `2DECFA02AABB51030A68F70F54F9E3BB4E1CDB9ACDE8D5F38CD77F1948B6DA8A`; candidate `B654AF8ECEF6EDEC9ED2D5F38E16B10A09428DB0CCF9B68F7F2FDA66ED99D9D9`, matching measured/tested copies. Forced clean builds and pre/post checks keep 37 production copies per variant consistent; all seven unaffected production modules are byte-identical. Full Default **7,904** / Store **7,824**, all 11 steps plus five explicit read-only installed checks each, zero failures/compiler warnings/errors. Current measurement **271 >30 = 168 >50 + 103 >30 and ≤50**, 4,589 baseline methods + one fixture = 4,590; 97 one-to-one generated ordinal aliases. Independent original ≤30 count remains **109**. No unchanged method loses coverage; native and UI gains are collateral. Source union **20,211/29,186 lines and 16,534/23,121 branches**; 80% global line target remains unmet. **Inherited theme appearance acceptance remains false**, last accepted **286/187/99** stays separate. See [capture presentation report](CRAP-REMEDIATION-2026-11-21.md#capture-presentation-characterization) and batch artifacts for raw evidence and limitations.

## Guide navigation test-only verification (November 22, 2026)

`MainWindow.ShowGuideTarget` falls from **CRAP 170.9286 to 15.9286**, unchanged complexity **14**; **11/14 lines, 13/14 branches**, 49 new original-implementation NUnit cases. Navigation preserves exact compact exceptions, panel expansion, tab/history state, scrolling/focus order, exception identity and reentrant changes. Current OS animation-disabled preference/base opacity are observed without settings changes or sleeps. Existing unavailable Export/Automatic and nonfocusable Walkthrough outcomes are retained and documented; ready owned toolbar controls separately demonstrate focus without action activation. No production changes or new dependencies.

Forced clean app/test/benchmark builds and final checks prove all eight production modules / 37 copies per variant **byte-identical**. Default app remains `2DECFA02AABB51030A68F70F54F9E3BB4E1CDB9ACDE8D5F38CD77F1948B6DA8A`, matching the keyboard candidate measured in eight native pairs and four full UI pairs below; existing limits still apply. No new timings or visual smokes were run. All 11 steps pass on Default **7,871** / Store **7,791**, plus five explicit read-only installed checks each, zero failures/compiler warnings/errors. Current measurement **271 >30 = 169 >50 + 102 >30 and ≤50**, unchanged **4,589** method identities/shapes, independently characterized original-method ≤30 count **109**. Exact aggregate and unchanged-method delta review are retained. **Inherited theme appearance acceptance remains false** and last accepted **286/187/99** stays separate. See [guide navigation characterization](CRAP-REMEDIATION-2026-11-21.md#guide-navigation-characterization) and `artifacts/crap/guide-navigation/`.

## Guide target test-only verification (November 22, 2026)

`MainWindow.GuideTarget` drops from **CRAP 1,640 to 40**, unchanged complexity 40, with **13/13 lines / 38/40 branches** and 45 cases passing against original production code. No production source changes or new dependencies. Forced clean app/test/benchmark builds before verification and final module checks prove **all eight production modules / 37 copies per variant byte-identical**. Default app remains `2DECFA02AABB51030A68F70F54F9E3BB4E1CDB9ACDE8D5F38CD77F1948B6DA8A`, matching the keyboard batch's measured candidate below. Its eight native pairs and four full UI pairs apply unchanged; no new timing or smoke run was performed. No new performance effect is introduced by test-only files.

All 11 verification steps pass on Default **7,822** / Store **7,742** tests, plus five explicit read-only installed checks each, zero failures/warnings/errors. Current measured inventory is **272 >30 = 170 >50 + 102 >30 and ≤50**, across unchanged 4,589 method identities. `ShowGuideTarget` remains **170.9286** with only its unknown guard characterized; `SetChartPanelCollapsed` crossing to 10 is collateral. Original-method independent ≤30 count remains 108. Two unchanged native/live-sampling branches decline; exact XML and limits are retained rather than hidden by reruns. Source-line union is **20,152/29,185 lines / 16,479/23,121 branches**; line coverage remains below 80%. **Whole-tree appearance acceptance remains false** because of the inherited theme gauges blocker. See [guide target characterization](CRAP-REMEDIATION-2026-11-21.md#guide-target-characterization) and `artifacts/crap/guide-target/`.

## Window keyboard characterization (November 22, 2026)

`MainWindow.OnWindowKeyDown` starts at complexity **44 / CRAP 1,980**. All **84 cases passed before production edits**; the minimum nullable modifier observation then enables **147 passing cases** on the same flow. Core complexity/CRAP is **46**, with **29/29 lines / 45/46 branches**. Native XAML signature and read timing are retained, with a named diagnostic forwarder in `MainWindow.Internal.cs`. There is no new state, dependency or mutable override, and no subsequent complexity extraction. Entry/fixture scores of 1 do not represent full ≤30 remediation; the core remains a >30 finding.

Eight isolated .NET 10.0.15 process pairs / nine native scenarios pass the existing 95% latency/allocation gate. Identical probe bytes yield **72 matching fingerprints / 504 matching paired allocation batches**, all **88 bytes per dispatch**. Native retained Escape medians **0.4159 → 0.4197 µs**, paired ratio **1.0250**, interval **0.9771–1.0753**. Native F without modifiers **2.4767 → 2.5879 µs**, ratio **1.0014**, interval **0.8825–1.1363**. WPF-observed native modifiers must remain None at the batch checks; actual native-Control timing/delivery is not claimed. Controlled Ctrl+F behavior, focus/reentrancy, exception identity and search/history ordering are tested.

Four full UI pairs / 24 scenarios / 21 timed scenarios pass, with **72 scenario images + four live images equal** and no detected allocation regression. Live-frame medians **4.2869 → 4.5179 ms**, paired ratio **1.0286**, interval **0.7986–1.3248**. Compact medians **1.6912 → 1.7878 ms**, ratio **1.0443**, interval **0.8378–1.3017**. Times are medians of process medians; paired log ratios drive intervals. No builds/tests/profilers overlap timing. These broad intervals do not prove equivalence or whole-app performance.

Original app SHA256 `4A266E74A8AE7ACDD4B935DE5AFCE4ECF67FF123F9B662BAF586B580AD2C7FE3`; measured/verified candidate `2DECFA02AABB51030A68F70F54F9E3BB4E1CDB9ACDE8D5F38CD77F1948B6DA8A`. Forced clean pre-test builds and final checks keep the measured app and all 37 production copies per variant consistent; all seven unaffected module bytes match. Full Default **7,777** / Store **7,697** tests pass, all 11 steps plus five explicit read-only installed checks each. Clean builds have zero warnings/errors. Search **9** and compact **106** smoke renders pass per variant, **4 passed / 0 failed** each; all 230 images reviewed in contact sheets and four at full resolution.

Current measured inventory becomes **273 >30 = 172 >50 + 101 >30 and ≤50**. Twelve collateral >50 crossings and one >30/≤50 crossing are numerical caller-coverage results, not independent remediation claims. The fully characterized original-method ≤30 count stays **108**. All 4,587 baseline identities/shapes match except the intentional event-forwarder change, plus two additions; 98 compiler aliases change only owner ordinals (90 by one, eight by two). The exact EndProcess boundary correction remains explicit. One unchanged pipe method's normal-loop-exit coverage declines (78 → 77 of 81 lines, 25 → 23 of 26 branches; CRAP 26.0814); inferred scheduling/exit-path variation is documented rather than hidden by rerunning.

Source-line union is **68.9704% lines / 71.0566% branches**; collector raw aggregate **68.8000% / 70.2857%**. Both branch scopes meet 70%; line coverage remains below 80%. `remaining.csv` now means current measurement; `inventory-context.json` separates it from the preserved last accepted 286/187/99 snapshot. **The inherited theme gauges failure is unresolved, so whole-tree appearance acceptance remains false.** The keyboard gates do not accept the theme candidate. See the [keyboard report](CRAP-REMEDIATION-2026-11-21.md#window-keyboard-characterization) and `artifacts/crap/window-keyboard/` for source/test snapshots, original-flow evidence, raw comparisons, coverage, runtime hashes and limitations.

## Theme application measurements; appearance acceptance blocked (November 22, 2026)

The `MainWindow.ApplyTheme` candidate separates high-contrast resources, surfaces, chrome, selection/content resources, text and accents. Original complexity/CRAP **178 / 31,862** becomes entry **1**, core **22**, and helpers **2 / 26 / 30 / 40 / 48 / 14**. All have 100% lines; branch-bearing methods have 93.3–100% branches. Selection/text remain >30 findings. There are 107 native-default characterization cases passing before any source edit and 120 passing on the observation seam before extraction and afterward. The no-argument entry preserves native defaults, resource-write ordering and late preference reads.

Focused .NET 10.0.15 comparisons use identical probe DLL bytes, alternating isolated WPF processes pinned to the same allowed CPU, 500-ms warm-up and seven batches. Eight pairs × fifteen native-default scenarios compare against the original application; eight × seven supplied-observation scenarios compare against the seam-only original flow. All **176 output fingerprints / 1,232 individual paired allocation batches match**, with no detected 95% latency/allocation regression. Native Dark: **11.267 → 11.866 ms**, paired ratio **1.0204**, 95% interval **0.9569–1.0882**, unchanged **6,568,624 bytes/application** with full application resources. Supplied high-contrast Dark: **2.935 → 2.979 microseconds**, ratio **1.0017**, interval **0.9812–1.0226**, unchanged **232 bytes**. Supplied observations do not verify native DWM composition.

Four repository UI pairs / 24 scenarios / 21 timed scenarios pass the existing gate with **72 scenario pixels + four live pixels equal** and no detected allocation regression. Live-frame medians **4.837 → 4.692 ms**, paired ratio **0.9881**, interval **0.9006–1.0842**. Original app SHA256 `6DAC3AAFC1982F9270741AFF1565B2AA6BCA398FE7F2545C993DAEE2DCE10057`; measured/verified candidate `4A266E74A8AE7ACDD4B935DE5AFCE4ECF67FF123F9B662BAF586B580AD2C7FE3`. These measurements do not establish whole-app, cold-start or cross-Windows equivalence.

Clean pre-test builds and all 11 verification steps pass for Default **7,630** / Store **7,550**, plus five explicit read-only installed checks each; zero compiler warnings/errors. Hash checks before tests and after completion keep all seven unaffected modules unchanged and all app copies consistent. The measured inventory is **286 >30 = 185 >50 + 101 >30 and ≤50**, with 4,587 compiled methods; the canonical accepted inventory remains **286/187/99** pending appearance acceptance.

**The candidate is not fully accepted.** Feature (131) and compact (106) smoke pass per variant. Contact sheets cover all 76 palette and 212 compact images, with four full-resolution checks. Gauges smoke fails before full-size finish/backdrop captures on both variants: `CPU bowl or value is off-center at 1000 DIPs: column 83.3, bowl 84.3, value 84.3.` The frozen original reproduces the identical exception at the same line. No assertion was weakened or unrelated layout changed. Both appearance invocations report **4 passed, 1 failed**; the final validator deliberately retains `FullAppearanceAcceptance: false` and exit 1. Investigate that existing layout/diagnostic discrepancy separately before declaring this batch complete.

See the [theme batch report](CRAP-REMEDIATION-2026-11-21.md#theme-application-batch--measured-appearance-acceptance-blocked) and `artifacts/crap/theme-application/` for raw timings, provenance, complete coverage, smoke failures, original reproduction and visual review. Source-line union coverage is **68.1036% lines / 70.0030% branches**; collector raw aggregate is **67.9430% / 69.2555%** with different generated/duplicate accounting. The overall coverage objectives are not uniformly achieved.

## Metric-details characterization; structural candidate rejected (November 22, 2026)

Added 82 NUnit 4 cases covering six metric tooltips, missing/partial readings, sampling errors, units, drive matching, live/full versus retained data, tooltip ownership, uptime bounds and reentrant notifications. On unchanged production, `MainWindow.UpdateMetricDetails` falls from CRAP **22,436.57 → 156** at unchanged complexity 156, with 100% lines / 91.0% branches. It remains above 50. Existing `Busiest` **110 → 10** and disk formatting **72 → 8** reach 100% lines/branches through tests alone; 108 original methods are now independently characterized at ≤30.

The rejected structural candidate reached dispatcher CRAP 18 plus five private helpers (14/28/38/22/14), all with 100% lines/branches. Eight paired isolated WPF processes / 15 focused scenarios / 120 allocation and output comparisons matched; no focused timing regression was detected. The broader four-pair UI comparison had equal pixels (72 scenario + four live comparisons) and no allocation regression, but pinned Connections measured **3.219 → 3.406 ms**, latency ratio **1.0593**, 95% interval **1.0191–1.1012**. The tooltip is closed before this scenario; the cause is unestablished. The candidate was reverted, its failing evidence retained, and none of its helpers is accepted.

An initial post-restoration full run was invalidated because the copied backup's old timestamp let incremental builds reuse the candidate. Method identity checks detected the mismatch. Corrected verification forces clean app/test/benchmark builds for both variants and proves all eight production modules / 37 copies per variant match baseline before tests and after completion. Valid Default **7,510** / Store **7,430** tests pass, all 11 steps plus five explicit read-only installed checks per variant; clean builds have zero warnings/errors. Every original case remains passing. No new timing or smoke run is needed for the accepted test-only, production-byte-identical change.

All 4,580 method identities/shapes match with no additions/aliases. Inventory is **286 >30 = 187 >50 + 99 >30 and ≤50**. `PeakFor` 110 → 10 and `UpdateResources` 54.55 → 42 are collateral numerical crossings, with no independent behavioral-remediation claim. Overall coverage is **67.7% lines / 69.1% branches**, still below target. Native/initialization-dependent declines and the exact EndProcess threshold correction remain recorded. The [CRAP report](CRAP-REMEDIATION-2026-11-21.md#metric-details-characterization-and-rejected-structural-candidate) and `artifacts/crap/metric-details/` preserve the original/candidate source and binaries, TRX, valid/invalidated coverage, runtime hashes, raw timings, rejection and validation. Profile the pinned Connections difference before revisiting structural work; prioritize other >50 production methods meanwhile. No native-load or cold-start equivalence is claimed.

## Process-action dispatcher separation (November 22, 2026)

`ProcessesView.OnProcessAction` falls from complexity 130 / CRAP **17,030** to complexity/CRAP **50** with 100% lines and 68% reported branches. Six cohesive private operation handlers score 6–12, all with 100% lines. The parent remains above the original ≤30 target and below the 70% secondary branch target; it stays in the inventory. Fifty cases passed before any production seam; the same 72 cases passed on the seam-only and final implementations. Full Default 7,428 / Store 7,348 tests pass, all 11 steps plus five explicit read-only checks per variant; clean builds have zero warnings/errors, and feature smoke produces 131 renders per variant with relevant process-action renders reviewed.

Eight paired isolated WPF processes / seven focused scenarios pass the unchanged paired 95% latency/allocation gate with matching original outputs. Four dispatch paths allocate 80 → 48 bytes, historical copy 4,432 → 4,400, and rejected grouped end remains 80. View construction/cleanup timing 6.173 → 6.568 ms has a wide 0.904–1.233 ratio interval; construction/cleanup allocation-ratio interval 0.9894–1.0026. One optional reference per view is an explicit fixed memory tradeoff (expected eight bytes on x64); the public constructor allocates no dependency carrier. The noisy full-construction measurement does not establish allocation neutrality or tight timing equivalence.

| Scenario | Before → after µs | Latency ratio 95% CI | Allocated bytes/request |
|---|---:|---:|---:|
| captured-navigation | 0.322 → 0.352 | 0.987–1.093 | 80.0 → 48.0 |
| closed-copy | 1.471 → 1.351 | 0.782–1.106 | 80.0 → 48.0 |
| construct | 6173.231 → 6567.766 | 0.904–1.233 | 530800.9 → 527736.5 |
| group-mismatch-end | 1.702 → 1.722 | 0.855–1.128 | 80.0 → 80.0 |
| historical-copy | 4.542 → 4.637 | 0.909–1.072 | 4432.0 → 4400.0 |
| historical-suspend | 0.365 → 0.407 | 0.912–1.167 | 80.0 → 48.0 |
| unavailable-affinity | 0.333 → 0.316 | 0.854–1.062 | 80.0 → 48.0 |

Four full UI process pairs / 24 scenarios / 21 timed scenarios also pass, with 72 scenario pixel comparisons plus four top-level live comparisons equal. Measured and verified app copies match; all seven unaffected modules are byte-identical. All 4,572 baseline methods match through 43 reviewed generated aliases; exactly eight reported methods are added. Inventory: 289 >30 = 191 >50 + 98 >30 and ≤50, with 106 independently characterized original methods ≤30. Exact rational threshold comparison corrects one floating-point boundary artefact: `EndProcess` is exactly 30, while the raw tool flags `30.000000000000004`; raw output and boundary proof are retained. Four collateral crossings are not claimed as independent remediation. Overall coverage remains below target at 67.2% lines / 68.3% branches. The [CRAP report](CRAP-REMEDIATION-2026-11-21.md#process-action-dispatcher-characterization-and-separation-batch) records behavior evidence, collateral coverage and remaining risks. Raw measurements and verification are in `artifacts/crap/process-action-dispatch/`; no real destructive-process action, native-load benchmark, WPF render-thread timing or cold-start guarantee is claimed.

## Connections source/count/status separation (November 22, 2026)

`MainWindow.RefreshConnections` falls from complexity 98 / CRAP 111.1742 to complexity/CRAP **48**. Two cohesive private methods retain endpoint counts (**12**) and traffic status (**34**), with the original count → totals → status → rows order. All three have 100% lines and respectively 97.9%, 100% and 94.1% reported branches. The parent and traffic helper remain above the original ≤30 target while meeting ≤50; the new >30 helper stays in the inventory.

All **29 characterization cases pass on both the frozen original DLL and candidate**; coverage proves original complexity 98 and absence of helpers. Full Default **7,356 passed** / 34 expected exclusions; Store **7,276 passed** / 52 expected exclusions, all 11 steps and five explicit read-only installed checks per variant. Clean app/test builds have zero warnings/errors. Connections smoke passes 23 renders per variant; all 46 were visually reviewed, with selected full-resolution checks. All 4,570 baseline method identities match through 119 reviewed compiler aliases; exactly two private methods are added. All seven unaffected modules are byte-identical; the measured candidate matches every verified Default app copy.

Four alternating process pairs, 24 scenarios and 21 timed scenarios pass the unchanged paired 95% latency/allocation gate. All 72 rendered-scenario and four top-level live pixel comparisons match. Each connection scenario's allocation medians match exactly in every pair; allocation ratio CIs are 1–1. Timing intervals remain wide; there is no statistically detected regression, rather than proven tight latency equivalence. The main-window driver uses 400 warm-up frames and seven batches of 60; separate trend windows already use 100 warm-up selections and five batches of 20, now made explicit in the guide.

| Scenario | Before UI ms | After UI ms | Latency ratio 95% CI | Allocated bytes/frame |
|---|---:|---:|---:|---:|
| `connections-executable-live-frame` | 2.405 | 2.514 | 0.934–1.167 | 510885.9 → 510885.9 |
| `connections-network-executable-live-frame` | 3.414 | 3.319 | 0.818–1.122 | 1285286.5 → 1285286.5 |
| `connections-network-history-executable-frame` | 3.488 | 3.397 | 0.844–1.104 | 1258872.0 → 1258872.0 |
| `connections-network-history-pinned-frame` | 3.494 | 3.204 | 0.799–1.087 | 1261928.0 → 1261928.0 |

The benchmark measures warmed UI-thread refresh/layout/OnRender/channel commit, excluding native collection and the WPF render thread; no new cold-start or sustained native-load evidence is claimed. In particular, the first scenario's 0.934–1.167 timing ratio interval does not rule out a small regression. Raw runs and all proof/verification artifacts remain in `artifacts/crap/connections-refresh/`. Inventory: **293 >30 = 195 >50 + 98 >30 and ≤50**; fully characterized original methods ≤30 remain 106. The increase by one >30 entry is the retained private traffic-status helper; the >50 count falls by two including a collateral caller.

## Capture history coverage tests without production changes (November 22, 2026)

`MainWindow.DescribeHistory` falls from CRAP 126.8204 → 42 at unchanged complexity 42, with 100% lines and 97.6% reported branches. Seventeen NUnit 4 cases exercise independent source bounds, Usage interval starts, selection/gap/failure/retention identity rules, service-memory shortening, tab badges and accessibility names. The method meets ≤50 while remaining above the original ≤30 target. No production change or seam is introduced.

Full Default **7,327 passed** / 34 expected exclusions; Store **7,247 passed** / 52 expected exclusions, all 11 steps and five explicit read-only installed checks per variant. All new identities pass in both builds; clean test-project build has zero warnings/errors. The owning source and eight production modules / 37 copies per variant are byte-identical to the prior builds. No same-binary benchmark or new UI smoke was run; no execution path/allocation changed. This adds no whole-app, native-load or screen-reader performance evidence.

Evidence: `artifacts/crap/history-coverage/`. Inventory: 292 >30 (197 >50; 95 >30 and ≤50). Fully characterized original methods ≤30 remain 106; collateral caller crossings, if any, are reported separately in the remediation report.

## Tray status tests without production changes (November 22, 2026)

`MainWindow.UpdateTrayText` falls from CRAP 108.4688 → 12 at unchanged complexity 12, with 100% line and 83.3% reported branch coverage. Seventeen NUnit 4 cases cover missing trays/readings, error/recovery, culture formatting and live CPU during historical selection through both real presentation callers. Ten other methods cross below 30 through collateral caller execution; their broader contracts and remaining branch gaps are not claimed as characterized. Global hotkey warning states remain untested.

Full Default **7,310 passed** / 34 expected exclusions; Store **7,230 passed** / 52 expected exclusions, all 11 steps and five explicit read-only installed checks per variant. All new cases pass in both builds; clean test-project build has zero warnings/errors. The owning source and all eight production modules / 37 copies per variant are byte-identical to the prior accepted builds. No same-binary benchmark or new UI smoke was run: no production execution path or allocation changed. This adds no whole-app, native-load or physical-hotkey performance evidence.

Evidence: `artifacts/crap/tray-status/`. Inventory: 297 >30 (204 >50; 93 >30 and ≤50). Fully characterized original methods ≤30: 106; collateral numeric gains are reported separately.

## Usage frame source-selection tests without production changes (November 22, 2026)

`MainWindow.CurrentUsageFrame` falls from CRAP 285.5995 → 38 at unchanged complexity 38, with 100% lines and 100.0% reported branch coverage. Seventeen NUnit 4 cases exercise live, retained Capture and archived calendar sources, period bounds/time zones, empty/unavailable data, mode priority and an owned archive-load failure. The original empty-zone notice and clock fallback remain unchanged. The method meets the revised ≤50 maximum and remains above the original ≤30 target.

Full Default **7,293 passed** / 34 expected exclusions; Store **7,213 passed** / 52 expected exclusions. All 11 verification steps and five explicit read-only installed-service/tree/inventory/pipe checks pass per variant. Default coverage includes the explicit checks. All 17 new case identities pass in both full builds. A clean test-project build has zero warnings/errors. The owning source and all eight production modules / 37 copies per variant are byte-identical to the preceding accepted builds. No same-binary benchmark or new UI smoke was run: this tests-only batch adds no production execution path or allocation. No native-load, cold/concurrent persistence or whole-app profiling evidence is added.

Evidence: `artifacts/crap/usage-frame/`. The canonical inventory stays at 308 >30 findings: 214 >50 and 94 with scores >30 and ≤50. Fully ≤30 original compiled methods remain 105. The diagnostic-only icon override remains deferred while production-critical methods are prioritized.

## Process-row keyboard tests without production changes (November 22, 2026)

The `ProcessRow` constructor KeyDown handler (`<.ctor>b__62_1`) falls from CRAP 110 → 10 at unchanged complexity 10, with 100% line and branch coverage. Twenty-nine NUnit 4 cases exercise actual windowless routed events, including reused lifetimes/grouping, unavailable rows, exception identity, reentrant refresh and parent bubbling. No production code or seam changes.

Full Default **7,276 passed** / 34 expected exclusions; Store **7,196 passed** / 52 expected exclusions. All 11 verification steps and five explicit read-only installed-service/tree/inventory/pipe checks pass per variant. Default coverage includes the explicit checks. All 29 original-passing case identities appear in both full builds. A clean test-project build has zero warnings/errors. The owning source and all eight production modules / 37 copies per variant are byte-identical to the preceding accepted build. No same-binary benchmark or new UI smoke was run for this tests-only batch; it adds no production execution path or allocation. This is not new physical-keyboard, screen-reader, native-load or whole-app profiling evidence.

Evidence: `artifacts/crap/process-row-keyboard/`. The canonical inventory has 308 >30 findings: 215 >50 and 93 with scores >30 and ≤50; 105 original compiled methods are now fully remediated to ≤30. Other `ProcessRow` findings remain.

## Process control menu composition (November 22, 2026)

`ProcessRow.PrepareProcessMenu` now delegates suspend/resume, affinity and efficiency entries to one private `AddProcessControlItems` method. Complexity 92 becomes 48 / 44; final CRAP 48.0000 / 44.0000 meets the revised ≤50 maximum while both remain >30 findings. The body, callback timing and existing signatures are preserved.

The first unconditionally called helper was rejected for a measured 40 B/menu increase on unconfigured menus (allocation ratio 95% CI 1.00341274–1.00341288). IL metadata confirms its callback closure allocates before the inner guard. Keeping the first binding check at the caller prevents entering that scope when controls are unconfigured. The rejected source, binary and all raw measurements remain available.

The guarded comparison passes the unchanged gate across all 16 scenarios and eight process pairs: no statistically detected latency or allocation regression. Every allocation median matches its original counterpart; allocation ratio intervals are 1–1. Minimal menus now allocate 11,720.4481 B/menu on both sides. Latency intervals are wide (for example, unavailable rows span 0.8611–1.5586), so these data do not establish tight latency equivalence. All process samples remain available.

The repository real-window benchmark completes four alternating process pairs, covering 24 scenarios (21 timed). No latency/allocation regression is detected by its paired confidence-interval gate; all recorded scenario and top-level live pixel hashes match. Both assemblies and the existing 400-frame warm-up / seven 60-frame batches are verified. Full per-scenario medians, raw batches and intervals are retained under `ui-scenario-summary.json` and the original JSON/PNG files. This benchmark measures UI-thread refresh/layout/render submission and excludes native collection and the WPF render thread.

Full Default **7,247 passed** / 34 expected exclusions; Store **7,167 passed** / 52 expected exclusions. All 11 verification steps and five explicit read-only installed-service/tree/inventory/pipe checks pass per variant. Default coverage includes the explicit checks; smoke runs separately to preserve coverage scope. All 58 original-implementation characterization cases and 110 focused cases pass. Clean app/test builds report zero warnings/errors. Feature smoke and menu PNG review pass in both variants. The measured guarded Default DLL matches the final build exactly; seven unaffected modules remain byte-identical.

Evidence: `artifacts/crap/process-menu/`. The inventory retains 309 >30 findings: 216 >50 and 93 with scores >30 and ≤50; 104 original compiled methods remain fully remediated to ≤30. Synthetic timings do not establish native-control latency, cold/concurrent performance or full production equivalence.

## Process navigation identity tests without production changes (November 22, 2026)

`MainWindow.CanNavigateProcess` is exercised through the existing process-view delegate by 37 deterministic NUnit 4 cases. CRAP falls 2,550 → 50 at unchanged complexity 50, with 100.0% lines and 96.0% branches. It meets the revised ≤50 goal and remains a >30 finding; no production extraction or new seam was introduced.

Full Default **7,189 passed** / 34 expected exclusions; Store **7,109 passed** / 52 expected exclusions. All 11 verification steps and five explicit read-only installed-service/tree/inventory/pipe checks pass per variant. Default coverage includes the explicit checks. A clean non-incremental test-project build has zero warnings/errors. The production source and all eight modules / 37 output copies in each variant are byte-identical to the preceding accepted builds. No same-binary benchmark was run: this test-only batch adds no production execution path or allocation. Prior accepted trend-render benchmarks remain independent evidence; sustained native-load, cold/concurrent UI and whole-app profiling were not added.

Evidence: `artifacts/crap/process-navigation/` contains original passing tests, source/assembly hashes, raw coverage and complete method mapping, full gates, clean build and validation. The latest inventory has **308 above 30 = 217 above 50 + 91 with scores >30 and ≤50**. No finding was removed from the >30 inventory. This batch reduces the >50 count by one. Repository-wide coverage targets remain incomplete.

## Resource trend primary plotting extraction (November 22, 2026)

`ResourceTrendGraph.OnRender` now delegates the original primary-column binning, min/max wash, partial marks and trace to one private `DrawPrimarySeries` method. The primary body moves verbatim; axes, labels, secondary dispatch and selection/focus stay in the override. Complexity 78 becomes 36 / 42; final CRAP is 36.6377 / 42.0276, within the revised ≤50 goal while both remain tracked above the original ≤30 threshold. There is no new field, dependency, callback, I/O or production seam.

68 original-implementation characterization cases and the existing render/keyboard cases pass (112 focused). Forty original/current scenarios match exact pixels, retained geometry/glyphs and selection state, including 24-hour-wide and seven-day data. The initial path-inspection fixture error was corrected against the actual original assembly; failures remain retained. Source reconstruction confirms the moved body and all other source.

The initial isolated timing pass and its same-binary control were unreliable; both remain recorded. Stabilized controls and eight isolated original/current WPF process pairs use fixed affinity, balanced order and GC outside seven timed batches. No latency/allocation regression is detected on ten workloads, with wide confidence intervals explicitly retained. All 21 timed scenarios detect no regression; all recorded scenario and top-level pixel hashes match. Seven-day rendering/selection medians are 3.981 → 3.532 ms (ratio 95% CI 0.7800–0.9988); wide 24-hour medians are 3.605 → 3.291 ms (0.8142–1.0792). Their allocation medians remain 800,379.6 and 1,088,043.6 B/frame, respectively. These are synthetic measurements with four process pairs, not a guarantee of unchanged real-workload performance. Full Default **7,152 passed** / 34 expected exclusions; Store **7,072 passed** / 52 expected exclusions. All 11 verification steps plus five explicit read-only installed-service/tree/inventory/pipe checks pass per variant. All 68 new case identities pass in both full builds. Default coverage includes the explicit checks; smoke runs separately to preserve baseline coverage scope. No new compiler warnings/errors or test regressions were introduced. Capture smoke PNGs are reviewed in both variants. Physical keyboard/screen-reader, cold behavior, native-load concurrency and sustained whole-app performance are not established by these synthetic measurements.

See the [batch report](CRAP-REMEDIATION-2026-11-21.md#resource-trend-primary-plotting-extraction-batch) and `artifacts/crap/trend-render/` for all raw results, failed controls, exact output/source proofs, full gates, smoke captures and limitations.


## Resource trend keyboard tests without production changes (November 22, 2026)

`ResourceTrendGraph.OnKeyDown` CRAP 272→16 through 38 original-implementation characterization cases, at unchanged complexity 16 and 100.0% line / 100.0% branch coverage. Windowless STA routed events pin singleton/multiple/unselected/clamped navigation, ignored/empty states, repeated boundary notifications, no-subscriber state progression, exception identity and assignment order, reentrant data clearing, stale/equal refreshed selection and handled-event routing.

The source and all eight production modules / 37 copies per variant are byte-identical to the prior verified builds, so no same-binary latency/allocation benchmark was run. Default 7,084 / Store 7,004 tests, all 11 verification steps and five explicit installed read-only checks per variant pass; the changed test project rebuilds with zero compiler warnings/errors. Existing render tests run, but physical keyboard/focus, screen-reader, visible UI and whole-app performance were not newly profiled. The revised goal is ≤50; all >30 findings remain tracked. See the [batch report](CRAP-REMEDIATION-2026-11-21.md#resource-trend-keyboard-characterization-batch) and `artifacts/crap/trend-keyboard/`.


## Connections keyboard tests without production changes (November 22, 2026)

`ConnectionsView.OnRowKeyDown` CRAP 156→12 through 14 original-implementation characterization cases, with 100.0% line / 100.0% branch coverage at unchanged complexity 12. Windowless STA routed events cover complete details for known/unknown owners, no subscribers, missing selection/endpoints, button/child origins, non-Enter keys, exception identity/Handled timing, reentrancy and already-handled routing. Owned views release their language subscriptions in teardown.

Production source/XAML and all eight modules / 37 copies per variant are byte-identical to the prior verified builds. No same-binary latency/allocation benchmark was run. Default 7,046 / Store 6,966 tests, all 11 verification steps and five explicit installed read-only checks per variant pass; the changed test project rebuilds with zero compiler warnings/errors. Physical keyboard/focus, visible UI, screen-reader and whole-app performance were not profiled. See the [batch report](CRAP-REMEDIATION-2026-11-21.md#connections-keyboard-characterization-batch) and `artifacts/crap/connections-keyboard/`.


## Connection state-label tests without production changes (November 22, 2026)

CLI `ConnectionsTab.State` CRAP 88.1140→15 and desktop `ConnectionsView.StateText` 81.6667→15 through 51 characterization cases each, at unchanged complexity 15 and 100% line coverage. Branch coverage is 100.0% / 100.0%. Cases cover all named TCP states, null/unknown/out-of-range values and three cultures; CLI assertions exercise public Build and exact full details output, while desktop assertions use the existing internal formatter. No production seam or resource change was introduced.

Both source files and all eight production modules / 37 copies per variant are byte-identical to the prior verified builds, so no same-binary latency/allocation benchmark was run. Default 7,032 / Store 6,952 tests, all 11 verification steps and five explicit installed read-only checks per variant pass; both changed test projects rebuild with zero compiler warnings/errors. Native networking, physical terminal, interactive UI and whole-app performance were not profiled. See the [batch report](CRAP-REMEDIATION-2026-11-21.md#connection-state-label-characterization-batch) and `artifacts/crap/connection-state/`.


## CLI keyboard extraction rejected for latency (November 22, 2026)

`InteractiveState.Handle` retains its original implementation. 316 original/final characterization cases cover 10,000 independently prepared transitions plus explicit editing/history/pause/pin/sorting boundaries. Coverage improves 88.3% line / 67.1% branch → 100% / 98.7%; CRAP falls 315.3832→231 at unchanged complexity 231 and remains above target.

Two equivalent private-handler extractions were rejected. The first measured root CRAP 20 but regressed sort ≈2.42× and ignored keys ≈2.37×. Removing duplicate modal guards and applying small-handler inlining hints left sort 1.1001× (95% CI 1.0231–1.1829) and ignored keys 1.1419× (1.0788–1.2087). Original/current medians are 9.311/10.487 ns and 4.699/5.222 ns per key cycle: warm microtimings, not a claim of user-visible delay. Allocations match on all ten workloads. Each candidate used eight paired processes, seven alternating batches, reversed load/warm order, 500 ms warm-up, tiering disabled and adaptive ~40 ms timing; actual original/current DLLs match all 200×50 transitions outside timing. Raw samples and both failed acceptance results remain available.

The accepted source and all eight production modules / 37 copies per variant are byte-identical to the prior verified builds. All 11 Default/Store verification steps, 6,930/6,850 tests and five explicit read-only installed checks per variant pass with zero compiler warnings/errors. No production helper or inlining hint remains. Native terminal, cold/tiered/concurrent and whole-app performance are unmeasured. See the [batch report](CRAP-REMEDIATION-2026-11-21.md#cli-keyboard-characterization-and-rejected-extraction-batch) and `artifacts/crap/cli-input/`.


## Activity table tests without production changes (November 22, 2026)

`ActivityCommands.Render` CRAP 210→14 through 26 characterization cases, with 100% line and 85.7% branch coverage at unchanged complexity 14. Tests pin literal/Unicode output, ESC/control/format stripping, row/header/note ordering, empty tables, lazy cells/unread details, borrowed writer ownership, original exception identity and all three command paths with injected sources. Process NO_COLOR is restored; real terminal output/ANSI detection remains outside scope and the two missing branches remain reported.

All eight production modules and 37 copies per variant are byte-identical to the prior verified builds, so no same-binary latency benchmark was run. Full Default 6,614 / Store 6,534 tests, all 11 verification steps and five explicit installed read-only checks per variant pass; the changed test project rebuilds with zero compiler warnings/errors. No native terminal/hardware or whole-app profiling claim is made. See the [batch report](CRAP-REMEDIATION-2026-11-21.md#activity-table-rendering-characterization-batch) and `artifacts/crap/activity-render/`.

## CLI view composition (November 22, 2026)

`InteractiveMonitor.BuildView` now orchestrates cohesive header, modal-details, controls, status and warning-policy helpers. Complexity/CRAP 96→26; all seven units have 100% line/branch coverage, with helper complexity 4–20. Original expressions/evaluation order/public state mutations remain unchanged by source reconstruction. There is no new dependency, public API or native operation. The original unconditional heap capture is removed; marker capture becomes stack-based and details capture is acquired only on that branch.

154 characterization cases passed before production edits; 156 expanded cases pass against the actual retained original DLL and current renderer, with all previous 97 baseline fingerprints and ten readable views unchanged when the missing Usage state was added. Full Default 6,588 / Store 6,508 tests, all 11 steps and five explicit installed read-only checks per variant pass; clean CLI/test rebuilds have zero compiler warnings/errors. All 42 measured/full renderer IL/EH/implementation bodies match.

16 construction/rendering workloads × eight paired processes × seven alternating batches use actual original/current DLLs and strongly held contexts. All 98 raw text/ANSI/state outcomes are compared twice outside timing and again after workloads. Warmup is 500 ms/side; bounded adaptive calibration reaches ≥25 ms and targets ~40 ms batches. Setup/delegates/fingerprints stay outside timing, tiered compilation is disabled, and no owned build/test/coverage/profiler overlaps. Every median allocation decreases, generally about 32 B/call and about 8 B for details. No statistically detected latency/allocation regression; the widest 95% time ratio permits 9.68432% more time. Small-view construction's upward estimate is 1.70607% (95% ratio 0.99999–1.03442); it is retained, without favorable reruns or new inlining hints. Zero regression is not established. Cold/tiered execution, native input/resize, physical terminal/font behavior and whole-app profiling remain unmeasured. See the [batch report](CRAP-REMEDIATION-2026-11-21.md#cli-view-composition-batch) and `artifacts/crap/cli-build-view/`.

| Workload | Before µs | After µs | Before B/op | After B/op | Paired time ratio 95% interval |
|---|---:|---:|---:|---:|---|
| connections-Active-Build | 195.893 | 198.807 | 217052.61 | 217020.06 | 0.95610–1.03960 |
| connections-Active-Render | 404.961 | 419.255 | 467430.25 | 467391.52 | 0.99114–1.09014 |
| connections-Unavailable-save-Build | 240.517 | 241.291 | 252668.30 | 252636.44 | 0.96201–1.09684 |
| connections-Unavailable-save-Render | 459.085 | 447.627 | 507058.80 | 507024.87 | 0.92462–1.05027 |
| details-0-3-Build | 59.708 | 59.932 | 75394.21 | 75385.25 | 0.97880–1.03049 |
| details-0-3-Render | 139.028 | 136.934 | 199789.06 | 199781.06 | 0.94155–0.99337 |
| processes-120-unicode-Build | 200.021 | 198.997 | 198822.72 | 198789.94 | 0.89258–1.05218 |
| processes-120-unicode-Render | 418.864 | 407.786 | 450477.54 | 450435.74 | 0.87426–1.03343 |
| processes-80-ascii-Build | 101.295 | 104.100 | 113652.83 | 113619.30 | 0.94058–1.04939 |
| processes-80-ascii-Render | 221.971 | 225.799 | 282796.39 | 282758.81 | 0.96449–1.05973 |
| services-1-False-Build | 178.637 | 175.779 | 172219.36 | 172187.36 | 0.95330–1.04110 |
| services-1-False-Render | 370.453 | 358.095 | 401063.23 | 401031.23 | 0.94308–1.05914 |
| small-help-40-Build | 33.529 | 34.138 | 36840.64 | 36808.64 | 0.99999–1.03442 |
| small-help-40-Render | 100.108 | 101.446 | 139467.74 | 139435.66 | 0.99234–1.05055 |
| usage-unavailable-Build | 152.081 | 152.594 | 152946.87 | 152914.85 | 0.99154–1.04587 |
| usage-unavailable-Render | 309.556 | 308.670 | 362346.78 | 362310.13 | 0.96096–1.04686 |

## Hardware detail tests without production changes (November 22, 2026)

Added 146 shared cases in each GUI/CLI assembly (292 total), covering architecture localization, network-status precedence and speed boundaries, and missing/single/multiple CPU socket details. Five CLI findings above 50 and two related findings are remediated through tests alone: architecture/status 342→18, CPU 138.52→20, Boolean/adapter projection 72→8, related cache 42→6 and GUI CPU 34.81→20. All twelve copies have 100% line and 83.3–100% branch coverage. Generated unreachable switch/lifted-null branches remain in the raw report and are explained by IL, without suppression or production rewriting.

All eight production modules and 37 copies per variant remain byte-identical, so no same-binary latency benchmark was run. Full Default 6,432 / Store 6,352 tests, all 11 verification steps, five explicit installed read-only checks per variant and both clean test-project builds pass, zero compiler warnings/errors. Unusual pre-existing rounding and empty-socket output is retained; no native hardware, cold/concurrent rendering or whole-app performance claim is made. The rejected event-log experiment remains rejected with its source restored. See the [batch report](CRAP-REMEDIATION-2026-11-21.md#hardware-detail-formatting-characterization-batch) and `artifacts/crap/hardware-details/`.

## Hardware label tests without production changes (November 22, 2026)

Added 180 shared label cases in each GUI/CLI assembly (360 total), covering all provider mappings and unknown/null/high values in English/German/Japanese. Five CRAP findings fall below 30 with no refactoring: CLI memory 552→23, CLI bus 506→22, CLI media/health 72→8, GUI bus 60.1→22. All eight maps have 100% line and 87.5–95.7% branch coverage. The raw reports retain one unreachable generated switch edge per method; IL range guards explain it without suppression.

All eight production modules and 37 copies per variant are byte-identical to the previous verified builds, so no same-binary latency benchmark was run. Full Default 6,140 / Store 6,060 tests, all 11 verification steps and five explicit installed read-only checks per variant pass, zero compiler warnings/errors; both clean test-project builds pass. No native hardware or whole-app performance claim is made. The rejected event-log experiment remains rejected with its source restored. See the [batch report](CRAP-REMEDIATION-2026-11-21.md#hardware-label-characterization-batch) and `artifacts/crap/hardware-labels/`.

## Rejected event-log enumeration boundary (November 22, 2026)

A controlled clock/query/record boundary preserved 45 original-body characterization cases and passed 173 focused event-reader tests. Eight workloads × eight paired processes × seven alternating batches found detected latency regressions: the 16-event simulation was about 39% slower (95% ratio approximately 1.32–1.47), with one-event, retry and early-dispose paths also slower. Ordinary allocation fell from 216 to 136 B per enumeration; actual unstarted iterator creation fell from 184 to 104 B. The latency cost rejects the implementation despite allocation savings.

Production source was restored byte-for-byte, and the proposed boundary/fixture survive only in ignored `artifacts/crap/event-log-enumeration/`. Original CRAP 82.5 remains. Sequential native original/proposed reads of a future empty range produced identical complete/empty results without log writes; these are functional checks and do not override the paired simulated regression. No native failure injection, cold/concurrent query or whole-app profile was performed. See the [rejected attempt](CRAP-REMEDIATION-2026-11-21.md#event-log-enumeration-attempt--rejected-on-performance).

## Event message formatting (November 22, 2026)

The original native signature retains publisher opening and passes owned metadata to an internal core. The existing SafeEvtHandle implements the small metadata interface without fields or wrappers; native arguments/signatures and caller-item marshaling remain unchanged. Original loop complexity 8 remains 8 with 100% line/branch coverage; facade 1/CRAP 2 and native implementation 2/CRAP 6 remain outside ordinary coverage. Actual native probes outside coverage verify missing-publisher behavior and identical 80-character message hashes from one existing System event, with no log writes. These probes are functional evidence, not native latency profiling.

Forty-one source-verified original-body/current cases and 128 focused cases pass. Full Desktop 5,780 / Store 5,700 tests and all 11 steps plus five installed read-only checks per variant pass, zero warnings/errors. All 43 measured/full reader IL/implementation/EH bodies match, including both FormatMessage overloads using full-signature keys.

Eight workloads use eight paired processes each, seven batches and alternating/reversed order. Invalid metadata warms 1s/side with 1M operations;ordinary 500 ms/4,096;maximum 65,536-character payload 250 ms/64. Shared typed fixtures, reference projection and actual core use strongly owned contexts. Setup/delegates and 32 ordinary/two maximum complete output/count/size/ownership validations stay outside timing. No owned build/test/coverage/profiler overlaps. Every median allocation matches exactly, including 0/0 invalid metadata. No statistically detected latency/allocation regression; widest 95% bounds permit 23.84517% more time and 0% allocation. Bounds do not prove zero latency regression. Actual native latency, cold/interface initialization, concurrent disposal and whole-app profiling remain unmeasured. Source `C8B634E41DD648B1CEA970ED3CF1D8015E47E00FF9D3C46A0EE1F12DA5E12B03`. See the [batch report](CRAP-REMEDIATION-2026-11-21.md#event-message-formatter-batch) and `artifacts/crap/event-message-format/`.

| Workload | Before µs | After µs | Before B/op | After B/op | Time ratio 95% interval |
|---|---:|---:|---:|---:|---|
| clamped | 0.24199 | 0.25278 | 4144 | 4144 | 0.94014–1.12933 |
| invalid-metadata | 0.00208 | 0.00210 | 0 | 0 | 0.88801–1.10011 |
| maximum | 59.67578 | 57.56953 | 264264 | 264264 | 0.94652–1.02395 |
| native-error | 0.08267 | 0.07977 | 2072 | 2072 | 0.95168–1.23845 |
| ordinary | 0.08735 | 0.08658 | 2152 | 2152 | 0.93202–1.06469 |
| oversize | 0.07167 | 0.07062 | 2072 | 2072 | 0.95864–1.03864 |
| retry | 0.46340 | 0.46499 | 10312 | 10312 | 0.98546–1.02162 |
| zero-retry | 0.08596 | 0.08945 | 2096 | 2096 | 0.88713–1.08122 |

## Hardware disk inventory (November 22, 2026)

On-demand disk inventory retains four queries, cancellation, source fallback, all-list joins, array materialization, row/property-read order and status rules. Projection/strict-unique matching become cohesive private helpers; redundant nullable paths are removed only under the matched-row/list invariant. CRAP 70 →30; new projection 30 and uniqueness 6, all 100% line/branch coverage. No public/internal API or native operation changes. 79 original cases precede editing; expanded 81 actual-original/current cases and 101 focused cases pass. Final Desktop 5,739 / Store 5,659 tests, all 11 steps plus five explicit installed read-only checks per variant; zero warnings/errors.

Eight workloads each use eight paired processes, seven batches and alternating/reversed order. Tiny paths warm 1s/side with 1M operations; ordinary 500 ms/4,096 operations;256/257-row paths 250 ms/eight operations. Initial tiny-path precision was inadequate; all unchanged workloads were repeated with these longer settings and all raw data retained. Shared typed bridge source targets actual original/current private entries in strongly owned isolated contexts; inputs, metadata/delegates and 32 ordinary/two large full section/query/field-order checks per workload/process are outside timing. No owned build/test/coverage/profiler overlaps. A prior DispatchProxy adapter failed binding before measurement and was replaced without a production change.

All outcomes and final median allocations match exactly. No statistically detected latency/allocation regression; widest 95% bounds permit 37.66541% more time and 0% allocation. The wide timing bounds do not prove zero regression. Cross-run warmup allocation differences are not claimed as savings. All 47 measured/full inventory IL/implementation/EH bodies match. Actual WMI/provider latency, hardware, cold startup, concurrent provider changes and whole-app profiling remain unmeasured. Source `A0A177AF7146A20129C577EB96417FC5303A6CC6D30BAA629A26B62D3A344858`. See the [batch report](CRAP-REMEDIATION-2026-11-21.md#hardware-disk-inventory-batch) and `artifacts/crap/hardware-disks/`.

| Workload | Before µs | After µs | Before B/op | After B/op | Time ratio 95% interval |
|---|---:|---:|---:|---:|---|
| duplicate-id | 0.657 | 0.641 | 1576 | 1576 | 0.86738–1.19187 |
| empty | 0.037 | 0.037 | 336 | 336 | 0.95952–1.02172 |
| fallback | 0.626 | 0.624 | 1608 | 1608 | 0.86520–1.31602 |
| many-256 | 4329.675 | 4129.575 | 295432 | 295432 | 0.86279–1.13301 |
| missing-number | 0.133 | 0.131 | 608 | 608 | 0.88311–1.04445 |
| one | 0.633 | 0.635 | 1576 | 1576 | 0.85682–1.37665 |
| truncated-257 | 4227.231 | 4178.050 | 295464 | 295464 | 0.90112–1.10357 |
| unavailable | 0.035 | 0.034 | 312 | 312 | 0.93981–1.06104 |

## Network table acquisition (November 22, 2026)

`ReadTable` keeps its exact body, native callers and pin ownership. Only its existing callback/method/result declarations become internal for typed friend tests. CRAP 57.0370 →28.00, complexity 28 unchanged; 100.0% line /100.0% branch coverage. Forty-three actual private-entry cases passed before edits; expanded 44 actual-original/current cases include a complete 32 MiB copy. Full Desktop 5,658 / Store 5,578 tests and all 11 steps plus five explicit installed read-only checks per variant pass, zero compiler warnings/errors.

Eight workloads use eight paired processes each, seven measured batches with alternating/reversed order. Ordinary paths warm 250 ms/side and use 4,096 operations;4 KiB copy uses 256. Initial probe measurements were imprecise; retained raw data led to targeted remeasurement of both unchanged probes with 1s warmup and 1,000,000 operations/batch. No sample/source selection was used. Cached compiled invocation bridges bind the actual original/private and current/internal methods; contexts remain strongly owned. Setup, delegates, metadata/getters, payloads and 64 complete protocol/result checks per workload/process stay outside timing. No owned build/test/coverage/profiler overlaps.

All outcomes match and every measured median allocation matches exactly. No statistically detected latency/allocation regression; widest 95% bounds permit 14.41676% more time and 0% allocation. Bounds do not prove zero latency regression. All 47 original/measured/full IL/implementation/EH bodies match; production source differs only in three access modifiers. Live OS table churn/errors, cold startup, concurrency and whole-app profiling remain unmeasured. Source `2CF5566044D4A7EFE69143429DBD1C15DBA98FE1B8D93F8B09A57CFCB98B13B5`. See the [batch report](CRAP-REMEDIATION-2026-11-21.md#network-table-acquisition-batch) and `artifacts/crap/network-table-acquisition/`.

| Workload | Before µs | After µs | Before B/op | After B/op | Time ratio 95% interval |
|---|---:|---:|---:|---:|---|
| exhausted | 0.08177 | 0.08154 | 96 | 96 | 0.91625–1.07699 |
| probe-error | 0.00949 | 0.00956 | 32 | 32 | 0.94672–1.03937 |
| probe-zero | 0.00960 | 0.01028 | 32 | 32 | 0.95482–1.14417 |
| read-error | 0.04760 | 0.04701 | 72 | 72 | 0.98048–1.02905 |
| retry-success | 0.09832 | 0.09774 | 136 | 136 | 0.94835–1.05055 |
| short-success | 0.04358 | 0.04348 | 72 | 72 | 0.92844–1.04225 |
| success-full | 0.48477 | 0.43691 | 8272 | 8272 | 0.83090–1.01279 |
| success-small | 0.06157 | 0.06128 | 104 | 104 | 0.91030–1.08046 |

## History event XML parsing (November 22, 2026)

`TryParseXml` now orchestrates private header, severity and lifetime responsibilities without changing security settings, public/internal contracts, native reading/rendering or handle ownership. Complexity/CRAP 79/79.3551 →8/8; helpers 26/11/26, all full line coverage and 87.5% or better branch coverage. Eighty original/current cases and full gates pass: Desktop 5,614 / Store 5,534,all 11 steps plus five installed read-only checks per variant; zero compiler warnings/errors.

Six workloads run in eight paired processes. Each side warms 500 ms,then seven 1,024-operation batches (64 for maximum XML) alternate/reverse order. Inputs,delegate preparation and 64 complete-record outcome comparisons per workload/process are outside timing. The reference is exact original source with only class name changed. Current-thread precise allocations measure this synchronous path; no owned build/test/coverage/profiler overlaps. No statistically detected final latency/allocation regression; widest 95% bounds allow 16.26476% additional time and 0.0086637% allocation. These are uncertainty limits, not proof of zero regression. Tiny allocation variation is not claimed as a guaranteed saving.

The initial tuple-return design failed the duplicate-field timing comparison (ratio 1.0982,95% 1.00168–1.20406). It was replaced with direct output fields and a single System guard. Final measurement increases warmup/batch size; initial raw data remains retained,without attributing the exact slowdown solely to tuple copying. Native enumeration/rendering, cold initialization, concurrency, live incident collection and whole-app performance were not measured. All 41 measured/full event-reader IL/implementation/EH bodies match. Source `E25972C32A9706085A402F35EA998EC458A64EFCF8F4F492A3F5E9792AE1B9D6`. See the [batch report](CRAP-REMEDIATION-2026-11-21.md#history-event-xml-batch) and `artifacts/crap/history-event-xml/`.

| Workload | Before µs | After µs | Before B/op | After B/op | Time ratio 95% interval |
|---|---:|---:|---:|---:|---|
| duplicate | 2.893 | 2.920 | 13432.50 | 13360.00 | 0.81477–1.05906 |
| invalid-header | 1.018 | 1.199 | 11549.53 | 11549.53 | 0.84768–1.15720 |
| lifetime | 2.798 | 2.842 | 13239.60 | 13168.00 | 0.91239–1.04831 |
| maximum-xml | 105.744 | 104.307 | 432894.75 | 432816.00 | 0.82382–1.10759 |
| missing-system | 0.654 | 0.655 | 10536.97 | 10536.93 | 0.86699–1.16265 |
| ordinary | 7.899 | 8.323 | 12615.53 | 12616.06 | 0.87290–1.10037 |

## Network setup reply characterization (November 22, 2026)

Test-only batch: the actual GUI/CLI reply validators receive132 shared NUnit cases each. CLI CRAP210 →14; both validators reach100% line/branch coverage. Literal failure-phase predicates remain58 despite100% lines/87.9% branches because provider complexity counts generated string dispatch. No collection allocation, production seam or metric-only split is introduced.

All eight production modules,including their20 output/dependency copies, match pre-batch SHA-256 values byte for byte. Production source and runtime implementation are unchanged; no redundant same-binary latency/allocation comparison was run. This is binary identity evidence, not live native setup or whole-app profiling. Desktop5,534 /Store5,454 tests and all11steps plus five explicit installed read-only cases per variant pass with zero warnings/errors. Native/elevated setup operations were not exercised by the new tests. See the [batch report](CRAP-REMEDIATION-2026-11-21.md#network-setup-reply-batch) and `artifacts/crap/network-setup-replies/production-binary-proof.json`.

## Network service startup (November 22, 2026)

`EnsureRunningAsync` keeps its calling signature and original service rights/trust/start/error/cancellation/polling rules. The existing native SafeHandle implements a small owned startup scope; no provider wrapper or fields are added to it. Initial failures retain returned-task semantics using AsyncTaskMethodBuilder.SetException; the waiting core owns disposal. Before/reference/after async state is56 bytes with six fields, avoiding retained dependency parameters. Core CRAP 54.67 →14,100% line/92.9% branch; the full class is below 30.

Nine workloads use 72 paired processes, 32 warmups per side and seven 64-operation batches. Setup, delegate/reflection preparation, full-fingerprint validation and cleanup are outside timing; no build/test/coverage/profiler overlaps. Precise process-wide allocations include asynchronous cancellation completion. Task/error/token/order/lifetime/timer outcomes match. Median allocations match in every workload, including 0B running/immediate, 400B delayed and 1,184B cancellation-during-delay. No statistically detected latency/allocation regression; widest paired 95% bounds permit 35.04552% more time and0.22325% allocation. These intervals do not prove zero regression.

The original-body reference preserves the one-parameter async signature with its fixture factory configured outside timing in an isolated context; injecting the factory as an async argument would inflate the baseline. Both use shared frozen service/timer fixtures. Real SCM start/stop/configuration changes, physical scheduler/deadline behavior, cold native initialization and whole-app performance remain unmeasured. Forty current/original-expanded cases and all required gates pass: Desktop 5,270 / Store 5,190 tests, all 11 steps and five explicit installed-system checks per variant; zero warnings/errors. All 18 measured/full identity method bodies and exception regions match. Source `94391F482ECF0B2168CB65FBE0B689457406638CF6DFC0544407C55BD03D7A29`. See the [batch report](CRAP-REMEDIATION-2026-11-21.md#network-service-startup-batch) and `artifacts/crap/service-startup/`.

| Scenario | Before µs | After µs | Before B/op | After B/op | Time ratio 95% interval |
|---|---:|---:|---:|---:|---|
| canceled | 2.548 | 2.482 | 600 | 600 | 0.91207–1.01218 |
| open-failure | 2.563 | 2.413 | 592 | 592 | 0.75836–1.04836 |
| untrusted | 2.153 | 2.182 | 552 | 552 | 0.87085–1.17254 |
| running | 0.061 | 0.057 | 0 | 0 | 0.82464–1.14258 |
| start-error | 2.461 | 2.703 | 560 | 560 | 0.89927–1.22362 |
| immediate | 0.089 | 0.095 | 0 | 0 | 0.95619–1.26328 |
| delayed | 0.583 | 0.516 | 400 | 400 | 0.82273–1.01986 |
| timeout | 3.456 | 3.276 | 880 | 880 | 0.80181–1.14787 |
| cancel-delay | 89.667 | 94.666 | 1184 | 1184 | 0.86773–1.35046 |

## Restart identity (November 22, 2026)

`AcceptResultArguments` keeps its existing signature and forwards its original two fields by reference with null dependency selectors. The same two references retain token-before-directory assignment after healthy-handle disposal. Original policy/argument guards remain in the adapter to avoid ordinary startup dispatch; no production provider/state object or cached default delegate is added. Default adapter CRAP 16.00 (50% lines; valid restart forwarding/real root/native event unexecuted); actual controlled core 20.07 with 94.4% line/75% branch coverage; pure identity functions 1/4 with full line/branch coverage. Source SHA-256: `C116BFD36710FDA1ED3F312F834F9768B1C7DF7F3E994A544409441331504B90`.

Five core I/O scenarios use 40 paired processes, 32 warmup operations per side and seven sixteen-operation batches: token mismatch,missing event,success,retained prior identity,null-token transition. Eight direct-entry processes use 250 ms warmup per side and seven 500,000-call batches for empty arguments. Initial extra-dispatch overhead was confirmed and corrected by retaining original startup guards; initial results remain retained. Final actual entry costs median 1.593 →1.465 ns (ratio 0.75751,95% interval 0.64616–0.88806), with no detected regression and zero allocations. A first-use check also rejected static value-state storage (40 →64 bytes); original static references restore 40/40 bytes on first read and 0/0 thereafter. All I/O and entry measurements were repeated for the final storage implementation. The source-verified original-body projection uses fresh instance storage prepared outside timing; current bridge calls the actual core with fresh reference fields. Setup, typed delegate preparation, snapshots and cleanup are outside timing; no concurrent build/test/coverage/profiler. Every operation verifies identity/history, event names/counts,handle disposal, and unchanged seeded file set/bytes. Only validated owned history directory identities normalize to roles. No statistically detected latency/allocation regression; widest paired 95% bounds permit 24.81280% more time and 0.00000% allocation. Zero/zero allocation cases are checked explicitly; intervals are not proof of zero regression.

The first full test run exposed WPF resource contamination from additional assembly contexts in the identity tests. That failure is retained; tests now own primitive fields directly. Desktop 5,230 / Store 5,150 tests and all required gates pass, including all 40 new cases; measured/full-build IL/EH for seven affected methods match. Actual updater storage/native events,cold startup,concurrency/contention and whole-app performance remain unmeasured. See the [batch report](CRAP-REMEDIATION-2026-11-21.md#restart-identity-batch) and `artifacts/crap/update-restart-identity/`.

| Scenario | Before µs | After µs | Before B/op | After B/op | Time ratio 95% interval |
|---|---:|---:|---:|---:|---|
| token-mismatch | 467.819 | 476.750 | 1416 | 1416 | 0.91693–1.24813 |
| missing-event | 448.578 | 457.291 | 1616 | 1616 | 0.87257–1.20209 |
| success | 459.122 | 493.650 | 1616 | 1616 | 0.80618–1.17964 |
| retained | 612.853 | 461.719 | 1416 | 1416 | 0.77864–1.02032 |
| null-token | 473.216 | 462.466 | 1464 | 1464 | 0.89357–1.08803 |
| invalid-args-entry | 0.002 | 0.001 | 0 | 0 | 0.64616–0.88806 |

## Update notice lifecycle (November 22, 2026)

The unchanged `ConsumeNotice()` API forwards null/null to an internal controlled core. Null selects original static operations at their original positions; no default delegate caches, provider objects or global overrides are added. Wrapper complexity/CRAP is 1/2 (real-root path unexecuted), core 22/22 with full line coverage. The minimum seam enables owned-filesystem notice characterization; it does not establish runtime coverage of the real updater root. Source SHA-256: `6A3C32BC79E2C5E69D745ACA866F1BF623D61F65B5E994DA46AA305098003A7B`.

The first 48-process comparison was confounded by missing reference PDB versus production embedded symbols: write-failure tracing measured 10,776 →12,312 bytes because only production rendered source frames. Samples and diagnostics are retained. Without altering production/logging, the frozen original-body reference was rebuilt with matching Release optimization, embedded symbols, method identity and source-frame mapping. Inverse source/helper checks preserve original logic; traces match except the validated owned marker path.

Corrected measurements use 48 paired processes/six scenarios, 32 warmup operations per side and seven sixteen-operation batches. Preparation/snapshot validation/cleanup are outside timing, no build/test/coverage runs concurrently, and side order alternates/reverses. All text/file outcomes match; no statistically detected final latency/allocation increase. Widest paired 95% intervals permit 31.12846% additional time and 0.36675% allocation. Empty enumeration allocates zero on both sides, validated explicitly rather than dividing by zero. The real-root adapter, cold startup, actual user-profile/storage failures and whole-app performance remain unmeasured. Null/null wrapper IL and both full-build overloads match the measured code. See the [batch report](CRAP-REMEDIATION-2026-11-21.md#update-notice-lifecycle-batch) and `artifacts/crap/update-notices/`.

| Scenario | Before µs | After µs | Before B/op | After B/op | Time ratio 95% interval |
|---|---:|---:|---:|---:|---|
| empty | 1.67 | 1.71 | 0 | 0 | 0.88727–1.17590 |
| pending | 310.62 | 359.48 | 2096 | 2096 | 0.77789–1.31128 |
| shown | 272.84 | 267.31 | 1664 | 1664 | 0.82456–1.16821 |
| success | 876.93 | 968.81 | 2544 | 2544 | 0.88866–1.19340 |
| foreign-first | 1219.58 | 1181.29 | 3488 | 3488 | 0.79355–1.08886 |
| write-failure | 2147.32 | 2131.44 | 12392 | 12312 | 0.86797–1.19144 |

## Update helper orchestration (November 22, 2026)

`RunHelper` complexity falls from 88 to 24 by separating download trust, parent handoff, installer execution, child readiness and failure recovery. The entry point retains validity flags, parent/mutex/signal/child scopes, portable replacement and success-journal retry handling. All five new private helpers remain CRAP ≤30. Source SHA-256: `3FE6AAFFB72D12A2BFA262B32F9DB249ADC441961BB8F4F04103934129510A05`.

Both sides use the same frozen signed payload and existing fake process host; no process/installer is launched. The corrected driver holds collectible contexts strongly, requires intended return codes/states and rejects helper-error logs. Earlier context-unload failure-only samples are retained as invalid diagnostics and excluded. Final measurements use 56 paired processes/seven scenarios, 32 warmup operations per side and seven four-operation batches. Preparation/signing/ZIP creation, outcome validation and cleanup are outside timing; no build/tests/coverage run concurrently. Precise process-total allocation counts include worker-thread allocations from existing async file work and the same frozen test-host overhead on both sides.

All accepted argument/state/process/file snapshots match. No statistically detected final latency/allocation increase; the widest paired 95% interval permits 21.36196% additional time and 5.29196% allocation. Actual installer/native-process timing, cold startup, readiness-timeout/error-log performance, storage failure and whole-app profiling remain unmeasured. Measured/full-build IL and exception regions match for all six targets; full Default and Store verification pass. See the [batch report](CRAP-REMEDIATION-2026-11-21.md#update-helper-orchestration-batch) and `artifacts/crap/update-helper/`.

| Scenario | Before µs | After µs | Before B/op | After B/op | Time ratio 95% interval |
|---|---:|---:|---:|---:|---|
| portable | 109830.65 | 107705.69 | 63841.00 | 63991.00 | 0.93574–1.02762 |
| installed | 58005.44 | 57600.91 | 29730.00 | 29709.00 | 0.96708–1.04149 |
| retry | 108649.81 | 109564.31 | 66992.00 | 66954.00 | 0.98549–1.04889 |
| exhausted | 103328.61 | 103829.66 | 65750.00 | 65698.00 | 0.97522–1.02681 |
| cancel | 2925.59 | 2707.55 | 17990.00 | 17977.00 | 0.79251–1.21362 |
| slow-installer | 56036.29 | 57000.39 | 31369.00 | 31369.00 | 0.93107–1.05557 |
| late-cancel | 3004.86 | 3039.21 | 19620.00 | 19625.00 | 0.88931–1.06952 |

## Interrupted updater transaction recovery (November 22, 2026)

`UpdateRuntime.CompleteInterruptedTransaction` separates foreign-copy cleanup, legacy companion recovery, healthy-version finalization and completed-file cleanup. Entry-point complexity falls from 84 to 20; all four private helpers are measured and remain CRAP ≤30. Only the interruption-recovery region changes; `RunHelper` remains unchanged. Source SHA-256: `CE5AECC2B4E540BAA3DACD28EA2186F33821C69D435DC10A9F45D5EC5A8D5234`.

The first candidate allocated 32 additional bytes per healthy recovery (8,736 →8,768), so it was rejected. Status text now crosses the private boundaries instead of transient state records; the diagnostic probe returns to 8,736 on both sides. The initial DLL, 56 paired processes and raw summary remain available. Final controlled-filesystem measurements use 64 paired processes/eight scenarios, 32 warmup transactions per side and seven sixteen-operation batches. Preparation, snapshot validation and cleanup are outside timing, with no concurrent test/build/coverage work. Side order alternates and reverses between process pairs. Snapshot outcomes match for every operation, including legacy rollback and preservation of missing-target backups.

No statistically detected final time/allocation increase; the widest paired 95% interval permits 30.71219% extra time and 0.00000% allocation. These measurements cover owned temporary transactions with existing fixtures, not actual installer execution, real power loss, physical-storage failure, cold JIT or whole-app profiling. Final/full-build IL identity is checked. Full Default and Store verification pass; see the [batch report](CRAP-REMEDIATION-2026-11-21.md#interrupted-updater-transaction-recovery-batch) and `artifacts/crap/update-runtime/` for tests, source scope, raw samples, limits and results.

| Scenario | Before µs | After µs | Before B/op | After B/op | Time ratio 95% interval |
|---|---:|---:|---:|---:|---|
| healthy | 5577.12 | 5512.06 | 8736 | 8736 | 0.89131–1.11204 |
| failed | 3103.95 | 3389.78 | 7520 | 7520 | 0.93458–1.18144 |
| missing-target | 2898.21 | 2804.66 | 6672 | 6672 | 0.82158–1.14318 |
| noticed | 4720.88 | 5027.35 | 8008 | 8008 | 0.95850–1.10596 |
| foreign | 3597.62 | 3807.27 | 5304 | 5304 | 0.88939–1.14295 |
| active | 356.67 | 409.74 | 1240 | 1240 | 0.84627–1.30712 |
| unapplied | 2833.25 | 2818.49 | 3768 | 3768 | 0.86775–1.08011 |
| legacy | 10598.06 | 10358.75 | 16160 | 16160 | 0.85534–1.06209 |

## Tool installation and command lifecycle coverage (November 22, 2026)

Testing-only remediation lowers all three `ToolUpdateCommand.IsInstalled` copies to CRAP 10 with unchanged complexity and full 7/7 line, 10/10 branch coverage. Shared public-command cases also lower four GUI helper copies below 30: check, GET, run and download. Forty-three cases per GUI/CLI/MCP assembly use unique owned HKCU identities, validated owned staging roots, fake HTTP, signed in-memory metadata and inert payloads. The launcher only records arguments. Store asserts policy rejection before these operations; no feature policy is bypassed.

No production source, algorithm, dependency, key policy or API changes. Before/after entire shipping assemblies are byte-identical:

- WinMon: `66D019165917B140C0C406DE770F3CEEAC02D6F05A491BCCC3566F95652B4A63`
- WinMon.Cli: `64B54324636660DF306C5C8B5C28FE59DC5B1A3E4C46EC2255C57C7EBF24999A`
- WinMon.Mcp: `5E64C85CE294AF0080C0F5C81846FFA632A86238EB51DDE8F787C77041CF6A99`

The source remains SHA-256 `7666BACE8A7848B6A145E38DB5524C130A95C61995C66CCC3E4181CE88095E64`. Performance evidence is unchanged code; no timing result or whole-app profile is claimed. A diagnosed fake-queue teardown race was corrected in four query fixtures by disposing harness-owned queues only after sampling joins. Assertions and production binaries remain unchanged; the original failed full run is retained. No real installer or payload process, external update request, or actual product-registration mutation occurred. Evidence is under `artifacts/crap/tool-installation/`; see the [batch report](CRAP-REMEDIATION-2026-11-21.md#tool-installation-detection-and-command-lifecycle-batch).

## Signed-manifest verification coverage (November 22, 2026)

The CLI/MCP `UpdateManifest.Verify` copies improve from CRAP 33.42 to 26 through 68 shared characterization cases per product, with unchanged complexity 26. Both reach 18/18 lines and 26/26 branches; both asset validators reach 4/4 lines and 12/12 branches. No production code, algorithm, signature/key policy, dependency or runtime operation changes.

Performance verification compares entire shipping assemblies from the same Default build layout. CLI remains byte-identical at SHA-256 `64B54324636660DF306C5C8B5C28FE59DC5B1A3E4C46EC2255C57C7EBF24999A`; MCP at `5E64C85CE294AF0080C0F5C81846FFA632A86238EB51DDE8F787C77041CF6A99`. The shared source remains `CBA65F2FDFCE203190D93FFAE1D66C86FFB429F8F734EA9B5D363DAA96077BE7`. This establishes no introduced code-level performance change; no before/after timing result or whole-app profiling is claimed. Fresh ephemeral RSA keys and signed JSON are confined to tests in memory; production signing resources are untouched.

Characterization confirms an existing accepted 65-character hash consisting of 64 hex characters plus LF. The unchanged downstream checksum comparison would reject that value; this is recorded as a separate behavior issue, not silently fixed. Evidence is under `artifacts/crap/update-manifest/`; see the [batch report](CRAP-REMEDIATION-2026-11-21.md#signed-manifest-verification-batch).

## MCP process-list validation (November 22, 2026)

`DiagnosticTools.ListProcesses` delegates its unchanged sort/filter/limit predicate to a static local validator, retaining the existing `AggressiveInlining` pattern. Invariant lowercasing, enum parsing, acquisition/current checks, latest-frame ranking, public search and bounded projection remain in their original order/scopes. Reconstructing the predicate reproduces the original source after removing the helper/hint/comment. JIT disassembly shows no validator call; the state machine is 3,069 native bytes original and 2,939 final in the measured runtime.

Seven workloads each use eight paired processes, half reversing load/warm/starting order, with frozen original/final Release assemblies and the same fixture in separate load contexts. Tiered compilation is disabled; 100 calls warm each side, calibration reaches at least 20 ms, and seven alternating batches target 400 ms capped at one million calls. This yields 56 samples per side per workload. All 56 complete response fingerprints match without normalization. A pinned lease and blocked source hold prepared publications fixed; disposable system deadline timers use logical clock readings. Calls include validation, acquire/release, current/freshness checks, ranking, public search, projection and full protocol serialization. Setup, fingerprinting and joined disposal are outside timing. No build, test, coverage-analysis or profiler job ran during timing.

The frozen benchmark fixture/source preserves its representative values. After timing, unit-test metric assignments were strengthened so all six sort modes have different active-row orders; those cases pass against both assemblies. The timed production DLL remains byte-identical to the final focused DLL, and relevant IL/implementation flags match the full Desktop build. Changing test expectations did not replace measured production code or select favorable timing samples.

| Controlled request | Original median | Candidate median | Paired time ratio (95% interval) | Managed bytes/request, original → candidate |
|---|---:|---:|---|---:|
| Invalid request | 3.85160 µs | 3.87202 µs | 0.99153 (0.96006–1.02403) | 2984.00040 → 2984.00040 |
| Five rows, CPU sort | 34.55463 µs | 35.02553 µs | 0.99761 (0.98259–1.01287) | 24632.52744 → 24632.52725 |
| Five rows, handles sort | 33.82126 µs | 35.28623 µs | 1.02141 (0.99065–1.05312) | 24632.52298 → 24632.52429 |
| Five rows, uppercase CPU sort | 35.01065 µs | 34.94195 µs | 0.99689 (0.95402–1.04169) | 24664.52658 → 24664.52747 |
| Public application filter | 12.15758 µs | 12.04184 µs | 1.00003 (0.97097–1.02995) | 10344.20894 → 10344.20894 |
| 201 rows, limit 200 | 1204.59182 µs | 1197.67212 µs | 0.98981 (0.96474–1.01553) | 789923.78313 → 789913.80117 |
| Stale publication, withheld data | 4.37635 µs | 4.41589 µs | 0.99990 (0.96404–1.03709) | 3536.04292 → 3536.04204 |

No statistically detected latency or managed allocation increase was observed. The widest time interval permits approximately 5.31168% increase; the widest allocation interval permits approximately 0.00074% increase. Prepared requests complete synchronously, so allocations use the calling-thread counter; exact equality is not claimed. Real process discovery, active publication contention, cold/tiered JIT, other runtimes/architectures and whole-app profiling remain unmeasured. Raw evidence, frozen fixture/source/assemblies and source/JIT/body proof remain under `artifacts/crap/mcp-processes/`; see the [batch report](CRAP-REMEDIATION-2026-11-21.md#mcp-process-list-batch).

## MCP connection-query validation (November 22, 2026)

`DiagnosticTools.GetNetworkConnections` delegates its unchanged limit/optional-lifetime checks to a static local validator, with the existing repository pattern of `AggressiveInlining`. All current-frame, identity, family, filtering, endpoint-disclosure, projection and lease scopes remain unchanged. Inlining the source predicate reconstructs the original file after removal of the hint/import/comment. JIT disassembly confirms no validator call; the measured state-machine native code is 3,151 bytes original and 3,224 final.

Seven workloads each use eight paired processes, half reversing load/warm/starting order, with frozen original/final Release assemblies and the same fixture in separate load contexts. Tiered compilation is disabled; 100 calls warm each side, calibration reaches at least 20 ms, and seven alternating batches target 400 ms capped at one million calls. This yields 56 samples per side per workload. All 56 complete protocol response fingerprints match without normalization. Actual owner publications prepare synthetic IPv4/IPv6, TCP/UDP and lifetime data outside timing. A pinned lease and blocked source keep publications fixed; real disposable deadline timers use logical clock readings. Timed calls include validation, acquire/release, freshness/identity, filtering, projection and full protocol serialization. Binding, preparation, fingerprinting and joined cleanup are outside timing. No build, test, coverage-analysis or profiler job ran during timing.

| Controlled request | Original median | Candidate median | Paired time ratio (95% interval) | Managed bytes/request, original → candidate |
|---|---:|---:|---|---:|
| Invalid request | 3.79297 µs | 3.87363 µs | 1.00001 (0.95807–1.04380) | 3072.00049 → 3072.00049 |
| Four rows, endpoints hidden | 20.24715 µs | 20.06779 µs | 0.98633 (0.95544–1.01821) | 13416.34728 → 13416.34963 |
| Four rows, endpoints included | 20.93790 µs | 20.94995 µs | 1.00682 (0.98344–1.03074) | 13688.35821 → 13688.35967 |
| Exact Windows lifetime pin | 11.16921 µs | 10.65911 µs | 0.98123 (0.94025–1.02398) | 8232.28975 → 8232.28975 |
| 201 rows, limit 200 | 634.73104 µs | 637.77512 µs | 1.00416 (0.98305–1.02571) | 379843.84629 → 379829.51470 |
| Reused-token mismatch | 4.47961 µs | 4.42900 µs | 0.99794 (0.96740–1.02943) | 3720.00046 → 3720.00046 |
| Both families unavailable, retained rows | 20.25816 µs | 20.25503 µs | 0.99888 (0.96961–1.02903) | 13464.35678 → 13464.34693 |

No statistically detected latency or managed allocation increase was observed. The widest time interval permits approximately 4.37959% increase; the widest allocation interval permits approximately 0.00260% increase. Prepared requests complete synchronously, so allocation uses the calling-thread counter; exact equality is not claimed. Native socket-table/SCM discovery, actual endpoint disclosure, active publication contention, cold/tiered JIT, other runtimes/architectures and whole-app profiling remain unmeasured. Raw samples, frozen fixture/assemblies and source/JIT/body proof remain under `artifacts/crap/mcp-connections/`; see the [batch report](CRAP-REMEDIATION-2026-11-21.md#mcp-connection-query-batch).

## MCP service-query validation (November 22, 2026)

`DiagnosticTools.ListServices` delegates its unchanged sort/filter/limit checks to a static local validator. All sampling, ranking, filtering, projection and async lease operations remain in their original scopes. Reconstructing the predicate reproduces the original source, apart from the explicit inlining hint/import.

The first unhinted candidate showed a 2.59% three-group slowdown (95% interval 0.27%–4.95%). Disassembly showed an added call to the 236-byte validator. Applying the repository's existing `AggressiveInlining` pattern removes that call; state-machine native code is 3,971 bytes original and 3,940 final. The rejected candidate, its samples and successful Desktop gate remain under `first-candidate/`. Initial invalid samples overlapped a coverage-summary calculation; all eight pairs were repeated in isolation and the initial samples retained. Final measurements repeat every workload for the hinted candidate.

Seven workloads each use eight paired processes, half reversing load/warm/starting order. Separate load contexts load frozen original/final Release assemblies with the same fixture. Tiered compilation is disabled, 100 calls warm each side, calibration reaches at least 20 ms and seven alternating batches target 400 ms, capped at one million calls. This yields 56 samples per side per workload. All 56 complete protocol response fingerprints match without normalization. Prepared publications and a pinned lease hold input stable; real disposable deadline timers use fixed logical clock readings. Timing includes validation, acquire/release, current/history reads, ranking, filtering, projection and protocol serialization. Preparation, fingerprinting and joined disposal are outside timing. No build, test, coverage analysis or profiler job ran during the final measurements.

| Controlled request | Original median | Candidate median | Paired time ratio (95% interval) | Managed bytes/request, original → candidate |
|---|---:|---:|---|---:|
| Invalid request | 3.89509 µs | 3.80765 µs | 0.99216 (0.95309–1.03282) | 3064.00043 → 3064.00043 |
| Three groups, worker metrics | 27.42648 µs | 27.51009 µs | 1.02417 (0.99324–1.05607) | 18432.16208 → 18432.16162 |
| Five service publications | 28.61164 µs | 28.55656 µs | 0.99080 (0.96738–1.01479) | 18912.16327 → 18912.16327 |
| Three groups, host metrics | 27.54662 µs | 26.43084 µs | 0.98791 (0.95631–1.02056) | 18440.15906 → 18440.15906 |
| 201 groups, limit 200 | 1277.46714 µs | 1307.34221 µs | 1.00931 (0.98825–1.03081) | 788256.81231 → 788253.43084 |
| 201 services/processes in one group | 475.18812 µs | 475.97658 µs | 0.99415 (0.96478–1.02442) | 344635.04262 → 344635.05266 |
| Current services unavailable | 4.65182 µs | 4.72456 µs | 1.01242 (0.98594–1.03961) | 4136.00047 → 4136.00047 |

No statistically detected final latency or managed allocation increase was observed. The widest time interval permits approximately 5.60690% increase; the widest allocation interval permits approximately 0.00281% increase. Prepared requests complete synchronously, so allocation uses the calling-thread counter; exact equality is not claimed. Actual SCM discovery, physical cadence, concurrent publication, cold/tiered JIT, other runtimes/architectures and whole-app profiling remain unmeasured. Raw evidence, frozen fixture/assemblies and source/JIT/body proof remain under `artifacts/crap/mcp-services/`; see the [batch report](CRAP-REMEDIATION-2026-11-21.md#mcp-service-query-batch).

## MCP process-traffic query validation (November 22, 2026)

`DiagnosticTools.GetProcessNetworkTraffic` delegates its unchanged request predicate to a static local validator. Validation remains before cancellation/acquisition; subscription, nested/current-frame requests, freshness, exact token/domain matching, quality/rate masking, sorting and cached/full-frame metadata lookup remain in the original scopes. Inlining the predicate reconstructs the original source exactly.

Six workloads each use eight paired processes, half reversing load/warm/starting order, with original/candidate Release assemblies in separate load contexts. The reused harness disables tiered compilation, warms 100 calls per side, calibrates to at least 20 ms and targets 400 ms batches capped at one million calls. Seven alternating batches yield 56 samples per side. The client creates a random session GUID: fingerprinting first asserts that the response GUID equals that client's actual nonempty session and appears exactly twice in the protocol representations, then replaces only that GUID. All 48 **normalized** complete response fingerprints match; raw GUID equality is not claimed. All other fields are compared.

Owned fake connections and actual owner/session publication prepare the input outside timing, then block without publishing. A pinned lease keeps that prepared state stable. Logical clock readings are fixed; repeated-query deadline timers use real disposable system timers, avoiding retained manual timer history. Synthetic snapshot frequency isolates query-policy tests from physical delivery jitter; physical service clock policy is outside this measurement. Timed calls include request validation, acquire/release, enabled-client/current-frame checks, filtering, ranking, metadata lookup and full protocol serialization. Binding, setup/publication, fingerprinting and joined disposal are outside timing. No build, test, coverage-analysis or profiler job ran during timing.

| Controlled request | Original median | Candidate median | Paired time ratio (95% interval) | Managed bytes/request, original → candidate |
|---|---:|---:|---|---:|
| Invalid request, no collector/subscription | 3.80917 µs | 3.85270 µs | 0.99688 (0.96338–1.03155) | 2,840.00040 → 2,840.00040 |
| Warming client without snapshot | 6.49723 µs | 6.56323 µs | 0.99896 (0.96012–1.03937) | 5,928.08661 → 5,928.08796 |
| Four current rows | 18.70059 µs | 18.94396 µs | 1.01169 (0.98546–1.03862) | 13,704.19489 → 13,704.20649 |
| 201 rows, limit 200 | 614.95538 µs | 609.98420 µs | 0.99449 (0.97075–1.01882) | 351,526.26437 → 351,526.26437 |
| Exact identity mismatch | 4.75500 µs | 4.66303 µs | 0.98866 (0.96647–1.01136) | 3,984.00047 → 3,984.00047 |
| Unavailable client, retained totals | 17.89105 µs | 18.05740 µs | 0.98652 (0.93915–1.03628) | 13,848.21085 → 13,848.20864 |

No statistically detected latency or managed allocation increase was observed. These prepared requests complete synchronously, so allocations use the calling-thread counter. Exact allocation equality is not claimed. All paired allocation intervals include 1; their widest upper bound permits approximately 0.0000701% increase. Time intervals permit up to approximately 3.94% increase. Minor allocation differences were not isolated to a precise runtime/timer source. Actual installation/service/IPC behavior, physical five-second cadence, active publication contention, cold/tiered JIT, other runtimes/architectures and whole-app profiling remain unmeasured. Raw samples, fixed fixture, normalization checks, assemblies and source/body evidence remain under `artifacts/crap/mcp-network-traffic/`; see the [batch report](CRAP-REMEDIATION-2026-11-21.md#mcp-process-traffic-query-batch).

## MCP response wire-budget dispatch (November 22, 2026)

`Responses.Result` now obtains trimmed data and its count from one typed dispatch instead of repeating the payload-type switch. Each existing trim expression is unchanged. The count is half the stable source-array length; frozen-assembly inspection verifies sealed payload records with readonly array references. History still keeps the newest suffix; other collections keep prefixes. Serialization, the inclusive 256 KiB full-protocol limit, error flags and under-budget returns remain unchanged. No new helper, dependency or heap object is introduced by the value tuple itself.

Nine workloads each use eight paired processes, half reversing load/warm/starting order, with actual original/candidate Release assemblies in separate load contexts. All 72 complete response fingerprints match. The reused harness disables tiered compilation, warms 100 calls per side, calibrates to at least 20 ms and targets 400 ms batches capped at one million calls. Seven alternating batches yield 56 samples per side. Inputs are prepared outside timing and reused without mutation; measured calls include every serialization, byte-budget check and repeated trim. Large supported fixtures contain 65 rows with 12,000-character text to exercise repeated halving; the unknown payload contains a 140,000-character field. These are controlled stress fixtures, not production name-length claims. Current-thread allocations cover this synchronous path. No build, test, coverage-analysis or profiler job ran during timing.

| Controlled response | Original median | Candidate median | Paired time ratio (95% interval) | Managed bytes/request, original → candidate |
|---|---:|---:|---|---:|
| Small under-budget Usage | 12.50349 µs | 12.41222 µs | 0.99108 (0.96319–1.01978) | 7,112.00132 → 7,112.00132 |
| Usage | 3,829.63952 µs | 3,835.99202 µs | 1.00791 (0.98957–1.02659) | 7,421,099.48 → 7,421,086.27 |
| Archived Usage | 3,787.92759 µs | 3,871.62332 µs | 1.02122 (0.99037–1.05303) | 7,425,667.86 → 7,425,595.87 |
| History, newest suffix | 4,774.60682 µs | 4,657.49428 µs | 0.99400 (0.95531–1.03426) | 8,027,089.52 → 8,027,078.29 |
| Performance trend | 3,983.54729 µs | 4,014.21049 µs | 1.01473 (0.97793–1.05291) | 7,458,180.65 → 7,458,276.68 |
| Connections | 3,870.21179 µs | 3,816.61244 µs | 1.01211 (0.98325–1.04182) | 7,430,028.23 → 7,430,051.85 |
| Traffic | 3,798.15858 µs | 3,827.15125 µs | 0.99126 (0.95266–1.03142) | 7,436,611.72 → 7,436,603.55 |
| Generic array | 3,621.06151 µs | 3,625.43848 µs | 1.01930 (0.98872–1.05082) | 7,298,974.15 → 7,298,956.00 |
| Unknown oversized payload | 374.71818 µs | 369.57980 µs | 0.99189 (0.97614–1.00789) | 706,383.84 → 706,241.32 |

No statistically detected latency or managed allocation increase was observed. Exact allocation equality is not claimed; small observed differences were not isolated to a precise pooling/runtime source. All paired allocation intervals include 1; the widest upper bound permits approximately 0.0049% increase (unknown payload), and the widest time interval permits approximately 5.3% increase (Archived Usage). Complete performance equivalence is not proved. Concurrent callers, transport, cold/tiered JIT, other runtimes/architectures and whole-app profiling remain unmeasured.

A separate bounded probe confirms an **existing nontermination hazard** for an internal envelope whose metadata alone stays above the budget. Both original and candidate fixed points serialize to 280,759 bytes, exceeding 262,144; neither returns after at least 100 ms of CPU measured after readiness. Only the owned child is killed and joined. Current bounded production callers have no demonstrated untrusted route to that constructed envelope. The behavior is documented and preserved, not silently fixed. These probes ran outside benchmark timing. Frozen assemblies/fixture, raw samples, source/count invariants and probe evidence remain under `artifacts/crap/response-budget/`; see the [batch report](CRAP-REMEDIATION-2026-11-21.md#response-wire-budget-batch).

## MCP recorded-history option validation (November 22, 2026)

`DiagnosticTools.GetPerformanceHistory` delegates numeric limits and the allowed sort names to a static local validator. Cancellation remains first; identity checks, the single retained-history snapshot, UTC window selection, ranking versus exact lifetime matching and response construction remain unchanged. Inlining the predicate reconstructs the entire original activity-tools file, including the other endpoints and MCP attributes.

Five workloads each use eight paired processes, half reversing load/warm/starting order, with actual original/candidate Release assemblies in separate load contexts. The reused harness disables tiered compilation, warms 100 calls per side, calibrates to at least 20 ms and targets 400 ms batches capped at one million calls. Seven alternating batches yield 56 samples per side. All 40 complete response fingerprints match. Controlled publications prepare the retained frames through the real owner/session path, then the owned fake capture source blocks between publications during timing. Owner/fixture construction, publication, hashing and joined disposal are outside timing. Each measured call includes validation, history snapshot/filtering, ranking or pinned selection and protocol response serialization/size handling. These synchronous calls use the current-thread allocation counter. No build, test, coverage-analysis or profiler job ran during timing.

| Controlled request | Original median | Candidate median | Paired time ratio (95% interval) | Managed bytes/request, original → candidate |
|---|---:|---:|---|---:|
| Invalid seconds, no sampling | 3.78007 µs | 3.80025 µs | 1.01334 (0.97089–1.05766) | 2,840.00041 → 2,840.00041 |
| Empty retained history | 5.81245 µs | 5.77225 µs | 1.00571 (0.97545–1.03691) | 4,448.05414 → 4,448.05414 |
| Three ranked frames, three rows each | 97.88617 µs | 98.09226 µs | 1.00804 (0.98008–1.03681) | 64,889.35668 → 64,889.34422 |
| Three frames pinned to a zero-CPU lifetime | 57.59813 µs | 55.20331 µs | 0.97953 (0.93925–1.02154) | 38,272.80295 → 38,272.79763 |
| 120 retained frames, top three | 6,340.75877 µs | 6,220.16536 µs | 0.98283 (0.94519–1.02197) | 3,803,472.12308 → 3,803,459.19707 |

No statistically detected latency or managed allocation increase was observed. Exact allocation equality is not claimed: paired allocation ratios/95% intervals are 1 for invalid input, 0.999999968 (0.999999792–1.000000143) for empty history, 0.999999617 (0.999999193–1.000000041) for ranking, 0.999999772 (0.999998886–1.000000658) for pinning and 0.999995556 (0.999974110–1.000017003) for full retention. The retained allocation interval permits approximately 0.0017% increase. Small fractional values include amortized harness/pooling effects; their precise cause was not isolated. Time intervals still permit increases of approximately 2.2–5.8%. Active publication contention, real native capture, transport, cold/tiered JIT, other architectures/runtimes and whole-app profiling remain unmeasured. Frozen fixtures/assemblies, raw samples, source proof and harness are retained under `artifacts/crap/performance-history/`; see the [batch report](CRAP-REMEDIATION-2026-11-21.md#recorded-performance-history-batch).

## MCP archive-history request validation (November 22, 2026)

`DiagnosticTools.GetUsageHistory` now delegates ordered range/options/path checks to a static local validator. Date parsing, sort lookup, validation before cancellation, the single archive read and response projection remain in their original order/scopes. Inlining the extracted predicate reconstructs the original source exactly. No public signature, MCP attribute, privacy filter or exception policy changed.

The comparison reuses the paired assembly harness: eight processes per workload, half reversing load/warm/starting order; actual original/candidate Release assemblies in separate load contexts; 100 warmups, calibration to at least 20 ms and a 400 ms target capped at one million calls; seven alternating batches yield 56 samples per side. All 32 complete response fingerprints match. Fixture construction, binding, hashing and owner disposal are outside timing. Each measured call includes validation, the injected completed-task reader, archive query/filter/sort, projection and protocol response serialization. The archive is reused rather than read from disk. A fixed clock makes response timestamps deterministic. The completed reader keeps the measured path on the calling thread, so allocation uses `GC.GetAllocatedBytesForCurrentThread`. Tiered compilation is disabled; no build, test, coverage-analysis or profiler job ran during timing.

| Controlled request | Original median | Candidate median | Paired time ratio (95% interval) | Managed bytes/request, both |
|---|---:|---:|---|---:|
| Invalid limit, no archive read | 4.17576 µs | 4.19088 µs | 1.00053 (0.97082–1.03115) | 2,840.00042 |
| Observed day without app rows | 9.01196 µs | 9.09730 µs | 1.00228 (0.97114–1.03442) | 7,568.00101 |
| Three apps | 19.29398 µs | 18.98297 µs | 0.99387 (0.96288–1.02585) | 16,104.00194 |
| 500 archived apps, return limit 200 | 864.61155 µs | 861.07821 µs | 0.99626 (0.96695–1.02645) | 780,360.08250 |

No statistically detected latency increase or managed allocation increase was observed; all paired allocation ratios are exactly 1. Small fractional bytes reflect amortized harness overhead. Time intervals still permit increases of approximately 2.6–3.4%; complete equivalence is not proved. Real archive file I/O, genuinely asynchronous storage, transport, cold/tiered JIT, concurrency, other runtimes/architectures and whole-app profiling remain unmeasured. Frozen assemblies, fixture, source proof, raw samples and harness are retained under `artifacts/crap/usage-history/`; see the [batch report](CRAP-REMEDIATION-2026-11-21.md#archive-history-batch).

## MCP installer registration characterization (November 22, 2026)

Tests alone bring `InstallerRegistration.RegisterCore` from CRAP 54.32 to 28 and `ReadCurrent` from 44.06 to 30. Production source, complexity and runtime behavior are unchanged. The complete Desktop MCP Release assembly is byte-identical to the frozen pre-batch assembly: SHA256 `C294C1C02CAA41D132C48A7F91EFD6809B4D453531B0C8DCCFC2257E9D068321`. Its `MonCore` and `MonCore.Windows` dependencies also match byte-for-byte. This batch adds no production calls, allocations or runtime work. No timing improvement is inferred from test duration; no synthetic before/after timing comparison of identical code is presented.

All 87 focused cases pass against unchanged production code, including 71 new characterizations; the affected non-incremental Release build has zero warnings/errors. Desktop/Store verification completes all 11 steps with 4,461/4,395 ordinary tests passing, plus the same five explicit read-only installed-system cases per variant. Isolated child-process tests use owned custom configuration homes and an empty client search path; real Codex/Claude commands and user registration files are not touched. Live-client interoperability, cold-process timing, mutex contention and whole-app profiling remain unmeasured. See the [batch report](CRAP-REMEDIATION-2026-11-21.md#installer-registration-batch); identity proofs, logs and coverage remain under `artifacts/crap/installer-registration/`.

## MCP sampling lifecycle boundaries (November 22, 2026)

`DiagnosticTools.SamplePerformance` delegates request validation and pinned-process observation to local functions while preserving lease/slot/deadline scopes and recorded-frame order. A capture-layout prerequisite rejected the first extraction: it lost the compiler-generated predicate-cache field. An explicit equivalent lazy cache now keeps one predicate per request. Capture comparisons normalize only the compiler-generated versus explicit delegate-field name; captured data names/types and field counts must match. Source reconstruction accounts for that cache and otherwise recovers the original source, including the aggregation inlining hint.

Each workload uses eight paired processes, half reversing load/warm/starting order. Actual original/final Release assemblies run in separate load contexts with fixed logical clocks, controlled publications and escaped JSON results. Tiered compilation is disabled; 100 warmups per side precede calibration to at least 20 ms and a 400 ms target capped at 100,000 requests. Seven alternating batches produce 56 samples per side per workload. All 32 response fingerprints and equivalent capture-layout comparisons match. No build, test, coverage-analysis or profiler job ran during timing.

| Controlled request | Original median | Final median | Paired time ratio (95% interval) | Managed bytes/request, original → final |
|---|---:|---:|---|---:|
| Invalid input, no source created | 4.70107 µs | 4.67816 µs | 0.98679 (0.94913–1.02595) | 6,920 → 6,920 |
| Two-second logical system request | 15,602.026 µs | 15,601.900 µs | 0.99881 (0.98873–1.00900) | 120,637.92 → 120,625.76 |
| Two-second pinned-process request | 15,608.204 µs | 15,600.662 µs | 0.99559 (0.98945–1.00177) | 121,237.44 → 121,251.84 |
| Pinned-process exit/reused PID | 15,594.954 µs | 15,608.943 µs | 0.99723 (0.98991–1.00461) | 121,434.40 → 121,411.84 |

No statistically detected latency or allocation increase was observed. Allocation uses **process-wide** `GC.GetTotalAllocatedBytes(true)` because requests and sampling use multiple threads. Paired allocation ratios/intervals are 1 (invalid), 0.998686 (0.993685–1.003713) for system, 1.001350 (0.994769–1.007975) for process and 1.002262 (0.993735–1.010862) for exit. Exact asynchronous allocation equality is not claimed; intervals permit increases of 0.4%/0.8%/1.1%.

These measurements include owner construction, sampler warmup, controlled frame publication, real asynchronous orchestration, result serialization and joined cleanup. The roughly 15.6 ms requests reflect the whole controlled fixture, not isolated per-frame processing or a real two-second wait. Timer/thread scheduling can mask small processing differences. Native capture, transport, production cadence, cold/tiered JIT, other runtimes/architectures, competing callers and whole-app profiling remain unmeasured. Raw samples, fixture, harness, layout diagnostic, frozen assemblies and source/body proofs are retained under `artifacts/crap/performance-sampling/`; see the [batch report](CRAP-REMEDIATION-2026-11-21.md#sampling-lifecycle-batch).

## MCP performance aggregation projection (November 22, 2026)

`PerformanceAggregate.Add` now delegates the sixteen metric names, values and units to `ReadValues`. The existing tuple array, resource-read location, state advancement, validity and total/bucket loops are preserved by source reconstruction. The first extraction without an inlining hint **failed performance verification**: it added exactly 536 managed bytes per sample, consistent with losing the caller's array stack-allocation optimization. That candidate and its interrupted timing run are retained under `artifacts/crap/performance-aggregate/uninlined/` and were not accepted. `ReadValues` now has `AggressiveInlining` with a comment explaining the measured requirement. The corrected probe and complete suite restore original allocation counts. Removing the hint requires renewed performance verification.

The final suite uses actual original/corrected Release assemblies in separate load contexts across eight paired processes per workload, half reversing load/warm/starting order. Tiered compilation is disabled; each side gets 100 warmups, calibration to at least 20 ms and a 400 ms target capped at one million requests. Seven alternating batches yield 56 samples per side per workload. All 48 input/summary/trend fingerprints match. Each timed request constructs a fresh aggregate and feeds the whole recorded stream, retaining the resulting aggregate; fixtures, binding and summary/trend serialization stay outside timing. This exercises valid intervals rather than repeatedly adding already-consumed observations. No build, test, coverage-analysis or profiler job ran during timing.

| Aggregation request | Original median | Corrected median | Paired ratio (95% interval) | Managed bytes/request, both |
|---|---:|---:|---|---:|
| One sample, one bucket, process present | 1.12117 µs | 1.11268 µs | 0.99061 (0.96772–1.01405) | 4,712.000 |
| Ten system-only samples, ten buckets | 27.13798 µs | 26.82355 µs | 0.98697 (0.96313–1.01140) | 25,232.003 |
| Ten samples with nullable metrics | 25.89445 µs | 25.37989 µs | 0.98967 (0.97333–1.00628) | 25,232.003 |
| Sixty samples, sixty buckets | 731.89903 µs | 727.56701 µs | 0.99882 (0.96833–1.03028) | 139,232.073 |
| Sixty samples, six coarse buckets | 99.43693 µs | 98.28165 µs | 0.98165 (0.95577–1.00823) | 16,112.010 |
| Ten mixed stale/gap/invalid/recovery samples | 25.61619 µs | 25.71060 µs | 0.99848 (0.96029–1.03819) | 25,232.003 |

The corrected candidate shows no statistically detected slowdown, matching allocation medians and paired allocation ratios of 1 in all six workloads. Time intervals still permit 1.5% for the single-sample request, 3.1% at sixty buckets and 3.9% for mixed invalid data. These are request-construction plus aggregation measurements, not per-sample timings. Cold/tiered JIT behavior, other runtimes/architectures, concurrent callers, sampling waits, serialization and whole-app performance remain unmeasured; earlier uncertainty is not resolved. Raw samples, probe, failed candidate, fixture, harness and source/body proofs are retained under `artifacts/crap/performance-aggregate/`; see the [batch report](CRAP-REMEDIATION-2026-11-21.md#performance-aggregate-metric-projection-batch).

## MCP hardware projection and response bounds (November 22, 2026)

`HardwareProjection.Safe` keeps its project-all-sections, then clip-all-sections order. Local functions name each section's projection and clipping policy while sharing the original identifier-policy capture. Expanding their expressions reconstructs the original source; `Trim`, the issue filter, limits, privacy rules and other source are unchanged. All 413 new characterization cases plus 20 existing diagnostic cases pass before and after extraction, with covered runs as well. Compiler capture layout remains one `source` field in one display class.

Each workload uses eight paired processes, half reversing load/warm/starting order. Actual original/final Release assemblies run in separate load contexts, with fixed model fixtures, tiered compilation disabled, 100 warmups per side, calibration to at least 20 ms and a 400 ms target capped at one million calls. Seven alternating batches yield 56 samples per side per workload. All 56 payload/result/changed fingerprints and capture-layout comparisons match. Binding, fixture construction and fingerprinting stay outside timing; results escape through a retained sink. No build, test, coverage-analysis or profiler job ran during timing.

| Workload | Original median | Final median | Paired time ratio (95% interval) | Managed bytes/call, original → final |
|---|---:|---:|---|---:|
| Ordinary populated inventory | 4.85494 µs | 4.76470 µs | 0.98309 (0.96822–0.99819) | 8,032.001 → 8,032.001 |
| Empty collections | 0.78265 µs | 0.77586 µs | 0.97816 (0.93832–1.01969) | 1,632.000 → 1,632.000 |
| All section data missing | 0.36929 µs | 0.37940 µs | 1.02844 (0.99868–1.05909) | 896.000 → 896.000 |
| Hidden long identifiers | 4.08678 µs | 4.01329 µs | 0.97047 (0.94675–0.99479) | 7,336.000 → 7,336.000 |
| 257 rows in every top-level collection | 643.36175 µs | 646.10827 µs | 0.98363 (0.96697–1.00057) | 1,183,416.060 → 1,183,416.060 |
| One trim of eight five-row collections | 0.24268 µs | 0.24315 µs | 1.00039 (0.94967–1.05383) | 696.000 → 696.000 |
| Full bounded MCP hardware response | 34,598.52727 µs | 34,706.36045 µs | 1.00100 (0.97229–1.03056) | 18,727,386.545 → 18,727,729.545 |

No statistically detected slowdown or allocation increase was observed. The response allocation medians differ by 343 bytes/call on an 18.7 MB baseline; paired allocation ratio is **1.00000309 (0.99998023–1.00002595)**, with increases and decreases across process pairs. The six direct workloads have matching allocation medians and paired ratios of 1. No additional allocation expressions or capture fields were introduced. Exact response allocation equivalence is not claimed.

Time intervals still permit increases of 2.0% for empty data, 6.0% for missing data, 5.4% for trimming and 3.1% for the response. The response case includes identifier removal, `Safe`, serialization and repeated `Trim`; both versions meet the 256 KiB serialized response budget. Native hardware collection, transport, concurrent callers, JIT code footprint and whole-app profiling were not measured. Earlier uncertainty remains open. Raw samples, frozen assemblies, fixture, harness and source/body proofs are retained under `artifacts/crap/hardware-projection/`; see the [batch report](CRAP-REMEDIATION-2026-11-21.md#mcp-hardware-projection-batch).

## Service snapshot validation boundaries (November 22, 2026)

`NetworkServiceClient.ValidateSnapshot` now delegates header, window/counter and reading-set checks to three private predicates. Expanding their unchanged expressions reconstructs the original source, including short-circuit order, endpoint parsing, raw flow-key comparison and collection enumeration. The entry point retains the single `InvalidDataException` and its message. All 159 new characterization cases passed against the original implementation before extraction; all 172 selected cases pass afterward.

The actual original and final Release assemblies run in separate load contexts in each of eight paired processes per scenario, half reversing load/warm/starting order. Tiered compilation is disabled; each side gets 100 warmups, calibration to at least 20 ms and a 400 ms target capped at ten million calls. Seven alternating batches yield 56 samples per side. Payload/outcome fingerprints match in all 40 processes. Payload construction, binding, serialization and fingerprint assertions stay outside timing; the measured operation includes the original validation, enumeration, endpoint parsing and deduplication allocations. No build, test, coverage-analysis or profiler job ran during timing.

| Snapshot | Original median | Final median | Paired ratio (95% interval) | Managed bytes/call, original → final |
|---|---:|---:|---|---:|
| Empty collections | 0.09688 µs | 0.10271 µs | 1.05995 (0.98690–1.13840) | 328.002 → 328.002 |
| 8,192 process rows | 12.98747 µs | 13.03750 µs | 1.01931 (0.98463–1.05521) | 328.001 → 328.001 |
| 1,000 mixed IPv4/IPv6 flows | 159.69901 µs | 157.34885 µs | 0.97636 (0.95475–0.99846) | 344,563.619 → 344,563.606 |
| 65,536 mixed IPv4/IPv6 flows | 12,649.73167 µs | 12,804.12000 µs | 1.01569 (0.97322–1.06001) | 18,457,520.533 → 18,457,521.370 |
| Invalid subscription rejected | 1.36480 µs | 1.28709 µs | 0.97779 (0.95777–0.99824) | 336.000 → 336.000 |

No statistically detected slowdown or meaningful allocation increase was observed. Fractional allocation differences below one byte per call reflect measurement overhead amortization; exact allocation equivalence is not claimed. Intervals still permit increases of 13.8% for the roughly 0.1 µs empty case, 5.6% for process rows and 6.1% at the flow limit. Whole-app, end-to-end pipe/deserialization, concurrent load and adversarial mutable collections were not benchmarked. These results do not resolve earlier performance uncertainty. Raw samples, harness, assembly snapshots and source proof are retained under `artifacts/crap/snapshot-validation/`; see the [batch report](CRAP-REMEDIATION-2026-11-21.md#service-snapshot-validation-batch).

## Native service pipe identity boundaries (November 22, 2026)

`NetworkServiceIdentity.Verify` keeps pipe/SCM binding and delegates process-handle lifetime and token authority to cohesive private boundaries. Inlining both boundaries reconstructs the original source, preserving native bindings, access masks, query/recheck order, last-error handling and token disposal. Eight ordinary cases and one explicit matching-service case accompany existing client cases; see the [batch report](CRAP-REMEDIATION-2026-11-21.md#native-pipe-identity-and-service-pid-batch). `NetworkServiceProcess.Read` has tests only and unchanged source/API.

Accepted and rejected authentication use real native queries and the same physical borrowed pipe per original/final pair. Each case has eight isolated paired processes, half reversing load/warm/starting order, tiered compilation disabled, 100 warmup calls per side, calibration to at least 20 ms and a 400 ms target capped at 20,000 calls. Seven alternating batches produce **56 samples per side**. All 16 PID/verdict fingerprints match; both forced-GC fixture disposal checks pass. No build, test, coverage-analysis or profiler ran during timing.

| Native verification case | Original median | Final median | Paired ratio (95% interval) | Managed bytes/call, both |
|---|---:|---:|---|---:|
| Existing matching service | 457.831 µs | 458.902 µs | 0.99132 (0.94077–1.04457) | 1,760.044 |
| Unrelated owned pipe | 302.488 µs | 305.373 µs | 1.00317 (0.96918–1.03836) | 1,272.034 |

No statistically detectable slowdown or allocation increase was observed, but intervals permit **4.5% /3.8%** increases. Timing includes native SCM/process/token queries and excludes connection setup. Read-only status keepalives outside timing prevent the real service's idle timeout; no subscription, capture start or service control occurs. Functional characterization sends no protocol requests. Current live service/app background load is not controlled, and privileged token paths, all native failures/races and whole-app/deployment performance remain unverified. All raw pairs, fingerprints, source/API proof and timed/full-build method identity are retained in `artifacts/crap/pipe-identity/`; earlier timing limits remain open.

## Protected tree and inventory characterization without production changes (November 21, 2026)

Fifteen ordinary cases and three explicit read-only installed-system cases characterize `ProtectedInstallation.Tree` and `InstalledFiles.Validate`. Their CRAP scores fall **72 →8.03** and **72 →8** at unchanged complexity **8** each. Traversal reaches 12/13 lines and 7/8 branch outcomes; inventory reaches all ten lines but only 4/8 outcomes, so its secondary branch target remains unmet. The [batch report](CRAP-REMEDIATION-2026-11-21.md#protected-installed-tree-and-inventory-characterization-batch) separates ordinary and prerequisite-dependent coverage and describes the independent oracles.

No production source, runtime binding, handle behavior, allocation or I/O operation changed. All **21 managed method bodies and implementation flags** in the two classes and their nested types match the original, focused and full builds. The original and final full managed setup assembly remains byte-identical: SHA-256 `192AFB42A39CD8C74B53A43BDD285FCE45FAEF07DA90E54CC2062325938A9057`. The protection source stays `433573B16F655ED8BFC06AD955875095238D1B23EC7B1C8E57D180E717D4C28C`; inventory source stays `2C587805D14942111124EAF4D490C2337581F0821608F14480D61C95FA951048`. Identical logic was not benchmarked again. This establishes no implementation performance change from the tests-only batch; it does not cover mutation/rollback load, the published Native AOT executable or earlier timing uncertainty. Evidence is retained under `artifacts/crap/installed-readonly/`.

## Read-only native query characterization (November 21, 2026)

Ten ordinary integration cases and one explicit matching-installation case characterize `NativeService.EnsureExactConfiguration` and `QuerySecurity` without production changes. Ordinary selected tests already reduce CRAP **210 →14.71** and **72 →8**. The additional installed-service query raises configuration coverage to all 13 lines and 13/14 branch outcomes, giving CRAP **14**; the [batch report](CRAP-REMEDIATION-2026-11-21.md#read-only-native-configuration-and-permission-queries-batch) separates ordinary and host-dependent coverage.

Source SHA-256 stays `87AD3360FEF8B0A72A28842B56B9E7619A23DF462C4D19601ADC4D344CCB915E`. All **21 managed NativeService method bodies and implementation flags** match the frozen original, focused and full builds. The original and final full managed setup assemblies are byte-identical: `192AFB42A39CD8C74B53A43BDD285FCE45FAEF07DA90E54CC2062325938A9057`. No new production query, buffer, handle or allocation exists to benchmark, so identical logic was not timed again. This proves no implementation performance change from this tests-only batch; it does not validate native mutation/rollback load or the published Native AOT executable, and earlier timing uncertainty remains open. The verification catalogue adds only the exact new explicit test/reason pair, with self-checks retaining rejection of changed or unknown reasons. Evidence is in `artifacts/crap/native-queries/verified-unchanged.json`.

## Bounded archive entry copying (November 21, 2026)

`PackageExtraction.ExtractServiceArchive` retains archive-wide validation and delegates entry stream ownership, its unchanged 64 KiB copy loop and length checks to private `CopyEntry`. Thirty-five new characterization cases pass on the original and candidate, alongside 13 existing cases; the [batch report](CRAP-REMEDIATION-2026-11-21.md#archive-entry-copying-batch) records exact contracts, source comparison and coverage. No buffer, delegate allocation, I/O operation or asynchronous boundary was added.

Eight paired processes per case load frozen original/final libraries, half reversing load/warm/starting order. Real temporary files share the same staging path, and every iteration extracts a Fastest-compressed ZIP and deletes the staged tree. All 24 complete file/payload fingerprints match; input streams retain their bytes and remain open. A representative forced-GC fixture disposal check passes. Tiered compilation is disabled, warmup is 100 calls per side, calibration reaches at least 20 ms and batches target 400 ms with a 2,000-call cap. Seven alternating batches produce **56 samples per side/case**. No build, test, coverage-analysis or profiler ran during timing.

| Extraction and cleanup case | Original median | Final median | Paired ratio (95% interval) | Managed bytes/iteration, original → final |
|---|---:|---:|---|---:|
| One 256-byte executable | 3,123.443 µs | 3,047.013 µs | 0.95385 (0.85386–1.06555) | 77,944.247 →77,944.270 |
| Nine files, about 36 KiB total | 15,933.664 µs | 15,060.616 µs | 0.98054 (0.91399–1.05195) | 618,129.905 →618,129.818 |
| One 65,537-byte executable | 3,261.598 µs | 3,284.437 µs | 1.00170 (0.90624–1.10720) | 77,944.339 →77,944.258 |

No statistically detectable slowdown was observed, but upper intervals permit **6.6%, 5.2% and 10.7%** increases. Allocation differences below 0.1 byte/iteration are consistent with bounded amortization/noise, not an exact equality measurement. This is an extraction-plus-cleanup workload including filesystem and decompression costs, not isolated helper latency. Protected creation callbacks, publisher/SCM checks, the elevated Native AOT executable and full installation/rollback were not measured. Earlier uncertainty remains open. All raw pairs and harness/binary provenance remain in `artifacts/crap/archive-extraction/`.

## Setup protection characterization without production changes (November 21, 2026)

Nine new native characterization cases reduce `ProtectedInstallation.Check` from CRAP **272 to 16.12** at unchanged complexity **16**, covering protected Windows entries, unsafe owned temporary entries, junctions, missing paths and null. The [batch report](CRAP-REMEDIATION-2026-11-21.md#setup-native-protection-characterization-batch) records the uncovered writer rejection and environment-dependent native ACL scope.

No production code, dependency, runtime binding or compiler implementation flag changed. The frozen original and final full Desktop **setup assemblies are byte-identical**: SHA-256 `E5063911FF755CB4D60513C72B12744EA74B59CB4D29178ADC8D510737BD8F38`. All nine protection-method bodies and flags also match the focused build. Source SHA-256 stays `433573B16F655ED8BFC06AD955875095238D1B23EC7B1C8E57D180E717D4C28C`. There is no new production work or allocation to benchmark, so identical logic was not timed again. This establishes that this tests-only batch introduces no implementation performance change; it does not resolve earlier benchmark uncertainty or prove performance under elevated installation load. Evidence is retained in `artifacts/crap/setup-protection/verified-unchanged.json`.

## Pipe snapshot preparation (November 21, 2026)

`NetworkPipeServer.ServeAsync` delegates its existing rate check, collector read, generation/sequence stamp and payload fitting to private synchronous `PrepareSnapshot`. Dispatch, authentication, writes and cleanup retain their original order. Eight new characterization cases passed against the original and final implementations; see the [batch report](CRAP-REMEDIATION-2026-11-21.md#bounded-pipe-snapshot-preparation-batch).

The frozen original and final libraries ran through real current-user local pipes: status, subscribe, one snapshot, unsubscribe and complete handler cleanup per iteration. Eight paired processes per case, half reversing order, produced 56 alternating batches per side with tiered compilation disabled, 1,000 warmup iterations and adaptive calibration. All 24 response fingerprints match. No build, test or profiler ran during timing; one brief existing metrics-file inspection occurred during startup.

| Case | Original median | Final median | Paired ratio (95% interval) | Process-wide bytes/iteration, original → final |
|---|---:|---:|---|---:|
| Small | 1,408.455 µs | 1,471.015 µs | 1.04200 (0.97446–1.11423) | 16,589.46 →16,584.94 |
| Populated flows | 2,196.230 µs | 2,197.823 µs | 1.01250 (0.98059–1.04544) | 308,111.91 →308,110.33 |
| Unavailable | 1,473.696 µs | 1,406.807 µs | 0.97367 (0.93931–1.00928) | 15,977.74 →15,976.52 |

No statistically detectable slowdown or allocation increase was observed. The small-case interval still permits an 11.4% increase, and populated flows permit 4.5%. Process-wide allocation counters include async continuations and runtime noise; they do not establish exact per-operation equality. Transport, connection setup, serialization and teardown dominate this measurement. Authentication/impersonation, sustained multi-client load and the full elevated service were not benchmarked. Earlier native-policy timing limits remain open. Raw pairs, harness, original/final binaries and build-identity evidence are retained in `artifacts/crap/pipe-serve/`.

## Protected installation and ACL policy separation (November 21, 2026)

`ServicePolicy.Load` keeps its fixed executable-location guard and delegates protected-tree traversal/bounded JSON reading to `ReadInstallation`. The native checker still rejects reparse points before fetching ACLs, then delegates the unchanged owner/ACE rules to `ValidateProtection`. Seventy-five cases cover the separated responsibilities; see the [policy report](CRAP-REMEDIATION-2026-11-21.md#protected-installation-and-acl-policy-batch) for source/default-binding proofs, partial native coverage and original-code evidence.

Only **native rejection paths** are compared against the frozen original. The first 24 paired processes used separate physical entries and produced a native-file estimate of **1.12084** (95% interval **0.92565–1.35718**). All that data and harness source remain under `artifacts/crap/service-policy/separate-fixtures/`. All three cases were rerun uniformly with a **shared physical `FileSystemInfo`** for the original/final libraries to remove that fixture confound; no cause is assigned to the earlier variation.

Each final case has eight isolated paired processes, half reversing load/warm/starting order, seven alternating batches (**56 per side**), tiered compilation disabled, a 1,000-call warmup and calibration to at least 20 ms. The 400 ms target is capped at 20,000 launch calls or 5,000 native calls. No build/test/coverage-analysis job ran during timing. All 24 fingerprints match, and strongly rooted load contexts pass forced-GC disposal. JIT inspection finds a tail jump into the ACL validator: native wrapper **1,040 →548 bytes**, without an added return frame or new inlining hint.

| Native rejection path | Before | Final | Paired ratio (95% interval) | Bytes/call, both |
|---|---:|---:|---|---:|
| Wrong executable location | 2.16803 µs | 2.06393 µs | 0.95546 (0.89054–1.02512) | 792 |
| Untrusted file | 49.30342 µs | 54.81051 µs | 1.02059 (0.88423–1.17797) | 5,328 |
| Untrusted directory | 43.80200 µs | 43.34151 µs | 0.94320 (0.79319–1.12158) | 5,616 |

Allocations match, plus the same 40-byte harness cost per batch. No statistically detectable slowdown is established; the file median and paired estimate remain upward, and the interval still permits **17.8%** overhead (directory **12.2%**). This uncertainty remains open. Successful protected startup/trusted traversal, native link rejection and first-success allocation were not benchmarked. The default validator delegate is lazily cached, with no per-entry delegate construction; its first-success allocation was not measured. Controlled fake-success timing is not presented as a native-success comparison. Frozen versions, all pairs, JIT and validation evidence remain in `artifacts/crap/service-policy/`.

## Ledger snapshot quality extraction (November 21, 2026)

`NetworkTrafficLedger.Snapshot` delegates its common warmup/incomplete/capacity/discontinuity policy to `SnapshotQuality`. Conditions and evaluation order are unchanged; complexity becomes **28**, with the helper at **8**. Twenty-one new cases first pass against the original. Source reversal restores the entire original file, and all 96 final ledger/nested method bodies and flags match the full Desktop build. See the [ledger report](CRAP-REMEDIATION-2026-11-21.md#ledger-snapshot-quality-batch).

Each final scenario has eight isolated paired processes, half reversing load/warm/starting order, seven alternating batches (**56 per side**), tiered compilation disabled, a 1,000-call warmup and calibration to at least 20 ms. The 400 ms target is capped by allocation budgets: 50,000 calls for small snapshots, 2,000 for populated/loss snapshots and 30,000 for reset/capacity snapshots. No build/test/coverage-analysis job ran during timing. All 32 fingerprints match checked counts, bytes, rates, quality and returned payloads. Snapshot times are fixed and monotonic; collectible contexts remain rooted through disposal and pass the forced-GC check.

Initial small batches lasted about five milliseconds. Their complete raw data/harness remain in `artifacts/crap/ledger-snapshot/short-batches/`. All four scenarios were rerun uniformly with longer bounded batches. No accepted sample is discarded. JIT inspection shows an ordinary out-of-line helper call; no inlining hint was added.

| Scenario | Before | Final | Paired ratio (95% interval) | Allocated bytes, both |
|---|---:|---:|---|---:|
| One owner/one flow | 0.48172 µs | 0.47860 µs | 0.97930 (0.90469–1.06007) | 1,680 |
| 32 owners/256 flows, valid rates | 22.35093 µs | 22.44033 µs | 0.97503 (0.91019–1.04450) | 47,712 |
| 32 owners/256 flows, loss recovery | 21.61680 µs | 21.48570 µs | 1.01074 (0.95255–1.07248) | 47,712 |
| One owner/eight flows, reset/capacity | 1.23602 µs | 1.23369 µs | 0.98015 (0.92199–1.04197) | 3,272 |

Allocations match, plus the identical 40-byte harness cost per batch. No slowdown is detected; intervals still permit about **4.5%** overhead for populated healthy snapshots and **7.3%** for loss recovery. This is steady-state controlled evidence. Cold JIT, native scheduling and whole-app profiling were not measured, and earlier clock/native uncertainty remains open. Frozen source/binaries, all pairs, source/body proofs and harnesses are under `artifacts/crap/ledger-snapshot/`.

## Service power-transition characterization (November 21, 2026)

Twenty-eight cases cover initialized host power mapping, lease retention, fresh generations, asynchronous acknowledgement, cancellation, error logging and repeated stopping. **No production source changed.** The entire service library is byte-identical to its before snapshot in each build path: normal/focused SHA-256 `58DD114D556759B211D98A6B2BCAE7CCBDC04D8D6AB28FC5262C9BBAE5EDC10A`, full Desktop `B7C228B43C332BA79DACAFBE63F304C7DAF7F6F24D57AA118C2304F77A67AE06`, and Store `B54739F613025A1336D7F6843CCC4BD6355813C0CD509500831DCB1858247066`. No redundant timing comparison was run. The [power-transition report](CRAP-REMEDIATION-2026-11-21.md#service-power-transition-batch) records behavior, gates and scope. Actual hardware suspend/resume and combined native startup-failure recovery were not measured; earlier clock/native timing uncertainty is unchanged.

## ETW clock and reconciliation characterization (November 21, 2026)

The collector has an internal clock boundary for deterministic event, reconciliation, snapshot and timer tests. Both existing constructors retain system time. A nullable clock reference keeps production QPC/UTC reads direct; injected clocks must use Stopwatch timestamp units. Accounting, ownership and calibration protocols are unchanged. See the [characterization report](CRAP-REMEDIATION-2026-11-21.md#etw-clock-event-and-reconciliation-batch) for the behavior matrix and native-session limits.

The first virtual-clock candidate caused a **4.76% packet-processing slowdown** (95% paired ratio interval **1.00270–1.09460**) and was rejected. Its source, binaries and all 32 pairs remain under `artifacts/crap/etw-reconcile/virtual-clock/`. The final timestamp helper is inlined and selects the original direct QPC read for production; no extra inlining hint was added. The initial snapshot setup also sampled a future QPC during fingerprinting; the corrected fixture avoids that unintended backwards-clock transition. All four final scenarios were rerun uniformly.

Each scenario uses eight isolated paired processes, half reversing load/warm/starting order, seven alternating batches (**56 per side**), tiered compilation disabled and no concurrent build/test/coverage-analysis work. A 1,000-call warmup precedes adaptive calibration to at least 20 ms and a 400 ms target, capped at twenty million calls for allocation-free events and 100,000 for allocating operations. All 32 fingerprints match. Fixture construction/expressions are outside timing, and collectible contexts are retained through disposal with a forced-GC self-check.

| Scenario | Original | Final | Paired ratio (95% interval) |
|---|---:|---:|---|
| Unknown-PID stop | 42.664 ns | 41.607 ns | 0.97459 (0.93167–1.01948) |
| Accepted aggregate packet, cached owner | 93.082 ns | 90.937 ns | 0.98811 (0.93895–1.03986) |
| Eight-owner reconciliation, controlled creation reads | 0.52204 µs | 0.52361 µs | 0.96777 (0.92629–1.01111) |
| Snapshot, eight registered traffic-free owners | 1.00751 µs | 0.96083 µs | 1.00289 (0.87626–1.14781) |

No slowdown is detected; this is not proof of zero regression. The snapshot interval still permits **14.8%** cost, and the packet interval permits **4.0%**. Native ETW dispatch, non-null session loss reporting, sustained live capture and whole-app profiling were not measured. Earlier native ownership uncertainty remains open.

Per-operation allocations match: events have only the same 40-byte harness cost per batch, reconciliation uses **56 bytes**, and snapshots use **2,488 bytes**. A separate warmed construction/disposal comparison (100 warmups, 10,000 measured per side) measures **1,688 →1,696 bytes/collector**, reflecting the **eight-byte clock reference once per collector**. Raw pairs, harnesses, rejected candidate and allocation evidence remain under `artifacts/crap/etw-reconcile/`.

## ETW queue lifecycle characterization (November 21, 2026)

Thirteen tests cover the 4,096-event batch boundary, queue completion, cancellation and consumer failure mapping. **No production source or executable behavior changed.** Baseline, normal and focused-test `WinMon.NetworkService.dll` bytes match SHA-256 `615288BA60407C9685BA1D5271DD0E4A22E66707D0D4F4F8E6E6C04F0A3DD8D6`; all 116 collector/nested bodies and implementation flags also match the full Desktop build. No additional timing run was needed. This does not resolve the earlier native timing uncertainty or verify normal timer cadence/live ETW behavior. See the [queue lifecycle report](CRAP-REMEDIATION-2026-11-21.md#etw-queue-and-consumer-lifecycle-batch).

## ETW ownership characterization and cache refresh (November 21, 2026)

`EtwNetworkCollector` has an internal per-instance creation/SID probe boundary whose default forwards to the original static native functions. `TryRefreshKnownOwner` separates creation-only cache refresh from full SID authorization/registration. See the [ownership report](CRAP-REMEDIATION-2026-11-21.md#etw-ownership-batch) for original-native versus seam-only evidence and test scope.

The first extraction left a 177-byte helper out of line and caused a **3.87%** controlled known-cache slowdown (95% ratio interval **1.01584–1.06217**). That version and all eight raw pairs are retained in `artifacts/crap/etw-ownership/out-of-line/`. One measured `AggressiveInlining` hint removes the extra call; seam-only/final callers both compile to 739 native bytes. No other hint was added.

The first native benchmark also exposed a harness lifecycle defect: collectible contexts could begin unloading before late dependency resolution during disposal. Forced GC reproduces the failure; retaining contexts through disposal fixes it. Preliminary files are retained in `pre-retention-harness/` and excluded from final results. All four scenarios were rerun consistently with the corrected harness, yielding **32 matching paired fingerprints**.

Each scenario has eight processes, half reversing load/warm/starting order, seven alternating batches (56 per side), tiered compilation disabled and no simultaneous build/test/coverage work. A 1,000-call warm-up precedes adaptive calibration to at least 20 ms and a common 400 ms target, capped at twenty million calls. Construction, compilation and fingerprints are outside timing. The native reference is the original real current-process probe; the other references are the frozen seam-only implementation with preallocated controlled results.

| Scenario | Before | Final | Paired ratio (95% interval) |
|---|---:|---:|---|
| Native lifetime refresh | 1.24472 µs | 1.27548 µs | 1.03594 (0.98196–1.09288) |
| Known lifetime, controlled | 18.7165 ns | 18.4628 ns | 0.99112 (0.96437–1.01862) |
| Full probe after creation failure, controlled | 65.2217 ns | 65.7563 ns | 1.00522 (0.97843–1.03275) |
| Unknown process, controlled | 23.1493 ns | 22.5633 ns | 0.96629 (0.91298–1.02271) |

No slowdown is detected in the final comparisons; the native upward estimate remains noisy and cannot exclude about **9.3%** cost. Native allocation is 32 bytes/read on both sides. Controlled paths match at roughly 40 bytes of harness overhead per multi-million-call batch. A separate warmed construction/disposal test measures 1,712.00 →1,688.00 bytes/collector, excluding singleton initialization; no cause is assigned to the reduction. Raw data, frozen versions and harness checks remain under `artifacts/crap/etw-ownership/`. Real ETW load, full native SID-probe throughput and whole-app profiling were not measured.

## ETW callback direction normalization (November 21, 2026)

`EtwNetworkCollector.Enqueue` now normalizes receive-side address/port pairs once, replacing four parallel send/receive selections. No helper, allocation, native operation or production dependency was added. Complexity falls from 36 to 30; 53 new cases first passed against the unchanged original. The [CRAP report](CRAP-REMEDIATION-2026-11-21.md#etw-callback-batch) records validation, scope proofs and native-fixture limits.

The harness measures synthetic-header callback plus common fixture dispatch and channel dequeue. Three scenarios each use eight paired processes, half reversing load/warm/starting order, with seven alternating batches (56 per side/scenario), tiered compilation disabled. A 1,000-call warm-up precedes adaptive calibration (at least 20 ms) and common 400 ms target batches of about 4.5–6.5 million calls. No builds, tests or coverage analysis ran concurrently. Construction, header allocation, reflection, expression compilation and fingerprints are outside timing; all 24 packet/quality fingerprints match.

| Scenario | Original median | Final median | Paired ratio (95% interval) |
|---|---:|---:|---|
| IPv4 TCP send | 66.523 ns | 66.617 ns | 1.00490 (0.97086–1.04013) |
| Scoped IPv6 UDP receive | 69.544 ns | 69.106 ns | 0.99678 (0.98263–1.01113) |
| Mixed directions/families | 70.605 ns | 71.744 ns | 1.01272 (0.97695–1.04981) |

No slowdown is statistically detected; the mixed result cannot exclude roughly 5% cost. Allocation counters match, with about 40 bytes of harness overhead per multi-million-call batch and no measured per-callback allocation. All raw samples are retained under `artifacts/crap/etw-enqueue/`; no sample was discarded and no inlining hints were added. This does not measure real parser dispatch, native ETW under sustained load or whole-app throughput. The separate artifact fixture's restore reported unavailable vulnerability metadata (`NU1900`); runtime packages were already present and production/test verification builds had no warnings/errors.

## Windows service catalog characterization (November 21, 2026)

`WindowsServices.Reader.Read` retains its native paging/parser/cache algorithm. An internal SCM enumeration and `TimeProvider` seam enables deterministic failure and boundary tests; public construction still uses native SCM and system UTC. Complexity stays 30 and CRAP falls from 30.16793 to 30 through coverage. See the [characterization report](CRAP-REMEDIATION-2026-11-21.md#windows-service-catalog-batch).

The benchmark loads original and final assemblies into separate contexts and calls their public Readers against actual read-only SCM. Eight paired processes, half reversing load/warm/starting order, use seven alternating batches per side (56 each), tiered compilation disabled. No builds, tests or coverage analysis ran concurrently. Reader construction, reflection, serialization and snapshot checks are outside timing; five warm reads and ten calibration reads precede a common count targeting 400 ms (617–722 reads per batch). This measures the persistent-reader sampling path, not first-reader cost.

| Measurement | Original | Final |
|---|---:|---:|
| Median native read | 617.58 µs | 617.48 µs |
| Caller-thread allocation/read | 2,624.06 B | 2,624.06 B |

The process-paired geometric timing ratio is **1.00061**, 95% interval **0.96644–1.03599**. No regression was detected; a slowdown of about 3.6% cannot be excluded. Allocation includes small amortized harness overhead. Live services can change during measurement. All eight raw pairs and the harness are retained under `artifacts/crap/service-catalog/`; no sample was discarded. This does not establish service-action/ETW/whole-app performance.

## Windows service control refactoring (November 21, 2026)

Service control now has an internal native-operation/process boundary and a `TimeProvider` parameter. The public entry point still uses the original SCM/process APIs and system time. Two checks separate initial service/identity validation from opened-process validation; the caller retains the original process `using` scope across the stop wait. The [CRAP report](CRAP-REMEDIATION-2026-11-21.md#windows-service-control-batch) distinguishes original public tests, seam-only characterization and final refactoring. No actual service was started/stopped by the new tests or benchmarks. Artifacts are under `artifacts/crap/service-control/`.

**Reference limits:** public unsupported-action rejection compares the original library with the final library and never opens SCM. Valid start/stop/restart/suspended-stop cases compare the frozen **seam-only core** with the final core using controlled handles, process data and timers. They do not compare original valid native operations or measure live SCM cost. Fake resources are preallocated/reused; zero allocation in synchronous controlled cases is **not** a claim about real service operations. The native process adapter adds a managed wrapper per stop/restart, while `TimeProvider` removes the old Stopwatch object per wait. Their net default-path cost has not been measured against real service operations.

The fixture loads separately against both libraries. Complete operation-order hashes, query/access/start/stop/disposal/timer counts and relevant public service/Reader signatures match in all **forty** final paired processes. Each scenario has eight processes, half reversing library load/hash/warm-up/starting order, and seven alternating batches: **56 batches per side/scenario**. Tiered compilation is disabled. Setup, fixture construction, reflection, hashes and JSON are outside timing. Both caller-thread and whole-process allocation counters are recorded; medians agree in this set, including suspended control.

The final harness warms 1,000 calls and the Stopwatch conversion before timing, then increases calibration calls by four until at least 20 ms or its cap. A common count targets 400 ms. Allocation-free synchronous cases cap at twenty million calls; rejection and suspended control cap at 100,000 to bound allocations. Counts are 37,101–100,000 rejection, 12,316,685–14,284,518 start, 8,690,191–10,781,685 stop, 6,516,093–8,783,140 restart and 100,000 suspended control. Suspended batches are consequently shorter (about 23 ms). No agent build/test/analysis jobs run during timing.

| Scenario | Reference | Before→after median | Allocation/call, both | Paired ratio (95% interval) |
|---|---|---:|---:|---|
| Public rejection | Original public API | 3.338→3.269 µs | 1,296 B | 0.975 (0.926–1.026) |
| Controlled start | Seam-only core | 30.06→30.27 ns | 0 B | 1.016 (0.970–1.063) |
| Controlled stop | Seam-only core | 40.25→41.58 ns | 0 B | 1.017 (0.994–1.041) |
| Controlled restart | Seam-only core | 49.22→49.95 ns | 0 B | 1.004 (0.960–1.051) |
| Suspended stop | Seam-only core | 225.86→226.56 ns | 424 B | 1.011 (0.925–1.106) |

Intervals use per-process mean paired log ratios and a Student-t interval across eight processes. No consistent final timing increase or allocation increase is established. The intervals permit small costs; fake dispatch throughput does not establish native service latency, whole-app performance or equivalence under production races.

The first host-factory extraction returned an owned process and disposed validation failures in a manual catch. Its initial 100,000-call cap produced only a few milliseconds per synchronous batch and showed a 9.25% stop increase. An initial attempt to raise the cap still under-calibrated because the thirty-call estimate included timing-helper startup costs. All sets remain `short-batch-*` and `confirmation-stop-*`. Adaptive calibration with longer batches confirmed a smaller **2.49%** increase (interval **1.006–1.044**), retained as `confirmed-stop-*`. JIT output showed the `OpenExpectedHost` call. That version was not accepted. Keeping ownership in the caller and using two validation checks removes the transfer/catch; the first check inlines naturally, and no inlining hint is added. The final stop set is the eight `validation-stop-*` comparisons, copied to `perf-stop-*`; the other four final scenarios were then measured with the same adaptive harness. No samples within a set were retried/dropped, and versions are not pooled. Initial factory source/DLL/PDB, passing gates and coverage remain `factory*`. The final timed/focused-test library and full Desktop test build have matching IL and implementation flags across all 31 declared service/nested methods. Native structure layouts and P/Invoke declarations match the original. Final build/test verification follows these measurements.

## Incident event mapping refactoring (November 21, 2026)

`IncidentEventMapper.Locate` keeps the nearest-observation/containing-interval scan and delegates interpolation and ambiguity checks to `Place`. The calculation body is text-identical; no collection, traversal or heap state is added. The [CRAP report](CRAP-REMEDIATION-2026-11-21.md#incident-event-mapping-batch) records fifty new cases, original/final verification and clock/identity boundaries. Original, call-based and corrected libraries, JIT listings, harness and results are under `artifacts/crap/event-mapping/`.

Small history maps eight events over three observations. Normal history maps sixty events over 600 one-second observations. Repeated-clock history maps sixty events over 600 observations whose wall time cycles through sixty seconds while monotonic time advances. Metadata rotates through eight owners, including a foreign-domain owner; long histories include periodic gaps, nullable event identities and retained metadata. Each call executes the complete public `Map`, including lazy owner indexing and all output records. Complete ordered event/time/selection/approximation/linked-process payloads and exported APIs match in all **24** corrected comparisons. All seven declared mapper/generated method bodies and implementation flags match the full-test build; the timed DLL is byte-identical to the focused-test library.

Eight separate paired processes per scenario, half reversing library load/hash/warm-up/starting order, run seven alternating batches: **56 batches per side/scenario**. Tiered compilation is disabled. Thirty warm-up and thirty calibration calls select a common count targeting 400 ms, bounded at 10–100,000 calls. Counts are 100,000 small, 598–1,247 normal and 1,174–1,260 repeated. The small workload reaches the cap and produces shorter batches. Setup, reflection, JSON and hashing are outside timing. Builds, tests and coverage analysis end before timing; no agent heavy jobs run concurrently.

| Workload | Original median | Corrected median | Allocation/call, both | Paired ratio (95% interval) |
|---|---:|---:|---:|---|
| Small history | 0.800 µs | 0.788 µs | 1,336 B | 0.967 (0.910–1.026) |
| Normal history | 330.39 µs | 333.45 µs | 28,544 B | 1.017 (0.982–1.053) |
| Repeated clocks | 328.69 µs | 337.06 µs | 28,840 B | 1.008 (0.983–1.034) |

Intervals use the mean within-process paired log ratio and a Student-t interval across eight independent processes. Medians and paired ratios summarize different aspects of the distributions. No consistent corrected timing increase or allocation increase is established; intervals do not prove equivalence or exclude small costs. These are synthetic Windows x64 shared-machine measurements with tiering disabled, not live event-provider, macOS or whole-app profiling.

The initial call-based version showed a **7.40%** paired repeated-clock increase (interval **1.054–1.094**), with unchanged allocations and matching outputs. It was rejected as a performance result. JIT output showed a non-inlined `Place` call. One measured `AggressiveInlining` hint removes it; corrected `Locate` code is **1,438 bytes** versus **1,435 original** and **758 plus the call** initially. All 59 focused cases and the full 3,148-test gate pass with the hint. The initial 24 comparisons and their DLL/PDB/source remain `unhinted*`; corrected 24 comparisons are `perf-*.json` / `performance-summary.json`. No samples were retried, dropped or pooled across versions. Final full verification follows the corrected timings and confirms identical IL/implementation flags; it does not turn these measurements into an equivalence proof.

## Connection CSV refactoring (November 21, 2026)

`CsvExport.WriteConnections` keeps capture traversal and tuple-keyed owner lookup, delegating only endpoint row formatting to `WriteConnectionRow`. No file, flush, replacement, dictionary or traversal operation changes; no inlining hints are added. The [CRAP report](CRAP-REMEDIATION-2026-11-21.md#connection-csv-batch) records 33 new characterization cases, exact formatting equivalence, the unchanged cross-domain attribution issue and full verification. Artifacts, original/final libraries, paired harness and results are under `artifacts/crap/connection-csv/`.

Both workloads export 480 ordered endpoints across sixty owners, with IPv4/IPv6, TCP/UDP, absent lifetime tokens, absent remote/state fields, owner names requiring formula protection/CSV quoting, plus null and empty captures. In-memory formatting calls the original/final private traversal through identical compiled delegates, each with a fresh invariant `StringWriter`; hashing/materializing its text occurs outside timing. Public file export calls the complete atomic `Write`, including UTF-8 BOM, disk flush, temporary creation and replacement. It uses an isolated OS-temporary directory per process and equally long per-side filenames; full file bytes are fingerprinted outside timing. The harness's endpoint JSON converter is setup only. All twelve complete-output and exported API comparisons match. All 32 declared CSV method bodies and implementation flags match the full-test build; the timed library is byte-identical to the focused-test library.

Eight paired formatting processes and four paired public-file processes each use seven alternating batches, half reversing library loading/hash/warm-up/starting order. Tiered compilation is disabled; builds, tests and coverage analysis finish before timing. Formatting warms/calibrates thirty calls, then targets 400 ms with a common call count bounded at 10–100,000 (actual 1,292–1,423). File export warms/calibrates three calls, targets 200 ms and caps at fifty writes per batch to bound disk work (actual 38–49). There are **56 formatting / 28 file batches per side**. No samples or failed file operations were retried or dropped; all runs completed successfully.

| Workload | Original median | Final median | Allocation/call, original→final | Paired timing ratio (95% interval) |
|---|---:|---:|---:|---|
| In-memory formatting | 159.90 µs | 161.58 µs | 469,444→469,444 B | 1.000 (0.927–1.078) |
| Public atomic file export | 4,604.22 µs | 4,464.35 µs | 750,348→750,360.5 B | 0.964 (0.682–1.362) |

Intervals use each process's mean paired log timing ratio, with Student-t intervals across eight/four processes. The file allocation counts vary on both sides: original 750,296–750,680 B, final 750,296–750,630 B. The mean paired process allocation difference is **+6.96 B**, with interval **−59.56 to +73.48 B**; exact formatting allocations are unchanged. These measurements establish no consistent timing/allocation increase, while permitting a small cost. File-flush latency and runtime I/O allocation variation dominate precision. Synthetic Windows x64 shared-machine results do not establish production disk behavior, macOS behavior or whole-app performance. The earlier workspace atomic-move reliability observation remains open; this OS-temporary export set passed without retries.

## Connection query refactoring (November 21, 2026)

`ConnectionQuery` keeps executable/lifetime grouping order while separating current-rate accumulation and residual owner resolution. The [CRAP report](CRAP-REMEDIATION-2026-11-21.md#connection-query-batch) records the behavior matrix and verification. Original/final DLL/PDB/source, paired harness, JIT listings and all measurements are under `artifacts/crap/connection-query/`.

Both workloads use 60 owners, mostly four siblings per executable, with pathless owners, friendly names, cached metadata, explicit current/retained lifetimes and invalid/null directional rates. Endpoint summary processes 480 polled TCP endpoints. Flow grouping processes 496 UDP flows, including sixteen unknown lifetimes, without a polled endpoint frame. Calls execute the complete public `Build`, including grouping, summaries and sorting. Complete ordered results are fingerprinted outside timing; the harness represents IP endpoints as their address/port strings to avoid the default IPv4 `ScopeId` serializer failure. This is scratch setup only; no production serializer changed. Every original/final output and exported public API comparison matches.

Eight separate paired processes per workload, half reversing library loading/fingerprinting/warm-up/starting order, run seven alternating batches: **56 batches per side**. Tiered compilation is disabled. Thirty warm-up calls and thirty calibration calls select a common count targeting 400 ms, bounded at 10–100,000 calls. Final counts are 1,827–3,546 summary and 985–3,749 flow calls; one-pass calibration can produce shorter batches. Setup, reflection, JSON, hashes and current-set construction are outside timing. Final timing runs after coverage analysis has ended, without concurrent agent build/test/analysis jobs.

| Workload | Original median | Final median | Allocation/call, both | Paired ratio (95% interval) |
|---|---:|---:|---:|---|
| Endpoint summary | 102.61 µs | 96.65 µs | 119,752 B | 0.993 (0.942–1.048) |
| Flow grouping | 87.23 µs | 88.10 µs | 130,440 B | 1.012 (0.942–1.087) |

Intervals use the mean log ratio within each paired process and a Student-t interval across eight independent processes. Medians and paired ratios summarize different aspects of the distributions. No consistent final timing regression or allocation increase is established; the wide intervals do **not** prove equivalence or exclude a small cost. These are synthetic Windows x64 shared-machine measurements, not whole-app, native-provider or macOS profiling.

The first quiet version showed a **2.83%** paired flow increase (interval **1.001–1.056**), with unchanged allocations. JIT output identified non-inlined `NetworkRates.Add` and `Finish` calls in the hot aggregation path. Inlining hints were added to those two private helpers only, after measurement. Final JIT output removes both calls; summary code is 923 bytes versus 964 original and 783 in the call-based version. All 59 focused regression tests still pass. The call-based DLL/PDB and sixteen comparisons remain under `unhinted*`; its summary ratio was 1.008 (0.976–1.042). A separate preliminary sixteen-process set began while coverage analysis was running and is retained under `preliminary-*`; it is not combined with the quiet or corrected versions. Final sixteen-process results are `perf-*.json` / `performance-summary.json`. No timing samples were retried or dropped within a set.

## Incident timeline refactoring (November 21, 2026)

`IncidentTimeline` names complete-inventory selection and separates scalar projection from the exact-capture network join. No traversal, collection type, dependency or inlining hint was added. The [CRAP report](CRAP-REMEDIATION-2026-11-21.md#incident-timeline-batch) records 97 new cases passing against original/final libraries and final full Desktop verification. Original/final DLL/PDB/source, harness and results are under `artifacts/crap/incident-timeline/`. The network body is text-identical after dispatch (`projection-equivalence.json`); timed/full-test method IL matches in `verified-method-bodies.json`.

Lifecycle capture uses 240 one-second samples with 80 owners each, periodic missing/gap samples and PID lifetime changes every twenty frames. Usage history is populated outside timing. Network projection joins 240 sample processes plus ten duplicates, 180 captured owners and 180 cached owners from a 480-owner pool, exercising metadata precedence, cached-field scrubbing, foreign domains, missing/negative directions and explicit current-lifetime membership. Scalar projection cycles CPU, memory, primary/secondary I/O, GPU and process metrics over the same sample. Complete lifecycle key/start/stop payloads (including tuple fields) and complete ordered process-reading results are fingerprinted before timing. All **32** final comparisons match: sixteen lifecycle, eight network and eight scalar processes.

Final timing follows the strengthened tests, final full build and coverage analysis. Tiered compilation is disabled; half the processes in each scenario reverse library loading/fingerprinting/warm-up/starting order. Both sides use identical compiled delegates. Setup, Usage recording, JSON, reflection, lifetime-set construction and hashes are outside timing. Each side warms for thirty calls and calibrates over thirty; a common count targets 400 ms batches, bounded at 10–100,000 calls. Counts are 308–641 lifecycle, 3,438–4,210 network and 3,389–22,541 scalar. Seven alternating batches give 112 samples per side for lifecycle and 56 for network/scalar. One-pass calibration, especially scalar, can yield shorter batches than the target.

| Operation, per call | Before | After | Median allocation before / after |
| --- | ---: | ---: | ---: |
| Capture lifecycle transitions | 719.06 µs | 720.85 µs | 157,288 / 157,288 B |
| Network process readings | 82.93 µs | 78.02 µs | 160,032 / 160,032 B |
| Scalar process readings | 15.151 µs | 14.353 µs | 18,704 / 18,680 B |

The first eight lifecycle processes had medians 685.18→706.43 µs, with a wide paired ratio interval 0.970–1.154. Eight independent confirmation processes were added without production or harness changes; both batches are retained. The pooled sixteen-process paired ratio is 1.018, interval 0.968–1.071, so a consistent slowdown was not established and a small cost remains possible. Scalar projection saves 24 B per call by isolating its captured state; network/lifecycle allocations are unchanged. No claimed allocation gain is inferred solely from source structure.

Exploratory 95% paired after/before timing intervals are 0.968–1.071 lifecycle, 0.963–1.036 network and 0.915–0.993 scalar, using per-process mean paired log ratios and t intervals across sixteen/eight processes. Timing equivalence is not proven. These shared-machine synthetic measurements exclude UI rendering, live provider collection, filesystem/report work and whole-app profiling. Earlier performance/reliability follow-ups remain open.

An initial network benchmark setup failed before timing because `System.Text.Json` could not populate `IReadOnlySet`; setup now reconstructs the same 464 current identities in the loaded assembly's `HashSet`, validates its count, then fingerprints before measuring. No production serializer was changed. Earlier valid lifecycle runs remain `initial-lifecycle-*.json`; final raw files are `perf-*.json`, with first-batch `performance-initial-summary.json` and pooled `performance-summary.json`/`summarize.ps1`. Assertion review also enabled tuple fields in the test snapshot and reran original/final tests and full verification without further production edits.

## Session network-owner attribution refactoring (November 21, 2026)

`MonitorSession` separates metadata admission/refresh from retained-lifetime pruning and owner-frame construction. Production still captures one Stopwatch timestamp before touching cached state. An internal timestamp-taking overload controls expiry tests without changing the public constructor, clock units or adding provider state. The [CRAP report](CRAP-REMEDIATION-2026-11-21.md#session-network-owner-attribution-batch) distinguishes 16 original-binary public-flow cases from 35 timestamp-only boundary/cache cases and records passing full Desktop verification. Original, timestamp-only and final DLL/PDB/source plus the harness are under `artifacts/crap/network-owners/`.

Streams contain 120 owner-attachment calls with process and overlapping flow identities, changing metadata names/paths and preserved network fields. Steady uses 80 lifetimes, including empty-path fallback. Capacity first fills all 8,192 metadata slots, then updates 64 known lifetimes and presents ten rejected new lifetimes per frame. Both compare the original and final **production default paths**, including their real Stopwatch call. A separate expiry fixture advances supplied Stopwatch timestamps one second per frame, rotates identities and supplies metadata every 15 frames. Its reference is the **timestamp-only, otherwise text-identical core**, not the original binary; it isolates metadata extraction under deterministic pruning. Normalized-source equality is retained in `seam-equivalence.json`.

Complete returned `ProcessNetworkFrame` fingerprints include owners, metadata, readings/flows, quality and other fields. All **32** before/after comparisons match: eight steady, sixteen capacity and eight expiry. In each scenario half the processes reverse library loading, hashing, warm-up and starting order. Final declared method IL matches the full-test build in `verified-method-bodies.json`.

After full builds/tests/coverage analysis, tiered compilation is disabled and both libraries run through identical compiled delegates. Setup, JSON, reflection and hashing are excluded. Each side warms for ten streams, calibrates over twenty streams, then uses a common stream count targeting a 400 ms batch (10–5,000 bounds). Counts are 483–582 steady, 13–15 capacity and 616–772 expiry. Seven alternating batches yield 56 samples per side for steady/expiry and 112 for capacity. Session construction is included once per stream and amortized per call; its unused collector/dispatcher callbacks are not invoked. Fixture/model construction remains outside timing.

| Attach owners, per call | Reference | Before | After | Median allocation before / after |
| --- | --- | ---: | ---: | ---: |
| Steady | Original default | 5.464 µs | 5.217 µs | 887 / 887 B |
| Full metadata cache | Original default | 231.20 µs | 231.03 µs | 7,224 / 7,224 B |
| Controlled expiry | Timestamp-only core | 4.230 µs | 4.210 µs | 959 / 959 B |

The first eight capacity processes had medians 227.21→234.76 µs and a paired ratio interval 0.993–1.037. A second independent eight-process batch was run without production or harness changes. The pooled sixteen-process result does not reproduce a consistent slowdown: paired ratio 1.004, interval 0.991–1.017. Initial data and `performance-initial-summary.json` are retained alongside all final `perf-*.json` files and `performance-summary.json`; the confirmation did not replace the initial runs.

Exploratory 95% paired after/before timing intervals are 0.932–0.992 steady, 0.991–1.017 capacity and 0.969–1.005 expiry, using per-process mean paired log ratios and t intervals across eight/sixteen processes. No allocation increase or consistent slowdown was detected; these are synthetic shared-machine comparisons, not proof of performance equivalence. Original-binary real-time expiry cannot be accelerated without a seam; its boundary behavior is supported by unchanged-body inspection and timestamp-only tests. Native ETW collection, dispatcher stalls, whole-app profiling and macOS behavior are outside this measurement. Earlier performance/reliability follow-ups remain open.

## Capture-rule engine refactoring (November 21, 2026)

`CaptureRules` names target-process selection and network-rate accumulation, and evaluates scoped CPU core units alongside the other scoped units. No traversal, collection type, I/O or inlining hint was added. The [CRAP report](CRAP-REMEDIATION-2026-11-21.md#capture-rule-engine-batch) records 103 new characterization cases and passing full Desktop verification. Original/final libraries, symbols, harness and results are under `artifacts/crap/capture-rules/`. Timed/full-test declared method IL matches in `verified-method-bodies.json`.

Each stateful stream observes 180 samples with 80 processes. Values alternate between high and low phases, every 37th sample is a gap, rules require two qualifying samples, and cooldown is ten seconds. Application runs use CPU/memory/network rules matching `worker1.exe`; service runs use those three metrics for exclusive `svc1` with eight members; machine runs use CPU/memory/network and any-process rules. Service frame keys align with their full samples; traffic uses permitted provisional/incomplete quality. Complete fired results, including rule, firing/peak samples, peak value, run values and process attachment, are fingerprinted before timing. Every fixture must fire. All 24 final before/after comparisons match. Reference identity and prior-result stability are additionally checked in characterization tests.

Final runs follow full build/tests/coverage analysis. With tiered compilation disabled, eight processes per scenario use four normal and four reversed library loading/fingerprinting/warm-up/starting orders. Both sides use compiled delegates; setup, JSON, reflection and hashing are outside timing. Each side warms for ten streams, then calibrates over 40 streams; one common stream count targets 400 ms batches, capped at 5,000. Counts are 98–140 application, 336–434 service and 347–869 machine streams. Seven alternating batches give 56 samples per side. Service batches remain substantially shorter than the target because one-pass calibration costs differ from later steady execution. Timings include constructing one engine per stream, normalized per observation.

| Observe, per sample | Before | After | Median allocation before / after |
| --- | ---: | ---: | ---: |
| Application rules | 13.775 µs | 13.912 µs | 1,487 / 1,487 B |
| Service rules | 0.687 µs | 0.690 µs | 1,335 / 1,335 B |
| Machine/process rules | 2.825 µs | 2.756 µs | 216 / 216 B |

Exploratory 95% paired after/before timing ratio intervals are 0.913–1.066 application, 0.879–1.015 service and 0.930–1.024 machine, using per-process mean paired log ratios and a t interval across eight processes. No consistent slowdown was detected; all include 1 and do not prove equivalence. The application median is about 1% higher, so a small cost remains possible. Shared-machine scheduling/GC and shorter service batches limit precision. These measurements exclude native sampling, report writes, notifications, UI refresh and whole-app profiling. Earlier trend-ingestion/legacy-reader follow-ups remain open.

The first pass used only three warm-up streams and fixed 30-stream batches: approximately 15 ms for machine rules and less for services. It flagged a machine-rule slowdown (paired ratio 1.231, interval 1.105–1.372). That result was investigated by lengthening warm-up/batches without changing production code; the final longer machine batches did not reproduce it. The exact short-run cause was not established. Preserve both sets: initial `short-*.json`, `performance-short-summary.json`, `bench-short.cs`; final `perf-*.json`, `performance-summary.json`, `bench/`. No results were suppressed or substituted with a code-tuned benchmark.

## Connection-traffic totals refactoring (November 21, 2026)

`ConnectionTrafficTotals.Build` names its existing history-selection and counter-accumulation stages. Clock conversion, traversal order, lifetime identity, quality handling, subtraction and duplicate behavior are unchanged. No inlining hints or dependencies were added. The [CRAP report](CRAP-REMEDIATION-2026-11-21.md#connection-traffic-totals-batch) records 79 new characterization cases and final full Desktop verification. Original/final libraries, symbols, harness and raw results are under `artifacts/crap/connection-totals/`.

The fixture has 300 one-second captures and 240 process readings per capture. Fourteen lifetimes change after frame 250, frame 270 is a gap, and the end includes a reset duplicate and an invalid duplicate after valid readings. The interval scenario requests 60 seconds; cumulative requests zero; unavailable uses the same end with unavailable quality. Complete result fingerprints include identities/domains, both byte counters, dictionary iteration order, covered seconds and availability/completeness flags, with tuple fields included explicitly. All 24 before/after fingerprints match. Fixture checks independently assert 240 contributions, expected coverage and flags for valid scenarios, and an empty unavailable result.

After final build/tests/coverage analysis finished, eight processes per scenario ran with tiered compilation disabled: four normal and four reversed library loading/fingerprinting/warm-up/starting order. Setup, JSON, reflection and fingerprints are outside timing; both libraries use identical compiled delegates. Available paths warm for 1,000 calls and unavailable for 10,000, then calibrate with 1,000 or 100,000 calls respectively. A common count per process targets a 300 ms batch, with 25,000/3,000,000 caps. Actual counts were 11,581–19,620 interval, 8,881–25,000 cumulative and 1,628,160–2,065,831 unavailable. Seven alternating batches give 56 samples per side. The nanosecond path's one-pass calibration yields substantially shorter measured batches than the target and should be treated as a noisy microbenchmark.

| Build, per call | Before | After | Median allocation before / after |
| --- | ---: | ---: | ---: |
| 60-second interval | 19.54 µs | 18.77 µs | 63,680 / 63,680 B |
| Cumulative | 9.59 µs | 9.43 µs | 35,112 / 35,112 B |
| Unavailable | 14.512 ns | 12.684 ns | 144 / 120 B |

The original captured `Any` predicate's closure was created at entry; after extraction it is created only inside accumulation. Unavailable input returns before that stage, removing 24 B from that path. The available paths retain that allocation. There is no new collection or traversal.

Exploratory 95% paired after/before timing ratio intervals are 0.959–1.019 interval, 0.888–1.037 cumulative and 0.787–0.884 unavailable. They use per-process mean paired log ratios and a t interval across eight processes. No slowdown was detected; available-path timing equivalence is not established, and the unavailable improvement is specific to this synthetic fixture. Measurements exclude native collection, UI refresh, report rendering and whole-app profiling. Earlier trend-ingestion and legacy-reader performance follow-ups remain open. Raw files are `perf-*.json`; `performance-summary.json` and `summarize.ps1` retain the aggregation. Timed/full-test declared method IL matches in `verified-method-bodies.json`.

## Service-tree capture refactoring (November 21, 2026)

`ServiceTree.Capture` names its existing indexing, ownership, member-collection and group-materialization stages. The traversals, pooled lists, lifetime rules and cache swaps remain in their original order, with no new allocation sites or inlining hints. The [CRAP report](CRAP-REMEDIATION-2026-11-21.md#service-tree-capture-batch) records 35 new characterization cases and passing full Desktop verification. Original/final libraries, symbols and the standalone harness are under `artifacts/crap/service-tree/`.

Each stream captures 120 frames of 800 processes and 40 service observations, with one catalog reused by reference throughout. Steady load has 36 live hosts and four stopped services. Mixed load periodically removes five hosts, changes creation tokens and inserts gaps, exercising pending groups, cache invalidation and recovery. Complete serialized frame fingerprints match in all 16 original/final comparisons. Separate characterization tests check reference reuse and earlier-frame stability; value fingerprints alone cannot establish those contracts.

After full builds/tests/coverage analysis finished, eight processes per scenario ran with tiered compilation disabled, four normal and four reversed library loading/fingerprinting/warm-up/starting order. Both sides use identical compiled delegates; setup, JSON and fingerprinting are outside measurement. Each side warms for ten streams, then seven alternating batches execute 40 streams (4,800 captures) each. Timings include new `ServiceTree` construction once per stream, normalized per capture. Results pool 56 batches per side.

| Capture, per call | Before | After | Median allocation before / after |
| --- | ---: | ---: | ---: |
| Steady | 71.54 µs | 69.47 µs | 11,033 / 11,033 B |
| Mixed | 69.36 µs | 66.21 µs | 11,150 / 11,150 B |

Exploratory 95% paired after/before ratio intervals are 0.898–1.111 steady and 0.902–1.016 mixed, using per-process mean paired log ratios and a t interval across eight processes. No regression was detected; broad intervals mean performance equivalence and a speedup are not established. Minor allocation-counter variation is retained in raw batches; there is no new allocation site. These are synthetic shared-machine portable-library measurements, excluding native service enumeration, UI refresh and whole-app profiling. They do not close earlier trend-ingestion/legacy-reader performance follow-ups. Raw results are `perf-*.json`, summarized in `performance-summary.json`; the reproducible harness and summarizer are retained alongside them.

## Restart-history writer refactoring (November 21, 2026)

`RestartHistory.Save` names its unchanged sample/usage compatibility preflight. Serialization, deadlines, size checks, flushing, disposal, replacement and cleanup are unchanged. The preflight remains outside the catch and before clock/path/file work. The [CRAP report](CRAP-REMEDIATION-2026-11-21.md#restart-history-writer-batch) records 42 new cases, known exception behavior and passing Desktop/Store gates. Libraries, symbols and harness are under `artifacts/crap/restart-save/` (`before.dll`, `after.dll`, `bench/`).

The fixture has 130 samples and two processes per sample, with resource readings and logical CPU values. Plain saves omit usage/investigation; metadata saves include usage and aligned investigation owners. Rejection sets the first sample's independent-reading flag and preserves an existing destination marker. Every process compares complete output bytes and checks successful files restore all 130 samples with the expected usage presence. All 24 original/final byte fingerprints match.

With tiered compilation disabled, both libraries run through identical compiled delegates. Successful timings include real file creation, compression, durable flush, disposal and replacement of an existing file in owned per-process OS temporary directories. Fixture construction, assembly loading, JSON, byte checks and restoration are excluded. Each side warms for five calls, followed by five calibration calls per side; one common count per process targets a 300 ms batch, capped at 1,000 successful saves or five million rejections. Seven batches alternate order. Eight processes per scenario include four normal and four reversed library loading, hashing, warm-up and starting order. Counts were 67–74 plain, 53–68 metadata and 882,353–5,000,000 rejection calls; the nanosecond path's cap/calibration makes those batches shorter and noisier. Final values pool 56 batches per side after final build, tests and coverage analysis finished.

| Save, per call | Before | After | Median allocation before / after |
| --- | ---: | ---: | ---: |
| Plain | 4,183.71 µs | 4,223.07 µs | 95,000 / 94,984 B |
| With usage/investigation | 4,838.71 µs | 4,806.91 µs | 257,728 / 257,712 B |
| Unsupported metadata | 16.284 ns | 16.531 ns | 40 / 40 B |

The initial extraction added about 1 ns median on rejection (15.986→16.976 ns), with a paired ratio interval excluding 1. One preflight inlining hint reduced that difference to approximately 0.23 ns in a follow-up and 0.25 ns in the final comparison. No new allocation sites were introduced; successful median allocation decreases by 16 B. Small per-batch allocation-counter variations are retained in the raw data rather than presented as exact totals for every successful save. The pre-hint library is `pre-inline.dll`.

Exploratory final 95% after/before intervals are 0.979–1.038 plain, 0.969–1.008 metadata and 0.948–1.070 rejection. They use per-process mean paired log ratios and a t interval across eight processes. All include 1; performance equivalence is not proven. These are synthetic shared-machine filesystem measurements, not whole-app upgrade, near-deadline, power-loss, cross-platform or different-volume acceptance.

Initial rapid-replacement runs in the workspace intermittently failed in the original library with `UnauthorizedAccessException` at `File.Move`, inside and outside the sandbox; one workspace diagnostic passed. The cause was not established. Final runs match integration-test practice by using OS temporary directories outside the sandbox, with no retries and no failures. Failed traces remain in `bench-runs/` and `failed-*.json`; final traces are in `io-traces/`, with work-directory paths in the results. Do not infer that workspace/volume replacement reliability was fixed. Final raw data are `perf-final-*.json`; summaries are `performance-final-summary.json` and `performance-final-paired.json`.

## Restart-history reader refactoring (November 21, 2026)

`RestartHistory.Read(Stream, ...)` separates header/clock validation, trailer integrity, chunk restoration and recoverable optional metadata. Caller stream ownership, logging, format versions, limits, deadlines and corruption recovery remain unchanged. No parser state object, collection or I/O was added, and no new inlining hints were used. The [CRAP report](CRAP-REMEDIATION-2026-11-21.md#restart-history-reader-batch) records 65 new cases and passing full Desktop/Store verification. Preserved uninstrumented libraries and the harness are under `artifacts/crap/restart-read/` (`before.dll`, `after.dll`, `bench/`).

Three fixtures exercise public stream reading: a current-format file with 130 samples, two processes per sample, usage and aligned investigation metadata; the retained v1 fixture with 75 samples and usage; and the current file with a bad ledger CRC plus a bad second sample-chunk CRC, retaining the newest 60 samples and their metadata. Complete public contents and each aligned internal investigation frame are hashed before timing. All 24 original/final fingerprints match; expected sample counts and caller stream ownership are checked too. Reflection is used only for fixture setup and fingerprinting, outside measurement.

The harness calls both libraries through identical compiled delegates, with tiered compilation disabled. Each read creates/disposes its own read-only MemoryStream over the same bytes; construction/disposal and parsing allocations are included. Filesystem reads, fixture writing, assembly loading, serialization and hashing are excluded. Fixed injected clocks prevent wall-time cutoffs from changing the workload. Each side warms for ten reads, followed by ten calibration reads per side; one common call count per process targets a 300 ms batch. Seven measured batches alternate side order. Four initial processes per scenario and four controls with library loading, fingerprinting, warm-up, calibration and starting batch order reversed ran after build, tests and coverage analysis finished. Call counts were 126–142 current, 391–430 legacy and 273–307 damaged. Values below pool 56 batches per side.

| Stream Read, per file | Before | After | Allocation before / after |
| --- | ---: | ---: | ---: |
| Current, with investigation | 2,069.72 µs | 2,040.93 µs | 737,152 / 737,152 B |
| Retained v1 | 675.53 µs | 687.48 µs | 232,352 / 232,352 B |
| Damaged current | 931.33 µs | 939.57 µs | 340,792 / 340,792 B |

Allocation is identical in every measured batch. The initial four-process legacy comparison had a paired ratio interval of 1.004–1.020, prompting the reversed-order control rather than a production-code change. The control's interval was 0.950–1.073. Pooling all eight processes gives exploratory 95% intervals of 0.959–1.011 current, 0.990–1.032 legacy and 0.981–1.022 damaged. These use per-process mean paired log ratios and a t interval across eight processes. The legacy median remains 1.8% higher; the pooled evidence does not establish zero regression, and a small legacy cost remains possible. An isolated-machine performance follow-up remains open before closing the global no-regression objective.

This measures in-memory parsing on a shared desktop, not disk latency, near-deadline large restores, whole-app upgrade/restart time, macOS or different hardware. Both initial and control observations remain in `perf-*.json` / `perf-reverse-*.json`; combined summaries are `performance-combined-summary.json` and `performance-combined-paired.json`.

## Resource-trend ingestion refactoring (November 21, 2026)

`ResourceTrendArchive.Add` keeps interval validation at entry and delegates clock advancement/quarantine and UTC-minute accumulation; `MutableBucket.Add` names live numeric bounds. Device continuity retains its distinct eligibility and is calculated once per reading. No extra collections, resource enumeration or I/O were introduced. The [CRAP report](CRAP-REMEDIATION-2026-11-21.md#resource-trend-ingestion-batch) records 102 characterization cases, preserved edge behavior and two passing full verification runs. Original/final libraries are `artifacts/crap/trend-ingestion/before.dll` / `after.dll`.

The fixture contains 600 process-free samples and all twelve resource metrics. Steady samples have one-second intervals and stable devices. Split samples use five-second intervals offset across UTC minute boundaries. Mixed samples add backward/forward wall-clock changes, monotonic time, collection resets, gaps, invalid intervals, unavailable/nonfinite/out-of-range readings, and changing device IDs/labels. Each process checks complete serialized final archive snapshots before timing; fingerprints match original/final code in all twelve final runs. This checks final retained state, not every intermediate snapshot; characterization tests cover transient boundaries and exceptions separately.

Both libraries run through identical compiled delegates in separate assembly load contexts. Tiered compilation is disabled. Each side warms for ten complete streams, then alternates order across seven measured batches of 1,000 streams (600,000 additions). Archive construction and stream resets are included and amortized per sample. Fixture creation, deserialization, hashing and snapshot serialization are excluded. Four processes per scenario ran after final build, verification and coverage analysis finished. Values below are medians of 28 batches per side.

| Add, per sample | Before | After | Rounded allocation before / after |
| --- | ---: | ---: | ---: |
| Steady | 0.971 µs | 0.998 µs | 1,321.93 / 1,321.93 B |
| Minute splits | 1.301 µs | 1.303 µs | 1,488.60 / 1,488.60 B |
| Mixed | 1.271 µs | 1.349 µs | 1,322.94 / 1,322.94 B |

An initial extraction had separate interval-validation and per-reading-update helpers; a longer split comparison showed a 4% median increase. Inlining hints did not clearly remove it. The final smaller extraction removes those two helper boundaries and adds no new inlining hints; existing persistence-validator hints are unchanged. Its focused split follow-up was 1.356→1.337 µs, and the final split comparison above is nearly equal. Intermediate libraries (`split-initial.dll`, `inlining-prototype.dll`) and short/intermediate results remain available; they were not discarded or substituted for the final comparison.

Timing does not establish equivalence: the final mixed median is 6.1% higher, while exploratory 95% paired after/before intervals are 0.961–1.057 (steady), 0.908–1.087 (split) and 0.907–1.189 (mixed). These use each process's mean paired log ratio and a t interval across four processes, as in earlier entries. All cross 1; a small slowdown remains possible. Allocation medians agree to 0.01 B/sample; raw ranges overlap and vary by less than 0.002 B/sample, so the table does not claim byte-for-byte equality. Code inspection finds no new allocation sites. This is a shared-desktop microbenchmark of ingestion, not a whole-app, seven-day-retention or cross-platform profile. Final raw data are `perf-final-*.json`; summaries are `performance-final-summary.json` and `performance-final-paired.json`.

## Process grouping refactoring (November 21, 2026)

`ProcessRanking.Group` now uses a private value-type accumulator for current memory, counts and window ownership during the original member traversal. Historical activity, CPU/GPU representatives, sorting and overflow semantics remain unchanged. The baseline is the completed Rank batch; preserved libraries and the reused harness are in `artifacts/crap/process-grouping/` (`before.dll`, `after.dll`, `bench/`). The [CRAP report](CRAP-REMEDIATION-2026-11-21.md#process-grouping-batch) records 33 characterization cases and full verification.

Reused the preceding ranking fixture: five samples, 850 lifetimes, 729 latest processes and 86 executable groups, with exited members, shared paths, zero readings and unavailable metrics. Each scenario cycles through all six sorts. Complete serialized ranking fingerprints match original/refactored outputs for every sort in all twelve runs.

With tiered compilation disabled, each library warms for 1,000 calls. Seven measured batches alternate library order, each with 2,400 calls (400 per sort). Four processes per scenario ran after full verification and score computation finished. Fixture creation, assembly loading, JSON and hashing are excluded from timing; the same compiled delegates invoke both libraries. Medians below cover 28 batches per side.

| Rank scenario | Before | After | Allocation before / after |
| --- | ---: | ---: | ---: |
| Individual, active | 256.53 µs | 252.09 µs | 223,866 / 223,866 B |
| Grouped, active | 229.54 µs | 219.04 µs | 32,540 / 32,540 B |
| Individual, include inactive | 253.94 µs | 254.87 µs | 269,200 / 269,200 B |

Allocation is identical in every measured batch. No timing change is resolved: exploratory 95% intervals for paired after/before ratios are 0.917–1.054, 0.876–1.049 and 0.868–1.111 respectively. As in the preceding entry, these use per-process mean paired log ratios and a t interval across four processes. They describe shared-desktop noise, not proven equivalence or speedup. A small slowdown remains possible; individual modes are controls that bypass the changed grouping code. No new inlining hints were needed. Raw `perf-long-*.json` and `performance-summary.json` / `performance-paired.json` retain the results. Whole-app/UI profiling, macOS and different hardware were not measured.

## Process ranking refactoring (November 21, 2026)

Separated `ProcessRanking.Rank` accumulation, row construction, grouped-member insertion, ordering and scratch cleanup. The baseline includes the completed process-network batches. Original/final libraries and the standalone harness are preserved under `artifacts/crap/process-ranking/` as `before.dll`, `after.dll` and `bench/`. The [CRAP report](CRAP-REMEDIATION-2026-11-21.md#process-ranking-batch) records behavioral evidence and final scores.

The deterministic fixture has five samples, 850 process lifetimes, 729 processes in the latest sample and 86 executable groups. It mixes unavailable CPU, zero readings, nullable memory/I/O/GPU/thread/handle metrics, exited lifetimes and shared executable paths. Each scenario cycles through all six sort columns. Individual and grouped scenarios exclude inactive rows; the third includes inactive individual rows. Serialized complete ranking fingerprints match before/after for every sort in all twelve final runs.

Both libraries run in one process through identical compiled delegates, with tiered compilation disabled. Each side warms for 1,000 calls, then alternates order across seven measured batches of 2,400 calls (400 per sort). Fixture construction, serialization, hashing and assembly loading are excluded. Four processes per scenario ran after build, full verification and coverage analysis completed. Values below are medians of 28 batches per side; thread allocation is per call.

| Rank scenario | Before | After | Allocation before / after |
| --- | ---: | ---: | ---: |
| Individual, active | 259.05 µs | 249.00 µs | 223,866 / 223,866 B |
| Grouped, active | 221.87 µs | 214.07 µs | 32,540 / 32,540 B |
| Individual, include inactive | 267.46 µs | 257.52 µs | 269,200 / 269,200 B |

The initial extraction's short grouped comparison was 222.22→236.36 µs. To limit loop overhead, the final implementation keeps the CPU sum local with a value-tuple return and hints that per-member insertion should inline; the readonly totals argument also avoids a struct copy. The final short batches remained noisy, so the final comparison above uses ten times as many calls per batch. Short, intermediate and final observations remain in the artifacts rather than being discarded.

No allocation increase occurs in any final long batch. Timing does not establish a speedup or a slowdown: exploratory 95% intervals for paired after/before ratios are 0.859–1.101 (individual), 0.902–1.058 (grouped), and 0.906–1.053 (include inactive). These use each process's mean paired log ratio, then a t interval across four processes; they are uncertainty estimates from a shared desktop, not a formal equivalence test. A small timing regression remains possible. This measures the portable ranking workload, not UI rendering, whole-app sampling, hardware-specific behavior or macOS. Final raw results are `perf-long-*.json`; summaries are `performance-long-summary.json` and `performance-long-paired.json`.

## Flow validation and frame refactoring (November 21, 2026)

Extracted per-flow validation from `ProcessNetworkSession.AddFlows` and flow-specific rate eligibility/quality from `Frame`. The original library is the completed Add-entry-point batch; the final library includes one `AggressiveInlining` hint on the per-flow predicate. Assemblies are preserved in `artifacts/crap/network-flows/before.dll` and `after.dll`. No accounting, retention, model or public API behavior changed; the [CRAP report](CRAP-REMEDIATION-2026-11-21.md#flow-validation-and-frame-batch) records characterization and an existing null-endpoint exception.

Reused the prior session harness, adding a mixed-quality stream. Each 600-snapshot fixture contains 100 processes and up to 100 endpoint flows. Steady and interrupted fixtures retain the prior generation/subscription, sequence, loss and counter-regression scenarios. Mixed quality also varies unavailable/warming-up/incomplete/capacity flags, unsupported/missing/empty flow collections and rejected negative counters. Before timing, every serialized output frame is hashed; all twelve final runs match original/refactored output fingerprints.

The harness loads both libraries in one process and calls public Add through identical compiled expression delegates. With tiered compilation disabled, each side warms for ten complete streams, then alternates order across seven measured batches of five streams (3,000 additions) per side. Reset is included, while fixture creation, JSON, hashing and assembly loading are excluded. Four processes per scenario ran after full verification and coverage analysis finished. Values are medians of 28 batches per side.

| Session Add, per snapshot | Before | After | Allocation before / after |
| --- | ---: | ---: | ---: |
| Steady | 30.89 µs | 31.41 µs | 53,605 / 53,605 B |
| Interrupted | 30.45 µs | 30.70 µs | 53,963 / 53,963 B |
| Mixed quality | 24.15 µs | 24.87 µs | 50,387 / 50,387 B |

Allocation is unchanged. Timing point estimates are +1.7%, +0.8% and +2.9%; individual batches overlap substantially. Exploratory resampling of 28 paired batch ratios gave intervals crossing 1 for every scenario: steady 0.961–1.153, interrupted 0.960–1.037, mixed quality 0.987–1.164. These correlated synthetic batches cannot prove an exact zero-regression guarantee; no timing slowdown is resolved above measurement noise, and a small regression remains possible.

Initial one-stream batches appeared to slow interrupted traffic, but longer batches did not reproduce that effect. A direct split-predicate comparison without/with its inlining hint measured 22.83/22.47 µs for mixed quality with identical output/allocation; this justified retaining that single hint without other production tuning. Final numbers above use the hinted implementation. Raw final JSON (`perf-final-*`), resampling data, exploratory runs and harness source are local artifacts under `artifacts/crap/network-flows/`.

This measures portable session accounting and frame creation, excluding ETW collection, dispatcher queueing, rendering, whole-app profiling and worst-case capacity throughput. Correctness tests separately cover the 8,192-flow cap and exact retention boundaries.

## Process network session refactoring (November 21, 2026)

Refactored `ProcessNetworkSession.Add` into snapshot validation/replay, continuity/quality handling, process accumulation and checkpointing. Original assembly: the completed persistence batch before any session production edits (`artifacts/crap/network-session/before.dll`). The public API and `MonitorSession` caller remain unchanged. There are no new native calls, I/O, synchronization or retained collections; the pair returned by the process helper is a value tuple.

The local harness in `artifacts/crap/network-session/bench/` loads original and changed assemblies in separate AssemblyLoadContexts in the same process. Identical compiled expression delegates call the public `Add` method without reflection invocation or per-call boxing. Each fixture contains 600 snapshots of 100 processes and 100 endpoint flows. The steady stream advances counters and service windows normally. The interrupted stream also changes generations and subscriptions, drops sequences/duplicates snapshots, regresses counters and increases loss counters. Every output frame is serialized and fed into SHA-256 before timing; all four runs of each scenario produce matching before/after fingerprints.

With tiered compilation disabled, each process warms both implementations for ten full streams, then alternates order over seven timed batches per implementation. Reset and 600 additions are included in each batch; fixture creation, JSON, hashing and assembly loading are excluded. Four processes per scenario ran after full verification and coverage analysis finished. Values are medians of 28 batches per side.

| Session Add, per snapshot | Before | After | Allocation before / after |
| --- | ---: | ---: | ---: |
| Steady stream | 45.41 µs | 46.48 µs | 53,605 / 53,605 B |
| Interrupted stream | 35.31 µs | 32.43 µs | 53,963 / 53,963 B |

Allocation is unchanged. Steady medians differ by +2.4%; interrupted medians differ by −8.2%. Individual batches overlap substantially: steady 30.01–131.60 / 30.60–176.01 µs, interrupted 28.15–105.94 / 27.68–111.69 µs. These measurements establish no consistent slowdown, but machine variation prevents an exact zero-regression guarantee. No production inlining hints or other performance changes were added for this batch. The measurement includes the existing downstream frame/flow logic, but excludes native ETW collection, dispatcher queueing, rendering and whole-app profiling. Raw JSON runs and preserved original library are under `artifacts/crap/network-session/`; see the [CRAP report](CRAP-REMEDIATION-2026-11-21.md#process-network-session-batch) for tests and scores.

## Persistence validation refactoring (November 21, 2026)

Split incident, investigation-history, Usage archive and resource-trend archive validation into private checks for their separate responsibilities. The before assembly is the clean `e33267b` MonCore build; the after assembly has the validation refactor. No native collection or UI code changed. See the [CRAP remediation report](CRAP-REMEDIATION-2026-11-21.md) for behavioral evidence and remaining work.

The local harness in `artifacts/crap/bench/` warms each validation delegate 1,000 times, then measures seven batches of 500 calls with tiered compilation disabled. Fixtures are a 60-observation incident with 20 owners and traffic readings per observation, a 35-day Usage archive with 20 app observations per day, and a 10,080-bucket trend with one CPU value per bucket. Incident validation also exercises the shared investigation-history section checks. Four alternating before/after process pairs ran after verification and coverage analysis finished. The trend comparison additionally loads the original and final assemblies in separate AssemblyLoadContexts in the same process, alternates batch order, and runs four processes to reduce scheduling/clock differences between separate launches. Its private static calls use identical reflection wrappers on both sides.

| Validation fixture | Before | After | Allocation per call, before / after |
| --- | ---: | ---: | ---: |
| Incident plus investigation sections | 102.11 µs | 104.10 µs | 165,960 / 165,960 B |
| 35-day Usage archive | 12.00 µs | 12.33 µs | 19,904 / 19,904 B |
| Seven-day trend, paired in one process | 704.43 µs | 721.84 µs | Approximately 1,290,253 / 1,290,253 B |

The incident/Usage figures are medians of four run medians. The paired trend figures are medians of 28 batches per assembly; bookkeeping varies by about one byte per reported call. Individual trend batches ranged from 481–900 µs before and 520–1,029 µs after. The median paired after/before ratio was 1.029; exploratory resampling of those 28 paired ratios gave a 95% interval of 0.956–1.201. That overlap does not establish a consistent slowdown, and it does not exclude a small timing regression. Three resource-trend predicate helpers have inlining hints to avoid unnecessary method boundaries inside the validation loop. Initial measurements concurrent with verification were too variable for a reliable comparison and were not used for the table.

There is no observed allocation increase. Timing medians are about 2–3% higher with substantial overlap, so performance evidence remains qualified rather than an exact zero-regression guarantee. These fixtures measure validation only, excluding compression, disk latency, native sampling, the WPF render thread and whole-app profiling. Raw runs, the paired-ratio calculation, harness source and preserved before/after assemblies are local artifacts under `artifacts/crap/`.

## Resource trend review fixes (November 21, 2026)

After fixing wall-clock jitter, startup-load/clear ordering and wide-chart continuity, reran `WinMon.Performance` on the interactive desktop after desktop/Store tests and capture smoke had finished. Native collection and logging were disabled. The archive ingestion fixture now supplies positive monotonic timestamps to exercise the real ordering path. Artifacts: `artifacts/trend-review-fixes/before-bench.json` (the original feature measurement) and `artifacts/trend-review-fixes/after-bench.json`.

| Trend operation | Before review fixes | After review fixes |
| --- | ---: | ---: |
| Accumulate one full resource sample | 0.0055 ms / 1,085 B | 0.0063 ms / 1,085 B |
| Seven-day render and selection, 1,000 pixels | 3.76 ms / 800,380 B | 4.33 ms / 800,380 B |
| 24-hour render and selection, 3,000 pixels | Not measured | 3.63 ms / 1,088,044 B |
| Retained archive state | 4,106,104 B | 4,106,104 B |
| Full immutable snapshot | 12.60 ms | 11.75 ms |

Routine ingestion remains well below the 1 ms budget, with unchanged allocation and retained state below 4 MiB. The ingestion inputs differ in monotonic-clock availability, so the timing difference is not a controlled estimate of the fix's cost. Seven-day render batches were 2.77–4.67 ms before and 3.44–4.47 ms after, with identical allocation and pixel hash; the nonalternated median shift is within that observed variation. The wide chart now draws a continuous trace across unused pixel columns; its separate measured cost remains bounded. No unexplained regression blocks these fixes. Save/read medians were 154.4/231.6 ms, with the same 125,011 B file. This is synthetic UI-thread and archive evidence; it excludes native collection, the WPF render thread and whole-app profiling.

## Longer resource trends (November 21, 2026)

Resource trends aggregate the existing live `MonitorSample` into bounded one-minute slots; they add no collector, service or process retention. The archive uses packed fixed metric slots and a shared bounded device table. The History view draws a frozen seven-day snapshot by screen column and changes selection through the real modeless window.

An isolated `HEAD` baseline (`7927b6b`) and the completed build were measured with `WinMon.Performance` on the same interactive desktop, each with 400 warm-up frames and seven 60-frame batches. Native collection was disabled. The runs were not alternated, so the existing-view timing differences below are directional evidence only; there is no regression signal. Artifacts: `C:\tmp\winmon-trends-baseline\artifacts\performance-trends\baseline\bench.json` and `artifacts/performance-trends/after/bench.json`.

| Existing UI refresh | Baseline | With trends |
| --- | ---: | ---: |
| Live | 5.44 ms / 553,189 B | 4.70 ms / 560,455 B |
| Historical | 2.65 ms / 144,582 B | 1.48 ms / 144,658 B |
| Grouped | 11.17 ms / 551,933 B | 7.34 ms / 551,933 B |
| Services | 11.26 ms / 1,024,512 B | 6.78 ms / 1,024,512 B |
| Compact | 3.40 ms / 346,444 B | 1.82 ms / 346,444 B |
| Connections network history | 6.32 ms / 1,258,936 B | 4.15 ms / 1,258,936 B |

The new ingestion path took a median 0.0055 ms and allocated 1,085 B per full resource sample. A fully populated seven-day archive (10,081 retained minute slots, 12 metrics) retained 4,106,104 B, below the 4 MiB budget; it saved as a 125,011 B compressed file, with median save/read times of 154.2/237.6 ms off the dispatcher. Snapshot and seven-day query were 12.6/9.7 ms. The real seven-day History graph plus minute selection rendered in 3.76 ms and allocated 800,380 B per selection; its pixel hash was `A7DF9D0DC3146AA86ECB305FB23D0B94CAE1E06E4FA0BB7719B37307B9C3FC4B`. This is a synthetic UI-thread benchmark, not native-collection, disk-speed or whole-app profiling.

## Network recording recovery (November 21, 2026)

`MonitorSession.Pending` now retains raw collection QPC so delayed UI processing cannot discard valid captured network rates or move a missing-snapshot recovery boundary. History and scoped capture rules accept recovered non-null rates while cumulative session totals remain marked incomplete.

Measured the shared session pipeline before/after with 100 synthetic processes and 100 traffic readings per capture, 600 retained frames, inline draining and a minimal loop interval. Each process ran one 1,000-frame warm-up session and seven timed 1,000-frame sessions. Four alternating before/after pairs ran after verification/smoke had finished, with tiered compilation disabled for both builds. Values below are medians of the four run medians.

| Session collection and drain | Before | After |
| --- | ---: | ---: |
| Time per capture | 33.81 µs | 33.04 µs |
| Run median range | 32.30–36.24 µs | 32.67–35.68 µs |
| Allocated bytes per capture | 36,262 | 36,270 |

The timing difference is within observed variation. The additional eight bytes retain the collection timestamp once per capture (normally once per second). No observed slowdown blocks the fix. This is a synthetic shared-session measurement; native collection, WPF rendering, CPU saturation and whole-app profiling were not measured. Probe source, retained baseline and raw results: `artifacts/network-recovery/`.

## History event mapping (November 20, 2026)

A review of the process details, process controls, memory pressure and Windows events features for UI-thread work found that live refresh and sampling were already measured and unchanged. The largest remaining cost was `IncidentEventMapper.Map`, which runs on the UI thread when History opens (once with no events, then with the read events), when a recording opens, and, before this change, again when an event between observations was selected. For each event it made three passes over every sample: nearest observation, shortest containing interval, then a repeated-interval check. It also built a lookup of every retained process even when no event named one.

`Map` now finds the nearest observation and the containing intervals in one pass, and checks only those intervals for disagreement. Timestamps are still not assumed sorted, so wall-clock jumps behave as before. The process lookup is built only when an event carries a PID and creation time. `FlightRecorderWindow.SelectEvent` no longer remaps every event, since `Select` already refreshes the event list.

The mapper was measured directly: one hour of history (3,600 samples, 139 processes each, at the 500,000-entry budget, with a gap every ten minutes) and evenly spaced events, a third with a process identity. There were 3 warm-up and 15 timed maps per count, over four alternating pairs of the pre-change and changed `MonCore.dll`.

| Events | Before | After | Allocated bytes/map |
| --- | ---: | ---: | ---: |
| 0 (History opening, before events arrive) | 6.10 ms | 0.00 ms | 143,837 → 77 |
| 218 (the earlier seven-day query's count) | 16.50 ms | 9.92 ms | 157,787 → 157,859 |
| 500 (the reader's limit) | 31.04 ms | 10.56 ms | 175,835 → 175,907 |

Values are medians of the four run medians. Opening History with 218 events drops from about 23 ms to 10 ms of mapping, and selecting an event between observations no longer maps at all. A randomized check, 300 histories with repeated, backward and forward wall-clock steps, gaps and partial process identities, produced byte-identical mappings from both builds. Removing the repeated-interval check fails `IncidentEventMapperTests`.

Also measured and not changed:

- The menu-time suspension snapshot (`WindowsProcessSuspension.IsSuspended`) takes about 4–9 ms and allocates 1.2–2.1 MB per process menu open on this machine (1.17 MB snapshot). Starting from the last needed size measured 4.5 → 4.7 ms (median of four alternating pairs, within noise), because the undersized first query fails quickly, so it was dropped.
- Memory pressure adds 0.057 ms and 1,456 bytes per sample on the sampling thread, as recorded below.

Limits: these are direct method timings on one machine, not window-level History timings, and whole-app profiling was not run. Evidence: `artifacts/tune-events/` (probe sources).

## Final process diagnostics live refresh (November 20, 2026)

Compared the retained process-controls build with the final memory/events build using `WinMon.Performance --live-only`: 850 synthetic processes, 400 warm-up frames, seven batches of 60, and four alternating pairs. Builds, tests and capture smoke were stopped during measurement; native collection was disabled by the benchmark.

| Live refresh | Before | After |
| --- | ---: | ---: |
| Median of four run medians | 4.970 ms | 4.918 ms |
| Run median range | 3.716–5.274 ms | 4.300–6.838 ms |
| Median allocated bytes/frame | 560,449 | 560,457 |

All eight pixel hashes matched. The 0.052 ms median difference is within observed variation; allocation is effectively unchanged. Event queries and mapping run only for frozen History, with no new reads in live refresh or sampling. Evidence: `artifacts/process-control-events-measurements/ui-summary.json`. A separate real seven-day event query returned 218 bounded events and formatted messages, with complete Application/System coverage, in 906 ms; this is one machine's on-demand latency rather than a cross-machine guarantee. Sampler costs for memory counters are recorded below. Whole-app profiling was not performed.

## Memory pressure counters (November 20, 2026)

Compared retained pre-memory and changed native samplers on Windows x64, .NET 10.0.15, using 850 synthetic process rows, 30 warm-up samples and 200 timed samples per run. Four alternating pairs reversed launch order. Native PDH counters were readable in every changed run; compression store was unavailable because the fixture has no compression process.

| Resource sampler | Before | After |
| --- | ---: | ---: |
| Median of four run medians | 1.553 ms | 1.610 ms |
| Run median range | 1.485–1.570 ms | 1.546–1.642 ms |
| Allocated bytes/sample | 196,453 | 197,909 |

The 0.057 ms median difference is smaller than the observed run variation, with overlapping ranges. The six-counter query adds 1,456 bytes/sample. The sampler keeps one query and performs no additional process opens. This focused worker measurement does not measure whole-app performance. Evidence: `artifacts/process-control-sampler-results/*-{baseline,changed}.json`; measurements ran outside the sandbox so PDH was accessible.

## Process controls and suspension status (November 20, 2026)

Compared the retained process-details build and the process-controls build with the existing `WinMon.Performance --live-only` workload: 850 synthetic processes, 400 warm-up frames and seven batches of 60. Four alternating pairs swapped launch order each pair, with diagnostic logging enabled. Native collection and control operations are outside this UI-thread measurement.

| Live refresh | Before | After |
| --- | ---: | ---: |
| Median of four run medians | 5.642 ms | 5.695 ms |
| Run median range | 4.957–5.950 ms | 5.193–6.296 ms |
| Median allocated bytes/frame | 560,457 | 560,430 |

All eight pixel hashes matched. The 0.053 ms difference is within the observed run variation; allocation is effectively unchanged. Suspension labels use cached observations and add no native reads to refresh or sampling. Evidence: `artifacts/process-control-measurements/ui-summary.json`, with individual JSON/PNG files beside it. This is a focused live Processes check, not whole-app profiling or a measurement of native action latency.

## Chart labels and sparkline reuse (November 20, 2026)

An allocation and thread-time trace of the UI benchmark (`dotnet-trace`, `gc-verbose` plus sampled thread time) showed two avoidable per-refresh costs. `CpuGraph.Label` cached `FormattedText`, but `DrawText` reshapes that text on every call, so each axis label still ran DirectWrite shaping each second. `ProcessGraphData.Build` scanned every process in all 120 visible samples for every refresh: about 11% of the live frame's UI-thread samples, and about half of the grouped Processes refresh. Labels now cache a frozen drawing of their shaped glyphs and draw it under a frozen translation. `ProcessGraphData.Reuse`, held by `ProcessesView`, keeps the previous refresh's rows: a row seen last time copies its values for the samples both windows share (matched by reference, so the substituted newest full sample is recomputed), and only the new samples are scanned. Groups also keep which samples observed them and redo the compaction idle fill each refresh, because a group's representative process, and so its start time, changes under the same key. Two sample loops in `CpuGraph` no longer allocate interface enumerators.

Compared preserved `main` (`artifacts/tune-baseline`) and changed (`artifacts/tune-changed`) builds of `WinMon.Performance` on Windows x64, .NET 10.0.15: four alternating pairs with launch order swapped each pair, medians of the four run medians. Every scenario's pixel hash matched `main` in every run.

| UI refresh scenario | Before ms | After ms | Before bytes | After bytes |
|---|---:|---:|---:|---:|
| Live | 4.61 | 4.91 | 603,249 | 560,381 |
| Small multiples | 2.95 | 2.77 | 573,943 | 519,711 |
| Heatmap | 2.97 | 2.99 | 618,798 | 564,346 |
| Focus | 3.13 | 2.99 | 592,747 | 538,602 |
| Historical | 1.27 | 1.09 | 183,849 | 144,798 |
| Grouped | 6.50 | 6.54 | 597,199 | 551,933 |
| Services | 6.53 | 6.22 | 1,061,664 | 1,023,988 |
| Compact | 1.60 | 1.45 | 385,493 | 346,444 |
| Pinned and selected | 4.20 | 3.55 | 974,204 | 931,184 |
| Pinned and selected, grouped | 7.91 | 7.82 | 777,270 | 731,504 |
| Grouped with tooltip open | 10.55 | 10.37 | 975,422 | 930,434 |
| Logical processors | 6.69 | 6.00 | 1,145,964 | 1,051,757 |
| Connections | 2.53 | 2.26 | 602,169 | 510,886 |
| Connections with network | 3.19 | 2.97 | 1,376,582 | 1,285,311 |
| Connections network history | 3.52 | 3.12 | 1,350,432 | 1,258,936 |
| Connections network history, pinned | 3.60 | 3.16 | 1,353,488 | 1,261,992 |

Allocation fell 4–16% in every UI scenario; `UsageLedger` and history save/read cases are unchanged. Live is the first, tiering-sensitive scenario: it measured 4.51 → 4.39 ms in an earlier round whose only difference was the first grouped design, which Live does not use, and 4.61 → 4.91 ms here, with run ranges overlapping (4.42–4.69 before, 4.43–5.03 after). Six alternating `--live-only` pairs with `DOTNET_TieredCompilation=0` measured 5.22 → 5.11 ms and 602,853 → 559,749 bytes, so the Live difference is tiering, not extra work. An earlier version reused grouped rows only while their representative process stayed the same; that rarely held, and grouped measured 6.65 → 7.08 ms in all four pairs, which led to the observed-sample design above.

This measures UI-thread refresh, layout and render submission with native sampling stopped; it does not measure the render thread, collection or whole-app CPU. Evidence: `artifacts/tune-Measure.ps1`, `artifacts/tune-run1` (first grouped design), `artifacts/tune-run2` (final, tabled above) and `artifacts/tune-notiering`, each with per-run JSON and PNGs.

## History traffic and incident files (November 19, 2026)

History traffic joining and incident serialization run only on explicit investigation actions. No collector, timer or sampling-loop work was added. `artifacts/incident-probe` contains a Release .NET 10 probe, the original checked-in CPU comparison/ranking implementation as `BaselineTimeline`, and `final-default.json` / `final-no-tiering.json`. The analysis workload has 600 one-second samples and 850 process lifetimes per sample; the network case adds aligned delayed provisional traffic for every lifetime. After 400 warm-up calls per action, four alternating baseline CPU / changed CPU / changed network runs each use seven batches of 60 calls. Values below are medians of the 28 batch means; allocations use the calling thread.

| Navigation analysis | Before CPU | After CPU | After network |
| --- | ---: | ---: | ---: |
| Default tiering, elapsed | 2.01 ms | 2.42 ms | 10.01 ms |
| Default tiering, allocated | 2,696,905 B | 2,697,273 B | 8,443,617 B |
| Tiering disabled, elapsed | 3.15 ms | 3.56 ms | 10.33 ms |

The traffic join builds lifetime maps and includes owners omitted by process compaction. CPU analysis retains its original process metrics rather than allocating traffic rows for every CPU observation; its additional dispatch/closure stage costs 368 bytes and about 0.41 ms per navigation in this mixed workload. This is an explicit on-demand cost, not a per-frame or sampling regression. Other verification activity was running on the machine, so absolute times are not quiet-machine latency guarantees. These measurements isolate analysis and exclude WPF formatting, layout, rendering and whole-app resource use.

A separate persistence workload saves 300 observations with 120 process rows each. Four measured saves/loads follow one warm-up round trip. Default-tiered medians were 331 ms save and 414 ms load; the file was 1,286,433 bytes. The tiering-disabled run measured 94 ms save and 200 ms load on the same data, illustrating sensitivity to machine activity and runtime state. These are new-operation costs, with no pre-feature persistence equivalent. File I/O runs off the dispatcher; normal shutdown waits for pending saves through the existing export tracking. File and decompression limits remain enforced independently.

## History period comparison analysis (November 19, 2026)

The on-demand History comparison adds three 10-second observed averages and up to 10 process changes when navigating or refreshing the frozen recording. It adds no sampling-loop or per-frame work. A Release .NET 10 probe in `artifacts/history-comparison-probe` used 600 retained one-second samples with 850 process lifetimes each, selecting the midpoint. The baseline executes the unchanged `IncidentTimeline.Contributors`; the changed action executes that same ranking plus `IncidentTimeline.Compare`. Both ran in the same process after 400 warm-up calls each, alternating four baseline/changed runs with seven 60-call batches per run. Results below are medians of the 28 batch means, measured after verification completed.

| Analysis per selection | Before: contributors | After: contributors + comparison |
| --- | ---: | ---: |
| Elapsed time | 0.021 ms | 1.154 ms |
| Thread allocation | 34,561 B | 2,696,905 B |

The extra approximately 1.13 ms and 2.54 MiB are the cost of examining 30 seconds of retained process observations and producing the requested comparison. Raw measurements and the reproducible probe are `artifacts/history-comparison-probe/results.json`, `Program.cs` and `Probe.csproj`. This isolates analysis; it does not include WPF formatting/layout/rendering, a whole-app profile or a full UI benchmark comparison. The capture smoke separately passed 35 renders, including both tabs in light/dark at normal/minimum sizes; those History renders were visually reviewed. No sampling or history-storage format changed.

## Dedicated sampling worker (November 19, 2026)

Measured the existing and changed `MonitorSession` with the real `WindowsFrameSource` on Windows x64, SDK 10.0.404, using the probe and preserved binaries under `artifacts/sampling-probe`. Each case ran four ten-second recordings at the default one-second interval. The machine was already CPU-saturated: observed total CPU averaged 99.96–100% across runs. The explicit-load case additionally ran twelve normal-priority spin workers. The starvation case limited the probe process's pool to one worker and queued a four-second blocking item after its third capture. This isolates pool delays without changing native collection or chart thresholds.

| Measurement across four runs | Before | After |
| --- | ---: | ---: |
| CPU load: longest interval | 1.071 s | 1.012 s |
| CPU load: intervals over 2.5 s | 0 | 0 |
| Pool starvation: longest interval | 4.289 s | 1.181 s |
| Pool starvation: median of each run's longest interval | 4.025 s | 1.013 s |
| Pool starvation: intervals over 2.5 s | 4 | 0 |

Raw results are `before-load.json`, `after-load.json`, `before-starve.json` and `after-starve.json`; `Program.cs` and `Probe.csproj` retain the probe. The before measurements preceded the after measurements, rather than alternating quiet-machine runs; verification also ran alongside the after probe. These are scheduling/coverage observations under load, not a throughput or allocation comparison. CPU saturation alone did not reproduce gaps in the baseline; controlled pool starvation did. No synthetic UI benchmark was run because it stops sampling. Blocked native reads, suspend and higher-priority workloads can still create genuine gaps.

The desktop smoke also completed at 100% observed CPU, including hidden/minimized recording and the blocked-dispatcher sampling check. Evidence is `artifacts/sampling-probe/smoke-drained/smoke.json`; generated PNGs were reviewed. Its quiet-observation cost was 0.42% of machine CPU over 18.8 seconds, without a corresponding preserved before run; this is not a sustained overhead comparison. The smoke must drain Background-priority capture callbacks before checking history, since its normal-priority test continuation can otherwise observe a queued frame before it is recorded.

## On-demand incident timeline (November 18, 2026)

Compared the preserved pre-change app against the History/What just happened? implementation on Windows x64, .NET 10.0.15. Four alternating before/after pairs ran the existing `WinMon.Performance --live-only` workload: 850 synthetic processes, 400 warm-up frames and seven batches of 60 frames. Both variants used `DOTNET_TieredCompilation=0`. Values below are medians of the four run medians. Reproduction and raw evidence are `artifacts/Measure-FlightRecorder.ps1`, `flight-{before,after}-quiet-*.json`/PNG and `flight-performance-summary.json`; the preserved baseline is under `artifacts/flight-baseline` and the changed build under `artifacts/verify/build`.

| Live UI refresh | Before | After |
| --- | ---: | ---: |
| Median milliseconds per frame | 12.61 | 11.92 |
| Median allocated bytes per frame | 602,853 | 602,853 |

Timing varied substantially between runs: 7.53–17.51 ms before and 6.66–32.85 ms after. These observations do not establish a speedup or a tight timing bound; allocation was identical in every pair, and the aggregate median did not regress. Each variant produced one consistent pixel hash across its four runs. Hashes intentionally differ between variants because the main title bar now contains History. The new window adds no sampler, timer or analysis to the one-second refresh; its work happens only when opening or navigating the recorder.

The focused on-demand lifecycle analysis separately used 588 retained observations with 850 stable process lifetimes per observation, approaching the existing 500,000-entry Usage budget. After five warm-up calls, seven calls to `IncidentTimeline.CaptureChanges` had a median of 23.76 ms and 377,520 allocated bytes. Reusing two identity sets reduced an initial implementation's measured allocation from 56,886,768 bytes. The harness asserts known empty lifecycle differences for these stable inventories. Source and results are `artifacts/flight-analysis-perf/{Program.cs,FlightAnalysis.csproj,results-final.json}`. This isolates lifecycle analysis; it does not measure full window creation, native collection, sustained whole-app CPU or other hardware. Functional and visual evidence is recorded in [the feature note](design/FLIGHT-RECORDER.md).

## Bounded captures and historical connection details (November 15, 2026)

Compared the preserved clean `main` app with the capture changes on Windows x64, .NET 10.0.15. A focused copy of `WinMon.Performance` uses the same synthetic 850-process session, 400 warm-up frames and seven batches of 60 frames. It measures Live, historical Processes, grouped Live and a historical Connections view after 121 traffic captures. Storage/Usage benchmark cases are excluded. Four alternating before/after pairs completed; values below are medians of the four run medians. `artifacts/capture-info/Prepare-FocusedBenchmark.ps1`, `Measure.ps1`, `Summarize.ps1`, `focused-before-*.json`, `focused-after-*.json` and `summary.json` retain the reproduction and raw evidence. Each build's assembly hash and each scenario's pixel hash stayed consistent across all four runs.

| UI refresh scenario | Before ms | After ms | Before bytes | After bytes |
|---|---:|---:|---:|---:|
| Live | 4.42 | 4.49 | 603,250 | 603,256 |
| Historical Processes | 1.25 | 1.20 | 180,934 | 183,840 |
| Grouped Live | 7.02 | 7.14 | 605,391 | 603,624 |
| Historical Connections | 4.31 | 5.13 | 1,535,096 | 951,304 |

Live and grouped Live pixel hashes match the baseline. Historical hashes intentionally differ: Capture now displays ten grouped process rows for its copied report, limits connection apps to ten, includes paths and keeps pins within the cap. The historical Connections case therefore measures a changed workload, including the hidden Processes view used by Copy capture. It allocates 38.0% less but costs 0.82 ms more per refresh. Two additional alternating control pairs explicitly give both builds ten grouped process rows, ten connection rows and no extra pins (`--match-capture-shape`); the historical Connections medians are 4.90 ms / 949,456 bytes before and 5.20 ms / 950,760 bytes after. Matching the table shape narrows the timing gap to 0.30 ms. The remaining bounded cost accompanies the additional historical path text and Capture state handling; its individual methods were not profiled. The new historical refresh remains about 5 ms on this machine. The Live timing differences are 1.5% and 1.7%, with essentially unchanged allocation. These observations explain the changed Capture cost rather than treating it as a collector regression.

This measures UI-thread refresh, layout, rendering submission and allocation with native sampling stopped. It does not measure ETW collection, service overhead, the WPF render thread, sustained whole-app CPU or other hardware. Functional validation and screenshot evidence are in [the capture implementation record](design/CAPTURE-INFO-IMPLEMENTATION.md).

## MCP process trend metrics (November 15, 2026)

Compared the unchanged `HEAD` performance aggregate with the working implementation adding process GPU, thread and handle metrics, compiled together in Release on this Windows x64 development machine. This is an isolated aggregate measurement, not a native-collector, UI or end-to-end server profile. The reproduction helper, generated before/after sources and raw results are under `artifacts/mcp-gap-perf/` (`Measure.ps1`, `results.json`). Each window aggregates 60 synthetic one-second frames and materializes its summary and 60 output buckets. After 400 warm-up windows per variant, seven alternating batches of 1,000 windows measured elapsed time and current-thread allocation.

| Median observation per 60-frame window | Before | After |
|---|---:|---:|
| Elapsed time | 280.09 µs | 337.00 µs |
| Allocated bytes | 249,760 | 281,968 |

The three extra metric accumulators and their summaries add about 56.91 µs and 32,208 bytes per one-minute request (roughly 0.95 µs and 537 bytes per underlying observation). This explains the relative increase of about 20% in this small aggregate-only workload. Aggregation occurs only during bounded performance requests; no collector, background timer or retained-frame representation changed. Calendar Usage reads and lifetime history filtering also run only on demand. Full native MCP profiling and desktop UI benchmarks were not repeated for these MCP-only changes.

## On-demand MCP sampling (November 13, 2026)

Compared a preserved unchanged WinMon 4.1.2 MCP build with the changed server on this Windows x64 development machine, using SDK 10.0.404. Both builds initialized a real stdio connection, idled for ten seconds, answered a snapshot, sampled for ten seconds, waited another 25 seconds, measured ten idle seconds, and answered a resumed snapshot. Temporary children exited cleanly when their client input closed. Evidence and the measurement helper are under `artifacts/mcp-idle`: `before.json`, `after.json`, `after-final.json`, and the final runtime build's `after-verified.json`; `before/` preserves the baseline binaries.

| Observation | Before | After (final runtime build) |
|---|---:|---:|
| CPU during ten seconds after initialization | 1,078 ms | 94 ms |
| Private memory at that point | 41.8 MiB | 13.8 MiB |
| CPU during ten seconds after the idle deadline | 281 ms | 0 ms |
| First snapshot latency | 40 ms (already sampling) | 1,490 ms (new baseline) |
| Resumed snapshot latency | 3 ms (already sampling) | 1,140 ms (new baseline) |

Both snapshots returned `ok`. Two earlier changed-build runs also used zero measured CPU after the idle deadline, with resumed snapshots taking 1.09–1.15 seconds. Zero here is the process CPU counter's observation for a short window, not a guarantee of zero future CPU. Idle shutdown releases the native source and optional traffic subscription; it retains bounded frame history and Usage totals and does not force GC. Final post-idle private memory was 56.7 MiB versus the baseline's 63.6 MiB; retained managed memory is not expected to return to the initial process size.

The first active ten-second window used 422 ms CPU before and 750 ms after. These windows have different warm-up histories: the old server had already sampled during the preceding ten idle seconds, while the new server deferred initialization until the request. They do not establish a steady-state collection regression or speedup. The intended latency tradeoff is a fresh baseline on first use and after idle. The sampling loop, one-second cadence and per-frame publication path are unchanged; lifecycle work runs at request/idle boundaries. This is a bounded native idle/resume check, not a long-duration memory soak or a desktop/UI benchmark.

## Setup transaction test boundaries (November 12, 2026)

The setup helper gained per-run OS boundaries and an internal package factory so protected file transactions and named-pipe handshakes can be tested without changing the installed service. No sampling or UI path changed. SHA-256 checks confirmed that `WinMon.Performance.exe`, `WinMon.dll`, `MonCore.dll` and `MonCore.Windows.dll` were identical before and after the final rebuild.

The isolated, uninstrumented `--live-only --without-logging` UI benchmark used SDK 10.0.404/runtime 10.0.15, 850 synthetic processes, 400 warm-up frames and seven batches of 60. Median live-frame time was **3.936 → 4.019 ms** (+2.1%); median allocation was **603,253 → 603,233 bytes/frame**. Both runs show the same falling warm-up pattern, and the measured binaries were identical; this spread does not indicate a code-induced UI regression or speedup. Evidence: `artifacts/coverage-goal/setup-refactor-perf/before-idle.json`, `after.json` and `hash-comparison.json`. The earlier `before.json` overlapped a native workload and was discarded. This checks the unaffected desktop path, not native installation throughput.

## Scoped captures, CLI history and calendar Usage (November 11, 2026)

Measured on this Windows x64 development machine with SDK 10.0.404/runtime 10.0.15. The full UI benchmark uses the existing 850-process workload, 400 warm-up frames and seven batches of 60. The unchanged baseline assembly was preserved and its SHA-256 checked before the repeat. Evidence: `artifacts/gaps567-before.json`, `gaps567-baseline-recheck.json`, `gaps567-after-lazy.json` and the paired `gaps567-live-baseline.json` / `gaps567-live-after.json`.

An initial implementation constructed the calendar picker even when Usage was hidden. Its full-run Services median rose from 5.516 to 6.519 ms and selected/grouped from 7.271 to 8.101 ms. Deferring that template until calendar Usage is visible, and releasing its content while hidden, removed that increase. Final medians:

| Scenario | Baseline ms/frame | Final ms/frame |
|---|---:|---:|
| Services | 5.516 | 5.302 |
| Selected/grouped | 7.271 | 6.989 |
| Compact | 1.564 | 1.505 |
| Live, paired focused check | 4.088 | 4.206 |

The paired live check retained identical median allocation (603,181 bytes/frame). The first live case in the full run varied more (4.165 to 4.730 ms); its batches fell substantially during both runs. The paired repeat was +2.9%, with last-three-batch medians 3.681 and 3.723 ms. This supports warm-up variance as the explanation for the first-case spread, rather than a sustained Services/grouped regression. Other final full-run UI scenarios ranged from -12.4% to +3.6%; pixel hashes of existing views remained unchanged. These are synthetic UI-thread results, not whole-process or native collection measurements.

`UsageArchiveTests.MeasureArchiveOverhead` separately measures the new sampling work: 850 distinct apps, 400 warm-up additions, seven batches of 100. Existing ledger only: **0.0931 ms and 32 bytes/tick**. Ledger plus daily archive and three enabled app rules: **0.3357 ms and 1,112 bytes/tick**. The explained addition is **0.2426 ms and 1,080 bytes per one-second tick**. Span-based executable comparisons removed roughly 219 KiB/tick of avoidable name-string allocation in the initial implementation. Checkpoint serialization is off the UI thread and outside this per-tick measurement. CLI period retention uses the existing measured UsageHistory path; its observation budget can shorten available history.

Reproduce the focused check with `dotnet test tests/MonCore.Tests/MonCore.Tests.csproj -c Release -m:1 -p:UseSharedCompilation=false --filter FullyQualifiedName~MeasureArchiveOverhead --logger "console;verbosity=normal"`. Existing benchmark `--live-only` skips unrelated scenarios for paired live checks.

## Hardware inventory in the CLI and MCP (November 11, 2026)

Hardware collection is on demand and outside `MonitorSession`. The desktop and CLI share typed inventory and localized formatting; the CLI caches unchanged Hardware rows between refreshes. Measurements used this Windows/Hyper-V development machine, SDK 10.0.404/runtime 10.0.15. Local evidence is under `artifacts/hardware-performance`.

The preserved desktop baseline was built before switching Info to the shared reader. One full before/after UI benchmark used the existing 850-process workloads, 400 warm-up frames and seven batches of 60. All **16 rendered scenario hashes matched**. Historical-frame median was 1.335 ms before and after, with 180,857 bytes/frame. Connections with traffic history and pins was 3.869 → 3.896 ms, with unchanged 1,353,336 bytes/frame. Initial live/Services timing increased while other builds/tests were active, without increased allocation; these runs do not establish a performance regression or improvement.

Repeated live-only measurements disabled tiered compilation as the developer guide prescribes. Eight alternating pairs, reversing launch order halfway through, gave median live-frame time **5.004 → 5.069 ms** (+1.3%) and unchanged **602,813 bytes/frame**. Individual run medians overlapped (before 4.862–5.235 ms; after 4.787–5.549 ms). The changing order and spread support ordinary workstation timing variation; no new inventory work runs in the measured refresh path. This is a comparison with observed variance, not a speedup claim.

A separate immutable HEAD source export (`a9f294b`) supplied the CLI baseline. Seven alternating native `snapshot --format json --top 10` runs per build gave median elapsed time **1,576.5 → 1,559.0 ms**, including the required one-second sample interval, and process CPU **562.5 → 531.3 ms**. Live process populations vary, and the changed checkout also contains concurrent investigation commands; this does not isolate a hardware-only improvement. Peak working set was unavailable after these short-lived processes exited and is not reported as zero.

The published MCP server completed **120 sequential hardware reads**: first request **284.3 ms**, subsequent median **102.6 ms**. Server private bytes grew from 25.6 MiB during startup to 39.2 MiB after the series; the final 20 reads ranged 38.8–45.3 MiB with reclamation, rather than a monotonic retained rise. Eight additional hardware requests alongside a five-second performance sample completed successfully. Total server CPU for the run was 4.33 seconds; that excludes Windows provider-process CPU. Default identifier omission and native opt-in were checked separately. Single-worker/cancellation ownership and full 256 KiB wire bounds are covered by deterministic tests.

These short runs do not replace a long-duration cancellation/memory soak, physical-machine or multi-socket acceptance, or provider-specific performance testing. WMI/native calls remain non-interruptible while blocked; observed latency is not a hard deadline.

## MCP Usage publication (November 11, 2026)

Focused synthetic before/after measurement on this Windows x64 development machine: 850 distinct executable lifetimes, 400 warm-up additions, then seven batches of 100; median time/allocation per tick. `ActivityToolsTests.MeasureUsagePublication` compares the existing ledger-only operation against ledger plus its immutable Apps snapshot. Baseline: **0.0913 ms and 32 bytes/tick**. With snapshot: **0.1165 ms and 122,616 bytes/tick**, an explained increase of **0.0252 ms and 122,584 bytes** per one-second publication. The server keeps only the latest Usage snapshot, not 120 copies. Native collection, serialization and whole-process memory are outside this focused measurement; desktop/UI hot paths are unchanged.

Reproduce with `dotnet test tests/WinMon.Mcp.Tests/WinMon.Mcp.Tests.csproj -c Release -m:1 -p:UseSharedCompilation=false --filter FullyQualifiedName~MeasureUsagePublication --logger "console;verbosity=normal"`.

## Investigation continuity (November 11, 2026)

Compared untouched HEAD with the completed changes on this Windows x64 desktop, SDK 10.0.404/runtime 10.0.15, using the existing synthetic UI benchmark (400 warm-up frames and seven batches of 60). Services now retain configured history within their observation cap; presentation still passes only the two-minute row window. An initial implementation scanned the full retained list during refresh and increased Services work; bounded slicing removed that cost before the final run.

| Scenario | Before median ms | After median ms | Before bytes/frame | After bytes/frame |
| --- | ---: | ---: | ---: | ---: |
| Live | 4.449 | 4.372 | 603,181 | 603,181 |
| Historical | 1.020 | 0.970 | 180,729 | 180,793 |
| Services | 5.253 | 5.049 | 1,061,386 | 1,061,434 |
| Usage ledger and interval recording | 0.042 | 0.044 | 34,136 | 34,136 |
| Connections with retained traffic and pins | 3.147 | 3.126 | 1,353,336 | 1,353,336 |

All 16 rendered scenario hashes matched. Other scenarios varied modestly, with unchanged allocations; this is one sequential comparison, not a statistical claim of improvement. Longer service retention can consume more memory within its unchanged 120,000-observation limit. This UI benchmark does not measure native traffic collection or MCP server overhead.

The added investigation workload records 600 synthetic 850-process samples plus services, preserving uncompacted Usage intervals under their own 500,000-entry limit. Across five saves/reads, median normal save was **854 ms**, median read **1,406 ms**, and the compressed file was **24,632,276 bytes**. The final run restored all 600 resource samples; every run passed the nonempty resource and usable period Usage checks. The first read took 1,650 ms: deadlines are checked between chunks, so parsing a chunk can finish after the nominal read budget. Activity sources remain bounded and may cover less than the resource recording. Endpoint/flow round trips and reboot alignment are covered by deterministic tests, not this benchmark fixture.

Evidence: `artifacts/gaps-before.json`, `artifacts/gaps-final.json` and their PNGs. The original 60-minute resource-only workloads still restored all 1,961 retained samples; final median save/read were 1,028/764 ms for update history and 1,024/643 ms for normal history.

## Capture reports and endpoint-pair accounting (November 11, 2026)

Windows x64, SDK 10.0.404/runtime 10.0.15. Four alternating preserved-baseline/final 850-process live UI runs used the existing 400-frame warm-up and seven batches of 60. Median run medians were 4.123 ms before and 4.265 ms after. One first changed run was an outlier at 12.051 ms. Following the documented tiering check, four more alternating runs with `DOTNET_TieredCompilation=0` measured 5.002 ms before and 4.942 ms after, with exactly 602,812.5 allocated bytes/frame in every run. All pixel hashes matched. The controlled runs show no reproducible UI regression; the small timing difference is not a speed improvement claim. Reports are formatted on demand, and this workload does not measure report-copy latency, CLI query cost or native ETW collection.

The explicit ledger measurement runs 40,000 synthetic events against a process-only control and endpoint-aware accounting, after warming both paths, four alternating times. It deliberately visits 10,000 peer pairs to exercise the global 8,192-key cap while preserving process bytes. Median aggregation-plus-snapshot elapsed time was 0.855 ms without flow detail and 8.354 ms with it. Per-flow wire-budget calculation added a median 4.652 ms at 8,192 rows. Allocations for aggregation, snapshot and budget calculation were 2,080 versus 8,556,000 bytes, including endpoint strings constructed by this workload. The resulting flow snapshot JSON was 1,622,505 bytes; separate response-boundary tests include the outer response wrapper and verify deterministic truncation under 2 MiB. The benchmark uses elapsed time rather than process CPU and does not include native callback/address decoding, final pipe serialization/I/O, retained-heap profiling or sustained full-service load.

The measured workload fits the chosen 8,192-key and 2 MiB bounds; it does not establish a native collector CPU or memory budget. The endpoint-aware ETW queue retains its 262,144-event count bound and carries typed address references rather than formatting unverified events in callbacks. Its burst memory footprint needs elevated native profiling. Flow keys stay until the owning lifetime expires, rather than being recycled under a live process; cap omissions remain explicit and process totals continue. See the [implementation record](design/CAPTURE-INFO-IMPLEMENTATION.md) for that departure and native acceptance still required before release.

Evidence: `artifacts/capture-info-{baseline,final}-live-{1..4}.json`, `artifacts/capture-info-{baseline,final}-notiering-{1..4}.json` and `artifacts/network-flow-benchmark/ledger.json`. The explicit ledger measurement passed. No service was installed or replaced during these measurements.

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
