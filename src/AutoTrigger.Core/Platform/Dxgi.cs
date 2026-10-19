using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using AutoTrigger.Core.Displays;

namespace AutoTrigger.Core.Platform
{
    internal static class Dxgi
    {
        private const int DxgiErrorNotFound = unchecked((int)0x887A0002);
        private const int DxgiErrorMoreData = unchecked((int)0x887A0003);
        private const int FormatR8G8B8A8Unorm = 28;
        private const int FormatB8G8R8A8Unorm = 87;
        private const int OutputDescSize = 96;
        private const int ModeDescSize = 28;

        private const int SlotRelease = 2;
        private const int SlotFactoryEnumAdapters1 = 12;
        private const int SlotAdapterEnumOutputs = 7;
        private const int SlotOutputGetDesc = 7;
        private const int SlotOutputGetDisplayModeList = 8;

        private static readonly Guid IidFactory1 = new Guid("770aae78-f26f-4dba-a829-253c83d1b387");

        [DllImport("dxgi.dll")]
        private static extern int CreateDXGIFactory1(ref Guid riid, out IntPtr factory);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate uint ReleaseFn(IntPtr self);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int EnumFn(IntPtr self, uint index, out IntPtr item);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int GetDescFn(IntPtr self, IntPtr desc);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int GetDisplayModeListFn(IntPtr self, int format, uint flags, ref uint count, IntPtr modes);

        public static List<RefreshRate> RefreshRates(string gdiName, PixelSize size)
        {
            var rates = new List<RefreshRate>();
            Guid iid = IidFactory1;
            int hr = CreateDXGIFactory1(ref iid, out IntPtr factory);
            if (hr < 0)
                throw new InvalidOperationException($"DXGI is not available (0x{hr:X8})");
            try
            {
                for (uint a = 0; ; a++)
                {
                    hr = Method<EnumFn>(factory, SlotFactoryEnumAdapters1)(factory, a, out IntPtr adapter);
                    if (hr == DxgiErrorNotFound)
                        break;
                    if (hr < 0)
                        throw new InvalidOperationException($"DXGI adapters cannot be read (0x{hr:X8})");
                    try
                    {
                        for (uint o = 0; ; o++)
                        {
                            hr = Method<EnumFn>(adapter, SlotAdapterEnumOutputs)(adapter, o, out IntPtr output);
                            if (hr == DxgiErrorNotFound)
                                break;
                            if (hr < 0)
                                break;
                            try
                            {
                                if (string.Equals(OutputName(output), gdiName, StringComparison.OrdinalIgnoreCase))
                                {
                                    ReadRates(output, size, rates);
                                    return rates;
                                }
                            }
                            finally
                            {
                                Release(output);
                            }
                        }
                    }
                    finally
                    {
                        Release(adapter);
                    }
                }
            }
            finally
            {
                Release(factory);
            }
            return rates;
        }

        private static string OutputName(IntPtr output)
        {
            IntPtr desc = Marshal.AllocHGlobal(OutputDescSize);
            try
            {
                for (int i = 0; i < OutputDescSize; i++)
                    Marshal.WriteByte(desc, i, 0);
                if (Method<GetDescFn>(output, SlotOutputGetDesc)(output, desc) < 0)
                    return string.Empty;
                return Marshal.PtrToStringUni(desc) ?? string.Empty;
            }
            finally
            {
                Marshal.FreeHGlobal(desc);
            }
        }

        private static void ReadRates(IntPtr output, PixelSize size, List<RefreshRate> rates)
        {
            foreach (int format in new[] { FormatR8G8B8A8Unorm, FormatB8G8R8A8Unorm })
            {
                GetDisplayModeListFn list = Method<GetDisplayModeListFn>(output, SlotOutputGetDisplayModeList);
                for (int attempt = 0; attempt < 3; attempt++)
                {
                    uint count = 0;
                    if (list(output, format, 0, ref count, IntPtr.Zero) < 0 || count == 0)
                        break;
                    IntPtr modes = Marshal.AllocHGlobal((int)count * ModeDescSize);
                    try
                    {
                        int hr = list(output, format, 0, ref count, modes);
                        if (hr == DxgiErrorMoreData)
                            continue;
                        if (hr < 0)
                            break;
                        for (int i = 0; i < count; i++)
                        {
                            IntPtr mode = modes + i * ModeDescSize;
                            if (Marshal.ReadInt32(mode, 0) != size.Width || Marshal.ReadInt32(mode, 4) != size.Height)
                                continue;
                            var rate = new RefreshRate((uint)Marshal.ReadInt32(mode, 8), (uint)Marshal.ReadInt32(mode, 12));
                            if (rate.IsSet && rate.Hz > 1 && !rates.Exists(r => r.SameAs(rate)))
                                rates.Add(rate);
                        }
                        break;
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(modes);
                    }
                }
                if (rates.Count > 0)
                    return;
            }
        }

        private static T Method<T>(IntPtr instance, int slot) where T : class
        {
            IntPtr table = Marshal.ReadIntPtr(instance);
            IntPtr function = Marshal.ReadIntPtr(table, slot * IntPtr.Size);
            return Marshal.GetDelegateForFunctionPointer<T>(function);
        }

        private static void Release(IntPtr instance)
        {
            if (instance != IntPtr.Zero)
                Method<ReleaseFn>(instance, SlotRelease)(instance);
        }
    }
}
