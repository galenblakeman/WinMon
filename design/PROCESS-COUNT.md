# Inline top-process count

Selected on 2026-10-19: option 1, [inline dropdown mockup](process-count-selected.png).

The main process heading reads `Top [5] processes by CPU`. The dropdown offers 5, 10, 15, and 20 and immediately reranks the displayed live or selected historical sample. Changing the count preserves history, pause state, and the selected timestamp. The count is saved with existing window preferences on normal exit; missing or unsupported stored counts fall back to five.

The process list scrolls independently, keeping the resource graph, column headings, and footer visible. Column headings follow the list viewport width when its scrollbar appears. At minimum window height fewer rows fit; scrolling exposes the rest. Existing themed dropdown controls, focus treatment, and keyboard behavior are reused. The number selector is temporarily disabled while the settings dialog's save is in progress.

Implementation targets the existing .NET 10 WPF application without additional dependencies. Eight preference cases cover all supported counts and old/unsupported settings. Native diagnostics exercise live counts, historical selection, scrolling, minimum-size footer visibility, the menu, and light rendering. Bitmap scale captures do not verify actual desktop DPI or physical keyboard/mouse interaction.
