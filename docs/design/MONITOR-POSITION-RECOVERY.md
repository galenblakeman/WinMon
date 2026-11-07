# Monitor position recovery

## Problem

`DisplaySettingsChanged` previously called `EnsureVisible` immediately. A monitor temporarily missing during wake could cause WinMon to move onto the primary display, with no target retained for reconnection.

## Behaviour

- Capture the normal window's monitor device name and physical offset within its work area at startup and after user move/resize completion or Windows-key placement.
- Restart a three-second timer on each display-change or resume notification, letting successive reconnection notifications settle.
- Restore onto the remembered monitor without activating, raising or explicitly resizing the window. Clamp the position when its work area shrinks or moves.
- If the monitor is absent, keep the existing visibility fallback and retain the target for a later reconnect. A user move replaces it.
- Defer recovery while moving, minimized or maximized. Unsubscribe native hooks and power events and stop the timer during shutdown.

## Verification

`WindowPositionTests` covers disconnect/reconnect, negative coordinates, monitor rearrangement and reduced work areas. Compact smoke checks the settling delay, restarting it on a later notification, restoring native position and respecting a simulated user move. Full and compact smoke modes cover window lifecycle and compact/full switching. Real sleep/wake, monitor removal/reconnection, Windows-key moves and mixed-DPI transitions require manual hardware validation.

## Landed

Implemented in `MainWindow.Position.cs` and `WindowPosition.cs`; `EnsureVisible` now checks native physical bounds rather than scaling the virtual-desktop origin by the current monitor's DPI. No settings, dependencies or sampling work were added. Monitor identity is retained only during the current process, preserving the existing startup preference format. The three-second quiet period is a settling heuristic, not a guarantee that all hardware has reconnected; later display notifications retry recovery.
