# WinMon screenshot gallery

[Download WinMon](https://github.com/galenblakeman/WinMon/releases/latest) · [Overview](../../README.md) · [User guide](../USER-GUIDE.md)

Actual WinMon windows with illustrative identities and measurements. These images show features, not performance measurements. Click any preview to open the full-size image.

Captured November 9, 2026, with **WinMon 4.0.6** on Windows build **26300.9818**, x64. The main gallery uses the installer/portable configuration; the separate Store Settings image uses `StoreBuild=true` and does not prove installed MSIX behavior.

## Investigate

| Watch live activity | Look back at a spike |
| --- | --- |
| [![Live dashboard with resource gauges, a recent spike and process rankings](example_individual.png)](example_individual.png) | [![Capture showing Chrome's CPU activity during an earlier spike](example_snapshot.png)](example_snapshot.png) |
| See current resource use alongside recent history. | Click a spike to inspect its interval and process rankings. Top gauges remain live; process CPU averages five seconds. |

| Follow services and their workers | Find apps with network activity |
| --- | --- |
| [![Services showing Build Agent and its compiler and worker processes](example_services.png)](example_services.png) | [![Connections showing app traffic rates, accumulated byte totals and endpoint counts](example_connections.png)](example_connections.png) |
| Expand a service to inspect its host and observed workers. | Inspect TCP/UDP endpoints and optional app traffic. This example uses synthetic traffic; no monitoring service is installed by the diagnostic. |

[![Usage showing CPU time, I/O, GPU time and exited apps](example_usage.png)](example_usage.png)

**Account for the session.** Usage retains app totals after processes exit. Here, Example Setup has exited while Chrome remains active. Not attributed shows machine busy time that could not be assigned to an app.

## Compare charts

| Separate resource trends | Focus on one resource |
| --- | --- |
| [![Small multiples with a separate chart for each resource](example_small_multiples.png)](example_small_multiples.png) | [![Network Focus chart with a throughput scale and smaller context charts](example_focus_network.png)](example_focus_network.png) |
| Small multiples make simultaneous trends easier to compare. | Focus gives one resource more room and its own scale. |

The app also offers Heatmap and Logical processors charts. See the [chart guide](../USER-GUIDE.md#your-system-at-a-glance) for all choices.

## Find and follow apps

| Combine related processes | Search and filter |
| --- | --- |
| [![Processes grouped by executable, combining Chrome instances](example_grouped.png)](example_grouped.png) | [![Chrome search with App only enabled](example_apps_only.png)](example_apps_only.png) |
| Group by executable to see an app's combined activity. | Search by name or PID; App only focuses on processes observed with application windows. |

| Keep important apps at the top | Show only pinned apps |
| --- | --- |
| [![Visual Studio Code and Chrome pinned above the ranked list](example_pinned.png)](example_pinned.png) | [![Pinned only showing Visual Studio Code and Chrome](example_pinned_only.png)](example_pinned_only.png) |
| Pins stay above rankings without consuming a Top slot. | Pinned only gives your chosen apps the whole list. |

## Customize

| Light appearance | Preview before saving |
| --- | --- |
| [![Light appearance with the Classic chart palette](example_light.png)](example_light.png) | [![Appearance settings previewing the Dark theme, Neon palette and Bowl gauges](example_settings.png)](example_settings.png) |
| Choose an appearance and chart palette independently. | Preview the theme, chart colors and gauges together. |

| Neon palette | Arcade palette | Tropical palette |
| --- | --- | --- |
| [![Neon chart colors in Dark appearance](example_neon.png)](example_neon.png) | [![Arcade chart colors in Dark appearance](example_arcade.png)](example_arcade.png) | [![Tropical chart colors in Dark appearance](example_tropical.png)](example_tropical.png) |

These previews compare chart palettes on the same recording. Additional appearances and gauge styles are available in Settings.

<details>
<summary>Settings in a Store build</summary>

[![Store Appearance settings without the GitHub Updates section](example_settings_store.png)](example_settings_store.png)

Store builds use Store-managed updates and omit the GitHub Updates section. This is a build-variant screenshot, not evidence of Store publication or installation.

</details>

## Save space

[![Dark Compact view showing live resource gauges](example_compact.png)](example_compact.png)

[![Light Compact view showing the same readings](example_compact_light.png)](example_compact_light.png)

**Compact view** keeps live readings and peak marks visible without the chart or activity list.

<details>
<summary>Dashboard at its minimum width</summary>

[![Light dashboard at minimum width with grouped process rankings](example_narrow.png)](example_narrow.png)

The full dashboard also fits a narrower window. Long process names and peak labels may be shortened; hover in the app for full values.

</details>

## About these examples

The process dashboard, chart, palette and Compact views share one illustrative recording. Chrome becomes busy around the selected spike, then returns to its baseline. Services, Usage and Connections use separate recordings tailored to those features. Settings uses real controls; drive choices reflect the capture machine.

The main examples use installed application icons when available, with generic icons for other illustrative processes. All gallery images capture the complete Windows-composed window, including native caption buttons. Unavailable, partial and other diagnostic states remain separate verification outputs.

[Regenerate and verify the gallery](REGENERATING.md).
