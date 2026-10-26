# Theme tab and chart readability

Reviewed on 2026-10-25 after comparing the current Light, Dark and named-finish smoke renders.

## Design

Keep the selected activity tab's underline and semibold label, while giving inactive tabs their own theme brush. Its text should clear the section header at 4.5:1 or better; for brushed finishes, check the brightest part of the reflection as well. Keep other muted labels and the resource palette unchanged.

Raise chart axis, network-scale and selected-sample labels from 11 to 12 DIPs. Use the same size when reserving width for the alternate chart views.

## Landed

- `InactiveTabText` uses stronger text in Candy, Copper, Crystal and Slate. Other finishes retain their muted text; Windows high contrast uses system window text.
- `ThemeChipContrastTests` checks inactive-tab contrast against each named finish's header and the reflected headers of brushed finishes.
- `CpuGraph` uses one 12-DIP label size for shaping and width measurements.
- Focused tests and the relevant smoke renders verify the change.

## Departure from the design

The stronger tab text was needed in Candy and Crystal as well as the initially identified Copper and Slate finishes. No other departure.
