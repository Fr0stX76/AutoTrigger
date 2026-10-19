using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using AutoTrigger.Core.Json;

namespace AutoTrigger.Core.Model
{
    public sealed class ImportResult
    {
        public List<AppEntry> Applications { get; } = new List<AppEntry>();

        public List<Profile> Profiles { get; } = new List<Profile>();

        public List<DisplayEntry> Displays { get; } = new List<DisplayEntry>();

        public List<string> Warnings { get; } = new List<string>();

        public int ActionCount { get; set; }

        public bool AutoStart { get; set; }
    }

    public static class AutoActionsImport
    {
        public const string FileName = "UserSettings.json";
        public const long AllDisplaysUid = 4294967295;
        public const int CloseForceAfterSec = 3;

        private static readonly KeyValuePair<string, EventKind>[] EventLists =
        {
            new KeyValuePair<string, EventKind>("ApplicationStarted", EventKind.Started),
            new KeyValuePair<string, EventKind>("ApplicationClosed", EventKind.Closed),
            new KeyValuePair<string, EventKind>("ApplicationGotFocus", EventKind.GotFocus),
            new KeyValuePair<string, EventKind>("ApplicationLostFocus", EventKind.LostFocus),
        };

        private static readonly int[] ColorDepths = { 6, 8, 10, 12, 16 };

        public static ImportResult Read(string text) => new Reader(ParseRoot(text)).Read();

        private static JsonValue ParseRoot(string text)
        {
            JsonValue root;
            try
            {
                root = JsonValue.Parse(text ?? string.Empty);
            }
            catch (JsonException ex)
            {
                throw new FormatException("this file is not valid JSON (" + ex.Message + ")", ex);
            }
            bool looksRight = root != null
                && root.Kind == JsonKind.Object
                && (Str(root, "$type") ?? string.Empty).IndexOf("UserAppSettings", StringComparison.Ordinal) >= 0
                && root["ApplicationProfiles"] != null
                && root["ApplicationProfiles"].Kind != JsonKind.Null;
            if (!looksRight)
                throw new FormatException("this file does not look like AutoActions settings (UserSettings.json)");
            return root;
        }

        private sealed class Reader
        {
            private readonly JsonValue _root;
            private readonly ImportResult _result = new ImportResult();
            private readonly Dictionary<long, DisplayEntry> _displayByUid = new Dictionary<long, DisplayEntry>();
            private readonly HashSet<Guid> _profileIds = new HashSet<Guid>();

            public Reader(JsonValue root)
            {
                _root = root;
            }

            public ImportResult Read()
            {
                _result.AutoStart = Bool(_root, "AutoStart");
                ReadDisplays();
                ReadProfiles();
                ReadApplications();
                CheckReferences();
                NoteShortcuts();
                return _result;
            }

            private void ReadDisplays()
            {
                foreach (JsonValue display in List(_root, "Displays"))
                {
                    long uid = (long)Num(display, "UID", -1);
                    if (uid < 0 || uid == AllDisplaysUid || _displayByUid.ContainsKey(uid))
                        continue;
                    AddDisplay(uid, Bool(display, "Managed", true));
                }
            }

            private void ReadProfiles()
            {
                foreach (JsonValue item in List(_root, "ApplicationProfiles"))
                {
                    string name = Str(item, "Name") ?? string.Empty;
                    Guid id = GuidOf(item, "GUID");
                    if (id == Guid.Empty)
                    {
                        id = Guid.NewGuid();
                        _result.Warnings.Add($"Profile '{name}' had no valid id; it was given a new one.");
                    }
                    if (!_profileIds.Add(id))
                    {
                        _result.Warnings.Add($"Profile '{name}' has the same id as another profile; skipped.");
                        continue;
                    }

                    var profile = new Profile { Id = id, Name = name, RestartApp = Bool(item, "RestartApplication") };
                    foreach (KeyValuePair<string, EventKind> list in EventLists)
                    {
                        foreach (JsonValue action in List(item, list.Key))
                        {
                            if (action.Kind != JsonKind.Object)
                                continue;
                            profile.For(list.Value).Add(ReadAction(action, name));
                            _result.ActionCount++;
                        }
                    }
                    _result.Profiles.Add(profile);
                }
                Ordering.SortByName(_result.Profiles, profile => profile.Name, false);
            }

