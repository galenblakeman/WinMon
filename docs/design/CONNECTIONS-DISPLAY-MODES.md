# Connections display modes and summaries

## Accepted design

- Move the system-wide TCP/UDP totals beside the activity tabs, matching the other tab summaries.
- Remove the Live connections heading and its row; retain the existing live-only history notice.
- Add Individual / Grouped chips using the Processes toggle styling. Individual lists endpoints; Grouped combines exact executable paths across process instances and expands to endpoint rows with owner PIDs.
- Show exact local/remote endpoints when there is one distinct value. Otherwise summarize distinct addresses and ports, with full values in tooltips.
- Count TCP states honestly, including unavailable states. UDP contributes to local endpoints and protocol totals only.
- Preserve lifetime-safe process actions, stable endpoint selection, duplicate bindings, unavailable/partial states, icons and keyboard behavior. Unknown paths must not merge by display name.

## Requirements

| Requirement | Status |
| --- | --- |
| Header totals independent of filters/mode, live while the body is collapsed | Verified |
| Compact single-row toolbar and display-mode chips | Verified |
| Flat Individual endpoint rows and expandable executable groups | Verified |
| Group endpoint/state summaries and complete tooltips/reports | Verified |
| Exact-lifetime individual/group navigation and unknown-owner handling | Verified |
| Unit tests, native/UI smoke and visual checks | Verified |
| Before/after UI performance measurements | Verified |

## Outcome

The header totals, display-mode chips, executable grouping, address/port summaries, state counts and protocol-tagged endpoint tooltips landed in ConnectionsView and MainWindow.Connections. Display mode and filters are session-local; the existing ordinary-user collector is unchanged.

The isolated integrated snapshot passed 1,005 tests and the Connections native/UI smoke. Feature, search, usage and compact smoke modes also passed before the final internal key-allocation change; final Dark/Light and narrow Connections renders were inspected. Four alternating before/after performance pairs measured grouped refresh at 2.34 / 2.77 ms and the new Individual mode at 2.80 ms for 480 endpoints. Richer grouped summaries add 639.22 KiB per refresh in that fixture; executable-key reuse reduced allocation from the initial implementation. Full results and limitations are in [the performance history](../PERFORMANCE-HISTORY.md#connections-display-modes-and-summaries-november-7-2026).
