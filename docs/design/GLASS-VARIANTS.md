# Glass variants

Selected on 2026-10-25 from three WinMon Glass redesign mockups: Crystal Edge, Frosted Slate and Prismatic Glass.

## Goal

Replace Glass, which read too much like Dark, with three distinct translucent finishes. Keep the dashboard layout, chart palette, resource colors, gauge styles and data readability intact. All selector labels remain one word.

## Finishes

| Selector | Reference | Treatment | Backdrop |
| --- | --- | --- | --- |
| Crystal | Crystal Edge | Brighter blue-silver chrome, cool dividers and a soft header reflection | Mica |
| Slate | Frosted Slate | Neutral slate tint with the most translucent panel and chrome | Acrylic |
| Prism | Prismatic Glass | Dark violet-blue chrome and a restrained colored header reflection | Mica |

The chart plot and search field remain opaque. Panels reveal the system backdrop only when DWM accepts it; otherwise the same palettes render with opaque surfaces. Windows high contrast continues to use system colors. Existing `GlassRim` preferences normalize to `Crystal`.

## Landed

- Three stored identifiers and localized one-word labels replace Glass in Settings and theme resources.
- Crystal and Prism use frozen horizontal header gradients; Slate keeps a neutral flat tint. All three retain native Windows caption buttons.
- Preference, preview and resource tests cover migration, surfaces and translations. Full, compact and desktop-composed smoke captures cover the three finishes.

## Departures from the mockups

The mockups exaggerated bloom, rounded panel borders and backdrop blur. WinMon keeps its existing WPF layout and native window frame, using restrained colors and translucency so charts and process information remain legible on varied desktops.
