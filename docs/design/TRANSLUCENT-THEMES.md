# Translucent appearance themes

Selected on 2026-10-25 from three WinMon mockups: Frosted porcelain, Smoked sapphire and Glass rim.

## Goal

Give the window a glass treatment without changing dashboard layout, resource colors, gauge styles or chart behavior. Preserve readable chart and process data and the native Windows caption buttons.

## Finishes

| Theme | System backdrop | Tint | Data plot |
| --- | --- | --- | --- |
| Frosted porcelain | Acrylic | Pale blue-white | Opaque white |
| Smoked sapphire | Acrylic | Deep navy blue | Opaque navy |
| Glass rim | Mica | Charcoal and cool gray | Opaque charcoal |

The app requests the documented DWM system backdrop on Windows 11 build 22621 or later. WPF's glass frame spans the window. Frosted porcelain and Smoked sapphire expose the backdrop through tinted panel, title and section brushes. Glass rim keeps its main panel opaque and exposes the backdrop through the title bar; its section headers use a translucent tint over that panel. The main chart plot, search surface and controls remain opaque. The Windows caption-button area keeps its native glyphs and a solid matching caption tint.

If the DWM request fails, the system lacks support, or Windows high contrast is active, the colors fall back to opaque WPF surfaces. Settings previews show the tint and the chosen palette, but the desktop backdrop itself appears only in the running window. The `gauges` smoke captures both WPF-only renders and desktop-composed window screenshots for visual checks; `backdrops.json` records whether DWM accepted each request.

The chart palette still supplies all six resource colors. These themes affect only window surfaces and UI accents, not sampling or history.

## Landed

- Three persisted identifiers and translated labels in Appearance.
- DWM Acrylic or Mica setup in `WindowTheme`, with full glass frame only for active backdrops.
- Theme opacity in the shared finish model, Settings preview, full and compact views, and high-contrast fallback.
- Preference round trips, preview checks, and desktop-composed smoke renders.
- Selector labels were later shortened to Frost, Sapphire and Glass; stored identifiers stayed the same.

## Departures from the mockups

The mockups used a specific wallpaper, rounded outer edge and stronger translucence behind some data. WinMon keeps the system's window frame and uses the user's actual desktop backdrop. Data surfaces remain more opaque for legibility.

## Platform references

- [DWM system backdrop types](https://learn.microsoft.com/windows/win32/api/dwmapi/ne-dwmapi-dwm_systembackdrop_type)
- [WPF WindowChrome glass frame](https://learn.microsoft.com/dotnet/api/system.windows.shell.windowchrome?view=windowsdesktop-10.0)
