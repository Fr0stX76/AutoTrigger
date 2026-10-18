using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AutoTrigger.Core.Logging;
using AutoTrigger.Core.Model;

namespace AutoTrigger.Core.Engine
{
    public sealed class TriggerEngine : IDisposable
    {
        private const int MaxQueryAttempts = 3;
        private const int RestartCloseTimeoutMs = 15000;
        private const int RestartStartTimeoutMs = 30000;

        private readonly IProcessSystem _processes;
        private readonly IEngineScheduler _scheduler;
        private readonly Func<ProfileAction, Task<ActionOutcome>> _execute;
        private readonly IForegroundSource _foreground;

        private readonly Dictionary<Guid, AppState> _apps = new Dictionary<Guid, AppState>();
        private readonly Dictionary<int, KnownProcess> _known = new Dictionary<int, KnownProcess>();
        private readonly List<ProcessEntry> _scan = new List<ProcessEntry>(512);
        private Dictionary<string, List<IndexEntry>> _index = new Dictionary<string, List<IndexEntry>>(StringComparer.OrdinalIgnoreCase);

        private volatile AppSettings _settings;
        private IDisposable _pollTimer;
        private IDisposable _focusTimer;
        private int _generation;
        private bool _running;
        private bool _paused;
        private bool _stopped;
        private bool _rebaseline;
        private int _snapshotErrors;
        private AppState _focusApp;
        private AppState _lastStarted;
        private volatile EngineSnapshot _snapshot = EngineSnapshot.Empty;

        public TriggerEngine(AppSettings settings, IProcessSystem processes, IEngineScheduler scheduler,
            Func<ProfileAction, Task<ActionOutcome>> execute, IForegroundSource foreground)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _processes = processes ?? throw new ArgumentNullException(nameof(processes));
            _scheduler = scheduler ?? throw new ArgumentNullException(nameof(scheduler));
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _foreground = foreground;
        }

        public event Action StateChanged;

        public EngineSnapshot Snapshot => _snapshot;

        public AppSettings Settings => _settings;

        public void Start(bool paused = false) => _scheduler.Post(() => StartCore(paused));

        public void SetPaused(bool paused) => _scheduler.Post(() => SetPausedCore(paused));

