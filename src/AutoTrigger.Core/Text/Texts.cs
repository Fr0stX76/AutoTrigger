using System.Globalization;

namespace AutoTrigger.Core.Text
{
    // Every text of the window, read from the active language; keys match lang/en.json, which the tests check.
    public static class Texts
    {
        // Names, units and the import filter stay the same in every language.
        public const string AppName = "AutoTrigger";
        public const string Hdr = "HDR";
        public const string GitHub = "GitHub";
        public const string Milliseconds = "ms";
        public const string Seconds = "s";
        public const string Hours = "h";
        public const string ImportFilter = "UserSettings.json|UserSettings.json|JSON (*.json)|*.json";
        public const string RepositoryUrl = "https://github.com/" + Platform.UpdateCheck.Repository;

        public static string TabStatus => Get(nameof(TabStatus));
        public static string TabApplications => Get(nameof(TabApplications));
        public static string TabProfiles => Get(nameof(TabProfiles));
        public static string TabDisplays => Get(nameof(TabDisplays));
        public static string TabSettings => Get(nameof(TabSettings));
        public static string About => Get(nameof(About));
        public static string UpdateAvailable(string version) => Fill(nameof(UpdateAvailable), "version", version);

        public static string Applications => Get(nameof(Applications));
        public static string Status => Get(nameof(Status));
        public static string Monitoring => Get(nameof(Monitoring));
        public static string CurrentApplication => Get(nameof(CurrentApplication));
        public static string CurrentProfile => Get(nameof(CurrentProfile));
        public static string StatusHdrOn => Get(nameof(StatusHdrOn));
        public static string StatusHdrOff => Get(nameof(StatusHdrOff));
        public static string StatusHdrUnavailable => Get(nameof(StatusHdrUnavailable));
        public static string StatusMonitoringOn => Get(nameof(StatusMonitoringOn));
        public static string StatusMonitoringOff => Get(nameof(StatusMonitoringOff));
        public static string StatusNoApplication => Get(nameof(StatusNoApplication));
        public static string StatusNoProfile => Get(nameof(StatusNoProfile));
        public static string StatusNoShortcuts => Get(nameof(StatusNoShortcuts));
        public static string KeyboardShortcuts => Get(nameof(KeyboardShortcuts));
        public static string Launch => Get(nameof(Launch));
        public static string NoApplicationsYet => Get(nameof(NoApplicationsYet));
        public static string CreateDesktopShortcut => Get(nameof(CreateDesktopShortcut));

        public static string Add => Get(nameof(Add));
        public static string Edit => Get(nameof(Edit));
        public static string Delete => Get(nameof(Delete));
        public static string Duplicate => Get(nameof(Duplicate));
        public static string CopyWord => Get(nameof(CopyWord));
        public static string Save => Get(nameof(Save));
        public static string Cancel => Get(nameof(Cancel));
        public static string Select => Get(nameof(Select));
        public static string Browse => Get(nameof(Browse));
        public static string Search => Get(nameof(Search));
        public static string Refresh => Get(nameof(Refresh));
        public static string MoveUp => Get(nameof(MoveUp));
        public static string MoveDown => Get(nameof(MoveDown));
        public static string Name => Get(nameof(Name));
        public static string Profile => Get(nameof(Profile));
        public static string Program => Get(nameof(Program));
        public static string Application => Get(nameof(Application));
        public static string Icon => Get(nameof(Icon));
        public static string Type => Get(nameof(Type));
        public static string Yes => Get(nameof(Yes));
        public static string No => Get(nameof(No));
        public static string Unnamed => Get(nameof(Unnamed));
        public static string NothingFound => Get(nameof(NothingFound));
        public static string Optional => Get(nameof(Optional));
        public static string Close => Get(nameof(Close));
        public static string Ok => Get(nameof(Ok));
        public static string Replace => Get(nameof(Replace));
        public static string Reset => Get(nameof(Reset));
        public static string Sort => Get(nameof(Sort));
        public static string SortAscending => Get(nameof(SortAscending));
        public static string SortDescending => Get(nameof(SortDescending));
        public static string FileTypePrograms => Get(nameof(FileTypePrograms));
        public static string FileTypeAllFiles => Get(nameof(FileTypeAllFiles));

