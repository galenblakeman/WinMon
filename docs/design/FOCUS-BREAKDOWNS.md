# Focus breakdowns

Status: implemented, 2026-11-27. Follows [chart scales and network direction](CHART-SCALES-AND-NETWORK-DIRECTION.md), which split network receive and send.

Focus showed one percentage line per resource, although every sample already carries the readings that explain it. Each Focus resource now draws its own breakdown from those readings. No collection, preference or stored format changes; readings Windows does not supply leave gaps, like any other trace.

| Focus | Added | Readings |
| --- | --- | --- |
| CPU | Kernel band shaded under the total trace | `CpuKernelPercent` (kernel time as a share of all CPU time) |
| Memory | Stacked in use, modified, standby and free, plus a dashed committed line | `UsedMemoryBytes`, `ModifiedBytes`, `StandbyBytes`, `FreeBytes`, `CommittedBytes / CommitLimitBytes` |
| Disk | Two lanes above the context lanes: I/O (read solid, write dashed) and response time | Read and write bytes per second and response seconds for the focused volume |
| GPU | Sparse dotted dedicated-memory line | `GpuMemoryUsedBytes / GpuMemoryTotalBytes` |

## Decisions

- **Shared percentage axis where possible.** Kernel share, memory composition, commit and GPU memory are percentages, so they use the existing left axis. The CPU, Disk and GPU temperature overlay keeps the right axis.
- **Memory stack.** Used memory is total minus available, so it already includes modified pages. The stack is in use without modified, then modified, standby and free, each clamped to the plot. It replaces the single memory fill; the used trace stays on top of the modified layer. The layers are same-hue fills drawn from the baseline with opacities 0.05, 0.18, 0.3 and 0.15 (free to in use), so each band reads one step darker than the one above it; legend swatches use each band's combined opacity. Equal opacities, and then a smaller free-to-standby step, left the bands too close to tell apart in the snapshot render.
- **Disk lanes.** Throughput and response time need their own units, and the right axis already holds temperature. They take two lanes at the top of the context area, each twice the height of a resource lane. Each lane is labeled with its range ("0–500 MB/s", "0–20 ms"), using the 1-2-5 rounding from `NetworkChartScale.RoundUp`, with floors of 100 KB/s and 1 ms: below the lane name in the gutter when the lane is at least 28 DIPs tall, otherwise inside its top-right corner. The Read and Write legend sits in the main Focus legend, because an in-lane legend collided with the traces in the first render.
- **Legends.** Items follow the resource name inside the plot, or start at the plot's left edge when the temperature legend occupies the band above it. Items that do not fit the width are dropped rather than overlapping.
- **High contrast.** Windows high contrast draws no washes, so the kernel share becomes a dotted line and the memory stack is omitted; the commit and used lines remain.

## What landed

`CpuGraph.FocusDetails.cs` holds the breakdown series, scales, legends and disk lanes; `CpuGraph.cs` routes the extra series through `SeriesValue`, `SeriesSource` and `DrawSeries` (which gained a fill opacity), and offsets context-lane selection markers by the disk lanes. Three strings (Kernel, In use, Response) were added in all ten languages; the rest reuse detail labels. `FocusDetailsTests` covers the memory stack, commit and GPU memory shares, millisecond labels and each resource's drawing order. The snapshot smoke's design fixture gained these readings and a Memory Focus render (`chart-design-focus-memory.png`).

The GPU memory line and the high-contrast kernel line use a dot pattern half as dense as `DashStyles.Dot` (dash 1, gap 3), because stroking the denser pattern added about 1.5 ms per GPU Focus render; see the performance history.

Departures from the review's suggestion: none in scope. Selection marks only the resource's main reading; the breakdown values are not added to the hover tooltip.
