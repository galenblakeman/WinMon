# Palette theme variants

Reviewed on 2026-10-25 for chart palettes that use one set of colors in every appearance.

`ChartPalettes.ChartColorsForTheme` changes a palette color's lightness until a chart trace reaches 3.5:1 against the plot. Most single-set palettes needed only small changes, but some lost their character or collided once adjusted:

- Sophisticated Pastel was darkened on every color in light appearances, so it no longer read as pastel.
- Signal's sky-blue GPU darkened to sit beside its blue memory color (CIE76 ΔE 12, down from 26).
- Cyberpunk / Neon, Neon and Arcade turned their yellows olive and their limes khaki in light appearances.
- Spectrum's deepest purple brightened to vivid magenta on dark plots, and its lime and yellow turned olive on light plots.
- High-Contrast & Inclusive lifted its pure blue on dark plots and darkened its sky blue heavily on light plots.
- Vibrant Monochromatic Shift moved two colors in each mode; its disk and GPU colors came within ΔE 16 on light plots.
- Warm Earth Tones' second disk brightened to ΔE 7.5 from its primary disk on dark plots.

The Candy appearance also had no palette of its own.

## Design

Affected palettes get a hand-tuned set for the mode that shifted, chosen to clear 3.5:1 on every plot in that mode, so contrast correction leaves chart traces as designed. The other mode keeps its original colors. Each set keeps the palette's resource order and hue families: yellows become golds rather than olives, limes become greens, and colliding pairs are separated in lightness. Gauge readouts still apply the 4.75:1 text threshold on top.

Candy is a new palette with sweet-shop names: sour apple, blue raspberry, tangerine, bubblegum, spearmint and butterscotch. Its light set clears 4.75:1 against Candy's cream surface, so readouts keep the designed colors as well. Its dark set uses brighter versions of the same hues.

## Landed

- `ChartPalettes.Colors` has separate dark and light sets for High-Contrast & Inclusive, Sophisticated Pastel, Vibrant Monochromatic Shift, Cyberpunk / Neon, Warm Earth Tones, Signal, Spectrum, Neon, Arcade and the new Candy palette.
- Candy's label and description are in all ten resource files. Its translated name matches the Candy appearance in each language.
- `ChartPaletteTests` checks that every hand-tuned set's chart colors are unchanged on every plot in its mode, and pins the Neon, Arcade, Tropical and Candy colors in each mode.

## Departures from the design

- Six drafted colors fell just short of 3.5:1 on Candy's slightly warm plot and were darkened by at most 0.12:1 before landing.
- The sets kept from before still shift in places. Cyberpunk / Neon's violet, Neon's violet and Signal's blue move slightly on some dark finishes. Warm Earth Tones' amber and tan still darken in light appearances. None of these collide.
- Sophisticated Pastel remains hard to separate under simulated deuteranopia, as it was before. The Colorblind palette remains the choice for that.
