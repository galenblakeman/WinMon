# Chart scales and network direction

Status: implemented, 2026-11-27.

A review of the resource chart views and the README examples found four readability problems. All four fixes use readings the chart already has; no collection, preference or stored format changes.

1. **Network scale in bytes.** `NetworkChartScale.Maximum` rounded the busier-direction peak to a 1-2-5 step in bits per second, but every rate is shown in bytes. A 100 Mbps scale read as 12.5 MB/s, and the Focus axis quarters became 3.13 / 6.25 / 9.38 MB/s. The step now applies to bytes per second, with a 100 KB/s floor in place of 1 Mbps, so the quarters are exact (for example 2.5 / 5 / 7.5 MB/s). The result stays in bits, so desktop gauges, the CLI and every chart keep one shared scale.
2. **Lane ranges.** Small multiples and Heatmap labeled each lane with the top of its scale ("CPU 100%"), which read as a current value. Lanes now show the range through the localized `LaneScaleRange` format ("0–100%", "0–10 MB/s"). Focus axis ticks keep bare values, so the Focus gutter is unchanged.
3. **Receive and send in Focus: Network.** The chart plotted only the busier direction. Focus: Network now draws receive as a solid line with the area shaded and send as a dashed line on the same throughput scale, with an arrow legend (↓ solid, ↑ dashed) after the resource name. Selection marks both readings. Other views, lanes and the heatmap keep the single busier-direction trace. Both directions break together when either rate is missing, as the busier-direction trace did.
4. **Overlay fills.** With every resource visible, six translucent fills stacked into a gray film under shared spikes, and the flat memory fill tinted the plot. Overlay now shades the areas only when two or fewer resources are visible. This also applies to the History timeline, which uses the same overlay.

## What landed

Landed in `NetworkChartScale`, `CpuGraph` (`SeriesValue` now handles the network scale for every view, along with `DrawFocusNetwork`, `LaneScale` and the overlay fill rule) and the ten string resources. `NetworkChartScaleTests` and `NetworkScaleTests` cover the byte-rate steps and the new floor. The CLI shares the scale, so its network panel range, bar and history follow it (a 1 MB/s peak now reads "0–1MB/s" instead of "0–1.25MB/s"). The rendered-view baselines in `tests/WinMon.Cli.Tests/Fixtures/InteractiveView` and four CLI assertions were updated; no other text in the screens changed. The existing snapshot and chart smokes render every chart type, including Focus: Network.

Departures from the review's suggestions: the lanes show a range rather than the live value, which keeps axis labels static and their cache warm. Overlay drops its fills by series count rather than following hover, because hover would change the drawing as the pointer moves.
