# Flat appearance themes

Selected on 2026-10-25 from three WinMon dashboard mockups: Porcelain, Midnight Ink and Evergreen Instrument.

## Goal

Offer three distinct window appearances without changing dashboard layout, gauge styles or chart palettes. Each uses flat, solid surfaces; Windows high contrast continues to override theme colors.

## Finishes

| Theme | Surface | Plot | UI accent |
| --- | --- | --- | --- |
| Porcelain | Warm off-white `#F5F2EB` | Soft white `#FFFEFA` | Indigo blue `#365F85` |
| Midnight Ink | Dark navy `#101A28` | Deep navy `#0D1724` | Cobalt `#477DAE` |
| Evergreen Instrument | Forest green `#172522` | Dark evergreen `#111E1B` | Sage `#87B782` |

Resource readouts, chart traces and process sparklines continue to use the separately selected chart palette. The UI accent affects controls and selection affordances.

## Landed

- Added three stored theme identifiers and translated labels in the Appearance selector.
- Added colors to the shared theme finish model without applying the metal grain to flat finishes.
- Included all three in full and compact smoke renders and the isolated Settings preview.
- Preference and preview tests check persistence, surfaces and palette independence.
- Selector labels were later shortened to Porcelain, Midnight and Evergreen; stored identifiers stayed the same.

## Departures from the mockups

The generated mockups suggest some raised panels, shadows and icon changes. The implementation retains WinMon's existing WPF layout and control styling; only theme resources change.
