# Theme palette pairing

Reviewed on 2026-10-25, after [Copper and Gunmetal palettes](COPPER-GUNMETAL-PALETTES.md). Only Candy, Wildberry, Copper and Gunmetal had a paired palette, and Settings switched to it on every change to those themes.

## Proposal and review

The proposed mapping paired every custom theme:

| Theme | Proposed | Review |
| --- | --- | --- |
| Candy, Copper, Gunmetal, Wildberry | Own palette | Already paired |
| Crystal | Cool Tech | Suits the icy theme. Readouts need brightening by 0.19, and its slate blue sits near Crystal's blues (ΔE 17.7) |
| Evergreen | Warm Earth Tones | Its dark CPU green `#7FB27E` was ΔE 2.3 from Evergreen's accent green, so the reading looked like chrome |
| Frost | High-Contrast & Inclusive | Best measured fit |
| Midnight | Neon | Striking on navy. Disk and second disk come within ΔE 3.7 under deuteranopia |
| Porcelain | Warm Earth Tones | The light set was never hand-tuned. It needed heavy correction (0.26 readouts, 0.16 traces), and its closest pair fell to ΔE 12.2, or 3.7 under deuteranopia |
| Prism | Classic | Ranked last for Prism. Classic's CPU green and second-disk yellow are ΔE 0.7 apart under deuteranopia, and its purple sits near Prism's violet accent |
| Sapphire | Tropical | Best measured fit |
| Slate | Ocean | No correction needed. Network and GPU come within ΔE 5.3 under deuteranopia |

Pairing every theme also meant every theme change would replace the palette, including one chosen for accessibility.

## Design

- Prism pairs with Arcade instead of Classic. The other proposed pairs stay. System, Light and Dark pair with Classic.
- Warm Earth Tones gets a hand-tuned light set of sage, ochre, terracotta, plum, walnut and olive. Its dark CPU green moves to `#60CA81`, clear of Evergreen's accents.
- When the theme changes in Settings, the palette follows only if it is still Classic or the previous theme's pair. Any other palette was picked deliberately and stays.
- A paired palette's readouts must stay at least ΔE 12 from its theme's selected, highlight and accent colors.

## Landed

- `ChartPalettes.ForTheme` returns a palette for every theme. `SettingsWindow` remembers the previous theme and follows the new pair only for Classic or that theme's pair.
- Warm Earth Tones has the new light set and dark CPU green.
- `ChartPaletteTests` adds the accent-distance check for every finish, and Warm Earth Tones' light set joins the hand-tuned list. `SettingsLayoutTests` walks every theme from Classic, checks that a picked palette survives a theme change, and checks that a saved pair follows the next theme.

## Departures from the review

- The accent check also caught two earlier pairings. Candy's bubblegum network color `#C2185B` was ΔE 2.5 from the Candy theme's raspberry accent; it becomes deep berry `#910B43`, which also raises protanopia separation from 3.6 to 12.7. Wildberry's raspberry `#FF6FA8` was ΔE 9.9 from its theme accent; it becomes `#FF5BA0`. Other colors in those two sets moved slightly to keep separation.
- Classic counts as unchosen, so someone who deliberately picked Classic will see it follow the theme. Telling the two apart would need a stored flag; this keeps preferences unchanged.
- Midnight keeps Neon and Slate keeps Ocean despite weaker red-green separation. The Colorblind palette remains the choice for that, and choosing it stops the palette following the theme.