        public static string EventStarted => Get(nameof(EventStarted));
        public static string EventClosed => Get(nameof(EventClosed));
        public static string EventGotFocus => Get(nameof(EventGotFocus));
        public static string EventLostFocus => Get(nameof(EventLostFocus));
        public static string ActionCount(int count) => Fill(nameof(ActionCount), "count", Number(count));
        public static string NoActions => Get(nameof(NoActions));

        public static string SearchApplications => Get(nameof(SearchApplications));
        public static string ApplicationMonitoringOn => Get(nameof(ApplicationMonitoringOn));
        public static string ApplicationMonitoringOff => Get(nameof(ApplicationMonitoringOff));
        public static string LaunchButton => Get(nameof(LaunchButton));
        public static string LaunchNone => Get(nameof(LaunchNone));
        public static string ProfileNone => Get(nameof(ProfileNone));
        public static string AddApplication => Get(nameof(AddApplication));
        public static string EditApplication => Get(nameof(EditApplication));
        public static string ChooseExe => Get(nameof(ChooseExe));
        public static string ErrorChooseExe => Get(nameof(ErrorChooseExe));
        public static string DisplayName => Get(nameof(DisplayName));
        public static string NameFromProgram => Get(nameof(NameFromProgram));
        public static string RecognizeBy => Get(nameof(RecognizeBy));
        public static string FullPath => Get(nameof(FullPath));
        public static string FileNameOnly => Get(nameof(FileNameOnly));
        public static string ErrorFileMissing => Get(nameof(ErrorFileMissing));
        public static string RunningPrograms => Get(nameof(RunningPrograms));
        public static string ChooseRunning => Get(nameof(ChooseRunning));
        public static string ChooseAProgram => Get(nameof(ChooseAProgram));
        public static string ChooseProgramFile => Get(nameof(ChooseProgramFile));

        public static string SearchProfiles => Get(nameof(SearchProfiles));
        public static string NoProfiles => Get(nameof(NoProfiles));
        public static string NoProfileSelected => Get(nameof(NoProfileSelected));
        public static string NewProfileName => Get(nameof(NewProfileName));
        public static string RestartApp => Get(nameof(RestartApp));
        public static string KeyboardShortcut => Get(nameof(KeyboardShortcut));
        public static string KeyboardShortcutStarted => Get(nameof(KeyboardShortcutStarted));
        public static string KeyboardShortcutClosed => Get(nameof(KeyboardShortcutClosed));
        public static string Actions => Get(nameof(Actions));
        public static string AddAction => Get(nameof(AddAction));
        public static string EditAction => Get(nameof(EditAction));
        public static string RemoveAction => Get(nameof(RemoveAction));
        public static string ActionNumber(int number) => Fill(nameof(ActionNumber), "number", Number(number));
        public static string Delay => Get(nameof(Delay));

        public static string TypeRunProgram => Get(nameof(TypeRunProgram));
        public static string TypeCloseProgram => Get(nameof(TypeCloseProgram));
        public static string TypeReferenceProfile => Get(nameof(TypeReferenceProfile));
        public static string TypeDisplay => Get(nameof(TypeDisplay));
        public static string TypeWindowsSettings => Get(nameof(TypeWindowsSettings));
        public static string TypeHotkey => Get(nameof(TypeHotkey));
        public static string TypeUnknown => Get(nameof(TypeUnknown));
        public static string EditLater => Get(nameof(EditLater));

        public static string Arguments => Get(nameof(Arguments));
        public static string ArgumentsNone => Get(nameof(ArgumentsNone));
        public static string HideConsoleWindow => Get(nameof(HideConsoleWindow));
        public static string SkipIfRunning => Get(nameof(SkipIfRunning));
        public static string HoldNext => Get(nameof(HoldNext));
        public static string StopHoldingAfter => Get(nameof(StopHoldingAfter));
        public static string NoProgram => Get(nameof(NoProgram));
        public static string ErrorNoProgram => Get(nameof(ErrorNoProgram));
        public static string CloseTargetHint => Get(nameof(CloseTargetHint));
        public static string ForceClose => Get(nameof(ForceClose));
        public static string ForceAfter => Get(nameof(ForceAfter));
        public static string NotAllowed => Get(nameof(NotAllowed));

