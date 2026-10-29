# AutoTrigger

**Automatic HDR, resolution, refresh rate and audio switching per game or app on Windows 10 and 11.**

AutoTrigger is a free, open-source tray app that changes Windows settings when a game or app starts, and sets them back when it closes. Turn HDR on for a game and off on the desktop, change the resolution or refresh rate, switch the default audio device, change the power plan, start or close other programs, or press a keyboard shortcut of another app.

**An AutoActions alternative**: it imports the applications, profiles and displays of AutoActions in one step. Inspired by [AutoActions](https://github.com/Codectory/AutoActions) by Codectory; independent rewrite, not affiliated with Codectory or NVIDIA.

<img src="images/profiles.png" alt="AutoTrigger Profiles page: a profile that turns HDR on with its Started actions and off with its Closed actions, each action with its delay">

## Use cases

- Turn HDR on automatically when a game starts, and off when it closes.
- Use a different resolution or refresh rate for games and for the desktop, DSR and DLDSR included.
- Switch the default audio output or input per game or app, for example to a headset.
- Use the High performance power plan while a game, a video encoder or a 3D app runs, and a power saving plan afterwards.
- Turn the screen saver off for games played with a controller.
- Start helper programs before a game or app and close them after it.
- Change settings when an app gains the focus, and set them back when you switch to another window.
- Press a keyboard shortcut of another app when a game starts, for example to switch a mode in an overlay or a recording app.
- Run the actions of a profile on demand, from a keyboard shortcut or a desktop shortcut, without waiting for an app to start.

## Features

- Four events per application: Started, Closed, Gained focus, Lost focus.
- Actions, each with its own delay:
  - **Display**: HDR, resolution, refresh rate and, on NVIDIA, color depth, format and range. Works with DSR and DLDSR.
  - **Windows settings**: default audio output and input devices, power plan, and screen saver on or off.
  - **Run program**, with options to hold the next actions until it finishes and to hide the console window of command-line tools and scripts, and **Close program**.
  - **Hotkey**: presses a keyboard shortcut of another app.
  - **Reference profile**: reuses the actions of another profile.
- An optional name for Run program, Close program and Hotkey actions, shown on the action card and in the log.
- Shared profiles, a monitoring switch per application and a global pause.
- Keyboard shortcuts and command line to run the Started or Closed actions of a profile on demand.
- Windows Store apps, focus included.
- Light and dark themes, following Windows by default.
- Import from AutoActions.
- Update check: a link at the bottom of the window and a dot on the tray icon when a new version is out.
- About 0.06 % of one CPU core while idle. No administrator rights. The only network access is the update check, which can be turned off.

<img src="images/status.png" alt="AutoTrigger Status page: HDR and monitoring switches, current application and profile, list of followed applications">

<img src="images/applications.png" alt="Applications page: each game or app with its profile and a monitoring switch">

<img src="images/display-action.png" width="49%" align="top" alt="Display action: HDR on or off, resolution, refresh rate, color depth, format and range"> <img src="images/windows-settings-action.png" width="49%" align="top" alt="Windows settings action: default audio devices, power plan and screen saver">

<img src="images/run-program-action.png" width="49%" align="top" alt="Run program action: name, program, arguments and the option to hold the next actions until it finishes"> <img src="images/hotkey-action.png" width="49%" align="top" alt="Hotkey action: name and the keyboard shortcut to press">

<img src="images/displays.png" alt="Displays page: each monitor with its resolution, refresh rate, color format and HDR state">

<img src="images/settings.png" width="430" alt="Settings page: start with Windows, theme, log level, update check and import from AutoActions">

## How actions run

An event of an application, a keyboard shortcut or a command picks a profile; the actions of that event then run one after the other.

```mermaid
%%{init: {"theme": "base", "themeVariables": {"fontSize": "15px", "lineColor": "#8b949e", "textColor": "#8b949e", "titleColor": "#8b949e", "clusterBkg": "transparent", "clusterBorder": "#8b949e", "edgeLabelBackground": "#6e7681", "primaryTextColor": "#ffffff"}}}%%
flowchart TB
  app(["Application event<br/>Started, Closed, Gained focus, Lost focus"]) --> prof
  key(["Keyboard shortcut or command line<br/>/started, /closed"]) --> prof
  prof["Profile: the actions of that event"] --> a1
  subgraph run ["Actions run in order"]
    direction TB
    a1["Display: HDR on"] -->|"delay"| ref
    subgraph ref ["Reference profile: its actions are inserted here"]
      direction TB
      r1["Windows settings: audio and power plan"] --> r2["Run program, hold until it exits"]
    end
    ref -->|"program exited"| a3["Hotkey"] -->|"delay"| a4["Run program"]
  end
  classDef event fill:#8957e5,stroke:#8957e5,color:#ffffff
  classDef profile fill:#6e7681,stroke:#6e7681,color:#ffffff
  classDef action fill:#1f6feb,stroke:#1f6feb,color:#ffffff
  class app,key event
  class prof profile
  class a1,r1,r2,a3,a4 action
  style ref stroke-dasharray: 5 5
```

When each action starts, with the delay set on each one:

```mermaid
%%{init: {"theme": "base", "gantt": {"leftPadding": 190, "barHeight": 24, "barGap": 8}, "themeVariables": {"fontSize": "14px", "textColor": "#8b949e", "titleColor": "#8b949e", "sectionBkgColor": "transparent", "altSectionBkgColor": "transparent", "sectionBkgColor2": "transparent", "gridColor": "#8b949e", "taskBkgColor": "#1f6feb", "taskBorderColor": "#1f6feb", "taskTextColor": "#ffffff", "taskTextLightColor": "#ffffff", "taskTextOutsideColor": "#8b949e", "taskTextDarkColor": "#ffffff", "activeTaskBkgColor": "#6e7681", "activeTaskBorderColor": "#6e7681", "critBkgColor": "#8957e5", "critBorderColor": "#8957e5", "todayLineColor": "transparent"}, "themeCSS": ".grid .tick line { stroke: #8b949e; opacity: 0.35; } .grid path { stroke-width: 0; }"}}%%
gantt
  dateFormat X
  axisFormat %-S s
  section Display, delay 0
    HDR on, 2 s : a1, 0, 2s
  section Run program, delay 3 s
    delay 3 s : active, d1, 0, 3s
    runs, next actions held : a2, after d1, 5s
  section Hotkey, delay 0
    starts when the program exits : milestone, m1, after a2, 0s
  section Run program, delay 2 s
    delay 2 s : active, d2, after a2, 2s
    runs : a4, after d2, 1s
```

- Each action waits for the previous one to finish, and its delay counts from the start of the previous action, not from its end: above, the 3 s delay of Run program begins with HDR on, so the program starts 1 s after the 2 s display change. With a delay of 0, an action starts as soon as the previous one has finished.
- **Hold next actions until this program finishes** makes the next actions wait for that program to exit, 30 s at most by default (**Stop holding after**). A delay that has already passed when the program exits adds nothing: the next action starts at once.
- **Reference profile** inserts the actions of another profile for the same event, with their delays, as if they were written in its place. References can be nested 8 levels deep.
- When a profile starts, the profiles still running for another application or a keyboard shortcut finish their remaining actions at once, without their delays and holds, then the new profile runs. This waits 10 s at most.

## Requirements

- Windows 10 22H2 or Windows 11, 64-bit.
- .NET Framework 4.8, included in Windows 10 22H2 and Windows 11.
- An NVIDIA GPU for color depth, format and range.

## Usage

Download the zip from the Releases page, extract it and run AutoTrigger.exe. The program is not signed: if SmartScreen warns, choose **More info**, then **Run anyway**.

1. **Applications**: add the programs to follow.
2. **Profiles**: add actions to each event, then assign the profile to applications.
3. Leave AutoTrigger in the tray. Double-click opens it; right-click for Open, Pause, Start with Windows and Exit.

<img src="images/menu.png" alt="Tray menu: Open, Pause, Start with Windows, Exit">

### Moving from AutoActions

Settings, **Import from AutoActions**, then pick its `UserSettings.json`. Applications, profiles and displays are replaced; the previous AutoTrigger settings are backed up and AutoActions' file is not changed. The summary lists the programs the imported profiles will start. Quit AutoActions afterwards, or every action runs twice. AutoActions shortcuts and the default profile are not imported; an action type AutoTrigger does not know is kept as an unknown action, which does nothing.

### Full path or file name

Applications are matched by full path. Choose **File name only** when the folder changes over time, such as Game Pass games or programs that update into versioned folders.

### Keyboard shortcuts and command line

A profile can have two keyboard shortcuts (Ctrl or Alt, with or without Shift, plus a key; F13 to F24 also work alone): **Keyboard shortcut (Started)** runs its **Started** actions and **Keyboard shortcut (Closed)** its **Closed** actions, from anywhere. They also work while monitoring is paused. The Status page lists them all.

| Command | Result |
| --- | --- |
| `AutoTrigger.exe /pause` | Pause monitoring |
| `AutoTrigger.exe /resume` | Resume monitoring |
| `AutoTrigger.exe /toggle` | Pause or resume |
| `AutoTrigger.exe /started "Name"` | Run the Started actions of a profile |
| `AutoTrigger.exe /closed "Name"` | Run the Closed actions of a profile |

If AutoTrigger is not running, the command starts it and returns only when AutoTrigger exits. Sent to a running AutoTrigger, it returns 0 on success and 1 on failure. An unknown command returns 2.

### Hotkey limits

- One keyboard shortcut per action.
- The window in front receives the keys too.
- Windows blocks keys sent to a window run as administrator; the log names that program.
- If keys are still held down, such as the keyboard shortcut that ran the profile, the action waits up to 1 s for their release.
- No confirmation that the other program reacted; some games and anti-cheat systems ignore simulated keys.

### Log and uninstall

To report a problem, set **Log level** to **Detailed** in Settings, reproduce it, then attach `AutoTrigger.log` (**Open log**). Your user folder appears as `%USERPROFILE%`.

To uninstall, uncheck Start with Windows, choose Exit, then delete the AutoTrigger folder and `%LOCALAPPDATA%\AutoTrigger`.

## FAQ

**How do I turn HDR on automatically for one game?** Add the game in **Applications**, give it a profile with a **Display** action set to HDR on in **Started** and HDR off in **Closed**.

**Is this Windows Auto HDR?** No. Windows Auto HDR adds HDR to SDR games while HDR is already on. AutoTrigger turns HDR itself on and off, per game. Both can be used together.

**Does it work with Steam, Epic Games, GOG, Game Pass or emulators?** Yes. AutoTrigger follows the game's own program, whatever launched it. For Game Pass games, choose **File name only**.

**Can I run a profile without waiting for an app to start?** Yes. Give the profile a keyboard shortcut, or run `AutoTrigger.exe /started "Profile name"` or `/closed "Profile name"`, for example from a desktop shortcut. A **Run program** action in the profile can then start the program itself.

**Does it need administrator rights?** No.

**Can I keep my AutoActions setup?** Yes, see [Moving from AutoActions](#moving-from-autoactions).

## Build

Visual Studio 2026 with the **.NET desktop development** workload, from a Developer Command Prompt:

```
msbuild AutoTrigger.sln -restore -p:Configuration=Release
```

The program is written to `out\Release`. [WPF UI](https://github.com/lepoco/wpfui) 4.3.0 is downloaded from NuGet on the first build. The tests are written to `out\Tests`: run `AutoTrigger.Tests.exe`, exit code 0 means they all passed. GitHub builds and tests every change. How the code is organized: [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md).

## How it works

- **Starts**: the process list is read every 500 ms with `NtQuerySystemInformation`. **Closes**: signaled by Windows. **Focus**: a `WinEvent` hook; a change must last 300 ms. For Windows Store apps, the app inside the `ApplicationFrameHost` frame is used.
- **Display**: resolution with `SetDisplayConfig` and GPU scaling, HDR with `DisplayConfigSetDeviceInfo`, NVIDIA color with NVAPI. Only what differs is changed, and a failed step undoes the previous ones.
- **Windows settings** sets the default audio devices and uses `PowerSetActiveScheme` for the power plan. The screen saver is changed for the current session only, and turned back on when AutoTrigger exits. **Hotkey** uses `SendInput`.
- Keyboard shortcuts use `RegisterHotKey`. The command line talks to the running AutoTrigger through a local named pipe open to the current user only.
- Settings are in `%LOCALAPPDATA%\AutoTrigger\settings.json`, written atomically. Start with Windows uses the current user's `Run` key.
- **Close program** asks the program's windows to close, its hidden ones when none is visible, as `taskkill` does without `/F`. With **Force**, it ends the program once the delay is over.
- **Update check**: one HTTPS request to the GitHub API, shortly after start and then every 4 h by default, reads the number of the latest release. Nothing is downloaded, installed or sent. With **Check for updates** off in Settings, AutoTrigger doesn't use the network.

  <img src="images/tray-update.png" width="208" alt="Tray icon: normal, update available, paused, paused with an update available">

## AI disclosure

This app was built with the help of AI (Claude, by Anthropic) under the direction of the author.

## License

MIT, see [LICENSE](LICENSE). WPF UI and the .NET libraries are distributed under their MIT licenses and the NVAPI definitions under NVIDIA's MIT license, see [NOTICE.txt](NOTICE.txt).

NVIDIA is a trademark or registered trademark of NVIDIA Corporation.
