using System;
using System.Collections.Generic;
using AutoTrigger.Core.Displays;
using AutoTrigger.Core.Json;

namespace AutoTrigger.Core.Model
{
    public static partial class SettingsJson
    {
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
                        HideConsole = Bool(json, "hideConsole", false),
                        SkipIfRunning = Bool(json, "skipIfRunning", false),
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
            if (action.CanBeNamed)
                action.Name = Str(json, "name", string.Empty);
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
            if (action.CanBeNamed)
                json.Set("name", action.Name ?? string.Empty);
            switch (action)
            {
                case RunProgramAction run:
                    json.Set("path", run.Path ?? string.Empty)
                        .Set("arguments", run.Arguments ?? string.Empty)
                        .Set("holdNext", run.HoldNext)
                        .Set("hideConsole", run.HideConsole)
                        .Set("skipIfRunning", run.SkipIfRunning)
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
