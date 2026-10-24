# Automatic captures

Proposed on 2026-10-24. Status: design, not started.

## Why

Capture mode lets you look back at a spike, but only if you notice it within the retention window, at most 60 minutes. The spikes that hurt most happen while you are away from the screen: the fan spins up overnight, a game stutters while you are playing, the build machine stalls over lunch. By the time you look, the moment has scrolled out of history.

Automatic captures watch for a condition you set, such as "CPU above 90% for 10 seconds". When it holds, WinMon marks the moment on the chart and saves the same plain-text report **Copy capture** produces to a folder, so the evidence outlasts history.

## Behavior

### Rules

Settings gains an **Automatic captures** section, off by default:

```
Automatic captures                                  [ On ]
  ☑ CPU            above [ 90 ] %      for [ 10 ] s
  ☐ Memory         above [ 90 ] %      for [ 30 ] s
  ☐ Disk C:        above [ 95 ] %      for [ 15 ] s
  ☐ Disk D:        above [ 95 ] %      for [ 15 ] s
  ☐ Network        above [ 50 ] Mbps   for [ 10 ] s
  ☐ GPU            above [ 95 ] %      for [ 10 ] s
  ☐ Any process    above [ 90 ] % of one core  for [ 30 ] s
  Wait at least [ 5 ] min between captures of the same rule
  Save reports to  %LOCALAPPDATA%\WinMon\Captures     [Change…] [Open]
  ☐ Show a notification
```

- **One rule per resource.** A fixed list, not a rule builder, keeps the UI to one row per resource and covers the common cases. Disk rows follow the configured volumes. Thresholds use each tile's own unit.
- **Any process** fires when a single process (Individual, not grouped) stays above the threshold. It is the runaway-process case, which the machine-wide CPU rule misses on many-core PCs. Its threshold is in **percent of one logical processor** (`CpuPercent × ProcessorCount`), not percent of the machine. A spinning single-threaded process reads only 6% of the machine on 16 logical processors, so a machine-percent threshold would never catch it.
- **Duration** is how many consecutive seconds the condition must hold, 1 to 300. A missing reading or a gap breaks the run. It does not count as below the threshold, but it does not count toward the duration either.

### When a rule fires

