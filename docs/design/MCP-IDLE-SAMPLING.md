# On-demand MCP sampling

An agent can retain several stdio server connections for idle chats. Starting a native collector when each server starts multiplies background work even when no tool needs live metrics.

## Lifecycle

- Host startup and tool discovery create no frame source or network subscription.
- Valid live requests acquire a shared sampling lease. Concurrent requests share one `MonitorSession`; performance requests hold their lease for the entire requested window.
- Releasing the final lease starts a 30-second grace period. Another live request cancels the deadline. Hardware, saved-report and retained-history tools neither start sampling nor extend the deadline.
- At the deadline, cancel and await the collection loop and optional traffic subscription, then dispose their native resources. A synchronous native capture must finish before disposal or restart.
- The next live request creates a new source and waits for two new publications, including a baseline. Previously retained frames cannot satisfy its warm-up. Traffic reconnects only when requested again and receives a new subscription/session ID.
- Host shutdown cancels pending readers and awaits cleanup. Cancelling one request does not interrupt another request's collection.

## Observation limits

Retain the bounded 120-frame buffer and session Usage ledger across idle stops. They cover only observed activity; idle time is not reconstructed or added to Usage. The resumed native source establishes new rate baselines rather than measuring across the idle interval. Historical queries can return retained observations while sampling is stopped; requested and retained UTC bounds still apply. Traffic totals restart with each new subscription.

The grace period is fixed rather than adding a user setting or protocol option. This preserves warm samples for nearby requests and the existing five-second traffic warm-up/retry behavior.

## What landed

`SamplingOwner` manages shared request leases, a cancellable idle deadline, serialized stop/restart, and disposal. Live diagnostic tools hold leases; history, hardware and saved-report tools keep their independent behavior. Tests cover lazy startup, cancelled/invalid requests, sharing, idle release/restart, warm-up, long performance requests, and traffic unsubscribe/reconnect. Native before/after measurements are recorded in `docs/PERFORMANCE-HISTORY.md`. No departure from this design is intended.
