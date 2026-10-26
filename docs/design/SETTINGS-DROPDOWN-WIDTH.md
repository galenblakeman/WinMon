# Settings dropdown width

## Intent

Keep Settings labels readable beside dropdowns when the content panel is narrow.

## Behavior

Every dropdown in the General, Appearance and Monitoring field rows takes half that row's available width. Long field labels trim at narrow widths, and the network interface selector may shorten a long selected name; its tooltip retains the full name.

## Landed result and departures

`SettingsWindow.Field` uses two equal star columns for dropdown rows. This covers both the standard and minimum Settings window widths without a later layout pass. The result follows the intent above.
