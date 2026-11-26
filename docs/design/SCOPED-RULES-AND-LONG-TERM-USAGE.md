# Scoped rules, CLI history and calendar Usage

[Scoped rules and disk response time](SCOPED-RULE-TARGETS-AND-DISK-LATENCY.md) extends the original single-target rules described below.

Implement feature gaps 5, 6 and 7 while preserving the existing collectors, recent-history limits and session totals.

## Scope

- Add executable/service CPU, private-memory and network-rate rules to the existing Settings table. Reuse duration, cooldown and hysteresis. Exact name/path targets aggregate matching app lifetimes; named services use aligned observed host/workers. Shared hosts stay unattributed. Network rules require valid lifetime-safe rates and coverage. New rules default off and preserve old settings.
- Add terminal historical selection with one-step and 60-observation navigation, keep collection running, and use uncompacted UsageHistory for period totals. Missing data and memory-limited periods stay explicit.
- Persist 35 calendar days of CPU/I/O/GPU totals, with observed seconds, calendar/DST boundaries and a fixed archive time zone. Desktop Day/7 days views and copy/CSV use the selected period; CLI reads the same archive. Do not extrapolate through closed-app time or sampling gaps.

## Result and departures

Implemented in CaptureRules/AutoCaptureSection, InteractiveState/InteractiveMonitor, UsageArchive, MainWindow.UsageArchive and UsageView. One executable and one service target are supported per metric; no general rule editor or regex matching was added. CPU uses percent of one logical processor, memory uses private MiB, and network uses decimal rates. Service shared-host suppression avoids false attribution. Network rules retain the existing delayed/provisional service-window semantics.

The archive stores versioned UsageLedger states and survives normal/update restarts, with minute checkpoints and bounded session-end saving. Saves use immutable snapshots and atomic replacement; a lifetime lock prevents concurrent writers. Invalid files are preserved rather than silently overwritten. Data/file size and retention are bounded. Running counts are unavailable in calendar reports; weekly lifetime observations sum daily counts and are labeled accordingly. Session Reset never clears the archive.

Daily/weekly views cover observed desktop activity only, not system-wide collection while WinMon is closed. CLI/MCP live-session totals remain independent. The archive begins with new observations and does not backfill old compacted history. CLI history stays within existing retention and observation budgets; it is not a saved incident viewer.

Validation includes scoped attribution/coverage, midnight/DST, corruption/persistence, retention, CSV semantics, Settings round-trips, terminal cursor/paging, and translated resources. Usage smoke includes daily/weekly Dark/Light screenshots. Final verification, smoke and performance outcomes are recorded in the task report and PERFORMANCE-HISTORY.md.

Review corrections: Monitoring-page spacing was tightened without removing its explanations or controls, allowing the previously failing 47-DIP overflow check to complete. Scoped target fields use separate full-width rows and a literal translated unit key, avoiding stretched number fields and a format placeholder in the unit label. Calendar views use calendar-specific notes and unknown running counts. A lazy date-control template removed the initial Services/grouped benchmark increase. Shared archive validation rejects oversized/non-finite/out-of-range values before saving as well as loading, including GPU durations that could otherwise overflow presentation.

Final acceptance: all 11 Verify.ps1 steps passed for desktop (1,781 tests) and Store (1,724 tests), with isolated artifact roots and temporary runners removed. Usage smoke passed 9 renders and capture/Settings smoke passed 24; the new Dark/Light calendar views and scoped-rule/Monitoring screenshots were inspected. Native CLI checks confirmed earlier/later selection, period changes, continued collection and return to Live, plus reading a versioned archive without sampling. Midnight, DST, backward-clock day limits, malformed totals and atomic replacement have deterministic coverage. No signed installation, release publication, real reboot/sign-out or elevated ETW acceptance was performed.
