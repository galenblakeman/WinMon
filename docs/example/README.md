# WinMon screenshot gallery

[Download WinMon](https://github.com/galenblakeman/WinMon/releases/latest) · [Overview](../../README.md) · [User guide](../USER-GUIDE.md)

Actual WinMon windows with illustrative identities and measurements. Click any preview to open the full-size image.

Captured November 27, 2026, with **WinMon 5.2.0** on Windows build **26300.10075**, x64. The main gallery uses the desktop build. The separate Store Settings image comes from an unpackaged build with `StoreBuild=true`.

## Investigate

[![History showing the CPU peak, the before and after comparison, and the collapsed Events sidebar on the right](example_history.png)](example_history.png)

**Find what was busy at a spike.** Choose **History** just before **Export** in the Full view title bar to open **What just happened?** at the CPU peak. Use **Compare periods** to see which processes changed most across the 10-second windows before, during and after the selected moment. **Selected moment** shows the recorded readings at the cursor. Choose another resource or use **Refresh** to load the latest recording. Network ranks processes by recorded receive and send rates when available.

Expand **Events** on the right to inspect related Windows events or select a group in **Crashes** to highlight recurring failures on the timeline. Use **Save incident…** to choose how much of the recording and its events to keep. If the spike has already rolled out of History, check **History → Longer trends** for its minute peak and any saved automatic incident covering it.

| Watch live activity | Look back at a spike |
| --- | --- |
| [![Live dashboard with resource gauges, a recent spike and process rankings](example_individual.png)](example_individual.png) | [![Capture showing Chrome's CPU activity during an earlier spike](example_snapshot.png)](example_snapshot.png) |
| See current resource use alongside recent history. | Click a spike to inspect its interval and process rankings. Top gauges remain live; process CPU averages five seconds. |

To catch the next Chrome spike while you’re away, open **Auto → Automatic capture settings…**, choose the application CPU metric, and add a scoped rule for `chrome.exe`. Set the CPU threshold and how long it must hold. Open the resulting `.wmincident` in History to review the readings around the trigger.

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
| Small multiples give each resource its own scale on a shared timeline. | Network Focus separates receive and send rates while keeping the other resources visible for context. |

The app also offers Heatmap and Logical processors charts. Choose CPU, Disk or GPU Focus to see activity and available temperature readings together. Temperature uses a thin trace and a scale on the right. See the [chart guide](../USER-GUIDE.md#your-system-at-a-glance) for all choices.

With the hardware sensor service enabled, select **Temperature** to compare CPU, GPU and drive readings on a Celsius or Fahrenheit scale. In **History → Longer trends**, select a temperature series to review minute peaks across the last day or week. To capture a sustained spike, open **Settings → Automatic captures**, choose the sensor metric and set a Celsius threshold and duration. [Temperature history and captures](../design/TEMPERATURE-HISTORY.md).

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

Drag the divider below the meters down in Full or Compact view to see three more readings per resource, such as disk latency, network link speed, and GPU memory use. Drag it back up to collapse the details, or focus the divider and use the Up and Down arrow keys.

<details>
<summary>Dashboard at its minimum width</summary>

[![Light dashboard at minimum width with grouped process rankings](example_narrow.png)](example_narrow.png)

The full dashboard also fits a narrower window. Long process names and peak labels may be shortened; hover in the app for full values.

</details>

## About these examples

The process dashboard, History, chart, palette and Compact views share one illustrative recording. Chrome becomes busy around the selected spike, then returns to its baseline. Services, Usage and Connections use separate recordings tailored to those features. Settings uses real controls; drive choices reflect the capture machine.

The main examples use installed application icons when available, with generic icons for other illustrative processes. All gallery images capture the complete Windows-composed window, including native caption buttons. Unavailable, partial and other diagnostic states remain separate verification outputs.

[Regenerate and verify the gallery](REGENERATING.md).
