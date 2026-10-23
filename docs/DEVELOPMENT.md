# WinMon developer guide

[Back to WinMon](../README.md)

These detailed usage, implementation and validation notes were preserved from the previous README. Benchmark results and validation counts describe the recorded builds; some implementation descriptions reflect earlier iterations. Commands assume the repository root as the working directory.

A Windows resource monitor built with WPF on .NET 10. One view overlays CPU, physical memory, activity for up to two configured disk volumes, network throughput, and GPU utilization in one chart, above the busiest processes. Range chips switch the chart between 2, 10, 30 and 60 minutes, up to the retained history.

## Run

The Release distribution is a single optimized, self-contained Windows x64 executable at `src/WinMon/bin/Release/net10.0-windows/win-x64/publish/WinMon.exe`. Copy just that executable; no separate .NET installation is required. Bundled native libraries extract automatically when the application runs.

Requires Windows. Development requires the .NET 10 SDK.

```powershell
dotnet run --project src/WinMon/WinMon.csproj -c Release
```

The default is the selected dark resource-overlay design: green CPU, blue memory, orange disk, purple network, and cyan GPU. Toggle each metric's checkbox to hide/show its trace; recording continues for all metrics and visibility is remembered. The range chips under **System resources** (`2m · 10m · 30m · 60m`) choose how much history the chart shows; the choice is remembered, and chips longer than the retention setting are hidden. Widen the window past 800 px and process rows also show their PID. Click the graph to hold a historical sample; use Left/Right to move, Home for the oldest visible sample, End or **Return to live** to resume the live display. Escape also clears a historical selection. Top readouts remain live; the historical summary and process table show the selected timestamp. Hover shows resource values and transfer rates for that interval.

The centered **Live | Snapshot** switch above the chart selects the newest recorded sample without clicking the plot. **Snapshot** opens the same snapshot card and historical process view as a chart click; click it again to select the newest sample. **Live**, **Return to live**, or Escape clears the selection. Recording and the top readouts continue updating while a snapshot is held. The switch also follows chart clicks and keyboard navigation, and Snapshot is disabled until the first recorded sample arrives. Labels and tooltips follow the selected language.

**Copy snapshot** always exports a plain-text summary of the selected historical sample and the exact ranked, filtered top process rows from the displayed presentation, including the remainder row. Grouped rows use private bytes, individual rows use working sets, and missing values remain unavailable. The report distinguishes interval resource readings from five-second process averages, includes local timestamp/offset and units, and preserves the current process sort, search and app filter. Text columns are tab-separated and numbers use local decimal separators. Copying persists the text after exit, confirms success for two seconds, and leaves the selection open. Expiry disables copying. Clipboard contention displays the existing retry message. Snapshot smoke exports the report through a callback without changing the user's clipboard and checks footer containment and expiry.

The heading, Info, Settings and Pin share the title bar with the native Minimize, Maximize and Close buttons. Drag the heading to move the window; title-bar double-click and the system menu retain standard Windows behavior. WPF WindowChrome defines the caption/resize regions, with all three application actions receiving normal button input.

The compact System resources header puts time-range chips on the right; network scaling is labeled inside the chart. The process toolbar keeps ranking controls on the left, its explanatory note in the middle, and grouping on the right. Narrow windows shorten the note with an ellipsis; hover reveals its full text. Historical resource values appear only while inspecting a sample. Unattributed CPU appears directly beside the Process column heading, with full coverage information on hover. The process list extends to the bottom without a footer.

**Pin** keeps the window above other windows. With **Minimize to the notification area** enabled in Settings, minimizing hides the window; click its tray icon or choose Show WinMon to restore it. Press **Ctrl+Shift+M** from any app to bring WinMon to the front, and again to minimize it; the combination is configurable in Settings, and can optionally also start WinMon when it isn't running (this adds it to the Start menu). Starting WinMon again while it runs also brings the existing window back. Closing the window or choosing the tray Exit command always exits.

Settings include primary and optional second drive selectors, light/dark/system theme, retained history, minimize-to-tray behavior, the show shortcut, and opt-in current-user startup. Preferences are stored atomically in `%LOCALAPPDATA%\WinMon\settings.json`. Resource history stays in memory and is discarded on exit. Monitoring requires no elevation or network service.

