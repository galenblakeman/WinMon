# Feature-oriented Guide

## Purpose

Make Info → Guide a discoverable reference to WinMon's features, with practical instructions rather than five combined FAQ answers.

## Design

Keep the existing expandable topics and keyboard behavior. Give Charts and history, Processes and pins, Services, Usage, Connections, Captures and export, Automatic captures, and Compact view and shortcuts their own entries. End with a CPU-spike walkthrough connecting the chart, historical processes and Copy capture.

Each entry has a short purpose followed by two numbered steps. Include important limits alongside the relevant instructions: shared service hosts, cumulative Usage totals, optional network monitoring and Store availability, retained export history, and missing observations. Start with Charts expanded. Localize all text into the nine supported translations.

## Verification

InfoWindowTests checks the feature headings, instructional structure and default expansion. StringResourceTests checks translation coverage and matching line breaks. Feature smoke captures every expanded topic in Dark and Light and the Info tabs at minimum size; scroll each expanded topic into view before capture.

## Result

Implemented in HardwareInfoWindow.xaml and the Strings resources, with regenerated typed properties. The existing expanders remain; no new navigation actions or dependencies were added. Direct links to features are deferred as proposed. This changes help content only, with no sampling or hot-path changes.
