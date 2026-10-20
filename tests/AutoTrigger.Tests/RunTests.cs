using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using AutoTrigger.Core.Engine;
using AutoTrigger.Core.Json;
using AutoTrigger.Core.Model;

namespace AutoTrigger.Tests
{
    public sealed class FastRig
    {
        public readonly FakeScheduler Sched = new FakeScheduler();
        public readonly FakeProcessSystem Ps;
        public readonly FakeForeground Fg = new FakeForeground();
        public readonly TriggerEngine Engine;
        public readonly List<string> Calls = new List<string>();
        public readonly Dictionary<string, TaskCompletionSource<ActionOutcome>> Blocked = new Dictionary<string, TaskCompletionSource<ActionOutcome>>();
        public readonly Dictionary<string, TaskCompletionSource<bool>> Holding = new Dictionary<string, TaskCompletionSource<bool>>();

        public FastRig(AppSettings settings)
        {
            Ps = new FakeProcessSystem(Sched);
            Engine = new TriggerEngine(settings, Ps, Sched, Execute, Fg);
        }

        private Task<ActionOutcome> Execute(ProfileAction action)
        {
            string name = action is RunProgramAction run ? System.IO.Path.GetFileNameWithoutExtension(run.Path) : action.Type;
            Calls.Add($"{name}@{Sched.NowMs}");
            if (name.StartsWith("block", StringComparison.Ordinal))
            {
                var tcs = new TaskCompletionSource<ActionOutcome>();
                Blocked[name] = tcs;
                return tcs.Task;
            }
            if (action is RunProgramAction r && r.HoldNext)
            {
                var exited = new TaskCompletionSource<bool>();
                Holding[name] = exited;
                return Task.FromResult(ActionOutcome.Success("started", exited.Task));
            }
            return Task.FromResult(ActionOutcome.Success("done"));
        }

        public long T() => Sched.NowMs;

        public long At(string name)
        {
            string call = Calls.FirstOrDefault(c => c.StartsWith(name + "@", StringComparison.Ordinal));
            return call == null ? -1 : long.Parse(call.Substring(call.IndexOf('@') + 1), CultureInfo.InvariantCulture);
        }

        public int Count(string name) => Calls.Count(c => c.StartsWith(name + "@", StringComparison.Ordinal));

        public int Index(string name) => Calls.FindIndex(c => c.StartsWith(name + "@", StringComparison.Ordinal));

        public void Stop()
        {
            Task t = Task.Run(() => Engine.Dispose());
            while (!t.IsCompleted)
                Sched.Advance(1);
        }
    }

    public sealed class FakeScreenSaver : IScreenSaver
    {
        public bool On = true;
        public bool Locked;
        public readonly List<string> Calls = new List<string>();

        public bool IsOn() => On;

        public void Set(bool on)
        {
            Calls.Add(on ? "on" : "off");
            if (!Locked)
                On = on;
        }
    }

    public static partial class Runner
    {
        private const string OtherGameExe = "/games/Other/Other.exe";

        private static (AppSettings settings, Profile a, Profile b) TwoGames()
        {
            var s = NewSettings();
            Profile a = AddProfile(s, "A");
            Profile b = AddProfile(s, "B");
            AddApp(s, "Days Gone", GameExe, a);
            AddApp(s, "Other", OtherGameExe, b);
            return (s, a, b);
        }

        private static void FastTrackGameSwitch()
        {
            var (s, a, b) = TwoGames();
            a.OnClose.Add(Run("/tools/a-off.exe", 5000));
            a.OnClose.Add(Run("/tools/a-after.exe", 0));
            b.OnStart.Add(Run("/tools/b-on.exe", 0));
            var rig = new FastRig(s);
            rig.Engine.Start();
            rig.Sched.Advance(10);
            FakeProc game = rig.Ps.Launch(GameExe);
            rig.Sched.Advance(1500);
            rig.Ps.Exit(game);
            rig.Sched.Advance(10);
            long closed = rig.T();
            rig.Sched.Advance(1000);
            Eq(-1L, rig.At("a-off"), "A's closed action still waiting in its delay");
            rig.Ps.Launch(OtherGameExe);
            rig.Sched.Advance(1500);
            long bOn = rig.At("b-on");
            Check(bOn > 0, "B started");
            Check(rig.At("a-off") > 0 && rig.At("a-off") < closed + 5000, "A's pending action fast-tracked instead of waiting 5 s");
            Check(rig.Index("a-off") < rig.Index("a-after") && rig.Index("a-after") < rig.Index("b-on"), "A finishes in order before B starts");
            rig.Sched.Advance(6000);
            Eq(1, rig.Count("a-off"), "A's action ran once, not again after its delay");
            rig.Stop();
        }

