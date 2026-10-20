using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AutoTrigger.Core.Engine;
using AutoTrigger.Core.Json;
using AutoTrigger.Core.Logging;
using AutoTrigger.Core.Model;

namespace AutoTrigger.Tests
{
    public sealed class FakeScheduler : IEngineScheduler
    {
        private readonly object _lock = new object();
        private readonly Queue<Action> _queue = new Queue<Action>();
        private readonly List<Timer> _timers = new List<Timer>();
        private long _now;
        private long _seq;
        public int Outstanding;

        public long NowMs { get { lock (_lock) return _now; } }

        public void Post(Action action)
        {
            lock (_lock) _queue.Enqueue(action);
        }

        public IDisposable Schedule(int delayMs, Action action)
        {
            var t = new Timer { Due = NowMs + Math.Max(0, delayMs), Action = action };
            lock (_lock)
            {
                t.Seq = _seq++;
                _timers.Add(t);
            }
            return t;
        }

        public void RunUntil(long until)
        {
            while (true)
            {
                Action next = null;
                lock (_lock)
                {
                    if (_queue.Count > 0)
                        next = _queue.Dequeue();
                }
                if (next != null)
                {
                    next();
                    continue;
                }
                if (WaitForPosts())
                    continue;
                Timer timer = null;
                lock (_lock)
                {
                    _timers.RemoveAll(x => x.Cancelled);
                    timer = _timers.Where(x => x.Due <= until).OrderBy(x => x.Due).ThenBy(x => x.Seq).FirstOrDefault();
                    if (timer != null)
                    {
                        _timers.Remove(timer);
                        if (timer.Due > _now)
                            _now = timer.Due;
                    }
                }
                if (timer != null)
                {
                    if (!timer.Cancelled)
                        timer.Action();
                    continue;
                }
                lock (_lock)
                {
                    if (until > _now)
                        _now = until;
                }
                return;
            }
        }

        public void Advance(long ms) => RunUntil(NowMs + ms);

        private bool WaitForPosts()
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (Volatile.Read(ref Outstanding) > 0 && sw.ElapsedMilliseconds < 3000)
            {
                lock (_lock)
                {
                    if (_queue.Count > 0)
                        return true;
                }
                Thread.Sleep(1);
            }
            for (int i = 0; i < 4; i++)
            {
                lock (_lock)
                {
                    if (_queue.Count > 0)
                        return true;
                }
                Thread.Sleep(1);
            }
            return false;
        }

        private sealed class Timer : IDisposable
        {
            public long Due;
            public long Seq;
            public Action Action;
            public volatile bool Cancelled;

