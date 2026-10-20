# WinMon

A Windows resource monitor built with WPF on .NET 10. One view overlays CPU, physical memory, system-volume disk activity, network throughput, and GPU utilization in one chart, above the busiest processes. Range chips switch the chart between the short range, 10 minutes and the full retained history.

## Run

The Release distribution is a single optimized, self-contained Windows x64 executable at `src/WinMon/bin/Release/net10.0-windows/win-x64/publish/WinMon.exe`. Copy just that executable; no separate .NET installation is required. Bundled native libraries extract automatically when the application runs.

Requires Windows. Development requires the .NET 10 SDK.

```powershell
dotnet run --project src/WinMon/WinMon.csproj -c Release
```

The default is the selected dark resource-overlay design: green CPU, blue memory, orange disk, purple network, and cyan GPU. Toggle each metric's checkbox to hide/show its trace; recording continues for all metrics and visibility is remembered. The range chips under **System resources** (for example `2m · 10m`) choose how much history the chart shows; the choice is remembered, and the choices follow the short-range and retention settings. Widen the window past 800 px and process rows also show their PID. Click the graph to hold a historical sample; use Left/Right to move, Home for the oldest visible sample, End or **Return to live** to resume the live display. Escape also clears a historical selection. Top readouts remain live; the historical summary and process table show the selected timestamp. Hover shows resource values and transfer rates for that interval.

The heading and Pin share the title bar with the native Minimize, Maximize and Close buttons. Drag the heading to move the window; title-bar double-click and the system menu retain standard Windows behavior. WPF WindowChrome defines the caption/resize regions, with Pin explicitly receiving normal button input.

**Pause** freezes recording; **Resume** starts a fresh CPU interval and leaves a graph gap. **Pin** keeps the window above other windows. **Tray** hides the window; double-click its tray icon or choose Show WinMon to restore it. Closing exits unless Close to the notification area is enabled. The tray Exit command always exits.

Settings include light/dark/system theme, short chart range (also the sparkline duration), retained history, closing behavior, and opt-in current-user startup. Preferences are stored atomically in `%LOCALAPPDATA%\WinMon\settings.json`. Resource history stays in memory and is discarded on exit. No elevation, process mutation, or network service is required.

## Measurement semantics

CPU, memory, disk, and GPU traces use a fixed 0–100% scale; network throughput uses its own visible-peak scale. Physical memory is used/total OS-visible physical RAM, with GiB readouts. Disk means active time (`100 − % Idle Time`) on the Windows system volume, with read/write throughput in decimal MB/s; it is not disk space used. Network measures one connected Ethernet/Wi-Fi adapter, preferring a gateway-bearing interface, then the fastest, and retaining that choice while connected. Its name and link speed are in the readout tooltip. The readout shows the busier direction's throughput, with receive (↓) and send (↑) below in adaptive decimal Kbps/Mbps/Gbps; utilization is the busier direction divided by reported link speed. This is link utilization, not a measurement of Internet bandwidth. Interface/speed changes, counter resets, pause and long collection gaps require fresh rate baselines. Unavailable metrics show a dash and break the corresponding trace; CPU provider failure does not stop other resources.

GPU reports the busiest physical engine across available adapters. Process contributions are summed within each engine before selecting the busiest; independent engines and GPUs are never added together. Hover the GPU readout for the adapter identifier and engine type. Windows GPU Engine performance counters supply utilization, without vendor libraries. Missing counters, startup and fresh rate baselines show a dash. Existing settings enable GPU visibility by default, and its toggle is remembered. GPU memory and temperature are outside this basic view.

