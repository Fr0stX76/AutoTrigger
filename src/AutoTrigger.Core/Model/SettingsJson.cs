using System;
using System.Collections.Generic;
using System.Globalization;
using AutoTrigger.Core.Displays;
using AutoTrigger.Core.Json;
using AutoTrigger.Core.Logging;

namespace AutoTrigger.Core.Model
{
    public static class SettingsJson
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

        private static void ReadActions(JsonValue profile, string key, List<ProfileAction> target, string profileName, List<string> warnings)
        {
            foreach (JsonValue item in Array(profile, key))
            {
                if (item.Kind != JsonKind.Object)
                    continue;
                ProfileAction action = ReadAction(item);
                if (action is UnknownAction unknown)
                    warnings.Add($"Profile '{profileName}': unknown action type '{unknown.TypeName}' kept as is.");
                target.Add(action);
            }
        }

        public static ProfileAction ReadAction(JsonValue json)
        {
            string type = Str(json, "type", string.Empty);
            ProfileAction action;
            switch (type)
            {
                case RunProgramAction.TypeName:
                    action = new RunProgramAction
                    {
                        Path = Str(json, "path", string.Empty),
                        Arguments = Str(json, "arguments", string.Empty),
                        HoldNext = Bool(json, "holdNext", false),
                        HoldCapSec = (int)Num(json, "holdCapSec", RunProgramAction.DefaultHoldCapSec),
                    };
                    break;
                case CloseProgramAction.TypeName:
                    action = new CloseProgramAction
                    {
                        Target = Str(json, "target", string.Empty),
                        Force = Bool(json, "force", false),
                        ForceAfterSec = (int)Num(json, "forceAfterSec", CloseProgramAction.DefaultForceAfterSec),
                    };
                    break;
                case IncludeProfileAction.TypeName:
                    action = new IncludeProfileAction { ProfileId = GuidOf(json, "profileId") };
                    break;
                case DisplayAction.TypeName:
                    action = ReadDisplayAction(json);
                    break;
                case WindowsSettingsAction.TypeName:
                case WindowsSettingsAction.AudioTypeName:
                case WindowsSettingsAction.PowerPlanTypeName:
                case WindowsSettingsAction.ScreenSaverTypeName:
                    action = ReadWindowsSettings(json, type);
                    break;
                case PressHotkeyAction.TypeName:
                    action = new PressHotkeyAction { Keys = ReadKeys(json) };
                    break;
                default:
                    return new UnknownAction(type, json) { DelayMs = (int)Num(json, "delayMs", 0) };
            }
            action.DelayMs = (int)Num(json, "delayMs", 0);
            return action;
        }

        private static WindowsSettingsAction ReadWindowsSettings(JsonValue json, string type)
        {
            return new WindowsSettingsAction
            {
                PlaybackDevice = Str(json, "playbackDevice", string.Empty),
                RecordDevice = Str(json, "recordDevice", string.Empty),
                PlaybackName = Str(json, "playbackName", string.Empty),
                RecordName = Str(json, "recordName", string.Empty),
                PlanId = GuidOf(json, "planId"),
                PlanName = Str(json, "planName", string.Empty),
                ScreenSaverOn = type == WindowsSettingsAction.ScreenSaverTypeName
                    ? Bool(json, "on", true)
                    : ParseOnOff(Str(json, "screenSaver", "unchanged")),
            };
        }

        private static string OnOffText(bool? on) => on == null ? "unchanged" : on.Value ? "on" : "off";

        private static bool? ParseOnOff(string text)
        {
            switch ((text ?? string.Empty).ToLowerInvariant())
            {
                case "on": return true;
                case "off": return false;
                default: return null;
            }
        }

        private static KeyCombination ReadKeys(JsonValue json)
        {
            double key = Num(json, "key", 0);
            return new KeyCombination
            {
                Ctrl = Bool(json, "ctrl", false),
                Alt = Bool(json, "alt", false),
                Shift = Bool(json, "shift", false),
                Key = key > 0 && key < 256 ? (int)key : 0,
            };
        }

        private static JsonValue WriteKeys(JsonValue json, KeyCombination keys)
        {
            keys = keys ?? new KeyCombination();
            return json.Set("keys", KeyNames.Combination(keys))
                .Set("ctrl", keys.Ctrl)
                .Set("alt", keys.Alt)
                .Set("shift", keys.Shift)
                .Set("key", keys.Key);
        }

        private static KeyCombination ReadShortcut(JsonValue profile, string name)
        {
            JsonValue shortcut = profile[name];
            if (shortcut == null || shortcut.Kind != JsonKind.Object)
                return null;
            KeyCombination keys = ReadKeys(shortcut);
            return keys.IsSet ? keys : null;
        }

