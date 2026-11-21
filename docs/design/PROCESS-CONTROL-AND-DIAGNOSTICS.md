# Process control and system diagnostics

Four independent increments. Implement and verify them in order, one commit each.

| Increment | Status |
| --- | --- |
| 1. Process details: command line, verified signer, user | Verified |
| 2. Process actions: suspend/resume, CPU affinity, Efficiency mode | Verified |
| 3. Memory pressure: paging reads/sec, standby, modified, compression store | Verified |
| 4. Windows event markers in History | Verified |

Shared rules:

- Native code goes in `MonCore.Windows`. `MonCore` gains only portable records and fields. Increments 1, 2 and 4 read nothing on the sampling hot path. Increment 3 adds PDH counters to every tick, so it needs before/after measurements.
- Every action and read targets one confirmed process lifetime. Open a handle, compare `CreationTicks` on that same handle, then act. This is the pattern `ProcessPriorityChanger` and `ProcessTerminator` already use, with `Func<int, ITarget>` fakes in tests.
- Missing or denied readings use the existing `MissingReading` text. They are never shown as empty or zero.
- All new text goes in `Strings.resx` and all nine cultures. Docs to update: `README.md`, `docs/USER-GUIDE.md`, `docs/PRIVACY.md`, and `docs/DEVELOPMENT.md` for the new measurement meanings.
- MCP stays read-only and keeps omitting command lines and usernames. The CLI is unchanged. Both are listed as follow-ups below.

## 1. Process details

What already exists: Properties and Copy details (`ProcessDetailsSnapshot.Format`) already show the start time (`ProcessStartedLabel`). They also show **Publisher**, but that value is the executable's CompanyName metadata, which anyone can set. This increment adds the parts that are still missing:

| Row | Source | Notes |
| --- | --- | --- |
| Command line | `NtQueryInformationProcess(ProcessCommandLineInformation)` with `PROCESS_QUERY_LIMITED_INFORMATION` | Works unelevated for the user's own processes. Protected or other users' processes read as unavailable. |
| File on disk signed by | `WinVerifyTrust` on an open file handle for the image path (embedded signature, then catalog via `CryptCATAdminCalcHashFromFileHandle2`), plus the verified leaf certificate subject CN | Statuses: *Signed by X*, *Not signed*, *Signature invalid*, or unavailable. Catalog lookup is required because most Windows binaries are catalog-signed. The existing Publisher row stays and is relabelled to show it comes from file metadata. |
| User | `OpenProcessToken(TOKEN_QUERY)` → `TokenUser` → `LookupAccountSid`, plus session ID and `TokenElevation` | Shows something like `DOMAIN\user · Session 1 · Elevated`. |

