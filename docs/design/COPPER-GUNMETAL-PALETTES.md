# Copper and Gunmetal palettes

Reviewed on 2026-10-25, after [palette accessibility and pairing](PALETTE-ACCESSIBILITY.md).

## Findings

- **Copper:** gauge readouts sit on a brown panel (`#574033`) that is light for a dark theme. To reach the 4.75:1 readout threshold, most palettes had to be brightened a lot; Classic moved by up to 0.23 in lightness. Blues and purples washed out to pale lavender (Classic's memory became `#90B7FF` and its network `#CEA9E5`). Orange and gold readings sat close to Copper's peach highlight and gold selection, so they read as theme chrome rather than data. Colorblind fitted best, moving by at most 0.12 with ΔE 12.2 under deuteranopia, but still had two orange-gold readings.
- **Gunmetal:** the dark, neutral steel panel needed almost no correction, and no palette clashed with its blue-grey chrome. High-Contrast & Inclusive fitted best: it moved by 0.02 at most, kept vivid colors, and already passed the color-vision test.
- Darkening Copper's panel to `#3F2F26` would cut Classic's worst readout shift only to 0.15 and change the theme's look, so the fix belongs in a palette.

## Design

- **Copper palette:** a patina theme of verdigris, periwinkle, brass, rose, aqua and lavender. It avoids the theme's own copper and gold by at least ΔE 18. Its dark set clears 4.75:1 on Copper's panel without correction. Its rose, lavender and periwinkle are necessarily pastel there: those hues reach text contrast on that surface only at high lightness.
- **Gunmetal palette:** an instrument-panel set of phosphor green, sky blue, amber, magenta, violet and red-orange.
- Both have light sets in the same hue families. Every set clears 3.5:1 on every plot in its mode. Every pair stays at least ΔE 16.5 apart under simulated deuteranopia and protanopia, and 11 under tritanopia, which leaves margin over the test thresholds.
- As with Candy and Wildberry, each palette uses its theme's name in each language, and choosing the theme in Settings preselects it.

## Landed

- `ChartPalettes` adds Copper and Gunmetal dark and light sets, and `ForTheme` pairs them with their themes.
- All ten resource files have the labels and descriptions.
- `ChartPaletteTests` pins both palettes in each mode. It checks that their chart colors stay unchanged on every plot in their mode and that Copper's readouts stay unchanged on Copper's panel. The color-vision test now covers both palettes. `SettingsLayoutTests` covers the preselection for Copper and Gunmetal.

## Departures from the review

- The Copper dark set's colors were nudged slightly from the reviewed drafts to gain margin under color-vision simulation; the largest change is aqua becoming `#97F4F0`.
