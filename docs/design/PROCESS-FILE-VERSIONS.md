# Process file versions

Show file and product versions in process Properties and Copy details, including grouped and historical rows. Reuse the executable metadata read and bounded per-path cache rather than adding file access when the user opens details. Missing or inaccessible metadata uses the existing missing-reading display.

Carry both strings on ProcessUsage so ranking, grouping and retained samples preserve them. Persist them in restart-history format 2, keeping the format-1 reader and fixture; old history has empty versions. Older application builds reject format-2 files. Versions describe cached metadata from the file on disk, which may differ from a running image after replacement.

Landed: both fields use the shared details formatter, all supported translations, existing metadata error handling and cache. Saved history retains the versions. No departures from this scope; CSV and Usage app reports are unchanged.
