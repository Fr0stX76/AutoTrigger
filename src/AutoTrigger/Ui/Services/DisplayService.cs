using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Threading;
using AutoTrigger.Core.Displays;
using AutoTrigger.Core.Engine;
using AutoTrigger.Core.Logging;
using AutoTrigger.Core.Model;
using AutoTrigger.Core.Platform;

namespace AutoTrigger.Ui
{
    internal sealed class DisplayService
    {
        private readonly SettingsController _settings;
        private readonly IDisplayHardware _hardware;
        private readonly ActionExecutor _executor;
        private readonly Dispatcher _ui;
        private readonly Dictionary<string, DisplayCapabilities> _live =
            new Dictionary<string, DisplayCapabilities>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _signatures = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private List<MonitorInfo> _monitors = new List<MonitorInfo>();
        private bool _refreshing;
        private bool _refreshAgain;
        private string _shownMonitors;
        private bool _changing;

        public DisplayService(SettingsController settings, IDisplayHardware hardware, ActionExecutor executor, Dispatcher ui)
        {
            _settings = settings;
            _hardware = hardware;
            _executor = executor;
            _ui = ui;
        }

        public HdrState Hdr { get; private set; } = HdrState.Unknown;

        public IReadOnlyList<MonitorInfo> Monitors => _monitors;

        public event Action MonitorsChanged;

        public bool IsChanging => _changing;

        public event Action Changed;

        private AppSettings Settings => _settings.Current;

        public MonitorInfo MonitorOf(DisplayEntry entry) =>
            entry == null || !entry.IsLinked
                ? null
                : _monitors.FirstOrDefault(m => string.Equals(m.DevicePath, entry.DevicePath, StringComparison.OrdinalIgnoreCase));

        public DisplayCapabilities CapabilitiesOf(DisplayEntry entry)
        {
            if (entry == null)
                return null;
            if (entry.IsLinked && _live.TryGetValue(entry.DevicePath, out DisplayCapabilities live))
                return live;
            return entry.Known;
        }

        public void Refresh()
        {
            if (_refreshing)
            {
                _refreshAgain = true;
                return;
            }
            _refreshing = true;
            _executor.RunExclusiveAsync(Read).ContinueWith(t => _ui.BeginInvoke(new Action(() =>
            {
                _refreshing = false;
                if (t.Status == TaskStatus.RanToCompletion)
                    Apply(t.Result.Monitors, t.Result.Capabilities);
                else
                    Log.Warning("The displays cannot be read: " + t.Exception?.GetBaseException().Message);
                if (_refreshAgain)
                {
                    _refreshAgain = false;
                    Refresh();
                }
            })), TaskScheduler.Default);
        }

        private sealed class Reading
        {
            public List<MonitorInfo> Monitors;
            public Dictionary<string, DisplayCapabilities> Capabilities =
                new Dictionary<string, DisplayCapabilities>(StringComparer.OrdinalIgnoreCase);
        }

        private Reading Read()
        {
            var reading = new Reading { Monitors = _hardware.GetMonitors() };
            foreach (MonitorInfo monitor in reading.Monitors)
            {
                if (string.IsNullOrEmpty(monitor.DevicePath))
                    continue;
                string signature = ModeSignature(monitor);
                lock (_signatures)
                {
                    if (_signatures.TryGetValue(monitor.DevicePath, out string known) && known == signature)
                        continue;
                    _signatures[monitor.DevicePath] = signature;
                }
                try
                {
                    reading.Capabilities[monitor.DevicePath] = _hardware.GetCapabilities(monitor);
                }
                catch (Exception ex)
                {
                    lock (_signatures)
                        _signatures.Remove(monitor.DevicePath);
                    Log.Warning($"{monitor.Name}: what the display offers cannot be read: {ex.Message}");
                }
            }
            return reading;
        }

