using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using AutoTrigger.Core.Logging;
using AutoTrigger.Core.Model;

namespace AutoTrigger.Core.Engine
{
    public sealed partial class TriggerEngine
    {
        private const int RestartCloseTimeoutMs = 15000;
        private const int RestartStartTimeoutMs = 30000;

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
                // A protected process hides its command line; the restart then goes without arguments.
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

            TrackedProcess[] targets = app.Processes.Values.ToArray();
            // Ending and starting processes can block, so it runs off the engine thread.
            Task.Run(() =>
            {
                var errors = new List<string>();
                foreach (TrackedProcess target in targets)
                {
                    try
                    {
                        _processes.Kill(target.Pid, target.CreationTime);
                    }
                    catch (Win32Exception ex)
                    {
                        errors.Add($"pid {target.Pid}: {ex.Message}");
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

        private sealed class RestartState
        {
            public string Path;
            public string Arguments;
            public bool Relaunching;
            public IDisposable Timeout;
        }
    }
}
