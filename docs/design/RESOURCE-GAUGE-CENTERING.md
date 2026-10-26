# Resource gauge centering

## Intent

Keep each full-view bowl and its value centered in its resource column, including GPU beside the window edge.

## Behavior

The full-view tiles use equal left and right insets. The bowls and values retain their available width while sharing the horizontal center of the column.

## Landed result and departures

Implemented in `MainWindow.xaml`. Gauge smoke checks each bowl and value against its column center at 836 and 1000 DIPs. The result follows the intended behavior.