- **When it reads:** only when Properties opens or Copy details runs, on a background task. Properties shows *Reading…* until the result arrives. Rows are never added per tick.
- **Signature meaning:** label the row *File on disk signed by*. This verifies the file currently at the image path, not the image loaded by the running process; an updater can replace that file while the process runs. Preserve this qualification in Properties and Copy details.
- **Signature read:** open the file once and use that handle for file identity, metadata, embedded verification and catalog hashing. Extract the signer from the successful trust verification state rather than reopening the path. Keep the file stable against writes and replacement during the read; if that cannot be done, report unavailable. [`WINTRUST_FILE_INFO.hFile`](https://learn.microsoft.com/en-us/windows/win32/api/wintrust/ns-wintrust-wintrust_file_info) supports verification through an open file handle.
- **Signature cache:** results are cached per (volume and file ID, file size, last write time) in a bounded LRU (256 entries). Read the key from the open handle on every lookup, so a replacement with matching size and write time invalidates the entry. If file identity is unavailable, skip caching. WinVerifyTrust can take tens of milliseconds and can touch the network for revocation, so pass `WTD_REVOKE_NONE` and `WTD_CACHE_ONLY_URL_RETRIEVAL`. Describe the result as an offline check of the file on disk; it does not establish current revocation status.
- **Scope:** live individual rows only. For historical rows and group rows, these three rows say they are not available for that view. Group members can differ, and the values are never persisted.
- **Privacy:** command lines can contain tokens and file paths. They appear in Properties and in user-initiated Copy details only. They are never written to capture reports, CSV, restart history, incident files, logs or MCP. `PRIVACY.md` must say this.

Tests: formatter cases covering every status and every missing value, including the file-on-disk qualification; signature cache tests (a changed file identity, size or write time invalidates the entry, including replacement with matching size and write time); a verification test proving hashing, trust and signer extraction use the same open file; a lifetime check where a PID reused under a different start token reports as exited. Native integration tests (Integration category) read WinMon's own command line, `notepad.exe`'s catalog signature and the current user.

## 2. Process actions

All three actions go in the existing process More menu (`ProcessActionBar`, next to Set priority and Create dump file). They use the same eligibility rule as priority, `ProcessPriorityChanger.IsEligible`: a live individual row, PID above 4, not WinMon itself, a known start token and not critical. Group rows and historical rows show the actions disabled with a tooltip. Access-denied (error 5) and already-exited (error 87) map to the same messages priority uses.

### Suspend and resume

- Use `NtSuspendProcess` and `NtResumeProcess` with `PROCESS_SUSPEND_RESUME | PROCESS_QUERY_LIMITED_INFORMATION`.
- Suspend asks for confirmation: the app freezes, and anything waiting on it can hang. Resume does not.
- Also refuse `dwm.exe` (suspending it freezes the display) and WinMon's own network service. The critical-process check already covers csrss, wininit and similar.
- **Detecting suspended state:** when the menu opens, take one `SystemProcessInformation` snapshot and match PID and creation time. Report suspended only for a nonempty thread list whose threads are all Waiting with reason Suspended. This observation does not establish who suspended it. An unavailable snapshot stays unknown. It is not checked per tick.
- **Tracking and ownership:** allow at most one outstanding WinMon Suspend per lifetime and serialize Suspend, Resume and exit cleanup. Add the lifetime to the session set only after Suspend succeeds; while present, disable further Suspend and offer Resume once. After successful Resume, remove it even if threads remain suspended; report that remaining suspension separately and never keep resuming until they run. Do not offer Resume for a suspension WinMon did not initiate. On failed Resume, retain the entry for an explicit retry; remove exited lifetimes without acting on a reused PID. Show *Suspended by WinMon* only when the outstanding entry and the observed suspended state agree.
- **Concurrency limit:** native suspension counts belong to individual threads and do not identify their owner. One balanced process-level Suspend/Resume pair is best effort if another tool changes those counts concurrently. State this in the confirmation and never claim to restore the exact prior thread state. See [Microsoft's resume semantics](https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-resumethread).
- **Exit guard:** before closing or entering update shutdown, offer Resume, Leave suspended, or Cancel when outstanding live lifetimes remain; Resume is the default. Attempt one balancing Resume per tracked lifetime regardless of the latest thread-state snapshot. If a read or Resume fails, keep the window open and report the failures so the user can retry, explicitly leave them suspended, or cancel. Successful cleanup removes the entry even if another suspension remains. Crashes and forced termination cannot run this guard and may leave processes suspended.

### CPU affinity

- A dialog lists one checkbox per logical processor, with All and None buttons. OK stays disabled while nothing is selected.
- Use `GetProcessAffinityMask` and `SetProcessAffinityMask` with `PROCESS_SET_INFORMATION | PROCESS_QUERY_LIMITED_INFORMATION`.
- WinMon already refuses machines with multiple processor groups, so one 64-bit mask covers every processor. Still, if `GetProcessGroupAffinity` reports more than one group, show the dialog read-only.
- The change lasts for the process lifetime only, the same as Task Manager. Nothing is persisted.

### Efficiency mode

- Turning it on matches Task Manager: `SetProcessInformation(ProcessPowerThrottling)` with `PROCESS_POWER_THROTTLING_EXECUTION_SPEED` on, plus `IDLE_PRIORITY_CLASS`.
- Turning it off clears throttling and sets Normal priority.
- **Partial failures:** before changing anything, read both power-throttling control/state masks and priority from the confirmed process handle. Abort without changes if either read fails. Change only the execution-speed throttling bit, preserving unrelated flags, then set priority. If the second operation fails, attempt to restore the previous throttling setting on the same handle. Report the original failure and any rollback failure, then reread both settings; never report success for a partial change.
- The menu item reads both throttling and priority when it opens and after each attempted change. Show checked only for explicit execution-speed throttling plus Idle priority, unchecked for explicit throttling off plus Normal priority, and a labelled mixed or unknown state otherwise. Set priority reflects the reread priority. Turning Efficiency mode off deliberately selects Normal; rollback after failure instead restores the pre-action setting.
- On Windows 10 the API is accepted but EcoQoS has limited effect. The tooltip says so.

Tests: fake-target tests for each action covering eligibility, start-token mismatch, exited, critical, access denied, the deny list and an empty affinity mask. Suspension tests cover repeated Suspend rejection, an external suspension, successful balancing Resume with threads still suspended (no second Resume), failed Resume retaining tracking, unavailable snapshots, and serialized close/update cleanup including failure and cancellation. Efficiency mode tests cover on and off selecting the intended priority, mixed/unknown state, preserving unrelated throttling flags, pre-read and first-write failure, second-write failure with successful rollback, rollback failure, and rereading both settings after each attempt. Feature smoke renders the menu items, partial-failure messages, exit guard and affinity dialog in Dark and Light at minimum size. As the smoke limits already state, smoke never suspends or changes a real process.

## 3. Memory pressure

What exists today: available, committed/limit, cached (system cache working set) and pool sizes come from `GetPerformanceInfo` and appear in the Memory hover details (`MainWindow.Details.cs`).

| Value | Source | Definition shown to users |
| --- | --- | --- |
| Paging reads/sec | PDH `\Memory\Page Reads/sec` | Disk read operations Windows made to resolve hard page faults, including memory-mapped files as well as the page file. Counts read operations, not faulting references or pages retrieved. |
| Standby | Sum of PDH `\Memory\Standby Cache Core/Normal Priority/Reserve Bytes` | Cached data Windows can reuse immediately. |
| Modified | PDH `\Memory\Modified Page List Bytes` | Changed pages that must be written to disk before reuse. |
| Free | PDH `\Memory\Free & Zero Page List Bytes` | Memory holding no data. |
| Compression store | Working set of the `Memory Compression` process, already in the process sample | Physical memory used by compressed pages. This is not the uncompressed amount Task Manager shows. |

- **Collection:** one PDH query per sampler with six counters (paging reads, three standby lists, modified and free/zero), collected on the existing tick. Compression store comes from the existing process sample. Paging reads is a rate, so the first sample is unavailable, as disk rates are today. Optional counters use the existing `AddOptionalCounter` pattern, so a missing counter means unavailable, not a failure.
- **Model:** add nullable `PagingReadsPerSecond`, `StandbyBytes`, `ModifiedBytes`, `FreeBytes` and `CompressionStoreBytes` to `ResourceSample`. The existing Cached row is relabelled *System cache* so nobody confuses it with standby.
- **Display:** new rows in the Memory hover details and capture report text, and new columns in the CSV resource export. Use the read-operation meaning consistently in UI text, reports, CSV headers and persistence; see [Microsoft's counter definitions](https://techcommunity.microsoft.com/blog/askperf/an-overview-of-troubleshooting-memory-issues---part-two/372679).
- **Persistence:** restart history format 4. Keep the format-3 reader and its fixture; old history reads these values as unavailable. Incident files stay at version 1 because the fields are nullable JSON members.
- **Out of scope for this increment:** a paging-reads chart series, a CaptureMetric for it and an automatic-capture rule.

Performance: follow `DEVELOPMENT.md#performance` and add the before/after results to the top of `PERFORMANCE-HISTORY.md`. The extra PDH collection must not raise the sampling worker's per-tick time beyond run-to-run noise.

Tests: sampler mapping with a fake counter source (missing counters, first-sample rate), a restart-history format 3→4 round trip, CSV header and row tests, and details formatter tests. Assert that paging reads pass through as read operations per second and are labelled consistently in details, reports and CSV. Smoke runs `compact` and `snapshot` for the details rows.

## 4. Windows event markers in History

- **When it reads:** the History window (`FlightRecorderWindow`) queries Windows event logs for the frozen recording's wall-clock span when it opens and on each Refresh. There is no background collection and no live-dashboard markers.
- **Reader:** `System.Diagnostics.Eventing.Reader` if `MonCore.Windows` can reference it without a NuGet package; otherwise `wevtapi` `EvtQuery` via P/Invoke. It runs off the dispatcher, can be cancelled, and has a 5-second timeout.

Curated events, kept in one table in code:

| Log | Provider / IDs | Meaning |
| --- | --- | --- |
| Application | Application Error 1000, Application Hang 1002, .NET Runtime 1026 | App crash, app hang, unhandled .NET exception |
| System | Service Control Manager 7031, 7034 | Service ended unexpectedly |
| System | Resource-Exhaustion-Detector 2004 | Low virtual memory |
| System | disk 7, 51, 153; Ntfs 55, 98; System 129 (provider varies) | Disk errors, paging errors, retries, resets |
| System | Display 4101 | GPU driver stopped responding and recovered |
| System | WHEA-Logger 17, 18, 19, 47 | Hardware error |
| System | Kernel-Processor-Power 37 | Firmware is limiting processor speed |
| System | WindowsUpdateClient 19, 20, 43 | Update installation started or finished |

- **Bounds:** at most 500 events per query. Messages are formatted lazily for listed events only, using the first line truncated to 300 characters. If the limit is hit, say so.
- **Unreadable logs:** if a log cannot be read, the coverage line says which one, rather than implying there were no events.
- **Timeline:** events become ticks above the History timeline, coloured and shaped by level (error, warning, information or unknown) with a pattern as well as colour for accessibility. Clicking a tick selects the nearest observation within 2 seconds. Further away, the event is shown as falling *between observations*.
- **Event list:** an *Events* list next to process contributors shows events inside the Compare periods window around the selection.
- **Time mapping:** events map by wall time to `MonitorSample.Timestamp`. Wall-clock jumps during the recording mark nearby events as approximate.
- **Process links:** Application Error 1000 carries the faulting PID and start time. When both match a retained lifetime, the event links to that process row. Other events are not linked by name.
- **Incidents:** saving writes the events into incident version 2, keeping the version-1 reader. Opening an incident shows only its saved events and never queries local logs, because the incident may come from another machine or time. Event messages can contain user and path names, so update the Save tooltip and `PRIVACY.md`.

Tests: XPath/query builder for span and IDs; mapping to observations, including gaps and clock jumps; the 500-event cap; unreadable-log coverage; the Application Error lifetime match; incident v1/v2 round trips proving that opening never queries. An Integration test reads a real System log span. Capture smoke adds fixture events to the History renders in Dark and Light at normal and minimum sizes.

## Validation

For each increment, run `./tools/Verify.ps1` plus these smoke modes, and inspect the listed PNGs:

| Increment | Smoke modes |
| --- | --- |
| 1 | `feature` |
| 2 | `feature` |
| 3 | `compact`, `snapshot` |
| 4 | `capture` |

Increment 2 also requires `./tools/Verify.ps1 -Store`: its suspension exit guard integrates with the update shutdown path in `MainWindow.OnClosing`. Cover ordinary close, update restart, cancellation and failed cleanup before shutdown begins. Any other increment that touches update or Settings code also requires `-Store`, per the repository rules. Check that the Store build still compiles if the Efficiency mode or Event Log code needs conditional compilation. Increment 3 also needs the performance record.

## Follow-ups (not in this design)

- MCP: optional opt-in signer and user fields, and an events tool.
- CLI: the same Properties rows and memory values.
- A paging-reads chart series and an automatic-capture rule.
- Process tree view.
- Event markers on the live dashboard chart.

## Result and departures

Increment 1 landed in `WindowsProcessDetailsReader`, portable `ProcessInspection` records and the existing Properties/Copy flow. Sensitive inspection values are not added to sampled process records or persistence. File trust uses a stable file handle, embedded/catalog verification and a 256-entry identity cache; the signer is explicitly the file on disk. All nine translations are present. Native integration tests read the current process and verify a Windows library through the catalog-only path without launching another application.

Validation for increment 1: `Verify.ps1 -Smoke feature` passed 2,060 tests and 125 renders; dark/light Properties renders were inspected. Focused native tests passed 8/8, and process UI/localization tests passed 65/65. Protected-process access and online revocation are not claimed.

Increment 2 landed in `ProcessController`, `SuspendedProcessTracker`, the native process-control/suspension/service helpers, the More/row menus, affinity and close dialogs, and `MainWindow.OnClosing`. Cancel and cleanup failure stop shutdown and cancel a pending update. The tracker serializes requests, removes confirmed exited/reused lifetimes without acting on another process, and retains failures for retry. Row labels use cached, observed suspension state. Efficiency mode uses explicit On/Off throttling, preserves unrelated flags and reports rollback failures. Processor-group checks also protect the setter; unsupported multi-group masks are shown read-only.

Validation for increment 2: `Verify.ps1 -Smoke feature` passed 2,099 tests and 131 renders; `Verify.ps1 -Store` passed 2,033 tests. Dark/light minimum-size affinity and close-dialog renders were inspected. `WindowsProcessControlTests.OwnedHelper_ControlsAndRestoresTheSameProcessLifetime` exercised native suspension observation, balanced Resume, affinity and throttling/priority changes on a hidden test-owned helper only. `ProcessControllerTests`, `ProcessControlCloseTests` and `ProcessControlUiTests` cover denied/stale/critical targets, service protection, failed cleanup/retry, explicit dialog choices, mixed settings and rollback. Four alternating UI benchmark pairs showed unchanged pixels and no measurable regression; see `PERFORMANCE-HISTORY.md`. Older Windows builds, multi-group hardware and concurrent control by external tools were not validated on this machine.

Design correction: memory pressure needs six PDH counters, because standby is the sum of three lists; compression store reuses process data.

Increment 3 landed in the Windows resource sampler, nullable resource fields, memory details, reports and CSV. One reusable six-counter PDH query supplies paging reads, standby, modified and free/zero pages; compression reuses an observed compression process working set. Missing or reset rate baselines remain unavailable. Restart history writes format 4 and reads formats 1–3; a retained format-3 fixture verifies compatibility. Optional incident fields preserve version-1 compatibility.

Validation for increment 3: `Verify.ps1 -Smoke compact,snapshot` passed 2,110 tests and 103/64 renders. Additional asserted memory-details fixtures passed a subsequent build and 106/64 renders; populated dark/light and unavailable light rows were inspected. Four alternating native sampler pairs showed a 0.057 ms median difference within observed run variation and 1,456 extra allocated bytes/sample; see `PERFORMANCE-HISTORY.md`. Compression-process availability and older Windows builds were not validated here.
Increment 4 landed in `WindowsHistoryEventReader`, portable incident event records/mapping, the persistent History Events pane, severity-pattern markers and incident format 2. Native queries run off the dispatcher with thread-owned handles, bounded XML/message buffers, a shared 500-event cap and a five-second budget; UI cancellation and generation fences prevent late reads replacing refreshed or imported recordings. Coverage remains explicit per log. Event-list and marker selection show between-observation details, and exact PID/FILETIME links can select retained process metadata without inventing readings for another moment. Version-1 incident files retain unknown event coverage; imported files never trigger a local event query. All nine translations and privacy/export documentation are updated.

Departures for increment 4: storage-reset event 129 uses a provider-agnostic System selector because Windows storage drivers publish under varying names (including storahci/stornvme/vendor providers); the UI keeps the actual provider and does not assume every System 129 is a storage reset. Structured queries keep each provider selector below Windows XPath complexity limits. A query still pending when Save is chosen records unknown coverage; completed results cannot mutate that frozen saved snapshot. Native calls without their own timeout can finish after the UI deadline, but their late result is ignored and their handles close on the owning worker.
Validation for increment 4: the final `Verify.ps1 -Smoke capture` run passed 2,150 tests and 35 renders. The focused desktop event suite also passed 8/8. Dark/light normal and minimum History renders, including selected-moment contributors, were inspected. Tests exercise shared cap short-circuit/disposal, per-log failures, cancellation, clock ambiguity/gaps, exact lifetime links, event selection, keyboard navigation, pending-save coverage and late query fences. A real seven-day query read 218 events with 218 formatted messages and complete Application/System coverage in about 906 ms on this machine. This timing is an observation, not a latency guarantee for other event logs or machines.

Final Store verification passed 2,084 tests. Four alternating live-refresh benchmark pairs preserved every pixel hash and showed no measurable regression; the median was 4.970 ms before and 4.918 ms after, within broad run variation. See `PERFORMANCE-HISTORY.md`. Older Windows versions, multi-group hardware, manual screen-reader use and whole-app profiling remain unverified.