        public static string MissingProfile => Get(nameof(MissingProfile));
        public static string ErrorNoReference => Get(nameof(ErrorNoReference));
        public static string ErrorReferenceLoop => Get(nameof(ErrorReferenceLoop));
        public static string LoopIgnored => Get(nameof(LoopIgnored));
        public static string MissingIgnored => Get(nameof(MissingIgnored));

        public static string HdrChoiceUnchanged => Get(nameof(HdrChoiceUnchanged));
        public static string HdrChoiceOn => Get(nameof(HdrChoiceOn));
        public static string HdrChoiceOff => Get(nameof(HdrChoiceOff));
        public static string HdrOn => Get(nameof(HdrOn));
        public static string HdrOff => Get(nameof(HdrOff));
        public static string DesktopResolution => Get(nameof(DesktopResolution));
        public static string ResolutionUnchanged => Get(nameof(ResolutionUnchanged));
        public static string RefreshRateUnchanged => Get(nameof(RefreshRateUnchanged));
        public static string Maximum => Get(nameof(Maximum));
        public static string MaxHz => Get(nameof(MaxHz));
        public static string NvidiaColor => Get(nameof(NvidiaColor));
        public static string OutputFormat => Get(nameof(OutputFormat));
        public static string OutputFormatUnchanged => Get(nameof(OutputFormatUnchanged));
        public static string ColorDepthUnchanged => Get(nameof(ColorDepthUnchanged));
        public static string DynamicRange => Get(nameof(DynamicRange));
        public static string DynamicRangeUnchanged => Get(nameof(DynamicRangeUnchanged));
        public static string RangeFull => Get(nameof(RangeFull));
        public static string RangeLimited => Get(nameof(RangeLimited));
        public static string NotOffered(string value) => Fill(nameof(NotOffered), "value", value);
        public static string AllManagedDisplays => Get(nameof(AllManagedDisplays));
        public static string PrimaryShort => Get(nameof(PrimaryShort));
        public static string NotConnectedShort => Get(nameof(NotConnectedShort));
        public static string MissingDisplay => Get(nameof(MissingDisplay));
        public static string MissingDisplayIgnored => Get(nameof(MissingDisplayIgnored));
        public static string ErrorNothingToChange => Get(nameof(ErrorNothingToChange));

        public static string AudioOutputDevice => Get(nameof(AudioOutputDevice));
        public static string AudioInputDevice => Get(nameof(AudioInputDevice));
        public static string AudioOutputUnchanged => Get(nameof(AudioOutputUnchanged));
        public static string AudioInputUnchanged => Get(nameof(AudioInputUnchanged));
        public static string DefaultShort => Get(nameof(DefaultShort));
        public static string AudioOutput => Get(nameof(AudioOutput));
        public static string AudioInput => Get(nameof(AudioInput));
        public static string NoChange => Get(nameof(NoChange));
        public static string PowerPlan => Get(nameof(PowerPlan));
        public static string PowerPlanUnchanged => Get(nameof(PowerPlanUnchanged));
        public static string ActiveShort => Get(nameof(ActiveShort));
        public static string PowerPlanMissing(string name) => Fill(nameof(PowerPlanMissing), "name", name);
        public static string PowerPlanNotFound => Get(nameof(PowerPlanNotFound));
        public static string ScreenSaver => Get(nameof(ScreenSaver));
        public static string ScreenSaverChoiceUnchanged => Get(nameof(ScreenSaverChoiceUnchanged));
        public static string ScreenSaverChoiceOn => Get(nameof(ScreenSaverChoiceOn));
        public static string ScreenSaverChoiceOff => Get(nameof(ScreenSaverChoiceOff));
        public static string ScreenSaverOn => Get(nameof(ScreenSaverOn));
        public static string ScreenSaverOff => Get(nameof(ScreenSaverOff));

