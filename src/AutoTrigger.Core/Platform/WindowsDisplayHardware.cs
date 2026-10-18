using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using AutoTrigger.Core.Displays;
using AutoTrigger.Core.Engine;
using AutoTrigger.Core.Logging;

namespace AutoTrigger.Core.Platform
{
    public sealed class WindowsDisplayHardware : IDisplayHardware
    {
        private const int SettleMs = 1000;
        private const int ConfirmTimeoutMs = 3000;

        public List<MonitorInfo> GetMonitors()
        {
            List<MonitorInfo> monitors = DisplayConfig.GetActiveMonitors();
            foreach (MonitorInfo monitor in monitors)
            {
                try
                {
                    if (NvApi.TryGetDisplayId(monitor.GdiName, out uint id))
                        monitor.Color = NvApi.GetColor(id);
                }
                catch (Exception ex)
                {
                    Log.Detailed($"{monitor.Name}: NVIDIA color cannot be read ({ex.Message})");
                }
            }
            return monitors;
        }

        public DisplayCapabilities GetCapabilities(MonitorInfo monitor)
        {
            var caps = new DisplayCapabilities { Signal = monitor.Signal, HdrSupported = monitor.HdrSupported };
            try
            {
                caps.DesktopSizes.AddRange(DisplayModes.DesktopSizes(monitor.GdiName));
            }
            catch (Exception ex)
            {
                Log.Detailed($"{monitor.Name}: resolutions cannot be read ({ex.Message})");
            }
            if (monitor.Desktop.IsSet && !caps.DesktopSizes.Contains(monitor.Desktop))
                caps.DesktopSizes.Insert(0, monitor.Desktop);
            try
            {
                caps.RefreshRates.AddRange(DisplayModes.RefreshRates(monitor.GdiName, monitor.Signal));
            }
            catch (Exception ex)
            {
                Log.Detailed($"{monitor.Name}: refresh rates cannot be read ({ex.Message})");
            }
            if (monitor.Refresh.IsSet && !caps.RefreshRates.Any(r => r.SameAs(monitor.Refresh)))
                caps.RefreshRates.Add(monitor.Refresh);
            try
            {
                if (NvApi.TryGetDisplayId(monitor.GdiName, out uint id))
                {
                    caps.Nvidia = true;
                    caps.Colors.AddRange(NvApi.SupportedColors(id));
                    if (monitor.Color != null && !caps.Colors.Contains(monitor.Color))
                        caps.Colors.Add(monitor.Color);
                }
            }
            catch (Exception ex)
            {
                Log.Detailed($"{monitor.Name}: NVIDIA colors cannot be read ({ex.Message})");
            }
            return caps;
        }

        public bool SetDesktopSize(MonitorInfo monitor, PixelSize size) => DisplayModes.SetDesktopSize(monitor, size);

        public bool SetRefresh(MonitorInfo monitor, RefreshRate rate, PixelSize desktop) => DisplayModes.SetRefresh(monitor, rate, desktop);

        public void SetHdr(MonitorInfo monitor, bool on)
        {
            DisplayConfig.SetHdr(monitor, on);
            for (int waited = 0; waited < ConfirmTimeoutMs; waited += 100)
            {
                MonitorInfo now = DisplayConfig.GetActiveMonitors().FirstOrDefault(m =>
                    string.Equals(m.DevicePath, monitor.DevicePath, StringComparison.OrdinalIgnoreCase));
                if (now != null && now.HdrOn == on)
                    return;
                Thread.Sleep(100);
            }
            Log.Detailed($"{monitor.Name}: HDR {(on ? "on" : "off")} not confirmed by Windows after {ConfirmTimeoutMs / 1000} s");
        }

        public bool IsColorSupported(MonitorInfo monitor, ColorSetting color) =>
            NvApi.TryGetDisplayId(monitor.GdiName, out uint id) && NvApi.IsSupported(id, color);

        public void SetColor(MonitorInfo monitor, ColorSetting color)
        {
            if (!NvApi.TryGetDisplayId(monitor.GdiName, out uint id))
                throw new InvalidOperationException("this display is not on an NVIDIA graphics card");
            NvApi.SetColor(id, color);
        }

        public void Settle() => Thread.Sleep(SettleMs);
    }
}
