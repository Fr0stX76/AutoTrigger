# AutoTrigger architecture

How the code is organized, for anyone who maintains it. Comments in the code explain why, not what: Windows and WPF constraints, engine decisions and traps. Names, tests and this page carry the rest.

## Projects

| Project | Contents |
| --- | --- |
| `src/AutoTrigger.Core` | Engine, settings model, JSON, log and Windows interop. No user interface. |
| `src/AutoTrigger` | WPF app built in code, without XAML, on [WPF UI](https://github.com/lepoco/wpfui): window, tray, dialogs, keyboard shortcuts, command line. |
| `tests/AutoTrigger.Tests` | Console test runner, no test framework. Exit code 0 when every check passes. |

## Threads

| Thread | Owns |
| --- | --- |
| UI thread | Windows, tray, settings edits (`SettingsController`), keyboard shortcuts (`HotkeyService`, `RegisterHotKey` on a message-only window), the focus hook (`ForegroundWatcher`). |
| Engine thread (`EngineLoop`) | All engine state: process detection, focus, event runs, timers. Other threads only `Post` to it. |
| Thread pool | Actions (`ActionExecutor`), the command line server (`CommandServer`), settings saves (`SettingsStore`). |
| Icons (`AppIcons`) | Reads program icons and keeps their cache, on its own STA thread. |
| Log writer | Writes `AutoTrigger.log` in batches. |

Rules that keep this safe:

- Engine state is read and changed on the engine thread only. Actions run elsewhere and report back with `Post`.
- Display and Windows settings changes take one shared semaphore: one hardware change at a time.
- Run program, Close program requests and Hotkey keys are sent in the order they were asked for.
- Nothing that can block (pipes, files, network) runs on the engine thread. The UI thread only checks a file the user has just picked in a dialog; a path being typed is read on the thread pool.

## From a process to an action

1. **Detection** (`TriggerEngine`): the process list is read every 500 ms; a new process is matched against the applications by path or file name and fires *Started*. Its exit is signaled by Windows and fires *Closed*. Focus (`TriggerEngine.Focus`) comes from a WinEvent hook and must stay 300 ms; for Windows Store apps, the app inside the `ApplicationFrameHost` frame is used. *Restart app* is in `TriggerEngine.Restart`.
2. **Plan** (`PlanBuilder`): the profile's actions for the event, with *Reference profile* inlined (8 levels at most, loops refused).
3. **Run** (`EventRun`): actions start one after the other; a delay counts from the start of the previous action; *Hold next* waits for the program to end, up to its cap.
4. **Newest takes over** (`TriggerEngine.Runs`): a new run with actions fast-tracks the runs of other applications and of keyboard shortcuts (remaining actions at once, holds released) and waits for them, 10 s at most.
5. **On demand**: a keyboard shortcut, `/started` or `/closed` runs the *Started* or *Closed* actions of a profile, also while monitoring is paused.

## User interface

The window, pages and dialogs are built in C#, without XAML, on WPF UI. The whole app compiles and reads as C#, and each page builds its layout in short `Build` methods such as `BuildStatusCard`.

- `Kit`: the shared controls (buttons, fields, switches, lists) and their WPF UI styles.
- `Spacing`: the spacing scale (4, 8, 12, 16, 24, 32, plus 18 between form fields) and the page, panel and list margins. Margins use these names; one-off values stay as numbers where they are used.
- `Res`: theme brushes and text, so light and dark themes follow Windows.
- `DropDowns`, `WindowPlacement`, `DialogFit`: list placement and dialog position, as Windows 11 does them.

## Settings

- `%LOCALAPPDATA%\AutoTrigger\settings.json`, saved 500 ms after a change through a temporary file and `File.Replace`.
- The window edits `SettingsController.Current`; the engine receives a copy 300 ms after the last change.
- Unknown action types are kept unchanged. Older types (`audio`, `powerPlan`, `screenSaver`) are read as `windowsSettings`.
- A file that cannot be parsed is set aside and AutoTrigger starts empty. A file that cannot be opened, or set aside, is never written over.
- At start, only the 3 most recent copies of each kind are kept (`settings.before-import-*`, `settings.invalid-*`), and cached icons of programs no longer listed are removed.

## Command line

A second `AutoTrigger.exe` with `/pause`, `/resume`, `/toggle`, `/started "Name"` or `/closed "Name"` sends one line through the named pipe `AutoTrigger.7f3c1d2e.Commands.<session>` and waits for one line back. The pipe is local and open to the current user only. The server keeps a single instance for the whole session, so no other program can take the name. It drops a silent client after 5 s and answers within 10 s.

## Tests

`AutoTrigger.Tests.exe` runs the engine against fakes (process list, scheduler, foreground window, hardware) and checks settings JSON (including files written by earlier versions), display change order, keyboard shortcut rules and the command line through a private pipe. GitHub Actions builds and runs it on every push.

## Conventions

- Texts of the window are in `Texts.cs`. Messages built by the engine and the settings code, such as import notes and settings problems, are written where they are produced.
- Windows 11 conventions for controls, wording and spacing.
- No administrator rights. The only network access is `UpdateCheck`: one HTTPS GET to the GitHub API, no redirect, size and time limits, turned off in Settings. The release page it opens is built from the checked version, never taken from the response.
- Errors: `catch (Exception)` only where an exception must never escape (window procedures, background threads, actions, startup and exit) or around drivers, COM and third-party code (WPF UI, GDI+), whose exceptions are not documented. Everywhere else, only the documented exceptions are caught. Caught failures are logged, unless a fallback is the expected outcome.
