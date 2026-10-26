# Chart toolbar controls

## Intent

Make the chart toolbar easier to scan while keeping Live/Capture centered.

## Behavior

- Remove the chart toolbar's "2m peak" legend and its tooltip. The dark gauge marks and their two-minute meaning stay available in the Resource gauges setting.
- Place Auto immediately to the right of Capture in the middle of the toolbar, separated by 8 DIPs. The Live/Capture switch stays centered. The button keeps its count, menu, tooltip and accessible status.
- Narrow the chart type dropdown from 140 to 120 DIPs. Its tooltip retains the full selected name when the visible text is shortened.

## Landed result and departures

Implemented in `MainWindow.xaml` and verified by capture, chart and gauge smoke. Auto originally sat beside the chart type dropdown; it moved after Capture at the user's request.