        private void Apply(List<MonitorInfo> monitors, Dictionary<string, DisplayCapabilities> capabilities)
        {
            _monitors = monitors;
            bool listChanged = DisplayLinker.Sync(Settings, monitors);
            bool known = false;
            foreach (MonitorInfo monitor in monitors)
            {
                DisplayEntry entry = EntryOf(monitor.DevicePath);
                if (entry != null && entry.Known == null && !string.IsNullOrEmpty(monitor.DevicePath)
                    && !capabilities.ContainsKey(monitor.DevicePath) && _live.TryGetValue(monitor.DevicePath, out DisplayCapabilities live))
                {
                    entry.Known = live;
                    known = true;
                }
            }
            foreach (KeyValuePair<string, DisplayCapabilities> pair in capabilities)
            {
                _live[pair.Key] = pair.Value;
                DisplayEntry entry = EntryOf(pair.Key);
                if (entry != null && !SettingsJson.SameCapabilities(entry.Known, pair.Value))
                {
                    entry.Known = pair.Value;
                    known = true;
                }
            }
            if (listChanged)
                _settings.Commit();
            else if (known)
                _settings.SaveOnly();
            string shown = string.Join(";", monitors.Select(ShownSignature));
            if (listChanged || known || shown != _shownMonitors)
            {
                _shownMonitors = shown;
                MonitorsChanged?.Invoke();
            }
            UpdateHdr();
        }

        private DisplayEntry EntryOf(string devicePath) =>
            Settings.Displays.FirstOrDefault(d => string.Equals(d.DevicePath, devicePath, StringComparison.OrdinalIgnoreCase));

        private static string ModeSignature(MonitorInfo m) =>
            $"{m.Desktop}|{m.Signal}|{m.Refresh.Numerator}/{m.Refresh.Denominator}|{m.HdrSupported}|{m.HdrOn}|{m.Color}";

        private static string ShownSignature(MonitorInfo m) =>
            $"{m.DevicePath}|{m.Name}|{m.GdiName}|{m.AdapterName}|{ModeSignature(m)}|{m.BitsPerColor}|{m.IsPrimary}|{m.X},{m.Y}";

        public void UpdateHdr()
        {
            List<MonitorInfo> hdr = ManagedHdr();
            var state = new HdrState { Available = hdr.Count > 0, On = hdr.Count > 0 && hdr.All(m => m.HdrOn) };
            if (state.Available != Hdr.Available || state.On != Hdr.On)
            {
                if (Hdr != HdrState.Unknown)
                    Log.Detailed($"HDR is now {(state.On ? "on" : "off")}");
                Hdr = state;
                Changed?.Invoke();
            }
        }

        private List<MonitorInfo> ManagedHdr() =>
            DisplayLinker.Managed(Settings, _monitors).Where(m => m.HdrSupported).ToList();

        public void SetHdr(bool on, Action<string> onError)
        {
            List<MonitorInfo> targets = ManagedHdr().Where(m => m.HdrOn != on).ToList();
            if (targets.Count == 0 || _changing)
            {
                Refresh();
                return;
            }
            _changing = true;
            Changed?.Invoke();
            _executor.RunExclusiveAsync(() =>
            {
                var errors = new List<string>();
                foreach (MonitorInfo monitor in targets)
                {
                    try
                    {
                        DisplayConfig.SetHdr(monitor, on);
                        Log.Info($"HDR turned {(on ? "on" : "off")} for {monitor.Name} (Status tab)");
                    }
                    catch (Exception ex)
                    {
                        Log.Error($"HDR could not be turned {(on ? "on" : "off")} for {monitor.Name}: {ex.Message}");
                        errors.Add($"{monitor.Name}: {ex.Message}");
                    }
                }
                return errors;
            }).ContinueWith(t => _ui.BeginInvoke(new Action(() =>
            {
                _changing = false;
                List<string> errors = t.Status == TaskStatus.RanToCompletion
                    ? t.Result
                    : new List<string> { t.Exception?.GetBaseException().Message ?? "unexpected error" };
                if (errors.Count > 0)
                    onError?.Invoke(string.Join("\n", errors));
                Changed?.Invoke();
                Refresh();
            })), TaskScheduler.Default);
        }
    }
}
