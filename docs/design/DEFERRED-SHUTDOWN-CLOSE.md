# Deferred shutdown close

The November 19 WER reports recorded a recursive `Close` from `MainWindow.OnClosing`.
An `await` can complete synchronously, leaving the original WPF Closing event active
when shutdown reaches its final close.

Queue the final close on the window dispatcher after releasing owned resources and
setting `_allowClose`. The original event remains cancelled; the queued close then
passes the existing guard. Pending saves and update handoff retain their ordering.

Landed in `MainWindow.xaml.cs`, with a regression in
`NormalShutdownCharacterizationTests` that observes the cancelled initial event,
successful final close, and absence of a diagnostic settings write. The implementation
follows the design without departures.
