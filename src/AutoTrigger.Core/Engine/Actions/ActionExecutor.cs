using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AutoTrigger.Core.Logging;
using AutoTrigger.Core.Model;

namespace AutoTrigger.Core.Engine
{
    public sealed class ActionExecutor : IDisposable
    {
        private const int ClosePollMs = 100;
        public const int KeyHoldMs = 50;

        private readonly IProcessSystem _processes;
        private readonly DisplayActionRunner _displays;
        private readonly AudioActionRunner _audio;
        private readonly IPowerPlans _powerPlans;
        private readonly IKeyboard _keyboard;
        private readonly SemaphoreSlim _hardware = new SemaphoreSlim(1, 1);
        private readonly CancellationTokenSource _shutdown = new CancellationTokenSource();
        private readonly object _orderLock = new object();
        private readonly int _ownPid;
        private Task _orderTail = Task.FromResult(true);

        public ActionExecutor(IProcessSystem processes, IDisplayHardware displays = null, IAudioHardware audio = null,
            IPowerPlans powerPlans = null, IKeyboard keyboard = null)
        {
            _processes = processes ?? throw new ArgumentNullException(nameof(processes));
            _displays = displays == null ? null : new DisplayActionRunner(displays);
            _audio = audio == null ? null : new AudioActionRunner(audio);
            _powerPlans = powerPlans;
            _keyboard = keyboard;
            using (Process self = Process.GetCurrentProcess())
                _ownPid = self.Id;
        }

        public Func<IReadOnlyList<DisplayEntry>> DisplayEntries { get; set; }

        public Task<ActionOutcome> ExecuteAsync(ProfileAction action)
        {
            if (_shutdown.IsCancellationRequested)
                return Task.FromResult(ActionOutcome.Cancel("AutoTrigger is closing"));
            switch (action)
            {
                case RunProgramAction run:
                    return Safe(InOrder(() => RunProgram(run)));
                case CloseProgramAction close:
                    return Safe(CloseProgramAsync(close));
                case PowerPlanAction plan:
                    return Safe(Task.Run(() => SetPowerPlan(plan)));
                case PressHotkeyAction keys:
                    return Safe(InOrder(() => PressHotkey(keys)));
                case IncludeProfileAction _:
                    return Task.FromResult(ActionOutcome.Skip("'Reference profile' is resolved before the actions run"));
                case UnknownAction unknown:
                    return Task.FromResult(ActionOutcome.Skip($"unknown action type '{unknown.TypeName}'"));
                default:
                    if (action.IsHardwareChange)
                        return Safe(Task.Run(() => HardwareAsync(action)));
                    return Task.FromResult(ActionOutcome.Skip($"unsupported action '{action.Type}'"));
            }
        }

        public async Task<T> RunExclusiveAsync<T>(Func<T> work)
        {
            await _hardware.WaitAsync().ConfigureAwait(false);
            try
            {
                return await Task.Run(work).ConfigureAwait(false);
            }
            finally
            {
                _hardware.Release();
            }
        }

        private Task<T> InOrder<T>(Func<T> work)
        {
            lock (_orderLock)
            {
                Task<T> task = _orderTail.ContinueWith(_ => work(), CancellationToken.None,
                    TaskContinuationOptions.None, TaskScheduler.Default);
                _orderTail = task;
                return task;
            }
        }

        private static async Task<ActionOutcome> Safe(Task<ActionOutcome> task)
        {
            try
            {
                return await task.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return ActionOutcome.Cancel("AutoTrigger is closing");
            }
            catch (Exception ex)
            {
                Log.Detailed("Action error details: " + ex);
                return ActionOutcome.Failure(ex.Message);
            }
        }

        private async Task<ActionOutcome> HardwareAsync(ProfileAction action)
        {
            await _hardware.WaitAsync(_shutdown.Token).ConfigureAwait(false);
            try
            {
                switch (action)
                {
                    case DisplayAction display when _displays != null:
                        return _displays.Run(display, DisplayEntries?.Invoke() ?? new List<DisplayEntry>());
                    case AudioAction audio when _audio != null:
                        return _audio.Run(audio);
                    case DisplayAction _:
                    case AudioAction _:
                        return ActionOutcome.Skip("display and audio changes are not available here");
                    default:
                        return ActionOutcome.Skip($"unsupported action '{action.Type}'");
                }
            }
            finally
            {
                _hardware.Release();
            }
        }

        private ActionOutcome SetPowerPlan(PowerPlanAction action)
        {
            if (_powerPlans == null)
                return ActionOutcome.Skip("power plans are not available here");
            if (action.PlanId == Guid.Empty)
                return ActionOutcome.Failure("no power plan set");
            PowerPlan plan = _powerPlans.GetPlans().Find(p => p.Id == action.PlanId);
            if (plan == null)
                return ActionOutcome.Failure("power plan not found");
            if (_powerPlans.GetActive() == plan.Id)
                return ActionOutcome.Success("already active");
            _powerPlans.SetActive(plan.Id);
            return ActionOutcome.Success("now active");
        }

        private ActionOutcome PressHotkey(PressHotkeyAction action)
        {
            if (_keyboard == null)
                return ActionOutcome.Skip("hotkeys are not available here");
            if (action.Key == 0)
                return ActionOutcome.Failure("no hotkey set");
            string keys = KeyNames.Combination(action);
            if (_keyboard.FrontWindowRunsAsAdmin())
                return ActionOutcome.Failure($"{keys} not sent: the window in front runs as administrator, and Windows blocks keys sent to it");
            _keyboard.Press(action, KeyHoldMs);
            return ActionOutcome.Success(keys + " sent");
        }

