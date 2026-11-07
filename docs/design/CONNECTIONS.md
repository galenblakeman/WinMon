# Connections

## Scope

Add the approved app-first Connections activity tab. Group endpoints by process lifetime, show executable icons with the existing generic fallback, and expand a process to inspect protocol, local endpoint, remote endpoint and TCP state. Search names/PIDs and filter All, TCP or UDP. Copy the selection and navigate to the matching live process.

Collection uses Windows IP Helper owner-PID tables for IPv4 and IPv6 without elevation, ETW, drivers or a service. UDP tables describe local endpoints only. Counts describe endpoints, never bandwidth or traffic volume. The panel remains live when the resource chart shows historical data.

## Requirements and validation

| Requirement | Status |
| --- | --- |
| Bounded ordinary-user IPv4/IPv6 TCP/UDP collection and unavailable/partial states | Verified |
| Stable process lifetime association and safe navigation | Verified |
| Expandable process rows, icons and TCP/UDP counts | Verified |
| Search, protocol filter, copy and keyboard access | Verified |
| Live-only presentation; no collection on hidden tabs | Verified |
| English and translated UI resources | Verified |
| Collector tests, UI smoke and inspected screenshots | Verified |
| Baseline and changed performance measurements | Verified |

## Outcome

Implemented in `MainWindow.Connections.cs`, `ConnectionsView`, portable endpoint records and `WindowsConnections`. The selected grouped layout landed with process icons, protocol filtering, live endpoint details, copy/navigation and the existing shared action drawer. Endpoint rows use the existing flat list and virtualized scrolling rather than nested list controls. Search/protocol choices are session-local; there is no new persisted settings schema.

The final Connections snapshot passed `Verify.ps1 -Smoke connections,search,feature,usage,compact`: 982 tests, all five smoke modes and both builds passed with zero warnings. Dark, Light and narrow Connections PNGs were inspected. Native smoke reported `Elevated: false`, successful reads of both address families, and ownership of actual loopback TCP/UDP endpoints. Unit tests cover IPv4/IPv6 parsing, unknown state values, malformed tables, duplicate UDP bindings, retained selection/filter state, group reports and lifetime-safe navigation.

One deliberate data limitation differs from the ideal mock: this Windows installation reports state 0 for a listening endpoint. WinMon shows that state as unavailable and does not infer Listening; established TCP state was verified independently. UDP local endpoints never gain a peer or a fabricated state. Collection failures do not leave stale rows actionable.

Concurrent Info-window/updater work appeared during verification, so an isolated copy of HEAD plus the Connections files under `artifacts/connections/source` was used to verify and measure this feature independently. After those unrelated edits became buildable, the integrated checkout passed 990 tests and `Verify.ps1 -Smoke connections,search,feature,usage,compact` with zero build warnings/errors. The final smoke also protects Capture history when Escape belongs to the protocol menu. Nonzero peer addresses are preserved independently of the OS-reported TCP state; the native check accepts the established state on either end of its loopback connection.

Four alternating performance pairs found no material timing regression. The 60-process/480-endpoint Connections scenario took 2.44 ms per complete UI refresh; the native smoke separately verified collection with a non-elevated token. Measurements and limitations are recorded in [the performance history](../PERFORMANCE-HISTORY.md#connections-activity-tab-november-7-2026).
