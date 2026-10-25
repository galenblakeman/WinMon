# Action drawer

Selected on 2026-10-25: tie hiding the action bar to the bar itself, instead of a **Hide actions** button in the tab strip.

## Problem

The toggle sat at the right end of the Processes, Services and Usage tab strip, about 330 DIPs above the bar it hid. Its up chevron pointed away from the direction the bar went, "actions" did not name the bar, and once the bar was hidden nothing at the bottom showed it could come back.

## Behavior

- A down chevron, after a divider at the end of the bar's buttons, slides the bar down and out of view.
- Hidden, the bar leaves a strip 26 DIPs high with the accent stripe, the selection's name and detail, and **Show actions** with an up chevron, which slides the bar back.
- When the chevron or **Show actions** had keyboard focus, focus moves to the control that replaces it: **Show actions**, or the bar's first button.
- The slide takes 140 ms and the strip's rise 100 ms. Only render transforms move, so the activity table lays out once, when the bar and strip swap. When Windows animations are off, the bar switches without sliding.
- One preference (`ProcessActionsHidden`) still covers all three tabs, and WinMon remembers it.

## Landed

`Controls/ActionDrawer.cs` wraps each tab's existing bar: `ProcessActionBar` in `ProcessesView`, and the `ActionBar` borders in `ServicesView` and `UsageView`. It owns the strip and the animation, and raises `CollapsedChanged` when the user moves it. Each bar ends with a button in the `ActionBarCollapseButton` style (`App.xaml`), which sends `ActionDrawer.CollapseCommand` up to the drawer, so a bar needs no reference to it. Each view relays the change as `ActionsHiddenChanged`, and `MainWindow` stores the preference and applies it to the other tabs without animating. Views call `Summarize` with the selection text they already show. The tab-strip toggle is gone; the chevron and strip reuse the existing **Hide actions** and **Show actions** strings, so no translations changed.

`ActionDrawerTests` covers collapsing, restoring, the strip's text and the change events. The feature smoke uses the chevron and **Show actions** on the live window, checks that all three tabs follow, and renders `action-drawer-open.png` and `action-drawer-collapsed-{dark,light}.png`.

## Departures

- The design paired the chevron with a pill grip on the bar's top edge that could also be dragged. The grip was built and then removed: two controls for one action made the bar busier without adding anything the chevron and **Show actions** did not already do.
- Collapsed, the drawer keeps 32 DIPs (the strip and the gap above it) where the old toggle reclaimed the bar's whole 60. That is the cost of keeping the selection and the way back visible.
