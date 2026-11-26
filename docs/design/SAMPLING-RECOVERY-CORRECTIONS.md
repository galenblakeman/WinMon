# Sampling recovery corrections

The November 26, 2026 audit found that failed CPU collection could prematurely end an MCP process recording, and that one failed executable-path lookup prevented recovery for the rest of a running process's lifetime.

## Recording continuity

An empty process list from Windows CPU failure is not proof that a process disappeared. `DiagnosticTools.SamplePerformance` now uses the existing `UsageLedger.IsValid` rule before ending a pinned recording for absence. Resource gaps remain unavailable observations; independently valid process inventories remain usable without system CPU. PID reuse in a valid inventory still ends the original lifetime's recording. Collection errors and partial metric coverage remain visible in the report.

## Path recovery

`ProcessImagePathProvider` caches a path and the monotonic time of its lookup. Successful paths remain cached. Empty results retry after 30 seconds, and another failure starts the same backoff again. A PID-only fallback path remains unpublished until a later system snapshot confirms the same PID and start token. Cache swapping drops absent lifetimes and their retry deadlines. Tests use an instance-local clock and the existing lookup delegates, with no sleeps or native process lookups.

## What landed

The recording guard landed in `src/WinMon.Mcp/PerformanceSampling.cs`; path recovery landed in `src/MonCore.Windows/Monitoring/ProcessImagePathProvider.cs`. Regressions cover failure/recovery, independent inventories, retry boundaries, repeated failures, successful caching, disappearance and PID reuse. There are no departures from this design. Native lookup failures were simulated; this does not establish occurrence on a deployed machine.
