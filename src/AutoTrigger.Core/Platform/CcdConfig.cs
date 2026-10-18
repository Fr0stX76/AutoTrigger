using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using AutoTrigger.Core.Displays;

namespace AutoTrigger.Core.Platform
{
    internal sealed class CcdConfig : IDisposable
    {
        public const int PathSize = 72;
        public const int ModeSize = 64;
        public const uint InvalidIndex = 0xFFFFFFFF;

        public const int ScalingIdentity = 1;
        public const int ScalingAspectRatioCenteredMax = 4;
        public const int ScalingPreferred = 128;

        private const int ModeInfoTypeSource = 1;
        private const int ModeInfoTypeTarget = 2;

        private const int PathSourceAdapterLow = 0;
        private const int PathSourceAdapterHigh = 4;
        private const int PathSourceId = 8;
        private const int PathSourceMode = 12;
        private const int PathTargetAdapterLow = 20;
        private const int PathTargetAdapterHigh = 24;
        private const int PathTargetId = 28;
        private const int PathTargetMode = 32;
        private const int PathScaling = 44;
        private const int PathRefreshNumerator = 48;
        private const int PathRefreshDenominator = 52;

        private const int ModeType = 0;
        private const int SourceWidth = 16;
        private const int SourceHeight = 20;
        private const int SourceX = 28;
        private const int SourceY = 32;
        private const int TargetActiveWidth = 40;
        private const int TargetActiveHeight = 44;

        private IntPtr _paths;
        private IntPtr _modes;
        private uint _pathCount;
        private uint _modeCount;

        private CcdConfig()
        {
        }

        public int PathCount => (int)_pathCount;

        public uint ModeCount => _modeCount;

        public static CcdConfig Query()
        {
            for (int attempt = 0; ; attempt++)
            {
                int error = NativeMethods.GetDisplayConfigBufferSizes(NativeMethods.QDC_ONLY_ACTIVE_PATHS, out uint pathCount, out uint modeCount);
                if (error != 0)
                    throw new Win32Exception(error);
                var config = new CcdConfig
                {
                    _paths = Marshal.AllocHGlobal((int)Math.Max(1, pathCount) * PathSize),
                    _modes = Marshal.AllocHGlobal((int)Math.Max(1, modeCount) * ModeSize),
                    _pathCount = pathCount,
                    _modeCount = modeCount,
                };
                error = NativeMethods.QueryDisplayConfig(NativeMethods.QDC_ONLY_ACTIVE_PATHS, ref config._pathCount, config._paths,
                    ref config._modeCount, config._modes, IntPtr.Zero);
                if (error == 0)
                    return config;
                config.Dispose();
                if (error != NativeMethods.ERROR_INSUFFICIENT_BUFFER || attempt >= 3)
                    throw new Win32Exception(error);
            }
        }

        internal static CcdConfig FromBytes(byte[] paths, int pathCount, byte[] modes, int modeCount)
        {
            var config = new CcdConfig
            {
                _paths = Marshal.AllocHGlobal(Math.Max(1, pathCount) * PathSize),
                _modes = Marshal.AllocHGlobal(Math.Max(1, modeCount) * ModeSize),
                _pathCount = (uint)pathCount,
                _modeCount = (uint)modeCount,
            };
            Marshal.Copy(paths, 0, config._paths, pathCount * PathSize);
            Marshal.Copy(modes, 0, config._modes, modeCount * ModeSize);
            return config;
        }

        private IntPtr Path(int index) => _paths + index * PathSize;

        private IntPtr Mode(uint index) => _modes + (int)index * ModeSize;

        public uint TargetAdapterLow(int path) => (uint)Marshal.ReadInt32(Path(path), PathTargetAdapterLow);

        public int TargetAdapterHigh(int path) => Marshal.ReadInt32(Path(path), PathTargetAdapterHigh);

        public uint TargetId(int path) => (uint)Marshal.ReadInt32(Path(path), PathTargetId);

        public uint SourceAdapterLow(int path) => (uint)Marshal.ReadInt32(Path(path), PathSourceAdapterLow);

        public int SourceAdapterHigh(int path) => Marshal.ReadInt32(Path(path), PathSourceAdapterHigh);

        public uint SourceId(int path) => (uint)Marshal.ReadInt32(Path(path), PathSourceId);

        public uint RefreshNumerator(int path) => (uint)Marshal.ReadInt32(Path(path), PathRefreshNumerator);

        public uint RefreshDenominator(int path) => (uint)Marshal.ReadInt32(Path(path), PathRefreshDenominator);

        public uint SourceModeIndex(int path) => (uint)Marshal.ReadInt32(Path(path), PathSourceMode);

        public uint TargetModeIndex(int path) => (uint)Marshal.ReadInt32(Path(path), PathTargetMode);

        public bool IsSourceMode(uint index) => index < _modeCount && Marshal.ReadInt32(Mode(index), ModeType) == ModeInfoTypeSource;

        public bool IsTargetMode(uint index) => index < _modeCount && Marshal.ReadInt32(Mode(index), ModeType) == ModeInfoTypeTarget;

        public PixelSize SourceSize(uint source) =>
            new PixelSize(Marshal.ReadInt32(Mode(source), SourceWidth), Marshal.ReadInt32(Mode(source), SourceHeight));

