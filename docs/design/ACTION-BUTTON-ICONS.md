# Action button icons

Status: implemented on 2026-10-25.

## Goal

Show a recognizable icon alongside each app-owned action button label, including export, update, Settings, and secondary dialogs. Keep the text label and accessible name; the icon supports recognition but does not replace the label.

## Implementation

The implicit `Button` style displays `ButtonIcon.Data` before the content. Buttons defined in XAML set the attached property, and code-created buttons call `ButtonIcon.SetData`. The service row's expand/collapse glyph is already an icon. Native Windows buttons in file pickers and the window caption are outside the app's button style.

## What landed

The CSV dialog, capture control, update controls, automatic-capture folder actions, shortcut reset and update check, hardware Info actions, confirmation, dump and properties dialogs now provide icons. The update action gained a download glyph in `ButtonIcon.cs`. Feature smoke checks that the CSV dialog actions have icons and captures their appearance in Dark, Light and Slate. The export title-bar button uses the tray-arrow glyph, while the CSV dialog's Save button keeps the disk glyph. The chart's Live segment and the snapshot panel's Return to live button use the existing play triangle; circular arrows remain on refresh and restart actions.

The original request was to review all buttons. Controls that select a value, such as radio buttons, check boxes and tabs, retain their existing selection indicators rather than action icons.
