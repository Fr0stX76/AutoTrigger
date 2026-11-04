using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AutoTrigger.Core.Engine;
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

            Check(!run.HideConsole, "1.2.0 file: console window shown by default");
            SavedWithoutLoss(SettingsFixtures.Version120, s, "1.2.0 file");
        }

        private static void SettingsFrom130()
        {
            var warnings = new List<string>();
            AppSettings s = SettingsJson.Read(JsonValue.Parse(SettingsFixtures.Version130), warnings);
            Eq(0, warnings.Count, "1.3.0 file: read without warnings: " + string.Join(" | ", warnings));
            Check(!s.CheckForUpdates, "1.3.0 file: update check switch kept");
            Eq(12, s.UpdateCheckHours, "1.3.0 file: update check interval kept");
            Eq(3, s.Applications.Count, "1.3.0 file: applications kept");
            Eq(2, s.Profiles.Count, "1.3.0 file: profiles kept");
            Eq(2, s.Displays.Count, "1.3.0 file: displays kept");
            Profile vrr = s.Profiles.First(p => p.Name == "VRR");
            Eq(6, vrr.OnStart.Count, "1.3.0 file: Started actions kept");
            Check(vrr.OnStart.OfType<RunProgramAction>().All(r => !r.HideConsole), "1.3.0 file: console window shown by default");
            SavedWithoutLoss(SettingsFixtures.Version130, s, "1.3.0 file");
        }

        private static void SettingsFrom140()
        {
            var warnings = new List<string>();
            AppSettings s = SettingsJson.Read(JsonValue.Parse(SettingsFixtures.Version140), warnings);
            Eq(0, warnings.Count, "1.4.0 file: read without warnings: " + string.Join(" | ", warnings));
            Eq(string.Empty, s.Language, "1.4.0 file: Windows language");
            Profile vrr = s.Profiles.First(p => p.Name == "VRR");
            RunProgramAction run = vrr.OnStart.OfType<RunProgramAction>().Single();
            Check(run.HideConsole && run.Name == "Mixer", "1.4.0 file: hidden console and name kept");
            Eq("Close mixer", vrr.OnClose.OfType<CloseProgramAction>().Single().Name, "1.4.0 file: Close program name kept");
            Eq("Overlay on", vrr.OnStart.OfType<PressHotkeyAction>().Single().Name, "1.4.0 file: Hotkey name kept");
            SavedWithoutLoss(SettingsFixtures.Version140, s, "1.4.0 file");
        }

        private static void SettingsFrom150()
        {
            var warnings = new List<string>();
            AppSettings s = SettingsJson.Read(JsonValue.Parse(SettingsFixtures.Version150), warnings);
            Eq(0, warnings.Count, "1.5.0 file: read without warnings: " + string.Join(" | ", warnings));
            Eq("fr", s.Language, "1.5.0 file: language kept");
            Eq(3, s.Applications.Count, "1.5.0 file: applications kept");
            Check(s.Applications.All(a => a.Launch == LaunchKind.Program && a.LaunchProfileId == Guid.Empty),
                "1.5.0 file: every application launches its program, as before");
            SavedWithoutLoss(SettingsFixtures.Version150, s, "1.5.0 file");
        }

        private static void LaunchButton()
        {
            var s = NewSettings();
            Profile eden = AddProfile(s, "Eden 60Hz");
            AppEntry none = AddApp(s, "Tool", "C:\\Tools\\tool.exe", null);
            none.Launch = LaunchKind.None;
            AppEntry game = AddApp(s, "Metroid Dread", "D:\\Eden\\eden.exe", eden);
            game.Launch = LaunchKind.Profile;
            game.LaunchProfileId = eden.Id;
            AppEntry program = AddApp(s, "Game", "D:\\Games\\game.exe", null);

            var warnings = new List<string>();
            AppSettings read = SettingsJson.Read(SettingsJson.Write(s), warnings);
            Eq(0, warnings.Count, "round trip: no warnings");
            Eq(LaunchKind.None, read.FindApplication(none.Id).Launch, "round trip: None kept");
            Check(read.FindApplication(game.Id).Launch == LaunchKind.Profile && read.FindApplication(game.Id).LaunchProfileId == eden.Id,
                "round trip: launch profile kept");
            Check(read.FindApplication(program.Id).Launch == LaunchKind.Program && read.FindApplication(program.Id).LaunchProfileId == Guid.Empty,
                "round trip: Program kept");

            program.LaunchProfileId = eden.Id;
            Eq(Guid.Empty, SettingsJson.Read(SettingsJson.Write(s), new List<string>()).FindApplication(program.Id).LaunchProfileId,
                "a launch profile is saved only when a profile is launched");

            game.LaunchProfileId = Guid.NewGuid();
            warnings.Clear();
            AppSettings broken = SettingsJson.Read(SettingsJson.Write(s), warnings);
            game.LaunchProfileId = eden.Id;
            Check(broken.FindApplication(game.Id).Launch == LaunchKind.None && broken.FindApplication(game.Id).LaunchProfileId == Guid.Empty,
                "missing launch profile: launches nothing");
            Check(warnings.Count == 1 && warnings[0].Contains("Metroid Dread"), "missing launch profile: one warning: " + string.Join(" | ", warnings));

            string text = SettingsJson.Write(s).ToJson();
            AppSettings odd = SettingsJson.Read(JsonValue.Parse(text.Replace("\"launch\": \"none\"", "\"launch\": \"rocket\"")), new List<string>());
            Eq(LaunchKind.Program, odd.FindApplication(none.Id).Launch, "unknown launch: Program");

            s.RemoveProfile(eden);
            Check(game.Launch == LaunchKind.None && game.LaunchProfileId == Guid.Empty, "deleted profile: its application launches nothing");
            Eq(Guid.Empty, game.ProfileId, "deleted profile: its application has no profile");
            Eq(LaunchKind.Program, program.Launch, "deleted profile: other launches unchanged");
            Check(s.FindProfile(eden.Id) == null, "deleted profile: removed");
        }

        private static void SkipIfRunning()
        {
            const string tool = "/tools/Overlay/Overlay.exe";
            var procs = new FakeProcessSystem(new FakeScheduler());
            using (var exec = new ActionExecutor(procs))
            {
                ActionOutcome first = exec.ExecuteAsync(new RunProgramAction { Path = tool, SkipIfRunning = true }).Result;
                ActionOutcome second = exec.ExecuteAsync(new RunProgramAction { Path = tool, SkipIfRunning = true, HoldNext = true }).Result;
                Eq(1, procs.Starts.Count, "started once, then left alone while it runs");
                Check(first.Result == ActionResult.Succeeded && second.Result == ActionResult.Succeeded && second.Message.Contains("already running"),
                    "outcomes: " + first.Message + " | " + second.Message);
                Check(second.ProgramExited == null, "nothing to hold when it was not started");

                exec.ExecuteAsync(new RunProgramAction { Path = tool }).Wait();
                Eq(2, procs.Starts.Count, "without the switch, it starts again");

                procs.Launch("/other/Overlay.exe");
                exec.ExecuteAsync(new RunProgramAction { Path = "/tools/Second/Overlay.exe", SkipIfRunning = true }).Wait();
                Eq(3, procs.Starts.Count, "another program with the same file name does not count");

                procs.Launch("/tools/run.cmd");
                exec.ExecuteAsync(new RunProgramAction { Path = "/tools/run.cmd", SkipIfRunning = true }).Wait();
                Eq(4, procs.Starts.Count, "a script always starts");
            }

            var saved = (RunProgramAction)SettingsJson.ReadAction(SettingsJson.WriteAction(new RunProgramAction { Path = tool, SkipIfRunning = true }));
            Check(saved.SkipIfRunning, "switch kept in settings.json");
            AppSettings old = SettingsJson.Read(JsonValue.Parse(SettingsFixtures.Version150), new List<string>());
            Check(old.Profiles.SelectMany(p => p.OnStart.Concat(p.OnClose)).OfType<RunProgramAction>().All(r => !r.SkipIfRunning),
                "1.5.0 file: Run program actions start as before");
        }

        private static void HiddenConsole()
        {
            Check(ConsoleProgram.IsConsoleImage(PeHeader(3), 512), "console subsystem: console");
            Check(!ConsoleProgram.IsConsoleImage(PeHeader(2), 512), "window subsystem: not console");
            byte[] notMz = PeHeader(3);
            notMz[0] = (byte)'X';
            Check(!ConsoleProgram.IsConsoleImage(notMz, 512), "no MZ: not console");
            byte[] farPe = PeHeader(3);
            BitConverter.GetBytes(4000).CopyTo(farPe, 0x3C);
            Check(!ConsoleProgram.IsConsoleImage(farPe, 512), "PE header past the bytes read: not console");
            Check(!ConsoleProgram.IsConsoleImage(PeHeader(3), 100), "file too short: not console");
            Check(!ConsoleProgram.IsConsoleImage(null, 0), "nothing read: not console");

            string dir = Path.Combine(Path.GetTempPath(), "at-console-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                string console = Path.Combine(dir, "tool.exe");
                string window = Path.Combine(dir, "app.exe");
                string script = Path.Combine(dir, "run.cmd");
                File.WriteAllBytes(console, PeHeader(3));
                File.WriteAllBytes(window, PeHeader(2));
                File.WriteAllText(script, "@echo off");
                Check(ConsoleProgram.Is(console), "console exe on disk: console");
                Check(!ConsoleProgram.Is(window), "window exe on disk: not console");
                Check(ConsoleProgram.Is(script), ".cmd script: console");
                Check(!ConsoleProgram.Is(Path.Combine(dir, "gone.exe")), "missing file: not console");
                Check(!ConsoleProgram.Is("tool.exe"), "no folder: not console");

                var procs = new FakeProcessSystem(new FakeScheduler());
                using (var exec = new ActionExecutor(procs))
                {
                    string hidden = exec.ExecuteAsync(new RunProgramAction { Path = console, HideConsole = true }).Result.Message;
                    string shown = exec.ExecuteAsync(new RunProgramAction { Path = window, HideConsole = true }).Result.Message;
                    exec.ExecuteAsync(new RunProgramAction { Path = script, HideConsole = true }).Wait();
                    exec.ExecuteAsync(new RunProgramAction { Path = console }).Wait();
                    Check(hidden.Contains("hidden"), "log says the console was hidden: " + hidden);
                    Check(!shown.Contains("hidden"), "log does not say hidden for a window program: " + shown);
                }
                Eq("True|False|True|False", string.Join("|", procs.Starts.Select(r => r.Hidden)),
                    "hidden only for console programs with the switch on; a window program is never hidden");

                AppSettings saved = SettingsJson.Read(SettingsJson.Write(SettingsWith(new RunProgramAction { Path = console, HideConsole = true })),
                    new List<string>());
                Check(((RunProgramAction)saved.Profiles[0].OnStart[0]).HideConsole, "switch kept when saved");
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        private static void ActionNames()
        {
            var named = new RunProgramAction { Name = "Close Voicemeeter", Path = "C:/Tools/vmrcli.exe", Arguments = "command.shutdown=1" };
            AppSettings saved = SettingsJson.Read(SettingsJson.Write(SettingsWith(named)), new List<string>());
            Eq("Close Voicemeeter", ((RunProgramAction)saved.Profiles[0].OnStart[0]).Name, "name kept when saved");
            Eq("Run program vmrcli.exe 'Close Voicemeeter'", named.Describe(), "name in the log");
            Eq("Run program vmrcli.exe", new RunProgramAction { Path = "C:/Tools/vmrcli.exe" }.Describe(), "no name: log unchanged");
            Eq("Run program vmrcli.exe", new RunProgramAction { Path = "\"C:/Tools/vmrcli.exe\"" }.Describe(), "quotes from Explorer: left out of the log");
            Check(new RunProgramAction { Path = "C:/Tools/a|b.exe" }.Describe().StartsWith("Run program ", StringComparison.Ordinal),
                "a mistyped path still gives a log line");
            Profile copy = ProfileCopy.Duplicate(saved.Profiles[0], "P (copy)");
            Eq("Close Voicemeeter", ((RunProgramAction)copy.OnStart[0]).Name, "name kept in a duplicated profile");

            var close = new CloseProgramAction { Name = "Stop Steam", Target = "steam.exe" };
            Eq("Stop Steam", ((CloseProgramAction)SettingsJson.ReadAction(SettingsJson.WriteAction(close))).Name, "Close program: name kept");
            Eq("Close program steam.exe 'Stop Steam'", close.Describe(), "Close program: name in the log");
            var keys = new PressHotkeyAction { Name = "RTSS normal", Keys = new KeyCombination { Ctrl = true, Alt = true, Key = 0x31 } };
            Eq("RTSS normal", ((PressHotkeyAction)SettingsJson.ReadAction(SettingsJson.WriteAction(keys))).Name, "Hotkey: name kept");
            Eq("Hotkey Ctrl+Alt+1 'RTSS normal'", keys.Describe(), "Hotkey: name in the log");

            var display = new DisplayAction { Name = "Not offered" };
            JsonValue written = SettingsJson.WriteAction(display);
            Check(written["name"] == null, "Display: no name written");
            Eq("Display", display.Describe(), "Display: no name in the log");
            Eq("", SettingsJson.ReadAction(written.Set("name", "Edited by hand")).Name, "Display: a name in the file is ignored");
        }

        private static AppSettings SettingsWith(ProfileAction action)
        {
            var settings = new AppSettings();
            var profile = new Profile { Name = "P" };
            profile.OnStart.Add(action);
            settings.Profiles.Add(profile);
            return settings;
        }

        private static byte[] PeHeader(ushort subsystem)
        {
            var bytes = new byte[512];
            bytes[0] = (byte)'M';
            bytes[1] = (byte)'Z';
            BitConverter.GetBytes(0x80).CopyTo(bytes, 0x3C);
            bytes[0x80] = (byte)'P';
            bytes[0x81] = (byte)'E';
            BitConverter.GetBytes(subsystem).CopyTo(bytes, 0x80 + 4 + 20 + 68);
            return bytes;
        }

        private static void SavedWithoutLoss(string fixture, AppSettings read, string label)
        {
            JsonValue rewritten = SettingsJson.Write(read);
            var lost = new List<string>();
            SameValues(JsonValue.Parse(fixture), rewritten, "settings", lost);
            Eq(0, lost.Count, label + ": nothing lost when saved again: " + string.Join(", ", lost));
            string once = rewritten.ToJson();
            string twice = SettingsJson.Write(SettingsJson.Read(JsonValue.Parse(once), new List<string>())).ToJson();
            Eq(once, twice, label + ": saving again changes nothing more");
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
