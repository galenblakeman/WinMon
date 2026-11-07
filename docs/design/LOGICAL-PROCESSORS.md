# Logical processor charts

The selected mockup adds **Logical processors** to the full-view chart selector, preserving the resource gauges, chart toolbar and activity panel. CPU 0 through CPU N each get a small history chart with a fixed 0–100% scale, a current or selected-interval percentage and a common time range. The grid adapts to processor count rather than assuming the mockup's sixteen processors.

## Requirements and validation

| Requirement | Status | Evidence |
| --- | --- | --- |
| Real per-processor readings, independent of machine CPU percentage | Verified | Sampler tests and native chart smoke on 12 logical processors |
| Shared chart selection, time ranges and capture markers | Verified | Chart/snapshot/capture smoke and cell-local selection tests |
| Save chart choice and restore hidden CPU when selected | Verified | Preference round-trip tests and native chart smoke |
| Preserve processor history across restarts; read old history | Verified | Restart-history format 3 round trip and format-1/2 fixtures |
| Localized labels and accessible readings | Verified | Resource tests for all nine satellites and graph automation-peer test |
| Dark/light/narrow/native screenshots | Verified | Inspected 16-processor, dense 64-processor, capture and unavailable renders |
| Performance comparison | Verified | Four alternating UI benchmark pairs, tiering-off diagnostic and two swapped-launch native profile pairs; see performance history |

## Implementation

`MonitorSample.LogicalProcessorPercents` stores optional, nullable per-processor percentages. `WindowsCpuSnapshotProvider` reads Windows processor timing counters; `WindowsCpuSampler` calculates interval utilization and rebaselines on gaps or topology changes. Total-machine and process percentages retain their prior semantics. `MonCore` stays portable.

`CpuGraph` draws the grid using existing cached text, brushes, pens and trace buffers. Missing observations break traces. Clicking a cell maps its horizontal position into the common history range. Keyboard navigation and automatic captures keep their existing semantics. Selecting Logical processors shows CPU if hidden; other resource visibility choices are retained.

## What landed and departures

The feature landed in the sample/provider/sampler, restart history, `CpuGraph`, the existing selector/preferences, localized resources, diagnostics and benchmark. The production layout uses an adaptive grid rather than a fixed 4×4 layout. Short cells use labeled sparklines; their exact values remain in tooltips and UI Automation instead of taking space from the trace. Larger cells show the current or selected interval's rounded percentage. The selector is 150 DIPs wide so its English label is visible. Existing chart chrome follows the current app, including changes made after the illustrative mockup.

The repository verification workflow passed in isolated output folders (the shared verification output was locked by other work). Standard: 971 tests, chart/snapshot/capture smoke, 85 renders. Store: 973 tests. The temporary verification copy only changed output paths and disabled the unreachable NuGet vulnerability audit. Relevant screenshots under `artifacts/logical-processors/verify/20261107-113547/` were visually inspected. Real high-contrast desktop switching, screen-reader operation and a physical 64-processor machine were not exercised; the 64-processor layout uses synthetic data.

[Performance measurements](../PERFORMANCE-HISTORY.md#logical-processor-charts-november-7-2026) found no reproducible existing-view regression. The new 12-processor grid took 6.03 ms per refresh in the pinned/selected-process workload; saved-history size grew as expected while full restoration remained within the existing budget. Native profiles showed no consistent collector regression. No dependencies were added.
