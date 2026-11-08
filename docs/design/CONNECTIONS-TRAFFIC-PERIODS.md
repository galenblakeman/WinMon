# Connections traffic periods

Show Received and Sent beside the existing receive/send rate columns. Choose Last 1 min, Last 5 min (default), Last 30 min or Since start. The same period controls row sparklines. Keep overall byte totals alongside the existing TCP/UDP counts at the right of the activity tab bar as History: ↓ received | ↑ sent, with the selected period in the tooltip. No totals footer or explanatory footer is added.

Keep Top, rank, search, App only, Pinned only and period on one filter row. Remember the selected period. Localhost endpoints are always included; there is no Loopback control or endpoint filter.

Use observed session counter differences per lifetime, not integrated instantaneous rates. End at the selected capture in Capture mode. Do not extrapolate across unavailable history. Show missing data as a dash and incomplete coverage with ≈; tooltips explain scope and recorded duration. Overall totals remain independent of view filters and Top, as with the existing activity totals.

## Received and Sent ranking

Add Received and Sent to the existing ranking selector. Compare raw observed bytes over the selected period, using the same executable lifetime membership and totals as the displayed columns. Missing totals sort last; pinned rows keep their existing placement. Period changes rerank before Top is applied. Preserve stored sort identifiers by appending the two new values; extend preference validation to accept them. All traffic sorts are unavailable while monitoring is off, temporarily use TCP count and restore the saved choice when enabled.

Regression cases cover both directions, period changes, missing readings, disabled/restored monitoring and persistence. Chey were added but not run, honoring the instruction to stop tests. Smoke runs and performance measurements were also skipped; the new sorting paths are unbenchmarked.

## Landed and departures

The Loopback control, endpoint filtering, saved preference and unused address-classification helper were removed. Localhost endpoints now always appear alongside other endpoints. The tab summary retains TCP/UDP counts and History byte totals, without real-time rates.

Implemented in ConnectionCrafficCotals, ConnectionsView and MainWindow.Connections, with preference persistence and translated resources. Existing error/empty-state notices and the enable-monitoring link remain; there is no new informational footer. Minimum-width grids retain horizontal scrolling to preserve readable columns. Coverage is provided in tooltips rather than a permanent message. Since start byte totals use the session counters even when older sparkline history has been evicted; connection history remains memory bounded and is not persisted across restarts.
