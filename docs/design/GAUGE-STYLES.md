# Gauge styles

Selected on 2026-10-25: make the Full-view resource gauge a user setting, with a nearly round bowl as the default.

## Why

The [wide resource rails](WIDE-RESOURCE-GAUGES.md) sat flush against the next tile's checkbox, so a rail read as part of its neighbour, and at low values they showed as empty boxes. Mockups of seven bar and arc styles, and then five half-radial concepts, showed that no one style suits every taste. Some styles are better for glancing, others for precise readings.

## Choices

**Resource gauges** in Settings → General → Appearance offers, in this order:

| Style | Drawing | Peak mark |
| --- | --- | --- |
| Bowl (default) | A nearly round half arc, as tall as the name and detail rows allow, with the value centred inside and the detail below | Dark radial tick |
| Needle | The largest half circle the tile's height allows, with a colored fill and needle; the value is centred under the hub | Dark radial tick |
| Segmented arc | The bowl's arc in ten segments, with the value inside and the detail below | Dark radial tick |
| Left bar | The former 22-DIP rail, moved to the left of the text so it belongs to its own tile | Dark horizontal line |
| Bottom bar | A rounded bar under the detail line | Dark vertical tick |
| Segmented bar | Ten blocks under the detail line | Dark vertical tick |
| None | No gauge; the peak legend is hidden too | — |

Settings saved before this choice existed load as Bowl, so everyone sees the new default after updating. The style is saved by name, so the list order can change without changing anyone's choice.

## Decisions

- **One height.** Every style fits 92 DIPs, so switching styles never moves the chart. That is 7 DIPs taller than the rails' natural height; bottom bars and the bowl needed it.
- **Nearly round arcs.** Height, not width, sets the arcs' size.
  - **Bowl and segmented arc.** These keep the value inside the arc and the detail line below it. A perfect circle that tall is too narrow inside for "↓9.52 Mbps", so these arcs may be at most 1.2 times as wide as they are tall (`MetricGauge.BowlWidening`). A test checks that the widest rate's digits clear the inside of the arc.
  - **Needle.** The needle is a true half circle with the value under its hub, close enough to clear a needle lying flat at zero. Its detail line (clock speed, memory in use, upload rate and so on) moves to the tile's tooltip, which already shows it. That gives a radius of about 39 DIPs.
  - **Font.** All three use the smaller value font at every width.
- **The peak is always a dark line**, so the existing "2m peak" legend stays true for every style.
- **Network scale on arcs.** A dial's angle reads as absolute, so arc styles print the top of the network's two-minute auto scale in a compact form ("100M") at the right of the resource name. Bars don't print it, as before.
- **Narrow tiles.** At the 836-DIP minimum width, six tiles are about 120 DIPs wide. The widest rate, "↓9.52 Mbps", needs 89 DIPs at the smaller font. Every style except the left bar gives the value the tile's full width. Values elsewhere take the smaller font when their column is under 118 DIPs.
- **Drawn, not hosted.** `MetricGauge` replaces the `MicroGauge.Wpf` control and package. It keeps its marks in a retained `DrawingGroup` that it reopens only when a mark moves by at least half a pixel. Pens, the track and the arc segments are frozen and rebuilt only when the style, size or a brush changes.
- **Preview.** Settings shows a Memory tile in the selected style. Its host hides it from assistive technology, since it is an illustration and its checkbox does nothing.

## Landed

- `Preferences/GaugeStyle.cs`, `MonitorPreferences.GaugeStyle` (saved by name), validation in `PreferenceStore`, and the selector and preview in `SettingsWindow`.
- `Controls/MetricGauge.cs` draws each style, and `ArcBounds` sizes the arcs. `Controls/MetricReadout.cs` places the labels and the gauge in one of five layouts: bowl (also the segmented arc), dial (needle), beside (left bar), under (bars) or none. `ShowGauge` takes an optional scale label.
- `MainWindow.ApplyGaugeStyle` runs with `ApplyTheme`, and hides the `PeakLegend` for None.
- `WinMon.exe --gauges-smoke-output <dir>` (`Verify.ps1 -Smoke gauges`) renders every style in both themes at 1000 and 836 DIPs. It checks:
  - the shared height;
  - that the arcs are round or nearly round;
  - that values aren't clipped;
  - the network scale and the legend;
  - unavailable readings;
  - the saved preference and the Settings preview.

## Departures

- The first build made the needle the default and put a 48-DIP arc beside the text, stacked above the value in narrow tiles. Those arcs looked small. The needle became the speedometer layout above, and the bowl became the default.
- The bowl first flattened to fill the tile's width, about 1.6 times as wide as tall. It was made nearly round to match the needle.
- For a while the segmented arc shared the needle's layout, with its detail in the tooltip. It now shares the bowl's layout instead, so it keeps its detail line.
- The mockups' scale label read "100 Mbps", which was wider than a small arc and overlapped the bowl's resource name. It now reads "100M".
- The empty part of a gauge first used the `Line` brush. High contrast sets `Line` and every resource color to WindowText, so the bowl, segmented arc and both bars drew full whatever the reading. The track now has its own `GaugeTrack` brush: the same color as `Line` in the normal themes, and GrayText in high contrast.
