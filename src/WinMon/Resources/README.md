Edit `Strings.resx` for application text, including accessibility names, tooltips, formatted messages, and hardware reports. Numeric placeholders such as `{0:0.0}` preserve number and date formatting.

Use sentence case for English buttons, menus, headings and status labels. Keep product names, palette names, abbreviations and units in their established capitalization; translations follow each language's grammar.

After adding or renaming a resource, regenerate the typed properties from the repository root:

```powershell
pwsh -NoProfile -File tools/Generate-StringResources.ps1
```

WinMon follows the Windows display language on first launch, independently of the regional number and date formats. Settings includes a language selector with native language names and a Windows display language option. Language changes apply immediately when saved and remain selected for future launches. Selecting Windows display language restores the system language captured before any override. Existing settings without a language choice keep the automatic default.

The default `Strings.resx` is English and stays embedded in the main assembly, so unsupported system languages and missing translations fall back to English.

Translations are included for Simplified Chinese (`zh-Hans`), Russian (`ru`), Spanish (`es`), Brazilian Portuguese (`pt-BR`), German (`de`), Japanese (`ja`), French (`fr`), Polish (`pl`), and Korean (`ko`). Regional variants use their parent language when available, such as `fr-CA` using `fr`. Portuguese variants other than Brazil and Traditional Chinese currently fall back to English.

Add other translations as `Strings.<culture>.resx`. Keep all keys, placeholder numbers and format specifiers, and line breaks consistent with the English file. Technical identifiers and standard units intentionally remain unchanged. Rebuild to package the translations; the SDK builds satellite assemblies and includes them in the single-file publish.

Main-window XAML binds to the notifying `Strings.Text` indexer. Generated properties use the shared selected `Strings.Culture` so existing asynchronous operations see the current language too. Dynamic readouts, tooltips, chart labels and tray text refresh in place without restarting recording or replacing history. Settings and Info are modal and read the current language when opened.

This uses the standard [.NET resource lookup and UI culture behavior](https://learn.microsoft.com/dotnet/core/extensions/resources).

Stored theme and palette identifiers, shortcut serialization, WMI property names, paths, and diagnostic identifiers remain stable in code. Their display labels use resources.
