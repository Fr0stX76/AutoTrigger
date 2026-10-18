using System;
using System.Collections.Generic;
using System.Windows.Threading;
using AutoTrigger.Core.Model;
using AutoTrigger.Core.Storage;

namespace AutoTrigger.Ui
{
    internal sealed class SettingsController
    {
        private const int EngineDelayMs = 300;

        private readonly SettingsStore _store;
        private readonly DispatcherTimer _engineTimer;
        private Action<AppSettings> _toEngine;
        private List<ValidationIssue> _issues;

        public SettingsController(AppSettings current, SettingsStore store, Dispatcher ui)
        {
            Current = current;
            _store = store;
            _engineTimer = new DispatcherTimer(DispatcherPriority.Background, ui) { Interval = TimeSpan.FromMilliseconds(EngineDelayMs) };
            _engineTimer.Tick += (s, e) =>
            {
                _engineTimer.Stop();
                _toEngine?.Invoke(Copy());
            };
        }

        public AppSettings Current { get; }

        public event Action Changed;

        public int Version { get; private set; }

        public List<ValidationIssue> Issues => _issues ?? (_issues = SettingsValidator.Validate(Current));

        public void ConnectEngine(Action<AppSettings> toEngine) => _toEngine = toEngine;

        public void Commit()
        {
            Version++;
            _issues = null;
            _store.SaveSoon(Current);
            _engineTimer.Stop();
            _engineTimer.Start();
            Changed?.Invoke();
        }

        public void SaveOnly() => _store.SaveSoon(Current);

        public AppSettings Copy() => SettingsJson.Read(SettingsJson.Write(Current), new List<string>());

        public void Flush()
        {
            if (_engineTimer.IsEnabled)
            {
                _engineTimer.Stop();
                _toEngine?.Invoke(Copy());
            }
            _store.FlushPending();
        }
    }
}
