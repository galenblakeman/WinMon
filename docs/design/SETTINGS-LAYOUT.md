# Settings sidebar

Selected and implemented on 2026-10-24: Option A of the Settings reorganization mockups, a sidebar of sections.

## Why

With automatic captures added, the single-column Settings dialog grew to about 1,300 pixels tall and needed scrolling on most screens. Grouping the settings into sections keeps every section on one screen.

## Layout

The dialog is 780 × 600 DIPs, shorter on small work areas, and does not resize. A 216-DIP sidebar lists the sections; the selected one has an accent bar. Cancel and Save stay in a footer below every section, so one Save applies changes from all of them.

| Section | Contents |
| --- | --- |
| General | Language; start at sign-in and minimize to the notification area; the show/hide shortcut |
| Appearance | Theme, chart palette, resource gauge style and its preview |
| Monitoring | Historical retention; primary and second drive |
| Automatic captures | An on/off switch in the page header, the rule table, cooldown, reports folder and notifications. The sidebar shows On or Off. |
| Updates | Installed version, when this session last checked, Check for updates, and automatic checks. Store builds have no Updates section. |

## Behavior

- **Rule table.** Column headings (Rule, Above, For) replace the per-row "above" and "for" words. Each rule shows its resource's chart color. The Any one process explanation is the rule's tooltip. The second-disk rule follows the second-drive selector: it is disabled, with its values kept, while no second drive is chosen.
- **Last checked** records the end of this session's most recent update check, automatic or manual, and is not saved.
- **Validation.** When Save finds a problem in another section, the dialog switches to that section, shows the message, and focuses the field to fix: the drive selectors for a drive conflict, or the threshold, duration or cooldown box that is out of range.
- **Keyboard.** Arrow keys move through the sidebar. Ctrl+Tab and Ctrl+Shift+Tab step through sections from anywhere in the dialog, except while the shortcut box is recording a combination.
- **Selection contrast.** The selected section uses the theme's selected-text color for its label, icon and On/Off state, including in Light and high-contrast appearances.
- **Diagnostics and tests** reach the dialog through named members (`ShowSection`, `Section`, `LanguageSelector`, the drive selectors, `SaveButton`, `AutoCapture`) instead of its layout. `WinMon.exe --capture-smoke-output <directory>` renders every section.

## 2026-10-25 refinement

Appearance became its own sidebar section as the theme choices expanded. The three appearance selectors and gauge preview moved together; language remains in General. The dialog size, navigation behavior and single Save action remain the same. Layout tests check that the controls belong to the intended sections, and the capture smoke renders the new page in Dark and Light.

The main window now remembers the last section viewed when Settings closes, whether the user saved, cancelled or dismissed the dialog. Opening Settings again in the same app session selects that section; a new app session starts on General. The dialog still selects General by default when created independently for tests and diagnostics.

## Departures from the mockup

- The window is 780 DIPs wide rather than 760, and the sidebar 216 rather than 196, so "Automatic captures" and its On/Off state fit without trimming in English.
- Option B, top tabs in a 500-DIP window, was rejected because the automatic captures section still had to scroll.
