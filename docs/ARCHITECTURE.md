# AutoTrigger architecture

How the code is organized, for anyone who maintains it. The code has no comments by choice: names, tests and this page carry the intent.

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
| Thread pool | Actions (`ActionExecutor`), the command line server (`CommandServer`). |
| Log writer | Writes `AutoTrigger.log` in batches. |

Rules that keep this safe:

- Engine state is read and changed on the engine thread only. Actions run elsewhere and report back with `Post`.
- Display and Windows settings changes take one shared semaphore: one hardware change at a time.
- Run program, Close program requests and Hotkey keys are sent in the order they were asked for.
- Nothing that can block (pipes, files) runs on the engine or UI thread.

## From a process to an action

1. **Detection** (`TriggerEngine`): the process list is read every 500 ms; a new process is matched against the applications by path or file name and fires *Started*. Its exit is signaled by Windows and fires *Closed*. Focus comes from a WinEvent hook and must stay 300 ms; for Windows Store apps, the app inside the `ApplicationFrameHost` frame is used.
2. **Plan** (`PlanBuilder`): the profile's actions for the event, with *Reference profile* inlined (8 levels at most, loops refused).
3. **Run** (`EventRun`): actions start one after the other; a delay counts from the start of the previous action; *Hold next* waits for the program to end, up to its cap.
4. **Newest takes over** (`TriggerEngine.Runs`): a new run with actions fast-tracks the runs of other applications and of keyboard shortcuts (remaining actions at once, holds released) and waits for them, 10 s at most.
5. **On demand**: a keyboard shortcut, `/started` or `/closed` runs the *Started* or *Closed* actions of a profile, also while monitoring is paused.

## Settings

- `%LOCALAPPDATA%\AutoTrigger\settings.json`, saved 500 ms after a change through a temporary file and `File.Replace`.
- The window edits `SettingsController.Current`; the engine receives a copy 300 ms after the last change.
- Unknown action types are kept unchanged. Older types (`audio`, `powerPlan`, `screenSaver`) are read as `windowsSettings`.
- A file that cannot be parsed is set aside and AutoTrigger starts empty. A file that cannot be opened is never written over.

## Command line

A second `AutoTrigger.exe` with `/pause`, `/resume`, `/toggle`, `/started "Name"` or `/closed "Name"` sends one line through the named pipe `AutoTrigger.7f3c1d2e.Commands.<session>` and waits for one line back. The pipe is local and open to the current user only. The server drops a silent client after 5 s and answers within 10 s.

## Tests

`AutoTrigger.Tests.exe` runs the engine against fakes (process list, scheduler, foreground window, hardware) and checks settings JSON, display change order, keyboard shortcut rules and the command line through a private pipe. GitHub Actions builds and runs it on every push.

## Conventions

- Texts shown to users are in `Texts.cs`.
- Windows 11 conventions for controls, wording and spacing.
- No administrator rights and no network access.
