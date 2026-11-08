# Connections search clear button

Reuse the Services search control's trailing Cancel icon, dimensions, translated Clear search tooltip and accessible name. Show it only while the Connections search contains text. Clicking clears the filter and returns keyboard focus to the search box; existing Escape behavior remains available.

Implemented in ConnectionsView.xaml and its code-behind, with a filtering regression test and Connections smoke coverage for visibility, clearing and focus. No departure from the design.
