using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using AutoTrigger.Core.Engine;

namespace AutoTrigger.Core.Platform
{
    public sealed class WindowsAudioHardware : IAudioHardware
    {
        private const int DataFlowRender = 0;
        private const int DataFlowCapture = 1;
        private const int RoleConsole = 0;
        private const int RoleMultimedia = 1;
        private const uint DeviceStateActive = 0x1;
        private const uint StgmRead = 0;
        private const ushort VtLpwstr = 31;

        private static readonly PropertyKey FriendlyName = new PropertyKey(new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), 14);

        public List<AudioDevice> GetDevices(bool playback)
        {
            var devices = new List<AudioDevice>();
            var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
            try
            {
                Check(enumerator.EnumAudioEndpoints(DataFlow(playback), DeviceStateActive, out IMMDeviceCollection collection));
                try
                {
                    Check(collection.GetCount(out uint count));
                    for (uint i = 0; i < count; i++)
                    {
                        if (collection.Item(i, out IMMDevice device) != 0 || device == null)
                            continue;
                        try
                        {
                            if (device.GetId(out string id) != 0 || string.IsNullOrEmpty(id))
                                continue;
                            devices.Add(new AudioDevice { Id = id, Name = NameOf(device) ?? id, Playback = playback });
                        }
                        finally
                        {
                            Marshal.ReleaseComObject(device);
                        }
                    }
                }
                finally
                {
                    Marshal.ReleaseComObject(collection);
                }
            }
            finally
            {
                Marshal.ReleaseComObject(enumerator);
            }
            return devices;
        }

        public string GetDefaultId(bool playback)
        {
            var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
            try
            {
                if (enumerator.GetDefaultAudioEndpoint(DataFlow(playback), RoleConsole, out IMMDevice device) != 0 || device == null)
                    return null;
                try
                {
                    return device.GetId(out string id) == 0 ? id : null;
                }
                finally
                {
                    Marshal.ReleaseComObject(device);
                }
            }
            finally
            {
                Marshal.ReleaseComObject(enumerator);
            }
        }

        public void SetDefault(AudioDevice device)
        {
            var policy = (IPolicyConfig)new PolicyConfigClient();
            try
            {
                Check(policy.SetDefaultEndpoint(device.Id, RoleConsole));
                Check(policy.SetDefaultEndpoint(device.Id, RoleMultimedia));
            }
            finally
            {
                Marshal.ReleaseComObject(policy);
            }
        }

        private static int DataFlow(bool playback) => playback ? DataFlowRender : DataFlowCapture;

        private static string NameOf(IMMDevice device)
        {
            if (device.OpenPropertyStore(StgmRead, out IPropertyStore store) != 0 || store == null)
                return null;
            try
            {
                PropertyKey key = FriendlyName;
                var value = new PropVariant();
                if (store.GetValue(ref key, out value) != 0)
                    return null;
                try
                {
                    return value.Type == VtLpwstr ? Marshal.PtrToStringUni(value.Pointer) : null;
                }
                finally
                {
                    _ = PropVariantClear(ref value);
                }
            }
            finally
            {
                Marshal.ReleaseComObject(store);
            }
        }

        private static void Check(int hr)
        {
            if (hr < 0)
                Marshal.ThrowExceptionForHR(hr);
        }

        [DllImport("ole32.dll")]
        private static extern int PropVariantClear(ref PropVariant value);

        [StructLayout(LayoutKind.Sequential)]
        private struct PropertyKey
        {
            public Guid FormatId;
            public int PropertyId;

            public PropertyKey(Guid formatId, int propertyId)
            {
                FormatId = formatId;
                PropertyId = propertyId;
            }
        }

        [StructLayout(LayoutKind.Explicit, Size = 24)]
        private struct PropVariant
        {
            [FieldOffset(0)]
            public ushort Type;

            [FieldOffset(8)]
            public IntPtr Pointer;
        }

        [ComImport]
        [Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
        private class MMDeviceEnumerator
        {
        }

        [ComImport]
        [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDeviceEnumerator
        {
            [PreserveSig]
            int EnumAudioEndpoints(int dataFlow, uint stateMask, out IMMDeviceCollection devices);

            [PreserveSig]
            int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice endpoint);

            [PreserveSig]
            int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);

            [PreserveSig]
            int RegisterEndpointNotificationCallback(IntPtr client);

            [PreserveSig]
            int UnregisterEndpointNotificationCallback(IntPtr client);
        }

        [ComImport]
        [Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDeviceCollection
        {
            [PreserveSig]
            int GetCount(out uint count);

            [PreserveSig]
            int Item(uint index, out IMMDevice device);
        }

        [ComImport]
        [Guid("D666063F-1587-4E43-81F1-B948E807363F")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDevice
        {
            [PreserveSig]
            int Activate(ref Guid iid, uint clsCtx, IntPtr activationParams, out IntPtr instance);

            [PreserveSig]
            int OpenPropertyStore(uint access, out IPropertyStore properties);

            [PreserveSig]
            int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);

            [PreserveSig]
            int GetState(out uint state);
        }

        [ComImport]
        [Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IPropertyStore
        {
            [PreserveSig]
            int GetCount(out uint count);

            [PreserveSig]
            int GetAt(uint index, out PropertyKey key);

            [PreserveSig]
            int GetValue(ref PropertyKey key, out PropVariant value);

            [PreserveSig]
            int SetValue(ref PropertyKey key, ref PropVariant value);

            [PreserveSig]
            int Commit();
        }

        [ComImport]
        [Guid("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9")]
        private class PolicyConfigClient
        {
        }

        [ComImport]
        [Guid("F8679F50-850A-41CF-9C72-430F290290C8")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IPolicyConfig
        {
            [PreserveSig]
            int GetMixFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceId, IntPtr format);

            [PreserveSig]
            int GetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceId, int defaultFormat, IntPtr format);

            [PreserveSig]
            int ResetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceId);

            [PreserveSig]
            int SetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceId, IntPtr endpointFormat, IntPtr mixFormat);

            [PreserveSig]
            int GetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string deviceId, int defaultPeriod, IntPtr defaultValue, IntPtr minimum);

            [PreserveSig]
            int SetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string deviceId, IntPtr period);

            [PreserveSig]
            int GetShareMode([MarshalAs(UnmanagedType.LPWStr)] string deviceId, IntPtr mode);

            [PreserveSig]
            int SetShareMode([MarshalAs(UnmanagedType.LPWStr)] string deviceId, IntPtr mode);

            [PreserveSig]
            int GetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string deviceId, IntPtr key, IntPtr value);

            [PreserveSig]
            int SetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string deviceId, IntPtr key, IntPtr value);

            [PreserveSig]
            int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string deviceId, int role);

            [PreserveSig]
            int SetEndpointVisibility([MarshalAs(UnmanagedType.LPWStr)] string deviceId, int visible);
        }
    }
}