### Portable settings

To keep preferences with the app, close WinMon and create an empty file named `portable.flag` beside `WinMon.exe`. On the next launch, WinMon loads and saves `settings.json` in that same folder, regardless of the working directory. Keep the executable, marker, and settings file together when moving or updating the app. The folder must be writable; portable mode does not fall back to the local application-data folder if saving fails.

Portable settings and the normal profile are separate. To carry existing preferences over, copy `%LOCALAPPDATA%\WinMon\settings.json` beside the executable while WinMon is closed. Without that copy, portable mode starts with default preferences. Remove `portable.flag` and restart to return to the normal profile. Windows startup registration and the optional Start menu shortcut remain machine-specific; disable them before moving the app and enable them again from its new location if needed.

**Info** opens an on-demand hardware snapshot: CPU model and Windows-reported physical/logical core counts, installed RAM, graphics adapters with dedicated video memory and shared-memory limits, local volume capacity/free space, and network adapters with status/link speed. Refresh updates the snapshot while recording continues. Values reflect what Windows exposes, including virtual hardware; unavailable sections are labeled. GPU memory here is capacity, not live VRAM usage.

Memory also shows OS-usable capacity, module count, firmware-reported slot usage, DDR type and configured speed. Expand **Module details** for each module's capacity, location, manufacturer, part number and reported/configured speeds. **Physical drives** is separate from **Local volumes**: it shows model, media type, connection, capacity and associated drive letters; expand **Drive details** for manufacturer and Windows-reported health. Identifiers must match unambiguously before media type is associated with an OS disk. RAID/virtual hardware, unavailable providers and missing firmware values can leave fields unavailable. Health is a Windows provider report, not a full SMART diagnostic. Inventory runs only when Info is opened or refreshed.

CPU adds populated sockets, cores per socket and reported maximum clock. Expand **CPU details** for each socket's model, manufacturer, architecture, physical/logical counts, L2/L3 cache and Windows-reported virtualization extensions and firmware enablement. Maximum clock comes from the [Windows processor inventory](https://learn.microsoft.com/windows/win32/cimwin32prov/win32-processor); it is a specification rather than live frequency or a measured boost clock. Missing specifications remain unavailable, and virtual machines may mask capabilities. Native model/core readings remain visible if the detailed provider fails.

**Copy** in Info puts a plain-text report on the clipboard: product/version details, snapshot timestamp and every hardware section, including collapsed CPU/module/drive details and unavailable values. It copies the displayed snapshot without querying hardware again. Copy is unavailable while loading or after a failed refresh; a busy clipboard produces a retry message. The copied text remains available after WinMon exits.

Right-click a live **Individual** process row and choose **End process** to request termination after confirming its name and PID. In **Grouped** mode, the themed confirmation lists all current members, including idle processes, before requesting termination for those exact lifetimes. New processes started after confirmation opens are excluded. Confirmation and error dialogs follow the application theme. Each member is checked separately; failures are collected while the remaining members are attempted. Unsaved work can be lost. The selected process's creation time is checked against the same native handle used to terminate it, preventing a reused PID from targeting another process. Historical, system and WinMon rows are disabled; groups containing protected or unidentified members are disabled; Windows-critical processes are refused. Access-denied and already-exited outcomes are reported without elevation or a process-tree kill.

**Settings → Chart palette** offers Classic, Ocean, Colorblind, High-Contrast & Inclusive, Sophisticated Pastel, Vibrant Monochromatic Shift, Cyberpunk / Neon, Warm Earth Tones, Cool Tech, Studio, Signal, Botanical, Spectrum, Neon, Arcade and Tropical independently of Light/Dark/System. The choice is remembered and applies consistently to resource tiles, overlay traces and process sparklines. Classic uses purple (`#9B4DCA`) for network in both themes. The thirteen additional schemes keep the same hex colors in both themes; their C:/D: colors follow the configured primary/secondary drives. Studio draws from Tableau 10, Signal from Okabe–Ito, Botanical from ColorBrewer Dark2 and Spectrum from Viridis. Neon, Arcade and Tropical are custom palettes; their tooltips describe their vivid cyan/pink/lime/violet, saturated primary/secondary, and bright warm/blue/teal directions respectively. Sparklines use the selected ranking metric's color (I/O uses disk); Colorblind also adds overlay line patterns. Windows high contrast retains system colors and line patterns.

