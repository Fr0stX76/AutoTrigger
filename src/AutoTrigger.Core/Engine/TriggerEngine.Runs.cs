using System;
using System.Collections.Generic;
using System.Linq;
using AutoTrigger.Core.Logging;
using AutoTrigger.Core.Model;

namespace AutoTrigger.Core.Engine
{
    public sealed partial class TriggerEngine
    {
        public const int FastTrackLimitMs = 10000;

        private readonly Dictionary<Guid, EventRun> _manualRuns = new Dictionary<Guid, EventRun>();
        private readonly List<WaitingRun> _waiting = new List<WaitingRun>();

        public void RunProfile(Guid profileId, string trigger, Action<bool> done = null) =>
            _scheduler.Post(() =>
            {
                Profile profile = _settings.FindProfile(profileId);
                if (profile == null && !_stopped)
                    Log.Detailed($"{trigger}: its profile is not known yet; press it again");
                Report(done, RunProfileCore(profile, trigger));
            });

        public void RunProfileNamed(string name, string trigger, Action<bool> done = null) =>
            _scheduler.Post(() => Report(done, RunProfileCore(FindProfileNamed(name), trigger)));

        private static void Report(Action<bool> done, bool result)
        {
            try
            {
                done?.Invoke(result);
            }
            catch (Exception ex)
            {
                Log.Error("Profile run result could not be reported", ex);
            }
        }

        private Profile FindProfileNamed(string name)
        {
            string wanted = (name ?? string.Empty).Trim();
            List<Profile> matches = _settings.Profiles
                .Where(p => string.Equals((p.Name ?? string.Empty).Trim(), wanted, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (matches.Count == 0)
                Log.Warning($"No profile named '{wanted}'");
            else if (matches.Count > 1)
                Log.Warning($"{matches.Count} profiles are named '{wanted}'; the first one in the list is used");
            return matches.FirstOrDefault();
        }

        private bool RunProfileCore(Profile profile, string trigger)
        {
            if (_stopped || profile == null)
                return false;
            if (_manualRuns.TryGetValue(profile.Id, out EventRun previous))
            {
                _manualRuns.Remove(profile.Id);
                previous.Cancel(trigger);
                CheckWaiting();
            }

            string name = string.IsNullOrWhiteSpace(profile.Name) ? "unnamed" : profile.Name;
            List<PlannedAction> plan = PlanBuilder.Build(_settings, profile, EventKind.Started);
            Log.Info($"{trigger}: profile '{name}', {plan.Count} action(s)");
            if (profile.RestartApp)
                Log.Detailed($"Profile '{name}': Restart app is ignored ({trigger})");

            var run = new EventRun(_scheduler, _execute, $"{name} / {trigger}", trigger, EventKind.Started, profile, plan,
                r => ManualRunFinished(profile.Id, r));
            _manualRuns[profile.Id] = run;
            Begin(run, ManualSource(profile.Id));
            return true;
        }

        private void ManualRunFinished(Guid profileId, EventRun run)
        {
            if (_manualRuns.TryGetValue(profileId, out EventRun current) && current == run)
                _manualRuns.Remove(profileId);
            CheckWaiting();
        }

        private static string AppSource(AppState app) => "app:" + app.Entry.Id.ToString("N");

        private static string ManualSource(Guid profileId) => "profile:" + profileId.ToString("N");

        private List<(EventRun Run, string Source)> ActiveRuns()
        {
            var runs = new List<(EventRun Run, string Source)>();
            foreach (AppState app in _apps.Values)
            {
                string source = AppSource(app);
                if (app.LifeRun != null && !app.LifeRun.IsFinished)
                    runs.Add((app.LifeRun, source));
                if (app.FocusRun != null && !app.FocusRun.IsFinished)
                    runs.Add((app.FocusRun, source));
            }
            foreach (KeyValuePair<Guid, EventRun> manual in _manualRuns)
            {
                if (!manual.Value.IsFinished)
                    runs.Add((manual.Value, ManualSource(manual.Key)));
            }
            return runs;
        }

        private void Begin(EventRun run, string source)
        {
            var blockers = new List<EventRun>();
            if (run.HasActions)
            {
                foreach ((EventRun other, string otherSource) in ActiveRuns())
                {
                    if (other == run || otherSource == source || other.IsFinished)
                        continue;
                    other.FastTrack(run.Trigger);
                    if (!other.IsFinished)
                        blockers.Add(other);
                }
            }
            if (blockers.Count == 0)
            {
                run.Start();
                return;
            }

            var waiting = new WaitingRun { Run = run, Blockers = blockers };
            _waiting.Add(waiting);
            waiting.Limit = _scheduler.Schedule(FastTrackLimitMs, () => FastTrackLimitReached(waiting));
            Log.Detailed($"{run.Trigger}: starts once {blockers.Count} fast-tracked profile run(s) finish");
        }

        private void CheckWaiting()
        {
            foreach (WaitingRun waiting in _waiting.ToList())
            {
                if (!_waiting.Contains(waiting))
                    continue;
                if (!waiting.Run.IsFinished && waiting.Blockers.Any(b => !b.IsFinished))
                    continue;
                _waiting.Remove(waiting);
                waiting.Limit?.Dispose();
                if (!waiting.Run.IsFinished)
                    waiting.Run.Start();
            }
        }

        private void FastTrackLimitReached(WaitingRun waiting)
        {
            if (!_waiting.Contains(waiting))
                return;
            foreach (EventRun blocker in waiting.Blockers.Where(b => !b.IsFinished).ToList())
                blocker.Cancel($"fast track took longer than {FastTrackLimitMs / 1000} s");
            CheckWaiting();
        }

        private void StopManualRuns()
        {
            foreach (EventRun run in _manualRuns.Values.ToList())
                run.Cancel("AutoTrigger is closing");
            _manualRuns.Clear();
            foreach (WaitingRun waiting in _waiting)
                waiting.Limit?.Dispose();
            _waiting.Clear();
        }

        private sealed class WaitingRun
        {
            public EventRun Run;
            public List<EventRun> Blockers;
            public IDisposable Limit;
        }
    }
}