        private static void FastTrackShortcutDuringDelay()
        {
            var (s, a, b) = TwoGames();
            b.OnStart.Add(Run("/tools/b-later.exe", 3000));
            Profile desk = AddProfile(s, "Desktop");
            desk.OnStart.Add(Run("/tools/desk.exe", 0));
            var rig = new FastRig(s);
            rig.Engine.Start();
            rig.Sched.Advance(10);
            rig.Ps.Launch(OtherGameExe);
            rig.Sched.Advance(1100);
            long started = rig.T();
            Eq(-1L, rig.At("b-later"), "B's action waits for its 3 s delay");
            bool? found = null;
            rig.Engine.RunProfile(desk.Id, EventKind.Started, "shortcut Ctrl+Shift+D", ok => found = ok);
            rig.Sched.Advance(10);
            Eq(true, found, "shortcut ran the profile");
            Check(rig.At("b-later") >= 0 && rig.At("b-later") < started + 3000, "B's pending action fast-tracked");
            Check(rig.Index("b-later") < rig.Index("desk"), "the shortcut runs last, so it wins");
            rig.Sched.Advance(5000);
            Eq(1, rig.Count("b-later"), "B's action ran once");
            rig.Stop();
        }

        private static void FastTrackReleasesHold()
        {
            var (s, a, b) = TwoGames();
            a.OnClose.Add(Run("/tools/wait.exe", 0, hold: true));
            a.OnClose.Add(Run("/tools/next.exe", 2000));
            Profile desk = AddProfile(s, "Desktop");
            desk.OnStart.Add(Run("/tools/desk.exe", 0));
            var rig = new FastRig(s);
            rig.Engine.Start();
            rig.Sched.Advance(10);
            FakeProc game = rig.Ps.Launch(GameExe);
            rig.Sched.Advance(1500);
            rig.Ps.Exit(game);
            rig.Sched.Advance(1000);
            Eq(1, rig.Count("wait"), "wait.exe started and holds");
            Eq(-1L, rig.At("next"), "next held");
            long pressed = rig.T();
            rig.Engine.RunProfile(desk.Id, EventKind.Started, "shortcut");
            rig.Sched.Advance(10);
            Eq(pressed, rig.At("next"), "hold released and delay skipped");
            Check(rig.Index("next") < rig.Index("desk"), "then the shortcut profile");
            rig.Stop();
        }

        private static void FastTrackLimit()
        {
            var (s, a, b) = TwoGames();
            a.OnClose.Add(Run("/tools/block.exe", 0));
            a.OnClose.Add(Run("/tools/never.exe", 0));
            Profile desk = AddProfile(s, "Desktop");
            desk.OnStart.Add(Run("/tools/desk.exe", 0));
            var rig = new FastRig(s);
            rig.Engine.Start();
            rig.Sched.Advance(10);
            FakeProc game = rig.Ps.Launch(GameExe);
            rig.Sched.Advance(1500);
            rig.Ps.Exit(game);
            rig.Sched.Advance(100);
            long pressed = rig.T();
            rig.Engine.RunProfile(desk.Id, EventKind.Started, "shortcut");
            rig.Sched.Advance(9000);
            Eq(-1L, rig.At("desk"), "the shortcut waits while A's action is still running");
            rig.Sched.Advance(1100);
            Check(rig.At("desk") >= pressed + TriggerEngine.FastTrackLimitMs, "after 10 s the shortcut starts anyway");
            Eq(-1L, rig.At("never"), "A's remaining actions cancelled at the limit");
            rig.Blocked["block"].TrySetResult(ActionOutcome.Success("late"));
            rig.Sched.Advance(100);
            Eq(-1L, rig.At("never"), "and they stay cancelled");
            rig.Stop();
        }

