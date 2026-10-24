# Usage tab

Proposed on 2026-10-24. Status: design, not started.

## Why

Processes and Services answer "what is busy now, or at this moment?" They do not answer "what has used my machine since I started looking?" A browser that idles at 2% but never stops, or an updater that ran for 20 minutes while you were away, barely shows in a five-second ranking but dominates the total.

The Usage tab shows, per app, the total **CPU time**, **I/O** and **GPU time** since WinMon started. Totals survive [update restarts](UPDATE-RESTART-HISTORY.md) and are cleared on a normal exit. Persisting across exits or reboots, Task Manager's 30-day "App history", is out of scope.

## Behavior

A third tab, **Processes | Services | Usage**:

```
Usage since 09:12 (2 h 14 min) · machine busy 3 h 40 min CPU on 12 logical processors   [Copy usage] [Reset]
Top [20] apps by [CPU time]      Search…                                      ☐ App only

App                     CPU time   Share    I/O        GPU time   Processes   Last active
● Google Chrome         1:12:40    33.0%    4.8 GB     0:21:03    41 (18)     now
● Visual Studio Code    0:31:18    14.2%    1.1 GB     0:00:42    12 (9)      now
  MsMpEng               0:24:51    11.3%    9.6 GB     —          1 (1)       now
  Windows Update        0:18:30     8.4%    2.2 GB     —          3 (0)       10:47
…
  Not attributed        0:06:12     2.8%                                       ⓘ
```

### Rows

One row per executable, using the same identity as the Processes **Grouped** mode (the image path), so the two views agree. A process whose path cannot be read is keyed by its name, for example `System`, `Registry` or `MemCompression`. Apps that have exited stay in the list; that is the point of the tab.

### Columns

| Column | Meaning |
| --- | --- |
| **App** | Icon and display name: the executable's description, else its name. **App** and **Background** labels as in Processes: an app if any of its processes ever had an app window this session. |
| **CPU time** | Total processor time across all its processes, as `h:mm:ss`. On a multi-core PC it can grow faster than the clock: 4 busy cores add 4 s per second. |
| **Share** | Its CPU time as a percentage of **machine busy CPU time** this session: the header's total, summed from `TotalCpuPercent × IntervalCpuTicks` over valid samples. Every row uses this denominator, including Not attributed, so the app rows plus Not attributed add up to 100% within rounding. |
| **I/O** | Total read + write bytes, the same counter as the Processes I/O column. Like that column, it includes file, network and device I/O. |
| **GPU time** | Time its busiest GPU engine was busy, summed per interval. A dash if no GPU reading was ever available. It is not additive across engines, matching the Processes GPU column. |
| **Processes** | Process lifetimes seen this session, with the number running now in brackets. |
| **Last active** | `now`, or the time of the last interval with any CPU, I/O or GPU activity. |

### Controls

- **Top N and sort.** **Top [N] apps by [metric]** and clickable column headers, reusing the Services toolbar pattern: 5 to 50 or All, default 20. Sortable by CPU time, I/O, GPU time, Processes and Last active.
- **Search and filter.** Search by display name, executable name or path, plus **App only**. Both apply before the Top N limit.
- **Copy usage** copies the visible table as plain text, with the "since" time, sort, filters and units, the same style as Copy capture.
- **Reset** asks for confirmation through `MessageDialog`, then clears the totals and sets "since" to now.
- **Row menu:** **Copy details**, **Open file location**, and **Show in Processes** (described below).

### Show in Processes

This selects the app's executable group in Processes **by identity**, never by search text. `ProcessSearch` matches names, descriptions, products and PIDs, but not paths, so searching for `node.exe` could land on a different `node.exe`.

- **Preparing the list.** It switches to Processes, turns on Grouped, and clears the search, App only and Pinned only, so nothing hides the target.
- **Selecting the group.** It uses the existing grouped selection (`ProcessesView.SelectProcess(…, grouped: true)`), passing a representative `ProcessUsage` for the app that the ledger keeps per key. The selection key is `ProcessRanking.GroupKey`, the same key as the Usage row. Grouped selection already follows the executable through rank changes and filters, and the action bar shows it.
- **Which moment it shows** depends on whether the app is still around:

  | App state | Result |
  | --- | --- |
  | Running now (Running > 0) | Processes in Live, with the group selected. |
  | Exited, and its `LastActive` sample is still in history | Capture mode at that exact sample, matched by `SampleKey` like an automatic-capture marker, with the group selected. The table shows what the app was doing when it was last active. |
  | Exited, and `LastActive` has left history, or it was never active | The menu item is disabled, with the tooltip "Not in recorded history. History keeps the last N minutes." |

  An exited app shown in Live would select a group with no current members and show nothing useful, which is why the middle case opens a capture instead.
- **Freshness.** Availability is checked when the menu opens, against the current history. If the sample expires between opening the menu and clicking, the click falls back to the disabled case's message in the footer line.

### Not attributed row

A **Not attributed** row is pinned at the bottom and never ranked. It holds machine busy CPU time minus the CPU time attributed to apps, both over the same valid samples, and its Share uses the same denominator as every app row. Process and system times come from one snapshot, so they agree closely. If rounding ever makes the attributed sum slightly larger, Not attributed shows 0:00:00 and 0.0%, never a negative value. The gap comes from:

- processes that started and exited between samples;
- each process's first interval, which has no baseline;
- each process's final partial interval before it exits;
- processes whose CPU time WinMon could not read (`UnavailableProcesses`).

Its tooltip lists those reasons. Showing the gap is more honest than quietly letting the columns sum to less than the machine total.

### Interaction with the rest of WinMon

- **Capture mode.** The tab always shows live totals. Selecting a historical sample does not change it, and a one-line note says so while Capture mode is on. Totals at an arbitrary past moment would need per-sample snapshots of the ledger. That is not worth the memory.
- **Compact view.** Hidden, like Services.
- **Hidden or minimized.** Totals keep accumulating, as recording does. The view refreshes only while visible.

## Design

### Exact interval amounts

`ProcessUsage` stores rates and shares, not amounts, so the ledger needs each interval's length and size. Two fields go on `MonitorSample`, set by `WindowsCpuSampler`, which already has both values:

```csharp
/// <summary>Seconds this sample's interval covers; zero for gaps and fixtures.</summary>
public double IntervalSeconds { get; init; }

/// <summary>Machine CPU capacity in the interval, in 100-ns ticks across all logical processors.</summary>
public long IntervalCpuTicks { get; init; }
```

| Total | Interval amount |
| --- | --- |
| Process CPU ticks | `CpuPercent / 100 × IntervalCpuTicks`, which is exactly the sampler's `processDelta` because `CpuPercent = 100 × processDelta / totalDelta` |
| Machine busy ticks (for Not attributed) | `TotalCpuPercent / 100 × IntervalCpuTicks` |
| I/O bytes | `IoBytesPerSecond × IntervalSeconds` |
| GPU busy seconds | `GpuPercent / 100 × IntervalSeconds` |

That is 16 bytes per sample, not per process entry, so history memory does not change measurably. Gap samples carry zero and add nothing.

### Ledger

`UsageLedger` lives in `Monitoring`, with no WPF:

```csharp
internal sealed class UsageLedger
{
    internal DateTimeOffset Since { get; }
    internal void Add(MonitorSample full);              // one live, uncompacted sample
    internal IReadOnlyList<AppUsage> Apps { get; }      // copied on read, like MonitorHistory.Samples
    internal AppUsage NotAttributed { get; }
    internal long MachineBusyTicks { get; }             // the denominator for every Share
    internal void Reset(DateTimeOffset now);
}

internal sealed record AppUsage(string Key, string Name, string ImagePath, string AppName, string Publisher,
    long CpuTicks, double IoBytes, double? GpuSeconds, int Lifetimes, int Running, bool IsApp,
    SampleKey? LastActive);   // the last valid sample with activity; see "Show in Processes"

// SampleKey (MonotonicTimestamp, Timestamp) is shared with automatic-capture markers.
```

- **Owner.** `MonitorSession` owns it (`Session.Usage`) and feeds it inside `Record` whenever a full sample is passed. A live drain passes one. The [restart-history](UPDATE-RESTART-HISTORY.md) restore passes compacted samples only, and restores the ledger from its own file section, so restored samples are never counted twice. Diagnostics that pass full fixtures get usage automatically.
- **Input.** Only full samples. Compaction drops only processes with no CPU, I/O or GPU reading, so a compacted sample would give the same CPU, I/O and GPU totals. **Running** and **Lifetimes**, however, need idle processes too. Taking the full sample also gives `Record` a clean rule: live frames carry a full sample and restored ones do not.
- **Key.** Each app is keyed by `ProcessRanking.GroupKey`: the full path, or the name when the path is unreadable. That is the exact key Grouped mode uses, so the two views can never disagree about what one app is.
- **Valid samples only.** The ledger ignores any sample where `IsGap` is set or `TotalCpuPercent` is null. `WindowsCpuSampler` returns those with an **empty process list** after a first capture, a long gap, or a bad CPU counter delta. A failed capture never reaches `Record` at all. An ignored sample adds nothing, changes no **Running** count, and is **not** evidence that any process exited. While samples are invalid, **Running** keeps its last valid value.
- **Counted lifetimes.** The ledger keeps a set of counted lifetimes, keyed by `(ProcessId, CreationTicks)`. The creation time makes a lifetime unique for the whole boot, so a lifetime seen again is always the same process, never a new one. Each valid sample does three things:
  - A lifetime in the sample but not in the set is a new **Lifetime** for its app. It is added with its app entry cached, so the image path is hashed once per lifetime, not once per second.
  - A lifetime in the sample has its absence count reset to zero.
  - A lifetime missing from the sample has its absence count increased by one. A lifetime is retired from the set only after it has been missing from **300 consecutive valid samples**. Gaps don't advance the count, so a sleep, a long gap or a run of failed captures never retires a process that is still running.

  The 300-sample grace also covers a process that drops out of one sample, for example when its CPU time reads backwards. **Running** is the number of lifetimes in the latest valid sample. Memory is bounded by the process start rate over about five minutes: a set entry is about 40 bytes, so even 10,000 lifetimes in five minutes is about 400 KB.