        private static JsonValue WriteShortcut(KeyCombination keys) =>
            keys == null || !keys.IsSet ? JsonValue.Null : WriteKeys(JsonValue.NewObject(), keys);

        private static DisplayAction ReadDisplayAction(JsonValue json)
        {
            var display = new DisplayAction
            {
                DisplayId = GuidOf(json, "displayId"),
                Hdr = ParseHdr(Str(json, "hdr", "unchanged")),
                ColorDepthBpc = (int)Num(json, "colorDepthBpc", 0),
                ColorFormat = Str(json, "colorFormat", string.Empty),
                DynamicRange = Str(json, "dynamicRange", string.Empty),
            };
            JsonValue resolution = json["resolution"];
            if (resolution != null && resolution.Kind == JsonKind.Object)
            {
                display.Width = (int)Num(resolution, "width", 0);
                display.Height = (int)Num(resolution, "height", 0);
            }
            ReadRefresh(json, display);
            return display;
        }

        private static void ReadRefresh(JsonValue json, DisplayAction display)
        {
            JsonValue refresh = json["refresh"];
            if (refresh != null && refresh.Kind == JsonKind.String && refresh.AsString() == "max")
            {
                display.RefreshMax = true;
                return;
            }
            if (refresh != null && refresh.Kind == JsonKind.Object)
            {
                display.RefreshNumerator = (uint)Math.Max(0, Num(refresh, "numerator", 0));
                display.RefreshDenominator = (uint)Math.Max(1, Num(refresh, "denominator", 1));
                return;
            }
            double hz = Num(json, "refreshHz", 0);
            if (hz > 0)
            {
                display.RefreshNumerator = (uint)Math.Round(hz * 1000);
                display.RefreshDenominator = 1000;
            }
        }

        private static JsonValue WriteActions(List<ProfileAction> actions)
        {
            var list = JsonValue.NewArray();
            foreach (ProfileAction action in actions)
                list.Add(WriteAction(action));
            return list;
        }

        public static JsonValue WriteAction(ProfileAction action)
        {
            if (action is UnknownAction unknown && unknown.Raw != null)
            {
                if (unknown.Raw.Kind != JsonKind.Object)
                    return unknown.Raw;
                var copy = JsonValue.NewObject();
                foreach (KeyValuePair<string, JsonValue> property in unknown.Raw.Properties)
                    copy.Set(property.Key, property.Value);
                return copy.Set("delayMs", unknown.DelayMs);
            }

            var json = JsonValue.NewObject().Set("type", action.Type).Set("delayMs", action.DelayMs);
            switch (action)
            {
                case RunProgramAction run:
                    json.Set("path", run.Path ?? string.Empty)
                        .Set("arguments", run.Arguments ?? string.Empty)
                        .Set("holdNext", run.HoldNext)
                        .Set("holdCapSec", run.HoldCapSec);
                    break;
                case CloseProgramAction close:
                    json.Set("target", close.Target ?? string.Empty)
                        .Set("force", close.Force)
                        .Set("forceAfterSec", close.ForceAfterSec);
                    break;
                case IncludeProfileAction include:
                    json.Set("profileId", GuidValue(include.ProfileId));
                    break;
                case DisplayAction display:
                    json.Set("displayId", GuidValue(display.DisplayId))
                        .Set("hdr", HdrToText(display.Hdr))
                        .Set("resolution", ResolutionValue(display))
                        .Set("refresh", RefreshValue(display))
                        .Set("colorDepthBpc", display.ColorDepthBpc)
                        .Set("colorFormat", display.ColorFormat ?? string.Empty)
                        .Set("dynamicRange", display.DynamicRange ?? string.Empty);
                    break;
                case WindowsSettingsAction windows:
                    json.Set("playbackDevice", windows.PlaybackDevice ?? string.Empty)
                        .Set("recordDevice", windows.RecordDevice ?? string.Empty)
                        .Set("playbackName", windows.PlaybackName ?? string.Empty)
                        .Set("recordName", windows.RecordName ?? string.Empty)
                        .Set("planId", GuidValue(windows.PlanId))
                        .Set("planName", windows.PlanName ?? string.Empty)
                        .Set("screenSaver", OnOffText(windows.ScreenSaverOn));
                    break;
                case PressHotkeyAction hotkey:
                    WriteKeys(json, hotkey.Keys);
                    break;
            }
            return json;
        }

