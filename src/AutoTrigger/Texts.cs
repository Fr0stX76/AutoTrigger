using System.Globalization;

namespace AutoTrigger
{
    internal static class Texts
    {
        public const string AppName = "AutoTrigger";

        public const string TabStatus = "Status";
        public const string Applications = "Applications";
        public const string Status = "Status";
        public const string Hdr = "HDR";
        public const string Monitoring = "Monitoring";
        public const string CurrentApplication = "Current application";
        public const string CurrentProfile = "Current profile";
        public const string Enabled = "Enabled";
        public const string Disabled = "Disabled";
        public const string NotAvailable = "Not available";
        public const string None = "None";
        public const string Launch = "Launch";

        public const string TabProfiles = "Profiles";
        public const string TabApplications = "Applications";
        public const string TabDisplays = "Displays";
        public const string TabSettings = "Settings";

        public const string Add = "Add";
        public const string Edit = "Edit";
        public const string Delete = "Delete";
        public const string Duplicate = "Duplicate";
        public const string CopyWord = "copy";
        public const string Save = "Save";
        public const string Cancel = "Cancel";
        public const string Select = "Select";
        public const string Browse = "Browse";
        public const string Search = "Search";
        public const string Refresh = "Refresh";
        public const string MoveUp = "Move up";
        public const string MoveDown = "Move down";
        public const string Name = "Name";
        public const string Profile = "Profile";
        public const string Program = "Program";
        public const string Application = "Application";
        public const string Icon = "Icon";
        public const string Type = "Type";
        public const string Yes = "Yes";
        public const string No = "No";
        public const string On = "On";
        public const string Unnamed = "(unnamed)";
        public const string NothingFound = "Nothing found";
        public const string Milliseconds = "ms";
        public const string Seconds = "s";
        public const string FilterExe = "Programs (*.exe)|*.exe";
        public const string FilterPrograms = "Programs (*.exe;*.bat;*.cmd;*.lnk)|*.exe;*.bat;*.cmd;*.lnk|All files (*.*)|*.*";

        public const string RunningPrograms = "Running programs";
        public const string ChooseRunning = "Choose from running programs";
        public const string ChooseAProgram = "Choose a program.";

        public const string EventStarted = "Started";
        public const string EventClosed = "Closed";
        public const string EventGotFocus = "Gained focus";
        public const string EventLostFocus = "Lost focus";
        public const string TypeRunProgram = "Run program";
        public const string TypeCloseProgram = "Close program";
        public const string TypeReferenceProfile = "Reference profile";
        public const string TypeDisplay = "Display";
        public const string TypeWindowsSettings = "Windows settings";
        public const string TypeHotkey = "Hotkey";
        public const string TypeUnknown = "Unknown action";
        public const string NoProgram = "No program";
        public const string MissingProfile = "Missing profile";
        public const string AudioOutput = "Audio output";
        public const string AudioInput = "Audio input";
        public const string NoChange = "No change";
        public const string NoActions = "No actions";
        public const string OneAction = "1 action";
        public const string ManyActions = "{0} actions";
        public const string Actions = "Actions";
        public const string AddAction = "Add action";
        public const string EditAction = "Edit action";
        public const string RemoveAction = "Remove action";
        public const string ActionNumber = "Action {0}";
        public const string Delay = "Delay";
        public const string ChooseProgramFile = "Choose a program";
        public const string Arguments = "Arguments";
        public const string HoldNext = "Hold next actions until this program finishes";
        public const string StopHoldingAfter = "Stop holding after";
        public const string CloseTargetHint = "Program name or full path";
        public const string ForceClose = "Force close";
        public const string ForceAfter = "Force close after";
        public const string NotAllowed = "not allowed";
        public const string ErrorNoProgram = "Choose a program.";
        public const string ErrorNoReference = "There is no other profile to reference.";
        public const string ErrorReferenceLoop = "This profile would end up running itself. Choose another profile.";
        public const string LoopIgnored = "Ignored: reference loop";
        public const string MissingIgnored = "Ignored: missing profile";
        public const string EditLater = "This action type is not known to this version of AutoTrigger.";

        public const string Unchanged = "Unchanged";
        public const string Maximum = "Maximum";
        public const string MaxHz = "Max Hz";
        public const string Off = "Off";
        public const string AllManagedDisplays = "All managed displays";
        public const string PrimaryShort = "primary";
        public const string NotConnectedShort = "not connected";
        public const string DefaultShort = "default";
        public const string MissingDisplay = "Missing display";
        public const string DesktopResolution = "Desktop resolution";
        public const string NvidiaColor = "NVIDIA color";
        public const string OutputFormat = "Output format";
        public const string DynamicRange = "Dynamic range";
        public const string NotOffered = "not offered";
        public const string AudioOutputDevice = "Audio output device";
        public const string AudioInputDevice = "Audio input device";
        public const string ErrorNothingToChange = "Choose at least one setting to change.";
        public const string MissingDisplayIgnored = "Ignored: missing display";
        public const string PowerPlan = "Power plan";
        public const string ActiveShort = "active";
        public const string NotFoundShort = "not found";
        public const string PowerPlanNotFound = "Power plan not found";
        public const string PressKeys = "Press the keys";
        public const string NoHotkey = "No keyboard shortcut";
        public const string ErrorNoHotkey = "Press the keyboard shortcut to send.";
        public const string ErrorHotkeyIsShortcut = "This is the keyboard shortcut of profile '{0}': pressing it would run that profile.";
        public const string ScreenSaver = "Screen saver";
        public const string ScreenSaverOn = "Screen saver on";
        public const string ScreenSaverOff = "Screen saver off";
        public const string KeyboardShortcut = "Keyboard shortcut";
        public const string KeyboardShortcutStarted = "Keyboard shortcut (Started)";
        public const string KeyboardShortcutClosed = "Keyboard shortcut (Closed)";
        public const string KeyboardShortcuts = "Keyboard shortcuts";
        public const string ErrorShortcutModifier = "Use Ctrl or Alt, with or without Shift.";
        public const string ErrorShortcutAltGr = "With your keyboard layout, this combination types a character (AltGr).";
        public const string ErrorShortcutTaken = "Profile '{0}' already uses this keyboard shortcut ({1}).";
        public const string ErrorShortcutIsHotkey = "A 'Hotkey' action in profile '{0}' sends these keys.";
        public const string ShortcutInUse = "Another program already uses this keyboard shortcut ({1}).";

