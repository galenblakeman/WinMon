# WinMon application icon

Selected concept: option 3, a green activity waveform forming a W on a charcoal tile.

`WinMon.xaml` is the editable vector source. Regenerate the application assets on Windows:

```powershell
pwsh -NoProfile -STA -File docs/design/icon/Generate-Icon.ps1
```

The script writes `src/WinMon/Assets/WinMon.ico` with 16, 20, 24, 32, 40, 48, 64, 128, and 256-pixel frames, plus a 256-pixel PNG preview. WPF renders the source directly; no external image packages are needed.

Each frame is rendered directly from the vector source. The 16, 20, and 24-pixel frames use a heavier waveform stroke for legibility. A fixed 256-unit view box preserves transparent padding; broad peaks and valleys avoid fine details that disappear at tray sizes.

The ICO is embedded as both the executable icon and a WPF resource. The main window and notification-area icon use that resource. The tray owns a cloned icon and disposes it during shutdown.
