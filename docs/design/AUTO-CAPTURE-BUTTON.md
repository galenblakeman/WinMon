# Automatic capture button label

## Intent

Keep the chart toolbar's automatic-capture button short while preserving its full status for hover and assistive technology.

## Behavior

The visible button reads **Auto** with no captures and **Auto {count}** when captures exist in history. Its tooltip and accessible name say the full localized status. The captures menu retains the detailed entries.

## Landed result and departures

The menu's **Automatic capture settings…** action opens the Automatic captures section directly, overriding the previously viewed section. Capture smoke exercises the menu action and checks the opened section.

Implemented in `MainWindow.Captures.cs` and localized resources. Capture smoke checks both the visible label and accessible status for zero and two captures. The result follows the intended behavior.