        private static void ManualRunRules()
        {
            var s = NewSettings();
            Profile m = AddProfile(s, "Overlay");
            m.OnStart.Add(Run("/tools/m1.exe", 0));
            m.OnStart.Add(Run("/tools/m2.exe", 5000));
            m.RestartApp = true;
            var rig = new FastRig(s);
            rig.Engine.Start(paused: true);
            rig.Sched.Advance(10);
            rig.Engine.RunProfile(m.Id, EventKind.Started, "shortcut");
            rig.Sched.Advance(1000);
            Eq(1, rig.Count("m1"), "works while monitoring is paused");
            rig.Engine.RunProfile(m.Id, EventKind.Started, "shortcut");
            rig.Sched.Advance(10);
            Eq(2, rig.Count("m1"), "pressed again: runs again");
            rig.Sched.Advance(4500);
            Eq(0, rig.Count("m2"), "the first run's pending action was replaced, not fast-tracked");
            rig.Sched.Advance(600);
            Eq(1, rig.Count("m2"), "the second run keeps its own delay");

            bool? found = null;
            rig.Engine.RunProfileNamed("  overlay ", EventKind.Started, "command line", ok => found = ok);
            rig.Sched.Advance(10);
            Eq(true, found, "found by name, case and spaces ignored");
            rig.Engine.RunProfileNamed("Nope", EventKind.Started, "command line", ok => found = ok);
            rig.Sched.Advance(10);
            Eq(false, found, "unknown name reported");
            rig.Engine.RunProfile(Guid.NewGuid(), EventKind.Started, "shortcut", ok => found = ok);
            rig.Sched.Advance(10);
            Eq(false, found, "deleted profile reported");
            rig.Stop();
        }

        private static void ManualClosedRun()
        {
            var s = NewSettings();
            Profile m = AddProfile(s, "Overlay");
            m.OnStart.Add(Run("/tools/open.exe", 0));
            m.OnClose.Add(Run("/tools/shut.exe", 0));
            var rig = new FastRig(s);
            rig.Engine.Start();
            rig.Sched.Advance(10);
            bool? found = null;
            rig.Engine.RunProfile(m.Id, EventKind.Closed, "shortcut", ok => found = ok);
            rig.Sched.Advance(100);
            Eq(true, found, "Closed shortcut ran the profile");
            Eq(1, rig.Count("shut"), "its Closed actions ran");
            Eq(0, rig.Count("open"), "its Started actions did not");
            rig.Engine.RunProfileNamed("overlay", EventKind.Closed, "command line", ok => found = ok);
            rig.Sched.Advance(100);
            Eq(2, rig.Count("shut"), "/closed runs the Closed actions");
            Eq(0, rig.Count("open"), "and not the Started ones");
            rig.Engine.RunProfileNamed("overlay", EventKind.Started, "command line");
            rig.Sched.Advance(100);
            Eq(1, rig.Count("open"), "/started runs the Started actions");
            rig.Stop();
        }