In **Settings → Disk monitoring**, choose a primary volume and optionally a different second volume. The Windows system volume is the default; **None** keeps the original five-column view. Two volumes use six metric columns and a 720-DIP minimum width. Each disk has its own active-time percentage, read/write MB/s, visibility checkbox, and chart trace: orange solid for the primary, gold dashed for the second. Selecting an unavailable volume shows a dash and **Unavailable**, without disrupting the other readings. Selections and trace visibility are remembered; hiding a trace continues recording. Historical summaries and hover details retain each sample's volume names and values. Drive letters identify logical volumes, which may share a physical disk. Disk percentages are never added together.
## Measurement semantics

CPU, memory, disk, and GPU traces use a fixed 0–100% scale; network throughput uses its own visible-peak scale. Physical memory is used/total OS-visible physical RAM, with GiB readouts. Disk means active time (`100 − % Idle Time`) on each configured volume, with read/write throughput in decimal MB/s; it is not disk space used. Network measures one connected Ethernet/Wi-Fi adapter, preferring a gateway-bearing interface, then the fastest, and retaining that choice while connected. Its name and link speed are in the readout tooltip. The readout shows the busier direction's throughput, with receive (↓) and send (↑) below in adaptive decimal Kbps/Mbps/Gbps; utilization is the busier direction divided by reported link speed. This is link utilization, not a measurement of Internet bandwidth. Interface/speed changes, counter resets and long collection gaps require fresh rate baselines. Unavailable metrics show a dash and break the corresponding trace; CPU provider failure does not stop other resources.

GPU reports the busiest physical engine across available adapters. Process contributions are summed within each engine before selecting the busiest; independent engines and GPUs are never added together. Hover the GPU readout for the adapter identifier and engine type. Windows GPU Engine performance counters supply utilization, without vendor libraries. Missing counters, startup and fresh rate baselines show a dash. Existing settings enable GPU visibility by default, and its toggle is remembered. Live GPU memory usage and temperature are outside this basic view; Info shows dedicated memory capacity.

The process table defaults to CPU ranking; the selector switches it to GPU, Memory or I/O, marks the sorted column with ▾, and is remembered. Memory ranks by the current working set of processes present in the newest sample; I/O ranks by the five-second average read/write rate. Earlier GPU-sort settings carry over. **Individual | Grouped** (remembered) combines processes of the same executable into one row such as `chrome ×24`. Executables are identified by full path, so same-named programs in different folders stay apart; when a path cannot be read, the process name is used. A group sums CPU and I/O, reports the busiest member's GPU (engines are not additive), and shows the summed private bytes of its current members in a **Private** column, since working sets share pages. Its sparkline is the members' summed CPU, and the tooltip lists member PIDs. The live view ranks the newest sample in full, so member counts and memory are exact; in history, idle members compacted out of older samples are omitted. CPU and GPU columns use a five-second window. GPU is each process's busiest engine per interval, averaged over readable intervals; a missing counter or unverified process lifetime shows a dash, and a valid idle reading is zero. Per-process GPU values are not additive, so the Other processes GPU cell is a dash. Sparklines continue to show CPU history in both ranking modes. Each row shows an executable icon and a CPU history sparkline covering the last 2 minutes. All sparklines share one scale, from zero to the busiest listed process's visible peak with headroom (minimum upper limit 0.1%, capped at 100%), so heights compare across rows; hover a row to see the scale. Sparklines plot raw one-second CPU, while the CPU % column is the five-second average. Historical selection ends each sparkline at the selected interval. Icons load in the background and use a generic application icon when the process has exited or cannot be inspected; icons are cached by PID and creation time, but are not recorded in history. Memory is resident working set in MiB, including shared pages. Process I/O is interval read/write MB/s and includes file, network and device operations; it is not disk-only attribution. These columns and resource values are retained with each historical sample. Collection is sequential within one pass, not an atomic snapshot across Windows APIs.

