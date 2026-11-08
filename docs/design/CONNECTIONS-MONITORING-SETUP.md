# Connections monitoring setup strip

Replace the small footer hyperlink with a setup strip directly below the filters and above the table header. Use the existing theme surfaces, a thin accent at the left, “Traffic monitoring is off”, a short explanation of app download/upload rates, and a filled “Enable monitoring…” button. The button opens Settings → Network through the existing event; it does not enable monitoring automatically.

Keep the existing visibility rules: show only in supported desktop builds while monitoring is disabled and the view is live. Enabled monitoring, Capture history and Store builds reclaim the space. Collection recovery and setup progress continue to use the existing Network settings flow.

The strip is at least 64 DIPs tall and grows for translated text. Below 640 DIPs it places the button below the text. The button has a localized accessible name and help text, a keyboard focus outline, and contrasting text selected for the current accent. High contrast uses system highlight colors.

Landed in ConnectionsView XAML/code-behind, shared accent text resources and theme application, all ten string resource files, Connections view tests and the Connections smoke. README and developer guide describe the strip. The layout follows the selected mockup; the button follows the user's theme accent rather than forcing blue, and the strip grows instead of clipping translated text. Setup progress remains in Network settings rather than adding a second setup controller to Connections.
