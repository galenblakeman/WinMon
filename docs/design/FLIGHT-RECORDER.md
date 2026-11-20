# What just happened?

Current extensions: [network contributors](HISTORY-NETWORK.md) replaces the initial CPU fallback with retained app traffic, and [incident files](HISTORY-INCIDENTS.md) adds explicit save/reopen support. The implementation record below describes the original increment.

The History button sits beside Capture in the chart toolbar, using a clock-with-back-arrow icon. The centered action group reads Live/Capture, History, Auto. History opens a separate, resizable investigation window. Its graph, readings and process list share one selected observation. Find peak selects the latest highest valid reading for the chosen resource; Previous/Next and the chart's existing arrow-key navigation explore the surrounding samples. The window freezes the retained recording while the main window keeps sampling. Refresh in the upper-right corner loads the latest recording in place, preserving the selected resource and observation when it is still retained; otherwise it selects the current resource peak. Only one recorder may be open, bounding retained references to one recording.

Reuse `MonitorSession.History`, `CpuGraph`, `CaptureRules.Value`, resource labels and table formatters. No new collector, dependency, preference, disk format or background work is needed. The initial selection is the CPU peak. The full retained range is visible, rather than assuming a fixed five-minute recording.

Process rankings use total-capacity CPU, working set, process I/O or GPU. Network currently uses CPU ranking, explicitly labeled. Process I/O is not physical-disk attribution. Missing metrics remain unavailable. Compacted idle processes and inaccessible processes are disclosed. On opening, lifecycle differences are frozen from the existing uncompacted Usage inventories; complete resource-history inventories provide a fallback. Starts/stops use PID, start token and clock domain; gaps, unknown lifetimes and incomplete inventories suppress the comparison. Processes that exist entirely between samples cannot be detected.

Verification uses focused `IncidentTimelineTests` and the existing capture smoke, including normal/minimum window sizes in dark and light themes. The smoke exercises opening the window through History, duplicate opening, resource peak changes, navigation boundaries and empty history.

| Requirement | Evidence |
| --- | --- |
| Find resource spikes without treating missing data as zero | `Peak_IgnoresGapsAndUnavailableValuesAndSelectsLatestTie`, `Peak_UsesSelectedResourceInsteadOfCpu` |
| Rank the selected moment's process contributors | `Contributors_RanksSelectedResourceWithUnknownValuesLast` |
| Preserve lifecycle identity and avoid invented events | `Changes_PidReuseIsAStopAndStartWhileSurvivingLifetimesAreExcluded`, `Changes_EqualPidAndTokenInDifferentClockDomainsAreDifferentLifetimes`, `Changes_IncompleteOrDisconnectedObservationsDoNotInventEvents`, `Changes_FiveSecondBoundaryAndEmptyCompleteObservationAreValid` |
| Use full inventories while freezing the recording | `CaptureChanges_UsesFullInventoriesAndFreezesEventsAfterUsageExpires`, `CaptureChanges_DoesNotTurnMissingOrIncompleteInventoriesIntoEvents` |
| Navigate and render the actual window | `CaptureFlightRecorderAsync` in the capture smoke: single-window entry, peak changes, Previous/Next, empty history, readable headings, dark/light and minimum size, and starts/stops with compacted chart history |
| Refresh the recording in place | `CaptureFlightRecorderAsync`: explicit refresh after new sampling, preserved resource/retained selection, updated peaks/contributors/lifecycle changes, expired-selection fallback, empty/populated transitions, and upper-right placement at normal/minimum size |

The full desktop verification passed 1,996 tests and capture/compact smoke. After the final allocation improvement, focused verification passed all 26 `IncidentTimelineTests`. The final diagnostic-fixture correction was followed by `Verify.ps1 -SkipTests -Smoke capture,compact`: builds passed with zero warnings/errors, with 30 capture and 103 compact renders. The recorder's normal, minimum-size, empty and lifecycle renders were visually inspected. NuGet vulnerability auditing was disabled for these verification processes because the configured feeds were unreachable; no repository audit setting was changed.

## Landed and departures

The upper-right Refresh action now raises `RefreshRequested`; MainWindow supplies a fresh recording through `Update`. Helper text was updated in all ten resource files. `Verify.ps1 -Smoke capture` passed the full 2,001-test desktop suite, zero-warning app/benchmark builds and 31 capture renders, including the refresh interaction assertions. Normal/minimum-size dark/light and empty-history renders were visually inspected. NuGet auditing remained disabled for this run.

On November 19, History moved from the title bar to the chart toolbar immediately after Capture, with the shared clock-with-back-arrow vector icon. The full desktop gate passed 2,001 tests and capture/compact/snapshot smoke. A final spacing adjustment was checked with `Verify.ps1 -SkipTests -Smoke capture,compact,snapshot` (31/103/64 renders); the English toolbar remains one row at 836 DIPs, German labels wrap without overlap, and the investigation group stays centered when Auto is hidden. Relevant toolbar, snapshot and compact renders were visually inspected. The NuGet audit remained disabled for these runs.

Implemented the on-demand investigation window, existing timeline interaction, resource peaks, ranked process table, observed process changes, translations and documentation. The recording remains in memory and depends on configured retained history. Shareable timeline files, pre/post-trigger recording, automatic incident detection and per-process network ranking are outside this first implementation; existing automatic capture reports remain available separately.
