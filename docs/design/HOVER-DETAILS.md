# Resource detail tooltips

Proposed on 2026-10-24. Status: implemented on 2026-10-24.

## Why

Each resource tile shows one number and one detail line. Most "why is it at 90%?" questions need one more level of detail: is the disk reading or writing, is memory committed or just cached, which process is busiest right now. Today that means opening Task Manager or Resource Monitor next to WinMon.

The tiles already have plain-text tooltips, set every tick in `UpdateResources` ([MainWindow.xaml.cs](../../src/WinMon/MainWindow.xaml.cs)). They give a measurement definition, plus link speed for network and adapter and engine for GPU. This design replaces them with a short table of live values per resource, keeping the definition sentence at the top.

## Behavior

Hovering a tile, or focusing it with the keyboard (its chart toggle in full view, or the box itself in compact view), opens a tooltip:

```
CPU                                      live
Share of total processor time.
──────────────────────────────────────────────
Clock speed                          4.21 GHz
User / kernel                    18.2% / 6.1%
Processes · threads        1,247 · 18,903
Handles                               612,880
Busiest process (live)       chrome  9.4%
Up time                             3 d 04:12
```

| Tile | Rows | Source | New sampling? |
| --- | --- | --- | --- |
| **CPU** | Clock speed · User / kernel · Processes · threads · Handles · Busiest process · Up time | Clock: existing. User/kernel: `SystemCpuTimes` deltas, where kernel busy = kernel − idle. Counts: the `ProcessCount`, `ThreadCount` and `HandleCount` fields of `GetPerformanceInfo`, which are machine-wide. Busiest: the [busiest-process rule](#busiest-process). Up time: `Environment.TickCount64` | Kernel split, plus the `GetPerformanceInfo` call shared with Memory |
| **Memory** | In use / total · Available · Committed / limit · Cached · Paged / non-paged pool · Largest working set | `GetPerformanceInfo` (one call next to the existing `GlobalMemoryStatusEx`). Largest: the [busiest-process rule](#busiest-process) with `ProcessSort.Memory` | One cheap call per second |
| **Disk** (each volume) | Active time · Read · Write · Average response · Queue length · Free space | Four PDH counters added to the existing per-volume `LogicalDisk` query: `Disk Read Bytes/sec`, `Disk Write Bytes/sec`, `Avg. Disk sec/Transfer` and `Current Disk Queue Length`. Free space: `DriveInfo` every 30 s | Yes, same query and collect call |
| **Network** | Adapter · Link speed · Utilization · Receive / send · Received / sent since start | Existing fields. Totals: the adapter's cumulative octet counters minus the value when WinMon started or the adapter last changed | Totals only, already read for rates |
| **GPU** | Adapter name · Busiest engine · Dedicated memory · Shared memory · Busiest process | Name: DXGI description, which replaces the LUID-style `GpuId` shown today. Shared: `GPU Adapter Memory(*)\Shared Usage` in the existing memory query. Busiest: the [busiest-process rule](#busiest-process) with `ProcessSort.Gpu` | One counter |

Rules shared by every tooltip:

- **Live values.** Values update every second while the tooltip is open. They stay live in Capture mode, like the tiles. The header says "live" so nobody mistakes them for the selected sample.
- **Missing values.** An unavailable value shows the existing dash (`Strings.MissingReading`), never zero.
- **"Busiest process" rows** follow one fixed rule, described in [Busiest process](#busiest-process), whatever the process table is currently showing.
- **Compact view.** The compact boxes show the same tooltip, by mouse or keyboard.

Left out on purpose:

- Per-disk process I/O. Windows process I/O counters include network and device I/O, so a "busiest on this disk" row would be wrong.
- IP addresses. Screenshots of WinMon get shared in issues. The Info window already lists adapters.
- GPU temperature. The README already says it is outside this view.

## Design

### Data

The new readings are nullable fields on `ResourceSample`, following its rule: null means unavailable, never zero.

```csharp
public double? CpuKernelPercent { get; init; }
public ulong? AvailableMemoryBytes { get; init; }
public ulong? CommittedBytes { get; init; }
public ulong? CommitLimitBytes { get; init; }
public ulong? CachedBytes { get; init; }
public ulong? PagedPoolBytes { get; init; }
public ulong? NonPagedPoolBytes { get; init; }
public uint? ProcessCount { get; init; }                    // machine-wide, from GetPerformanceInfo
public uint? ThreadCount { get; init; }
public uint? HandleCount { get; init; }
public double? DiskReadBytesPerSecond { get; init; }       // and Secondary…
public double? DiskWriteBytesPerSecond { get; init; }
public double? DiskResponseSeconds { get; init; }
public double? DiskQueueLength { get; init; }
public ulong? DiskFreeBytes { get; init; }
public ulong? DiskTotalBytes { get; init; }
public ulong? ReceivedBytesSinceStart { get; init; }
public ulong? SentBytesSinceStart { get; init; }
public ulong? GpuSharedMemoryUsedBytes { get; init; }
public string GpuName { get; init; } = "";
```

Putting them on the sample, rather than reading them only when a tooltip opens, means history has them too. Copy capture and the chart's history tooltip can show them later at no extra sampling cost. About 20 fields × 3,600 samples is negligible next to process entries.

**Process, thread and handle counts come from Windows, not from WinMon's process list.** `LatestFull.Processes` lists only processes the CPU sampler could read and had a baseline for. `WindowsCpuSampler.CreateProcessUsages` skips each process's first observation, and processes whose CPU time cannot be read are only counted in `UnavailableProcesses`. Summing that list would understate the machine. `GetPerformanceInfo`, which this design already calls for memory, returns machine-wide totals in the same call. If that call fails, the three rows show the dash. They never fall back to the sampled list, which would give a smaller number that looks correct.

### Busiest process

The CPU, Memory and GPU tooltips each show one busiest process, chosen by this rule:

```csharp
ProcessRanking.Rank(live, sort: metric, grouped: false, includeInactive: false).Processes.FirstOrDefault()
```

- **`live`** is the live history ending at `LatestFull`. It is the same `SampleList` the process view builds when no capture is selected, so the ranking runs on exact, uncompacted values.
- **Window:** `ProcessRanking`'s default five-second window.
- **Metric:** CPU → `ProcessSort.Cpu`, Memory → `ProcessSort.Memory` (working set, as Individual mode ranks it), GPU → `ProcessSort.Gpu`. Disk and Network have no busiest-process row.
- **Scope:** Individual processes only. No grouping, search, App only, Pinned only, pins or selection.
- **Display:** process name and that metric's value. A WinMon row is shown like any other, labelled as WinMon.
- **None:** if no process qualifies, for example no GPU readings, the row shows the dash.

Because the rule is fixed, the tooltip will **differ from the process table's top row** whenever the table is grouped, sorted by another column, searched, filtered, has pins above the ranking, or is showing a capture. That is intended. The tooltip answers "what is busiest on the machine right now?", and the table answers whatever question its settings ask. The row label says "Busiest process (live)" so the difference is not mistaken for a bug.

Ranking runs on the UI thread only while that tooltip is open, at most once per refresh. It covers five samples of about 1,250 processes, the same work as one process-table refresh.

**Free space** is not read in the 1-second capture. `DriveInfo` can block on a sleeping removable drive and stretch the sampling interval past the gap threshold. A separate 30-second background read publishes the last value, which the capture copies into each sample.

### Tooltip content

A WPF element can have only one parent, so the compact boxes cannot share a `UIElement` tooltip with the full tiles. `MainWindow.Compact.cs` copies `source.ToolTip` today, which works only because it is a string. So:

- Each tile owns a `MetricDetails` view model: title, definition, and a fixed list of `DetailRow` (label, value), all raising `PropertyChanged`.
- `ToolTip` is set once to that model, not every tick. A `DataTemplate` for `MetricDetails` in `App.xaml` renders it, giving each tooltip its own visual tree from the shared model.
- **Where it is set.** Hovering and keyboard focus need the tooltip on different elements, so the same model goes on each:

  | Layout | Mouse | Keyboard focus |
  | --- | --- | --- |
  | Full | The `MetricReadout` tile (a `StackPanel`, not focusable) | Its `Toggle` check box, the element that actually takes focus |
  | Compact | The compact `Border` box | The same box, made focusable (see [Accessibility](#accessibility)) |

  When the mouse is over the toggle, WPF shows the toggle's tooltip rather than the tile's, but both show the same model, so the user sees no difference. `ShowsToolTipOnKeyboardFocus` goes on the element that takes focus, never on a parent.
- `UpdateResources` writes values into the rows, which updates an open tooltip in place. Rows compare text before setting it, the same way `MetricReadout.ShowValue` avoids relayout, so a closed tooltip costs a string format per row per tick and nothing else. Formatting can be skipped entirely unless `ToolTipService` reports the tooltip as open, if measurement shows it matters.
- The template is a two-column grid: muted labels on the left, right-aligned tabular values. The existing tooltip style sets the width, and long names trim with an ellipsis.

The existing detail-label tooltips (CPU clock, GPU memory) fold into the new tooltip and are removed, so each tile has one tooltip.

### Accessibility

- **Full view.** Each tile's `Toggle` gets `ToolTip` set to the tile's model and `ToolTipService.ShowsToolTipOnKeyboardFocus = true`. Tabbing to a chart toggle opens the same table as hovering the tile.
- **Compact view.** The compact boxes are not focusable today: the `Border` built in `InitializeCompactView` has no focusable child, and its graph is not hit-testable. So compact view has no keyboard access to the readings at all. Each box becomes a tab stop:
  - `Focusable = true`, in left-to-right order;
  - the existing focus visual style;
  - `AutomationProperties.Name` set to the label and value, for example "CPU 24.3%";
  - `ShowsToolTipOnKeyboardFocus = true`, with the box's own `ToolTip` set to the shared model.

  The boxes take no action on Enter or Space, so they are read-only stops.
- **Screen readers.** `AutomationProperties.HelpText` on the toggle and the compact box keeps the definition sentence. Screen readers do not get a live table that changes every second, which would be noisy.

### Localization

About 25 new strings: row labels, the "live" header, and units such as "ms" and "free". Each needs all 10 languages, following `Resources/README.md`. Numbers use the existing culture-aware formats.

## Decisions

| Area | Decision |
| --- | --- |
| Where the data lives | On `ResourceSample`, so history and Copy capture can use it later. Machine-wide counts come from `GetPerformanceInfo`, never from the sampled process list. |
| Busiest process | A fixed live rule: Individual processes, the tile's metric, five-second window, no filters. It is not tied to the process table's current view. |
| Tooltip type | A data object with a `DataTemplate`, shared by the full and compact tiles and updated in place. |
| Keyboard | The tooltip goes on the element that takes focus: the chart toggle in full view, and the compact box, now a tab stop, in compact view. |
| Free space | 30-second background read, never inside the capture. |
| Process I/O on disk tiles | Left out, because it would be misleading. |
| History | Not in this change. Adding the new fields to the chart tooltip and Copy capture is a follow-up. |

## Performance

Record `WinMon.Performance` before and after on the same machine, as [STRUCTURE.md](STRUCTURE.md) requires. Expected cost: four extra PDH counters per disk in an existing query, one counter in the GPU memory query, and one `GetPerformanceInfo` call per second. Check two things specifically:

- The live scenario's CPU, which must be within noise.
- A refresh with a tooltip open, against one with tooltips closed.

## Tests

- Sampler: the new fields are null when their counter fails, and a failed disk counter leaves memory, network and CPU readings intact (the existing PDH isolation rule).
- Kernel split: user + kernel equals total busy within rounding, with idle excluded from kernel.
- Network totals reset when the adapter changes, and never go negative after an adapter counter reset.
- `MetricDetails`: unchanged values do not raise `PropertyChanged`, and unavailable values show the dash.
- Machine counts: process, thread and handle rows show the `GetPerformanceInfo` values, including when the sampled list is shorter (a fixture with first-seen and unreadable processes). They show the dash when the call fails, never the sampled count.
- Busiest process, tested against the rule rather than the table:
  - for fixture histories, each tooltip's process equals `ProcessRanking.Rank(live, metric, grouped: false, includeInactive: false)`'s first row;
  - it is unchanged when the table is grouped, sorted by another column, searched, filtered, has pins, or is showing a capture;
  - with no qualifying process it shows the dash.
- Keyboard, native diagnostics:
  - in full view, focusing each chart toggle opens its tooltip with the tile's model;
  - in compact view, Tab moves through the visible boxes left to right, skipping a hidden secondary disk, and each opens its tooltip;
  - each compact box's automation name matches its label and value.
- Diagnostics: add the open CPU, disk and GPU tooltips to the existing dark and light tooltip captures (`CaptureToolTipAsync`), in full and compact view.

## Sequence

1. Sampling: the `ResourceSample` fields, the PDH counters, `GetPerformanceInfo`, the free-space reader and network totals, with tests and a benchmark.
2. `MetricDetails`, its template, the busiest-process rule, and wiring for all five tiles: tile and toggle in full view, plus focusable compact boxes. Remove the old string tooltips.
3. Localized strings, diagnostics captures and a README line under "Your system, at a glance".
