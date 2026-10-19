# UX review adjustments

Date: 2026-10-19. Applied the recommendations from a screenshot review of the compact view.

## Changes

- **Network readout leads with throughput.** Link utilization stays near 0% on fast links, so the large value is now the busier direction's rate (Kbps/Mbps/Gbps, three significant digits). Utilization moves to the caption. The overlay plots network throughput on its own 1-2-5 scale (minimum 1 Mbps), labelled inside the chart in the network color.
- **Disk color** changed from amber to coral (`#FF7B54` dark, `#B8461B` light) to separate it from the yellow-green CPU line for red-green color vision deficiency. The CPU series also gets a light area fill, so it does not rely on hue alone.
- **One surface hue.** The blue title bar and section header bands are replaced by a neutral title bar (`#1B2026` / `#ECEFF3`) and full-width divider lines. `SectionHeaderSurface` is removed.
- **Matching section headers.** Both sections use a 15px semibold title and an 11px muted subtitle. The duplicate "Last 2 min · 0–100%" caption under the chart is now the resources subtitle.
- **Labelled time guides.** Four time divisions, each labelled (for example 90s / 60s / 30s, or 7.5 / 5 / 2.5 min).
- **Live state beside its control.** "Live · every 1s · HH:mm:ss" (or Paused) sits next to Pause and Return to live in the resources header, replacing "Recording" in the footer.
- **Other processes row.** Measured processes outside the top list are summed into one muted row (CPU and I/O; memory is omitted because working sets share pages). CPU that no measured process accounts for is reported in the footer as "N% unattributed", plus the count of processes that need administrator access. A tooltip explains both. No process row is invented for unattributed CPU.
- **Shared sparkline scale.** All process rows share one scale, so sparkline heights compare across rows. Sparklines average into 2px bins with a thinner line and lighter fill.
- **Themed scrollbars.** An implicit 8px ScrollBar style has a rounded thumb (`Line`, `Muted` on hover or drag), a transparent track, and page-click support. The ScrollViewer template puts a 10px gutter before the vertical bar, so right-aligned columns such as I/O no longer touch it. Column headings stay aligned because they bind to `ViewportWidth`. The diagnostics now also capture `light-scroll.png`.
- **Sampling decoupled from the UI.** The collector used to await a dispatcher callback after each capture, so a UI stall of more than about 2.5 s delayed the next capture and the chart drew a gap. Captures now go into a concurrent queue that a single coalesced dispatcher callback drains in order. Pause/resume generations still discard stale samples. The chart's 2.5 s gap rule is unchanged for real delays such as sleep or scheduling stalls. The diagnostics block the UI thread for 3.5 s and require on-schedule intervals with no gap. With the old UI-blocking delivery the same check fails with a 3.42 s interval.
- **5-second rolling process ranking.** Windows charges process CPU in 15.6 ms clock ticks, so in a single one-second interval most lightly active processes read exactly zero. On a quiet machine the list could drop to 1–2 rows (a check here found 27 of about 1,000 processes non-zero in one second). `ProcessRanking` now averages the last five intervals ending at the live or selected sample, so a process stays listed while any interval in the window measured it. A gap or a delay over 2.5 s starts a new window. Memory is the latest reading and I/O is averaged. The subtitle, CPU % tooltip and coverage footer use the same window. Sparklines still plot the raw one-second values. This is the labelled smoothed ranking anticipated in the original design.
- **Labels.** "Tray" is now "Hide to tray", Expand has a tooltip, and the title uses "·" to match the window title.

## Verification

Release build had zero warnings. 82 NUnit cases passed, including new network scale and rate formatting cases. The native smoke run exited 0, with screenshots in `artifacts/ux-review/` (git-ignored). Actual desktop DPI and color-vision simulation were not checked.
