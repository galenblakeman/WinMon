# Metal theme prototypes

Selected for prototyping on 2026-10-25 from four WinMon mockups: brushed steel, copper, gunmetal, and brushed nickel. The two supplied references were a copper linear meter and a circular brushed-metal speedometer.

## Goal

Offer alternate material finishes without moving controls or changing monitoring behavior. Theme selection stays independent of chart palette and the seven resource gauge styles. High-contrast Windows mode continues to override theme colors.

## Finishes

| Theme | Surface | Detail |
| --- | --- | --- |
| Brushed steel | Cool medium silver | Satin chrome headers, dark chart inset |
| Copper | Warm bronze | Copper headers, charcoal plot and table |
| Gunmetal | Dark graphite | Smoked titanium headers, restrained highlights |
| Brushed nickel | Pale cool silver | Soft satin headers, deep slate plot |

The satin effect is a lightweight vertical gradient on the existing title and section-header brushes. All other surfaces remain solid so text, charts, and tables stay readable. Native Windows caption color uses the gradient's middle color.

## Landed

- Four new stored appearance identifiers, localized selector labels, and color resources in `MetalThemes`.
- Existing chart palettes and gauge styles still determine the resource colors and shapes.
- The `gauges` smoke captures a full-size resource view in each finish and checks that readout height stays fixed.
- Preference tests check theme persistence without changing the selected gauge or chart palette.

## Departures from the mockups

The mockups showed extra dials and meter tracks. Those would change layout and gauge behavior, so this prototype retains WinMon's existing gauge-style setting and applies only color and surface treatment. The mockups' textured table backgrounds are represented with solid surfaces for text clarity and low rendering cost.
