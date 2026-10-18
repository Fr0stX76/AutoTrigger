using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using AutoTrigger.Core.Displays;
using AutoTrigger.Core.Logging;

namespace AutoTrigger.Core.Platform
{
    public static class NvApi
    {
        private const uint IdInitialize = 0x0150E828;
        private const uint IdColorControl = 0x92F9D80D;
        private const uint IdGetDisplayIdByDisplayName = 0xAE457190;

        private const byte CmdGet = 1;
        private const byte CmdSet = 2;
        private const byte CmdIsSupported = 3;

        private const byte ColorimetryRgb = 0;
        private const byte ColorimetryYcc709 = 2;
        private const byte ColorimetryAuto = 0xFF;
        private const int PolicyUser = 0;

        private static readonly object Lock = new object();
        private static bool _tried;
        private static bool _ready;
        private static ColorControlFn _colorControl;
        private static GetDisplayIdFn _getDisplayId;
        private static int _version = 5;

        [DllImport("nvapi64.dll", EntryPoint = "nvapi_QueryInterface", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr QueryInterface(uint id);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int InitializeFn();

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int ColorControlFn(uint displayId, IntPtr colorData);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        private delegate int GetDisplayIdFn([MarshalAs(UnmanagedType.LPStr)] string displayName, out uint displayId);

        public static bool Available
        {
            get
            {
                lock (Lock)
                {
                    if (_tried)
                        return _ready;
                    _tried = true;
                    try
                    {
                        IntPtr init = QueryInterface(IdInitialize);
                        IntPtr color = QueryInterface(IdColorControl);
                        IntPtr byName = QueryInterface(IdGetDisplayIdByDisplayName);
                        if (init == IntPtr.Zero || color == IntPtr.Zero || byName == IntPtr.Zero)
                            return false;
                        int status = ((InitializeFn)Marshal.GetDelegateForFunctionPointer(init, typeof(InitializeFn)))();
                        if (status != 0)
                        {
                            Log.Detailed($"NVIDIA API not ready (status {status})");
                            return false;
                        }
                        _colorControl = (ColorControlFn)Marshal.GetDelegateForFunctionPointer(color, typeof(ColorControlFn));
                        _getDisplayId = (GetDisplayIdFn)Marshal.GetDelegateForFunctionPointer(byName, typeof(GetDisplayIdFn));
                        _ready = true;
                    }
                    catch (Exception ex) when (ex is DllNotFoundException || ex is EntryPointNotFoundException || ex is BadImageFormatException)
                    {
                        Log.Detailed("No NVIDIA driver API: " + ex.Message);
                    }
                    return _ready;
                }
            }
        }

        public static bool TryGetDisplayId(string gdiName, out uint displayId)
        {
            displayId = 0;
            if (string.IsNullOrEmpty(gdiName) || !Available)
                return false;
            lock (Lock)
                return _getDisplayId(gdiName, out displayId) == 0 && displayId != 0;
        }

        public static ColorSetting GetColor(uint displayId)
        {
            byte[] data = Call(displayId, CmdGet, null, ColorimetryAuto, out int status);
            if (status != 0)
                return null;
            int bpc = BpcFromNv(BitConverter.ToInt32(data, 12));
            string format = FormatFromNv(data[8]);
            string range = data[10] == 1 ? ColorSetting.Limited : ColorSetting.Full;
            return bpc > 0 && format != null ? new ColorSetting(bpc, format, range) : null;
        }

        public static bool IsSupported(uint displayId, ColorSetting color)
        {
            Call(displayId, CmdIsSupported, color, ColorimetryAuto, out int status);
            if (status == 0)
                return true;
            Call(displayId, CmdIsSupported, color, ExplicitColorimetry(color), out status);
            return status == 0;
        }

        public static void SetColor(uint displayId, ColorSetting color)
        {
            Call(displayId, CmdSet, color, ColorimetryAuto, out int status);
            if (status != 0)
                Call(displayId, CmdSet, color, ExplicitColorimetry(color), out status);
            if (status != 0)
                throw new InvalidOperationException($"NVIDIA driver status {status}");
        }

        public static List<ColorSetting> SupportedColors(uint displayId)
        {
            var colors = new List<ColorSetting>();
            foreach (string format in ColorSetting.Formats)
            {
                foreach (int bpc in new[] { 6, 8, 10, 12 })
                {
                    foreach (string range in ColorSetting.Ranges)
                    {
                        var color = new ColorSetting(bpc, format, range);
                        if (IsSupported(displayId, color))
                            colors.Add(color);
                    }
                }
            }
            return colors;
        }

        private static byte[] Call(uint displayId, byte command, ColorSetting color, byte colorimetry, out int status)
        {
            status = -1;
            if (!Available)
                return new byte[24];
            lock (Lock)
            {
                for (int attempt = 0; attempt < 2; attempt++)
                {
                    int size = _version == 5 ? 24 : 20;
                    IntPtr buffer = Marshal.AllocHGlobal(24);
                    try
                    {
                        for (int i = 0; i < 24; i++)
                            Marshal.WriteByte(buffer, i, 0);
                        Marshal.WriteInt32(buffer, 0, size | (_version << 16));
                        Marshal.WriteInt16(buffer, 4, (short)size);
                        Marshal.WriteByte(buffer, 6, command);
                        if (color != null)
                        {
                            Marshal.WriteByte(buffer, 8, FormatToNv(color.Format));
                            Marshal.WriteByte(buffer, 9, colorimetry);
                            Marshal.WriteByte(buffer, 10, color.Range == ColorSetting.Limited ? (byte)1 : (byte)0);
                            Marshal.WriteInt32(buffer, 12, BpcToNv(color.Bpc));
                            Marshal.WriteInt32(buffer, 16, PolicyUser);
                        }
                        status = _colorControl(displayId, buffer);
                        var data = new byte[24];
                        Marshal.Copy(buffer, data, 0, 24);
                        if (status == -9 && _version == 5)
                        {
                            _version = 4;
                            continue;
                        }
                        return data;
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(buffer);
                    }
                }
                return new byte[24];
            }
        }

        private static byte ExplicitColorimetry(ColorSetting color) =>
            color.Format == ColorSetting.Rgb ? ColorimetryRgb : ColorimetryYcc709;

        private static byte FormatToNv(string format)
        {
            switch (format)
            {
                case ColorSetting.YCbCr422: return 1;
                case ColorSetting.YCbCr444: return 2;
                case ColorSetting.YCbCr420: return 3;
                default: return 0;
            }
        }

        private static string FormatFromNv(byte value)
        {
            switch (value)
            {
                case 0: return ColorSetting.Rgb;
                case 1: return ColorSetting.YCbCr422;
                case 2: return ColorSetting.YCbCr444;
                case 3: return ColorSetting.YCbCr420;
                default: return null;
            }
        }

        private static int BpcToNv(int bpc)
        {
            switch (bpc)
            {
                case 6: return 1;
                case 8: return 2;
                case 10: return 3;
                case 12: return 4;
                case 16: return 5;
                default: return 0;
            }
        }

        private static int BpcFromNv(int value)
        {
            switch (value)
            {
                case 1: return 6;
                case 2: return 8;
                case 3: return 10;
                case 4: return 12;
                case 5: return 16;
                default: return 0;
            }
        }
    }
}
