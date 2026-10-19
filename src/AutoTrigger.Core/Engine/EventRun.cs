using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using AutoTrigger.Core.Logging;
using AutoTrigger.Core.Model;

namespace AutoTrigger.Core.Engine
{
    internal sealed class EventRun
    {
        private readonly IEngineScheduler _scheduler;
        private readonly Func<ProfileAction, Task<ActionOutcome>> _execute;
        private readonly Action<EventRun> _onFinished;
        private readonly List<PlannedAction> _plan;
        private readonly string _prefix;

        private long _t0;
        private int _next;
        private long _previousStart;
        private PlannedAction _running;
        private IDisposable _timer;
        private IDisposable _holdCapTimer;
        private bool _started;
        private bool _holding;
        private bool _fast;

        public EventRun(IEngineScheduler scheduler, Func<ProfileAction, Task<ActionOutcome>> execute, string source,
            string trigger, EventKind kind, Profile profile, List<PlannedAction> plan, Action<EventRun> onFinished)
        {
            _scheduler = scheduler;
            _execute = execute;
            _onFinished = onFinished;
            _plan = plan;
            Kind = kind;
            Profile = profile;
            Trigger = trigger;
            _prefix = $"[{source}]";
        }

        public EventKind Kind { get; }

        public Profile Profile { get; }

        public string Trigger { get; }

        public bool HasActions => _plan.Count > 0;

        public bool IsCancelled { get; private set; }

        public bool IsFinished { get; private set; }

        public static string EventText(EventKind kind)
        {
            switch (kind)
            {
                case EventKind.Started: return "started";
                case EventKind.Closed: return "closed";
                case EventKind.GotFocus: return "gained focus";
                default: return "lost focus";
            }
        }

        public void Start()
        {
            if (_started)
                return;
            _started = true;
            _t0 = _scheduler.NowMs;
            _previousStart = _t0;
            Advance();
        }

        public void FastTrack(string reason)
        {
            if (IsCancelled || IsFinished || _fast)
                return;
            _fast = true;
            Log.Info($"{_prefix} fast-tracked: {reason}");
            if (!_started)
                return;
            if (_holding)
            {
                ReleaseHold(_running, 0, true);
                return;
            }
            if (_timer != null)
            {
                _timer.Dispose();
                _timer = null;
                Advance();
            }
        }

        public void Cancel(string reason)
        {
            if (IsCancelled || IsFinished)
                return;
            IsCancelled = true;
            _timer?.Dispose();
            _timer = null;
            _holdCapTimer?.Dispose();
            _holdCapTimer = null;
            _running = null;
            _holding = false;
            for (int i = _next; i < _plan.Count; i++)
                Log.Info($"{_prefix} {Describe(_plan[i])}: cancelled ({reason})");
            _next = _plan.Count;
            IsFinished = true;
        }

        private void Advance()
        {
            if (IsCancelled || IsFinished || !_started || _running != null)
                return;
            if (_next >= _plan.Count)
            {
                Finish();
                return;
            }

            PlannedAction planned = _plan[_next];
            long now = _scheduler.NowMs;
            long due = _fast ? now : _previousStart + planned.DelayMs;
            if (due > now)
            {
                _timer?.Dispose();
                _timer = _scheduler.Schedule((int)Math.Min(int.MaxValue, due - now), () =>
                {
                    _timer = null;
                    Advance();
                });
                return;
            }

            _next++;
            if (planned.DelayMs > 0 && now - due > 50 && _next > 1)
                Log.Detailed($"{_prefix} {Describe(planned)}: the previous action took longer than the {planned.DelayMs} ms delay; "
                    + $"starting at +{now - _t0} ms");
            Launch(planned, now);
        }

        private void Launch(PlannedAction planned, long now)
        {
            long offset = now - _t0;
            _running = planned;
            _previousStart = now;

            if (planned.Action is RunProgramAction run && run.HoldNext && !_fast)
                _holdCapTimer = _scheduler.Schedule(run.HoldCapSec * 1000, () => ReleaseHold(planned, run.HoldCapSec, false));

            var stopwatch = Stopwatch.StartNew();
            Task<ActionOutcome> task;
            try
            {
                task = _execute(planned.Action);
            }
            catch (Exception ex)
            {
                task = Task.FromResult(ActionOutcome.Failure(ex.Message));
            }
            task.ContinueWith(t =>
            {
                ActionOutcome outcome = t.Status == TaskStatus.RanToCompletion && t.Result != null
                    ? t.Result
                    : ActionOutcome.Failure(t.Exception?.GetBaseException().Message ?? "the action did not complete");
                long elapsed = stopwatch.ElapsedMilliseconds;
                _scheduler.Post(() => OnOutcome(planned, outcome, offset, elapsed));
            }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }

        private void OnOutcome(PlannedAction planned, ActionOutcome outcome, long offset, long elapsedMs)
        {
            string head = $"{_prefix} +{offset} ms {Describe(planned)}: ";
            string tail = $" ({elapsedMs} ms)";
            switch (outcome.Result)
            {
                case ActionResult.Failed:
                    Log.Error(head + "failed, " + outcome.Message + tail);
                    break;
                case ActionResult.Skipped:
                    Log.Warning(head + "skipped, " + outcome.Message + tail);
                    break;
                case ActionResult.Cancelled:
                    Log.Info(head + "cancelled, " + outcome.Message + tail);
                    break;
                default:
                    Log.Info(head + outcome.Message + tail);
                    break;
            }

            if (_running != planned)
                return;

            if (planned.Action is RunProgramAction run && run.HoldNext && !_fast)
            {
                Task exited = outcome.Result == ActionResult.Succeeded ? outcome.ProgramExited : null;
                if (exited == null)
                {
                    if (outcome.Result == ActionResult.Succeeded)
                        Log.Warning($"{_prefix} {Describe(planned)}: no process to wait for; the next actions are not held");
                    ReleaseHold(planned, 0, false);
                }
                else if (exited.IsCompleted)
                    ReleaseHold(planned, 0, false);
                else
                {
                    _holding = true;
                    exited.ContinueWith(_ => _scheduler.Post(() => ReleaseHold(planned, 0, false)),
                        CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
                }
                return;
            }

            _holdCapTimer?.Dispose();
            _holdCapTimer = null;

            _running = null;
            Advance();
        }

        private void ReleaseHold(PlannedAction planned, int capSec, bool fastTracked)
        {
            if (planned == null || _running != planned)
                return;
            _running = null;
            _holding = false;
            _holdCapTimer?.Dispose();
            _holdCapTimer = null;
            if (fastTracked)
                Log.Info($"{_prefix} {Describe(planned)}: hold released (fast-tracked)");
            else if (capSec > 0)
                Log.Warning($"{_prefix} {Describe(planned)}: still running after {capSec} s; the next actions start now");
            else
                Log.Detailed($"{_prefix} {Describe(planned)}: finished at +{_scheduler.NowMs - _t0} ms; next actions released");
            Advance();
        }

        private void Finish()
        {
            if (IsFinished)
                return;
            IsFinished = true;
            _onFinished?.Invoke(this);
        }

        private static string Describe(PlannedAction planned)
        {
            string text = planned.Action.Describe();
            return string.IsNullOrEmpty(planned.Source) ? text : $"{text} (profile {planned.Source})";
        }
    }
}