The process table defaults to CPU ranking; the selector switches it to GPU, Memory or I/O, marks the sorted column with ▾, and is remembered. Memory ranks by the current working set of processes present in the newest sample; I/O ranks by the five-second average read/write rate. Earlier GPU-sort settings carry over. **Individual | Grouped** (remembered) combines processes of the same executable into one row such as `chrome ×24`. Executables are identified by full path, so same-named programs in different folders stay apart; when a path cannot be read, the process name is used. A group sums CPU and I/O, reports the busiest member's GPU (engines are not additive), and shows the summed private bytes of its current members in a **Private** column, since working sets share pages. Its sparkline is the members' summed CPU, and the tooltip lists member PIDs. The live view ranks the newest sample in full, so member counts and memory are exact; in history, idle members compacted out of older samples are omitted. CPU and GPU columns use a five-second window. GPU is each process's busiest engine per interval, averaged over readable intervals; a missing counter or unverified process lifetime shows a dash, and a valid idle reading is zero. Per-process GPU values are not additive, so the Other processes GPU cell is a dash. Sparklines continue to show CPU history in both ranking modes. Each row shows an executable icon and a CPU history sparkline covering the short chart range. All sparklines share one scale, from zero to the busiest listed process's visible peak with headroom (minimum upper limit 0.1%, capped at 100%), so heights compare across rows; hover a row to see the scale. Sparklines plot raw one-second CPU, while the CPU % column is the five-second average. Historical selection ends each sparkline at the selected interval. Icons load in the background and use a generic application icon when the process has exited or cannot be inspected; icons are cached by PID and creation time, but are not recorded in history. Memory is resident working set in MiB, including shared pages. Process I/O is interval read/write MB/s and includes file, network and device operations; it is not disk-only attribution. These columns and resource values are retained with each historical sample. Collection is sequential within one pass, not an atomic snapshot across Windows APIs.

Process percentages represent shares of total machine CPU capacity, not per-core percentages or lifetime CPU time. The first reading, resumed recording, and long delays require a new baseline. All processes are read from one system query without opening process handles, so access-restricted processes are included; any beyond the 20,000-process collection cap are counted explicitly. History also has a 500,000-process-measurement budget; the UI reports when this shortens the requested retention window. Listed processes plus the Other processes row need not sum to the overall percentage; the footer reports the unattributed remainder. Very short-lived processes can exit between polls and therefore escape attribution.

The initial provider uses Windows system/process time counters. Machines with multiple active processor groups are currently unsupported and show unavailable readings rather than partial machine totals. Process ranking is individual; executable grouping is deferred.

## Build and tests

The solution pins SDK 10.0.402 with latest-patch roll-forward.

```powershell
dotnet build WinMon.sln -c Release -m:1 -p:UseSharedCompilation=false
dotnet test tests/WinMon.Tests/WinMon.Tests.csproj -c Release -m:1 -p:UseSharedCompilation=false
dotnet publish src/WinMon/WinMon.csproj -c Release -m:1 -p:UseSharedCompilation=false
```

`dotnet publish` creates the single-file distribution; `dotnet build` produces intermediate build output. Release enables compiler optimization and single-file compression, bundles the runtime and native libraries, and embeds debug symbols in the assembly. Compression reduces distribution size but adds decompression work at startup. Debug builds retain the normal development configuration.

An isolated native/UI smoke mode collects real resource samples, checks series toggles, expansion, historical inspection, expiry, pause/resume and shutdown, and writes PNG renders plus `smoke.json`. It bypasses user preference persistence and startup registration. Separate `design-compact*.png` renders use explicitly labeled illustrative data to compare a full history against the selected mockup; they are not native measurement evidence:

```powershell
.\src\WinMon\bin\Release\net10.0-windows\win-x64\publish\WinMon.exe --smoke-output D:\Projects\WinMon\artifacts\smoke
```

Bitmap renders at 100%, 150%, and 200% are layout/render evidence; they do not verify actual desktop DPI changes, keyboard automation, mixed-DPI monitor transitions, tray interaction, or startup at sign-in. Those remain manual environment checks. The smoke report includes a separate 15-second quiet observation. Both it and the full run are short measurements and are not sustained overhead benchmarks.

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

The compressed single-file publish uses about 130 MiB more working set than an uncompressed one (312 vs 182 MiB, private 173 vs 96 MiB), because compressed assemblies are inflated into private memory instead of mapped from the file. Uncompressed, the executable is 173 MB instead of 76 MB. Software rendering made no measurable difference. The UI microbenchmark below predates later field renames and needs updating before it will run again.

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

## Design and implementation record

- [Design and visual concepts](design/README.md)
- [Requirements and validation record](design/IMPLEMENTATION.md)

The resource expansion implements selected overlay option 1; see [resource implementation and validation](design/RESOURCE-OVERLAY.md). The original CPU-only design remains a historical record. This is a working developer build, not a completed production release gate.

