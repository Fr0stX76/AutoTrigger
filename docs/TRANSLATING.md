# Translating AutoTrigger

Every text of the window is in one file per language in the [`lang`](../lang) folder. Fixing a translation means editing that file: no code, no build.

English (`en.json`) is the reference. French (`fr.json`) was reviewed by the author. The other languages were translated with AI, using the words of Windows in each language, and have not been reviewed by native speakers yet. Corrections are welcome.

## Languages

One file per language, named by its code. Regional variants share one file.

| Code | Language | Code | Language |
|---|---|---|---|
| `bg` | Bulgarian | `ko` | Korean |
| `ca` | Catalan | `lt` | Lithuanian |
| `cs` | Czech | `lv` | Latvian |
| `da` | Danish | `nb` | Norwegian (Bokmal, also used for Nynorsk) |
| `de` | German | `nl` | Dutch |
| `el` | Greek | `pl` | Polish |
| `en` | English (reference) | `pt` | Portuguese (Brazil, also used in Portugal) |
| `es` | Spanish | `ro` | Romanian |
| `et` | Estonian | `ru` | Russian |
| `eu` | Basque | `sk` | Slovak |
| `fi` | Finnish | `sl` | Slovenian |
| `fr` | French | `sr-Latn` | Serbian (Latin) |
| `gl` | Galician | `sv` | Swedish |
| `hr` | Croatian | `th` | Thai |
| `hu` | Hungarian | `tr` | Turkish |
| `id` | Indonesian | `uk` | Ukrainian |
| `it` | Italian | `vi` | Vietnamese |
| `ja` | Japanese | `zh-Hans` | Chinese (Simplified) |
| | | `zh-Hant` | Chinese (Traditional) |

## Try a correction in AutoTrigger

1. Create a file named after the language in `%LOCALAPPDATA%\AutoTrigger`, next to `settings.json`, for example `de.json`.
2. Put only the texts you correct in it, with the same keys as in `lang`:

   ```json
   {
     "Save": "Speichern",
     "ActionCount": "Aktionen: {count}"
   }
   ```

3. Choose the language in **Settings**, then restart AutoTrigger. Your texts replace the built-in ones; every other text stays as built in.
4. **Open log** in **Settings** shows how many of your texts were used and why any were ignored, for example a missing comma or a missing `{count}`.

Delete the file when your correction is part of a release.

## Send a correction

- **Pull request**: on GitHub, open the file in `lang`, choose the pencil icon, change the texts, and propose the change. The tests check every file: keys, `{placeholders}` and JSON.
- **Issue**: give the language, the key and the corrected text.

## Rules

- Change only the text on the right. The key on the left must stay as it is.
- Keep every `{placeholder}` with the same name. You can move it in the sentence. A text that misses one, or adds an unknown one, is ignored and English is shown instead.
- `\n` is a line break. A `"` inside a text is written `\"`.
- Keep these names as they are: AutoTrigger, AutoActions, Windows, Microsoft Store, HDR, NVIDIA, settings.json, UserSettings.json, Ctrl, Alt, Shift, AltGr, UID.
- Use the words of Windows in your language first: the Settings app, Control Panel, File Explorer. When Windows has no word for it, use the common and simplest one.
- Address the user the way Windows does in your language.
- Texts may be longer than in English: the window makes room for them. If a text is still cut, it ends with an ellipsis and shows in full in its tooltip; please report it.
- Units (ms, s, h, Hz) are added by the program and are not translated.

## Texts that need context

| Keys | Where they appear |
|---|---|
| `Monitoring`, `Status` | Status page: `Monitoring` is the heading of the cards on the right, `Status` the row of the monitoring switch. `Monitoring` is also a column of the Applications page. |
| `TabDisplays` | Name of the Displays page, also the heading of the Displays card of the Status page. |
| `StatusHdrOn`, `StatusHdrOff` | Status page, value of the HDR row. |
| `StatusMonitoringOn`, `StatusMonitoringOff` | Status page, value of the Status row: monitoring on or off. |
| `StatusNoApplication`, `StatusNoProfile`, `StatusNoShortcuts` | Status page, values of Current application, Current profile and Keyboard shortcuts. `StatusNoApplication` also stands in the list of applications when none has something to launch. |
| `ApplicationMonitoringOn`, `ApplicationMonitoringOff` | Applications page, monitoring of one application. |
| `LaunchButton`, `LaunchNone` | Applications page and application window: what the Launch button of the Status page runs. `LaunchButton` names that button, as in the Status page; `LaunchNone` is the choice with no button. |
| `CreateDesktopShortcut`, `ShortcutFailed` | Status page, right-click menu of an application, and its error message. The shortcut is a file on the desktop: use the word of Windows for it, not the one for a keyboard shortcut. |
| `NoDesktopFolder`, `ShortcutNamesTaken` | The `{reason}` of `ShortcutFailed`, after a colon. |
| `ProfileNone`, `ArgumentsNone` | No profile for an application; no arguments for a program. |
| `...Unchanged`, `HdrChoiceOn`, `HdrChoiceOff`, `ScreenSaverChoiceOn`, `ScreenSaverChoiceOff` | Choices in an action, for the setting named by the key. In languages with genders, they agree with that setting. |
| `DisplayHdrOn`, `DisplayHdrOffSupported`, `DisplayHdrNotSupported` | Displays page, HDR state of a display. |
| `PrimaryShort`, `DefaultShort`, `ActiveShort` | Lower case, after the name of a display, an audio device or a power plan. |
| `NotAllowed` | In parentheses after a profile name, in the list of profiles to reference. |
| `CopyWord` | Name of a duplicated profile: `Name (copy)`, `Name (copy 2)`. |
| `MaxHz` | In the summary of a Display action, after the resolution: maximum refresh rate. |
| `NotOffered` | A saved value that the display no longer offers. |
| `CheckEvery`, `StopHoldingAfter`, `ForceAfter`, `MinimumFocusTime`, `StartDetectionInterval` | Label before a number and its unit: `Check every [4] h`. |
| `EventStarted`, `EventClosed`, `EventGotFocus`, `EventLostFocus` | The four events of an application. Also in parentheses after `KeyboardShortcut`. |
| `KeyboardShortcut` and `TypeHotkey` | Two different things: the shortcut that runs a profile, and the action that presses keys in another app. They need two different words. |
| `LoopIgnored`, `MissingIgnored`, `MissingDisplayIgnored` | On an action card: the action is ignored. |
| `ImportNotJson`, `ImportNotAutoActions` | The `{reason}` of `ImportFailed`, after a colon. |
| `Paused` | Tooltip of the tray icon. |
