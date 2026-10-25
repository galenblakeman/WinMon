# Metal theme prototypes

Selected for prototyping on 2026-10-25 from four WinMon mockups: brushed steel, copper, gunmetal, and brushed nickel. The two supplied references were a copper linear meter and a circular brushed-metal speedometer.

## Goal

Offer alternate material finishes without moving controls or changing monitoring behavior. Theme selection stays independent of chart palette and the seven resource gauge styles. High-contrast Windows mode continues to override theme colors.

## Finishes

| Theme | Surface | Detail |
| --- | --- | --- |
| Brushed steel | Cool medium silver | Satin chrome headers, matte alloy chart plot |
| Copper | Warm bronze | Copper headers, charcoal plot and table |
| Gunmetal | Dark graphite | Smoked titanium headers, restrained highlights |
| Brushed nickel | Pale cool silver | Soft satin headers, pale nickel chart plot |

The satin effect uses fine horizontal grain in frozen drawing brushes on the title and section headers. Section headers keep a broad reflection; the title has a flat base color so its top and bottom edges meet the solid native caption-button area without bright bands. An absolute 16-DIP grain tile covers the full and compact window backgrounds, so its scale does not change with the window size. Brushed steel and brushed nickel use a lighter, subtly grained matte alloy chart plot; the other plots and controls keep their solid surfaces. Native Windows caption color uses the title's base color.

## Landed

- Four new stored appearance identifiers, localized selector labels, and color resources in `ThemeFinishes`.
- Existing chart palettes and gauge styles still determine the resource colors and shapes.
- The `gauges` smoke captures a full-size resource view in each finish and checks that readout height stays fixed.
- The `compact` smoke captures the same finishes at the narrow compact width.
- Preference tests check theme persistence without changing the selected gauge or chart palette.
- A later texture refinement added deterministic hairline grain to the metal chrome and resource backgrounds without adding UI elements or work to the sampling loop.
- The WPF title surface stops at the measured Windows caption-button bounds. DWM colors the remaining area to match the finish and paints its native Minimize, Maximize and Close glyphs there. A full-width WPF underlay hid those glyphs despite preserving their hit targets, so it was removed.
- The title's top and bottom highlight bands were removed so the brushed title surface meets the native button area without an abrupt horizontal border.
- The selected light alloy chart treatment applies only to brushed steel and brushed nickel. Chart traces, grid geometry, palettes, and interaction stay as before.

## Departures from the mockups

The mockups showed extra dials and meter tracks. Those would change layout and gauge behavior, so this prototype retains WinMon's existing gauge-style setting and applies only color and surface treatment. The mockups' textured table backgrounds remain solid for text clarity and low rendering cost.
