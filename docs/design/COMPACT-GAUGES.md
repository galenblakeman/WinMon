# Compact gauge view

Selected on 2026-10-25: keep Compact view's title bar and show only the configured Full-view resource gauge cluster beneath it.

## Behavior

- CPU, memory, primary disk, optional secondary disk, network and GPU retain their Full-view order, colors, labels, live values, detail lines, selected gauge style and two-minute peak marks. `None` shows the same plain readouts as Full view.
- The secondary disk appears when a drive is configured. Chart-series visibility does not hide readouts in either view.
- Compact resource names are plain labels. Full-view checkboxes continue to control chart series, and their settings are unchanged by switching views.
- The title bar retains Live or Unavailable status, Full view, Always on top and window controls. The resource boxes remain keyboard focusable and open the shared detail tooltips.
- Compact height is 136 DIPs, including the 36-DIP title row. The Full-view dimensions are restored on return.

## Landed

`MainWindow.Compact.cs` creates compact `MetricReadout` controls and mirrors the Full-view readings. `MainWindow.Gauges.cs` updates both sets from the same current values and peak calculation while Compact view is active. `MetricReadout.ShowSeriesToggle` presents a plain resource name in the chartless view. Compact diagnostics cover styles, themes, the optional disk, unavailable readings and size restoration.

## Departures

The compact gauges are separate controls fed from the Full-view readouts because a WPF element can have only one visual parent. They share the detail tooltip models and gauge calculations rather than moving the Full-view controls.
