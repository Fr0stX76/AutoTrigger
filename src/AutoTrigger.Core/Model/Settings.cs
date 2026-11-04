using System;
using System.Collections.Generic;
using AutoTrigger.Core.Logging;

namespace AutoTrigger.Core.Model
{
    public enum EventKind
    {
        Started,
        Closed,
        GotFocus,
        LostFocus,
    }

    public static class EventNames
    {
        public static string Of(EventKind kind)
        {
            switch (kind)
            {
                case EventKind.Started: return "started";
                case EventKind.Closed: return "closed";
                case EventKind.GotFocus: return "gained focus";
                default: return "lost focus";
            }
        }
    }

    public enum ThemeMode
    {
        Auto,
        Light,
        Dark,
    }

    public sealed class AppSettings
    {
        public const int CurrentSchemaVersion = 1;
        public const int DefaultFocusStabilityMs = 300;
        public const int DefaultStartPollMs = 500;
        public const int DefaultUpdateCheckHours = 4;
        public const int MinUpdateCheckHours = 1;
        public const int MaxUpdateCheckHours = 168;

        public bool StartMinimized { get; set; }

        public bool CloseToTray { get; set; }

        public ThemeMode Theme { get; set; } = ThemeMode.Auto;

        // A language code such as "fr", or empty for the Windows display language.
        public string Language { get; set; } = string.Empty;

        public int FocusStabilityMs { get; set; } = DefaultFocusStabilityMs;

        public int StartPollMs { get; set; } = DefaultStartPollMs;

        public LogLevel LogLevel { get; set; } = LogLevel.Error;

        public bool CheckForUpdates { get; set; } = true;

        public int UpdateCheckHours { get; set; } = DefaultUpdateCheckHours;

        public bool MonitoringPaused { get; set; }

        public double WindowWidth { get; set; }

        public double WindowHeight { get; set; }

        public List<AppEntry> Applications { get; } = new List<AppEntry>();

        public List<Profile> Profiles { get; } = new List<Profile>();

        public List<DisplayEntry> Displays { get; } = new List<DisplayEntry>();

        public void ResetPreferences()
        {
            var defaults = new AppSettings();
            StartMinimized = defaults.StartMinimized;
            CloseToTray = defaults.CloseToTray;
            Theme = defaults.Theme;
            Language = defaults.Language;
            FocusStabilityMs = defaults.FocusStabilityMs;
            StartPollMs = defaults.StartPollMs;
            LogLevel = defaults.LogLevel;
            CheckForUpdates = defaults.CheckForUpdates;
            UpdateCheckHours = defaults.UpdateCheckHours;
        }

        public Profile FindProfile(Guid id)
        {
            if (id == Guid.Empty)
                return null;
            foreach (Profile profile in Profiles)
            {
                if (profile.Id == id)
                    return profile;
            }
            return null;
        }

        // Applications that used the profile keep running without it: no profile, and nothing to launch.
        public void RemoveProfile(Profile profile)
        {
            foreach (AppEntry app in Applications)
            {
                if (app.ProfileId == profile.Id)
                    app.ProfileId = Guid.Empty;
                if (app.Launch == LaunchKind.Profile && app.LaunchProfileId == profile.Id)
                {
                    app.Launch = LaunchKind.None;
                    app.LaunchProfileId = Guid.Empty;
                }
            }
            Profiles.Remove(profile);
        }

        public AppEntry FindApplication(Guid id)
        {
            foreach (AppEntry app in Applications)
            {
                if (app.Id == id)
                    return app;
            }
            return null;
        }
    }

    public enum LaunchKind
    {
        None,
        Program,
        Profile,
    }

    public sealed class AppEntry
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public string Name { get; set; } = string.Empty;

        public string Path { get; set; } = string.Empty;

        public bool MatchByFileName { get; set; }

        public Guid ProfileId { get; set; }

        public bool Enabled { get; set; } = true;

        // What the Launch button of the Status page runs. Program by default, as before 1.6; an application
        // added by hand starts with None, since a game started without its store launcher may not run.
        public LaunchKind Launch { get; set; } = LaunchKind.Program;

        public Guid LaunchProfileId { get; set; }

        public string FileName
        {
            get
            {
                try
                {
                    return System.IO.Path.GetFileName(Path ?? string.Empty);
                }
                catch (ArgumentException)
                {
                    return string.Empty;
                }
            }
        }
    }

    public sealed class Profile
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public string Name { get; set; } = string.Empty;

        public bool RestartApp { get; set; }

        public KeyCombination Shortcut { get; set; }

        public KeyCombination ClosedShortcut { get; set; }

        public KeyCombination ShortcutFor(EventKind kind) =>
            kind == EventKind.Started ? Shortcut : kind == EventKind.Closed ? ClosedShortcut : null;

        public void SetShortcut(EventKind kind, KeyCombination keys)
        {
            if (kind == EventKind.Started)
                Shortcut = keys;
            else if (kind == EventKind.Closed)
                ClosedShortcut = keys;
        }

        public List<ProfileAction> OnStart { get; } = new List<ProfileAction>();

        public List<ProfileAction> OnClose { get; } = new List<ProfileAction>();

        public List<ProfileAction> OnFocus { get; } = new List<ProfileAction>();

        public List<ProfileAction> OnBlur { get; } = new List<ProfileAction>();

        public List<ProfileAction> For(EventKind kind)
        {
            switch (kind)
            {
                case EventKind.Started: return OnStart;
                case EventKind.Closed: return OnClose;
                case EventKind.GotFocus: return OnFocus;
                default: return OnBlur;
            }
        }
    }

    public sealed class DisplayEntry
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public long LegacyUid { get; set; } = -1;

        public string DevicePath { get; set; } = string.Empty;

        public string FriendlyName { get; set; } = string.Empty;

        public bool Managed { get; set; } = true;

        public AutoTrigger.Core.Displays.DisplayCapabilities Known { get; set; }

        public bool IsLinked => !string.IsNullOrEmpty(DevicePath);
    }
}
