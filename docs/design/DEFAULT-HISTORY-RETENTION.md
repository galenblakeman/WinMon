# Default history retention

New installations should retain 30 minutes of history instead of 10 minutes, giving users more time to investigate recent activity. Existing saved retention choices remain unchanged.

Keep the 10/30/60-minute choices, 500,000-process-entry budget, chart range default and opt-in restart persistence. Longer periods and summarized history are outside this change.

## Result

Landed in `MonitorPreferences.RetentionSeconds`, with preference-store tests for a fresh installation and saved choices. Missing or unreadable settings also use the new default. No departures from the design.
