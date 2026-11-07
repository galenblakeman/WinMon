# WinMon developer guide

[Back to WinMon](../README.md)

Technical notes for the current code. The [README](../README.md) describes user-facing behaviour, the [performance history](PERFORMANCE-HISTORY.md) keeps dated measurements, and [design notes](design/README.md) record how each feature was planned and what landed. Commands assume the repository root as the working directory, in PowerShell 7.4 or later.

## Requirements

- Windows x64.
- The portable `MonCore` project and `MonCore.Tests` target plain `net10.0`; they can also build and run on macOS with the .NET 10 SDK. That macOS run is a separate verification step.
- The .NET SDK pinned in [global.json](../global.json) (10.0.402, latest-patch roll-forward).
- An interactive desktop session for the smoke modes and the benchmark, which open real windows.

## Build and test

```powershell
dotnet build WinMon.sln -c Release -m:1 -p:UseSharedCompilation=false
dotnet test tests/WinMon.Tests/WinMon.Tests.csproj -c Release -m:1 -p:UseSharedCompilation=false
dotnet test tests/MonCore.Tests/MonCore.Tests.csproj -c Release -m:1 -p:UseSharedCompilation=false
dotnet test tests/MonCore.Windows.Tests/MonCore.Windows.Tests.csproj -c Release -m:1 -p:UseSharedCompilation=false
dotnet publish src/WinMon/WinMon.csproj -c Release -m:1 -p:UseSharedCompilation=false
dotnet pack src/MonCore/MonCore.csproj -c Release -m:1 -p:UseSharedCompilation=false -o artifacts/local-feed
```

