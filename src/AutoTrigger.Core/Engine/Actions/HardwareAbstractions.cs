using System;
using System.Collections.Generic;
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

    public interface IKeyboard
    {
        bool FrontWindowRunsAsAdmin();

        void Press(PressHotkeyAction keys, int holdMs);
    }
}