        private static void RunningProgramsList()
        {
            var procs = new FakeProcessSystem(new FakeScheduler());
            FakeProc self = procs.Launch(@"C:\Tools\AutoTrigger.exe");
            procs.Launch(@"C:\Windows\System32\win32calc.exe");
            procs.Launch(@"C:\Windows\System32\svchost.exe").HasWindow = false;
            procs.Launch(@"C:\Windows\System32\svchost.exe").HasWindow = false;
            procs.Launch(@"C:\Windows\notepad.exe");
            procs.Launch(@"C:\Windows\notepad.exe");
            procs.Launch(@"C:\Windows\notepad.exe").HasWindow = false;
            procs.Launch(@"C:\Program Files\Tray\tray.exe").HasWindow = false;
            procs.Launch(@"C:\Program Files\Tray\tray.exe");
            procs.Launch(@"C:\WindowsApps\game.exe").HasWindow = false;
            procs.Launch(@"C:\Games\hidden.exe").Readable = false;
            procs.Launch(@"C:\Windows\System32\dllhost.exe").HasWindow = false;
            procs.Launch(@"C:\WINDOWS\explorer.exe");

            List<string> paths = RunningPrograms.Read(procs, self.Pid, @"C:\Windows\").Select(p => p.Path).ToList();
            Check(paths.Contains(@"C:\Windows\System32\win32calc.exe"), "a Windows tool with a window is listed");
            Check(paths.Contains(@"C:\WINDOWS\explorer.exe"), "the Windows folder is matched without case");
            Check(!paths.Contains(@"C:\Windows\System32\svchost.exe") && !paths.Contains(@"C:\Windows\System32\dllhost.exe"),
                "Windows background processes stay hidden");
            Check(paths.Contains(@"C:\Program Files\Tray\tray.exe"), "a tray app without a window stays listed");
            Check(paths.Contains(@"C:\WindowsApps\game.exe"), "a folder that only starts like Windows is not the Windows folder");
            Check(!paths.Contains(@"C:\Tools\AutoTrigger.exe"), "AutoTrigger itself is not listed");
            Check(!paths.Contains(@"C:\Games\hidden.exe"), "a program whose path cannot be read is not listed");
            Eq(1, paths.Count(p => p == @"C:\Windows\notepad.exe"), "one line for three notepad instances");
            Eq(1, paths.Count(p => p == @"C:\Program Files\Tray\tray.exe"), "one line when an instance passes both tests");
            Eq(paths.Count, paths.Distinct(StringComparer.OrdinalIgnoreCase).Count(), "no path listed twice");
            Eq(5, paths.Count, "five programs listed");
        }

        private static void FastTrackSkipsEmptyAndSameApp()
        {
            var (s, a, b) = TwoGames();
            a.OnStart.Add(Run("/tools/a-late.exe", 3000));
            a.OnFocus.Add(Run("/tools/a-focus.exe", 0));
            var rig = new FastRig(s);
            rig.Engine.Start();
            rig.Sched.Advance(10);
            FakeProc game = rig.Ps.Launch(GameExe);
            rig.Fg.Set(game.Pid);
            rig.Sched.Advance(1100);
            Check(rig.Count("a-focus") == 1, "own focus event ran");
            rig.Ps.Launch(OtherGameExe);
            rig.Sched.Advance(1100);
            rig.Sched.Advance(2000);
            long late = rig.At("a-late");
            Check(late >= 3000, "neither the same app's focus nor an app without actions fast-tracks the delay (ran at " + late + ")");
            rig.Stop();
        }

        private static void StoreAppFrameFocus()
        {
            var s = NewSettings();
            Profile p = AddProfile(s, "Settings");
            p.OnFocus.Add(Run("/tools/focus.exe", 0));
            AddApp(s, "Settings", "/windows/SystemSettings.exe", p);
            var rig = new FastRig(s);
            FakeProc explorer = rig.Ps.Launch("/windows/explorer.exe");
            rig.Fg.Pid = explorer.Pid;
            rig.Engine.Start();
            rig.Sched.Advance(10);
            FakeProc app = rig.Ps.Launch("/windows/SystemSettings.exe");
            rig.Sched.Advance(1100);
            rig.Fg.Set(ForegroundIds.EmptyFrame);
            rig.Sched.Advance(600);
            Eq(0, rig.Count("focus"), "empty frame: not yet");
            rig.Fg.Pid = app.Pid;
            rig.Sched.Advance(600);
            Eq(1, rig.Count("focus"), "the app attached to its frame: gained focus without a new foreground event");

            rig.Fg.Set(explorer.Pid);
            rig.Sched.Advance(400);
            rig.Fg.Set(ForegroundIds.EmptyFrame);
            rig.Sched.Advance(3000);
            Eq(1, rig.Count("focus"), "a frame that stays empty is another program");
            rig.Stop();
        }

        private static void ScreenSaverActions()
        {
            var saver = new FakeScreenSaver();
            using var exec = new ActionExecutor(new FakeProcessSystem(new FakeScheduler()), null, null, null, null, saver);
            ActionOutcome r = exec.ExecuteAsync(new WindowsSettingsAction { ScreenSaverOn = false }).Result;
            Eq(ActionResult.Succeeded, r.Result, "off: result");
            Eq(false, saver.On, "off: applied");
            r = exec.ExecuteAsync(new WindowsSettingsAction { ScreenSaverOn = false }).Result;
            Eq("screen saver already off", r.Message, "already off: nothing sent");
            Eq(1, saver.Calls.Count, "only one call");
            saver.Locked = true;
            r = exec.ExecuteAsync(new WindowsSettingsAction { ScreenSaverOn = true }).Result;
            Eq(ActionResult.Failed, r.Result, "kept by Windows (policy): fails");
            using (var bare = new ActionExecutor(new FakeProcessSystem(new FakeScheduler())))
                Eq(ActionResult.Skipped, bare.ExecuteAsync(new WindowsSettingsAction { ScreenSaverOn = true }).Result.Result, "not available: skipped");

            var s = new AppSettings();
            var p = new Profile { Name = "P" };
            p.OnStart.Add(new WindowsSettingsAction { ScreenSaverOn = false, DelayMs = 2000 });
            p.OnClose.Add(new WindowsSettingsAction { ScreenSaverOn = true });
            s.Profiles.Add(p);
            AppSettings back = SettingsJson.Read(SettingsJson.Write(s), new List<string>());
            var off = back.Profiles[0].OnStart[0] as WindowsSettingsAction;
            var on = back.Profiles[0].OnClose[0] as WindowsSettingsAction;
            Check(off != null && off.ScreenSaverOn == false && off.DelayMs == 2000 && on != null && on.ScreenSaverOn == true, "saved and read back");
        }

        private static void ProfileShortcuts()
        {
            var s = new AppSettings();
            var p = new Profile { Name = "Overlay", Shortcut = new KeyCombination { Ctrl = true, Shift = true, Key = 0x44 } };
            s.Profiles.Add(p);
            s.Profiles.Add(new Profile { Name = "None" });
            JsonValue written = SettingsJson.Write(s);
            Check(written.ToJson(false).Contains("\"keys\":\"Ctrl+Shift+D\""), "readable shortcut in settings.json");
            AppSettings back = SettingsJson.Read(written, new List<string>());
            Check(p.Shortcut.Equals(back.Profiles[0].Shortcut), "shortcut saved and read back");
            Check(back.Profiles[1].Shortcut == null, "no shortcut stays empty");
            Check(ProfileCopy.Duplicate(back.Profiles[0], "copy").Shortcut == null, "a duplicated profile does not take the shortcut");

            p.ClosedShortcut = new KeyCombination { Alt = true, Key = 0x43 };
            written = SettingsJson.Write(s);
            Check(written.ToJson(false).Contains("\"closedShortcut\":{\"keys\":\"Alt+C\""), "readable Closed shortcut in settings.json");
            back = SettingsJson.Read(written, new List<string>());
            Check(p.ClosedShortcut.Equals(back.Profiles[0].ClosedShortcut) && p.Shortcut.Equals(back.Profiles[0].Shortcut),
                "both shortcuts saved and read back");
            Check(back.Profiles[1].ClosedShortcut == null, "no Closed shortcut stays empty");
            Check(ProfileCopy.Duplicate(back.Profiles[0], "copy").ClosedShortcut == null, "a duplicated profile does not take the Closed shortcut");
            Check(back.Profiles[0].ShortcutFor(EventKind.Closed).Equals(p.ClosedShortcut) && back.Profiles[0].ShortcutFor(EventKind.GotFocus) == null,
                "shortcut by event");

            JsonValue old = JsonValue.Parse("{\"version\":1,\"profiles\":[{\"id\":\"3f1d2c4b-5a69-4788-9abc-def012345678\",\"name\":\"Old\","
                + "\"shortcut\":{\"keys\":\"Ctrl+Shift+D\",\"ctrl\":true,\"alt\":false,\"shift\":true,\"key\":68},"
                + "\"onStart\":[],\"onClose\":[],\"onFocus\":[],\"onFocusLost\":[]}]}");
            AppSettings older = SettingsJson.Read(old, new List<string>());
            Check(older.Profiles[0].Shortcut != null && older.Profiles[0].Shortcut.Equals(new KeyCombination { Ctrl = true, Shift = true, Key = 0x44 }),
                "a 1.1.0 shortcut is read as the Started shortcut");
            Check(older.Profiles[0].ClosedShortcut == null, "a 1.1.0 profile has no Closed shortcut");

            Check(KeyNames.CanBeShortcut(new KeyCombination { Ctrl = true, Key = 0x31 }), "Ctrl+1");
            Check(KeyNames.CanBeShortcut(new KeyCombination { Alt = true, Key = 0x70 }), "Alt+F1");
            Check(KeyNames.CanBeShortcut(new KeyCombination { Key = 0x7C }), "F13 alone");
            Check(!KeyNames.CanBeShortcut(new KeyCombination { Key = 0x41 }), "a letter alone");
            Check(!KeyNames.CanBeShortcut(new KeyCombination { Shift = true, Key = 0x41 }), "Shift+letter");
            Check(!KeyNames.CanBeShortcut(new KeyCombination { Key = 0x70 }), "F1 alone");
            Check(!KeyNames.CanBeShortcut(null), "none");
        }
    }
}
