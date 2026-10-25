using System;
using System.Threading;
using System.Windows.Threading;
using AutoTrigger.Core.Logging;
using AutoTrigger.Core.Model;
using AutoTrigger.Core.Platform;

namespace AutoTrigger
{
    internal sealed partial class App
    {
        // The network is often not ready yet when AutoTrigger starts with Windows.
        private static readonly TimeSpan FirstUpdateCheck = TimeSpan.FromMinutes(1);

        private readonly CancellationTokenSource _updateCancel = new CancellationTokenSource();
        private UpdateCheck _updates;
        private DispatcherTimer _updateTimer;
        private AppVersion _current;
        private AppVersion _available;
        private int _updateHours;
        private bool _checking;

        private void CreateUpdateCheck()
        {
            _current = AppVersion.ParseBuild(_version);
            if (_current == null)
            {
                Log.Warning($"Update check off: version '{_version}' cannot be compared");
                return;
            }
            _updates = new UpdateCheck(_current);
            _updateTimer = new DispatcherTimer();
            _updateTimer.Tick += OnUpdateTimer;
            _window.UpdateClicked += OpenUpdatePage;
            _controller.Changed += ApplyUpdateSettings;
            ApplyUpdateSettings();
        }

        private void ApplyUpdateSettings()
        {
            if (!_settings.CheckForUpdates)
            {
                if (_updateHours != 0)
                    Log.Info("Update check turned off");
                _updateTimer.Stop();
                _updateHours = 0;
                ShowUpdate(null);
                return;
            }
            if (_updateHours == _settings.UpdateCheckHours)
                return;
            bool turnedOn = _updateHours == 0;
            _updateHours = _settings.UpdateCheckHours;
            _updateTimer.Stop();
            _updateTimer.Interval = turnedOn ? FirstUpdateCheck : TimeSpan.FromHours(_updateHours);
            _updateTimer.Start();
            Log.Info(turnedOn
                ? $"Update check on: first check in {FirstUpdateCheck.TotalMinutes:0} min, then every {_updateHours} h"
                : $"Update check every {_updateHours} h");
        }

        private async void OnUpdateTimer(object sender, EventArgs e)
        {
            _updateTimer.Interval = TimeSpan.FromHours(_updateHours);
            if (_checking)
                return;
            _checking = true;
            try
            {
                AppVersion latest = await _updates.LatestAsync(_updateCancel.Token).ConfigureAwait(true);
                if (latest != null && !_exiting && _settings.CheckForUpdates)
                    ShowUpdate(latest.IsNewerThan(_current) ? latest : null);
            }
            catch (OperationCanceledException)
            {
                // AutoTrigger is closing.
            }
            finally
            {
                _checking = false;
            }
        }

        private void ShowUpdate(AppVersion version)
        {
            if (version != null && _available == null)
                Log.Info($"Update available: {version}");
            _available = version;
            _window.ShowUpdate(version?.ToString());
            _tray.UpdateVersion = version?.ToString();
        }

        private void OpenUpdatePage()
        {
            if (_available != null)
                ShellOpen(UpdateCheck.ReleasePage(_available).AbsoluteUri);
        }

        private void StopUpdateCheck()
        {
            _updateTimer?.Stop();
            _updateCancel.Cancel();
            _updates?.Dispose();
            _updateCancel.Dispose();
        }
    }
}
