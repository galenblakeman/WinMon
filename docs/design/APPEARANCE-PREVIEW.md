# Combined appearance preview

Requested on 2026-10-25 for the Appearance section of Settings.

## Goal

Show the selected theme, chart palette and resource gauge style together before Save. Selector changes update only the sample; cancelling leaves the running dashboard and saved preferences unchanged.

## Design

- A compact, noninteractive dashboard sample sits below the three selectors in the existing Appearance page.
- Six resource tiles use the real `MetricReadout` control and the chosen gauge style. A small chart below them uses the real `CpuGraph` with fixed illustrative samples.
- The sample owns a local WPF resource dictionary. Theme surfaces and chart palette brushes resolve inside it, so the main window keeps its current appearance until Settings is saved. Palette hues adjust for contrast against the selected theme's panel and plot.
- Windows high contrast colors override the sample theme as they do in the main window. Assistive technology skips the decorative sample; the three selectors retain their labels.

## Landed

- `AppearancePreview` contains two rows of six gauge samples and a chart, with no sampling or preference writes.
- Changing any of the three selectors updates the sample immediately. Palette changes also retain the existing palette swatches.
- The `gauges` smoke captures Dark and a Porcelain/Ocean/Needle combination in Settings; layout tests check the unsaved resources and gauge style. The chart palette test covers every palette against every theme panel and plot.

Later refinement: the preview now uses separate chart and readout shades. Chart strokes and Settings swatches keep more of the palette color while gauge text keeps its stronger contrast threshold; chart labels use the text shade.

## Departures

The chart is shorter to keep the six readouts within the existing Settings dialog.
