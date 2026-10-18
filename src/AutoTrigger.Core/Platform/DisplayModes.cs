using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using AutoTrigger.Core.Displays;
using AutoTrigger.Core.Logging;

namespace AutoTrigger.Core.Platform
{
    public static class DisplayModes
    {
        private const int ConfirmTimeoutMs = 3000;

        public static bool SetDesktopSize(MonitorInfo monitor, PixelSize size)
        {
            int error;
            using (CcdConfig config = CcdConfig.Query())
            {
                int path = config.FindPath(monitor);
                uint source = config.SourceModeOf(path);
                uint target = config.TargetModeOf(path);
                PixelSize signal = target != CcdConfig.InvalidIndex ? config.TargetSize(target) : default;
                config.ResizeSource(source, size);
                config.SetScaling(path, signal.IsSet && !signal.Equals(size) ? CcdConfig.ScalingAspectRatioCenteredMax : CcdConfig.ScalingIdentity);
                error = config.Apply(NativeMethods.SDC_APPLY | NativeMethods.SDC_USE_SUPPLIED_DISPLAY_CONFIG | NativeMethods.SDC_SAVE_TO_DATABASE);
                if (error != 0)
                    Log.Detailed($"{monitor.Name}: desktops sent: {config.Layout()}");
            }
            if (error == 0)
            {
                Confirm(monitor, m => m.Desktop.Equals(size));
                return true;
            }
            Log.Detailed($"{monitor.Name}: desktop {size} with scaling refused (error {error}); changing the signal");

            using (CcdConfig config = CcdConfig.Query())
            {
                int path = config.FindPath(monitor);
                config.ResizeSource(config.SourceModeOf(path), size);
                config.SetScaling(path, CcdConfig.ScalingPreferred);
                config.ReleaseTargetMode(path);
                int second = config.Apply(NativeMethods.SDC_APPLY | NativeMethods.SDC_USE_SUPPLIED_DISPLAY_CONFIG
                    | NativeMethods.SDC_SAVE_TO_DATABASE | NativeMethods.SDC_ALLOW_CHANGES);
                if (second != 0)
                {
                    Log.Detailed($"{monitor.Name}: desktops sent: {config.Layout()}");
                    throw new Win32Exception(second);
                }
            }
            Confirm(monitor, m => m.Desktop.Equals(size));
            return false;
        }

        public static bool SetRefresh(MonitorInfo monitor, RefreshRate rate, PixelSize desktop)
        {
            PixelSize before;
            PixelSize signal;
            using (CcdConfig config = CcdConfig.Query())
            {
                int path = config.FindPath(monitor);
                uint source = config.SourceModeOf(path);
                uint target = config.TargetModeOf(path);
                before = config.SourceSize(source);
                signal = target != CcdConfig.InvalidIndex ? config.TargetSize(target) : before;
                if (!before.Equals(signal))
                    config.ResizeSource(source, signal);
                config.SetScaling(path, CcdConfig.ScalingIdentity);
                config.SetRefresh(path, rate);
                config.ReleaseTargetMode(path);
                int error = config.Apply(NativeMethods.SDC_APPLY | NativeMethods.SDC_USE_SUPPLIED_DISPLAY_CONFIG
                    | NativeMethods.SDC_SAVE_TO_DATABASE | NativeMethods.SDC_ALLOW_CHANGES);
                if (error != 0)
                {
                    Log.Detailed($"{monitor.Name}: desktops sent: {config.Layout()}");
                    throw new Win32Exception(error);
                }
            }

            MonitorInfo now = Confirm(monitor, m => m.Refresh.SameAs(rate));
            if (now != null && !now.Refresh.SameAs(rate))
                throw new InvalidOperationException($"Windows set {now.Refresh} instead of {rate}");
            if (now != null && !now.Signal.Equals(signal))
                Log.Warning($"{monitor.Name}: Windows changed the signal to {now.Signal} with {rate}");

            PixelSize wanted = desktop.IsSet ? desktop : before;
            PixelSize current = now != null ? now.Desktop : signal;
            if (wanted.Equals(current))
                return true;
            return SetDesktopSize(monitor, wanted);
        }

        private static MonitorInfo Confirm(MonitorInfo monitor, Func<MonitorInfo, bool> done)
        {
            MonitorInfo now = null;
            int waited = 0;
            while (true)
            {
                try
                {
                    now = DisplayConfig.GetActiveMonitors().FirstOrDefault(m =>
                        string.Equals(m.DevicePath, monitor.DevicePath, StringComparison.OrdinalIgnoreCase));
                }
                catch (Win32Exception)
                {
                    now = null;
                }
                if ((now != null && done(now)) || waited >= ConfirmTimeoutMs)
                    return now;
                Thread.Sleep(100);
                waited += 100;
            }
        }

        public static List<PixelSize> DesktopSizes(string gdiName)
        {
            var sizes = new List<PixelSize>();
            foreach (WindowsMode mode in WindowsModes(gdiName))
            {
                if (mode.Bits >= 24 && mode.Size.IsSet && !sizes.Contains(mode.Size))
                    sizes.Add(mode.Size);
            }
            return sizes.OrderByDescending(s => s.Width).ThenByDescending(s => s.Height).ToList();
        }

        public static List<RefreshRate> RefreshRates(string gdiName, PixelSize signal)
        {
            List<RefreshRate> rates = null;
            try
            {
                rates = Dxgi.RefreshRates(gdiName, signal);
            }
            catch (Exception ex)
            {
                Log.Detailed($"{gdiName}: DXGI modes cannot be read ({ex.Message})");
            }
            if (rates == null || rates.Count == 0)
                rates = WholeHertzRates(gdiName, signal);
            return rates.OrderByDescending(r => r.Hz).ToList();
        }

        private static List<RefreshRate> WholeHertzRates(string gdiName, PixelSize signal)
        {
            var rates = new List<RefreshRate>();
            foreach (WindowsMode mode in WindowsModes(gdiName))
            {
                if (!mode.Size.Equals(signal) || mode.Hz <= 1)
                    continue;
                RefreshRate rate = (mode.Hz + 1) % 24 == 0 || (mode.Hz + 1) % 30 == 0
                    ? new RefreshRate((uint)(mode.Hz + 1) * 1000, 1001)
                    : new RefreshRate((uint)mode.Hz, 1);
                if (!rates.Any(r => r.SameAs(rate)))
                    rates.Add(rate);
            }
            return rates;
        }

        private struct WindowsMode
        {
            public int Bits;
            public PixelSize Size;
            public int Hz;
        }

        private static List<WindowsMode> WindowsModes(string gdiName)
        {
            var modes = new List<WindowsMode>();
            IntPtr mode = Marshal.AllocHGlobal(NativeMethods.DevModeSize + 512);
            try
            {
                for (int i = 0; ; i++)
                {
                    for (int b = 0; b < NativeMethods.DevModeSize; b++)
                        Marshal.WriteByte(mode, b, 0);
                    Marshal.WriteInt16(mode, 68, NativeMethods.DevModeSize);
                    if (!NativeMethods.EnumDisplaySettingsEx(gdiName, i, mode, 0))
                        break;
                    modes.Add(new WindowsMode
                    {
                        Bits = Marshal.ReadInt32(mode, 168),
                        Size = new PixelSize(Marshal.ReadInt32(mode, 172), Marshal.ReadInt32(mode, 176)),
                        Hz = Marshal.ReadInt32(mode, 184),
                    });
                }
            }
            finally
            {
                Marshal.FreeHGlobal(mode);
            }
            return modes;
        }
    }
}
