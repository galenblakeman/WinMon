# Longer resource trends

Status: implemented, 2026-11-21. Automated validation and remaining manual checks are recorded below.

## Outcome and scope

Answer “has this machine been getting busier over the last several hours?” in History. Keep seven days of resource readings as one-minute observed averages, with 24-hour and 7-day views. Preserve the existing detailed recording and daily app Usage totals for their current jobs.

The first version records machine CPU, memory, the two configured disks, the selected network adapter and the selected GPU. It does not keep week-long process inventories, add another collector, collect while WinMon is closed, diagnose causes, or change incident files. Start recording new observations after installation; neither daily Usage totals nor restored compacted history can reconstruct this archive.

## Current code and integration points

Inspected against product version 4.2.0: desktop targets `net10.0-windows` (the Store path adds Windows 10.0.17763.0), and MonCore targets `net10.0`.

- `src/MonCore/Monitoring/MonitorHistory.cs`: recent observations are bounded by elapsed time, sample count and a 500,000-process-entry budget. The available chart choices reach 60 minutes, but actual detailed coverage may be shorter.
- `src/MonCore/Monitoring/MonitorSession.cs`: `LiveSampleRecorded` publishes each full live sample on the session owner thread, including frames drained after a dispatcher stall. Restored samples do not raise this event.
- `src/MonCore/Monitoring/MonitorSample.cs` and `ResourceSample.cs`: provide interval duration, monotonic collection time, nullable resource readings and device labels/identities.
- `src/MonCore/Monitoring/UsageArchive.cs` and `src/WinMon/MainWindow.UsageArchive.cs`: establish the existing bounded archive, single-writer lock, immutable checkpoint, background serialization and atomic replacement pattern. Usage is a separate 35-calendar-day app ledger.
- `src/WinMon/MainWindow.FlightRecorder.cs` and `Controls/FlightRecorderWindow.*`: own the single modeless History window, frozen snapshot, Refresh, resource selection and detailed incident interactions.

Keep aggregate models and file validation in portable MonCore. Desktop owns persistence lifetime and UI. Do not enlarge `MonitorHistory`, synthesize `MonitorSample` objects from averages, or feed aggregates into `IncidentTimeline.Compare`.

## History interaction

Add a native tab strip immediately below the History heading: **Detailed history** and **Longer trends**. Open on Detailed history to preserve the current entry action. Only Longer trends shows the new range controls. Each tab keeps its selection while the window is open.

Conceptual layout, reusing the existing theme and window chrome:

```text
History                                               Refresh
[ Detailed history ] [ Longer trends ]

Range [ Last 24 hours v ]    Resource [ Memory v ]
Nov 20, 14:32 – Nov 21, 14:32 · 1-minute averages
Observed 19 h 42 min of 24 h · Gaps mean no readings

  Used memory (GiB)
  24 |                                  ____
  16 |                  _______________/
   8 |____     ________/
     +-----------------------------------------
      Yesterday 15:00       Today 03:00    14:32

[Earlier] [Later]   Nov 21, 14:12–14:13
Average 21.3 GiB · 73% · Observed 56 of 60 seconds
[ Open detailed history ]
```

- Range choices are **Last 24 hours** and **Last 7 days**, meaning elapsed 24/168 hours, not local calendar days. Both end at the frozen snapshot time. Keep the full requested axis even when recording began recently, so short coverage cannot look like a full week.
- Use one resource at a time with an honest unit axis: CPU %, memory GiB (with average % in selection text), disk active % (with read/write rates), network receive/send in B/s using existing adaptive decimal units, and GPU %. Name disk slots, adapter and GPU from the archived segment, not current settings. Network uses rates rather than link-speed-normalized utilization.
- Selection shows the minute interval, average and metric-specific observed seconds. Identify the current incomplete minute as **In progress**. Unsupported metrics show **Unavailable**, never zero.
- Refresh freezes a new immutable snapshot. Recording continues while the window is open; neither timer-driven replotting nor per-tick file reads are needed. Keep the selected minute when it remains in range; otherwise select the nearest retained minute and announce the change.
- Clicking or keyboard navigation selects a stored minute. The **Open detailed history** button is enabled only when the frozen detailed snapshot has a valid observation inside that minute. Navigate to the observation nearest the minute midpoint and show its exact time; never imply that it represents the whole minute. Otherwise show **Detailed readings are no longer available for this minute**.
- Detailed history keeps its existing Compare periods, contributors, events and Save/Open behavior. Those controls are absent from Longer trends. Opening an incident switches to Detailed history; its data never enter the trend archive. No seven-day Windows event query is added.
- Empty state: **Longer trends start when WinMon records new readings. Keep WinMon running to build a history.** A partial archive additionally says when recording began. Read/write failures and lock contention appear as persistent, concise notices; a save failure must distinguish the available in-memory trend from the older saved checkpoint.

