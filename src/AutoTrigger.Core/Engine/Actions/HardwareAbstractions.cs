using System;
using System.Collections.Generic;
using System.Linq;
using AutoTrigger.Core.Displays;
using AutoTrigger.Core.Model;

namespace AutoTrigger.Core.Engine
{
    public interface IDisplayHardware
    {
        List<MonitorInfo> GetMonitors();

        DisplayCapabilities GetCapabilities(MonitorInfo monitor);

        bool SetDesktopSize(MonitorInfo monitor, PixelSize size);

        bool SetRefresh(MonitorInfo monitor, RefreshRate rate, PixelSize desktop);

        void SetHdr(MonitorInfo monitor, bool on);

        bool IsColorSupported(MonitorInfo monitor, ColorSetting color);

        void SetColor(MonitorInfo monitor, ColorSetting color);

        void Settle();
    }

    public sealed class AudioDevice
    {
        public string Id { get; set; }

        public string Name { get; set; }

        public bool Playback { get; set; }

        public Guid Guid => GuidOf(Id);

        public static Guid GuidOf(string idOrGuid)
        {
            string text = (idOrGuid ?? string.Empty).Trim();
            int dot = text.LastIndexOf("}.", StringComparison.Ordinal);
            if (dot >= 0)
                text = text.Substring(dot + 2);
            return Guid.TryParse(text, out Guid g) ? g : Guid.Empty;
        }

        public bool Matches(string stored)
        {
            if (string.IsNullOrWhiteSpace(stored))
                return false;
            Guid g = GuidOf(stored);
            return g != Guid.Empty ? g == Guid : string.Equals(stored.Trim(), Id, StringComparison.OrdinalIgnoreCase);
        }

        // A driver update or another USB port gives the same device a new id, so the saved name stands in for a
        // saved id that is gone; only when one connected device has that name, since two alike can't be told apart.
        // The engine and the action window both use it, so that they never disagree.
        public static AudioDevice Find(IEnumerable<AudioDevice> connected, string stored, string storedName)
        {
            if (string.IsNullOrWhiteSpace(stored))
                return null;
            List<AudioDevice> devices = connected.ToList();
            AudioDevice saved = devices.FirstOrDefault(d => d.Matches(stored));
            string name = (storedName ?? string.Empty).Trim();
            if (saved != null || name.Length == 0)
                return saved;
            List<AudioDevice> named = devices
                .Where(d => string.Equals((d.Name ?? string.Empty).Trim(), name, StringComparison.OrdinalIgnoreCase))
                .Take(2)
                .ToList();
            return named.Count == 1 ? named[0] : null;
        }
    }

    public interface IAudioHardware
    {
        List<AudioDevice> GetDevices(bool playback);

        string GetDefaultId(bool playback);

        void SetDefault(AudioDevice device);
    }

    public sealed class PowerPlan
    {
        public Guid Id { get; set; }

        public string Name { get; set; }
    }

    public interface IPowerPlans
    {
        List<PowerPlan> GetPlans();

        Guid GetActive();

        void SetActive(Guid id);
    }

    public interface IScreenSaver
    {
        bool IsOn();

        void Set(bool on);
    }

    public interface IKeyboard
    {
        // program is the exe name of the window's process, or null when Windows does not give it.
        bool FrontWindowRunsAsAdmin(out string program);

        bool WaitForHeldKeys(int timeoutMs);

        void Press(KeyCombination keys, int holdMs);
    }
}
