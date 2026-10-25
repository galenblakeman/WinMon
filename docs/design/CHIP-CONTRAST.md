# Theme chip contrast

Reviewed on 2026-10-25 for the shared `RangeChip` style used by chart time ranges and the Individual/Grouped process switch.

The original style drew unselected text with each theme's muted brush on its header, and selected text with the row-selection brush on its selected chip fill. Calculated contrast was below 4.5:1 for unselected Candy, Copper, Crystal and Slate; selected Candy, Copper, Porcelain and Frost also fell below 4.5:1. Candy, Porcelain and Frost selected text was near 1.3:1.

## Landed

- Unselected chips use the theme's body text color on its header. Selected chips use body text on light finishes and selected text on dark finishes; Copper uses white on its existing brown fill. The selected outline uses that same contrasting color because several selected fills are close to their surrounding header. The same brushes serve both chip groups.
- Windows high contrast overrides the chip colors with system window and highlight text colors.
- `ThemeChipContrastTests` checks a minimum 4.5:1 text contrast against each custom finish's header and selected chip fill, plus 3:1 for the selected outline against the header. Gauge and feature smoke renders cover the full dashboard.

## Departure from the original style

Unselected chip text is stronger than the former muted text. The transparent versus filled chip surfaces still distinguish the two states without relying on faint text.
