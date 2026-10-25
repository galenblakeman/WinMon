# Wide resource gauges

The selected visual direction was a wide vertical rail at the right of each Full-view resource readout. A resource-colored fill shows the current value, and a dark horizontal line shows the highest value in the preceding two minutes. The numeric reading remains primary. The rail fits beside the existing label and detail without changing the top strip's height.

The [836-DIP six-resource capture](../example/example_narrow.png) shows the narrower readout type used at the minimum window width.

CPU, memory, disk and GPU rails use a 0–100% scale. The Network rail follows the displayed download rate and uses its own two-minute auto scale, matching the chart while the chart is in two-minute mode. The peak window uses elapsed sample time, skips gaps and ignores other disk volumes. Missing current readings hide the rail, even when older samples have a value. The top readouts and rails stay live during historical chart inspection. The legend explains the dark line in all supported languages.

## Landed

`MetricReadout` hosts a vertical `MicroGauge.Wpf` 1.0.1.1 fill and a thin WPF peak marker. `MainWindow.Gauges.cs` derives live and peak levels from `MonitorSession` history. The package version preserves WinMon's current Windows target. The selected mockup placed the peak marker inside the gauge; the implementation draws it as a WPF overlay because the compatible package's marker did not render consistently for the network's large numeric scale. The gauge uses a normalized 0–100 drawing value while retaining the real scale in the calculation.
