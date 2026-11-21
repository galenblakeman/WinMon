# Preserve recorded and recovered network rates

Two corrections reuse the existing session accounting and immutable captured frames.

`ProcessNetworkSession` deliberately keeps cumulative byte totals incomplete after a disconnect or lost observations. Its fresh rate fields can recover once the service window is past the interruption. History and scoped capture rules accept that sticky `Incomplete` flag alongside `Provisional`, while keeping missing rates, warming-up, unavailable, discontinuous and capacity-limited observations unavailable. History still joins the exact sample and process lifetime.

Each `MonitorSession.Pending` carries the raw Stopwatch timestamp from collection, including failed captures. The drain uses that timestamp for traffic freshness and missing-snapshot accounting. UI delays cannot erase valid historical rates or move a collection gap's recovery boundary to the later drain time. Resource-history clock rebasing does not change this clock. Snapshots already stale when collected remain unavailable.

## What landed

Implemented in `MonitorSession`, `IncidentTimeline` and `CaptureRules`. Regressions cover queued fresh/stale snapshots, History readings/comparisons after recovery, and application/service capture rules restarting their duration after a disconnect. Existing lifetime, missing-reading and quality guards remain covered. No new dependencies, settings, collectors, file formats or design departures.

Validation: the four new regression cases failed against the original code. The final focused MonCore suites passed 113 tests with zero failures. `Verify.ps1 -Smoke capture,connections` passed all 13 steps and 2,152 executed tests; its 58 rendered PNGs were reviewed. Additional strengthened guard cases ran in the final focused pass. The shared-session before/after measurement is recorded in `docs/PERFORMANCE-HISTORY.md`. Native disconnects under sustained CPU load and whole-app profiling were not exercised.