Process percentages represent shares of total machine CPU capacity, not per-core percentages or lifetime CPU time. The first reading and long delays require a new baseline. All processes are read from one system query without opening process handles, so access-restricted processes are included; any beyond the 20,000-process collection cap are counted explicitly. History also has a 500,000-process-measurement budget; the UI reports when this shortens the requested retention window. Listed processes plus the Other processes row need not sum to the overall percentage; the Process column heading reports the unattributed remainder. Very short-lived processes can exit between polls and therefore escape attribution.

The initial provider uses Windows system/process time counters. Machines with multiple active processor groups are currently unsupported and show unavailable readings rather than partial machine totals. Process ranking is individual; executable grouping is deferred.

## Build and tests

The solution pins SDK 10.0.402 with latest-patch roll-forward.

```powershell
dotnet build WinMon.sln -c Release -m:1 -p:UseSharedCompilation=false
dotnet test tests/WinMon.Tests/WinMon.Tests.csproj -c Release -m:1 -p:UseSharedCompilation=false
dotnet publish src/WinMon/WinMon.csproj -c Release -m:1 -p:UseSharedCompilation=false
```

`dotnet publish` creates the single-file distribution; `dotnet build` produces intermediate build output. Release enables compiler optimization, bundles the runtime and native libraries, and embeds debug symbols in the assembly. Single-file compression is off: it shrank the executable from about 166 to 72 MiB, but doubled the working set and slowed startup (see [Performance tuning measurements](#performance-tuning-measurements)). Debug builds retain the normal development configuration. All configurations cap the GC gen0 budget at 6 MiB (`System.GC.Gen0MaxBudget`).

An isolated native/UI smoke mode collects real resource samples, checks series toggles, expansion, historical inspection, expiry, hidden/minimized recording and shutdown, and writes PNG renders plus `smoke.json`. It bypasses user preference persistence and startup registration. Separate `design-compact*.png` renders use explicitly labeled illustrative data to compare a full history against the selected mockup; they are not native measurement evidence:

```powershell
.\src\WinMon\bin\Release\net10.0-windows\win-x64\publish\WinMon.exe --smoke-output D:\Projects\WinMon\artifacts\smoke
```

Bitmap renders at 100%, 150%, and 200% are layout/render evidence; they do not verify actual desktop DPI changes, keyboard automation, mixed-DPI monitor transitions, tray interaction, or startup at sign-in. Those remain manual environment checks. The smoke report includes a separate 15-second quiet observation. Both it and the full run are short measurements and are not sustained overhead benchmarks.

The hardware/process/palette increment passed 155 Release tests, including termination of an owned disposable helper and rejection of a mismatched process lifetime. Focused feature smoke (`--feature-smoke-output <directory>`) and the full smoke both exited 0; renders and reports are under `artifacts/hardware-process-palettes-final/` and `artifacts/feature-full-smoke/`. The first full attempt failed its native caption-button check; the final full rerun passed. Tests ran outside the sandbox after its test host failed to connect. Restore used command-scoped `-p:NuGetAudit=false` because the audit endpoint was unreachable; no audit setting was changed in the repository. UI smoke does not invoke process termination; interactive confirmation and actual keyboard/mixed-DPI behavior remain manual checks.

Hardware capacity uses Windows [installed-memory reporting](https://learn.microsoft.com/windows/win32/api/sysinfoapi/nf-sysinfoapi-getphysicallyinstalledsystemmemory) and [DXGI adapter descriptions](https://learn.microsoft.com/windows/win32/api/dxgi/ns-dxgi-dxgi_adapter_desc1). Expanded inventory uses System.Management 10.0.3 with read-only [memory](https://learn.microsoft.com/windows/win32/cimwin32prov/win32-physicalmemory) and [storage](https://learn.microsoft.com/windows-hardware/drivers/storage/msft-disk) queries, finite enumeration waits, and explicit disposal. Process termination uses [TerminateProcess](https://learn.microsoft.com/windows/win32/api/processthreadsapi/nf-processthreadsapi-terminateprocess) after checking lifetime and [critical-process status](https://learn.microsoft.com/windows/win32/api/processthreadsapi/nf-processthreadsapi-isprocesscritical). The application targets the pinned .NET 10 Windows project.

## Performance tuning measurements

### Whole-process profile

`--profile-output <directory> [seconds]` runs the real window and sampler in isolation (no preferences, tray or single-instance lock), waits 20 seconds, then measures for the given duration (default 60). It writes `profile.json` with CPU per second, working set, private bytes, managed heap, allocation rate, GC counts and retained process entries. Memory is read for this process only, so the profiler does not inflate CPU.

```powershell
.\src\WinMon\bin\Release\net10.0-windows\win-x64\WinMon.exe --profile-output D:\Projects\WinMon\artifacts\profile 60
```

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

### Allocation, GC and compression (October 20, 2026)

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

### GPU aggregation scratch storage (October 20, 2026 follow-up)

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

### CPU profile and JIT settings (October 20, 2026, later)

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

### Chart drawing and refresh (October 22, 2026)

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

### Services view and 2.0 refresh paths (October 23, 2026)

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

### UI microbenchmark (earlier)

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

## Release packaging

See the [release guide](../packaging/RELEASING.md) for the portable ZIP, per-user Inno Setup installer, draft GitHub Releases workflow, and generated WinGet manifests. Build all release assets with `./tools/New-Release.ps1` after bootstrapping the compiler with `./tools/Install-InnoSetup.ps1`.

## Version and license

Current version: **1.0.2**. Product details (version, copyright, license, repository) live in [Directory.Build.props](../Directory.Build.props) and are stamped into the executable's file properties; the Info window's About section shows them. To release, change `Version` there and the matching `assemblyIdentity version` in `src/WinMon/app.manifest` (a test fails if they differ).

Copyright © 2026 Galen Blakeman. Released under the [MIT License](../LICENSE).

## Design assets and validation notes

- [Current design assets and feature notes](design/README.md)
- [Visual QA record](design-qa.md)

Superseded mockups and early CPU-only design records have been removed. Historical benchmark results and validation notes above remain records of their respective builds. This is a working developer build, not a completed production release gate.

## Compact resource strip

The title-bar **Compact view** action switches to a 156-DIP-high horizontal resource strip. `MainWindow.Compact.cs` reuses the live readouts and `CpuGraph` history, preserving gaps, resource identities and the existing throughput scale. Percentages retain the 0–100 scale; the strip shows the last two minutes independently of the full chart's selected range. Optional second-drive tiles collapse when unconfigured. `CompactMode` persists separately from full-view dimensions. Ctrl+F restores the full view before focusing process search.

Run `WinMon.exe --compact-smoke-output <directory>` for isolated live-data, button switching, saved-dimension and fixture checks. It emits `compact.json`, `strip-live.png`, and dark/light renders at 836 and 1000 DIPs and 100/150/200 percent scale. These renders do not prove physical mixed-DPI transitions or composed native caption buttons.

## Pinned process rows

Pins are session-only and live in `Monitoring/ProcessPins.cs`. Individual pins use PID plus creation time; grouped pins use the same executable key as grouping, with separate lists for the two modes. Immutable retention predicates are published to the sampling thread so idle pinned processes and all pinned-group members remain in future compacted samples. Existing retained samples are not rewritten. Ranked rows exclude pinned identities before applying Top N and calculating the remainder; pin rows stay in insertion order and share the process sparkline scale. Search and App Only apply to both sections. Missing live observations show Exited only for a complete readable sample; gaps and incomplete captures remain unavailable. Historical omissions are labeled Not recorded at this sample. Snapshot copying includes displayed pin rows and suppresses stale readings.

`--snapshot-smoke-output <directory>` also runs the pinning fixture and emits `pins-dark.png`, `pins-light.png`, `pins-minimum.png`, `pins-only.png` and `pins.json`. The wide fixture uses 1000 x 1000 DIPs; the pin controls wrap below the ranking toolbar at narrower widths. These are render/interaction fixtures, not sustained performance evidence. Final verification for the feature: 446 Release tests passed outside the sandbox, snapshot/pinning smoke exited 0, and `git diff --check` passed.