            private void ReadApplications()
            {
                var assignments = List(_root, "ApplicationProfileAssignments")
                    .Where(item => item.Kind == JsonKind.Object)
                    .Select((item, index) => new { Value = item, Index = index, Position = (int)Num(item, "Position", 0) })
                    .OrderBy(item => item.Position)
                    .ThenBy(item => item.Index);
                foreach (var assignment in assignments)
                {
                    JsonValue app = assignment.Value["Application"];
                    string name = Str(app, "DisplayName");
                    if (string.IsNullOrEmpty(name))
                        name = Str(app, "ApplicationName") ?? string.Empty;
                    string path = Str(app, "ApplicationFilePath") ?? string.Empty;
                    if (path.Length == 0)
                    {
                        _result.Warnings.Add($"Application '{name}' has no program path; skipped.");
                        continue;
                    }
                    if (Bool(app, "IsUWP"))
                        _result.Warnings.Add($"Application '{name}' is a Microsoft Store app; imported by its program, {path}.");

                    Guid profileId = GuidOf(assignment.Value, "ProfileGUID");
                    if (profileId != Guid.Empty && !_profileIds.Contains(profileId))
                    {
                        _result.Warnings.Add($"Application '{name}': its profile no longer exists; imported without a profile.");
                        profileId = Guid.Empty;
                    }
                    _result.Applications.Add(new AppEntry { Name = name, Path = path, ProfileId = profileId });
                }
            }

            private void CheckReferences()
            {
                var imported = new AppSettings();
                imported.Applications.AddRange(_result.Applications);
                imported.Profiles.AddRange(_result.Profiles);
                imported.Displays.AddRange(_result.Displays);
                foreach (ValidationIssue issue in SettingsValidator.Validate(imported))
                    _result.Warnings.Add(issue.Message);
            }

            private void NoteShortcuts()
            {
                int shortcuts = List(_root, "ActionShortcuts").Count();
                if (shortcuts > 0)
                    _result.Warnings.Add($"{shortcuts} shortcut(s) not imported: AutoTrigger has no shortcuts.");
            }

            private ProfileAction ReadAction(JsonValue action, string profileName)
            {
                string type = ShortType(Str(action, "$type") ?? string.Empty);
                switch (type)
                {
                    case "RunProgramAction":
                        var run = new RunProgramAction
                        {
                            Path = Str(action, "FilePath") ?? string.Empty,
                            Arguments = Str(action, "Arguments") ?? string.Empty,
                            HoldNext = Bool(action, "WaitForEnd"),
                            HoldCapSec = RunProgramAction.DefaultHoldCapSec,
                        };
                        if (run.Path.Length == 0)
                            _result.Warnings.Add($"Profile '{profileName}': a 'Run program' action has no program.");
                        return run;

                    case "CloseProgramAction":
                        return new CloseProgramAction
                        {
                            Target = Str(action, "ProcessName") ?? string.Empty,
                            Force = Bool(action, "Force"),
                            ForceAfterSec = CloseForceAfterSec,
                        };

                    case "ReferenceProfileAction":
                        return new IncludeProfileAction { ProfileId = GuidOf(action, "ReferenceGuid") };

                    case "DisplayAction":
                        return ReadDisplayAction(action, profileName);

                    case "AudioDeviceAction":
                        return new WindowsSettingsAction
                        {
                            PlaybackDevice = ChangedDevice(action, "ChangePlaybackDevice", "PlaybackDeviceID"),
                            RecordDevice = ChangedDevice(action, "ChangeRecordDevice", "RecordDeviceID"),
                        };

                    default:
                        throw new FormatException($"unknown action type '{type}' in profile '{profileName}'");
                }
            }

            private static string ChangedDevice(JsonValue action, string changeKey, string idKey) =>
                Bool(action, changeKey) ? Str(action, idKey) ?? string.Empty : string.Empty;