        public void UpdateSettings(AppSettings settings)
        {
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));
            _scheduler.Post(() => UpdateSettingsCore(settings));
        }

        public void Dispose()
        {
            if (_scheduler is EngineLoop loop && loop.IsEngineThread)
            {
                StopCore();
                return;
            }
            using (var done = new ManualResetEventSlim(false))
            {
                _scheduler.Post(() =>
                {
                    StopCore();
                    done.Set();
                });
                done.Wait(TimeSpan.FromSeconds(2));
            }
        }

        private void StartCore(bool paused)
        {
            if (_running || _stopped)
                return;
            _running = true;
            _paused = paused;
            RebuildIndex();
            _rebaseline = true;
            Poll();
            _focusApp = ResolveForeground(false);
            if (_foreground != null)
                _foreground.Changed += OnForegroundChanged;
            SchedulePoll();
            int disabled = _settings.Applications.Count(a => !a.Enabled);
            string apps = $"{_settings.Applications.Count} applications";
            if (disabled > 0)
                apps += $" ({disabled} disabled)";
            Log.Info($"Monitoring started: {apps}, {_settings.Profiles.Count} profiles, program starts checked every {_settings.StartPollMs} ms");
            if (_paused)
                Log.Info("Monitoring paused (kept from the last session)");
            Publish();
        }

        private void SetPausedCore(bool paused)
        {
            if (_stopped || _paused == paused)
                return;
            if (!paused && _running)
                Poll();
            _paused = paused;
            if (paused)
            {
                _focusTimer?.Dispose();
                _focusTimer = null;
                foreach (AppState app in _apps.Values)
                    CancelRuns(app, true, "monitoring paused");
                Log.Info("Monitoring paused");
            }
            else
            {
                foreach (AppState app in _apps.Values)
                {
                    if (app.Processes.Count > 0 && app.Restart == null && !app.Ignored)
                    {
                        app.Ignored = true;
                        Log.Info($"{app.Name} is running: ignored until it closes");
                    }
                }
                _focusApp = ResolveForeground(false);
                Log.Info("Monitoring resumed");
            }
            Publish();
        }

        private void UpdateSettingsCore(AppSettings settings)
        {
            if (_stopped)
                return;
            if (_running)
                Poll();
            _settings = settings;
            RebuildIndex();

            foreach (AppState app in _apps.Values.ToList())
            {
                AppEntry entry = settings.FindApplication(app.Entry.Id);
                if (entry == null)
                    DropApp(app);
                else
                    app.Entry = entry;
            }

            foreach (KnownProcess known in _known.Values.ToList())
            {
                if (known.Owner == null)
                {
                    _known.Remove(known.Pid);
                    continue;
                }
                AppEntry match = known.Path == null ? null : Match(known.ExeName, known.Path);
                if (match == null || match.Id != known.Owner.Entry.Id)
                {
                    _known.Remove(known.Pid);
                    DetachProcess(known.Owner, known.Pid);
                }
            }
            if (_running)
            {
                _rebaseline = true;
                Poll();
            }
            Log.Info("Settings updated");
            Publish();
        }

        private void StopCore()
        {
            if (_stopped)
                return;
            _stopped = true;
            _running = false;
            if (_foreground != null)
                _foreground.Changed -= OnForegroundChanged;
            _pollTimer?.Dispose();
            _pollTimer = null;
            _focusTimer?.Dispose();
            _focusTimer = null;
            foreach (AppState app in _apps.Values)
            {
                CancelRuns(app, true, "AutoTrigger is closing");
                EndRestart(app);
                foreach (TrackedProcess p in app.Processes.Values)
                    p.Watch?.Dispose();
                app.Processes.Clear();
            }
            _known.Clear();
            Log.Info("Monitoring stopped");
        }

        private void RebuildIndex()
        {
            var index = new Dictionary<string, List<IndexEntry>>(StringComparer.OrdinalIgnoreCase);
            foreach (AppEntry entry in _settings.Applications)
            {
                if (!entry.Enabled)
                    continue;
                string key = PathMatcher.FileNameKey(entry.Path);
                if (key.Length == 0)
                    continue;
                if (!index.TryGetValue(key, out List<IndexEntry> list))
                    index[key] = list = new List<IndexEntry>();
                list.Add(new IndexEntry { Entry = entry, NormalizedPath = PathMatcher.Normalize(entry.Path) });
            }
            _index = index;
        }

        private void SchedulePoll()
        {
            _pollTimer = _scheduler.Schedule(_settings.StartPollMs, () =>
            {
                _pollTimer = null;
                if (!_running)
                    return;
                Poll();
                SchedulePoll();
            });
        }

        private void Poll()
        {
            try
            {
                _processes.Snapshot(_scan);
                _snapshotErrors = 0;
            }
            catch (Exception ex)
            {
                if (_snapshotErrors++ == 0)
                    Log.Error("Cannot list the running programs", ex);
                return;
            }

            bool baseline = _rebaseline;
            _rebaseline = false;
            int generation = ++_generation;
            foreach (ProcessEntry entry in _scan)
            {
                _known.TryGetValue(entry.Pid, out KnownProcess known);
                if (known != null)
                    known.Seen = generation;
                if (string.IsNullOrEmpty(entry.ExeName))
                    continue;
                if (known != null)
                {
                    if (string.Equals(known.ExeName, entry.ExeName, StringComparison.OrdinalIgnoreCase))
                    {
                        if (!known.Resolved)
                            Resolve(known, baseline);
                        continue;
                    }
                    Forget(known);
                }
                if (!_index.ContainsKey(entry.ExeName))
                    continue;
                known = new KnownProcess { Pid = entry.Pid, ExeName = entry.ExeName, Seen = generation };
                _known[entry.Pid] = known;
                Resolve(known, baseline);
            }

            List<KnownProcess> gone = null;
            foreach (KnownProcess known in _known.Values)
            {
                if (known.Seen != generation)
                    (gone ?? (gone = new List<KnownProcess>())).Add(known);
            }
            if (gone != null)
            {
                foreach (KnownProcess known in gone)
                    Forget(known);
            }
        }

        private void Forget(KnownProcess known)
        {
            _known.Remove(known.Pid);
            if (known.Owner != null)
                ProcessExited(known.Owner, known.Pid, known.CreationTime);
        }

        private void Resolve(KnownProcess known, bool baseline)
        {
            ProcessDetails details = null;
            try
            {
                details = _processes.Query(known.Pid);
            }
            catch (Exception ex)
            {
                Log.Detailed($"{known.ExeName} (pid {known.Pid}): cannot be read ({ex.Message})");
            }
            if (details == null || string.IsNullOrEmpty(details.Path))
            {
                if (++known.Attempts >= MaxQueryAttempts)
                {
                    known.Resolved = true;
                    Log.Detailed($"{known.ExeName} (pid {known.Pid}): its path cannot be read; ignored");
                }
                return;
            }

            known.Resolved = true;
            known.Path = details.Path;
            known.CreationTime = details.CreationTime;
            AppEntry entry = Match(known.ExeName, details.Path);
            if (entry == null)
            {
                Log.Detailed($"{known.ExeName} (pid {known.Pid}) at {details.Path}: not a watched application");
                return;
            }
            AppState app = GetState(entry);
            known.Owner = app;
            AddProcess(app, known, baseline);
        }

        private AppEntry Match(string exeName, string path)
        {
            if (!_index.TryGetValue(exeName, out List<IndexEntry> candidates))
                return null;
            string normalized = PathMatcher.Normalize(path);
            foreach (IndexEntry c in candidates)
            {
                if (!c.Entry.MatchByFileName && string.Equals(c.NormalizedPath, normalized, StringComparison.OrdinalIgnoreCase))
                    return c.Entry;
            }
            foreach (IndexEntry c in candidates)
            {
                if (c.Entry.MatchByFileName)
                    return c.Entry;
            }
            return null;
        }

        private AppState GetState(AppEntry entry)
        {
            if (!_apps.TryGetValue(entry.Id, out AppState app))
                _apps[entry.Id] = app = new AppState { Entry = entry };
            return app;
        }

        private void AddProcess(AppState app, KnownProcess known, bool baseline)
        {
            if (app.Processes.TryGetValue(known.Pid, out TrackedProcess old))
                old.Watch?.Dispose();
            var tracked = new TrackedProcess { Pid = known.Pid, CreationTime = known.CreationTime };
            bool wasRunning = app.Processes.Count > 0;
            app.Processes[tracked.Pid] = tracked;
            tracked.Watch = WatchExit(app, tracked);

            if (wasRunning)
            {
                Log.Detailed($"{app.Name}: one more process, pid {tracked.Pid} ({app.Processes.Count} in all)");
                Publish();
                return;
            }

            if (app.Restart != null && app.Restart.Relaunching)
            {
                Log.Info($"{app.Name}: Restart app, running again, pid {tracked.Pid}");
                EndRestart(app);
                Publish();
                return;
            }

            if (baseline || _paused)
            {
                app.Ignored = true;
                Log.Info(baseline
                    ? $"{app.Name} was already running: ignored until it closes"
                    : $"{app.Name} started while monitoring is paused: ignored until it closes");
                Publish();
                return;
            }

            app.Ignored = false;
            app.RestartDone = false;
            app.LaunchPath = known.Path;
            app.LaunchPid = tracked.Pid;
            _lastStarted = app;
            Fire(app, EventKind.Started, $", pid {tracked.Pid}{SeenAfter(tracked.CreationTime)}");
        }

        private static string SeenAfter(long creationTime)
        {
            if (creationTime <= 0)
                return string.Empty;
            long ms = (DateTime.UtcNow.ToFileTimeUtc() - creationTime) / 10000;
            return ms >= 0 && ms < 60000 ? $", seen {ms} ms after its start" : string.Empty;
        }

        private IDisposable WatchExit(AppState app, TrackedProcess tracked)
        {
            try
            {
                return _processes.WatchExit(tracked.Pid, tracked.CreationTime,
                    () => _scheduler.Post(() => ProcessExited(app, tracked.Pid, tracked.CreationTime)));
            }
            catch (Exception ex)
            {
                Log.Detailed($"{app.Name}: pid {tracked.Pid} cannot be watched ({ex.Message}); its exit is seen by the regular check");
                return null;
            }
        }

        private void ProcessExited(AppState app, int pid, long creationTime)
        {
            if (!RemoveProcess(app, pid, creationTime))
                return;
            if (app.Processes.Count > 0)
            {
                Log.Detailed($"{app.Name}: pid {pid} ended ({app.Processes.Count} still running)");
                Publish();
                return;
            }

            if (_focusApp == app)
                _focusApp = null;

            if (app.Restart != null)
            {
                RestartClosed(app);
            }
            else if (app.Ignored)
            {
                app.Ignored = false;
                Log.Info($"{app.Name} closed (it was ignored); its next start will be followed");
            }
            else if (_paused || !_running)
            {
                Log.Info($"{app.Name} closed while monitoring is paused: no actions");
            }
            else
            {
                Fire(app, EventKind.Closed, string.Empty);
                return;
            }
            Publish();
        }

        private bool RemoveProcess(AppState app, int pid, long creationTime)
        {
            if (!app.Processes.TryGetValue(pid, out TrackedProcess tracked) || tracked.CreationTime != creationTime)
                return false;
            app.Processes.Remove(pid);
            tracked.Watch?.Dispose();
            if (_known.TryGetValue(pid, out KnownProcess known) && known.Owner == app && known.CreationTime == creationTime)
                _known.Remove(pid);
            return true;
        }

        private void DetachProcess(AppState app, int pid)
        {
            if (!app.Processes.TryGetValue(pid, out TrackedProcess tracked))
                return;
            app.Processes.Remove(pid);
            tracked.Watch?.Dispose();
            if (app.Processes.Count == 0)
            {
                CancelRuns(app, true, "application settings changed");
                EndRestart(app);
                app.Ignored = false;
                if (_focusApp == app)
                    _focusApp = null;
            }
        }

        private void DropApp(AppState app)
        {
            CancelRuns(app, true, "application removed");
            EndRestart(app);
            foreach (TrackedProcess p in app.Processes.Values)
                p.Watch?.Dispose();
            app.Processes.Clear();
            foreach (KnownProcess known in _known.Values.Where(k => k.Owner == app).ToList())
                _known.Remove(known.Pid);
            if (_focusApp == app)
                _focusApp = null;
            if (_lastStarted == app)
                _lastStarted = null;
            _apps.Remove(app.Entry.Id);
        }

        private void OnForegroundChanged()
        {
            _scheduler.Post(() =>
            {
                if (!_running || _paused)
                    return;
                if (_focusTimer != null)
                {
                    _focusTimer.Dispose();
                    Log.Detailed($"Focus changed again within {_settings.FocusStabilityMs} ms: previous change ignored");
                }
                _focusTimer = _scheduler.Schedule(_settings.FocusStabilityMs, CheckFocus);
            });
        }

        private void CheckFocus()
        {
            _focusTimer = null;
            if (!_running || _paused)
                return;
            AppState app = ResolveForeground(true);
            if (app == _focusApp)
                return;
            AppState previous = _focusApp;
            _focusApp = app;
            if (previous != null && IsFollowed(previous))
                Fire(previous, EventKind.LostFocus, string.Empty);
            if (app != null && IsFollowed(app))
                Fire(app, EventKind.GotFocus, string.Empty);
            Log.Detailed($"Focus: {previous?.Name ?? "other program"} -> {app?.Name ?? "other program"}");
            Publish();
        }

        private AppState ResolveForeground(bool pollIfUnknown)
        {
            if (_foreground == null)
                return null;
            int pid;
            try
            {
                pid = _foreground.GetForegroundProcessId();
            }
            catch (Exception ex)
            {
                Log.Detailed("Cannot read the foreground window: " + ex.Message);
                return null;
            }
            if (pid == 0)
                return null;
            if (pollIfUnknown && !_known.ContainsKey(pid))
                Poll();
            return _known.TryGetValue(pid, out KnownProcess known) && known.Owner != null && known.Owner.Processes.ContainsKey(pid)
                ? known.Owner
                : null;
        }

        private static bool IsFollowed(AppState app) => !app.Ignored && app.Restart == null && app.Processes.Count > 0;

        private void Fire(AppState app, EventKind kind, string detail)
        {
            string reason = $"{app.Name} {EventRun.EventText(kind)}";
            bool lifecycle = kind == EventKind.Started || kind == EventKind.Closed;
            if (lifecycle)
            {
                Cancel(ref app.LifeRun, reason);
                if (kind == EventKind.Closed)
                    Cancel(ref app.FocusRun, reason);
            }
            else
            {
                Cancel(ref app.FocusRun, reason);
            }

            Profile profile = _settings.FindProfile(app.Entry.ProfileId);
            if (profile == null)
            {
                Log.Info($"{app.Name} {EventRun.EventText(kind)}{detail}: no profile assigned");
                Publish();
                return;
            }

            List<PlannedAction> plan = PlanBuilder.Build(_settings, profile, kind);
            Log.Info($"{app.Name} {EventRun.EventText(kind)}{detail}: profile '{profile.Name}', {plan.Count} action(s)");

            if (kind == EventKind.Started && profile.RestartApp)
                ReadLaunchArguments(app);

            var run = new EventRun(_scheduler, _execute, app.Name, kind, profile, plan, r => RunFinished(app, r));
            if (lifecycle)
                app.LifeRun = run;
            else
                app.FocusRun = run;
            run.Start();
            Publish();
        }

        private static void Cancel(ref EventRun run, string reason)
        {
            run?.Cancel(reason);
            run = null;
        }

        private static void CancelRuns(AppState app, bool all, string reason)
        {
            Cancel(ref app.LifeRun, reason);
            if (all)
                Cancel(ref app.FocusRun, reason);
        }

        private void RunFinished(AppState app, EventRun run)
        {
            if (app.LifeRun == run)
                app.LifeRun = null;
            if (app.FocusRun == run)
                app.FocusRun = null;
            if (run.Kind == EventKind.Started && run.Profile.RestartApp && !app.RestartDone
                && IsFollowed(app) && !_paused && _running)
                BeginRestart(app);
        }

        private void ReadLaunchArguments(AppState app)
        {
            app.LaunchArgs = null;
            try
            {
                string commandLine = _processes.GetCommandLine(app.LaunchPid);
                if (commandLine != null)
                    app.LaunchArgs = CommandLine.ArgumentsOf(commandLine);
            }
            catch (Exception ex)
            {
                Log.Detailed($"{app.Name}: command line cannot be read ({ex.Message})");
            }
        }

        private void BeginRestart(AppState app)
        {
            app.RestartDone = true;
            if (string.IsNullOrEmpty(app.LaunchPath))
            {
                Log.Error($"{app.Name}: Restart app: the program path is unknown; not restarted");
                return;
            }
            if (app.LaunchArgs == null)
                Log.Warning($"{app.Name}: Restart app: its command line cannot be read; it will start again without arguments");

            var restart = new RestartState { Path = app.LaunchPath, Arguments = app.LaunchArgs ?? string.Empty };
            app.Restart = restart;
            Log.Info($"{app.Name}: Restart app, ending it");

            int[] pids = app.Processes.Keys.ToArray();
            Task.Run(() =>
            {
                var errors = new List<string>();
                foreach (int pid in pids)
                {
                    try
                    {
                        _processes.Kill(pid);
                    }
                    catch (Exception ex)
                    {
                        errors.Add($"pid {pid}: {ex.Message}");
                    }
                }
                return errors;
            }).ContinueWith(t => _scheduler.Post(() =>
            {
                List<string> errors = t.Status == TaskStatus.RanToCompletion ? t.Result : new List<string> { "unexpected error" };
                if (errors.Count > 0 && app.Restart == restart && !restart.Relaunching)
                {
                    Log.Error($"{app.Name}: Restart app: cannot end it ({string.Join("; ", errors)}). "
                        + "A program run as administrator cannot be ended by AutoTrigger.");
                    EndRestart(app);
                    Publish();
                }
            }), TaskScheduler.Default);

            restart.Timeout = _scheduler.Schedule(RestartCloseTimeoutMs, () =>
            {
                if (app.Restart == restart && !restart.Relaunching)
                {
                    Log.Error($"{app.Name}: Restart app: still running after {RestartCloseTimeoutMs / 1000} s; restart abandoned");
                    EndRestart(app);
                    Publish();
                }
            });
            Publish();
        }

        private void RestartClosed(AppState app)
        {
            RestartState restart = app.Restart;
            if (restart.Relaunching)
                return;
            restart.Timeout?.Dispose();
            restart.Relaunching = true;
            Log.Info($"{app.Name}: Restart app, starting it again");

            string folder = ActionExecutor.ProgramFolder(restart.Path);
            Task.Run(() => _processes.Start(restart.Path, restart.Arguments, folder, false)).ContinueWith(t => _scheduler.Post(() =>
            {
                if (t.Status != TaskStatus.RanToCompletion && app.Restart == restart)
                {
                    Log.Error($"{app.Name}: Restart app: cannot start it again ({t.Exception?.GetBaseException().Message})");
                    RestartFailed(app);
                }
            }), TaskScheduler.Default);

            restart.Timeout = _scheduler.Schedule(RestartStartTimeoutMs, () =>
            {
                if (app.Restart == restart)
                {
                    Log.Error($"{app.Name}: Restart app: not running again after {RestartStartTimeoutMs / 1000} s");
                    RestartFailed(app);
                }
            });
        }

        private void RestartFailed(AppState app)
        {
            EndRestart(app);
            if (app.Processes.Count == 0 && _running && !_paused)
                Fire(app, EventKind.Closed, " (restart failed)");
            else
                Publish();
        }

        private static void EndRestart(AppState app)
        {
            if (app.Restart == null)
                return;
            app.Restart.Timeout?.Dispose();
            app.Restart = null;
        }

        private void Publish()
        {
            var apps = new List<AppStatus>(_settings.Applications.Count);
            foreach (AppEntry entry in _settings.Applications)
            {
                _apps.TryGetValue(entry.Id, out AppState state);
                apps.Add(new AppStatus(entry.Id, entry.Name,
                    state != null && state.Processes.Count > 0,
                    state != null && state.Ignored,
                    state != null && state == _focusApp));
            }

            AppState current = null;
            if (_focusApp != null && IsShown(_focusApp))
                current = _focusApp;
            else if (_lastStarted != null && IsShown(_lastStarted))
                current = _lastStarted;
            Profile profile = current == null ? null : _settings.FindProfile(current.Entry.ProfileId);

            _snapshot = new EngineSnapshot(_running, _paused, apps,
                current?.Entry.Id ?? Guid.Empty, current?.Name,
                profile?.Id ?? Guid.Empty, profile?.Name);
            try
            {
                StateChanged?.Invoke();
            }
            catch (Exception ex)
            {
                Log.Error("Status update failed", ex);
            }
        }

        private static bool IsShown(AppState app) => !app.Ignored && (app.Processes.Count > 0 || app.Restart != null);

        private sealed class AppState
        {
            public AppEntry Entry;
            public readonly Dictionary<int, TrackedProcess> Processes = new Dictionary<int, TrackedProcess>();
            public bool Ignored;
            public EventRun LifeRun;
            public EventRun FocusRun;
            public bool RestartDone;
            public RestartState Restart;
            public string LaunchPath;
            public int LaunchPid;
            public string LaunchArgs;

            public string Name => string.IsNullOrWhiteSpace(Entry.Name) ? Entry.FileName : Entry.Name;
        }

        private sealed class TrackedProcess
        {
            public int Pid;
            public long CreationTime;
            public IDisposable Watch;
        }

        private sealed class KnownProcess
        {
            public int Pid;
            public string ExeName;
            public bool Resolved;
            public int Attempts;
            public string Path;
            public long CreationTime;
            public AppState Owner;
            public int Seen;
        }

        private sealed class IndexEntry
        {
            public AppEntry Entry;
            public string NormalizedPath;
        }

        private sealed class RestartState
        {
            public string Path;
            public string Arguments;
            public bool Relaunching;
            public IDisposable Timeout;
        }
    }
}
