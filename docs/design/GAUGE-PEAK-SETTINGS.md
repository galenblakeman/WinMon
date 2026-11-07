# Gauge peak settings and details

## Intent

Make recent gauge peaks easier to interpret without changing live values or increasing tile height. Full and Compact views share the behavior.

## Decisions

- Small arc peak labels always use native values when available, with percentage fallback. Monitoring settings offer only a shared peak window of 1, 2 (default), 5 or 10 minutes, independent of chart range.
- The marker and native label refer to one sample: the maximum utilization within the window. On ties, choose the most recent sample. Use RAM used, disk throughput and dedicated GPU memory where available; CPU and missing native values retain percentages. A throughput or GPU-memory label is the value at utilization peak, not an independent maximum.
- Network shows its download peak with the same icon, in compact rate notation. The tooltip includes the full download rate and automatic scale. The scale and peak use the configured window and current adapter.
- Append peak details to the existing resource tooltip: window, utilization, named native value when available, local timestamp and elapsed seconds relative to the latest sample. The whole tile and corner expose it; existing keyboard focus paths remain available in both views.
- Invalid/gap samples and other configured disk volumes do not contribute. Elapsed windows and peak ages use monotonic time when available. Only retained history is available, so memory-limited retention can shorten a requested window. Unavailable current readings hide marks and labels and report a missing peak.
- Keep the 98-DIP readout height. Arc headers show the selected capped-peak icon (option A) and value at every width, with no visible Peak prefix. The icon is decorative; the full accessibility name and tooltip identify the peak. Long native values can trim to preserve room for the resource name; the tooltip retains the complete measurement.

## Requirements and verification

| Requirement | Status |
| --- | --- |
| Persist and validate the window; default 2 minutes; ignore the removed format setting | Verified |
| Select peak samples and format same-sample native values | Verified |
| Shared pointer/keyboard peak tooltip in Full and Compact | Verified by shared model checks and tooltip renders; screen reader not manually tested |
| Peak-marker icon and value at every arc-header width, with full accessibility text | Verified |
| Localized settings and text | Verified |
| Gauge/Compact/Settings smokes and review of relevant PNGs | Verified |
| Complete normal/Store verification | Verified |
| Before/after UI performance comparison | Verified with four alternating pairs; see the performance history |
| Performance acceptance of the combined checkout | Blocked: expanded grouped Connections regressed in the mixed build; attribution is not isolated |

Final `Verify.ps1 -Smoke full,gauges,compact,capture` passed all 11 steps (`artifacts/peak-complete-verify.log`): app/benchmark builds, five test projects and all four modes. It produced 219, 51, 103 and 16 renders under `artifacts/verify/20261107-170644/`. Final `Verify.ps1 -Store` passed all seven steps (`artifacts/peak-complete-store.log`). Relevant native headers at minimum/default widths in Dark/Light, the window-only setting and Full/Compact tooltip renders were reviewed. Earlier focused peak, preference and localization tests also passed 184/184 in both variants.

Intermediate runs encountered concurrent per-process network test failures and broad-smoke timeouts under load; both cleared in final verification. The [performance history](../PERFORMANCE-HISTORY.md) records the four-pair comparison and its mixed-checkout limits, including the consistently slower, higher-allocation grouped Connections scenario. That other feature was left untouched; this result does not clear a combined release performance gate. Mixed-DPI and screen-reader acceptance remain manual.

Before committing, the peak-only index was exported separately from the concurrent network work. Its normal verification passed all three test projects (1,019 tests), the benchmark build and the gauges/Compact/Settings smoke modes. Store verification passed all 1,021 tests. Logs are `artifacts/peak-commit/isolated-verify.log` and `isolated-store.log`. An additional isolated performance follow-up was not started because an unrelated benchmark was active; the recorded mixed-build performance limits still apply.

## Landed and departures

`MainWindow.Gauges.cs` retains peak samples and passes normalized levels to the existing readouts. Hidden/minimized refreshes release that cache so it cannot retain process frames after history evicts them. `MetricDetails` adds a peak section to the existing XAML tooltip, avoiding another tooltip surface or keyboard stop. Native values use existing unit formatters. Settings retain the existing save/apply flow.

The peak-window setting lives under Monitoring rather than Appearance, keeping the existing appearance preview visible without scrolling. The final Network corner shows a download peak and icon, following the requested refinement; the automatic scale moved into the tooltip. No independent native-value peak, chart navigation, manual reset or session-long peak was added.