        public static string PressKeys => Get(nameof(PressKeys));
        public static string NoHotkey => Get(nameof(NoHotkey));
        public static string ErrorNoHotkey => Get(nameof(ErrorNoHotkey));
        public static string ErrorHotkeyIsShortcut(string profile) => Fill(nameof(ErrorHotkeyIsShortcut), "profile", profile);
        public static string ErrorShortcutModifier => Get(nameof(ErrorShortcutModifier));
        public static string ErrorShortcutAltGr => Get(nameof(ErrorShortcutAltGr));
        public static string ErrorShortcutTaken(string profile, string @event) => Fill(nameof(ErrorShortcutTaken), "profile", profile, "event", @event);
        public static string ErrorShortcutIsHotkey(string profile) => Fill(nameof(ErrorShortcutIsHotkey), "profile", profile);
        public static string ShortcutInUse => Get(nameof(ShortcutInUse));

        public static string NoDisplays => Get(nameof(NoDisplays));
        public static string Display => Get(nameof(Display));
        public static string ImportedDisplay(long uid) => Fill(nameof(ImportedDisplay), "uid", Number(uid));
        public static string Managed => Get(nameof(Managed));
        public static string State => Get(nameof(State));
        public static string NotConnected => Get(nameof(NotConnected));
        public static string NotLinkedYet => Get(nameof(NotLinkedYet));
        public static string WindowsName => Get(nameof(WindowsName));
        public static string GraphicsCard => Get(nameof(GraphicsCard));
        public static string Resolution => Get(nameof(Resolution));
        public static string RefreshRate => Get(nameof(RefreshRate));
        public static string ColorDepth => Get(nameof(ColorDepth));
        public static string FormatAndRange => Get(nameof(FormatAndRange));
        public static string DisplayHdrOn => Get(nameof(DisplayHdrOn));
        public static string DisplayHdrOffSupported => Get(nameof(DisplayHdrOffSupported));
        public static string DisplayHdrNotSupported => Get(nameof(DisplayHdrNotSupported));
        public static string Primary => Get(nameof(Primary));
        public static string MonitorPath => Get(nameof(MonitorPath));

        public static string General => Get(nameof(General));
        public static string Advanced => Get(nameof(Advanced));
        public static string StartWithWindows => Get(nameof(StartWithWindows));
        public static string StartMinimized => Get(nameof(StartMinimized));
        public static string CloseToTray => Get(nameof(CloseToTray));
        public static string Theme => Get(nameof(Theme));
        public static string ThemeAuto => Get(nameof(ThemeAuto));
        public static string ThemeLight => Get(nameof(ThemeLight));
        public static string ThemeDark => Get(nameof(ThemeDark));
        public static string Language => Get(nameof(Language));
        public static string LanguageSystem => Get(nameof(LanguageSystem));
        public static string LanguageRestart => Get(nameof(LanguageRestart));
        public static string RestartNow => Get(nameof(RestartNow));
        public static string MinimumFocusTime => Get(nameof(MinimumFocusTime));
        public static string StartDetectionInterval => Get(nameof(StartDetectionInterval));
        public static string CheckForUpdates => Get(nameof(CheckForUpdates));
        public static string CheckEvery => Get(nameof(CheckEvery));
        public static string LogLevel => Get(nameof(LogLevel));
        public static string LogErrors => Get(nameof(LogErrors));
        public static string LogWarnings => Get(nameof(LogWarnings));
        public static string LogInfo => Get(nameof(LogInfo));
        public static string LogDetailed => Get(nameof(LogDetailed));
        public static string LogFile => Get(nameof(LogFile));
        public static string OpenLog => Get(nameof(OpenLog));
        public static string ImportFromAutoActions => Get(nameof(ImportFromAutoActions));
        public static string Import => Get(nameof(Import));
        public static string ResetToDefaults => Get(nameof(ResetToDefaults));

        public static string Open => Get(nameof(Open));
        public static string Pause => Get(nameof(Pause));
        public static string Exit => Get(nameof(Exit));
        public static string Paused => Get(nameof(Paused));

        public static string OpenLicenses => Get(nameof(OpenLicenses));
        public static string AboutText(string version) => Fill(nameof(AboutText), "version", version);