This uses recognition and progressive disclosure: longer time ranges are beside detailed history, while controls requiring process-level evidence stay with the detailed recording. Coverage stays visible because a smooth-looking line across missing monitoring would give the wrong answer.

## Aggregation contract

Use UTC-aligned half-open minute buckets `[start, start + 60 seconds)`. Local time is presentation only. A 24-hour window intersects at most 1,441 buckets and a seven-day window at most 10,081, including partial edge minutes.

For each metric keep a weighted sum and valid observed duration. The average is `sum(value × observed seconds) / sum(observed seconds)`. Each metric has its own denominator: an unavailable GPU must not erase valid CPU or memory. Validate finite, nonnegative readings and percentage bounds before accumulation; no denominator means unavailable. Do not calculate averages of minute averages for range summaries: combine their sums and durations.

For rate/interval readings, use the recorded interval ending at the sample timestamp, split proportionally at minute boundaries. For memory, explicitly define the result as the endpoint reading weighted by its valid sampling interval: an approximation of observed memory occupancy, not exact byte-seconds. Average memory bytes and the sample's memory percentage separately so changing capacity does not retrospectively alter percentages.

Accept only live, non-gap observations with valid duration in `(0, 5]` seconds, consistent with the existing resource-rate gap limit. Never bridge sleep, collector failures, restart or closed-app time. A first observation without a valid baseline contributes no interval. Reject overlapping/duplicate intervals within a recording segment rather than count twice. A new desktop session starts a new segment and cannot use restored samples as a baseline.

Compare elapsed monotonic time with wall-clock progression. A wall-clock discontinuity starts a segment and breaks the line; do not spread one interval over the clock jump. A backward adjustment can produce multiple segments for the same UTC minute: mark those overlapping minutes **Clock changed — ambiguous time**, omit their average, and retain no unbounded duplicate bucket history. Cap retained distinct minute slots regardless of clock behavior. Query/prune against current UTC as well as the slot cap; future-dated data after a backward correction must not force current readings out or appear as present-day coverage.

Track disk, adapter and GPU identity changes as breaks. Existing `NetworkId`/`GpuId` are useful where populated; disk names alone are not proof of a stable physical device. A disk-target change, missing identity or restart begins a conservative device segment. Within a minute containing different targets, omit that metric and label **Resource changed**; CPU and memory can remain valid. Show all recorded segments with their labels, without a new historical-device picker. Never join lines across different devices or pass missing data off as a quiet period.

## Rendering and accessibility

Keep all one-minute aggregates in storage even when several map to one pixel. At render time, bound drawing work by chart width: combine sums/durations for the mean line and preserve the minimum/maximum **minute averages** as a faint range per pixel column. These are not raw-sample peaks. Do not connect across empty minutes, ambiguous time or resource changes. Partial minutes use an additional marker/line pattern and coverage text; color alone must not carry this information. Any render reduction must preserve visible gap boundaries, even within a single pixel column.

Pointer selection resolves the underlying stored minute, not an invented display average. Reuse chart drawing primitives and theme tokens where useful, but give this view its own aggregate input and value axis; the detailed chart's instant-sample model is not the aggregate contract.

Use native tabs, selectors and buttons with translated accessible names. Chart Left/Right moves by minute; Home/End selects the first/last minute in range. Earlier/Later buttons provide the same visible path. Selection of an empty minute reads **No readings**. The persistent selection summary exposes time, units and coverage to screen readers without requiring hover. Preserve visible focus, high contrast and enlarged text. At the existing 620 × 540 minimum window size, controls wrap and secondary reading text scrolls rather than displacing the chart or hiding coverage.

## Storage, privacy and lifecycle

