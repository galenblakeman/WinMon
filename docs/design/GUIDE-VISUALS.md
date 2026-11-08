# Guide visuals and Show me

## Purpose

Make Info → Guide easier to follow by showing what each step refers to, and let users jump from a topic to the feature it describes.

## Design

Split each topic's text into an intro and two step strings, so the numbers are layout rather than translated text. Each step shows a number badge with an accent ring, then the existing `ButtonIcon` of the button the step names, then the text. Steps without a matching button keep the icon column empty so the text stays aligned.

Charts and history gets a diagram: a schematic of the chart dropdown, range chips, Live button, two traces and a selected moment, with numbered callouts matching the steps. It has no text, so all languages share it, and it uses theme brushes so every theme and palette recolors it. Screenshots were rejected because they would need a copy for each language and theme and go stale with UI changes.

Each topic ends with **Show me**. It closes Info and the main window points out the feature: it leaves Compact view, expands the chart or activity panel if needed, selects the activity tab, focuses the control and flashes its opacity three times. The flash is skipped when Windows client-area animations are off.

## Verification

InfoWindowTests checks each topic's intro, numbered steps, Show me button and topic Tag, the Charts diagram, and that Show me closes Info with the topic. StringResourceTests checks the split strings in all translations. Feature smoke fails if a Show me Tag has no main-window target, and captures every topic in Dark and Light and the Info tabs at minimum size.

## Result

Implemented in HardwareInfoWindow.xaml (`GuideStep` and `GuideShowMe` styles, Charts diagram), `HardwareInfoWindow.ShowMeTopic`, `MainWindow.GuideTarget`/`ShowGuideTarget` and a new `ButtonIcon.Locate`. Show me sits under the steps rather than in the topic header as mocked up, so the header stays a single toggle for keyboard and screen readers. The diagram is decorative and has no accessible name, since the steps say the same thing. Only Charts has a diagram for now. No sampling or hot-path code changed.
