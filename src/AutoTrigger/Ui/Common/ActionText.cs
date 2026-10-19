using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using AutoTrigger.Core.Displays;
using AutoTrigger.Core.Model;

namespace AutoTrigger.Ui
{
    internal static class ActionText
    {
        public static readonly EventKind[] Events = { EventKind.Started, EventKind.Closed, EventKind.GotFocus, EventKind.LostFocus };

        public static string EventName(EventKind kind)
        {
            switch (kind)
            {
                case EventKind.Started: return Texts.EventStarted;
                case EventKind.Closed: return Texts.EventClosed;
                case EventKind.GotFocus: return Texts.EventGotFocus;
                default: return Texts.EventLostFocus;
            }
        }

        public static string TypeName(ProfileAction action)
        {
            switch (action)
            {
                case RunProgramAction _: return Texts.TypeRunProgram;
                case CloseProgramAction _: return Texts.TypeCloseProgram;
                case IncludeProfileAction _: return Texts.TypeReferenceProfile;
                case DisplayAction _: return Texts.TypeDisplay;
                case WindowsSettingsAction _: return Texts.TypeWindowsSettings;
                case PressHotkeyAction _: return Texts.TypeHotkey;
                default: return Texts.TypeUnknown;
            }
        }

        public static string Summary(ProfileAction action, AppSettings settings)
        {
            switch (action)
            {
                case RunProgramAction run:
                    return string.IsNullOrWhiteSpace(run.Path) ? Texts.NoProgram : SafeFileName(run.Path);
                case CloseProgramAction close:
                    return string.IsNullOrWhiteSpace(close.Target) ? Texts.NoProgram : SafeFileName(close.Target);
                case IncludeProfileAction include:
                    return ReferenceName(settings.FindProfile(include.ProfileId));
                case DisplayAction display:
                    return DisplaySummary(display, settings);
                case WindowsSettingsAction windows:
                    return WindowsSummary(windows);
                case PressHotkeyAction hotkey:
                    return HotkeyText(hotkey.Keys);
                case UnknownAction unknown:
                    return unknown.TypeName;
                default:
                    return action.Type;
            }
        }

        private static string ReferenceName(Profile target)
        {
            if (target == null)
                return Texts.MissingProfile;
            return string.IsNullOrWhiteSpace(target.Name) ? Texts.Unnamed : target.Name;
        }

        public static string Details(ProfileAction action, AppSettings settings)
        {
            switch (action)
            {
                case RunProgramAction run:
                    return string.IsNullOrEmpty(run.Arguments) ? run.Path : run.Path + " " + run.Arguments;
                case CloseProgramAction close:
                    return close.Target;
                case DisplayAction display:
                    return DisplaySummary(display, settings);
                case WindowsSettingsAction windows:
                    return WindowsSummary(windows);
                case PressHotkeyAction hotkey:
                    return HotkeyText(hotkey.Keys);
                default:
                    return null;
            }
        }

        public static string HotkeyText(KeyCombination keys) =>
            keys == null || !keys.IsSet ? Texts.NoHotkey : KeyNames.Readable(keys);

        private static string WindowsSummary(WindowsSettingsAction windows)
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(windows.PlaybackDevice))
                parts.Add(string.IsNullOrWhiteSpace(windows.PlaybackName) ? Texts.AudioOutput : windows.PlaybackName);
            if (!string.IsNullOrWhiteSpace(windows.RecordDevice))
                parts.Add(string.IsNullOrWhiteSpace(windows.RecordName) ? Texts.AudioInput : windows.RecordName);
            if (windows.ChangesPowerPlan)
                parts.Add(string.IsNullOrWhiteSpace(windows.PlanName) ? windows.PlanId.ToString("D") : windows.PlanName);
            if (windows.ScreenSaverOn.HasValue)
                parts.Add(windows.ScreenSaverOn.Value ? Texts.ScreenSaverOn : Texts.ScreenSaverOff);
            return parts.Count == 0 ? Texts.NoChange : string.Join(" · ", parts);
        }

        private static string DisplaySummary(DisplayAction d, AppSettings settings)
        {
            var parts = new List<string>();
            if (d.DisplayId == Guid.Empty)
            {
                parts.Add(Texts.AllManagedDisplays);
            }
            else
            {
                DisplayEntry entry = settings.Displays.Find(x => x.Id == d.DisplayId);
                parts.Add(entry == null ? Texts.MissingDisplay
                    : !string.IsNullOrEmpty(entry.FriendlyName) ? entry.FriendlyName
                    : entry.LegacyUid >= 0 ? Texts.Format(Texts.ImportedDisplay, entry.LegacyUid) : Texts.Display);
            }
            if (d.Hdr == HdrChange.On)
                parts.Add("HDR on");
            else if (d.Hdr == HdrChange.Off)
                parts.Add("HDR off");
            if (d.Width > 0 && d.Height > 0)
                parts.Add(d.Width.ToString(CultureInfo.InvariantCulture) + " × " + d.Height.ToString(CultureInfo.InvariantCulture));
            if (d.RefreshMax)
                parts.Add(Texts.MaxHz);
            else if (d.RefreshNumerator > 0)
                parts.Add(Hz(d.RefreshNumerator, d.RefreshDenominator));
            if (d.ColorDepthBpc > 0)
                parts.Add(d.ColorDepthBpc.ToString(CultureInfo.InvariantCulture) + " bpc");
            if (!string.IsNullOrEmpty(d.ColorFormat))
                parts.Add(ColorSetting.FormatName(d.ColorFormat));
            if (!string.IsNullOrEmpty(d.DynamicRange))
                parts.Add(ColorSetting.RangeName(d.DynamicRange));
            return parts.Count == 1 ? parts[0] + " · " + Texts.NoChange : string.Join(" · ", parts);
        }

        public static string Hz(uint numerator, uint denominator)
        {
            if (numerator == 0)
                return "—";
            double hz = (double)numerator / Math.Max(1u, denominator);
            double rounded = Math.Round(hz, 2);
            return (Math.Abs(rounded - Math.Round(rounded)) < 0.005
                ? Math.Round(rounded).ToString(CultureInfo.InvariantCulture)
                : rounded.ToString("0.##", CultureInfo.InvariantCulture)) + " Hz";
        }

        public static string Count(int count)
        {
            if (count == 0)
                return Texts.NoActions;
            return count == 1 ? Texts.OneAction : Texts.Format(Texts.ManyActions, count);
        }

        private static string SafeFileName(string path)
        {
            try
            {
                string name = Path.GetFileName(path.Trim().Trim('"'));
                return string.IsNullOrEmpty(name) ? path : name;
            }
            catch (ArgumentException)
            {
                return path;
            }
        }
    }
}