Proposed model: a bounded `ResourceTrendArchive` with `Add`, `Snapshot`, `LoadAsync` and `SaveAsync`, following the existing Usage archive pattern rather than introducing a storage framework or dependency. Subscribe once to `LiveSampleRecorded`; accumulation is constant work over a fixed metric set and touches no process collections. Include only configured resource readings already collected.

Store versioned GZip aggregate JSON in `resource-trends.json.gz` beside settings, following installed/portable path rules. Keep seven elapsed days, including the current partial minute, under a hard 10,081-slot limit and a 16 MiB encoded-file limit and 64 MiB decoded limit. Bound device-label lengths, metric count, nesting and segment metadata as well as numeric values. Validate before publishing loaded state or replacing a file; unknown schema, corrupt files and oversized files are preserved and disable replacement until the user explicitly clears the rejected archive. A locked archive permits current-session in-memory viewing with an explicit persistence notice.

Only the desktop writes, holding a separate lifetime file lock. Checkpoint at most once per elapsed monotonic minute, off the dispatcher, from an immutable snapshot. Serialize writes, coalesce newer dirty state, use unique temporary files and atomic replacement, and clean up failed temporary files. Keep the previous valid file on failure. Snapshot the open bucket as accumulated so far; loading restores its totals but never restores a live interval baseline. Checkpoint and shutdown cannot add the same observations twice. Observe cancellation and use the existing bounded shutdown budget; trend-save failure must not skip other saves.

Add **Keep 7 days of resource trends** (default on, matching automatic local calendar Usage recording) and **Clear resource trends** to the existing Monitoring settings. Explain that it records only while WinMon runs and that resource/device labels are saved locally. Turning recording off stops new collection into this archive, while existing data remain viewable until cleared; state this beside the control. Clear requires confirmation because it deletes history. Serialize clear with checkpoint work, reset in-memory state and safely delete the archive so an older pending save cannot resurrect it. If deletion fails, report that failure. Clearing this archive does not reset session or calendar Usage.

No process names, PIDs, paths, command lines, endpoints or traffic owners enter this file. Device names still disclose local hardware context. Portable storage follows the executable and therefore travels with it.

## Implementation reach and acceptance

Implement as two reviewable slices:

1. Portable aggregate model and persistence with deterministic fixtures; desktop subscription, settings/path/lifecycle integration and translations. Wire startup readiness so early live observations are not overwritten by an archive load. Keep persistence errors independent of Usage and restart saves.
2. Longer trends tab, bounded renderer, keyboard selection and detailed-history navigation; diagnostics, smoke fixtures and documentation. Keep raw and aggregate selection state separate. Update README/Guide and DEVELOPMENT only when behavior lands.

Expected touched areas: MonCore monitoring models; desktop startup/shutdown and live-sample subscription; `PreferenceStore`, `MonitorPreferences` and Monitoring settings; History window and chart rendering; `Strings.resx`, all nine culture resources and regenerated designer; focused MonCore/desktop tests; diagnostic capture fixtures and performance tool; design index and user/developer documentation. Existing CLI/MCP archive readers and incident/restart formats need no changes for this desktop increment.

Acceptance evidence required at implementation time:

- Unequal intervals produce weighted results (10% for 1 second plus 50% for 3 seconds gives 40%); an interval crossing a minute boundary splits correctly. Test nullable metrics, partial minutes, duplicate input, invalid numbers and gaps independently.
- Seven-day pruning and hard bounds hold during continuous recording, DST, time-zone changes, forward/backward clock corrections, sleep and restart. Mixed device minutes stay unavailable for that metric. A sample with invalid process inventory can still contribute valid resource metrics.
- Checkpoint/reload and normal/update restart preserve aggregates without double counting. Test truncated/oversized/unknown-schema data, writer contention, interrupted atomic replacement, shutdown failure and clear during an active checkpoint. Rejected files survive.
- Frozen range/refresh, empty and partial archives, unavailable resources, device changes, large values, gaps and minute-to-detail navigation have deterministic UI fixtures. Review light/dark, high contrast, keyboard and enlarged-text behavior at normal/minimum sizes; include a full seven-day chart and a same-pixel gap fixture.
- Run `tools/Verify.ps1` plus `tools/Verify.ps1 -Store` because preferences/Settings change. Extend `capture` smoke coverage (which includes Settings) and inspect its PNGs.
- Measure before/after with the repository performance method: live drain/aggregation, minute checkpoint, startup load, seven-day snapshot/render and selection at 10,081 slots. Record elapsed time, allocation, memory/file size and results at the top of PERFORMANCE-HISTORY. Target under 1 ms added owner-thread work per frame and under 4 MiB retained aggregate state; these are proposed budgets, not measurements. An unexplained regression blocks implementation completion.

