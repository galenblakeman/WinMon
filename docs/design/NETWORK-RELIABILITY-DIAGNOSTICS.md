# Network reliability diagnostics

## Problem and evidence

The November 8 app logs showed repeated pipe EOF failures, followed by successful five-second reconnects, and persistently incomplete snapshots without a coverage explanation. An isolated native pipe reproduction against the original client captured `Network.PipeSnapshotRateExceeded intervalMs=999.9095` before EOF. The client waited once for the remainder of a second; .NET 10.0.14 truncates TimeSpan delays to whole milliseconds, and Windows timer completion does not establish that the monotonic deadline passed. See [Task.ValidateTimeout in the exact runtime source](https://github.com/dotnet/runtime/blob/v10.0.14/src/libraries/System.Private.CoreLib/src/System/Threading/Tasks/Task.cs).

Two independent incomplete-data defects were confirmed by source and regression checks: unreadable seed/start process probes permanently marked coverage incomplete without any observed traffic, and ambiguous lifetime registrations suppressed rates with a long.MaxValue deadline. The isolated original ledger returned a null rate after a fresh window; the corrected ledger returned the expected 10 bytes/second while preserving the incomplete-total flag.

## Changes

- Recheck Stopwatch elapsed time after each client wait, rounding any remaining fractional millisecond up. Keep the server's one-second limit, framing, authentication and request serialization.
- Record server snapshot/control rate rejections and failed request operation, stage, elapsed time and safe exception details. Expected peer EOF while waiting for another request remains quiet. Client exchange failures identify the operation and elapsed time.
- Add generation-wide `IncompleteReasons`: unresolved traffic ownership, invalid events, queue overflow, native loss, lifetime ambiguity, capacity limits, invalid event timestamps and clock discontinuity. This additive protocol-1 field defaults to None with older services; unknown bits or reasons on non-incomplete readings fail client validation. No monitored identities, endpoints, payloads or arbitrary request text are added to logs.
- Do not infer missing traffic solely from an unreadable process discovered during startup or process-start reconciliation. Actual unresolved traffic still marks coverage incomplete, including traffic arriving during the probe rejection cache interval. Known other-account traffic remains excluded.
- Let fresh rate windows recover after registration ambiguity, retaining generation-wide incomplete totals. Native loss counter resets are identified as native loss rather than wall-clock drift.
- Log snapshot diagnostics on quality/reason/failure/generation changes and once per minute while unchanged. Include loss/drop/late counts, row/rate counts and window/lag duration. Record unavailable failure codes before reconnecting.

## Verification and limits

Focused service verification passed 51 tests, and controller verification passed 27 tests. The original native pipe client reproduced an EOF; the instrumented boundary probe identified the 999.9095 ms rejection. The fixed probe completed 12 consecutive boundary polls against an isolated server and another 12 against the existing signed installed service, with no disconnect, collector failure, native loss or queue loss. The installed service still reported incomplete data because it is the older binary without the new collector behavior/reason field. This short probe does not establish sustained or power-transition acceptance.

Full `Verify.ps1 -Smoke connections` was canceled at the user's request; no full-suite or smoke success is claimed. Final focused checks passed all five selected service regressions (boundary polling, rate-rejection diagnostics, duplicate-registration recovery, discovery-only probe handling and loss-counter reset reasons) and the new Windows-client diagnostic-field validation test. Performance observations are in [the performance history](../PERFORMANCE-HISTORY.md).

## Result

### November 9: expired process-history capacity

Local logs showed repeated `WarmingUp, Incomplete, CapacityExceeded` snapshots with every process rate unavailable, alternating with populated rates on the same generation and subscription. `RecordTombstone` marked a fresh global coverage gap whenever the bounded PID history evicted its oldest entry, even when that entry was already outside the 30-second retained event window.

Expired entries now roll over without changing coverage or the rate deadline. The comparison is strict: evicting history within the retention window still marks capacity/incomplete and suppresses affected rates, as does failure to retain a new process lifetime. Bounds, attribution, loss handling, protocol and UI are unchanged. The regression forces eight successive rollovers with a two-entry limit and checks both live rates, cumulative bytes, generation and clean coverage; the real-capacity regression also checks rate suppression. The new rollover test failed against the original source before passing with the fix. Collector probe results are in the performance history.

`Verify.ps1 -Smoke connections` passed: solution and benchmark builds had no warnings/errors; all six test projects passed (1,355 tests, with three explicit/elevated checks not executed). Connections smoke passed 22 WPF content renders, inspected visually; native window captions were not captured. The installed service was not replaced, and native ETW/long-uptime acceptance remains open.

Landed in `NetworkServiceClient`, `NetworkPipeServer`, `EtwNetworkCollector`, `NetworkTrafficLedger`, the portable snapshot and `NetworkMonitoringController`, with focused test coverage. There is no new setting, dependency or protocol-major change. Real gaps remain incomplete; this does not reconstruct missed traffic or promise complete accounting for protected/short-lived processes. No installed service or running application was replaced. A signed service upgrade and updated app launch are still needed for live acceptance of all changes.
