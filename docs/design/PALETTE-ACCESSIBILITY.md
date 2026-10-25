# Palette accessibility and pairing

Reviewed on 2026-10-25, after [palette theme variants](PALETTE-THEME-VARIANTS.md). Five follow-ups were proposed:

1. Draw the second disk as a dashed line, because primary and second disk were the closest pair in several palettes.
2. Test that the palettes promising accessibility keep resources apart under color-vision deficiency.
3. Turn yellows toward amber when contrast correction darkens them, so they read as gold instead of olive.
4. Resolve Cyberpunk / Neon and Neon, which used the same six hue families on different resources.
5. Pair palettes with appearances.

## Findings

- The overlay chart already drew the second disk dashed, since `efd6121`, and the Colorblind palette already gives every resource its own dash pattern. Item 1 needed no change.
- Simulated with the Machado, Oliveira and Fernandes (2009) full-severity matrices, Colorblind's light set was the weakest of the three accessibility palettes. Memory and disk came within CIE76 ΔE 2.9 under deuteranopia, and memory and second disk within 2.7 under protanopia. Its dark set reached 8.5. High-Contrast & Inclusive reached 8.3 (deuteranopia) and 4.7 (protanopia) on light themes and 13.0 on dark themes. Signal already cleared 17.7 and 20.1 in both modes.

## Design

- Colorblind, Signal and High-Contrast & Inclusive must keep every pair of chart colors at least ΔE 15 apart under deuteranopia and protanopia, and 10 under tritanopia, on every theme. The retuned sets make the smallest moves that clear those thresholds with margin, keep each hue within 20° of the original, and still clear 3.5:1 on every plot in their mode.
- Contrast correction rotates hues between 39° and 66° toward 39° as it darkens them in light appearances. The rotation is complete once lightness drops by 0.25. Limes stay outside that range, so they still darken to green.
- Cyberpunk / Neon is merged into Neon. Saved settings load as Neon, as Glass settings now load as Crystal.
- Choosing Candy or Wildberry in Settings preselects the matching palette before Save. Opening Settings keeps the saved palette, and other appearances leave the palette alone. Wildberry gets a new palette named after the appearance, in each language: gooseberry, blueberry, cloudberry, raspberry, blackcurrant and a golden second disk.

## Landed

- `ChartPaletteTests.AccessiblePalette_KeepsResourcesApartUnderColorVisionDeficiency` applies the thresholds to the chart colors of all three palettes on every theme. Colorblind's light set and parts of its dark set, and High-Contrast & Inclusive in both modes, were retuned to pass.
- `EnsureContrast` applies the amber rotation. Neon yellow on a white plot now becomes `#AE8200` instead of `#998A00`, and Studio's gold becomes `#B57E12` instead of `#A68510`. Colors that were already amber, such as Tropical's, barely move.
- `ChartPalettes.Normalize` maps Cyberpunk / Neon to Neon. Its label was removed from all ten resource files.
- `ChartPalettes.ForTheme` and the theme handler in `SettingsWindow` provide the preselection. `SettingsLayoutTests` covers opening, switching to each paired theme, and switching to an unpaired theme.
- Nothing here changes per-tick drawing. The amber rotation runs only when a theme or palette is applied, so no benchmark was needed.

## Departures from the proposal

- Item 1 was already implemented.
- The pairing preselects the palette instead of adding a "Match appearance" palette choice, so the saved palette stays explicit.
