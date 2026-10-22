# Global show shortcut

Status: implemented. Defaults: **Ctrl+Shift+M**, on while WinMon runs. Pressing it when WinMon is already in front minimizes it (toggle). WinMon always owns the hotkey directly for responsive hide/show.

Goal: one configurable key combination, pressed from any application, that brings a running WinMon to the front. Use Start with Windows to keep WinMon available after sign-in.

## Behavior

| WinMon state when the shortcut is pressed | Result |
| --- | --- |
| Not running | No action |
| Hidden in the notification area | Shows, restores the previous Normal/Maximized state, activates |
| Minimized to the taskbar | Restores and activates |
| Visible but behind other windows | Activates and brings it to the front |
| Already the foreground window | Minimizes, which follows Minimize to the notification area |
| Settings dialog open | Brings the dialog forward |

`MainWindow.ShowRequested` handles the direct hotkey and a second launch. It reuses `RestoreFromTray`, so restoring behaves the same as a tray click.

## Default key

A global hotkey takes the combination away from every other application while WinMon runs.

| Combination | Assessment |
| --- | --- |
| Ctrl+Shift+J | Not the default: it opens the developer console in Chrome, Edge and Firefox. Users can still choose it. |
| **Ctrl+Shift+M** | **Default** (chosen 2026-10-19). "M" for monitor; easy to press; no AltGr issue; a Start menu shortcut can use it. While WinMon runs it takes the combination from apps that use it: mute/unmute in Microsoft Teams, the profile menu in Chrome, and the device toolbar in Chrome/Edge DevTools. |
| Ctrl+Alt+Shift+W, Ctrl+Alt+Shift+M, Ctrl+Alt+Shift+F12 | Rarely used alternatives with three modifiers. AltGr counts as Ctrl+Alt, so on layouts where AltGr+Shift+letter types a character (US-International: Å for W), that character can't be typed while WinMon runs; function keys avoid this. |
| Anything with Win | Rejected: Windows reserves these, and a Start menu shortcut can't use them. |

`HotkeyGesture.Problem` enforces these rules: letters, digits, F1–F24 and navigation keys only. The combination needs at least two of Ctrl, Alt and Shift, one of them Ctrl or Alt, so Ctrl+C-style shortcuts can't be taken over. The Win key is rejected, and so is Ctrl+Alt+Delete. Esc and Tab aren't allowed keys, so Ctrl+Shift+Esc and Alt+Tab can't be chosen.

## Mechanisms

Only WinMon registers the key. The former Explorer shortcut mode was removed because it introduced a slower route for hide/show.

1. **WinMon owns the key** (`GlobalHotkey`). `RegisterHotKey` on the main window's HWND with `MOD_NOREPEAT`, and `WM_HOTKEY` through an `HwndSource` hook. Hiding the window keeps the HWND, so the key works from the tray. Receiving the hotkey grants foreground rights, so `Activate()` brings the window forward. The key is released while Settings is open, so the recorder can capture WinMon's own combination, and in `OnClosing`. Skipped in diagnostic/profile modes.

2. **Legacy migration** (`StartMenuShortcut`). Before direct registration, WinMon loads `%APPDATA%\Microsoft\Windows\Start Menu\Programs\WinMon.lnk`, clears any hotkey with `IShellLinkW::SetHotkey(0)`, and saves the shortcut. Its target, arguments, description and other properties stay intact. A missing shortcut is left missing, and a shortcut with no hotkey is not rewritten. Cleanup failures are logged; direct registration is still attempted.

3. **A second launch shows the running instance** (`InstanceActivation`). The first instance listens on the named event `Local\WinMon.CpuMonitor.Show`. A second launch calls `AllowSetForegroundWindow(ASFW_ANY)`, signals the event and exits without UI. It retries for 2 seconds, then falls back to the old "already running" message. This also makes double-clicking WinMon.exe restore the window.

During migration, `RegisterHotKey` may briefly fail while Explorer releases the legacy key. WinMon retries once a second for up to 10 seconds. If another app holds the key, the tray tooltip ends with "Shortcut unavailable" and Settings shows the conflict.

## Settings

A **Keyboard shortcut** section between the startup checkbox and Disk monitoring.

- **Show/Hide WinMon:** a read-only recorder box. While modifiers are held it shows `Ctrl + Alt + …`, and the first non-modifier key completes the combination. Backspace/Delete turns the shortcut off ("None"). Tab and Esc keep their dialog meaning.
- **Default** restores Ctrl+Shift+M.
- A status line shows the current state ("Works from any app while WinMon is running." or conflict text) or explains why a combination was rejected. A combination already held by another app never reaches the box, so the hint says that.

## Persistence

`MonitorPreferences.ShowShortcut` holds canonical text (`"Ctrl+Shift+M"`, or `"None"`). `PreferenceStore.Validate` replaces anything unparseable or disallowed with the default. Old `LaunchWithShortcut` JSON fields are ignored and dropped on the next save; the selected key and startup preference are preserved.

## Verification

- Unit: `HotkeyGestureTests`: parse/format, None, validation table, preference normalization, store round trip and old launch-option migration. `StartMenuShortcutTests` exercises real temporary `.lnk` files to verify hotkey removal, preservation of shortcut properties and idempotence.
- Native (Debug build, 2026-10-19): the key is free before launch, held while WinMon runs and free after exit. A second launch exits in about 220 ms and leaves one visible instance. Three real presses of Ctrl+Alt+Shift+W (the default at the time), starting with WinMon in the background: press 1 brought it to the foreground, press 2 minimized it, press 3 restored it to the foreground.
- Not yet verified: the Settings recorder UI by hand, Explorer releasing an installed legacy hotkey end to end, and the Release single-file launch time.
