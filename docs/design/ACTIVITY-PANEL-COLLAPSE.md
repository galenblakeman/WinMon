# Collapsible activity panel

Requested on November 7, 2026. Keep the Processes / Services / Usage tab strip visible while allowing the chart to use the space occupied by the panel's filters, table and action bar.

## Behavior

- The tab strip ends with a down chevron. It collapses all activity content and the chart/panel resize divider, leaving the tabs, counts and an up chevron at the bottom.
- The window keeps its dimensions; the chart fills the recovered space. The layout switches without animation.
- The up chevron or any activity tab, including the selected tab, expands the panel. Ctrl+F expands it before focusing the selected tab's search. Existing view instances retain filters, search, selection and scroll position.
- A saved star-size ratio restores the previous chart/panel split. At unchanged window dimensions the panel returns to its previous height; after resizing it keeps its share of available space, constrained by the existing 160-DIP chart and 344-DIP panel minimums.
- Collapsed state and the last expanded split persist through the existing preference store. Compact view does not replace them. Old settings default to the expanded panel and the existing 1:1.25 chart/panel split.
- Collapsing moves keyboard focus out of hidden content or the divider to the chevron. Its native button behavior, themed focus outline, localized name and UI Automation ExpandCollapse provider support keyboard and assistive-technology operation.
- Sampling, Usage accumulation, captures and counts continue while the body is hidden.

## Scope and requirements

Implementation lives in the main-window layout and activity-panel partial, `MonitorPreferences` / `PreferenceStore`, and a small button automation peer. The monitoring projects and Settings UI require no changes. Reuse the action-drawer chevron styling and existing icons; override its drawer command for this separate control. New tooltips are localized in all nine translations.

The automation peer exposes the [.NET 10 WPF `IExpandCollapseProvider` contract](https://learn.microsoft.com/dotnet/api/system.windows.automation.provider.iexpandcollapseprovider?view=windowsdesktop-10.0) alongside ordinary button invocation.

| Requirement | Status |
| --- | --- |
| Collapse body and divider, retain tabs/counts, grow chart | Verified |
| Restore saved split and preserve existing view instances | Verified |
| Persist state and ratio with legacy defaults and validation | Verified |
| Expand from every tab and preserve state through Compact view | Verified |
| Focus recovery and accessible disclosure state | Verified |
| Builds, regression tests and live renders | Verified |
| Performance comparison | Verified |

## Verification

- Standard `Verify.ps1 -Smoke feature,compact,search`: both builds, 947 tests and all three smoke modes passed on the final build. An earlier full check also passed the snapshot and Usage smoke modes; the final follow-ups only add persistence on entry to Compact view and reopening for Ctrl+F.
- `Verify.ps1 -Store`: both builds and 949 tests passed on the final build.
- The eight new NUnit cases cover legacy preferences, both collapsed states, ratio round-trips and invalid ratios, plus accessible state, idempotent Expand/Collapse and disabled controls.
- The feature smoke drags the real divider, checks restored height, chart growth, minimum sizes, focus, all three tabs, Compact transitions and a new window loaded from an isolated preference file. Dark, Light/minimum-width and scaled renders were inspected, along with the existing minimum-divider, Compact and Usage renders.
- Verification ran outside the sandbox after the baseline test host failed to connect inside it. Baseline builds and all 939 baseline tests passed outside it before implementation.
- Evidence: `artifacts/collapse-complete-verify.log`, `artifacts/collapse-complete-store-verify.log`, `artifacts/verify/20261107-095746/feature/activity-panel-*`, and `artifacts/verify/20261107-095306/{snapshot,usage}`.
- Four alternating before/after benchmark pairs completed against saved artifacts. See [the performance record](../PERFORMANCE-HISTORY.md#collapsible-activity-panel-november-7-2026) for exact medians, allocation, timing variation and scope. Allocations were effectively unchanged; Live and Focus aggregate times increased about 0.15 ms, within observed run-to-run variation.

## Landed and departures

The code follows the selected two-state mockup. The mockup's green circles and callout lines are presentation annotations and are omitted. The saved size is a split ratio rather than a fixed pixel height, so it adapts to resized windows and display scaling. Screen-reader disclosure behavior has automated provider coverage; a manual screen-reader session was not performed.