## What landed and departures

| Requirement | Status | Evidence |
| --- | --- | --- |
| Weighted minute archive with coverage, bounds and device/clock breaks | Verified | 21 `ResourceTrendArchiveTests`, including clock jitter, duplicate/backward QPC, real overlap, minute-boundary clipping, restart/device overlap, GZip decoded bounds and cancellation preservation |
| Live-only sampling, persistence and serialized clear | Verified | Eight `ResourceTrendLifecycleTests`; startup-load/clear ordering, rejected-file recovery, writer contention, failed checkpoint/clear and pending-write ordering |
| Default-on recording preference, confirmed clear and translations | Verified | Preference round-trip/merge and string-resource checks; Monitoring smoke images |
| Frozen 24-hour/7-day History view, keyboard and detailed link | Verified | Seven `ResourceTrendViewTests`; CPU/memory/network smoke images in both themes and window sizes |
| Continuous wide charts with honest collection gaps | Verified | Six `ResourceTrendGraphTests` render CPU and the secondary network trace at 1,000/3,000 pixels; wide 24-hour content captures in both themes |
| Desktop/Store verification and smoke image review | Verified | Full suites after review fixes: 2,198 desktop / 2,132 Store, zero failures; 29/47 expected skips; capture smoke: 49 renders |
| Performance baseline and after measurements | Verified | After review fixes: 0.0063 ms/sample accumulation; 4,106,104 B retained; 4.33 ms seven-day / 3.63 ms wide 24-hour render/selection; [measurements](../PERFORMANCE-HISTORY.md#resource-trend-review-fixes-november-21-2026) |

The archive, desktop lifecycle, History view, preferences, translations, tests and documentation are implemented. No new service or sampler is introduced. Detailed retention, daily Usage and incident files keep their existing contracts.

Review fixes retain valid observations when separately read wall time and QPC differ slightly, while rejecting duplicate/backward monotonic captures and genuine interval overlap. Minute coverage clips rounding at 60 seconds so persisted state remains valid. Clear lazily starts or joins the same startup load before deleting, preventing loaded history from returning afterward. Both chart traces skip unused pixel columns while retaining breaks for actual missing minutes. The Monitoring disk selectors also retain their shared two-column Grid, preserving the existing layout contract.

The file is GZip JSON (`resource-trends.json.gz`), rather than plain JSON: repeated seven-day metric/device metadata compresses well and keeps the encoded file within 16 MiB. Decoded input is capped at 64 MiB before parsing. Packed metric slots keep the full twelve-metric archive under the proposed 4 MiB retained-state target. Range coverage is marked approximate at minute boundaries; selection exposes each metric's retained observed seconds. Disk identity follows the configured volume name, so physical replacement under the same name is not independently established by this archive.

Routine accumulation meets the proposed sub-millisecond owner-thread target. A full immutable snapshot costs 12.6 ms at the minute checkpoint or explicit History refresh; that bounded burst is separate from routine accumulation, while compression and file I/O remain off the dispatcher. The synthetic file was 125,011 B with median save/read times of 154.2/237.6 ms. Existing-view before/after runs were not alternated, so their timing differences are directional evidence rather than a claim of an improvement.

Final review-fix verification: `./tools/Verify.ps1 -Smoke capture` passed with reports and 49 reviewed renders in `artifacts/verify/20261121-091110-279-desktop`; `./tools/Verify.ps1 -Store` passed with reports in `artifacts/verify/20261121-091350-313-store`. The focused resource-trend run passed all 43 cases. An earlier concurrent desktop/Store run had two updater recovery failures on nonblocking global-mutex paths; both full suites passed when run sequentially. Updater production code and tests were unchanged. The original feature verification had one transient event-query timing failure, also passing on retry without a production change.

Manual screen-reader use, an actual seven-day soak, real sign-out/reboot during a checkpoint, and native high-contrast/enlarged-text acceptance remain unverified. Minute averages can reveal sustained load but can hide short spikes; detailed history remains necessary for those investigations.
