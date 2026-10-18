using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using AutoTrigger.Core.Displays;

namespace AutoTrigger.Core.Platform
{
    public static class DisplayConfig
    {
        private const int HeaderSize = 20;

        private const uint GetTargetName = 2;
        private const uint GetAdvancedColorInfo = 9;
        private const uint SetAdvancedColorState = 10;
        private const uint GetAdvancedColorInfo2 = 15;
        private const uint SetHdrState = 16;

        private const int AdvancedColorModeHdr = 2;
        private const uint GetSourceName = 1;

        private const int FirstBuildWithHdrState = 26100;

        private const uint AdvancedColorSupported = 0x1;
        private const uint AdvancedColorEnabled = 0x2;
        private const uint WideColorEnforced = 0x4;
        private const uint AdvancedColorForceDisabled = 0x8;
        private const uint HighDynamicRangeSupported = 0x10;

        private static readonly bool UseHdrState = WindowsBuild() >= FirstBuildWithHdrState;

        public static List<MonitorInfo> GetActiveMonitors()
        {
            var monitors = new List<MonitorInfo>();
            var seen = new HashSet<string>();
            using (CcdConfig config = CcdConfig.Query())
            {
                for (int i = 0; i < config.PathCount; i++)
                {
                    uint adapterLow = config.TargetAdapterLow(i);
                    int adapterHigh = config.TargetAdapterHigh(i);
                    uint targetId = config.TargetId(i);
                    if (!seen.Add(adapterLow + ":" + adapterHigh + ":" + targetId))
                        continue;

                    var monitor = new MonitorInfo { AdapterLow = adapterLow, AdapterHigh = adapterHigh, TargetId = targetId };
                    ReadTargetName(monitor);
                    ReadHdr(monitor);
                    monitor.RefreshNumerator = config.RefreshNumerator(i);
                    monitor.RefreshDenominator = config.RefreshDenominator(i);

                    uint source = config.SourceModeIndex(i);
                    if (config.IsSourceMode(source))
                    {
                        PixelSize desktop = config.SourceSize(source);
                        monitor.Width = desktop.Width;
                        monitor.Height = desktop.Height;
                        monitor.X = config.SourceLeft(source);
                        monitor.Y = config.SourceTop(source);
                        monitor.IsPrimary = monitor.X == 0 && monitor.Y == 0;
                    }
                    uint target = config.TargetModeIndex(i);
                    if (config.IsTargetMode(target))
                    {
                        PixelSize signal = config.TargetSize(target);
                        monitor.SignalWidth = signal.Width;
                        monitor.SignalHeight = signal.Height;
                    }
                    if (monitor.SignalWidth <= 0)
                    {
                        monitor.SignalWidth = monitor.Width;
                        monitor.SignalHeight = monitor.Height;
                    }
                    monitor.GdiName = ReadSourceName(config.SourceAdapterLow(i), config.SourceAdapterHigh(i), config.SourceId(i));
                    monitor.AdapterName = AdapterNameOf(monitor.GdiName);
                    monitors.Add(monitor);
                }
            }
            return monitors;
        }

        public static void SetHdr(MonitorInfo monitor, bool on)
        {
            int error = SetState(monitor, UseHdrState ? SetHdrState : SetAdvancedColorState, on);
            if (error != 0)
                throw new Win32Exception(error);
        }

        private static int WindowsBuild()
        {
            try
            {
                var info = new NativeMethods.OSVERSIONINFOW { dwOSVersionInfoSize = Marshal.SizeOf(typeof(NativeMethods.OSVERSIONINFOW)) };
                if (NativeMethods.RtlGetVersion(ref info) == 0)
                {
                    if (info.dwMajorVersion > 10)
                        return int.MaxValue;
                    return info.dwMajorVersion == 10 ? info.dwBuildNumber : 0;
                }
            }
            catch (Exception)
            {
            }
            return Environment.OSVersion.Version.Major >= 10 ? Environment.OSVersion.Version.Build : 0;
        }

