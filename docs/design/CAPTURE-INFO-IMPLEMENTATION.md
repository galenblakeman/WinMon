# Capture information implementation

Tracks [the capture design](CAPTURE-INFO.md). Started November 11, 2026. Existing installer, MCP, update and documentation edits are outside this change and must be preserved.

| Requirement | Status | Evidence / next check |
|---|---|---|
| Reuse current Processes and Connections Top choices without new preferences | Verified | Supported finite choices and All covered by report tests |
| Manual report retains process/view semantics and adds sample-aligned connections | Verified | Connections smoke checks identical text after switching activity tabs and includes a flow absent from the OS table |
| Automatic report uses selected limits, trigger exception, independent connection groups | Verified | Report tests and written capture-smoke reports |
| Localized report labels, coverage, counts, observation intervals | Verified | All ten resource cultures; resource tests passed |
| ETW endpoint metadata and bounded process-lifetime flow accounting | Implemented | Deterministic ledger coverage passes; native acceptance remains separate |
| SID isolation, endpoint validation, additive protocol compatibility and message budget | Verified | Ledger/pipe/adapter tests, older DTO compatibility, combined quality flags, maximal payload truncation |
| Client baselines, resets, stale readings and history memory accounting | Verified | Session/history tests, flow-only siblings and unknown owner lifetime groups |
| Native multi-peer TCP/UDP IPv4/IPv6 attribution and loss acceptance | Blocked | Explicit test extended; both restricted and unrestricted process tokens are non-admin |
| Before/after UI performance and synthetic endpoint budgets | Verified | Four alternating UI runs plus four tiering-disabled pairs, identical pixels/allocation in the control; four warmed endpoint-ledger pairs exercise 8,192 keys and response sizing. See PERFORMANCE-HISTORY.md. |
| Native collector CPU/heap and queue burst acceptance | Blocked | Requires elevated native collection; synthetic timing is not a native collector budget |
| Default desktop and Store builds/tests | Verified | Final desktop gate: 1,697 tests. Store gate: 1,648 tests, followed by 31 client/history and 36 service checks covering final shared-code fixes. Zero build warnings/errors. |
| Snapshot and Connections smoke, PNG inspection | Verified | Snapshot 64 and Connections 23 renders pass; representative PNGs and copied/saved text inspected |
| Capture smoke including Settings layout | Blocked | Baseline-reproduced Settings Monitoring overflow of 47 DIPs; report/card checks before that assertion pass |
| User/technical documentation and final deviations | Verified | README, user/developer guides, design and performance history updated; local links checked |

## Baseline

SDK: 10.0.404 (global.json permits latest patch of 10.0.402), benchmark runtime 10.0.15. Service TraceEvent dependency: 3.1.23. The initial solution build succeeded, but benchmark compilation overlapped implementation edits, so a complete clean baseline gate is not claimed. Focused tests using the baseline output passed 41/41 after the sandbox test host failed to connect and was retried unrestricted. Runtime service installation is outside this task; use isolated test collectors instead.

The final default desktop gate at `artifacts/verify/20261111-192328-953-desktop` builds the solution and benchmark with zero warnings/errors and passes 1,697 tests (10 checks passed, 0 failed). The Store gate at `artifacts/verify/20261111-191717-563-store` passes 1,648 tests and both builds without warnings/errors. Focused Store follow-ups also pass 31 client/history and 36 service tests after the final unknown-owner and coverage refinements.

The smoke run at `artifacts/verify/20261111-191349-164-desktop` passes Snapshot (64 renders) and Connections (23). Capture smoke writes both expected reports and capture-card PNGs, then fails `Settings Monitoring scrolls by 47 DIPs in Dark`. The preserved baseline reproduces that exact failure at `artifacts/capture-info-baseline-smoke-unrestricted/failure.txt`; no unrelated Settings layout change was made. A separate explicit ledger performance measurement passes, with four warmed alternating process-only/endpoint-flow runs under `artifacts/network-flow-benchmark/ledger.json`. Native ETW and MCP profiling remain excluded from the default gate by their existing explicit-test contracts.

## Implementation choices and limits

- Service flow keys are globally limited to 8,192 and retained until the owning process lifetime expires (ended lifetimes have the existing 30-second retention). This intentionally departs from inactive-flow recycling: it preserves cumulative tuple identity without adding a second flow-session identifier. New endpoint detail can remain capped for long-running processes with heavy peer churn; app totals continue and `FlowQuality` warns about omitted detail. The client retires absent flow baselines after 30 seconds and conservatively rebaselines reappearances.
- Typed addresses/ports pass through the bounded ETW queue and are formatted only after owner verification. Scope-less IPv6 link-local events keep process bytes but skip ambiguous endpoint attribution and mark flow coverage incomplete.
- Protocol version 1 adds optional flow capability/data. Older payloads remain app-only; old DTO readers ignore additive fields. Flow truncation preserves a deterministic prefix within the existing 2 MiB response bound. Existing behavior for process-only payloads that exceed the bound remains a rejected response, not an oversized send.
- Rates and totals describe ETW-accounted endpoint pairs, not unique socket sessions or application payload. No installed service was replaced, no package was signed and no release was published.

## Deferred by design

Unique socket-session lifetimes, exact duration and selected-period endpoint totals remain deferred. Initial endpoint traffic aggregates process-lifetime endpoint pairs, with observed rates and monitoring-baseline totals.