        public static string SettingsProblemTitle => Get(nameof(SettingsProblemTitle));
        public static string LaunchFailedTitle => Get(nameof(LaunchFailedTitle));
        public static string LaunchFailed(string name, string reason) => Fill(nameof(LaunchFailed), "name", name, "reason", reason);
        public static string HdrFailed(string reason) => Fill(nameof(HdrFailed), "reason", reason);
        public static string StartupFailed(string reason) => Fill(nameof(StartupFailed), "reason", reason);
        public static string ShortcutFailed(string reason) => Fill(nameof(ShortcutFailed), "reason", reason);
        public static string NoDesktopFolder => Get(nameof(NoDesktopFolder));
        public static string ShortcutNamesTaken => Get(nameof(ShortcutNamesTaken));
        public static string RestartFailed(string reason) => Fill(nameof(RestartFailed), "reason", reason);
        public static string StartupError(string path) => Fill(nameof(StartupError), "path", path);
        public static string FatalError(string path) => Fill(nameof(FatalError), "path", path);
        public static string SettingsLocked => Get(nameof(SettingsLocked));
        public static string SettingsUnreadable => Get(nameof(SettingsUnreadable));
        public static string SettingsSetAside(string file) => Fill(nameof(SettingsSetAside), "file", file);

        public static string ImportReplaceText => Get(nameof(ImportReplaceText));
        public static string ImportFailed(string reason) => Fill(nameof(ImportFailed), "reason", reason);
        public static string ImportNotJson(string detail) => Fill(nameof(ImportNotJson), "detail", detail);
        public static string ImportNotAutoActions => Get(nameof(ImportNotAutoActions));
        public static string ImportedHeading => Get(nameof(ImportedHeading));
        public static string ImportedApplications(int count) => Fill(nameof(ImportedApplications), "count", Number(count));
        public static string ImportedProfiles(int count) => Fill(nameof(ImportedProfiles), "count", Number(count));
        public static string ImportedActions(int count) => Fill(nameof(ImportedActions), "count", Number(count));
        public static string ImportedDisplays(int count) => Fill(nameof(ImportedDisplays), "count", Number(count));
        public static string ImportNotes => Get(nameof(ImportNotes));
        public static string ImportPrograms => Get(nameof(ImportPrograms));
        public static string ImportMoreNotes(int count) => Fill(nameof(ImportMoreNotes), "count", Number(count));
        public static string ImportQuitAutoActions => Get(nameof(ImportQuitAutoActions));
        public static string ImportAutoStart => Get(nameof(ImportAutoStart));
        public static string ImportProfileNewId(string name) => Fill(nameof(ImportProfileNewId), "name", name);
        public static string ImportProfileSameId(string name) => Fill(nameof(ImportProfileSameId), "name", name);
        public static string ImportAppNoPath(string name) => Fill(nameof(ImportAppNoPath), "name", name);
        public static string ImportAppStore(string name, string path) => Fill(nameof(ImportAppStore), "name", name, "path", path);
        public static string ImportAppNoProfile(string name) => Fill(nameof(ImportAppNoProfile), "name", name);
        public static string ImportShortcutsSkipped(int count) => Fill(nameof(ImportShortcutsSkipped), "count", Number(count));
        public static string ImportRunNoProgram(string profile) => Fill(nameof(ImportRunNoProgram), "profile", profile);
        public static string ImportUnknownType(string profile, string type) => Fill(nameof(ImportUnknownType), "profile", profile, "type", type);
        public static string ImportUnknownDepth(string profile, int depth) => Fill(nameof(ImportUnknownDepth), "profile", profile, "depth", Number(depth));
        public static string ImportBadResolution(string profile, string text) => Fill(nameof(ImportBadResolution), "profile", profile, "text", text);

        public static string IssueApplicationProfile(string name) => Fill(nameof(IssueApplicationProfile), "name", name);
        public static string IssueDisplay(string profile, string @event) => Fill(nameof(IssueDisplay), "profile", profile, "event", @event);
        public static string IssueReference(string profile, string @event) => Fill(nameof(IssueReference), "profile", profile, "event", @event);
        public static string IssueLoop(string profile, string @event, string target) => Fill(nameof(IssueLoop), "profile", profile, "event", @event, "target", target);

        private static string Get(string key) => TextCatalog.Active.Get(key);

        private static string Fill(string key, params string[] namesAndValues) => TextCatalog.Active.Fill(key, namesAndValues);

        private static string Number(long value) => value.ToString(CultureInfo.InvariantCulture);
    }
}
