# Global show shortcut

Status: implemented. Defaults: **Ctrl+Shift+M**, on while WinMon runs. Pressing it when WinMon is already in front minimizes it (toggle). Starting WinMon with the key is opt-in.

Goal: one configurable key combination, pressed from any application, that brings WinMon to the front. If WinMon isn't running, the same combination can optionally start it.

## Behavior

| WinMon state when the shortcut is pressed | Result |
| --- | --- |
| Not running (only with "Also start WinMon with this shortcut") | Explorer starts WinMon |
| Hidden in the notification area | Shows, restores the previous Normal/Maximized state, activates |
| Minimized to the taskbar | Restores and activates |
| Visible but behind other windows | Activates and brings it to the front |
| Already the foreground window | Minimizes, which follows Minimize to the notification area |
| Settings dialog open | Brings the dialog forward |

`MainWindow.ShowRequested` handles every route: the hotkey, a second launch, and Explorer's shortcut hotkey. It reuses `RestoreFromTray`, so restoring behaves the same as a tray click.

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

Spike result (2026-10-19): when a Start menu `.lnk` has a hotkey, Explorer registers that combination within about a second, and `RegisterHotKey` for it then fails with `ERROR_HOTKEY_ALREADY_REGISTERED` (1409). Deleting the shortcut releases it within a few seconds. So only one owner can hold the key:

1. **Default: WinMon owns the key** (`GlobalHotkey`). `RegisterHotKey` on the main window's HWND with `MOD_NOREPEAT`, and `WM_HOTKEY` through an `HwndSource` hook. Hiding the window keeps the HWND, so the key works from the tray. Receiving the hotkey grants foreground rights, so `Activate()` brings the window forward. The key is released while Settings is open, so the recorder can capture WinMon's own combination, and in `OnClosing`. Skipped in diagnostic/profile modes.

2. **"Also start WinMon with this shortcut": Explorer owns the key** (`StartMenuShortcut`). WinMon releases its registration *before* writing `%APPDATA%\Microsoft\Windows\Start Menu\Programs\WinMon.lnk` with `IShellLinkW::SetHotkey`; Explorer couldn't take a combination WinMon holds and wouldn't retry once WinMon exited. Each press starts WinMon.exe. When WinMon is already running, that new process forwards to the running one (3). The shortcut is rewritten only when the executable path or hotkey changed, and deleted when the option is turned off. Its presence is the setting, as with startup registration. It's unavailable from development/test hosts. The hook also handles `WM_SYSCOMMAND`/`SC_HOTKEY`, in case Explorer hands the hotkey to the window it started.

3. **A second launch shows the running instance** (`InstanceActivation`). The first instance listens on the named event `Local\WinMon.CpuMonitor.Show`. A second launch calls `AllowSetForegroundWindow(ASFW_ANY)`, signals the event and exits without UI. It retries for 2 seconds, then falls back to the old "already running" message. This also makes double-clicking WinMon.exe restore the window.

When WinMon takes the key back after the Start menu shortcut is turned off, `RegisterHotKey` may briefly fail while Explorer releases it. WinMon retries once a second for up to 10 seconds. If another app holds the key, the tray tooltip ends with "Shortcut unavailable" and Settings shows the conflict.

## Settings

A **Keyboard shortcut** section between the startup checkbox and Disk monitoring. The dialog height went from 700 to 850.

- **Show WinMon:** a read-only recorder box. While modifiers are held it shows `Ctrl + Alt + …`, and the first non-modifier key completes the combination. Backspace/Delete turns the shortcut off ("None"). Tab and Esc keep their dialog meaning.
- **Default** restores Ctrl+Shift+M.
- A status line shows the current state ("Works from any app while WinMon is running.", "…and starts WinMon when it isn't running.", conflict text) or explains why a combination was rejected. A combination already held by another app never reaches the box, so the hint says that.
- **Also start WinMon with this shortcut:** off by default and disabled while the shortcut is None. Its tooltip says it adds WinMon to the Start menu.

## Persistence

`MonitorPreferences.ShowShortcut` holds canonical text (`"Ctrl+Shift+M"`, or `"None"`). `PreferenceStore.Validate` replaces anything unparseable or disallowed with the default. `LaunchWithShortcut` round-trips through the dialog, but the `.lnk` file is what's actually checked.

## Verification

- Unit: `HotkeyGestureTests`: parse/format, None, validation table, `.lnk` hotkey encoding, preference normalization and store round trip.
- Native (Debug build, 2026-10-19): the key is free before launch, held while WinMon runs and free after exit. A second launch exits in about 220 ms and leaves one visible instance. Three real presses of Ctrl+Alt+Shift+W (the default at the time), starting with WinMon in the background: press 1 brought it to the foreground, press 2 minimized it, press 3 restored it to the foreground.
- Not yet verified: the Settings recorder UI by hand, Start menu mode end to end (Explorer launching WinMon when closed, and forwarding when running), and the Release single-file launch time.