        private static JsonValue ResolutionValue(DisplayAction display)
        {
            if (display.Width <= 0 || display.Height <= 0)
                return JsonValue.Null;
            return JsonValue.NewObject().Set("width", display.Width).Set("height", display.Height);
        }

        private static JsonValue RefreshValue(DisplayAction display)
        {
            if (display.RefreshMax)
                return JsonValue.FromString("max");
            if (display.RefreshNumerator == 0)
                return JsonValue.Null;
            return JsonValue.NewObject()
                .Set("numerator", (long)display.RefreshNumerator)
                .Set("denominator", (long)Math.Max(1u, display.RefreshDenominator));
        }

        private static DisplayCapabilities ReadCapabilities(JsonValue known)
        {
            if (known == null || known.Kind != JsonKind.Object)
                return null;
            var caps = new DisplayCapabilities
            {
                HdrSupported = Bool(known, "hdr", false),
                Nvidia = Bool(known, "nvidia", false),
            };
            foreach (JsonValue item in Array(known, "desktopSizes"))
            {
                PixelSize size = ReadSize(item);
                if (size.IsSet && !caps.DesktopSizes.Contains(size))
                    caps.DesktopSizes.Add(size);
            }
            caps.Signal = ReadSize(known["signal"]);
            foreach (JsonValue rate in Array(known, "refreshRates"))
            {
                if (rate.Kind == JsonKind.Array && rate.Count == 2 && rate[0].AsNumber() > 0 && rate[1].AsNumber() > 0)
                    caps.RefreshRates.Add(new RefreshRate((uint)rate[0].AsNumber().Value, (uint)rate[1].AsNumber().Value));
            }
            foreach (JsonValue color in Array(known, "colors"))
            {
                if (color.Kind == JsonKind.Array && color.Count == 3 && color[0].AsNumber() > 0
                    && color[1].Kind == JsonKind.String && color[2].Kind == JsonKind.String)
                    caps.Colors.Add(new ColorSetting((int)color[0].AsNumber().Value, color[1].AsString(), color[2].AsString()));
            }
            return caps;
        }

        private static PixelSize ReadSize(JsonValue pair)
        {
            if (pair == null || pair.Kind != JsonKind.Array || pair.Count != 2)
                return default;
            double width = pair[0].AsNumber() ?? 0;
            double height = pair[1].AsNumber() ?? 0;
            return width > 0 && height > 0 ? new PixelSize((int)width, (int)height) : default;
        }

        private static JsonValue WriteCapabilities(DisplayCapabilities caps)
        {
            if (caps == null)
                return JsonValue.Null;
            var sizes = JsonValue.NewArray();
            foreach (PixelSize size in caps.DesktopSizes)
                sizes.Add(WriteSize(size));
            var rates = JsonValue.NewArray();
            foreach (RefreshRate rate in caps.RefreshRates)
            {
                rates.Add(JsonValue.NewArray()
                    .Add(JsonValue.FromNumber((long)rate.Numerator))
                    .Add(JsonValue.FromNumber((long)rate.Denominator)));
            }
            var colors = JsonValue.NewArray();
            foreach (ColorSetting color in caps.Colors)
            {
                colors.Add(JsonValue.NewArray()
                    .Add(JsonValue.FromNumber((long)color.Bpc))
                    .Add(JsonValue.FromString(color.Format))
                    .Add(JsonValue.FromString(color.Range)));
            }
            return JsonValue.NewObject()
                .Set("desktopSizes", sizes)
                .Set("signal", caps.Signal.IsSet ? WriteSize(caps.Signal) : JsonValue.Null)
                .Set("refreshRates", rates)
                .Set("hdr", caps.HdrSupported)
                .Set("nvidia", caps.Nvidia)
                .Set("colors", colors);
        }

        public static bool SameCapabilities(DisplayCapabilities stored, DisplayCapabilities live)
        {
            string saved = WriteCapabilities(stored).ToJson(false);
            string reloaded = WriteCapabilities(ReadCapabilities(WriteCapabilities(live))).ToJson(false);
            return saved == reloaded;
        }

        private static JsonValue WriteSize(PixelSize size) =>
            JsonValue.NewArray().Add(JsonValue.FromNumber((long)size.Width)).Add(JsonValue.FromNumber((long)size.Height));

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

        private static string HdrToText(HdrChange hdr) => hdr switch
        {
            HdrChange.On => "on",
            HdrChange.Off => "off",
            _ => "unchanged",
        };

        private static HdrChange ParseHdr(string text)
        {
            switch ((text ?? string.Empty).ToLowerInvariant())
            {
                case "on": return HdrChange.On;
                case "off": return HdrChange.Off;
                default: return HdrChange.Unchanged;
            }
        }
    }
}
