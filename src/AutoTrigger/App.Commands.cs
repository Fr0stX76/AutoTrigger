using System.Threading.Tasks;

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

        private void SetMonitoringPaused(bool paused)
        {
            _settings.MonitoringPaused = paused;
            _controller.SaveOnly();
            _engine.SetPaused(paused);
        }
    }
}