        private ActionOutcome RunProgram(RunProgramAction action)
        {
            string path = ExpandPath(action.Path);
            if (path.Length == 0)
                return ActionOutcome.Failure("no program set");

            string folder = ProgramFolder(path);
            StartedProgram started;
            try
            {
                started = _processes.Start(path, action.Arguments ?? string.Empty, folder, action.HoldNext);
            }
            catch (Exception ex)
            {
                return ActionOutcome.Failure($"cannot start '{path}': {ex.Message}");
            }

            if (started == null || started.Pid == 0)
                return ActionOutcome.Success("opened", started?.Exited);
            return ActionOutcome.Success($"started, pid {started.Pid}", started.Exited);
        }

        public static string ExpandPath(string path)
        {
            string cleaned = (path ?? string.Empty).Trim();
            if (cleaned.Length >= 2 && cleaned[0] == '"' && cleaned[cleaned.Length - 1] == '"')
                cleaned = cleaned.Substring(1, cleaned.Length - 2).Trim();
            return Environment.ExpandEnvironmentVariables(cleaned);
        }

        public static string ProgramFolder(string path)
        {
            try
            {
                if (!Path.IsPathRooted(path))
                    return null;
                string folder = Path.GetDirectoryName(path);
                return !string.IsNullOrEmpty(folder) && Directory.Exists(folder) ? folder : null;
            }
            catch (ArgumentException)
            {
                return null;
            }
        }

        private async Task<ActionOutcome> CloseProgramAsync(CloseProgramAction action)
        {
            CloseRequest request = await InOrder(() => RequestClose(action)).ConfigureAwait(false);
            if (request.Outcome != null)
                return request.Outcome;
            return await FinishCloseAsync(action, request).ConfigureAwait(false);
        }

        private CloseRequest RequestClose(CloseProgramAction action)
        {
            var request = new CloseRequest();
            string target = ExpandPath(action.Target);
            if (target.Length == 0)
            {
                request.Outcome = ActionOutcome.Failure("no program set");
                return request;
            }

            request.ExeName = CloseTargetExeName(target);
            string fullPath = HasFolder(target) ? target : null;
            var running = new List<ProcessEntry>();
            _processes.Snapshot(running);
            foreach (ProcessEntry entry in running)
            {
                if (entry.Pid == _ownPid || !string.Equals(entry.ExeName, request.ExeName, StringComparison.OrdinalIgnoreCase))
                    continue;
                ProcessDetails details = _processes.Query(entry.Pid);
                if (fullPath != null && (details == null || !PathMatcher.SamePath(details.Path, fullPath)))
                    continue;
                request.Instances.Add(details ?? new ProcessDetails { Pid = entry.Pid });
            }

            if (request.Instances.Count == 0)
            {
                request.Outcome = ActionOutcome.Success($"{request.ExeName} was not running");
                return request;
            }

            foreach (ProcessDetails p in request.Instances)
            {
                if (_processes.RequestClose(p.Pid))
                    request.Asked++;
            }
            return request;
        }

        private async Task<ActionOutcome> FinishCloseAsync(CloseProgramAction action, CloseRequest request)
        {
            string exeName = request.ExeName;
            List<ProcessDetails> instances = request.Instances;
            var stopwatch = Stopwatch.StartNew();
            int waitMs = request.Asked > 0 ? action.ForceAfterSec * 1000 : 0;
            List<ProcessDetails> remaining = StillRunning(instances);
            while (remaining.Count > 0 && stopwatch.ElapsedMilliseconds < waitMs)
            {
                await Task.Delay(ClosePollMs, _shutdown.Token).ConfigureAwait(false);
                remaining = StillRunning(remaining);
            }

            int closed = instances.Count - remaining.Count;
            if (remaining.Count == 0)
                return ActionOutcome.Success(Plural(closed, "instance") + " closed");

            if (!action.Force)
            {
                string why = request.Asked == 0
                    ? $"{exeName} has no window to close; turn on 'Force' to end it"
                    : $"{Plural(remaining.Count, "instance")} of {exeName} still running after {action.ForceAfterSec} s; 'Force' is off";
                return ActionOutcome.Failure(why);
            }

            var errors = new List<string>();
            foreach (ProcessDetails p in remaining)
            {
                try
                {
                    _processes.Kill(p.Pid);
                }
                catch (Exception ex)
                {
                    errors.Add($"pid {p.Pid}: {ex.Message}");
                }
            }
            if (errors.Count > 0)
                return ActionOutcome.Failure($"could not end {exeName} ({string.Join("; ", errors)}). "
                    + "A program run as administrator cannot be ended by AutoTrigger.");
            return ActionOutcome.Success($"{Plural(instances.Count, "instance")} closed, {remaining.Count} forced");
        }

        private List<ProcessDetails> StillRunning(List<ProcessDetails> processes)
        {
            var result = new List<ProcessDetails>();
            foreach (ProcessDetails p in processes)
            {
                if (_processes.IsRunning(p.Pid, p.CreationTime))
                    result.Add(p);
            }
            return result;
        }

        public static string CloseTargetExeName(string target)
        {
            string name;
            try
            {
                name = Path.GetFileName(target);
            }
            catch (ArgumentException)
            {
                name = target;
            }
            if (!name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                name += ".exe";
            return name;
        }

        private static bool HasFolder(string target) => target.IndexOf('\\') >= 0 || target.IndexOf('/') >= 0;

        private static string Plural(int count, string noun) => count == 1 ? $"1 {noun}" : $"{count} {noun}s";

        public void Dispose()
        {
            _shutdown.Cancel();
        }

        private sealed class CloseRequest
        {
            public string ExeName;
            public readonly List<ProcessDetails> Instances = new List<ProcessDetails>();
            public int Asked;
            public ActionOutcome Outcome;
        }
    }
}