- **Across an update restart.** The ledger chunk saves the counted-lifetime set with each entry's app key and absence count, alongside the totals and Since. After restoring it, a process that kept running through the update keeps its `(ProcessId, CreationTicks)` and is recognized, not counted again. WinMon's own process is correctly counted as a new lifetime, because it is one. The first sample after the restart is a gap, so the restored set is first compared against the first valid post-restart sample.
- **Bound.** At most 4,096 apps. Further executables fold into one "Other apps" row, so a machine that spawns thousands of uniquely named temp executables cannot grow memory without limit. Each entry is a few hundred bytes.

### View

`UsageView : UserControl` in `Controls/UsageView.xaml`, following the [code structure](STRUCTURE.md) view rule: one `Update(UsageFrame)` call, with rows built in code like `ProcessRow`.

- `MainWindow` adds `UsageTab` to the `ActivityView` radio group and calls `Update` from `RefreshPresentation` only while the tab is selected and the window is visible.
- List settings (top count, sort, App only) go into `MonitorPreferences` as a `UsageListSettings` record, the same way `ProcessListSettings` works: the window stores them and the view raises `SettingsChanged`. Old settings files load the defaults.
- Sorting at most 4,096 rows once a second is negligible, but the view still keeps rows and updates them in place to avoid layout churn, the way `ProcessesView` does.

## Decisions

| Area | Decision |
| --- | --- |
| Period | Since WinMon started, carried across update restarts, with a manual Reset. No multi-day history. |
| Grouping | Per executable only, with the same key as Grouped. Per-lifetime rows would mostly show exited PIDs nobody recognizes. |
| Source | Uncompacted live samples via `Record`, with exact interval amounts from two new `MonitorSample` fields. |
| Honesty | A visible Not attributed row instead of hiding the gap. |
| Share | Machine busy CPU time is the denominator for every row, including Not attributed. |
| Gaps | Gap and invalid samples are ignored entirely. They never retire lifetimes or change Running. |
| Lifetimes | A set keyed by `(ProcessId, CreationTicks)`, retired after 300 consecutive valid samples of absence, and saved across update restarts. |
| Show in Processes | Selects the group by `GroupKey`, not by search: Live if running, Capture at `LastActive` if exited but still in history, otherwise disabled. |
| I/O | Combined read + write, like Processes. A read/write split needs the sampler to keep them separate and is a follow-up. |
| Capture mode | Usage stays live. |
| Memory | 4,096-app cap with an Other apps bucket. |

## Docs to change

- README: a "See what used your PC this session" section after Services, with a screenshot from the example data set.
- PRIVACY.md: none for the tab itself, because totals stay in memory. The restart-history note already covers carrying them across an update.

## Tests

- Interval amounts: process CPU ticks from `CpuPercent × IntervalCpuTicks` equal the sampler's deltas, using fixture `CpuSnapshot`s through `WindowsCpuSampler`.
- Ledger:
  - gaps add nothing;
  - lifetime and Running counts after a sequence of valid → gap (empty list) → valid → bad CPU delta (empty list) → valid samples equal those of the same valid samples with no gaps in between;
  - a lifetime missing from one valid sample and back in the next is not counted twice;
  - a lifetime is retired only after 300 consecutive valid absences, and gaps don't advance the count;
  - a PID reused by a new lifetime counts as a new lifetime under the right key;
  - keys equal `ProcessRanking.GroupKey` for path and pathless processes;
  - Share: every row, including Not attributed, divides by `MachineBusyTicks`, and the rows sum to 100% within rounding;
  - processes without a path are keyed by name;
  - exited apps keep their totals with Running at 0;
  - Not attributed equals machine busy minus the attributed sum, and is never negative after rounding;
  - the 4,096 cap folds into Other apps;
  - Reset clears totals and moves Since.
- No double counting: restored samples plus a restored ledger give the same totals as an uninterrupted session, given the same live samples. Processes running through the restart are not counted as new lifetimes; WinMon's own new process is.
- View: sorting, Top N, search and App only applied before the limit; Copy usage text.
- Show in Processes, with two different executables both named `node.exe`:
  - a running app selects its own group in Live, with Grouped on and the search and filters cleared;
  - an exited app still in history opens Capture at its exact `LastActive` sample with its group selected;
  - an app whose `LastActive` has left history has the item disabled;
  - if the sample expires between the menu opening and the click, the click shows the footer message instead.
- Performance: a Usage-tab scenario in `WinMon.Performance`, live and with the tab hidden, recorded in the developer guide. The ledger's per-second cost must be within noise of the existing live scenario.

## Sequence

1. `MonitorSample.IntervalSeconds` and `IntervalCpuTicks`, with sampler tests.
2. `UsageLedger` and its session wiring, with tests.
3. `UsageView`, the tab, preferences, row actions, Copy usage and Reset.
4. Strings for all 10 languages, diagnostics captures in light and dark, and the README section.
5. With [UPDATE-RESTART-HISTORY.md](UPDATE-RESTART-HISTORY.md): serialize the ledger in its ledger chunk.