        private static string ReadSourceName(uint adapterLow, int adapterHigh, uint sourceId)
        {
            const int size = 84;
            IntPtr packet = Marshal.AllocHGlobal(size);
            try
            {
                for (int i = 0; i < size; i++)
                    Marshal.WriteByte(packet, i, 0);
                Marshal.WriteInt32(packet, 0, (int)GetSourceName);
                Marshal.WriteInt32(packet, 4, size);
                Marshal.WriteInt32(packet, 8, (int)adapterLow);
                Marshal.WriteInt32(packet, 12, adapterHigh);
                Marshal.WriteInt32(packet, 16, (int)sourceId);
                return NativeMethods.DisplayConfigGetDeviceInfo(packet) == 0 ? ReadString(packet, HeaderSize, 32) : string.Empty;
            }
            finally
            {
                Marshal.FreeHGlobal(packet);
            }
        }

        private static string AdapterNameOf(string gdiName)
        {
            if (string.IsNullOrEmpty(gdiName))
                return string.Empty;
            var device = new NativeMethods.DISPLAY_DEVICEW { cb = Marshal.SizeOf(typeof(NativeMethods.DISPLAY_DEVICEW)) };
            for (uint i = 0; NativeMethods.EnumDisplayDevices(null, i, ref device, 0); i++)
            {
                if (string.Equals(device.DeviceName, gdiName, StringComparison.OrdinalIgnoreCase))
                    return device.DeviceString ?? string.Empty;
                device.cb = Marshal.SizeOf(typeof(NativeMethods.DISPLAY_DEVICEW));
            }
            return string.Empty;
        }

        private static void ReadTargetName(MonitorInfo monitor)
        {
            const int size = 420;
            IntPtr packet = NewPacket(GetTargetName, size, monitor);
            try
            {
                if (NativeMethods.DisplayConfigGetDeviceInfo(packet) != 0)
                    return;
                monitor.FriendlyName = ReadString(packet, 36, 64);
                monitor.DevicePath = ReadString(packet, 164, 128);
            }
            finally
            {
                Marshal.FreeHGlobal(packet);
            }
        }

        private static void ReadHdr(MonitorInfo monitor)
        {
            IntPtr packet = UseHdrState ? NewPacket(GetAdvancedColorInfo2, 36, monitor) : NewPacket(GetAdvancedColorInfo, 32, monitor);
            try
            {
                if (NativeMethods.DisplayConfigGetDeviceInfo(packet) != 0)
                    return;
                uint flags = (uint)Marshal.ReadInt32(packet, HeaderSize);
                monitor.BitsPerColor = Marshal.ReadInt32(packet, 28);
                if (UseHdrState)
                {
                    monitor.HdrOn = Marshal.ReadInt32(packet, 32) == AdvancedColorModeHdr;
                    monitor.HdrSupported = (flags & HighDynamicRangeSupported) != 0 || monitor.HdrOn;
                }
                else
                {
                    bool usable = (flags & (WideColorEnforced | AdvancedColorForceDisabled)) == 0;
                    monitor.HdrSupported = usable && (flags & AdvancedColorSupported) != 0;
                    monitor.HdrOn = usable && (flags & AdvancedColorEnabled) != 0;
                }
            }
            finally
            {
                Marshal.FreeHGlobal(packet);
            }
        }

        private static int SetState(MonitorInfo monitor, uint type, bool on)
        {
            IntPtr packet = NewPacket(type, 24, monitor);
            try
            {
                Marshal.WriteInt32(packet, HeaderSize, on ? 1 : 0);
                return NativeMethods.DisplayConfigSetDeviceInfo(packet);
            }
            finally
            {
                Marshal.FreeHGlobal(packet);
            }
        }

        private static IntPtr NewPacket(uint type, int size, MonitorInfo monitor)
        {
            IntPtr packet = Marshal.AllocHGlobal(size);
            for (int i = 0; i < size; i++)
                Marshal.WriteByte(packet, i, 0);
            Marshal.WriteInt32(packet, 0, (int)type);
            Marshal.WriteInt32(packet, 4, size);
            Marshal.WriteInt32(packet, 8, (int)monitor.AdapterLow);
            Marshal.WriteInt32(packet, 12, monitor.AdapterHigh);
            Marshal.WriteInt32(packet, 16, (int)monitor.TargetId);
            return packet;
        }

        private static string ReadString(IntPtr packet, int offset, int maxChars)
        {
            var sb = new StringBuilder(maxChars);
            for (int i = 0; i < maxChars; i++)
            {
                char c = (char)Marshal.ReadInt16(packet, offset + i * 2);
                if (c == 0)
                    break;
                sb.Append(c);
            }
            return sb.ToString();
        }
    }
}