`tools/Verify.ps1` builds all six projects in the solution, builds the benchmark, runs all three test projects and any [smoke modes](#smoke-modes) in one step; see [Verify a change](#verify-a-change).

- Every build, test and release script passes `-m:1 -p:UseSharedCompilation=false`; keep them on manual commands too.
- `TreatWarningsAsErrors` is on for the app and both libraries.
- `-p:StoreBuild=true` builds the Microsoft Store variant, which defines `WINMON_STORE`, drops the GitHub updater, and targets `net10.0-windows10.0.17763.0` so sign-in startup can use the package's `StartupTask`. The test and benchmark projects switch TFM with it. `StoreBuildTests` covers what differs, so run the tests in both variants after touching update or Settings code.
- Inside a sandbox the VSTest host can fail to connect. The same command passes outside it.
- Run the native Services smoke outside a restricted sandbox. SCM enumeration returned Win32 error 5 (Access denied) inside the sandbox on the development machine, leaving every service frame unavailable; the same collector and smoke passed outside it.
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
| `--feature-smoke-output <dir>` | `feature` | Activity tab overview tooltips (`tab-help-*.png`), title bar, CSV export dialog in Dark, Light and Slate (`export-*.png`), Info tabs in Dark/Light and at minimum size (`info-*.png`), expanded hardware sections (`hardware-section-*.png`) and the complete copied report (`hardware.json`, `copied-info.txt`), process actions, the action drawer, activity-panel collapse/restore, resized and persisted splits (`activity-panel-*.png`, `activity-panel-passed.txt`), palettes, the update footer at full and minimum window heights |
| `--snapshot-smoke-output <dir>` | `snapshot` | Fixture renders: design reference, App only filtering, two disk volumes, GPU/Memory/I/O ranking and pinned processes (`pins.json`) |
| `--search-smoke-output <dir>` | `search` | Process search, clearing, thread and handle sorting, live and historical |
| `--compact-smoke-output <dir>` | `compact` | Window position settling delay, native restoration and a user move (`position-recovery.png`); compact gauges: live data, every gauge style, view switching and Export visibility, saved dimensions, optional disk, dark/light renders at 836 and 1000 DIPs, plus one render per additional appearance finish (`compact.json`) |
| `--services-smoke-output <dir>` | `services` | Services tab |
| `--usage-smoke-output <dir>` | `usage` | Usage tab |
| `--connections-smoke-output <dir>` | `connections` | Native unelevated TCP/UDP ownership, grouped connection rows, filtering, copy/navigation, unavailable readings and Dark/Light/narrow renders |
| `--capture-smoke-output <dir>` | `capture` | Automatic captures, the 836-DIP chart toolbar with Auto, and Settings sections in Dark and Light appearances; fails if the Appearance section scrolls at full height, and records each section's overflow in `captures.json` |
| `--chart-smoke-output <dir>` | `chart` | Chart dropdown, sectioned chart-type help on the heading and selector in Dark/Light (`chart-help-*.png`), focused resources, hidden-resource selection and capture inspection |
| `--gauges-smoke-output <dir>` | `gauges` | Every gauge style in dark and light at 1000 and 836 DIPs, plus one render per additional appearance finish: shared readout height, unclipped values, network scale, unavailable readings, the saved style and combined Settings appearance previews (`gauges.json`). The translucent finishes also get desktop-composed captures (`backdrop-*.png`, `backdrops.json`); if Windows denies foreground activation, WPF content captures are saved as `backdrop-*-content.png` with `content-capture-notes.txt` instead. |
| `--examples-output <dir>` | — | README screenshots; follow [the example gallery's steps](example/README.md#regenerate) |
| `--profile-output <dir> [seconds]` | — | Whole-process cost; see [Profile the whole app](#profile-the-whole-app) |

Limits:

- Each mode except the profile times out after 60 seconds. The full smoke's measured phase takes about 50 seconds on a quiet machine, so it can time out under load; rerun it on a quiet machine before treating that as a failure.
- Use a new output directory for each run.
- Renders at several scales are layout evidence. They do not prove real DPI changes, mixed-DPI monitor moves, keyboard automation, tray interaction or startup at sign-in, which remain manual checks. Smoke never ends a process or changes a service.
- Some renders use labelled illustrative data, such as `design-compact*.png`; they compare against the design reference and are not measurements.

Process Properties and Copy details share `ProcessDetailsSnapshot.Format()`. File and product versions come from the existing per-path `ProcessAppNameProvider` cache on the sampling thread; opening details performs no file I/O. Restart-history formats 2 and 3 preserve both strings; format 1 loads with empty versions. The current format 3 also preserves logical-processor readings. Older WinMon builds cannot restore format-3 history.

## Code structure

Connections is a live-only, on-demand view. `WindowsConnections.Capture()` in MonCore.Windows reads owner-PID tables through [GetExtendedTcpTable](https://learn.microsoft.com/en-us/windows/win32/api/iphlpapi/nf-iphlpapi-getextendedtcptable) and [GetExtendedUdpTable](https://learn.microsoft.com/en-us/windows/win32/api/iphlpapi/nf-iphlpapi-getextendedudptable) for both IP families, with bounded allocation/retries and explicit family availability. A preliminary endpoint pass limits the process-lifetime probes to endpoint owners; matching creation tokens before and after the final tables prevent reused PIDs from acquiring process actions. Denied identity probes leave an unknown owner and generic icon without hiding the endpoint. A failed TCP or UDP table makes that address family's readings unavailable; the other family can still be shown with a partial-reading notice.

`MainWindow.Connections.cs` schedules a single background read at most once a second from the existing presentation cadence while the Connections tab is visible, including when only its collapsed header remains. The header TCP/UDP totals describe all available system readings, independently of search, protocol and display mode. The hidden activity body is not redrawn. There is no additional sampling loop or endpoint history persistence.

`ConnectionsView.Update(...)` reconciles endpoint and executable-group rows without replacing unchanged list items. Individual mode lists exact endpoints with owner PID/protocol; Grouped uses case-insensitive executable paths like Processes, with a lifetime-only fallback when the path or owner is unavailable. An owner dictionary validates Windows lifetime identities in O(processes + endpoints). Group summaries count distinct addresses/ports and TCP states, with full protocol-tagged endpoint lists in tooltips; UDP contributes no remote peer/state. Duplicate socket bindings remain separate endpoint rows but do not inflate distinct address/port summaries. Selection survives refreshes and filters, expansion survives mode changes, and vanished rows are pruned. Display mode, search and protocol are session-local. Icons reuse `ProcessIconProvider`; the shared drawer and lifetime-checked individual/group navigation follow the other activity tabs.

TCP states outside the documented 1–12 range are unavailable, not inferred. This Windows installation has returned state 0 for a listener and inconsistent valid states for a working loopback pair. Unspecified zero-port peers are omitted; nonzero peers are preserved independently of state. The smoke proves the TCP connection with a byte exchange, checks TCP/UDP ownership and peer endpoints, and records OS-reported states without asserting that Windows must report Established. Its elevation and availability evidence is in `native-connections.json`. Fixture screenshots exercise grouping, summaries, global totals, flat rows and navigation separately from native readings.

Window placement recovery lives in `MainWindow.Position.cs` and `WindowPosition.cs`. Native move/resize completion and Windows-key placement record the user's monitor and offset in physical pixels; automatic Windows relocations do not overwrite that target. Display changes and resume restart a three-second dispatcher timer. Recovery preserves focus and size, defers minimized/maximized windows until they return to Normal, and retains a missing monitor's target until the user moves the window. Monitor identity and offsets are session-only; startup continues to use the persisted bounds. Mixed-DPI visibility checks use `GetWindowRect`, matching the physical coordinates from `Screen.WorkingArea`.

The [structure note](design/STRUCTURE.md) records the original WPF separation; the [shared monitoring design](design/SHARED-MONITORING.md) records the cross-platform assembly boundary.

| Area | Where |
| --- | --- |
| Portable samples, session, history, ranking, retention, usage, capture rules, CSV and restart serialization | `src/MonCore/Monitoring/` (`net10.0`) |
| Windows sampling, SCM enumeration, WMI, hardware probes and native process actions | `src/MonCore.Windows/Monitoring/` (`net10.0-windows`); `WindowsFrameSource` implements `IFrameSource` |
| Localized reports, hardware presentation and process-action messages | `src/WinMon/Monitoring/` |
| Window shell: title bar, readouts, chart, compact strip, tray, captures, updates | `MainWindow*.cs` |
| Process, Services and Usage tabs | `Controls/ProcessesView.xaml`, `Controls/ServicesView.xaml`, `Controls/UsageView.xaml` |
| Settings, persisted preferences, palettes, languages, startup registration | `Preferences/` |
| In-app updater | `Updates/`; see [Updating](UPDATING.md) |
| Global shortcut and single-instance activation | `Shortcuts/` |
| Localized text | `Resources/`; see [the resources README](../src/WinMon/Resources/README.md) |
| Smoke, example and profile modes | `Diagnostics/WindowDiagnostics*.cs`, dispatched from `App.xaml.cs` |
| Window members that diagnostics and the benchmark use | `MainWindow.Internal.cs` |

Appearance finishes live in `Preferences/ThemeFinishes.cs`; Settings lists the built-in modes first and sorts custom labels in the current language. `MainWindow.ApplyTheme` keeps `TitleText` separate from body `Text` so bright title bars can use white caption text without changing data-surface text. The shared time-range and process-grouping chips use dedicated `ChipText` and `ChipSelectedText` brushes, with high-contrast system colors taking precedence. `ChartPalettes.ColorsForTheme` adjusts palette lightness against panel and plot colors for gauge text and chart labels. `ChartColorsForTheme` adjusts only against the plot for chart traces, markers and sparklines. Palettes whose single set lost its character under that adjustment have separate dark and light sets in `ChartPalettes.Colors`, chosen to clear 3.5:1 on every plot in their mode; `ChartPaletteTests` keeps those sets unadjusted, and checks that Colorblind, Signal, High-Contrast & Inclusive, Copper and Gunmetal keep every pair of resources at least CIE76 ΔE 15 apart under simulated deuteranopia and protanopia, and 10 under tritanopia. When the adjustment darkens a yellow, it also turns the hue toward amber. `ChartPalettes.ForTheme` pairs every appearance with a palette, and Classic for System, Light and Dark. When the appearance changes in Settings, the palette follows only if it is still Classic or the previous appearance's pair, so a palette the user picked stays. `ChartPaletteTests` keeps each pair's readouts at least ΔE 12 from its finish's selected, highlight and accent colors. `Normalize` maps the retired Cyberpunk / Neon palette to Neon. Settings swatches show chart colors, and the six-resource Appearance preview uses both shades; Windows high contrast mode uses system brushes instead.

Activity tabs use a separate `InactiveTabText` brush so their labels clear each finish's header without changing other muted text. Copper's brushed reflection is included in the contrast test. `CpuGraph` shapes its axis, network-scale and selected-sample labels at 12 DIPs and measures alternate-view label widths at that same size.

`SettingsWindow.Field` gives dropdown rows two equal star columns, so each selector takes half the content width; long selected network names remain available through the selector tooltip. Field labels trim when needed at the minimum window width.

Rules for new code:

- **`MonCore` is portable.** It references no WPF, Windows Forms, Avalonia, System.Management or native imports. `AssemblyBoundaryTests` checks assembly references and imports, and `MonitoringLayerTests` checks compiled type use.
- **The Windows adapter depends on MonCore.** WinMon references both projects. `MonCore.Tests` references only MonCore; `MonCore.Windows.Tests` references the adapter.
- **Sampling lives outside the window.** `MonitorSession` owns the capture loop, the queue and history. It reaches the UI thread through a `post` delegate the window supplies.
- **Capabilities and observations are separate.** `IFrameSource.Capabilities` says which system and process metrics and actions an implementation supports. Nullable readings still report per-sample unavailability, and process actions recheck the lifetime and Windows permission at execution.
- **A view is a UserControl updated through one `Update(...)` call.** It raises events when the user changes something; the window keeps persisted settings in `MonitorPreferences` and passes them back in.
- **XAML for layout and window chrome; code-built elements only for rows updated every tick.**
- **Diagnostics and the benchmark use internal members, never reflection on WinMon's private names.** When one needs more, add a named member to `MainWindow.Internal.cs` or the view.
- **Structural changes must not regress performance.** Measure before and after as described under [Performance](#performance).

## Measurement semantics

### Resources

- **Logical processors** adds a CPU-only grid to the existing chart dropdown. Each processor's utilization is `(kernel + user − idle) / (kernel + user)` over successive Windows [SystemProcessorPerformanceInformation](https://learn.microsoft.com/windows/win32/api/winternl/nf-winternl-ntquerysysteminformation#parameters) readings, on its own 0–100% scale. Kernel includes idle. The machine and process percentages keep their existing total-capacity denominator. A missing or invalid processor reading breaks that processor's trace; an optional per-processor query failure leaves total/process CPU sampling intact. Grid cells share the selected chart range and historical interval. Processor count changes require a fresh baseline. Short cells use labeled sparklines, with values in tooltips and UI Automation; larger cells also display a percentage and grid guides.
- CPU, memory, disk and GPU traces use a fixed 0–100% scale; network throughput uses its own visible-peak scale.
- The chart toolbar keeps Auto immediately after the centered Live/Capture switch, without shifting that switch; the 150-DIP chart type selector stays on the left and time ranges stay on the right. Its width accommodates Logical processors; longer translations remain available in the tooltip. The former "2m peak" legend and tooltip are absent. The gauge setting explains the dark peak mark and its configurable window.
- Resource gauges in Full and Compact views are drawn by `MetricGauge` in the `GaugeStyle` chosen in Settings (default Bowl). Each gauge shows the live level and a dark peak line. GaugePeakWindowSeconds selects 60, 120 (default), 300 or 600 elapsed seconds, independently of chart range. Peaks retain the newest maximum sample, skip gaps and invalid readings, match configured disk volumes and keep network readings on the latest adapter. Only retained samples contribute; a shortened history can cover less than the requested window. Full-view tiles use symmetric horizontal margins so each bowl and value share the center of their column. Arc styles put a decorative peak-marker icon and value beside CPU, memory, disk and GPU names at every width; the accessibility name and tooltip retain the Peak label. Peak labels always use RAM used, disk throughput or dedicated GPU memory from the utilization-peak sample when available, otherwise a percentage. It does not change live readings. The shared resource tooltip includes the peak, its local timestamp and age relative to the latest sample, with monotonic elapsed time where available; it is available on the tile, corner and keyboard focus in both views. MetricDetails formats that section only while open. `MetricReadout` accounts for each tile's outer margin to place the corner label 12 DIPs from its right edge. The network gauge uses received bits per second on its own peak-window auto scale, which matches the chart when both ranges agree; arc styles print the download peak (`RateText.Short`, such as "64M") with the peak icon; the tooltip prints the full rate and top of the automatic scale. Percentages use 0–100. An unavailable current reading hides its gauge and corner label. Every style fits `MetricReadout.LayoutHeight`; see [Gauge styles](design/GAUGE-STYLES.md) for the layouts.
- **Memory** is used over total OS-visible physical RAM, with GiB readouts.
- **Disk** is active time (`100 − % Idle Time`) on each configured volume, with read/write throughput in decimal MB/s. It is not space used. Drive letters identify logical volumes, which may share a physical disk, so disk percentages are never added together.
- **Network** measures one connected Ethernet or Wi-Fi adapter. Automatic selection prefers one with a gateway, then the fastest, and keeps that choice while it stays connected. Monitoring settings can instead hold a specific adapter ID; if it is disconnected or missing, readings stay unavailable rather than switching adapters. Adapters are re-enumerated after a Windows network-change event or every 30 seconds; counters are read every second. Changing the selected adapter resets rate and total baselines. The readout shows the busier direction in adaptive decimal Kbps/Mbps/Gbps, and utilization is that direction over the reported link speed. This is link utilization, not Internet bandwidth.
- **GPU** reports the busiest physical engine across adapters, from the Windows GPU Engine performance counters, without vendor libraries. Process contributions are summed within each engine before the busiest is chosen; independent engines and GPUs are never added. Live VRAM use and temperature are out of scope; Info shows dedicated memory capacity.
- Interface or speed changes, counter resets, startup and long collection gaps need fresh rate baselines. The received and sent totals restart only when the adapter changes or its counters go backwards. Unavailable metrics show a dash and break the trace. A CPU provider failure does not stop the other resources. A capture that fails, or a sampler that cannot start, shows readings unavailable and is retried each second.
- Machines with more than one active processor group are unsupported and show unavailable CPU readings rather than partial machine totals; an update there proves healthy from the other resources.

Automatic capture report writes are tracked on the UI thread. Normal close and in-app update restart wait for them, like CSV exports and process dumps, before closing the window or committing the update. The capture smoke includes a delayed-write shutdown check in `captures.json`. A stalled report destination can delay exit; forced termination and Windows session shutdown do not provide this asynchronous wait.

Invalid JSON settings whose backup fails remain protected: subsequent saves retry the backup and fail without replacing the original until it succeeds.

### Processes

Portable lifetime, executable equality and CPU availability contracts are described in [Portable process identity](design/PORTABLE-PROCESS-IDENTITY.md). Windows defaults and history format 1 remain unchanged; portable consumers use `ObservedCpuPercent` and explicitly select their identity policy.

- Process CPU is a share of total machine CPU capacity, not per core or lifetime. CPU, GPU and I/O columns use a five-second window.
- The list ranks by CPU, GPU, Memory, I/O, Threads or Handles (`ProcessSort`). Memory, Threads and Handles rank the latest observation of processes in the newest sample.
- **Memory** is the resident working set, including shared pages. **Grouped** rows instead sum the private bytes of current members, since working sets share pages.
- Processes and Services keep units in numeric cells: CPU/GPU percentages, memory in adaptive binary B/KiB/MiB/GiB/TiB and read/write I/O rates in adaptive decimal B/KB/MB/GB/TB per second. Scaled memory and I/O values use one decimal place; bytes use whole numbers. Their headers use metric names only; Services labels private-memory totals as Memory. Usage already keeps Share's percent sign and adaptive decimal byte units in its cells.
- **GPU** per process is its busiest engine per interval, averaged over readable intervals. A missing counter or unverified process lifetime shows a dash. Per-process GPU is not additive, so a group shows its busiest member and the Other processes GPU cell is a dash.
- **I/O** is interval read/write MB/s and includes file, network and device operations; it is not disk-only.
- Grouping identifies executables by full path, falling back to the process name when the path cannot be read.
- Sparklines plot one-second values of the ranking metric over the last two minutes, on one shared scale across rows (zero to the busiest listed row's visible peak with headroom).
- Listed rows plus Other processes need not sum to the machine total. The Process column heading reports the unattributed remainder, and very short-lived processes can exit between polls.

### Collection and history

- Restart-history format 3 includes nullable per-logical-processor readings. Formats 1 and 2 still load, with those readings unavailable; older builds cannot restore format-3 history. Normal restarts and update restarts use the same versioned sample encoding.
- MonCore's `MonitorSession` owns history, latest samples, usage and service frames on the thread selected by the caller's `post` delegate. The collection loop runs on the thread pool, enqueues captures in order and never waits for the UI to drain them. Call session state operations on the dispatch thread; cancellation ends collection and `Close()` discards later drains.
- `ProcessUsage.CreationTicks` remains Windows FILETIME in the current Windows collector and saved-history format. A portable lifetime/start-time model is required before a macOS collector writes samples or persistence. Group keys currently compare executable paths without case, preserving WinMon behavior.
- CPU percentages are shares of total machine capacity; I/O rates use bytes per second, GPU is the busiest engine, and unavailable readings stay null. `WorkingSetBytes` is resident memory and `PrivateBytes` is private committed memory; a macOS footprint needs its own measurement contract.
- All processes come from one `NtQuerySystemInformation(SystemProcessInformation)` call, without opening process handles, so access-restricted processes are measured too. The buffer is pinned and reused, and collection stops at 20,000 processes, counting any beyond.
- Collection is sequential within one pass, not an atomic snapshot across Windows APIs.
- `ProcessRetention.Compact` keeps, in each retained sample, the processes with CPU, I/O or GPU readings, processes that have shown an app window, pinned processes, and the 50 largest working sets, thread counts and handle counts (ties at the cut-off are all kept). Dropped entries are counted in `CompactedProcesses`, and averages treat them as zero.
- `MonitorHistory` holds up to a 500,000-process-entry budget; the UI reports when that shortens the retention window. Service history keeps up to two minutes under its own bound.
- History is in memory by default. In-app updates save it for the new version to restore; see [the design note](design/UPDATE-RESTART-HISTORY.md). The optional normal-restart snapshot saves compacted samples beside settings, then consumes the file on startup before sampling. It rebases historical monotonic timestamps onto the current process clock and shifts live samples by the same offset when needed after a reboot. Usage totals and service frames are not included; see [the normal-restart design](design/NORMAL-RESTART-HISTORY.md).

## Implementation notes

- **Preferences** are saved atomically to `%LOCALAPPDATA%\WinMon\settings.json`. With an empty `portable.flag` beside `WinMon.exe`, they are loaded and saved beside the executable instead, whatever the working directory, with no fallback if that folder is not writable. Enums are stored by name through `LenientEnumConverter`, which reads a name this build lacks (one a newer build added) as an undefined value for `Validate` to replace, so only that setting resets. A file that still cannot be parsed, or stays locked after five tries, is copied to `settings.invalid.json` before the defaults are used, because the next save overwrites it.
- **Hardware Info** uses Windows [installed-memory reporting](https://learn.microsoft.com/windows/win32/api/sysinfoapi/nf-sysinfoapi-getphysicallyinstalledsystemmemory) and [DXGI adapter descriptions](https://learn.microsoft.com/windows/win32/api/dxgi/ns-dxgi-dxgi_adapter_desc1). The detailed sections use System.Management with read-only [processor](https://learn.microsoft.com/windows/win32/cimwin32prov/win32-processor), [memory](https://learn.microsoft.com/windows/win32/cimwin32prov/win32-physicalmemory) and [storage](https://learn.microsoft.com/windows-hardware/drivers/storage/msft-disk) queries, finite enumeration waits and explicit disposal. The tabbed Info window uses XAML for layout, guide expanders and About, and code only for snapshot rows. Inventory runs only when Info is opened or refreshed; tab changes do not collect again. Copy includes collapsed details. Guide text describes current behavior; About shares the owner's checker, busy state and update offer. Update now closes Info before the existing download/restart flow, and Store builds show a Store-managed explanation without GitHub controls. A drive's media type is associated with an OS disk only when identifiers match unambiguously.
- **Ending a process** uses [TerminateProcess](https://learn.microsoft.com/windows/win32/api/processthreadsapi/nf-processthreadsapi-terminateprocess) after checking the creation time against the same handle, so a reused PID cannot be targeted, and refuses [critical processes](https://learn.microsoft.com/windows/win32/api/processthreadsapi/nf-processthreadsapi-isprocesscritical). Group termination checks each member separately and collects failures. Historical, system and WinMon rows are disabled, and nothing elevates.
- **Pins** live in `src/MonCore/Monitoring/ProcessPins.cs` and persist as `SavedProcessPin` entries in `MonitorPreferences.PinnedProcesses`. Individual pins use PID plus creation time; grouped pins use the grouping key, with separate lists for each mode. The view publishes immutable retention predicates to the sampling thread so pinned processes survive compaction. Ranked rows exclude pinned identities before applying Top N and the remainder.
- **Compact view** (`MainWindow.Compact.cs`) is a 142-DIP window with the existing title bar and six `MetricReadout` instances. They copy the full readouts' live text and receive the same gauge values, configured-window peaks and selected style. The second disk is shown only when a drive is configured. Resource names replace chart-series checkboxes; chart visibility preferences remain unchanged. `CompactMode` persists separately from the full-view size. `StartupView` chooses whether launch uses that last saved mode or forces Full or Compact; it does not prevent switching views later. Ctrl+F restores the full view before focusing process search.
- **Activity panel** (`MainWindow.ActivityPanel.cs`) keeps its tabs and counts when collapsed, hiding only the body and resize divider. `ActivityPanelCollapsed` and `ActivityPanelRatio` persist the disclosure state and expanded star-size ratio. Collapse, entry to Compact view and normal/session-end exit capture the latest split; collapsed or Compact layouts never overwrite it. Expansion restores the split within the existing row minimums. Clicking any tab or using Ctrl+F expands the body. Views remain alive and monitoring continues.

- **Chart panel** shares the activity panel's disclosure control and split handling. `ChartPanelCollapsed` persists independently; the plot and snapshot share `ChartBody`, while chart selectors and the restore chevron remain visible. Either collapsed body hides the divider, and neither collapsed layout overwrites the saved split. The feature smoke includes chart collapse, focus recovery, both panels collapsed, Compact view, persistence and split restoration (`chart-panel-*.png`).
- **Services**: see [the services implementation note](design/services-implementation.md). The activity tab strip counts individual services in the selected service frame, including services in shared hosts; Running means state 4. An unavailable frame shows dashes. Usage counts ledger app groups, with Active meaning at least one currently running process; its totals continue to update while a capture is selected.

## Performance

Full-view section headers occupy the full-width layout grid. Gauges and the chart keep 20-DIP side margins; activity views use 20-DIP gutter columns and span their toolbar backgrounds across all three columns. Keep header backgrounds inside their parent bounds: negative margins are clipped horizontally when short windows force WPF to clip overflowing row minimums. The feature smoke checks header width and ancestor layout clips at 900, 720 and 600 DIPs high for all three activity tabs.

WinMon samples once a second all day, so its own cost is a feature. Measure structural or hot-path changes before and after, and add the results to the top of the [performance history](PERFORMANCE-HISTORY.md).

### Benchmark the UI thread

`tools/WinMon.Performance` drives the real window with synthetic 850-process samples while sampling is stopped. Each scenario runs 400 warm-up frames, then seven batches of 60, and reports UI-thread time, allocation and a pixel hash of the rendered window. Scenarios: live, historical, grouped, Services, compact, pinned and selected (individual and grouped), an open CPU tooltip, `UsageLedger.Add`, and 60-minute update and normal-restart history saves and reads. A 12-logical-processor grid with pinned/selected processes runs after the existing pixel capture, so it does not shift the earlier scenarios' synthetic sample sequence. The final Connections scenarios keep that chart and use 60 processes and 480 TCP endpoints: an expanded executable group, then flat Individual mode. They measure UI refresh only, with native collection disabled. Synthetic samples also carry per-processor readings into the history-save workload.

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

Release packaging tests `WinMon.sln`, covering all three test projects, and writes separately named TRX results per test run.

See the [release guide](../packaging/RELEASING.md) for the portable ZIP, per-user Inno Setup installer, GitHub Releases workflow and WinGet manifests, and the [MSIX guide](../packaging/msix/README.md) for Store packages. Build all release assets with `./tools/New-Release.ps1` after bootstrapping the compiler with `./tools/Install-InnoSetup.ps1`.

## Version and license

Product details (version, copyright, license, repository) live in [Directory.Build.props](../Directory.Build.props) and are stamped into the executable; the Info window's About section shows them. To release, change `Version` there and the matching `assemblyIdentity version` in `src/WinMon/app.manifest`; `AppInfoTests` fails if they differ.

Copyright © 2026 Galen Blakeman. Released under the [MIT License](../LICENSE).

## Design records

- [Design assets and feature notes](design/README.md)
- [Visual QA record](design-qa.md)
