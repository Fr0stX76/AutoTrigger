using System;
using System.Collections.Generic;
using System.Globalization;
using AutoTrigger.Core.Displays;
using AutoTrigger.Core.Json;
using AutoTrigger.Core.Logging;

namespace AutoTrigger.Core.Model
{
    public static partial class SettingsJson
    {
        private const double MaxWindowSize = 20000;

        public static AppSettings Read(JsonValue root, List<string> warnings)
        {
            if (root == null || root.Kind != JsonKind.Object)
                throw new FormatException("The settings file does not contain a JSON object.");
            warnings = warnings ?? new List<string>();

            int schema = (int)Num(root, "schemaVersion", AppSettings.CurrentSchemaVersion);
            if (schema > AppSettings.CurrentSchemaVersion)
                warnings.Add($"settings.json was written by a newer version (schema {schema}); unknown parts are ignored.");

            AppSettings settings = ReadPreferences(root);
            ReadApplications(root, settings);
            ReadProfiles(root, settings, warnings);
            ReadDisplays(root, settings);
            RepairDuplicateIds(settings, warnings);
            return settings;
        }

        private static AppSettings ReadPreferences(JsonValue root)
        {
            var settings = new AppSettings
            {
                StartMinimized = Bool(root, "startMinimized", false),
                CloseToTray = Bool(root, "closeToTray", false),
                Theme = ParseTheme(Str(root, "theme", "auto")),
                FocusStabilityMs = Clamp((int)Num(root, "focusStabilityMs", AppSettings.DefaultFocusStabilityMs), 0, 5000),
                StartPollMs = Clamp((int)Num(root, "startPollMs", AppSettings.DefaultStartPollMs), 50, 5000),
                LogLevel = Log.ParseLevel(Str(root, "logLevel", "errors"), LogLevel.Error),
                MonitoringPaused = Bool(root, "paused", false),
                CheckForUpdates = Bool(root, "checkForUpdates", true),
                UpdateCheckHours = Clamp((int)Num(root, "updateCheckHours", AppSettings.DefaultUpdateCheckHours),
                    AppSettings.MinUpdateCheckHours, AppSettings.MaxUpdateCheckHours),
            };
            JsonValue window = root["window"];
            if (window != null && window.Kind == JsonKind.Object)
            {
                settings.WindowWidth = Math.Max(0, Math.Min(MaxWindowSize, Num(window, "width", 0)));
                settings.WindowHeight = Math.Max(0, Math.Min(MaxWindowSize, Num(window, "height", 0)));
            }
            return settings;
        }

        private static void ReadApplications(JsonValue root, AppSettings settings)
        {
            foreach (JsonValue app in Array(root, "applications"))
            {
                if (app.Kind != JsonKind.Object)
                    continue;
                settings.Applications.Add(new AppEntry
                {
                    Id = GuidOrNew(app, "id"),
                    Name = Str(app, "name", string.Empty),
                    Path = Str(app, "path", string.Empty),
                    MatchByFileName = string.Equals(Str(app, "matchBy", "path"), "fileName", StringComparison.OrdinalIgnoreCase),
                    ProfileId = GuidOf(app, "profileId"),
                    Enabled = Bool(app, "enabled", true),
                });
            }
        }

        private static void ReadProfiles(JsonValue root, AppSettings settings, List<string> warnings)
        {
            foreach (JsonValue item in Array(root, "profiles"))
            {
                if (item.Kind != JsonKind.Object)
                    continue;
                var profile = new Profile
                {
                    Id = GuidOrNew(item, "id"),
                    Name = Str(item, "name", string.Empty),
                    RestartApp = Bool(item, "restartApp", false),
                    Shortcut = ReadShortcut(item, "shortcut"),
                    ClosedShortcut = ReadShortcut(item, "closedShortcut"),
                };
                ReadActions(item, "onStart", profile.OnStart, profile.Name, warnings);
                ReadActions(item, "onClose", profile.OnClose, profile.Name, warnings);
                ReadActions(item, "onFocus", profile.OnFocus, profile.Name, warnings);
                ReadActions(item, "onFocusLost", profile.OnBlur, profile.Name, warnings);
                settings.Profiles.Add(profile);
            }
        }

        private static void ReadDisplays(JsonValue root, AppSettings settings)
        {
            foreach (JsonValue display in Array(root, "displays"))
            {
                if (display.Kind != JsonKind.Object)
                    continue;
                settings.Displays.Add(new DisplayEntry
                {
                    Id = GuidOrNew(display, "id"),
                    LegacyUid = (long)Num(display, "legacyUid", -1),
                    DevicePath = Str(display, "devicePath", string.Empty),
                    FriendlyName = Str(display, "friendlyName", string.Empty),
                    Managed = Bool(display, "managed", true),
                    Known = ReadCapabilities(display["known"]),
                });
            }
        }

        private static void RepairDuplicateIds(AppSettings settings, List<string> warnings)
        {
            var seen = new HashSet<Guid>();
            foreach (AppEntry app in settings.Applications)
            {
                if (seen.Add(app.Id))
                    continue;
                app.Id = Guid.NewGuid();
                warnings.Add($"Application '{app.Name}' had the same id as another application; it was given a new id.");
            }

            seen.Clear();
            foreach (Profile profile in settings.Profiles)
            {
                if (seen.Add(profile.Id))
                    continue;
                profile.Id = Guid.NewGuid();
                warnings.Add($"Profile '{profile.Name}' had the same id as another profile; it was given a new id.");
            }

            seen.Clear();
            foreach (DisplayEntry display in settings.Displays)
            {
                if (seen.Add(display.Id))
                    continue;
                display.Id = Guid.NewGuid();
                warnings.Add($"Display '{display.FriendlyName}' had the same id as another display; it was given a new id.");
            }
        }