1. **Marker.** A small marker appears at the top edge of the chart at the moment the duration completed. Hovering it shows the rule. Clicking it opens Capture mode at the run's **peak** sample, or opens the saved report once the peak has left history (see [Markers](#markers)).
2. **Report.** A report is saved to the folder (see [Report](#report)).
3. **Notification** (only if enabled). "CPU above 90% for 10 s — chrome 62%". Disk and Network notifications name no process (see [Report](#report)). Clicking it shows WinMon and selects the peak sample if it is still in history; otherwise it opens the report.

After firing, a rule **re-arms** only when both of these are true:

- **Cooldown:** the configured time has passed since that rule last fired.
- **Hysteresis:** the value has dropped below the threshold minus a margin for at least the rule's duration. The margin is 5 points for percentages and 10% of the threshold for network.

Without the hysteresis, a value hovering at the threshold would fire again as soon as the cooldown ends.

Captures keep working while WinMon is hidden, minimized or in Compact view, because recording does. They also keep working while you are browsing history in Capture mode: rules evaluate live samples, never the selected one.

### Report

The same text `SnapshotText.Format` produces for Copy capture, with fixed list settings so the report does not depend on what the view happened to show:

- the peak sample of the run;
- top 20 processes, Individual, no search or filters, sorted by the rule's resource (CPU, memory, I/O for disk and network, GPU);
- a header block above the usual report:

```
WinMon automatic capture v1 · id 3f2a9c4e0b7d4a1e9c2f5b6a7d8e9f01
Automatic capture: CPU above 90% for 10 s
Fired 2026-10-24 14:03:12 +01:00; peak 98.2% at 14:03:08
CPU over the run: 91.0 94.3 97.7 98.2 96.1 93.0 92.4 95.5 97.1 94.8
```

**Disk and Network reports** sort the process list by I/O, but WinMon cannot tell which processes caused a volume or adapter spike. Windows process I/O counters lump file, network and device I/O together, across all drives and adapters. So these reports must not suggest otherwise:

- The process section is headed "Processes by total I/O (all files, network and devices)", not "Top processes".
- A line under the trigger header reads: "Process I/O is not broken down by drive or network adapter. These processes may not have caused this activity."
- The notification for these two rules names only the rule, for example "Disk C: above 95% for 15 s". It never names a process.

CPU, Memory, GPU and Any process attribute directly per process, so their reports and notifications name the busiest process as above.

### File names and pruning

The folder can be any writable folder you choose, including one that holds other files, so WinMon must only ever delete reports it wrote itself.

**Names** follow one WinMon-specific pattern: `WinMon-capture_2026-10-24_14-03-12_cpu.txt`.
- The timestamp is in local time, followed by a fixed rule token: `cpu`, `memory`, `disk1`, `disk2`, `network`, `gpu` or `process`.
- Two reports in the same second get `_2`, `_3` and so on.
- The rule tokens are never localized, so the pattern stays the same in every UI language.

**First line.** Every report starts with a fixed, unlocalized line:

```
WinMon automatic capture v1 · id 3f2a9c4e0b7d4a1e9c2f5b6a7d8e9f01
```

Anything after that line is localized as usual.

**Pruning.** The folder keeps the newest **200** reports, and the limit is shown next to the folder setting. When a new report is written, WinMon deletes older ones, but it treats a file as its own only if **all** of these hold:

- it sits directly in the folder; subfolders are never searched;
- its name matches the pattern exactly, checked with an anchored regular expression, not a wildcard;
- it is a regular file: not a directory, reparse point or symbolic link, and not read-only, hidden or system;
- its first line matches the WinMon line, read with a bounded read of at most 128 bytes.

A file that fails any check is never deleted and does not count toward the 200. That covers a renamed report, an edited first line, or someone else's `WinMon-capture_*.txt`.

Reports are ordered by the timestamp in their name, not by file times, which copying or syncing can change. If a check fails because a file cannot be read, WinMon leaves it alone and traces the error.

There is deliberately no manifest file. It would fall out of step as soon as someone moves, deletes or syncs reports. Each report proves on its own that WinMon wrote it.

## Design

### Evaluation

`CaptureRules` lives in `Monitoring`, a pure state machine with no WPF or file I/O, so it is unit-testable with synthetic samples:

```csharp
internal sealed record CaptureRule(CaptureMetric Metric, double Threshold, int Seconds, TimeSpan Cooldown);

internal sealed class CaptureRules
{
    // Feed every live sample in order; returns the rules that fired on this sample.
    internal IReadOnlyList<CaptureFired> Observe(MonitorSample full);
}

internal sealed record CaptureFired(CaptureRule Rule, MonitorSample Peak, IReadOnlyList<double?> Run,
    ProcessUsage? Process);
```

- **Input.** It reads `Session.LatestFull`, the uncompacted sample, so the Any process rule sees every process.
- **Per-process state** is keyed by `(ProcessId, CreationTicks)` and dropped when the process leaves the sample, so a reused PID cannot inherit a run.
- **Thread.** It runs in the window's `OnSamplesRecorded` handler, on the UI thread, once per drained live sample. Most ticks drain one sample. The session needs to expose each drained sample, not only `LatestFull`, so a delayed drain of two frames does not skip one. That is a small `Recorded` change: pass the drained full samples.
- **Not evaluated:** restored samples from an [update restart](UPDATE-RESTART-HISTORY.md), samples recorded by diagnostics through `Record`, and anything while `_diagnosticMode` is set.

### Report writing

The shell builds the `SnapshotReport` on the UI thread, which is cheap and needs `ProcessRanking`. It then writes the file and prunes the folder on the thread pool. A write failure is traced and shown once as a dismissible footer line, like the update status, and disables nothing. A full disk does not throw every few minutes into the UI.

The **Any process** report sorts by CPU and puts the triggering process first, even if a bigger process overtook it at the peak.

### Markers

- **Storage.** The window holds a list of markers, oldest first. They are not saved; the report on disk is the durable record, and markers are lost on an [update restart](UPDATE-RESTART-HISTORY.md).

  ```csharp
  internal sealed record CaptureMarker(
      CaptureRule Rule,
      SampleKey Fired,        // where the marker is drawn: the sample that completed the duration
      SampleKey Peak,         // what a click selects: the run's peak sample
      string? ReportPath,     // null until the write succeeds, or if it failed
      Guid ReportId);         // the id in the report's first line

  internal readonly record struct SampleKey(long MonotonicTimestamp, DateTimeOffset Timestamp);
  ```

- **Finding the peak.** A click finds the history sample whose `MonotonicTimestamp` and `Timestamp` both equal `Peak`, the same identity `ServiceTree` uses to match a frame to its sample. It never searches by nearest time, which could land on a neighbour after compaction or a gap. Selection goes through the existing Capture-mode path, so the process table follows it.
- **When the peak leaves history.** The peak is never newer than the firing sample, so it always expires first. The marker stays drawn until `Fired` leaves history, then it is removed. In between:
  - its tooltip adds "Peak no longer in history";
  - a click opens the saved report instead, if `ReportPath` still exists;
  - if the report doesn't exist either, a click shows "This capture is no longer in history and its report was not found" in the dismissible footer line.
- **Notification clicks** follow the same order: select the peak if it is in history, otherwise open the report, otherwise show the footer line.
- **Drawing.** `CpuGraph` draws them as small triangles on the top edge in the accent color, with an automation peer entry per marker so they are reachable without a mouse.
- **Navigation.** In Capture mode, **Ctrl+Left / Ctrl+Right** jumps to the previous or next marker, next to the existing Left/Right sample stepping.

### Notification

The existing `NotifyIcon` shows a balloon (`ShowBalloonTip`), which Windows renders as a toast. It does not need a toast registration, which would differ between the installer, portable and MSIX builds. One notification per rule firing, and at most one per minute across all rules. Windows Focus Assist and Do Not Disturb apply as usual.

### Preferences

Rules, cooldown, folder, notification and the master switch go into `MonitorPreferences`, in the existing JSON. Missing values mean off with the defaults shown above. Any process uses its own threshold field because its unit (percent of one core) differs from machine CPU. The disk rules are keyed by role, primary or secondary, not by letter, so changing the volume in Settings keeps the rule.

**Folder.** The default is `%LOCALAPPDATA%\WinMon\Captures`, or a `Captures` folder beside the executable for portable builds, matching where `PreferenceStore` puts settings. A custom folder is validated on save: it must be creatable and writable.

## Decisions

| Area | Decision |
| --- | --- |
| Rule model | One fixed row per resource, plus Any process. No rule builder, AND/OR or per-process names in v1. |
| Default | Off. Reports contain process names and paths, so writing them is opt-in. |
| What is saved | The plain-text Copy capture report with a trigger header. No screenshots and no binary history dump: text is readable, diffable and safe to paste into an issue after review. |
| Peak sample | The report and marker click use the run's peak, not its last sample, because the peak is what you want to explain. |
| Re-arming | Cooldown and hysteresis together. |
| Folder bound | 200 newest reports, pruned by count, not size. Reports are a few KB each. |
| What may be deleted | Only top-level regular files matching the exact WinMon name pattern and starting with the WinMon first line. No manifest, no recursion, never anything else in a shared folder. |
| Marker identity | A marker stores both its firing and peak samples by monotonic and wall-clock timestamp. A click selects the exact peak, or falls back to the report once the peak has left history. |
| Disk and network attribution | The process list is labelled as total process I/O and carries a disclaimer. Notifications for these rules name no process. |
| Notification | Tray balloon, off by default, rate-limited. |

## Docs to change

- README: an "Automatic captures" paragraph under "Catch the spike—even after it passes", and the Settings list.
- PRIVACY.md: when enabled, WinMon writes capture reports containing process names, IDs, executable paths and measurements to the chosen folder, and keeps the newest 200. It deletes only files it wrote, identified by name and first line. Nothing is uploaded.

## Tests

`CaptureRules`, driven by synthetic samples:

- fires exactly at N consecutive seconds;
- a gap or an unavailable reading breaks the run;
- the cooldown suppresses a second firing;
- hysteresis requires the drop below the threshold minus the margin, held for the rule's duration;
- Any process ignores a PID reused by a new process lifetime;
- the peak is the maximum of the run;
- two frames drained together are both evaluated.

Other tests:

- Report:
  - the unlocalized first line, header format and localized text;
  - fixed sorting per rule, and the triggering process first for Any process;
  - Disk and Network reports have the total-I/O heading and disclaimer, and their notifications name no process.
- File naming: the pattern in every UI language, collisions in the same second, and `disk1`/`disk2` tokens when the volume letters change.
- Pruning, in a temporary folder that also holds files that must survive:
  - unrelated `.txt` files;
  - a `WinMon-capture_*.txt` without the first line;
  - a near-miss name such as `WinMon-capture_2026-10-24_cpu.txt`;
  - a renamed report;
  - read-only, hidden and system copies of real reports;
  - a subfolder of reports, and a symbolic link to a report;
  - an unreadable file.

  None of those are deleted or counted. Only genuine reports beyond the newest 200 are, ordered by the name's timestamp even when file times have been changed.
- An unwritable folder is reported without exceptions reaching the UI.
- Markers:
  - a click selects the sample with the exact peak key, never a nearer neighbour;
  - after the peak leaves history but before the firing sample does, the marker stays and a click opens the report;
  - with the report deleted, a click shows the footer line;
  - the marker is removed once the firing sample leaves history;
  - notification clicks follow the same fallback order.
- Preferences round trip, and old settings without the new fields load as off.
- Diagnostics: a smoke mode that injects a synthetic CPU run and checks the marker, the report file, Capture-mode selection on marker click and Ctrl+Left/Right. Light and dark captures of the Settings section.

## Sequence

1. `CaptureRules` with tests, and the session change that passes each drained sample.
2. Preferences and the Settings section.
3. Report writing, folder pruning and the error footer line.
4. Chart markers, marker navigation and click-to-select.
5. Notification.
6. README, PRIVACY.md, strings for all 10 languages and diagnostics captures.

Steps 1 to 3 are a useful release on their own: reports on disk with no chart changes.
