# WinMon developer guide

[Back to WinMon](../README.md)

Technical notes for the current code. The [README](../README.md) describes user-facing behaviour, the [performance history](PERFORMANCE-HISTORY.md) keeps dated measurements, and [design notes](design/README.md) record how each feature was planned and what landed. Commands assume the repository root as the working directory, in PowerShell 7.4 or later.

## Requirements

- Windows x64.
- The .NET SDK pinned in [global.json](../global.json) (10.0.402, latest-patch roll-forward).
- An interactive desktop session for the smoke modes and the benchmark, which open real windows.

## Build and test

```powershell
dotnet build WinMon.sln -c Release -m:1 -p:UseSharedCompilation=false
dotnet test tests/WinMon.Tests/WinMon.Tests.csproj -c Release -m:1 -p:UseSharedCompilation=false
dotnet publish src/WinMon/WinMon.csproj -c Release -m:1 -p:UseSharedCompilation=false
```

`tools/Verify.ps1` runs the build, the tests and any [smoke modes](#smoke-modes) in one step; see [Verify a change](#verify-a-change).

- Every build, test and release script passes `-m:1 -p:UseSharedCompilation=false`; keep them on manual commands too.
- `TreatWarningsAsErrors` is on for the app.
- `-p:StoreBuild=true` builds the Microsoft Store variant, which defines `WINMON_STORE` and drops the GitHub updater. `StoreBuildTests` covers what differs, so run the tests in both variants after touching update or Settings code.
- Inside a sandbox the VSTest host can fail to connect. The same command passes outside it.
- When the NuGet audit endpoint is unreachable, add `-p:NuGetAudit=false` to that command only, and note that the run skipped the vulnerability audit.

`dotnet build` output is at `src/WinMon/bin/Release/net10.0-windows/win-x64/WinMon.exe`, and `dotnet publish` writes the single-file distribution to `…/win-x64/publish/WinMon.exe`. Release builds are self-contained, optimized and embed their debug symbols. Two settings in `WinMon.csproj` are measured choices; the [performance history](PERFORMANCE-HISTORY.md#allocation-gc-and-compression-october-20-2026) has the numbers:

- **Single-file compression is off.** Compressed assemblies are inflated into private memory instead of mapped from the file, which roughly doubled the working set and slowed startup, in exchange for an executable about 90 MB smaller.
- **The GC gen0 budget is capped at 6 MiB** (`System.GC.Gen0MaxBudget`), in every configuration. Workstation GC sizes gen0 from the L3 cache, and at WinMon's allocation rate the app committed the whole budget between rare collections. The setting is undocumented, so confirm with `GC.GetConfigurationVariables()` that it still applies after a runtime upgrade.

## Verify a change

```powershell
./tools/Verify.ps1                                # build (app, tests, benchmark) and run the tests
./tools/Verify.ps1 -Smoke feature, services       # also run those smoke modes
./tools/Verify.ps1 -Smoke all -Store              # every smoke mode, against the Store variant
```

The script builds into `artifacts/verify/build` (or `build-store`), so it leaves your normal `bin` folders alone. Each smoke mode writes to its own folder under `artifacts/verify/<timestamp>/`. The script prints a pass or fail line per step, shows the start of `failure.txt` for any failed mode, and exits non-zero if anything failed. Open the PNGs it lists to check the renders; a passing exit code checks behaviour, not appearance. `-SkipTests` goes straight to the smoke modes after building.

### Smoke modes

The shipped executable checks itself against live samples. Each mode opens an isolated window that bypasses the user's preferences, startup registration and single-instance lock, writes its evidence to the output directory, and exits 0 on success. On failure it exits 1 and writes the exception to `failure.txt`. Because `WinMon.exe` is a WinExe, PowerShell returns before it exits; wait for it to get the exit code:

```powershell
$run = Start-Process src/WinMon/bin/Release/net10.0-windows/win-x64/WinMon.exe -ArgumentList '--feature-smoke-output', "$PWD/artifacts/feature-smoke" -Wait -PassThru
$run.ExitCode
```

| Mode | `Verify.ps1 -Smoke` | Covers |
| --- | --- | --- |
| `--smoke-output <dir>` | `full` | Series toggles, expansion, history inspection, expiry, hidden and minimized recording, shutdown, renders at 100/150/200% and a 15-second quiet observation, in `smoke.json` |
| `--feature-smoke-output <dir>` | `feature` | Title bar, hardware Info and its copied report (`hardware.json`, `copied-info.txt`), process actions, palettes, the update footer at full and minimum window heights |
| `--snapshot-smoke-output <dir>` | `snapshot` | Fixture renders: design reference, App only filtering, two disk volumes, GPU/Memory/I/O ranking and pinned processes (`pins.json`) |
| `--search-smoke-output <dir>` | `search` | Process search, clearing, thread and handle sorting, live and historical |
| `--compact-smoke-output <dir>` | `compact` | Compact strip: live data, view switching, saved dimensions, dark/light renders at 836 and 1000 DIPs (`compact.json`) |
| `--services-smoke-output <dir>` | `services` | Services tab |
| `--usage-smoke-output <dir>` | `usage` | Usage tab |
| `--capture-smoke-output <dir>` | `capture` | Automatic captures |
| `--chart-smoke-output <dir>` | `chart` | Chart dropdown, focused resources, hidden-resource selection and capture inspection |
| `--examples-output <dir>` | — | README screenshots; follow [the example gallery's steps](example/README.md#regenerate) |
| `--profile-output <dir> [seconds]` | — | Whole-process cost; see [Profile the whole app](#profile-the-whole-app) |

Limits:

- Each mode except the profile times out after 60 seconds. The full smoke's measured phase takes about 50 seconds on a quiet machine, so it can time out under load; rerun it on a quiet machine before treating that as a failure.
- Use a new output directory for each run.
- Renders at several scales are layout evidence. They do not prove real DPI changes, mixed-DPI monitor moves, keyboard automation, tray interaction or startup at sign-in, which remain manual checks. Smoke never ends a process or changes a service.
- Some renders use labelled illustrative data, such as `design-compact*.png`; they compare against the design reference and are not measurements.

## Code structure

The [structure note](design/STRUCTURE.md) records why the code is laid out this way and how it got there.

| Area | Where |
| --- | --- |
| Sampling, history, ranking, retention, usage ledger, capture rules, Windows samplers | `Monitoring/`. `MonitorSession` runs the capture loop through an `IFrameSource` (`WindowsFrameSource` in the app, fakes in tests) |
| Window shell: title bar, readouts, chart, compact strip, tray, captures, updates | `MainWindow*.cs` |
| Process, Services and Usage tabs | `Controls/ProcessesView.xaml`, `Controls/ServicesView.xaml`, `Controls/UsageView.xaml` |
| Settings, persisted preferences, palettes, languages, startup registration | `Preferences/` |
| In-app updater | `Updates/`; see [Updating](UPDATING.md) |
| Global shortcut and single-instance activation | `Shortcuts/` |
| Localized text | `Resources/`; see [the resources README](../src/WinMon/Resources/README.md) |
| Smoke, example and profile modes | `Diagnostics/WindowDiagnostics*.cs`, dispatched from `App.xaml.cs` |
| Window members that diagnostics and the benchmark use | `MainWindow.Internal.cs` |

Rules for new code:

- **`Monitoring` is a leaf.** It never references WPF, Windows Forms, `WinMon.Controls` or `WinMon.Preferences`. `MonitoringLayerTests` checks the compiled IL.
- **Sampling lives outside the window.** `MonitorSession` owns the capture loop, the queue and history. It reaches the UI thread through a `post` delegate the window supplies.
- **A view is a UserControl updated through one `Update(...)` call.** It raises events when the user changes something; the window keeps persisted settings in `MonitorPreferences` and passes them back in.
- **XAML for layout and window chrome; code-built elements only for rows updated every tick.**
- **Diagnostics and the benchmark use internal members, never reflection on WinMon's private names.** When one needs more, add a named member to `MainWindow.Internal.cs` or the view.
- **Structural changes must not regress performance.** Measure before and after as described under [Performance](#performance).

## Measurement semantics

### Resources

- CPU, memory, disk and GPU traces use a fixed 0–100% scale; network throughput uses its own visible-peak scale.
- **Memory** is used over total OS-visible physical RAM, with GiB readouts.
- **Disk** is active time (`100 − % Idle Time`) on each configured volume, with read/write throughput in decimal MB/s. It is not space used. Drive letters identify logical volumes, which may share a physical disk, so disk percentages are never added together.
- **Network** measures one connected Ethernet or Wi-Fi adapter, preferring one with a gateway, then the fastest, and keeps that choice while it stays connected. Adapters are re-enumerated after a Windows network-change event or every 30 seconds; counters are read every second. The readout shows the busier direction in adaptive decimal Kbps/Mbps/Gbps, and utilization is that direction over the reported link speed. This is link utilization, not Internet bandwidth.
- **GPU** reports the busiest physical engine across adapters, from the Windows GPU Engine performance counters, without vendor libraries. Process contributions are summed within each engine before the busiest is chosen; independent engines and GPUs are never added. Live VRAM use and temperature are out of scope; Info shows dedicated memory capacity.
- Interface or speed changes, counter resets, startup and long collection gaps need fresh rate baselines. Unavailable metrics show a dash and break the trace. A CPU provider failure does not stop the other resources.
- Machines with more than one active processor group are unsupported and show unavailable CPU readings rather than partial machine totals.

### Processes

- Process CPU is a share of total machine CPU capacity, not per core or lifetime. CPU, GPU and I/O columns use a five-second window.
- The list ranks by CPU, GPU, Memory, I/O, Threads or Handles (`ProcessSort`). Memory, Threads and Handles rank the latest observation of processes in the newest sample.
- **Memory** is the resident working set, including shared pages. **Grouped** rows instead sum the private bytes of current members, since working sets share pages.
- **GPU** per process is its busiest engine per interval, averaged over readable intervals. A missing counter or unverified process lifetime shows a dash. Per-process GPU is not additive, so a group shows its busiest member and the Other processes GPU cell is a dash.
- **I/O** is interval read/write MB/s and includes file, network and device operations; it is not disk-only.
- Grouping identifies executables by full path, falling back to the process name when the path cannot be read.
- Sparklines plot one-second values of the ranking metric over the last two minutes, on one shared scale across rows (zero to the busiest listed row's visible peak with headroom).
- Listed rows plus Other processes need not sum to the machine total. The Process column heading reports the unattributed remainder, and very short-lived processes can exit between polls.

### Collection and history

- All processes come from one `NtQuerySystemInformation(SystemProcessInformation)` call, without opening process handles, so access-restricted processes are measured too. The buffer is pinned and reused, and collection stops at 20,000 processes, counting any beyond.
- Collection is sequential within one pass, not an atomic snapshot across Windows APIs.
- `ProcessRetention.Compact` keeps, in each retained sample, the processes with CPU, I/O or GPU readings, processes that have shown an app window, pinned processes, and the 50 largest working sets, thread counts and handle counts (ties at the cut-off are all kept). Dropped entries are counted in `CompactedProcesses`, and averages treat them as zero.
- `MonitorHistory` holds up to a 500,000-process-entry budget; the UI reports when that shortens the retention window. Service history keeps up to two minutes under its own bound.
- History is in memory only. In-app updates save it for the new version to restore; see [the design note](design/UPDATE-RESTART-HISTORY.md).

## Implementation notes

- **Preferences** are saved atomically to `%LOCALAPPDATA%\WinMon\settings.json`. With an empty `portable.flag` beside `WinMon.exe`, they are loaded and saved beside the executable instead, whatever the working directory, with no fallback if that folder is not writable.
- **Hardware Info** uses Windows [installed-memory reporting](https://learn.microsoft.com/windows/win32/api/sysinfoapi/nf-sysinfoapi-getphysicallyinstalledsystemmemory) and [DXGI adapter descriptions](https://learn.microsoft.com/windows/win32/api/dxgi/ns-dxgi-dxgi_adapter_desc1). The detailed sections use System.Management with read-only [processor](https://learn.microsoft.com/windows/win32/cimwin32prov/win32-processor), [memory](https://learn.microsoft.com/windows/win32/cimwin32prov/win32-physicalmemory) and [storage](https://learn.microsoft.com/windows-hardware/drivers/storage/msft-disk) queries, finite enumeration waits and explicit disposal. Inventory runs only when Info is opened or refreshed. A drive's media type is associated with an OS disk only when identifiers match unambiguously.
- **Ending a process** uses [TerminateProcess](https://learn.microsoft.com/windows/win32/api/processthreadsapi/nf-processthreadsapi-terminateprocess) after checking the creation time against the same handle, so a reused PID cannot be targeted, and refuses [critical processes](https://learn.microsoft.com/windows/win32/api/processthreadsapi/nf-processthreadsapi-isprocesscritical). Group termination checks each member separately and collects failures. Historical, system and WinMon rows are disabled, and nothing elevates.
- **Pins** live in `Monitoring/ProcessPins.cs` and persist as `SavedProcessPin` entries in `MonitorPreferences.PinnedProcesses`. Individual pins use PID plus creation time; grouped pins use the grouping key, with separate lists for each mode. The view publishes immutable retention predicates to the sampling thread so pinned processes survive compaction. Ranked rows exclude pinned identities before applying Top N and the remainder.
- **Compact view** (`MainWindow.Compact.cs`) is a 156-DIP strip that reuses the live readouts and `CpuGraph` history and always shows the last two minutes. `CompactMode` persists separately from the full-view size, and Ctrl+F restores the full view before focusing process search.
- **Services**: see [the services implementation note](design/services-implementation.md).

## Performance

WinMon samples once a second all day, so its own cost is a feature. Measure structural or hot-path changes before and after, and add the results to the top of the [performance history](PERFORMANCE-HISTORY.md).

### Benchmark the UI thread

`tools/WinMon.Performance` drives the real window with synthetic 850-process samples while sampling is stopped. Each scenario runs 400 warm-up frames, then seven batches of 60, and reports UI-thread time, allocation and a pixel hash of the rendered window. Scenarios: live, historical, grouped, Services, compact, pinned and selected (individual and grouped), an open CPU tooltip, `UsageLedger.Add`, and a 60-minute restart-history save and read.

```powershell
dotnet build tools/WinMon.Performance/WinMon.Performance.csproj -c Release -m:1 -p:UseSharedCompilation=false
./tools/WinMon.Performance/bin/Release/net10.0-windows/win-x64/WinMon.Performance.exe "$PWD/artifacts/bench.json" "$PWD/artifacts/bench.png"
```

- Build a baseline from `main` before editing, and keep its output folder so it stays runnable.
- Alternate baseline and changed runs on a quiet machine, at least four of each, and compare medians. An IDE indexing in the background hides real differences.
- Pixel hashes should match the baseline. The window is on screen, so a pointer over it can produce a known alternate frame.
- The first scenario is sensitive to JIT tiering. If only it differs, rerun both builds with `DOTNET_TieredCompilation=0` to tell tiering from extra work.

### Profile the whole app

`--profile-output <dir> [seconds]` runs the real window and sampler in isolation, waits 20 seconds, then measures for the given duration (default 60, 10–3600). It writes `profile.json` with CPU per second, working set, private bytes, managed heap, allocation rate, GC counts and retained process entries, reading memory for this process only. Run both builds side by side and swap launch order, because the first-launched instance tends to use more UI-thread time. Run at least 150 seconds when service history matters, so it reaches its two-minute steady state.

### Where the time goes

As of October 2026 (see the [history](PERFORMANCE-HISTORY.md#chart-drawing-and-refresh-october-22-2026)):

- The process snapshot dominates sampling, and its cost follows the machine's thread count, because the query returns every thread. It is the OS floor for 1 Hz sampling.
- On the UI thread, WPF text formatting and layout of values that actually changed is about half of a live frame; WinMon's own refresh and chart drawing are the rest.
- Whole-app CPU barely moves with UI-thread savings. Allocation and private bytes are the more sensitive measures.

### Decisions in force

| Decision | Why |
| --- | --- |
| Gen0 budget capped at 6 MiB | Private bytes 86 → 52 MiB in a 60 s profile, for cheap extra gen0 collections |
| Single-file compression off | About 130 MiB less working set and faster startup |
| Default tiered PGO kept | Turning it off cuts launch CPU but only breaks even after about an hour |
| No ReadyToRun | Most tier-1 work is already-precompiled WPF code; +20 MB for little gain |
| Retain only active, app, pinned and largest processes | About 87% fewer retained process entries at 10 minutes |

## Release packaging

See the [release guide](../packaging/RELEASING.md) for the portable ZIP, per-user Inno Setup installer, GitHub Releases workflow and WinGet manifests, and the [MSIX guide](../packaging/msix/README.md) for Store packages. Build all release assets with `./tools/New-Release.ps1` after bootstrapping the compiler with `./tools/Install-InnoSetup.ps1`.

## Version and license

Product details (version, copyright, license, repository) live in [Directory.Build.props](../Directory.Build.props) and are stamped into the executable; the Info window's About section shows them. To release, change `Version` there and the matching `assemblyIdentity version` in `src/WinMon/app.manifest`; `AppInfoTests` fails if they differ.

Copyright © 2026 Galen Blakeman. Released under the [MIT License](../LICENSE).

## Design records

- [Design assets and feature notes](design/README.md)
- [Visual QA record](design-qa.md)
