# Wildberry and Candy themes

Selected on 2026-10-25 from WinMon mockups: Bramble for Wildberry and the revised bold Candy concept.

## Goal

Add two recognizable appearances without changing monitoring, chart palette colors, gauge styles or dashboard layout. Keep each selector label to one word and make the theme list easy to scan.

## Finishes

| Theme | Surface | Chrome | Accent |
| --- | --- | --- | --- |
| Wildberry | Matte deep blackberry | Plum | Raspberry |
| Candy | Warm cream | Vivid pink title and blush section headers | Raspberry with mint selected segments |

Candy uses white title text over its saturated title bar while keeping dark plum text on the light data surfaces. The chart palette still owns gauge fills and chart traces. Both themes use opaque surfaces and continue to yield to Windows high contrast colors.

The Settings selector lists System, Light and Dark first, then sorts custom themes by their translated labels using the current culture. Stored theme identifiers do not depend on list position or translation.

## Landed

- `ThemeFinishes` defines Wildberry and Candy; Settings and `PreferenceStore` accept and persist both.
- Localized one-word labels cover all ten resource files. A title-only text brush keeps Candy's main and native caption controls legible.
- Preference, localization, preview and selector-order tests cover the new choices. Full and compact smoke captures cover their rendered surfaces.

## Departures from the mockups

The mockups showed saturated fills and decorative edge detail beyond WinMon's current controls. The implementation uses the existing WPF layout and brush resources, with bright color concentrated in Candy's title and headers and opaque plots for readability.
