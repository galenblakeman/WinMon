# History period comparison

Help explain a selected slowdown using the existing frozen recording. Reuse History navigation, resource selection, refresh and coverage; add no collection, settings or persisted format.

Compare three adjacent 10-second windows centered on selection using monotonic collection time (wall time for fixtures): Before [−15, −5), During [−5, +5), After [+5, +15). Display observed resource averages and observation counts, then the top 10 process lifetimes by absolute During minus Before change. Keep the original instantaneous table in a second tab. Use existing themed headers, keyboard-accessible tabs, horizontal table scrolling and translated resources.

Valid finite readings contribute arithmetic sample means. Missing and compacted observations remain unknown, and counts expose partial coverage. Never infer process absence as zero. Join full lifetime identities so PID reuse does not create a false delta. Unknown start tokens cannot support lifetime comparisons. Network process values retain the existing CPU fallback; process I/O is not physical disk attribution. No automatic causal diagnosis or threshold detection is added.

## What landed

Validation: `tools/Verify.ps1 -Smoke capture` passed the app/benchmark builds with zero warnings/errors, all 2,011 executed desktop tests and 35 capture renders (`artifacts/verify/20261119-203613-197-desktop`). Both tabs were reviewed in light/dark at normal/minimum sizes. Expected explicit, elevation/package-dependent and Store-only checks were not executed by the desktop gate. The isolated analysis cost is recorded in `docs/PERFORMANCE-HISTORY.md`.

Implemented `IncidentTimeline.Compare`, the default Compare periods tab and Selected moment tab in `FlightRecorderWindow`, translations for all supported cultures, focused comparison tests and capture-smoke checks/renders for both tabs. The comparison runs only on navigation or refresh, outside the sampling path. No design departures; variable window lengths, time-weighted integration, per-process network joining and recording/export formats remain outside this feature.
