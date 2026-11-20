# Network contributors in History

History should answer which app was transferring data at a network spike. Reuse `MonitorSession.Connections.Captures`, joining only an exact `SampleKey` and full Windows process lifetime identity. Freeze those frames when opening or refreshing History. Include retained traffic owners omitted from compacted process samples.

Selected moment ranks by receive + send bytes/second and displays a Network column. Compare periods uses those same rates for process means and deltas; the resource summary still measures the configured adapter. Traffic covers all adapters and the service's delayed provisional window, so it need not coincide with the system chart. Missing, incomplete, warming-up, capacity-limited, discontinuous and unavailable observations stay unknown. No CPU fallback or inferred zero traffic.

## What landed

Implemented the joins and coverage checks in `IncidentTimeline`, frozen traffic handoff in `MainWindow.FlightRecorder`, the Network column and comparison formatting in `FlightRecorderWindow`, translated attribution text and regression tests in `IncidentTrafficTests`. Capture smoke saves/reopens a synthetic incident whose network leader is absent from the compacted process list. No collector or sampling-path changes. No design departures.

Validation: the repository verification script, copied under `artifacts/incident-verify` with isolated output paths to avoid a concurrent build, passed all 2,041 executed desktop tests and 35 capture renders with zero build warnings/errors (`20261119-222658-166-desktop`). Final History PNGs were reviewed in dark/light at normal/minimum sizes, including both tabs and empty history. Explicit, Store-only, signed-package and elevation-dependent checks retained their normal conditional skips. On-demand measurements are recorded in `docs/PERFORMANCE-HISTORY.md`.