        public const string SearchProfiles = "Search profiles";
        public const string SearchApplications = "Search applications";
        public const string Sort = "Sort";
        public const string SortAscending = "A → Z";
        public const string SortDescending = "Z → A";
        public const string NoProfiles = "No profiles yet.";
        public const string NoProfileSelected = "No profile selected.";
        public const string NewProfileName = "New profile";
        public const string RestartApp = "Restart app";

        public const string AddApplication = "Add application";
        public const string EditApplication = "Edit application";
        public const string ChooseExe = "Choose an .exe file";
        public const string DisplayName = "Display name";
        public const string NameFromProgram = "From the program";
        public const string RecognizeBy = "Recognize by";
        public const string FullPath = "Full path";
        public const string FileNameOnly = "File name only";
        public const string ErrorChooseExe = "Choose an .exe file.";
        public const string ErrorFileMissing = "This file does not exist.";
        public const string NoApplicationsYet = "No applications yet.";

        public const string NoDisplays = "No displays found.";
        public const string Display = "Display";
        public const string ImportedDisplay = "Display (UID {0})";
        public const string Managed = "Managed";
        public const string State = "State";
        public const string NotConnected = "Not connected";
        public const string NotLinkedYet = "Not linked yet";
        public const string WindowsName = "Windows name";
        public const string GraphicsCard = "Graphics card";
        public const string Resolution = "Resolution";
        public const string RefreshRate = "Refresh rate";
        public const string ColorDepth = "Color depth";
        public const string FormatAndRange = "Format and range";
        public const string OffSupported = "Off (supported)";
        public const string NotSupported = "Not supported";
        public const string Primary = "Primary";
        public const string MonitorPath = "Monitor path";

        public const string StartMinimized = "Start minimized to tray";
        public const string CloseToTray = "Close to tray";
        public const string Theme = "Theme";
        public const string ThemeAuto = "Use Windows setting";
        public const string ThemeLight = "Light";
        public const string ThemeDark = "Dark";
        public const string FocusStability = "Focus stability";
        public const string StartDetectionInterval = "Start detection interval";
        public const string LogLevel = "Log level";
        public const string LogErrors = "Errors only";
        public const string LogWarnings = "Warnings";
        public const string LogInfo = "Information";
        public const string LogDetailed = "Detailed";
        public const string LogFile = "Log file";
        public const string ResetToDefaults = "Reset to defaults";
        public const string Reset = "Reset";

        public const string About = "About";
        public const string OpenLog = "Open log";
        public const string Exit = "Exit";
        public const string Open = "Open";
        public const string Pause = "Pause";
        public const string StartWithWindows = "Start with Windows";

        public const string Close = "Close";
        public const string Ok = "OK";
        public const string OpenLicenses = "Open licenses";
        public const string GitHub = "GitHub";
        public const string RepositoryUrl = "https://github.com/Fr0stX76/AutoTrigger";
        public const string SettingsProblemTitle = "Settings";
        public const string LaunchFailedTitle = "Launch";
        public const string LaunchFailed = "{0} cannot be started: {1}";
        public const string HdrFailedTitle = "HDR";
        public const string HdrFailed = "HDR could not be changed: {0}";
        public const string StartupFailed = "Start with Windows could not be changed: {0}";
        public const string AboutText =
            "Version {0}\n" +
            "Copyright © 2026 Fr0stX76. MIT License.\n\n" +
            "Uses WPF UI (MIT License), Copyright © Leszek Pomianowski and WPF UI Contributors.\n\n" +
            "The license texts are in LICENSE and NOTICE.txt, next to AutoTrigger.exe.";
        public const string ImportFromAutoActions = "Import from AutoActions";
        public const string Import = "Import";
        public const string ImportFilter = "UserSettings.json|UserSettings.json|JSON (*.json)|*.json";
        public const string ImportReplaceText = "Replace your applications, profiles and displays with the ones from AutoActions? A copy of your current settings is kept.";
        public const string Replace = "Replace";
        public const string ImportFailed = "This file cannot be imported: {0}.";
        public const string ImportDone = "Imported {0} applications, {1} profiles ({2} actions) and {3} displays.";
        public const string ImportNotes = "Notes:";
        public const string ImportPrograms = "Programs these profiles will start:";
        public const string ImportMoreNotes = "…and {0} more.";
        public const string ImportQuitAutoActions = "AutoActions is still running: quit it so that actions do not run twice.";
        public const string ImportAutoStart = "AutoActions starts with Windows: turn that off in AutoActions, and turn on Start with Windows here.";
        public const string StartupError = "AutoTrigger could not start. Details are in the log:\n{0}";
        public const string FatalError = "AutoTrigger has to close after an unexpected error. Details are in the log:\n{0}";
        public const string Paused = "AutoTrigger (paused)";

        public static string Format(string format, params object[] args) => string.Format(CultureInfo.CurrentCulture, format, args);
    }
}
