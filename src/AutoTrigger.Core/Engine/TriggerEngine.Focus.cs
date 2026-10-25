using System;
using AutoTrigger.Core.Logging;
using AutoTrigger.Core.Model;

namespace AutoTrigger.Core.Engine
{
    public sealed partial class TriggerEngine
    {
        private const int EmptyFrameChecks = 3;
        private const int EmptyFrameCheckMs = 500;

        private int _emptyFrameChecks;

        private void OnForegroundChanged()
        {
            _scheduler.Post(() =>
            {
                if (!_running || _paused)
                    return;
                _focusTimer?.Dispose();
                _emptyFrameChecks = 0;
                // Alt+Tab and splash screens move the focus briefly; only a focus that stays FocusStabilityMs counts.
                _focusTimer = _scheduler.Schedule(_settings.FocusStabilityMs, CheckFocus);
            });
        }

        private void CheckFocus()
        {
            _focusTimer = null;
            if (!_running || _paused)
                return;
            AppState app = ResolveForeground(true, out bool emptyFrame);
            // A Store app frame can get the focus before its app is attached to it.
            if (emptyFrame && _emptyFrameChecks < EmptyFrameChecks)
            {
                _emptyFrameChecks++;
                Log.Detailed("Focus: Windows Store app frame without its app yet; checking again");
                _focusTimer = _scheduler.Schedule(EmptyFrameCheckMs, CheckFocus);
                return;
            }
            _emptyFrameChecks = 0;
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

        private AppState ResolveForeground(bool pollIfUnknown) => ResolveForeground(pollIfUnknown, out _);

        private AppState ResolveForeground(bool pollIfUnknown, out bool emptyFrame)
        {
            emptyFrame = false;
            if (_foreground == null)
                return null;
            int pid;
            try
            {
                pid = _foreground.GetForegroundProcessId();
            }
            catch (Exception ex)
            {
                // The window in front can close between two calls; that counts as no focus.
                Log.Detailed("Cannot read the foreground window: " + ex.Message);
                return null;
            }
            if (pid == ForegroundIds.EmptyFrame)
            {
                emptyFrame = true;
                return null;
            }
            if (pid <= 0)
                return null;
            // A new window can get the focus before the next poll has seen its process.
            if (pollIfUnknown && !_known.ContainsKey(pid))
                Poll();
            return _known.TryGetValue(pid, out KnownProcess known) && known.Owner != null && known.Owner.Processes.ContainsKey(pid)
                ? known.Owner
                : null;
        }
    }
}
