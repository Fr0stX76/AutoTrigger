using System;
using System.Threading.Tasks;
using AutoTrigger.Core.Logging;
using AutoTrigger.Core.Model;

namespace AutoTrigger
{
    internal sealed partial class App
    {
        private void ApplyStartPause()
        {
            bool? paused = PausedAfter(_startCommand.Kind, _settings.MonitoringPaused);
            if (paused == null || paused.Value == _settings.MonitoringPaused)
                return;
            _settings.MonitoringPaused = paused.Value;
            _controller.SaveOnly();
        }

        private static bool? PausedAfter(CommandKind kind, bool paused)
        {
            switch (kind)
            {
                case CommandKind.Pause: return true;
                case CommandKind.Resume: return false;
                case CommandKind.Toggle: return !paused;
                default: return null;
            }
        }

        private Task<string> HandleCommand(Command command)
        {
            const string closing = "closing";
            if (_exiting)
                return Task.FromResult(closing);
            if (command.Kind == CommandKind.Launch)
            {
                var launched = new TaskCompletionSource<string>();
                Dispatcher.BeginInvoke(new Action(() =>
                    LaunchApp(command.AppId, ok => launched.TrySetResult(ok ? CommandServer.Ok : CommandServer.Failed))));
                return launched.Task;
            }
            if (command.RunsProfile)
            {
                var result = new TaskCompletionSource<string>();
                _engine.RunProfileNamed(command.ProfileName, command.Event, "command line",
                    found => result.TrySetResult(found ? CommandServer.Ok : "profile not found"));
                return result.Task;
            }
            return Dispatcher.InvokeAsync(() =>
            {
                if (_exiting)
                    return closing;
                bool? paused = PausedAfter(command.Kind, _settings.MonitoringPaused);
                if (paused != null && paused.Value != _settings.MonitoringPaused)
                    SetMonitoringPaused(paused.Value);
                return CommandServer.Ok;
            }).Task;
        }

        // A desktop shortcut names the application, not a profile, so it follows what its Launch button runs now.
        private void LaunchApp(Guid appId, Action<bool> done = null)
        {
            AppEntry app = _settings.FindApplication(appId);
            if (app == null)
            {
                Log.Warning($"Command line: no application with id {appId:D}");
                done?.Invoke(false);
                return;
            }
            Launch(app, "desktop shortcut", done);
        }

        private void SetMonitoringPaused(bool paused)
        {
            _settings.MonitoringPaused = paused;
            _controller.SaveOnly();
            _engine.SetPaused(paused);
        }
    }
}
