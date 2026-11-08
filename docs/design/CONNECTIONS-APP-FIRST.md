# App-first Connections and Capture

Accepted November 7, 2026 for WinMon 3.5.1 on .NET 10.

## Design

Make Connections follow the activity-tab conventions: executable groups first,
Top ranking, search, App only, and a shared action drawer. The primary table
contains identity, separate receive/send rates, TCP count and UDP count. Local
endpoints belong in Properties; expanded and Individual endpoint rows retain
remote destination and state. Endpoint traffic is never inferred from owner
traffic. Preserve protocol filters and lifetime-safe navigation.

Capture shows recorded endpoints, owners and accounted traffic from the
selected sample, including while another tab was visible during collection.
Missing history remains unavailable. Rates describe the existing delayed
five-second window, normally ending three seconds before collection; they do
not represent instantaneous traffic at the selected point.

## Requirements and verification

| Requirement | Status |
| --- | --- |
| App-first columns, expandable endpoints and full Properties | Verified |
| Persisted Top, receive/send/TCP/UDP ranking and App only | Verified |
| Unfiltered endpoint and process-traffic header summaries | Verified |
| Continuous endpoint capture and exact historical selection | Verified |
| Bounded history and explicit missing-data behavior | Verified |
| All-language labels and keyboard Properties access | Verified |
| Full verification, Store build and visible smoke checks | Verified |
| Before/after performance measurement | Verified |

## Outcome

Implementation lives in ConnectionsView, MainWindow.Connections,
WindowsFrameSource and MonitorSession. ConnectionHistory retains observations
within chart retention and a shared 100,000-entry endpoint/owner/traffic budget.
On a busy machine this can shorten connection history. It remains in memory;
resource history restored across restarts has no corresponding connection
observations. No packet payloads are collected or saved by this change.

The table keeps remote destinations and states on expanded rows rather than
removing endpoint inspection entirely. Properties reuses the existing read-only
details window used by Services and Usage. Rates remain optional and require
the existing network-monitoring service; endpoint collection needs no elevation.
CSV export and per-socket transfer accounting are outside the accepted scope.

Connections remains available without traffic monitoring. Receive/send ranking
items are disabled with an explanation; the effective ranking falls back to TCP
count while preserving the saved throughput choice. Rate columns and summaries
remain hidden. A live desktop-only enable link opens the existing Network
settings section without changing or provisioning anything automatically.
Captured observations retain their own availability, independently of the live
setting. The implementation uses the existing ranking and Settings paths; it
adds no sampling loop or history processing.

Availability gating passed desktop and Store verification, with regression
coverage for fallback ranking, preserving a saved throughput choice across
Top changes and enable/disable transitions, platform-specific link visibility,
and the Settings request event. Connections smoke passed with fifteen renders;
the disabled desktop link and the restored enabled layout were inspected.

Desktop and Store verification passed, including the Connections, search,
compact and Capture smoke modes. Connections produced fourteen inspected
renders, covering light/dark, minimum width, German, Properties and historical
observations. Final focused Connections/resource checks passed 61 tests.
Four sequential baseline/changed benchmark pairs and two native sampling
profile pairs with reversed launch order are recorded in the
[performance history](../PERFORMANCE-HISTORY.md). Individual endpoint mode
does more ranking/formatting work; continuous history also adds bounded CPU
and memory cost. These costs are measured rather than treated as free.
