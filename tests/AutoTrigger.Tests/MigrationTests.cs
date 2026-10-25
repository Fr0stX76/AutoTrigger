using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AutoTrigger.Core.Json;
using AutoTrigger.Core.Logging;
using AutoTrigger.Core.Model;
using AutoTrigger.Core.Storage;

namespace AutoTrigger.Tests
{
    public static partial class Runner
    {
        private static void SettingsFrom120()
        {
            var warnings = new List<string>();
            AppSettings s = SettingsJson.Read(JsonValue.Parse(SettingsFixtures.Version120), warnings);
            Eq(0, warnings.Count, "1.2.0 file: read without warnings: " + string.Join(" | ", warnings));

            Check(s.StartMinimized && s.CloseToTray && s.MonitoringPaused, "1.2.0 file: switches kept");
            Eq(ThemeMode.Dark, s.Theme, "1.2.0 file: theme kept");
            Eq(450, s.FocusStabilityMs, "1.2.0 file: focus stability kept");
            Eq(750, s.StartPollMs, "1.2.0 file: start detection kept");
            Eq(LogLevel.Detailed, s.LogLevel, "1.2.0 file: log level kept");
            Eq(1180.0, s.WindowWidth, "1.2.0 file: window width kept");
            Eq(760.0, s.WindowHeight, "1.2.0 file: window height kept");
            Check(s.CheckForUpdates, "1.2.0 file: update check on by default");
            Eq(AppSettings.DefaultUpdateCheckHours, s.UpdateCheckHours, "1.2.0 file: update check interval by default");

            Eq(3, s.Applications.Count, "1.2.0 file: applications kept");
            AppEntry pass = s.Applications[1];
            Check(pass.MatchByFileName && !pass.Enabled, "1.2.0 file: match by file name and disabled kept");
            Eq(2, s.Profiles.Count, "1.2.0 file: profiles kept");
            Eq(2, s.Displays.Count, "1.2.0 file: displays kept");
            Check(s.Displays[0].Known != null && s.Displays[0].Known.Colors.Count == 2, "1.2.0 file: display capabilities kept");
            Check(!s.Displays[1].Managed, "1.2.0 file: unmanaged display kept");

            Profile vrr = s.Profiles.First(p => p.Name == "VRR");
            Check(vrr.RestartApp, "1.2.0 file: Restart app kept");
            Eq("Ctrl+Alt+1", KeyNames.Readable(vrr.Shortcut), "1.2.0 file: Started shortcut kept");
            Eq("Ctrl+Shift+F1", KeyNames.Readable(vrr.ClosedShortcut), "1.2.0 file: Closed shortcut kept");
            Eq("display|display|includeProfile|windowsSettings|runProgram|sendKeys", string.Join("|", vrr.OnStart.Select(a => a.Type)),
                "1.2.0 file: Started actions kept in order");
            Eq("closeProgram|sendKeys|display|windowsSettings", string.Join("|", vrr.OnClose.Select(a => a.Type)),
                "1.2.0 file: Closed actions kept in order");
            Eq(1, vrr.OnFocus.Count, "1.2.0 file: Gained focus action kept");
            Eq(1, vrr.OnBlur.Count, "1.2.0 file: Lost focus action kept");

            var run = (RunProgramAction)vrr.OnStart[4];
            Eq("-profile \"game\" -silent", run.Arguments, "1.2.0 file: arguments kept");
            Check(run.HoldNext && run.HoldCapSec == 45 && run.DelayMs == 300, "1.2.0 file: hold and delay kept");
            var close = (CloseProgramAction)vrr.OnClose[0];
            Check(close.Force && close.ForceAfterSec == 8, "1.2.0 file: Force and its delay kept");
            var display = (DisplayAction)vrr.OnStart[0];
            Check(display.Hdr == HdrChange.On && display.Width == 3840 && display.RefreshNumerator == 120000
                && display.ColorDepthBpc == 10, "1.2.0 file: display action kept");
            Check(((DisplayAction)vrr.OnStart[1]).RefreshMax, "1.2.0 file: maximum refresh kept");
            var windows = (WindowsSettingsAction)vrr.OnStart[3];
            Check(windows.ChangesPowerPlan && windows.ScreenSaverOn == false, "1.2.0 file: power plan and screen saver kept");
            Eq("Ctrl+Alt+0", KeyNames.Readable(((PressHotkeyAction)vrr.OnClose[1]).Keys), "1.2.0 file: Hotkey keys kept");

            JsonValue rewritten = SettingsJson.Write(s);
            var lost = new List<string>();
            SameValues(JsonValue.Parse(SettingsFixtures.Version120), rewritten, "settings", lost);
            Eq(0, lost.Count, "1.2.0 file: nothing lost when saved again: " + string.Join(", ", lost));
            string once = rewritten.ToJson();
            string twice = SettingsJson.Write(SettingsJson.Read(JsonValue.Parse(once), new List<string>())).ToJson();
            Eq(once, twice, "1.2.0 file: saving again changes nothing more");
        }

        private static void OldSettingsCopies()
        {
            string dir = Path.Combine(Path.GetTempPath(), "at-copies-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                for (int i = 1; i <= 5; i++)
                    File.WriteAllText(Path.Combine(dir, $"settings.before-import-2026010{i}-120000.json"), "{}");
                for (int i = 1; i <= 2; i++)
                    File.WriteAllText(Path.Combine(dir, $"settings.invalid-2026020{i}-120000.json"), "{}");
                File.WriteAllText(Path.Combine(dir, "settings.json"), "{}");
                File.WriteAllText(Path.Combine(dir, "settings.before-import-notes.txt"), "mine");

                using (var store = new SettingsStore(dir))
                    store.RemoveOldCopies();

                string[] left = Directory.GetFiles(dir).Select(Path.GetFileName).OrderBy(n => n, StringComparer.Ordinal).ToArray();
                Eq("settings.before-import-20260103-120000.json|settings.before-import-20260104-120000.json|"
                    + "settings.before-import-20260105-120000.json|settings.before-import-notes.txt|"
                    + "settings.invalid-20260201-120000.json|settings.invalid-20260202-120000.json|settings.json",
                    string.Join("|", left), "the 3 most recent copies of each kind stay; other files are not touched");
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        private static void SameValues(JsonValue original, JsonValue rewritten, string path, List<string> lost)
        {
            switch (original.Kind)
            {
                case JsonKind.Object:
                    foreach (KeyValuePair<string, JsonValue> property in original.Properties)
                        SameValues(property.Value, rewritten?[property.Key], path + "." + property.Key, lost);
                    break;
                case JsonKind.Array:
                    if (rewritten == null || rewritten.Kind != JsonKind.Array || rewritten.Count != original.Count)
                    {
                        lost.Add(path);
                        break;
                    }
                    for (int i = 0; i < original.Count; i++)
                        SameValues(original[i], rewritten[i], path + "[" + i + "]", lost);
                    break;
                default:
                    if (rewritten == null || rewritten.ToJson(false) != original.ToJson(false))
                        lost.Add(path);
                    break;
            }
        }
    }
}
