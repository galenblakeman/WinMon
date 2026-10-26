# Chart toolbar controls

## Intent

Make the chart toolbar easier to scan and give the chart type and automatic-capture controls a clear relationship.

## Behavior

- Remove the chart toolbar's "2m peak" legend and its tooltip. The dark gauge marks and their two-minute meaning stay available in the Resource gauges setting.
- Place Auto immediately to the right of the chart type dropdown, separated by 6 DIPs. The button keeps its count, menu, tooltip and accessible status.
- Narrow the chart type dropdown from 140 to 120 DIPs. Its tooltip retains the full selected name when the visible text is shortened.

## Landed result and departures

Implemented in `MainWindow.xaml` and verified by capture, chart and gauge smoke. The result follows the intent above.
