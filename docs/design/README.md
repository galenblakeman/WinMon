# WinMon design assets

This folder contains the current layout reference, feature design notes and editable application icon sources. Superseded concept images and early CPU-only design records have been removed.

## Current visual reference

[Compact headers](compact-headers-selected.png) is the selected layout reference. It is an illustrative mockup, rather than a screenshot of the running app. Current application screenshots are in [the main README](../../README.md).

## Feature notes

- [Portable process identity](PORTABLE-PROCESS-IDENTITY.md) defines lifetime domains, executable policies and partial observations.

- [Shared monitoring library](SHARED-MONITORING.md) tracks the MonCore extraction for WinMon and future MacMon use.

These records describe the individual feature increments and their validation at the time. Use the [main README](../../README.md) for current user-facing behavior and the [developer guide](../DEVELOPMENT.md) for technical notes.

- [Global show shortcut](GLOBAL-SHORTCUT.md)
- [GPU utilization](GPU-UTILIZATION.md)
- [Multiple disk volumes](MULTIPLE-DISKS.md)
- [Process count selector](PROCESS-COUNT.md)
- [Code structure](STRUCTURE.md)
- [Wide resource gauges](WIDE-RESOURCE-GAUGES.md)
- [Gauge styles](GAUGE-STYLES.md), which replaced the wide rails with a choice of seven styles
- [Gauge corner labels](GAUGE-CORNER-LABELS.md), which show recent peaks above arc gauges
- [Compact gauge view](COMPACT-GAUGES.md)
- [Action drawer](ACTION-DRAWER.md), which moved hiding the action bar onto the bar
- [Activity counts](ACTIVITY-COUNTS.md), which took the old toggle's place in the tab strip
- [Metal theme prototypes](METAL-THEMES.md), which now provide Copper and Gunmetal finishes without changing layout
- [Flat appearance themes](FLAT-THEMES.md), which add Porcelain, Midnight and Evergreen
- [Translucent appearance themes](TRANSLUCENT-THEMES.md), which add Frost and Sapphire
- [Glass variants](GLASS-VARIANTS.md), which replace Glass with Crystal, Slate and Prism
- [Wildberry and Candy themes](WILDBERRY-CANDY-THEMES.md), which add the Bramble and bold Candy concepts
- [Theme chip contrast](CHIP-CONTRAST.md), which covers time-range and process-grouping text across finishes
- [Theme tab and chart readability](THEME-TAB-CHART-READABILITY.md), which improves inactive tab contrast and chart label size
- [Combined appearance preview](APPEARANCE-PREVIEW.md), which shows theme, palette and gauge choices together before Save
- [Palette theme variants](PALETTE-THEME-VARIANTS.md), which give nine palettes hand-tuned light or dark sets and add Candy
- [Palette accessibility and pairing](PALETTE-ACCESSIBILITY.md), which tests color-blind separation, keeps darkened yellows gold, merges the neon palettes and pairs Candy and Wildberry with their palettes
- [Copper and Gunmetal palettes](COPPER-GUNMETAL-PALETTES.md), which add palettes made for the two metal themes
- [Theme palette pairing](THEME-PALETTE-PAIRING.md), which pairs every appearance with a palette and follows it until the user picks their own
- [Store sign-in startup](STORE-STARTUP.md), which starts the Store build at sign-in through its MSIX startup task
- [Startup view and network interface settings](STARTUP-VIEW-AND-NETWORK-INTERFACE.md), which add fixed startup view choices and a persistent adapter choice
- [Automatic capture button label](AUTO-CAPTURE-BUTTON.md), which shortens the chart toolbar status while keeping its full accessible description
- [Resource gauge centering](RESOURCE-GAUGE-CENTERING.md), which aligns bowls and values in the full-view columns
- [Units in table values](TABLE-VALUE-UNITS.md), which moves Processes and Services units into cells and keeps Usage consistent

Implemented on 2026-10-24; each note ends with what landed and where it departed from the design:

- [Resource chart views](RESOURCE-CHART-VIEWS.md)
- [Keep history across update restarts](UPDATE-RESTART-HISTORY.md)
- [Keep history across normal restarts](NORMAL-RESTART-HISTORY.md)
- [Resource detail tooltips](HOVER-DETAILS.md)
- [Automatic captures](AUTO-CAPTURE.md)
- [Usage tab](USAGE-TAB.md)
- [Settings sidebar](SETTINGS-LAYOUT.md)
- [CSV export](CSV-EXPORT.md)
- [Action button icons](ACTION-BUTTON-ICONS.md)

## Application icon

[Icon source and generation instructions](icon/README.md) explain how to regenerate the executable and notification-area icon.

The [visual QA record](../design-qa.md) retains dated implementation checks. References there to removed mockups identify historical review inputs.
