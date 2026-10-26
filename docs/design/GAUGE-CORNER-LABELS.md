# Gauge corner labels

## Intent

The network arc already prints its changing scale above the gauge. Give the other arc gauges useful context in the same position without changing their live reading or 98-DIP height.

## Decisions

- CPU, each configured disk and GPU show their highest percentage from the same two-minute samples used by the dark peak mark. The header uses a short localized `Peak` label and rounds to a whole percent.
- Memory shows available physical memory in GiB, calculated as total minus used from the current sample so it agrees with the `used / total` detail line.
- Network keeps its two-minute auto-scale label. Bars and the None style do not have a header corner label.
- At narrow tile widths, the corner shows only the number (`78%` or `31G`) to preserve the resource name. The full localized label remains in a tooltip and accessibility name. A missing current reading hides the corresponding corner label as well as the gauge. Compact view receives the same values as Full view.

## Landed

`MainWindow.Gauges.cs` supplies the five new labels from existing samples. `MetricReadout` places them in the existing arc header slot. The gauge smoke checks their values and visibility at 1000 and 836 DIPs in each style and theme; resource strings cover all supported languages.

## Departures

None.