            private DisplayAction ReadDisplayAction(JsonValue action, string profileName)
            {
                var display = new DisplayAction();
                long uid = (long)Num(action, "DisplayUID", AllDisplaysUid);
                if (uid != AllDisplaysUid)
                {
                    if (!_displayByUid.TryGetValue(uid, out DisplayEntry entry))
                        entry = AddDisplay(uid, true);
                    display.DisplayId = entry.Id;
                }
                if (Bool(action, "ChangeHDR"))
                    display.Hdr = Bool(action, "EnableHDR") ? HdrChange.On : HdrChange.Off;
                if (Bool(action, "ChangeResolution"))
                    ReadResolution(action, display, profileName);
                if (Bool(action, "ChangeRefreshRate"))
                {
                    int hz = (int)Num(action, "RefreshRate", 0);
                    if (hz > 0)
                    {
                        display.RefreshNumerator = (uint)hz * 1000;
                        display.RefreshDenominator = 1000;
                    }
                }
                if (Bool(action, "ChangeColorDepth"))
                {
                    int depth = (int)Num(action, "ColorDepth", 0);
                    if (ColorDepths.Contains(depth))
                        display.ColorDepthBpc = depth;
                    else
                        _result.Warnings.Add($"Profile '{profileName}': unknown color depth ({depth}); left out.");
                }
                return display;
            }

            private void ReadResolution(JsonValue action, DisplayAction display, string profileName)
            {
                string text = Text(action["Resolution"]);
                int[] size = Regex.Split(text, "[^0-9]+")
                    .Where(part => part.Length > 0)
                    .Select(part => int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out int value) ? value : 0)
                    .ToArray();
                if (size.Length >= 2 && size[0] > 0 && size[1] > 0)
                {
                    display.Width = size[0];
                    display.Height = size[1];
                }
                else
                {
                    _result.Warnings.Add($"Profile '{profileName}': resolution '{text}' cannot be read; left out.");
                }
            }

            private DisplayEntry AddDisplay(long uid, bool managed)
            {
                var entry = new DisplayEntry { LegacyUid = uid, Managed = managed };
                _result.Displays.Add(entry);
                _displayByUid[uid] = entry;
                return entry;
            }
        }

        private static string ShortType(string full)
        {
            string name = full.Split(',')[0].Trim();
            int dot = name.LastIndexOf('.');
            return dot >= 0 ? name.Substring(dot + 1) : name;
        }

        private static IEnumerable<JsonValue> List(JsonValue obj, string key)
        {
            JsonValue value = obj?[key];
            if (value != null && value.Kind == JsonKind.Object)
                value = value["$values"];
            if (value == null || value.Kind != JsonKind.Array)
                return Enumerable.Empty<JsonValue>();
            return value.Items.Where(v => v != null && v.Kind != JsonKind.Null);
        }

        private static string Str(JsonValue obj, string key)
        {
            JsonValue value = obj?[key];
            return value != null && value.Kind == JsonKind.String ? value.AsString() : null;
        }

        private static string Text(JsonValue value)
        {
            if (value == null)
                return string.Empty;
            switch (value.Kind)
            {
                case JsonKind.String: return value.AsString();
                case JsonKind.Number: return value.AsNumber().Value.ToString(CultureInfo.InvariantCulture);
                case JsonKind.Object:
                    return Text(value["Width"] ?? value["width"]) + "x" + Text(value["Height"] ?? value["height"]);
                default: return string.Empty;
            }
        }

        private static bool Bool(JsonValue obj, string key, bool fallback = false)
        {
            JsonValue value = obj?[key];
            if (value == null)
                return fallback;
            if (value.Kind == JsonKind.Bool)
                return value.AsBool().Value;
            if (value.Kind == JsonKind.String && bool.TryParse(value.AsString(), out bool parsed))
                return parsed;
            return fallback;
        }

        private static double Num(JsonValue obj, string key, double fallback)
        {
            JsonValue value = obj?[key];
            if (value == null)
                return fallback;
            if (value.Kind == JsonKind.Number)
                return value.AsNumber().Value;
            if (value.Kind == JsonKind.String)
            {
                string digits = Regex.Match(value.AsString() ?? string.Empty, "-?[0-9]+").Value;
                if (double.TryParse(digits, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out double parsed))
                    return parsed;
            }
            return fallback;
        }

        private static Guid GuidOf(JsonValue obj, string key)
        {
            string text = Str(obj, key);
            return text != null && Guid.TryParse(text, out Guid g) ? g : Guid.Empty;
        }
    }
}
