# CLI report corrections

## Problem

Calendar Usage reused the live timestamp formatter. An application active within five seconds of the selected day's ending midnight could display “now” in an old archive. Standalone connection reports repeatedly scanned the process list for each endpoint, sorted twice, and constructed every JSON/table row before applying Top.

## Implementation

Pass the existing archived flag into the last-activity formatter for both cells and details. Archives use the localized local date/time, even for today's archive; missing activity remains unavailable. Live and frozen session formatting is unchanged.

Index Windows owners by PID and creation token, preserving the original first-match behavior. Resolve each endpoint once, filter and sort once, remember the matching count, then limit before constructing either output representation. Keep unknown owners, stable sort ties, family availability and existing JSON fields intact.

## Result and departures

Landed in `UsageTab` and `ActivityReport`, with regression cases in `UsageTabTests` and `ActivityReportTests`. Tests cover midnight boundaries, today's date, missing activity, one-pass owner lookup, exact lifetime/domain matching, stable JSON/table order, filtering, Top/truncation and discarded endpoints that must never be formatted. Release measurements are recorded in [performance history](../PERFORMANCE-HISTORY.md). No departure from this scope; collection, interactive rendering and stored formats are unchanged.