        public static JsonValue Write(AppSettings settings)
        {
            return JsonValue.NewObject()
                .Set("schemaVersion", AppSettings.CurrentSchemaVersion)
                .Set("startMinimized", settings.StartMinimized)
                .Set("closeToTray", settings.CloseToTray)
                .Set("theme", ThemeToText(settings.Theme))
                .Set("focusStabilityMs", settings.FocusStabilityMs)
                .Set("startPollMs", settings.StartPollMs)
                .Set("logLevel", Log.LevelToText(settings.LogLevel))
                .Set("paused", settings.MonitoringPaused)
                .Set("checkForUpdates", settings.CheckForUpdates)
                .Set("updateCheckHours", settings.UpdateCheckHours)
                .Set("window", JsonValue.NewObject()
                    .Set("width", Math.Round(settings.WindowWidth))
                    .Set("height", Math.Round(settings.WindowHeight)))
                .Set("applications", WriteApplications(settings.Applications))
                .Set("profiles", WriteProfiles(settings.Profiles))
                .Set("displays", WriteDisplays(settings.Displays));
        }

        private static JsonValue WriteApplications(List<AppEntry> applications)
        {
            var list = JsonValue.NewArray();
            foreach (AppEntry app in applications)
            {
                list.Add(JsonValue.NewObject()
                    .Set("id", app.Id.ToString("D"))
                    .Set("name", app.Name ?? string.Empty)
                    .Set("path", app.Path ?? string.Empty)
                    .Set("matchBy", app.MatchByFileName ? "fileName" : "path")
                    .Set("profileId", GuidValue(app.ProfileId))
                    .Set("enabled", app.Enabled));
            }
            return list;
        }

        private static JsonValue WriteProfiles(List<Profile> profiles)
        {
            var list = JsonValue.NewArray();
            foreach (Profile profile in profiles)
            {
                list.Add(JsonValue.NewObject()
                    .Set("id", profile.Id.ToString("D"))
                    .Set("name", profile.Name ?? string.Empty)
                    .Set("restartApp", profile.RestartApp)
                    .Set("shortcut", WriteShortcut(profile.Shortcut))
                    .Set("closedShortcut", WriteShortcut(profile.ClosedShortcut))
                    .Set("onStart", WriteActions(profile.OnStart))
                    .Set("onClose", WriteActions(profile.OnClose))
                    .Set("onFocus", WriteActions(profile.OnFocus))
                    .Set("onFocusLost", WriteActions(profile.OnBlur)));
            }
            return list;
        }

        private static JsonValue WriteDisplays(List<DisplayEntry> displays)
        {
            var list = JsonValue.NewArray();
            foreach (DisplayEntry display in displays)
            {
                list.Add(JsonValue.NewObject()
                    .Set("id", display.Id.ToString("D"))
                    .Set("legacyUid", display.LegacyUid)
                    .Set("devicePath", display.DevicePath ?? string.Empty)
                    .Set("friendlyName", display.FriendlyName ?? string.Empty)
                    .Set("managed", display.Managed)
                    .Set("known", WriteCapabilities(display.Known)));
            }
            return list;
        }

        private static IEnumerable<JsonValue> Array(JsonValue obj, string key)
        {
            JsonValue value = obj[key];
            return value != null && value.Kind == JsonKind.Array ? value.Items : System.Array.Empty<JsonValue>();
        }

        private static string Str(JsonValue obj, string key, string fallback)
        {
            JsonValue value = obj[key];
            return value != null && value.Kind == JsonKind.String ? value.AsString() : fallback;
        }

        private static bool Bool(JsonValue obj, string key, bool fallback)
        {
            return obj[key]?.AsBool() ?? fallback;
        }

        private static double Num(JsonValue obj, string key, double fallback)
        {
            JsonValue value = obj[key];
            if (value == null)
                return fallback;
            if (value.Kind == JsonKind.Number)
                return value.AsNumber().Value;
            if (value.Kind == JsonKind.String
                && double.TryParse(value.AsString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double number))
                return number;
            return fallback;
        }

        private static Guid GuidOf(JsonValue obj, string key)
        {
            string text = Str(obj, key, null);
            return text != null && Guid.TryParse(text, out Guid id) ? id : Guid.Empty;
        }

        private static Guid GuidOrNew(JsonValue obj, string key)
        {
            Guid id = GuidOf(obj, key);
            return id == Guid.Empty ? Guid.NewGuid() : id;
        }

        private static JsonValue GuidValue(Guid id) => id == Guid.Empty ? JsonValue.Null : JsonValue.FromString(id.ToString("D"));

        private static int Clamp(int value, int min, int max) => Math.Max(min, Math.Min(max, value));

        private static ThemeMode ParseTheme(string text)
        {
            switch ((text ?? string.Empty).ToLowerInvariant())
            {
                case "light": return ThemeMode.Light;
                case "dark": return ThemeMode.Dark;
                default: return ThemeMode.Auto;
            }
        }

        private static string ThemeToText(ThemeMode mode)
        {
            switch (mode)
            {
                case ThemeMode.Light: return "light";
                case ThemeMode.Dark: return "dark";
                default: return "auto";
            }
        }
    }
}