        public int SourceLeft(uint source) => Marshal.ReadInt32(Mode(source), SourceX);

        public int SourceTop(uint source) => Marshal.ReadInt32(Mode(source), SourceY);

        public PixelSize TargetSize(uint target) =>
            new PixelSize(Marshal.ReadInt32(Mode(target), TargetActiveWidth), Marshal.ReadInt32(Mode(target), TargetActiveHeight));

        public int FindPath(MonitorInfo monitor)
        {
            for (int i = 0; i < _pathCount; i++)
            {
                if (TargetAdapterLow(i) == monitor.AdapterLow && TargetAdapterHigh(i) == monitor.AdapterHigh && TargetId(i) == monitor.TargetId)
                    return i;
            }
            throw new InvalidOperationException($"{monitor.Name} is not connected");
        }

        public uint SourceModeOf(int path)
        {
            uint index = SourceModeIndex(path);
            if (!IsSourceMode(index))
                throw new InvalidOperationException("the desktop of this display cannot be read");
            return index;
        }

        public uint TargetModeOf(int path)
        {
            uint index = TargetModeIndex(path);
            return IsTargetMode(index) ? index : InvalidIndex;
        }

        public string Layout()
        {
            var parts = new List<string>();
            for (uint i = 0; i < _modeCount; i++)
            {
                if (!IsSourceMode(i))
                    continue;
                PixelSize size = SourceSize(i);
                parts.Add($"({SourceLeft(i)}, {SourceTop(i)}) {size.Width} × {size.Height}");
            }
            return string.Join("; ", parts);
        }

        public void ResizeSource(uint source, PixelSize size)
        {
            IntPtr mode = Mode(source);
            int oldWidth = Marshal.ReadInt32(mode, SourceWidth);
            int oldHeight = Marshal.ReadInt32(mode, SourceHeight);
            int x = Marshal.ReadInt32(mode, SourceX);
            int y = Marshal.ReadInt32(mode, SourceY);
            int dx = size.Width - oldWidth;
            int dy = size.Height - oldHeight;
            bool keepRight = x < 0;
            bool keepBottom = y < 0;
            Marshal.WriteInt32(mode, SourceWidth, size.Width);
            Marshal.WriteInt32(mode, SourceHeight, size.Height);
            if (keepRight)
                Marshal.WriteInt32(mode, SourceX, x - dx);
            if (keepBottom)
                Marshal.WriteInt32(mode, SourceY, y - dy);
            for (uint i = 0; i < _modeCount; i++)
            {
                if (i == source || !IsSourceMode(i))
                    continue;
                IntPtr other = Mode(i);
                int ox = Marshal.ReadInt32(other, SourceX);
                int oy = Marshal.ReadInt32(other, SourceY);
                int ow = Marshal.ReadInt32(other, SourceWidth);
                int oh = Marshal.ReadInt32(other, SourceHeight);
                if (dx != 0)
                {
                    if (!keepRight && ox >= x + oldWidth)
                        Marshal.WriteInt32(other, SourceX, ox + dx);
                    else if (keepRight && ox + ow <= x)
                        Marshal.WriteInt32(other, SourceX, ox - dx);
                }
                if (dy != 0)
                {
                    if (!keepBottom && oy >= y + oldHeight)
                        Marshal.WriteInt32(other, SourceY, oy + dy);
                    else if (keepBottom && oy + oh <= y)
                        Marshal.WriteInt32(other, SourceY, oy - dy);
                }
            }
        }

        public void SetScaling(int path, int scaling) => Marshal.WriteInt32(Path(path), PathScaling, scaling);

        public void SetRefresh(int path, RefreshRate rate)
        {
            Marshal.WriteInt32(Path(path), PathRefreshNumerator, (int)rate.Numerator);
            Marshal.WriteInt32(Path(path), PathRefreshDenominator, (int)rate.Denominator);
        }

        public void ReleaseTargetMode(int path)
        {
            uint target = TargetModeIndex(path);
            Marshal.WriteInt32(Path(path), PathTargetMode, unchecked((int)InvalidIndex));
            if (target >= _modeCount)
                return;
            for (int i = 0; i < _pathCount; i++)
            {
                if (SourceModeIndex(i) == target || TargetModeIndex(i) == target)
                    return;
            }
            for (uint i = target; i + 1 < _modeCount; i++)
            {
                for (int b = 0; b < ModeSize; b++)
                    Marshal.WriteByte(Mode(i), b, Marshal.ReadByte(Mode(i + 1), b));
            }
            _modeCount--;
            for (int i = 0; i < _pathCount; i++)
            {
                foreach (int offset in new[] { PathSourceMode, PathTargetMode })
                {
                    uint index = (uint)Marshal.ReadInt32(Path(i), offset);
                    if (index != InvalidIndex && index > target)
                        Marshal.WriteInt32(Path(i), offset, (int)(index - 1));
                }
            }
        }

        public int Apply(uint flags) => NativeMethods.SetDisplayConfig(_pathCount, _paths, _modeCount, _modes, flags);

        public void Dispose()
        {
            if (_paths != IntPtr.Zero)
                Marshal.FreeHGlobal(_paths);
            if (_modes != IntPtr.Zero)
                Marshal.FreeHGlobal(_modes);
            _paths = IntPtr.Zero;
            _modes = IntPtr.Zero;
        }
    }
}