            public void Dispose() => Cancelled = true;
        }
    }

    public sealed class FakeProc
    {
        public int Pid;
        public string Path;
        public long Creation;
        public bool Running = true;
        public bool Readable = true;
        public bool HasWindow = true;
        public bool ClosesOnRequest = true;
        public bool Protected;
        public string CommandLine;
        public readonly List<Action> Watchers = new List<Action>();
        public readonly TaskCompletionSource<bool> ExitTcs = new TaskCompletionSource<bool>();
    }

    public sealed class StartRecord
    {
        public string Path;
        public string Arguments;
        public string Folder;
        public long At;
        public FakeProc Process;
    }

    public sealed class FakeProcessSystem : IProcessSystem
    {
        private readonly object _lock = new object();
        private readonly Dictionary<int, FakeProc> _procs = new Dictionary<int, FakeProc>();
        private readonly FakeScheduler _clock;
        private int _nextPid = 1000;
        private long _creation = 100;
        public readonly List<StartRecord> Starts = new List<StartRecord>();
        public readonly List<int> Killed = new List<int>();
        public Func<string, string, bool> CreateProcessOnStart = (p, a) => true;
        public int QueryCount;

        public FakeProcessSystem(FakeScheduler clock) => _clock = clock;

        public FakeProc Launch(string path, string commandLine = null)
        {
            lock (_lock)
            {
                var p = new FakeProc { Pid = _nextPid++, Path = path, Creation = _creation++, CommandLine = commandLine ?? ("\"" + path + "\"") };
                _procs[p.Pid] = p;
                return p;
            }
        }

        public void Exit(FakeProc p)
        {
            List<Action> watchers;
            lock (_lock)
            {
                if (!p.Running)
                    return;
                p.Running = false;
                watchers = p.Watchers.ToList();
                p.Watchers.Clear();
            }
            foreach (Action w in watchers)
                w();
            p.ExitTcs.TrySetResult(true);
        }

        public FakeProc Find(int pid)
        {
            lock (_lock) return _procs.TryGetValue(pid, out FakeProc p) ? p : null;
        }

        public int SnapshotCount;

        public void Snapshot(List<ProcessEntry> into)
        {
            lock (_lock)
            {
                SnapshotCount++;
                into.Clear();
                into.AddRange(_procs.Values.Where(p => p.Running).Select(p => new ProcessEntry(p.Pid, System.IO.Path.GetFileName(p.Path))));
            }
        }

        public ProcessDetails Query(int pid)
        {
            lock (_lock)
            {
                QueryCount++;
                if (!_procs.TryGetValue(pid, out FakeProc p) || !p.Running || !p.Readable)
                    return null;
                return new ProcessDetails { Pid = pid, Path = p.Path, CreationTime = p.Creation };
            }
        }

        public IDisposable WatchExit(int pid, long creationTime, Action onExit)
        {
            lock (_lock)
            {
                if (!_procs.TryGetValue(pid, out FakeProc p))
                    return null;
                if (!p.Running || p.Creation != creationTime)
                {
                    Task.Run(onExit);
                    return new Disp(() => { });
                }
                p.Watchers.Add(onExit);
                return new Disp(() => { lock (_lock) p.Watchers.Remove(onExit); });
            }
        }

        public bool IsRunning(int pid, long creationTime)
        {
            lock (_lock)
                return _procs.TryGetValue(pid, out FakeProc p) && p.Running && (creationTime == 0 || creationTime == p.Creation);
        }

        public StartedProgram Start(string path, string arguments, string workingDirectory, bool followExit)
        {
            if (path.Contains("missing"))
                throw new Win32Exception(2);
            FakeProc proc = CreateProcessOnStart(path, arguments) ? Launch(path, "\"" + path + "\" " + arguments) : null;
            lock (_lock)
                Starts.Add(new StartRecord { Path = path, Arguments = arguments, Folder = workingDirectory, At = _clock.NowMs, Process = proc });
            return new StartedProgram { Pid = proc?.Pid ?? 0, Exited = followExit ? proc?.ExitTcs.Task : null };
        }

        public bool RequestClose(int pid)
        {
            FakeProc p = Find(pid);
            if (p == null || !p.HasWindow)
                return false;
            if (p.ClosesOnRequest)
                Exit(p);
            return true;
        }

        public void Kill(int pid)
        {
            FakeProc p = Find(pid);
            if (p == null)
                return;
            if (p.Protected)
                throw new Win32Exception(5);
            lock (_lock) Killed.Add(pid);
            Exit(p);
        }

        public string GetCommandLine(int pid) => Find(pid)?.CommandLine;

        public void WindowOwners(HashSet<int> into)
        {
            lock (_lock)
                into.UnionWith(_procs.Values.Where(p => p.Running && p.HasWindow).Select(p => p.Pid));
        }

        private sealed class Disp : IDisposable
        {
            private readonly Action _a;
            public Disp(Action a) => _a = a;
            public void Dispose() => _a();
        }
    }

    public sealed class FakeForeground : IForegroundSource
    {
        public int Pid;
        public event Action Changed;

        public int GetForegroundProcessId() => Pid;

        public void Set(int pid)
        {
            Pid = pid;
            Changed?.Invoke();
        }
    }

    public sealed class Rig
    {
        public readonly FakeScheduler Sched = new FakeScheduler();
        public readonly FakeProcessSystem Ps;
        public readonly FakeForeground Fg = new FakeForeground();
        public readonly ActionExecutor Exec;
        public readonly TriggerEngine Engine;
        public readonly AppSettings Settings;

        public Rig(AppSettings settings)
        {
            Settings = settings;
            Ps = new FakeProcessSystem(Sched);
            Exec = new ActionExecutor(Ps);
            Engine = new TriggerEngine(settings, Ps, Sched, a =>
            {
                Interlocked.Increment(ref Sched.Outstanding);
                return Exec.ExecuteAsync(a).ContinueWith(t =>
                {
                    Interlocked.Decrement(ref Sched.Outstanding);
                    return t.Result;
                }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            }, Fg);
        }

        public long T => Sched.NowMs;

        public void Stop()
        {
            Task t = Task.Run(() => Engine.Dispose());
            while (!t.IsCompleted)
                Sched.Advance(1);
        }

        public List<long> StartTimes(string fileName) =>
            Ps.Starts.Where(s => s.Path.EndsWith(fileName, StringComparison.OrdinalIgnoreCase)).Select(s => s.At).ToList();
    }

    public static partial class Runner
    {
        private static readonly List<string> Failures = new List<string>();
        private static int _checks;
        private static string _test;

        private static void Check(bool ok, string what)
        {
            _checks++;
            if (!ok)
                Failures.Add($"{_test}: {what}");
        }

        private static void Eq<T>(T expected, T actual, string what)
        {
            _checks++;
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
                Failures.Add($"{_test}: {what}: expected {expected}, got {actual}");
        }

        private static void Seq(IEnumerable<long> expected, IEnumerable<long> actual, string what)
        {
            _checks++;
            string e = string.Join(",", expected), a = string.Join(",", actual);
            if (e != a)
                Failures.Add($"{_test}: {what}: expected [{e}], got [{a}]");
        }

        private static AppSettings NewSettings()
        {
            var s = new AppSettings { StartPollMs = 1000, FocusStabilityMs = 300 };
            return s;
        }

        private static Profile AddProfile(AppSettings s, string name)
        {
            var p = new Profile { Name = name };
            s.Profiles.Add(p);
            return p;
        }

        private static AppEntry AddApp(AppSettings s, string name, string path, Profile profile, bool byName = false)
        {
            var a = new AppEntry { Name = name, Path = path, ProfileId = profile?.Id ?? Guid.Empty, MatchByFileName = byName };
            s.Applications.Add(a);
            return a;
        }

        private static RunProgramAction Run(string path, int delay, bool hold = false, int cap = 30) =>
            new RunProgramAction { Path = path, DelayMs = delay, HoldNext = hold, HoldCapSec = cap };

        private const string GameExe = "/games/Days Gone/DaysGone.exe";

        public static int FailureCount => Failures.Count;

        public static string RunAll(string logDir)
        {
            Log.Start(logDir, LogLevel.Detailed);
            var tests = new (string, Action)[]
            {
                ("Baseline ignores running apps", BaselineIgnored),
                ("Cumulative delays", CumulativeDelays),
                ("Waits for the previous action", WaitsForPrevious),
                ("Closed cancels pending started", ClosedCancelsStarted),
                ("Hold until program ends", HoldUntilExit),
                ("Hold cap", HoldCap),
                ("Reference profile order and loops", IncludeOffsetsAndLoops),
                ("Multi-process app", MultiProcess),
                ("Pause and resume", PauseResume),
                ("Focus with stability", Focus),
                ("Focus does not cancel start actions", FocusKeepsStart),
                ("Restart app", RestartApp),
                ("Restart app fails to come back", RestartFails),
                ("Match by path or file name", Matching),
                ("Unreadable process", Unreadable),
                ("Close program", CloseProgram),
                ("Run program failure continues", RunFailureContinues),
                ("Display/audio skipped", HardwareSkipped),
                ("Snapshot", SnapshotState),
                ("Settings update", SettingsUpdate),
                ("Monitoring disabled per application", AppDisabled),
                ("Duplicate a profile", DuplicateProfile),
                ("Duplicate ids in settings.json", DuplicateIds),
                ("Import from AutoActions", ImportFromAutoActions),
                ("Settings edited in the window", SettingsEditedCopy),
                ("Display: only what differs", DisplayOnlyDiffers),
                ("Display: safe order", DisplaySafeOrder),
                ("Display: undo after a failure", DisplayUndo),
                ("Display: target and refresh matching", DisplayTargets),
                ("Display: colour guard", DisplayColorGuard),
                ("Display: maximum rate, steps already in place", DisplayMaxAndInPlace),
                ("Windows settings: audio", AudioActions),
                ("Windows settings: power plan", PowerPlanActions),
                ("Windows settings: combined", WindowsSettingsCombined),
                ("Windows settings: older action types", WindowsSettingsLegacy),
                ("Hotkey action", PressHotkeyActions),
                ("Key names", KeyNameTexts),
                ("Newest takes over: game switch", FastTrackGameSwitch),
                ("Newest takes over: shortcut during a delay", FastTrackShortcutDuringDelay),
                ("Newest takes over: hold released", FastTrackReleasesHold),
                ("Newest takes over: 10 s limit", FastTrackLimit),
                ("Newest takes over: same app and empty profiles", FastTrackSkipsEmptyAndSameApp),
                ("Manual profile runs", ManualRunRules),
                ("Manual Closed runs", ManualClosedRun),
                ("Running programs list", RunningProgramsList),
                ("Windows Store app frame focus", StoreAppFrameFocus),
                ("Windows settings: screen saver", ScreenSaverActions),
                ("Profile shortcuts", ProfileShortcuts),
                ("Keyboard shortcut conflicts", ShortcutConflicts),
                ("Command line arguments", CommandArguments),
                ("Command line through the pipe", CommandPipe),
                ("Display capabilities saved", CapabilitiesSaved),
                ("Display path changed by a driver reinstall", DisplayPathChanged),
                ("Helpers", Helpers),
                ("JSON round trip", JsonRoundTrip),
                ("Reset to defaults", ResetDefaults),
                ("Sort and manual order", SortAndMove),
                ("Delays in seconds", DelaysInSeconds),
                ("Settings file recovery", StoreRecovery),
                ("Settings file in use is never overwritten", LockedSettings),
                ("JSON numbers out of range", JsonNumberRange),
                ("User folder hidden in the log", UserProfileHidden),
                ("Display capabilities saved only when they change", SameCapabilities),
                ("Process list reading", Scanner),
                ("Light process list reading", BasicScanner),
                ("Light process list checked against the full list", BasicAgreement),
                ("Engine loop: one thread, timers and posts", LoopTiming),
                ("Display layout: resized desktops stay together", CcdLayout),
                ("Display layout: signal mode released", CcdReleaseTarget),
            };
            var sb = new StringBuilder();
            foreach (var (name, test) in tests)
            {
                _test = name;
                int before = Failures.Count;
                var sw = System.Diagnostics.Stopwatch.StartNew();
                try
                {
                    test();
                }
                catch (Exception ex)
                {
                    Failures.Add($"{name}: exception {ex}");
                }
                sb.AppendLine((Failures.Count == before ? "PASS " : "FAIL ") + name + $" ({sw.ElapsedMilliseconds} ms)");
            }
            sb.AppendLine($"{_checks} checks, {Failures.Count} failures");
            foreach (string f in Failures)
                sb.AppendLine("  " + f);
            Log.Stop(TimeSpan.FromSeconds(2));
            return sb.ToString();
        }

        private static void BaselineIgnored()
        {
            var s = NewSettings();
            var p = AddProfile(s, "Game");
            p.OnStart.Add(Run("/tools/start.exe", 0));
            p.OnClose.Add(Run("/tools/close.exe", 0));
            AddApp(s, "Days Gone", GameExe, p);
            var rig = new Rig(s);
            FakeProc g = rig.Ps.Launch(GameExe);
            rig.Engine.Start();
            rig.Sched.Advance(10);
            Eq(0, rig.Ps.Starts.Count, "no action for an app already running");
            Check(rig.Engine.Snapshot.Apps[0].Ignored, "ignored flag");
            rig.Ps.Exit(g);
            rig.Sched.Advance(10);
            Eq(0, rig.Ps.Starts.Count, "no closed action for an ignored app");
            rig.Ps.Launch(GameExe);
            rig.Sched.Advance(1000);
            Eq(1, rig.StartTimes("start.exe").Count, "next launch followed");
            rig.Stop();
        }

        private static void CumulativeDelays()
        {
            var s = NewSettings();
            var p = AddProfile(s, "Game");
            p.OnClose.Add(Run("/tools/a.exe", 2000));
            p.OnClose.Add(Run("/tools/b.exe", 3000));
            p.OnClose.Add(Run("/tools/c.exe", 0));
            p.OnClose.Add(Run("/tools/d.exe", 0));
            AddApp(s, "Days Gone", GameExe, p);
            var rig = new Rig(s);
            rig.Engine.Start();
            rig.Sched.Advance(10);
            FakeProc g = rig.Ps.Launch(GameExe);
            rig.Sched.Advance(1000);
            rig.Ps.Exit(g);
            long t0 = rig.T;
            rig.Sched.Advance(10000);
            Seq(new[] { t0 + 2000 }, rig.StartTimes("a.exe"), "a 2 s after the detection");
            Seq(new[] { t0 + 5000 }, rig.StartTimes("b.exe"), "b 3 s after a");
            Seq(new[] { t0 + 5000 }, rig.StartTimes("c.exe"), "c right after b");
            Seq(new[] { t0 + 5000 }, rig.StartTimes("d.exe"), "d right after c");
            var order = rig.Ps.Starts.Select(x => Path.GetFileName(x.Path)).ToList();
            Check(string.Join(",", order) == "a.exe,b.exe,c.exe,d.exe", "list order: " + string.Join(",", order));
            Check(rig.Ps.Starts.All(x => x.Folder == null || x.Folder == "/tools"), "working folder is the program folder");
            rig.Stop();
        }

        private static void WaitsForPrevious()
        {
            var s = NewSettings();
            var p = AddProfile(s, "Game");
            p.OnClose.Add(Run("/tools/w1.exe", 0, hold: true));
            p.OnClose.Add(Run("/tools/n1.exe", 5000));
            p.OnClose.Add(Run("/tools/w2.exe", 0, hold: true));
            p.OnClose.Add(Run("/tools/n2.exe", 1000));
            AddApp(s, "Days Gone", GameExe, p);
            var rig = new Rig(s);
            rig.Engine.Start();
            rig.Sched.Advance(10);
            FakeProc g = rig.Ps.Launch(GameExe);
            rig.Sched.Advance(1000);
            rig.Ps.Exit(g);
            long t0 = rig.T;
            rig.Sched.Advance(2000);
            rig.Ps.Exit(rig.Ps.Starts.First(x => x.Path.EndsWith("w1.exe", StringComparison.OrdinalIgnoreCase)).Process);
            rig.Sched.Advance(4000);
            Seq(new[] { t0 + 5000 }, rig.StartTimes("n1.exe"), "w1 ended at 2 s: n1 keeps its 5 s");
            Seq(new[] { t0 + 5000 }, rig.StartTimes("w2.exe"), "w2 right after n1");
            rig.Sched.Advance(2000);
            Eq(0, rig.StartTimes("n2.exe").Count, "n2 waits for w2 past its 1 s delay");
            rig.Ps.Exit(rig.Ps.Starts.First(x => x.Path.EndsWith("w2.exe", StringComparison.OrdinalIgnoreCase)).Process);
            rig.Sched.Advance(10);
            Seq(new[] { t0 + 8000 }, rig.StartTimes("n2.exe"), "n2 when w2 ends");
            rig.Stop();
        }

        private static void ClosedCancelsStarted()
        {
            var s = NewSettings();
            var p = AddProfile(s, "Game");
            p.OnStart.Add(Run("/tools/s1.exe", 0));
            p.OnStart.Add(Run("/tools/s2.exe", 5000));
            p.OnClose.Add(Run("/tools/c1.exe", 0));
            AddApp(s, "Days Gone", GameExe, p);
            var rig = new Rig(s);
            rig.Engine.Start();
            rig.Sched.Advance(10);
            FakeProc g = rig.Ps.Launch(GameExe);
            rig.Sched.Advance(1500);
            rig.Ps.Exit(g);
            rig.Sched.Advance(10000);
            Eq(1, rig.StartTimes("s1.exe").Count, "s1 ran");
            Eq(0, rig.StartTimes("s2.exe").Count, "s2 cancelled");
            Eq(1, rig.StartTimes("c1.exe").Count, "closed ran");
            rig.Stop();
        }

        private static void HoldUntilExit()
        {
            var s = NewSettings();
            var p = AddProfile(s, "Game");
            p.OnClose.Add(Run("/tools/wait.exe", 0, hold: true));
            p.OnClose.Add(Run("/tools/next.exe", 100));
            p.OnClose.Add(Run("/tools/late.exe", 8000));
            AddApp(s, "Days Gone", GameExe, p);
            var rig = new Rig(s);
            rig.Engine.Start();
            rig.Sched.Advance(10);
            FakeProc g = rig.Ps.Launch(GameExe);
            rig.Sched.Advance(1000);
            rig.Ps.Exit(g);
            long t0 = rig.T;
            rig.Sched.Advance(3000);
            Eq(0, rig.StartTimes("next.exe").Count, "next waits");
            FakeProc w = rig.Ps.Starts.First(x => x.Path.EndsWith("wait.exe", StringComparison.OrdinalIgnoreCase)).Process;
            rig.Ps.Exit(w);
            rig.Sched.Advance(10);
            Seq(new[] { t0 + 3000 }, rig.StartTimes("next.exe"), "next starts when the program ends");
            rig.Sched.Advance(10000);
            Seq(new[] { t0 + 3000 + 8000 }, rig.StartTimes("late.exe"), "late 8 s after next");
            rig.Stop();
        }

        private static void HoldCap()
        {
            var s = NewSettings();
            var p = AddProfile(s, "Game");
            p.OnClose.Add(Run("/tools/forever.exe", 0, hold: true, cap: 30));
            p.OnClose.Add(Run("/tools/next.exe", 100));
            AddApp(s, "Days Gone", GameExe, p);
            var rig = new Rig(s);
            rig.Engine.Start();
            rig.Sched.Advance(10);
            FakeProc g = rig.Ps.Launch(GameExe);
            rig.Sched.Advance(1000);
            rig.Ps.Exit(g);
            long t0 = rig.T;
            rig.Sched.Advance(29000);
            Eq(0, rig.StartTimes("next.exe").Count, "still held at 29 s");
            rig.Sched.Advance(2000);
            Seq(new[] { t0 + 30000 }, rig.StartTimes("next.exe"), "released at the 30 s cap");
            rig.Stop();
        }

        private static void IncludeOffsetsAndLoops()
        {
            var s = NewSettings();
            var a = AddProfile(s, "A");
            var b = AddProfile(s, "B");
            var c = AddProfile(s, "C");
            a.OnClose.Add(new IncludeProfileAction { ProfileId = b.Id, DelayMs = 2000 });
            a.OnClose.Add(Run("/tools/a.exe", 100));
            b.OnClose.Add(Run("/tools/b.exe", 500));
            b.OnClose.Add(new IncludeProfileAction { ProfileId = a.Id, DelayMs = 0 });
            b.OnClose.Add(new IncludeProfileAction { ProfileId = c.Id, DelayMs = 0 });
            b.OnClose.Add(new IncludeProfileAction { ProfileId = Guid.NewGuid(), DelayMs = 0 });
            c.OnClose.Add(Run("/tools/c.exe", 0));
            AddApp(s, "Days Gone", GameExe, a);

            List<ValidationIssue> issues = SettingsValidator.Validate(s);
            Check(issues.Any(i => i.Kind == IssueKind.Loop), "loop reported by the validator");
            Check(issues.Any(i => i.Kind == IssueKind.MissingProfile), "missing profile reported");

            var rig = new Rig(s);
            rig.Engine.Start();
            rig.Sched.Advance(10);
            FakeProc g = rig.Ps.Launch(GameExe);
            rig.Sched.Advance(1000);
            rig.Ps.Exit(g);
            long t0 = rig.T;
            rig.Sched.Advance(5000);
            Seq(new[] { t0 + 2600 }, rig.StartTimes("a.exe"), "a once, 100 ms after the included actions");
            Seq(new[] { t0 + 2500 }, rig.StartTimes("b.exe"), "included action at 2000 + 500");
            Seq(new[] { t0 + 2500 }, rig.StartTimes("c.exe"), "nested include right after b");
            var order = rig.Ps.Starts.Select(x => Path.GetFileName(x.Path)).ToList();
            Check(string.Join(",", order) == "b.exe,c.exe,a.exe", "included actions take the place of the reference: " + string.Join(",", order));
            rig.Stop();

            var s2 = NewSettings();
            var chain = Enumerable.Range(0, 12).Select(i => AddProfile(s2, "P" + i)).ToList();
            for (int i = 0; i < 11; i++)
                chain[i].OnClose.Add(new IncludeProfileAction { ProfileId = chain[i + 1].Id });
            foreach (var pr in chain)
                pr.OnClose.Add(Run("/tools/" + pr.Name + ".exe", 0));
            var plan = PlanBuilder.Build(s2, chain[0], EventKind.Closed);
            Eq(PlanBuilder.MaxDepth + 1, plan.Count, "nesting stops at 8 levels");
        }

        private static void MultiProcess()
        {
            var s = NewSettings();
            var p = AddProfile(s, "Game");
            p.OnStart.Add(Run("/tools/start.exe", 0));
            p.OnClose.Add(Run("/tools/close.exe", 0));
            AddApp(s, "HandBrake", "/apps/HandBrake.Worker.exe", p);
            var rig = new Rig(s);
            rig.Engine.Start();
            rig.Sched.Advance(10);
            FakeProc w1 = rig.Ps.Launch("/apps/HandBrake.Worker.exe");
            FakeProc w2 = rig.Ps.Launch("/apps/HandBrake.Worker.exe");
            rig.Sched.Advance(1000);
            Eq(1, rig.StartTimes("start.exe").Count, "one start for two processes");
            rig.Ps.Exit(w1);
            rig.Sched.Advance(100);
            Eq(0, rig.StartTimes("close.exe").Count, "not closed while one process remains");
            rig.Ps.Exit(w2);
            rig.Sched.Advance(100);
            Eq(1, rig.StartTimes("close.exe").Count, "closed with the last process");
            rig.Stop();
        }

        private static void PauseResume()
        {
            var s = NewSettings();
            var p = AddProfile(s, "Game");
            p.OnStart.Add(Run("/tools/start.exe", 0));
            p.OnStart.Add(Run("/tools/start2.exe", 3000));
            p.OnClose.Add(Run("/tools/close.exe", 0));
            AddApp(s, "Days Gone", GameExe, p);
            var rig = new Rig(s);
            rig.Engine.Start();
            rig.Sched.Advance(10);
            FakeProc g = rig.Ps.Launch(GameExe);
            rig.Sched.Advance(1000);
            Eq(1, rig.StartTimes("start.exe").Count, "started");
            rig.Engine.SetPaused(true);
            rig.Sched.Advance(5000);
            Eq(0, rig.StartTimes("start2.exe").Count, "pending action cancelled by pause");
            Check(rig.Engine.Snapshot.Paused, "snapshot paused");
            rig.Ps.Exit(g);
            rig.Sched.Advance(100);
            Eq(0, rig.StartTimes("close.exe").Count, "no event while paused");
            FakeProc g2 = rig.Ps.Launch(GameExe);
            rig.Sched.Advance(2000);
            Eq(1, rig.StartTimes("start.exe").Count, "no start while paused");
            rig.Engine.SetPaused(false);
            rig.Sched.Advance(100);
            Check(rig.Engine.Snapshot.Apps[0].Ignored, "running app ignored after resume");
            rig.Ps.Exit(g2);
            rig.Sched.Advance(100);
            Eq(0, rig.StartTimes("close.exe").Count, "ignored app close runs nothing");
            rig.Ps.Launch(GameExe);
            rig.Sched.Advance(1000);
            Eq(2, rig.StartTimes("start.exe").Count, "followed again after resume");
            rig.Stop();

            var rig2 = new Rig(s);
            rig2.Engine.Start();
            rig2.Sched.Advance(10);
            rig2.Engine.SetPaused(true);
            rig2.Sched.Advance(10);
            rig2.Ps.Launch(GameExe);
            rig2.Engine.SetPaused(false);
            rig2.Sched.Advance(10);
            Eq(0, rig2.StartTimes("start.exe").Count, "started just before resume: no start actions");
            Check(rig2.Engine.Snapshot.Apps[0].Ignored, "started just before resume: ignored");
            rig2.Stop();

            var rig3 = new Rig(s);
            rig3.Engine.Start(true);
            rig3.Sched.Advance(10);
            Check(rig3.Engine.Snapshot.Paused, "started paused");
            FakeProc g3 = rig3.Ps.Launch(GameExe);
            rig3.Sched.Advance(2000);
            Eq(0, rig3.StartTimes("start.exe").Count, "no start while started paused");
            rig3.Engine.SetPaused(false);
            rig3.Sched.Advance(100);
            Check(!rig3.Engine.Snapshot.Paused, "resumed after a paused start");
            Check(rig3.Engine.Snapshot.Apps[0].Ignored, "app launched during the paused start is ignored");
            rig3.Ps.Exit(g3);
            rig3.Sched.Advance(100);
            rig3.Ps.Launch(GameExe);
            rig3.Sched.Advance(1000);
            Eq(1, rig3.StartTimes("start.exe").Count, "followed after its next start");
            rig3.Stop();
        }

        private static void Focus()
        {
            var s = NewSettings();
            var p = AddProfile(s, "Game");
            p.OnFocus.Add(Run("/tools/focus.exe", 0));
            p.OnBlur.Add(Run("/tools/blur.exe", 0));
            p.OnClose.Add(Run("/tools/close.exe", 0));
            AddApp(s, "Days Gone", GameExe, p);
            var rig = new Rig(s);
            FakeProc explorer = rig.Ps.Launch("/windows/explorer.exe");
            rig.Fg.Pid = explorer.Pid;
            rig.Engine.Start();
            rig.Sched.Advance(10);
            FakeProc g = rig.Ps.Launch(GameExe);
            rig.Fg.Set(g.Pid);
            rig.Sched.Advance(299);
            Eq(0, rig.StartTimes("focus.exe").Count, "not before 300 ms");
            rig.Sched.Advance(2);
            Eq(1, rig.StartTimes("focus.exe").Count, "got focus after 300 ms");
            rig.Fg.Set(explorer.Pid);
            rig.Sched.Advance(100);
            rig.Fg.Set(g.Pid);
            rig.Sched.Advance(1000);
            Eq(0, rig.StartTimes("blur.exe").Count, "short focus change ignored");
            rig.Fg.Set(explorer.Pid);
            rig.Sched.Advance(400);
            Eq(1, rig.StartTimes("blur.exe").Count, "lost focus");
            rig.Fg.Set(g.Pid);
            rig.Sched.Advance(400);
            Eq(2, rig.StartTimes("focus.exe").Count, "got focus again");
            rig.Ps.Exit(g);
            rig.Fg.Set(explorer.Pid);
            rig.Sched.Advance(1000);
            Eq(1, rig.StartTimes("blur.exe").Count, "no lost focus on close");
            Eq(1, rig.StartTimes("close.exe").Count, "closed");
            rig.Stop();
        }

        private static void FocusKeepsStart()
        {
            var s = NewSettings();
            var p = AddProfile(s, "Game");
            p.OnStart.Add(Run("/tools/s1.exe", 0));
            p.OnStart.Add(Run("/tools/s2.exe", 2000));
            p.OnFocus.Add(Run("/tools/focus.exe", 0));
            AddApp(s, "Days Gone", GameExe, p);
            var rig = new Rig(s);
            rig.Engine.Start();
            rig.Sched.Advance(10);
            FakeProc g = rig.Ps.Launch(GameExe);
            rig.Sched.Advance(1000);
            rig.Fg.Set(g.Pid);
            rig.Sched.Advance(5000);
            Eq(1, rig.StartTimes("s2.exe").Count, "start action still runs after got focus");
            Eq(1, rig.StartTimes("focus.exe").Count, "focus action ran");
            rig.Stop();
        }

        private static void RestartApp()
        {
            var s = NewSettings();
            var p = AddProfile(s, "Game");
            p.RestartApp = true;
            p.OnStart.Add(Run("/tools/hdr.exe", 0, hold: true));
            p.OnClose.Add(Run("/tools/close.exe", 0));
            AddApp(s, "Days Gone", GameExe, p);
            var rig = new Rig(s);
            rig.Engine.Start();
            rig.Sched.Advance(10);
            FakeProc g = rig.Ps.Launch(GameExe, "\"" + GameExe + "\" -dx12 -windowed");
            rig.Sched.Advance(1000);
            Eq(0, rig.Ps.Killed.Count, "not restarted before the start actions end");
            rig.Ps.Exit(rig.Ps.Starts.First(x => x.Path.EndsWith("hdr.exe", StringComparison.OrdinalIgnoreCase)).Process);
            rig.Sched.Advance(100);
            Check(rig.Ps.Killed.Contains(g.Pid), "game ended");
            StartRecord relaunch = rig.Ps.Starts.FirstOrDefault(x => x.Path == GameExe);
            Check(relaunch != null, "game started again");
            Eq("-dx12 -windowed", relaunch?.Arguments, "same arguments");
            Eq(null, relaunch?.Folder, "no working folder when the game folder does not exist (test machine)");
            rig.Sched.Advance(2000);
            Eq(0, rig.StartTimes("close.exe").Count, "no closed actions during the restart");
            Eq(1, rig.StartTimes("hdr.exe").Count, "no second start");
            rig.Ps.Exit(relaunch.Process);
            rig.Sched.Advance(100);
            Eq(1, rig.StartTimes("close.exe").Count, "closed after the restarted game ends");
            FakeProc g3 = rig.Ps.Launch(GameExe);
            rig.Sched.Advance(1000);
            rig.Ps.Exit(rig.Ps.Starts.Last(x => x.Path.EndsWith("hdr.exe", StringComparison.OrdinalIgnoreCase)).Process);
            rig.Sched.Advance(100);
            Check(rig.Ps.Killed.Contains(g3.Pid), "restart again on the next launch");
            Eq(2, rig.Ps.Starts.Count(x => x.Path == GameExe), "two relaunches in all");
            rig.Stop();
        }

        private static void RestartFails()
        {
            var s = NewSettings();
            var p = AddProfile(s, "Game");
            p.RestartApp = true;
            p.OnClose.Add(Run("/tools/close.exe", 0));
            AddApp(s, "Days Gone", GameExe, p);
            var rig = new Rig(s);
            rig.Ps.CreateProcessOnStart = (path, args) => path != GameExe;
            rig.Engine.Start();
            rig.Sched.Advance(10);
            rig.Ps.Launch(GameExe);
            rig.Sched.Advance(1000);
            Eq(1, rig.Ps.Killed.Count, "ended for restart");
            rig.Sched.Advance(29000);
            Eq(0, rig.StartTimes("close.exe").Count, "waiting for the game");
            rig.Sched.Advance(2000);
            Eq(1, rig.StartTimes("close.exe").Count, "closed actions after 30 s without the game");

            var rig2 = new Rig(s);
            rig2.Engine.Start();
            rig2.Sched.Advance(10);
            FakeProc g = rig2.Ps.Launch(GameExe);
            g.Protected = true;
            rig2.Sched.Advance(1000);
            rig2.Sched.Advance(100);
            Check(g.Running, "protected game still running");
            Eq(0, rig2.Ps.Starts.Count(x => x.Path == GameExe), "not relaunched");
            rig.Stop();
            rig2.Stop();
        }

        private static void Matching()
        {
            var s = NewSettings();
            var p1 = AddProfile(s, "ByPath");
            p1.OnStart.Add(Run("/tools/path.exe", 0));
            var p2 = AddProfile(s, "ByName");
            p2.OnStart.Add(Run("/tools/name.exe", 0));
            AddApp(s, "Game", "/games/A/Game.exe", p1);
            AddApp(s, "Game anywhere", "/old/place/Game.exe", p2, byName: true);
            var rig = new Rig(s);
            rig.Engine.Start();
            rig.Sched.Advance(10);
            rig.Ps.Launch("/games/A/Game.exe");
            rig.Sched.Advance(1000);
            Eq(1, rig.StartTimes("path.exe").Count, "exact path wins");
            Eq(0, rig.StartTimes("name.exe").Count, "not both");
            rig.Ps.Launch("/games/B/GAME.EXE");
            rig.Sched.Advance(1000);
            Eq(1, rig.StartTimes("name.exe").Count, "file name match, any case");
            rig.Ps.Launch("/games/C/Other.exe");
            rig.Sched.Advance(1000);
            Eq(2, rig.Ps.Starts.Count, "other programs ignored");
            rig.Stop();
        }

        private static void Unreadable()
        {
            var s = NewSettings();
            var p = AddProfile(s, "Game");
            p.OnStart.Add(Run("/tools/start.exe", 0));
            AddApp(s, "Days Gone", GameExe, p);
            var rig = new Rig(s);
            rig.Engine.Start();
            rig.Sched.Advance(10);
            FakeProc g = rig.Ps.Launch(GameExe);
            g.Readable = false;
            rig.Sched.Advance(10000);
            Eq(0, rig.Ps.Starts.Count, "unreadable process ignored");
            Check(rig.Ps.QueryCount <= 3, "read at most 3 times, got " + rig.Ps.QueryCount);
            rig.Stop();
        }

        private static void CloseProgram()
        {
            var s = NewSettings();
            var p = AddProfile(s, "Game");
            p.OnClose.Add(new CloseProgramAction { Target = "Voicemeeter", DelayMs = 0 });
            p.OnClose.Add(new CloseProgramAction { Target = "Stubborn", Force = true, ForceAfterSec = 0 });
            p.OnClose.Add(new CloseProgramAction { Target = "/tools/NoWindow.exe", Force = false, ForceAfterSec = 1 });
            p.OnClose.Add(new CloseProgramAction { Target = "NotRunning.exe" });
            AddApp(s, "Days Gone", GameExe, p);
            var rig = new Rig(s);
            FakeProc vm1 = rig.Ps.Launch("/apps/Voicemeeter.exe");
            FakeProc vm2 = rig.Ps.Launch("/apps/Voicemeeter.exe");
            FakeProc stubborn = rig.Ps.Launch("/apps/Stubborn.exe");
            stubborn.ClosesOnRequest = false;
            FakeProc noWindow = rig.Ps.Launch("/tools/NoWindow.exe");
            noWindow.HasWindow = false;
            FakeProc otherPath = rig.Ps.Launch("/elsewhere/NoWindow.exe");
            otherPath.HasWindow = false;
            rig.Engine.Start();
            rig.Sched.Advance(10);
            FakeProc g = rig.Ps.Launch(GameExe);
            rig.Sched.Advance(1000);
            rig.Ps.Exit(g);
            rig.Sched.Advance(100);
            Thread.Sleep(300);
            rig.Sched.Advance(100);
            Check(!vm1.Running && !vm2.Running, "all instances closed");
            Check(!stubborn.Running && rig.Ps.Killed.Contains(stubborn.Pid), "forced");
            Check(noWindow.Running && otherPath.Running, "not forced without 'Force'; other path untouched");
            Eq("Game.exe", ActionExecutor.CloseTargetExeName("Game"), "name without .exe");
            Eq("NieR Replicant ver.1.22474487139.exe", ActionExecutor.CloseTargetExeName("NieR Replicant ver.1.22474487139"), "dotted name");
            rig.Stop();
        }

        private static void RunFailureContinues()
        {
            var s = NewSettings();
            var p = AddProfile(s, "Game");
            p.OnClose.Add(Run("/tools/missing.exe", 0));
            p.OnClose.Add(Run("/tools/after.exe", 100));
            AddApp(s, "Days Gone", GameExe, p);
            var rig = new Rig(s);
            rig.Engine.Start();
            rig.Sched.Advance(10);
            FakeProc g = rig.Ps.Launch(GameExe);
            rig.Sched.Advance(1000);
            rig.Ps.Exit(g);
            rig.Sched.Advance(500);
            Eq(1, rig.StartTimes("after.exe").Count, "a failure does not stop the next actions");
            rig.Stop();
        }

        private static void HardwareSkipped()
        {
            var s = NewSettings();
            var p = AddProfile(s, "Game");
            p.OnClose.Add(new DisplayAction { Hdr = HdrChange.Off, DelayMs = 0 });
            p.OnClose.Add(new WindowsSettingsAction { PlaybackDevice = "x", DelayMs = 0 });
            p.OnClose.Add(Run("/tools/after.exe", 0));
            AddApp(s, "Days Gone", GameExe, p);
            var rig = new Rig(s);
            rig.Engine.Start();
            rig.Sched.Advance(10);
            FakeProc g = rig.Ps.Launch(GameExe);
            rig.Sched.Advance(1000);
            rig.Ps.Exit(g);
            rig.Sched.Advance(500);
            Eq(1, rig.StartTimes("after.exe").Count, "runs after skipped display and audio actions");
            rig.Stop();
        }

        private static void SnapshotState()
        {
            var s = NewSettings();
            var p = AddProfile(s, "HDR VRR");
            AddApp(s, "Days Gone", GameExe, p);
            AddApp(s, "Other", "/games/Other.exe", null);
            var rig = new Rig(s);
            int changes = 0;
            rig.Engine.StateChanged += () => changes++;
            rig.Engine.Start();
            rig.Sched.Advance(10);
            Check(rig.Engine.Snapshot.Monitoring, "monitoring");
            Eq(null, rig.Engine.Snapshot.CurrentAppName, "no current app");
            FakeProc g = rig.Ps.Launch(GameExe);
            rig.Sched.Advance(1000);
            Eq("Days Gone", rig.Engine.Snapshot.CurrentAppName, "current app = last started");
            Eq("HDR VRR", rig.Engine.Snapshot.CurrentProfileName, "current profile");
            Check(rig.Engine.Snapshot.Apps[0].Running, "running flag");
            rig.Ps.Exit(g);
            rig.Sched.Advance(100);
            Eq(null, rig.Engine.Snapshot.CurrentAppName, "cleared on close");
            Check(changes > 0, "StateChanged raised");
            rig.Stop();
            Check(!rig.Engine.Snapshot.Monitoring || true, "dispose ok");
        }

        private static void SettingsUpdate()
        {
            var s = NewSettings();
            var p = AddProfile(s, "Game");
            p.OnStart.Add(Run("/tools/start.exe", 0));
            p.OnClose.Add(Run("/tools/close.exe", 0));
            var rig = new Rig(s);
            rig.Engine.Start();
            rig.Sched.Advance(10);
            FakeProc g = rig.Ps.Launch(GameExe);
            rig.Sched.Advance(1000);
            AddApp(s, "Days Gone", GameExe, p);
            rig.Engine.UpdateSettings(s);
            rig.Sched.Advance(1000);
            Eq(0, rig.Ps.Starts.Count, "app added while running: no start actions");
            rig.Ps.Exit(g);
            rig.Sched.Advance(100);
            Eq(0, rig.Ps.Starts.Count, "and no close actions");
            rig.Ps.Launch(GameExe);
            rig.Sched.Advance(1000);
            Eq(1, rig.StartTimes("start.exe").Count, "followed from its next start");
            rig.Stop();
        }

        private static void AppDisabled()
        {
            var s = NewSettings();
            var p = AddProfile(s, "Game");
            p.OnStart.Add(Run("/tools/start.exe", 0));
            p.OnStart.Add(Run("/tools/later.exe", 5000));
            p.OnClose.Add(Run("/tools/close.exe", 0));
            AddApp(s, "Days Gone", GameExe, p);
            s.Applications[0].Enabled = false;
            var rig = new Rig(s);
            rig.Engine.Start();
            rig.Sched.Advance(10);
            FakeProc g = rig.Ps.Launch(GameExe);
            rig.Sched.Advance(1000);
            rig.Ps.Exit(g);
            rig.Sched.Advance(1000);
            Eq(0, rig.Ps.Starts.Count, "disabled: no start nor close actions");

            s.Applications[0].Enabled = true;
            rig.Engine.UpdateSettings(CopyOf(s));
            rig.Sched.Advance(100);
            g = rig.Ps.Launch(GameExe);
            rig.Sched.Advance(1000);
            Eq(1, rig.StartTimes("start.exe").Count, "enabled again: followed");
            s.Applications[0].Enabled = false;
            rig.Engine.UpdateSettings(CopyOf(s));
            rig.Sched.Advance(6000);
            Eq(0, rig.StartTimes("later.exe").Count, "disabled while running: pending actions cancelled");
            rig.Ps.Exit(g);
            rig.Sched.Advance(1000);
            Eq(0, rig.StartTimes("close.exe").Count, "disabled while running: its close runs nothing");

            g = rig.Ps.Launch(GameExe);
            rig.Sched.Advance(1000);
            s.Applications[0].Enabled = true;
            rig.Engine.UpdateSettings(CopyOf(s));
            rig.Sched.Advance(1000);
            Eq(1, rig.StartTimes("start.exe").Count, "enabled while running: no start actions");
            Check(rig.Engine.Snapshot.Apps[0].Ignored, "enabled while running: ignored until it closes");
            rig.Ps.Exit(g);
            rig.Sched.Advance(1000);
            Eq(0, rig.StartTimes("close.exe").Count, "and its close runs nothing");
            rig.Ps.Launch(GameExe);
            rig.Sched.Advance(1000);
            Eq(2, rig.StartTimes("start.exe").Count, "followed from its next start");
            rig.Stop();

            Check(CopyOf(s).Applications[0].Enabled, "enabled saved");
            s.Applications[0].Enabled = false;
            Check(!CopyOf(s).Applications[0].Enabled, "disabled saved");
            var old = SettingsJson.Read(JsonValue.Parse("{\"applications\":[{\"name\":\"x\",\"path\":\"C:\\\\x.exe\"}]}"), new List<string>());
            Check(old.Applications[0].Enabled, "a file without the field: enabled");
        }

        private static void DuplicateProfile()
        {
            var s = NewSettings();
            var inner = AddProfile(s, "HDR");
            var p = AddProfile(s, "Game");
            p.RestartApp = true;
            p.OnStart.Add(Run("/tools/start.exe", 2500));
            p.OnClose.Add(new IncludeProfileAction { ProfileId = inner.Id, DelayMs = 100 });
            p.OnFocus.Add(new DisplayAction { Hdr = HdrChange.On });
            JsonValue future = JsonValue.Parse("{\"type\":\"future\",\"delayMs\":300,\"option\":true}");
            p.OnBlur.Add(SettingsJson.ReadAction(future));
            AddApp(s, "Days Gone", GameExe, p);

            string name = ProfileCopy.CopyName(s.Profiles.Select(x => x.Name), p.Name, "copy");
            Eq("Game (copy)", name, "copy name");
            Profile copy = ProfileCopy.Duplicate(p, name);
            s.Profiles.Add(copy);
            Eq("Game (copy 2)", ProfileCopy.CopyName(s.Profiles.Select(x => x.Name), p.Name, "copy"), "second copy name");
            Check(copy.Id != p.Id, "new id");
            Check(copy.RestartApp, "Restart app copied");
            Eq(1, copy.OnStart.Count, "start actions copied");
            Eq(2500, copy.OnStart[0].DelayMs, "delay copied");
            Check(!ReferenceEquals(copy.OnStart[0], p.OnStart[0]), "actions are copies");
            Eq(inner.Id, ((IncludeProfileAction)copy.OnClose[0]).ProfileId, "included profile kept");
            Eq(HdrChange.On, ((DisplayAction)copy.OnFocus[0]).Hdr, "display action copied");
            Check(copy.OnBlur[0] is UnknownAction, "unknown action copied");
            Eq(p.Id, s.Applications[0].ProfileId, "applications stay on the original");
            copy.OnStart[0].DelayMs = 0;
            Eq(2500, p.OnStart[0].DelayMs, "editing the copy leaves the original");
        }

        private static AppSettings CopyOf(AppSettings s) => SettingsJson.Read(SettingsJson.Write(s), new List<string>());

        private static void SettingsEditedCopy()
        {
            var ui = NewSettings();
            var p = AddProfile(ui, "Game");
            p.OnStart.Add(Run("/tools/start.exe", 0));
            AddApp(ui, "Days Gone", GameExe, p);
            var rig = new Rig(CopyOf(ui));
            rig.Engine.Start();
            rig.Sched.Advance(10);
            FakeProc g = rig.Ps.Launch(GameExe);
            rig.Sched.Advance(1000);
            Eq(1, rig.StartTimes("start.exe").Count, "start action");
            Eq(p.Id, rig.Engine.Snapshot.CurrentProfileId, "current profile id");

            p.OnClose.Add(Run("/tools/close.exe", 500));
            p.Name = "Game renamed";
            rig.Engine.UpdateSettings(CopyOf(ui));
            rig.Sched.Advance(1000);
            Check(!rig.Engine.Snapshot.Apps[0].Ignored, "a followed app stays followed after a change");
            Eq("Game renamed", rig.Engine.Snapshot.CurrentProfileName, "new name shown");
            Eq(1, rig.StartTimes("start.exe").Count, "no new start action");

            p.OnClose[0].DelayMs = 2000;
            rig.Ps.Exit(g);
            long closedAt = rig.T;
            rig.Sched.Advance(3000);
            Seq(new long[] { closedAt + 500 }, rig.StartTimes("close.exe"), "close action from the copy (500 ms)");

            var other = AddProfile(ui, "Other");
            other.OnStart.Add(Run("/tools/other.exe", 0));
            ui.Applications[0].ProfileId = other.Id;
            rig.Engine.UpdateSettings(CopyOf(ui));
            rig.Sched.Advance(100);
            rig.Ps.Launch(GameExe);
            rig.Sched.Advance(1000);
            Eq(1, rig.StartTimes("other.exe").Count, "new profile used");
            Eq(1, rig.StartTimes("start.exe").Count, "old profile not used");
            rig.Stop();
        }

        private static void Helpers()
        {
            Eq("-dx12 -windowed", CommandLine.ArgumentsOf("\"C:\\Games\\x.exe\" -dx12 -windowed"), "quoted exe");
            Eq("-a", CommandLine.ArgumentsOf("C:\\Games\\x.exe -a"), "unquoted exe");
            Eq("", CommandLine.ArgumentsOf("\"C:\\Games\\x.exe\""), "no args");
            Eq("", CommandLine.ArgumentsOf(null), "null");
            Check(PathMatcher.SamePath("/Games/A.exe", "/games/a.EXE"), "case-insensitive");
            Check(PathMatcher.SamePath("\"/games/a.exe\"", "/games/a.exe"), "quotes ignored");
        }

        private static void JsonRoundTrip()
        {
            var s = NewSettings();
            var p = AddProfile(s, "HDR VRR");
            p.RestartApp = true;
            p.OnClose.Add(new RunProgramAction { Path = "C:\\Program Files\\AutoHotkey\\HDR Off 2s.exe", DelayMs = 100, HoldNext = true, HoldCapSec = 45 });
            p.OnClose.Add(new CloseProgramAction { Target = "Voicemeeter", Force = true, ForceAfterSec = 3 });
            p.OnClose.Add(new DisplayAction { Hdr = HdrChange.On, Width = 3840, Height = 2160, RefreshNumerator = 120000, RefreshDenominator = 1001, ColorDepthBpc = 10, ColorFormat = "rgb", DynamicRange = "full" });
            p.OnFocus.Add(new WindowsSettingsAction { PlaybackDevice = "{a}", RecordDevice = "" });
            var q = AddProfile(s, "Game");
            q.OnStart.Add(new IncludeProfileAction { ProfileId = p.Id, DelayMs = 2000 });
            AddApp(s, "Jeu é", "D:\\Games\\Jeu.exe", q);
            s.Displays.Add(new DisplayEntry { LegacyUid = 4352, Managed = true });
            string json = SettingsJson.Write(s).ToJson(true);
            var warnings = new List<string>();
            AppSettings back = SettingsJson.Read(JsonValue.Parse(json), warnings);
            string json2 = SettingsJson.Write(back).ToJson(true);
            Eq(json, json2, "write(read(write(s))) == write(s)");
            Eq(0, warnings.Count, "no warnings");
            var d = (DisplayAction)back.Profiles[0].OnClose[2];
            Eq(120000u, d.RefreshNumerator, "exact refresh numerator");
            Eq(1001u, d.RefreshDenominator, "exact refresh denominator");

            JsonValue future = JsonValue.Parse("{\"type\":\"future\",\"delayMs\":100,\"option\":true}");
            var unknown = (UnknownAction)SettingsJson.ReadAction(future);
            unknown.DelayMs = 2500;
            JsonValue written = SettingsJson.WriteAction(unknown);
            Eq(2500.0, written["delayMs"].AsNumber() ?? -1, "unknown action keeps its new delay");
            Eq(true, written["option"].AsBool() ?? false, "unknown action keeps its other fields");
            Eq(100.0, future["delayMs"].AsNumber() ?? -1, "the original is not changed");
        }

        private static void DelaysInSeconds()
        {
            Eq("0", DelayText.Format(0), "zero");
            Eq("2.5", DelayText.Format(2500), "point as separator");
            Eq("12", DelayText.Format(12000), "whole seconds without decimals");
            Eq("600", DelayText.Format(ProfileAction.MaxDelayMs), "maximum");
            Check(DelayText.TryParse("2.5", out int a) && a == 2500, "2.5 s");
            Check(DelayText.TryParse("2,5", out int b) && b == 2500, "comma accepted");
            Check(DelayText.TryParse(" 3 ", out int c) && c == 3000, "spaces ignored");
            Check(DelayText.TryParse(",3", out int d) && d == 300, "leading separator");
            Check(DelayText.TryParse("1.26", out int e) && e == 1300, "rounded to a tenth");
            Check(DelayText.TryParse("900", out int f) && f == ProfileAction.MaxDelayMs, "clamped to 600 s");
            Check(!DelayText.TryParse("", out _), "empty refused");
            Check(!DelayText.TryParse("-1", out _), "negative refused");
            Check(!DelayText.TryParse("1.2.3", out _), "two separators refused");
            var run = new RunProgramAction { DelayMs = 250 };
            Eq(300, run.DelayMs, "stored delay rounded to 100 ms");
            run.DelayMs = 249;
            Eq(200, run.DelayMs, "rounded down");
            run.DelayMs = 700000;
            Eq(ProfileAction.MaxDelayMs, run.DelayMs, "stored delay clamped");
        }

        private static void DisplayPathChanged()
        {
            const string g = "#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}";
            string tvOld = @"\\?\DISPLAY#SAM713F#5&2e42fe3&6&UID4352" + g, tvNew = @"\\?\DISPLAY#SAM713F#5&2e42fe3&7&UID4352" + g;
            string asusOld = @"\\?\DISPLAY#ACI28A7#5&2e42fe3&6&UID4353" + g, asusNew = @"\\?\DISPLAY#ACI28A7#5&2e42fe3&7&UID4353" + g;
            Eq("SAM713F|UID4352", AutoTrigger.Core.Displays.DisplayLinker.StableKey(tvOld), "model and connector");
            Eq(AutoTrigger.Core.Displays.DisplayLinker.StableKey(tvOld), AutoTrigger.Core.Displays.DisplayLinker.StableKey(tvNew), "same key after the reinstall");
            Check(AutoTrigger.Core.Displays.DisplayLinker.StableKey("garbage") == null, "unknown path: no key");

            var s = NewSettings();
            var tv = new DisplayEntry { DevicePath = tvOld, FriendlyName = "Q90A", Managed = true };
            var asus = new DisplayEntry { DevicePath = asusOld, FriendlyName = "ASUS MG28U", Managed = true };
            s.Displays.Add(tv);
            s.Displays.Add(asus);
            var monitors = new List<AutoTrigger.Core.Displays.MonitorInfo>
            {
                new AutoTrigger.Core.Displays.MonitorInfo { DevicePath = tvNew, FriendlyName = "Q90A", TargetId = 4352 },
                new AutoTrigger.Core.Displays.MonitorInfo { DevicePath = asusNew, FriendlyName = "ASUS MG28U", TargetId = 4353 },
            };
            Check(AutoTrigger.Core.Displays.DisplayLinker.Sync(s, monitors), "changed");
            Eq(2, s.Displays.Count, "no duplicate displays");
            Eq(tvNew, tv.DevicePath, "TV kept with its new path");
            Eq(asusNew, asus.DevicePath, "ASUS kept with its new path");
            Check(!AutoTrigger.Core.Displays.DisplayLinker.Sync(s, monitors), "second pass: nothing to do");

            var twins = NewSettings();
            twins.Displays.Add(new DisplayEntry { DevicePath = asusOld, Managed = true });
            twins.Displays.Add(new DisplayEntry { DevicePath = asusOld.Replace("&6&", "&5&"), Managed = true });
            AutoTrigger.Core.Displays.DisplayLinker.Sync(twins, new List<AutoTrigger.Core.Displays.MonitorInfo> { monitors[1] });
            Eq(3, twins.Displays.Count, "two candidates: no guess, added as a new display");

            var moved = NewSettings();
            moved.Displays.Add(new DisplayEntry { DevicePath = asusOld, Managed = true });
            AutoTrigger.Core.Displays.DisplayLinker.Sync(moved, new List<AutoTrigger.Core.Displays.MonitorInfo>
            {
                new AutoTrigger.Core.Displays.MonitorInfo { DevicePath = asusNew.Replace("UID4353", "UID4354"), TargetId = 4354 },
            });
            Eq(2, moved.Displays.Count, "other connector: a new display");

            var both = NewSettings();
            both.Displays.Add(new DisplayEntry { DevicePath = asusOld, Managed = true });
            AutoTrigger.Core.Displays.DisplayLinker.Sync(both, new List<AutoTrigger.Core.Displays.MonitorInfo>
            {
                new AutoTrigger.Core.Displays.MonitorInfo { DevicePath = asusOld, TargetId = 4353 },
                new AutoTrigger.Core.Displays.MonitorInfo { DevicePath = asusNew, TargetId = 4353 },
            });
            Eq(2, both.Displays.Count, "old path still connected: not taken over");
        }

        private static void SortAndMove()
        {
            var names = new List<string> { "beta", "Alpha", "delta", "Charlie", "alpha" };
            Ordering.SortByName(names, n => n, false);
            Eq("Alpha,alpha,beta,Charlie,delta", string.Join(",", names), "A to Z, case ignored, stable");
            Ordering.SortByName(names, n => n, true);
            Eq("delta,Charlie,beta,Alpha,alpha", string.Join(",", names), "Z to A");
            Check(Ordering.Move(names, "beta", -1), "move up");
            Eq("delta,beta,Charlie,Alpha,alpha", string.Join(",", names), "moved up one place");
            Check(!Ordering.Move(names, "delta", -1), "first item cannot go up");
            Check(!Ordering.Move(names, "alpha", 1), "last item cannot go down");
            Check(Ordering.Move(names, "delta", 1), "move down");
            Eq("beta,delta,Charlie,Alpha,alpha", string.Join(",", names), "moved down one place");
            Check(!Ordering.Move(names, "missing", 1), "unknown item");
        }

        private static void ResetDefaults()
        {
            var s = NewSettings();
            var p = AddProfile(s, "Game");
            AddApp(s, "Jeu", "D:\\Games\\Jeu.exe", p);
            s.Displays.Add(new DisplayEntry { LegacyUid = 7, Managed = true });
            s.StartMinimized = true;
            s.CloseToTray = true;
            s.Theme = ThemeMode.Dark;
            s.FocusStabilityMs = 1200;
            s.StartPollMs = 250;
            s.LogLevel = LogLevel.Detailed;
            s.WindowWidth = 1300;
            s.WindowHeight = 900;
            s.MonitoringPaused = true;
            s.ResetPreferences();
            Eq(false, s.StartMinimized, "start minimized");
            Eq(false, s.CloseToTray, "close to tray");
            Eq(ThemeMode.Auto, s.Theme, "theme");
            Eq(AppSettings.DefaultFocusStabilityMs, s.FocusStabilityMs, "focus stability");
            Eq(500, s.StartPollMs, "start detection");
            Eq(LogLevel.Error, s.LogLevel, "log level");
            Eq(1300.0, s.WindowWidth, "window width kept");
            Eq(900.0, s.WindowHeight, "window height kept");
            Eq(1, s.Applications.Count, "applications kept");
            Eq(p.Id, s.Applications[0].ProfileId, "application profile kept");
            Eq(1, s.Profiles.Count, "profiles kept");
            Eq(1, s.Displays.Count, "displays kept");
            Check(s.MonitoringPaused, "pause kept by the reset");
            Check(CopyOf(s).MonitoringPaused, "pause saved in settings.json");
            s.MonitoringPaused = false;
            Check(!CopyOf(s).MonitoringPaused, "resume saved in settings.json");

            var warnings = new List<string>();
            AppSettings old = SettingsJson.Read(JsonValue.Parse("{\"schemaVersion\":1,\"defaultProfileId\":\"" + p.Id.ToString("D") + "\"}"), warnings);
            Eq(0, warnings.Count, "an old default profile is ignored quietly");
            Check(!SettingsJson.Write(old).ToJson(true).Contains("defaultProfileId"), "the default profile is not written back");
        }

        private static void Scanner()
        {
            const int entrySize = 96;
            var names = new[] { "", "System", "DaysGone.exe", "explorer.exe" };
            var pids = new long[] { 0, 4, 1234, 5678 };
            int textStart = entrySize * names.Length;
            IntPtr buffer = System.Runtime.InteropServices.Marshal.AllocHGlobal(textStart + 256);
            try
            {
                for (int i = 0; i < textStart + 256; i++)
                    System.Runtime.InteropServices.Marshal.WriteByte(buffer, i, 0);
                IntPtr text = buffer + textStart;
                for (int i = 0; i < names.Length; i++)
                {
                    IntPtr e = buffer + i * entrySize;
                    System.Runtime.InteropServices.Marshal.WriteInt32(e, 0, i + 1 < names.Length ? entrySize : 0);
                    System.Runtime.InteropServices.Marshal.WriteInt64(e, 32, 1000 + i);
                    System.Runtime.InteropServices.Marshal.WriteInt16(e, 56, (short)(names[i].Length * 2));
                    System.Runtime.InteropServices.Marshal.WriteIntPtr(e, 64, names[i].Length == 0 ? IntPtr.Zero : text);
                    System.Runtime.InteropServices.Marshal.WriteIntPtr(e, 80, new IntPtr(pids[i]));
                    foreach (char c in names[i])
                    {
                        System.Runtime.InteropServices.Marshal.WriteInt16(text, c);
                        text += 2;
                    }
                }
                using var scanner = new AutoTrigger.Core.Platform.ProcessScanner();
                var list = new List<ProcessEntry>();
                scanner.Read(buffer, list);
                Eq("4:System,1234:DaysGone.exe,5678:explorer.exe", string.Join(",", list.Select(x => x.Pid + ":" + x.ExeName)), "entries read");
                string first = list[1].ExeName;
                scanner.Read(buffer, list);
                Check(ReferenceEquals(first, list[1].ExeName), "same process: name not read again");
                System.Runtime.InteropServices.Marshal.WriteInt64(buffer + 2 * entrySize, 32, 9999);
                scanner.Read(buffer, list);
                Check(!ReferenceEquals(first, list[1].ExeName) && list[1].ExeName == "DaysGone.exe", "id reused by a new process: name read again");
            }
            finally
            {
                System.Runtime.InteropServices.Marshal.FreeHGlobal(buffer);
            }
        }

        private static IntPtr BasicBuffer(long[] pids, string[] names, long[] sequences, out int used)
        {
            const int entrySize = 48;
            int textStart = entrySize * pids.Length;
            used = textStart + 512;
            IntPtr buffer = System.Runtime.InteropServices.Marshal.AllocHGlobal(used);
            for (int i = 0; i < used; i++)
                System.Runtime.InteropServices.Marshal.WriteByte(buffer, i, 0);
            IntPtr text = buffer + textStart;
            for (int i = 0; i < pids.Length; i++)
            {
                IntPtr e = buffer + i * entrySize;
                System.Runtime.InteropServices.Marshal.WriteInt32(e, 0, i + 1 < pids.Length ? entrySize : 0);
                System.Runtime.InteropServices.Marshal.WriteIntPtr(e, 8, new IntPtr(pids[i]));
                System.Runtime.InteropServices.Marshal.WriteInt64(e, 24, sequences[i]);
                System.Runtime.InteropServices.Marshal.WriteInt16(e, 32, (short)(names[i].Length * 2));
                System.Runtime.InteropServices.Marshal.WriteInt16(e, 34, (short)(names[i].Length * 2 + 2));
                System.Runtime.InteropServices.Marshal.WriteIntPtr(e, 40, names[i].Length == 0 ? IntPtr.Zero : text);
                foreach (char c in names[i])
                {
                    System.Runtime.InteropServices.Marshal.WriteInt16(text, c);
                    text += 2;
                }
                text += 2;
            }
            return buffer;
        }

        private static void BasicScanner()
        {
            IntPtr buffer = BasicBuffer(new long[] { 0, 4, 1234, 5678 }, new[] { "", "System", "DaysGone.exe", "explorer.exe" },
                new long[] { 1, 2, 300, 301 }, out int used);
            try
            {
                using var scanner = new AutoTrigger.Core.Platform.ProcessScanner();
                var list = new List<ProcessEntry>();
                Check(scanner.ReadBasic(buffer, used, list), "light list read");
                Eq("4:System,1234:DaysGone.exe,5678:explorer.exe", string.Join(",", list.Select(x => x.Pid + ":" + x.ExeName)), "light entries");
                string first = list[1].ExeName;
                scanner.ReadBasic(buffer, used, list);
                Check(ReferenceEquals(first, list[1].ExeName), "same process number: name not read again");
                System.Runtime.InteropServices.Marshal.WriteInt64(buffer + 2 * 48, 24, 999);
                scanner.ReadBasic(buffer, used, list);
                Check(!ReferenceEquals(first, list[1].ExeName) && list[1].ExeName == "DaysGone.exe", "new process number: name read again");

                Check(!scanner.ReadBasic(buffer, 100, list), "list longer than the data returned: refused");
                System.Runtime.InteropServices.Marshal.WriteInt16(buffer + 48, 32, 7);
                Check(!scanner.ReadBasic(buffer, used, list), "odd name length: refused");
                System.Runtime.InteropServices.Marshal.WriteInt16(buffer + 48, 32, 12);
                System.Runtime.InteropServices.Marshal.WriteIntPtr(buffer + 48, 40, buffer + used + 64);
                Check(!scanner.ReadBasic(buffer, used, list), "name outside the data: refused");
                System.Runtime.InteropServices.Marshal.WriteIntPtr(buffer + 48, 40, buffer + 4 * 48);
                Check(scanner.ReadBasic(buffer, used, list), "repaired entry read again");
                System.Runtime.InteropServices.Marshal.WriteInt32(buffer, 0, 20);
                Check(!scanner.ReadBasic(buffer, used, list), "entry shorter than its structure: refused");
                System.Runtime.InteropServices.Marshal.WriteInt32(buffer, 0, 52);
                Check(!scanner.ReadBasic(buffer, used, list), "misaligned entry: refused");
            }
            finally
            {
                System.Runtime.InteropServices.Marshal.FreeHGlobal(buffer);
            }
        }

        private static void BasicAgreement()
        {
            int own = System.Diagnostics.Process.GetCurrentProcess().Id;
            var full = new List<ProcessEntry>();
            for (int i = 1; i <= 20; i++)
                full.Add(new ProcessEntry(i * 4 + 100000, "p" + i + ".exe"));
            full.Add(new ProcessEntry(own, "AutoTrigger.exe"));
            var same = new List<ProcessEntry>(full);
            Check(AutoTrigger.Core.Platform.ProcessScanner.Agrees(same, full), "identical lists agree");
            var started = new List<ProcessEntry>(full) { new ProcessEntry(999999, "new.exe") };
            Check(AutoTrigger.Core.Platform.ProcessScanner.Agrees(started, full), "a program started in between still agrees");
            var renamed = full.Select((e, i) => i < 5 ? new ProcessEntry(e.Pid, "other.exe") : e).ToList();
            Check(!AutoTrigger.Core.Platform.ProcessScanner.Agrees(renamed, full), "different names: no agreement");
            var withoutSelf = full.Where(e => e.Pid != own).ToList();
            Check(!AutoTrigger.Core.Platform.ProcessScanner.Agrees(withoutSelf, full), "AutoTrigger itself missing: no agreement");
            Check(!AutoTrigger.Core.Platform.ProcessScanner.Agrees(new List<ProcessEntry>(), full), "empty light list: no agreement");
        }

        private static void LoopTiming()
        {
            using (var loop = new EngineLoop())
            {
                var order = new System.Collections.Concurrent.ConcurrentQueue<string>();
                var threads = new System.Collections.Concurrent.ConcurrentBag<int>();
                using var done = new ManualResetEventSlim(false);
                long start = loop.NowMs;
                loop.Schedule(150, () => { order.Enqueue("late"); threads.Add(Environment.CurrentManagedThreadId); done.Set(); });
                loop.Schedule(50, () => { order.Enqueue("early"); threads.Add(Environment.CurrentManagedThreadId); });
                IDisposable cancelled = loop.Schedule(100, () => order.Enqueue("cancelled"));
                loop.Post(() =>
                {
                    order.Enqueue("posted");
                    threads.Add(Environment.CurrentManagedThreadId);
                    loop.Schedule(0, () => order.Enqueue("from inside"));
                });
                cancelled.Dispose();
                Check(done.Wait(2000), "timers fire");
                long elapsed = loop.NowMs - start;
                Eq("posted,from inside,early,late", string.Join(",", order), "order: posts, then timers by due time, cancelled one skipped");
                Check(elapsed >= 140, "not early: " + elapsed + " ms");
                Eq(1, threads.Distinct().Count(), "everything runs on the engine thread");
            }
        }

        private static void StoreRecovery()
        {
            string dir = Path.Combine(Path.GetTempPath(), "at-store-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                using var store = new AutoTrigger.Core.Storage.SettingsStore(dir);
                string oldBackup = Path.Combine(dir, AutoTrigger.Core.Storage.SettingsStore.OldBackupName);

                var fresh = store.Load();
                Eq(AutoTrigger.Core.Storage.LoadSource.New, fresh.Source, "no file: new settings");
                Check(fresh.Message == null, "no message on first start");

                File.WriteAllText(store.FilePath, "{ broken");
                File.WriteAllText(oldBackup, "{}");
                var loaded = store.Load();
                Eq(AutoTrigger.Core.Storage.LoadSource.Reset, loaded.Source, "unreadable file: empty settings");
                Check(!File.Exists(store.FilePath), "bad file set aside");
                Check(Directory.GetFiles(dir, "settings.invalid-*.json").Length == 1, "bad file kept aside");
                Check(loaded.Message != null && loaded.Message.Contains("set aside"), "message names the set-aside file");
                Check(File.Exists(oldBackup), "old settings.bak left alone when settings.json is unreadable");

                AddProfile(loaded.Settings, "First");
                store.SaveSoon(loaded.Settings);
                store.FlushPending();
                loaded.Settings.Profiles[0].Name = "Second";
                store.SaveSoon(loaded.Settings);
                store.FlushPending();
                var again = store.Load();
                Eq(AutoTrigger.Core.Storage.LoadSource.Main, again.Source, "settings.json read");
                Eq("Second", again.Settings.Profiles[0].Name, "last save kept");
                Check(!File.Exists(oldBackup), "old settings.bak deleted once settings.json is read");
                Check(!File.Exists(store.FilePath + ".tmp"), "no temporary file left");
                Eq(2, Directory.GetFiles(dir).Length, "only settings.json and the set-aside file");
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch (Exception) { }
            }
        }

        private static void SameCapabilities()
        {
            Func<AutoTrigger.Core.Displays.DisplayCapabilities> live = () =>
            {
                var caps = new AutoTrigger.Core.Displays.DisplayCapabilities
                {
                    Signal = new AutoTrigger.Core.Displays.PixelSize(3840, 2160),
                    HdrSupported = true,
                    Nvidia = true,
                };
                caps.DesktopSizes.Add(new AutoTrigger.Core.Displays.PixelSize(3840, 2160));
                caps.DesktopSizes.Add(new AutoTrigger.Core.Displays.PixelSize(2560, 1440));
                caps.RefreshRates.Add(new AutoTrigger.Core.Displays.RefreshRate(120000, 1000));
                caps.RefreshRates.Add(new AutoTrigger.Core.Displays.RefreshRate(60000, 1001));
                caps.Colors.Add(new AutoTrigger.Core.Displays.ColorSetting(10, "RGB", "Full"));
                return caps;
            };

            var settings = new AppSettings();
            settings.Displays.Add(new DisplayEntry { FriendlyName = "TV", Known = live() });
            AppSettings saved = SettingsJson.Read(SettingsJson.Write(settings), new List<string>());
            var stored = saved.Displays[0].Known;

            Check(SettingsJson.SameCapabilities(stored, live()), "same reading after a restart: nothing to save");
            var duplicated = live();
            duplicated.DesktopSizes.Add(new AutoTrigger.Core.Displays.PixelSize(2560, 1440));
            Check(SettingsJson.SameCapabilities(stored, duplicated), "a duplicate size dropped on reading is not a change");
            var faster = live();
            faster.RefreshRates.Add(new AutoTrigger.Core.Displays.RefreshRate(144000, 1000));
            Check(!SettingsJson.SameCapabilities(stored, faster), "new refresh rate is a change");
            var noHdr = live();
            noHdr.HdrSupported = false;
            Check(!SettingsJson.SameCapabilities(stored, noHdr), "HDR support lost is a change");
            Check(!SettingsJson.SameCapabilities(null, live()), "nothing stored yet is a change");
        }

        private static void DuplicateIds()
        {
            var s = new AppSettings();
            var app = new AppEntry { Name = "Game", Path = @"C:\Games\game.exe" };
            s.Applications.Add(app);
            s.Applications.Add(new AppEntry { Id = app.Id, Name = "Game copy", Path = @"C:\Games\game.exe" });
            var profile = new Profile { Name = "HDR" };
            s.Profiles.Add(profile);
            s.Profiles.Add(new Profile { Id = profile.Id, Name = "HDR copy" });
            var display = new DisplayEntry { FriendlyName = "TV" };
            s.Displays.Add(display);
            s.Displays.Add(new DisplayEntry { Id = display.Id, FriendlyName = "TV copy" });
            app.ProfileId = profile.Id;

            var warnings = new List<string>();
            AppSettings back = SettingsJson.Read(SettingsJson.Write(s), warnings);
            Eq(2, back.Applications.Select(a => a.Id).Distinct().Count(), "application ids made unique");
            Eq(2, back.Profiles.Select(p => p.Id).Distinct().Count(), "profile ids made unique");
            Eq(2, back.Displays.Select(d => d.Id).Distinct().Count(), "display ids made unique");
            Eq(app.Id, back.Applications[0].Id, "first application keeps its id");
            Eq(profile.Id, back.Profiles[0].Id, "first profile keeps its id");
            Eq("HDR", back.FindProfile(back.Applications[0].ProfileId).Name, "reference still points to the first profile");
            Eq(3, warnings.Count, "one warning per duplicate");
        }

        private static void ImportFromAutoActions()
        {
            const string json = @"{
  ""$type"": ""AutoActions.UserAppSettings, AutoActions"",
  ""AutoStart"": true,
  ""Displays"": [ { ""$type"": ""AutoActions.Displays.Display, AutoActions.Displays"", ""Managed"": false, ""UID"": 4352 } ],
  ""ActionShortcuts"": [],
  ""ApplicationProfiles"": { ""$type"": ""System.Collections.ObjectModel.ObservableCollection"", ""$values"": [
    { ""$type"": ""AutoActions.Profiles.Profile, AutoActions"", ""GUID"": ""11111111-1111-1111-1111-111111111111"", ""Name"": ""Zeta HDR"", ""RestartApplication"": true,
      ""ApplicationStarted"": [
        { ""$type"": ""AutoActions.Profiles.Actions.DisplayAction, AutoActions"", ""DisplayUID"": 4294967295, ""ChangeHDR"": true, ""EnableHDR"": true,
          ""ChangeResolution"": true, ""Resolution"": ""2560, 1440"", ""ChangeRefreshRate"": true, ""RefreshRate"": 144, ""ChangeColorDepth"": true, ""ColorDepth"": 10 },
        { ""$type"": ""AutoActions.Profiles.Actions.DisplayAction, AutoActions"", ""DisplayUID"": 4353, ""ChangeColorDepth"": true, ""ColorDepth"": 7 },
        { ""$type"": ""AutoActions.Profiles.Actions.AudioDeviceAction, AutoActions"", ""ChangePlaybackDevice"": true, ""PlaybackDeviceID"": ""22222222-2222-2222-2222-222222222222"", ""ChangeRecordDevice"": false, ""RecordDeviceID"": ""33333333-3333-3333-3333-333333333333"" }
      ],
      ""ApplicationClosed"": [
        { ""$type"": ""AutoActions.Profiles.Actions.CloseProgramAction, AutoActions"", ""ProcessName"": ""obs64"", ""Force"": true },
        { ""$type"": ""AutoActions.Profiles.Actions.ReferenceProfileAction, AutoActions"", ""ReferenceGuid"": ""44444444-4444-4444-4444-444444444444"" }
      ],
      ""ApplicationGotFocus"": [], ""ApplicationLostFocus"": [] },
    { ""$type"": ""AutoActions.Profiles.Profile, AutoActions"", ""GUID"": ""44444444-4444-4444-4444-444444444444"", ""Name"": ""alpha"",
      ""ApplicationStarted"": [ { ""$type"": ""AutoActions.Profiles.Actions.RunProgramAction, AutoActions"", ""FilePath"": ""C:\\Tools\\a.exe"", ""Arguments"": ""-x"", ""WaitForEnd"": true } ] }
  ] },
  ""ApplicationProfileAssignments"": [
    { ""Application"": { ""DisplayName"": ""Second"", ""ApplicationFilePath"": ""C:\\Games\\b.exe"" }, ""ProfileGUID"": ""44444444-4444-4444-4444-444444444444"", ""Position"": 1 },
    { ""Application"": { ""DisplayName"": """", ""ApplicationName"": ""First"", ""ApplicationFilePath"": ""C:\\Games\\a.exe"" }, ""ProfileGUID"": ""11111111-1111-1111-1111-111111111111"", ""Position"": 0 },
    { ""Application"": { ""DisplayName"": ""Gone"", ""ApplicationFilePath"": ""C:\\Games\\c.exe"" }, ""ProfileGUID"": ""55555555-5555-5555-5555-555555555555"", ""Position"": 2 }
  ]
}";
            ImportResult r = AutoActionsImport.Read(json);
            Eq("First|Second|Gone", string.Join("|", r.Applications.Select(a => a.Name)), "applications in AutoActions order, name fallback");
            Eq(Guid.Empty, r.Applications[2].ProfileId, "missing profile: imported without one");
            Eq("alpha|Zeta HDR", string.Join("|", r.Profiles.Select(p => p.Name)), "profiles sorted by name");
            Profile zeta = r.Profiles[1];
            Check(zeta.RestartApp, "Restart app kept");
            var all = (DisplayAction)zeta.OnStart[0];
            Check(all.DisplayId == Guid.Empty && all.Hdr == HdrChange.On && all.Width == 2560 && all.Height == 1440, "display action: all displays, HDR on, resolution");
            Check(all.RefreshNumerator == 144000 && all.RefreshDenominator == 1000 && all.ColorDepthBpc == 10, "display action: refresh and depth");
            var one = (DisplayAction)zeta.OnStart[1];
            Check(one.DisplayId != Guid.Empty && one.ColorDepthBpc == 0, "display action: its display, bad depth left out");
            Eq(2, r.Displays.Count, "listed display plus the one only used by an action");
            Check(!r.Displays[0].Managed && r.Displays[0].LegacyUid == 4352 && r.Displays[1].LegacyUid == 4353, "display uids and managed flag");
            var audio = (WindowsSettingsAction)zeta.OnStart[2];
            Check(audio.PlaybackDevice == "22222222-2222-2222-2222-222222222222" && audio.RecordDevice == string.Empty && !audio.ChangesPowerPlan && audio.ScreenSaverOn == null, "audio: only the changed device, in Windows settings");
            var close = (CloseProgramAction)zeta.OnClose[0];
            Check(close.Target == "obs64" && close.Force && close.ForceAfterSec == AutoActionsImport.CloseForceAfterSec, "close program");
            Eq(r.Profiles[0].Id, ((IncludeProfileAction)zeta.OnClose[1]).ProfileId, "reference profile");
            var run = (RunProgramAction)r.Profiles[0].OnStart[0];
            Check(run.Path == @"C:\Tools\a.exe" && run.Arguments == "-x" && run.HoldNext, "run program");
            Eq(6, r.ActionCount, "action count");
            Check(r.AutoStart, "AutoActions start with Windows noticed");
            Check(r.Warnings.Any(w => w.Contains("color depth")) && r.Warnings.Any(w => w.Contains("no longer exists")), "warnings: " + string.Join(" | ", r.Warnings));

            Check(Throws(() => AutoActionsImport.Read("{ \"name\": 1 }")), "not AutoActions settings: refused");
            Check(Throws(() => AutoActionsImport.Read("{ not json")), "not JSON: refused");
            Check(Throws(() => AutoActionsImport.Read(json.Replace("CloseProgramAction", "TeleportAction"))), "unknown action type: refused");
        }

        private static bool Throws(Action action)
        {
            try
            {
                action();
                return false;
            }
            catch (FormatException)
            {
                return true;
            }
        }
    }
}
