# Read-only MCP Usage and lifetime history

The MCP needs to answer calendar Usage questions, inspect an exact retained process lifetime, and sample the process metrics already available in snapshots.

## Scope

- Add `get_usage_history(startDate, endDate, sort, filter, limit)` over the standard desktop Usage archive, with an optional explicit absolute local file path for portable desktop archives (UNC/device paths rejected). Reuse `UsageArchive.LoadAsync`, its validation and calendar aggregation. Bound requests to 35 inclusive calendar days and use the archive time zone. Return only public names and numeric totals, daily lifetime observations, observed time and explicit requested/retained bounds. Never return paths, executable keys or inferred current running counts. Missing history means not observed; malformed or inaccessible archives mean unavailable. Read cancellation propagates.
- Extend the existing weighted performance aggregate with process GPU, thread and handle readings. Reuse null/gap/stale exclusions and coverage. Do not add collectors or network sampling.
- Extend retained performance history with an optional complete PID/start-token/domain selector. Filter raw observations before ranking so a quiet process remains visible. Preserve empty points and system context; never switch to a reused PID or another clock domain. Queries do not acquire sampling leases.

All tools remain read-only, bounded to the existing 256 KiB wire budget, and treat OS/archive strings as untrusted data. The archive is read anew per request, with no new persistence or background work. Existing unfiltered history and live Usage semantics remain unchanged.

## What landed

Implemented in `UsageArchiveTools.cs`, `ActivityTools.cs`, `PerformanceSampling.cs` and `Responses.cs`. Discovery tests and the standalone stdio gate include the new tool. Regression tests cover archive privacy, dates, cancellation, wire bounds, PID reuse and missing trend coverage. Client guidance is updated alongside the tool reference. The implementation follows this scope; per-process network trends, desktop replay, longer retention and mutation tools remain outside it.

## Verification (November 15, 2026)

- Final `tools/Verify.ps1` passed: 1,970 tests across eight projects, with zero build warnings/errors. Evidence: `artifacts/verify/20261115-100757-354-desktop/`. The MCP project passed 102 tests; Store-only and explicit native/performance checks remained excluded as reported by the gate. Sandbox test-host connection failures were resolved by running outside the sandbox.
- `tools/Test-McpStdio.ps1` passed against the final verified executable, including discovery of all 13 read-only tools. Evidence: `artifacts/mcp-gap-stdio-final/`. The SDK integration test also invokes the archive query and exact-lifetime history selector over a real stdio connection.
- Before/after aggregate timing and allocations are recorded in [performance history](../PERFORMANCE-HISTORY.md#mcp-process-trend-metrics-november-15-2026). Native end-to-end profiling and UI smoke modes were not repeated for this MCP-only change. The installed MCP was not replaced.
