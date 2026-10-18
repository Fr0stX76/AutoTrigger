using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using AutoTrigger.Core.Engine;

namespace AutoTrigger.Core.Platform
{
    public sealed class WindowsPowerPlans : IPowerPlans
    {
        private const int MaxPlans = 256;

        public List<PowerPlan> GetPlans()
        {
            var plans = new List<PowerPlan>();
            for (uint index = 0; index < MaxPlans; index++)
            {
                uint size = 16;
                uint result = NativeMethods.PowerEnumerate(IntPtr.Zero, IntPtr.Zero, IntPtr.Zero,
                    NativeMethods.ACCESS_SCHEME, index, out Guid id, ref size);
                if (result == NativeMethods.ERROR_NO_MORE_ITEMS)
                    break;
                if (result != 0)
                    throw new Win32Exception((int)result);
                plans.Add(new PowerPlan { Id = id, Name = FriendlyName(id) });
            }
            return plans;
        }

        public Guid GetActive()
        {
            uint result = NativeMethods.PowerGetActiveScheme(IntPtr.Zero, out IntPtr active);
            if (result != 0)
                throw new Win32Exception((int)result);
            try
            {
                return Marshal.PtrToStructure<Guid>(active);
            }
            finally
            {
                NativeMethods.LocalFree(active);
            }
        }

        public void SetActive(Guid id)
        {
            uint result = NativeMethods.PowerSetActiveScheme(IntPtr.Zero, ref id);
            if (result != 0)
                throw new Win32Exception((int)result);
        }

        private static string FriendlyName(Guid id)
        {
            uint size = 0;
            if (NativeMethods.PowerReadFriendlyName(IntPtr.Zero, ref id, IntPtr.Zero, IntPtr.Zero, null, ref size) == 0 && size > 0)
            {
                var buffer = new byte[size];
                if (NativeMethods.PowerReadFriendlyName(IntPtr.Zero, ref id, IntPtr.Zero, IntPtr.Zero, buffer, ref size) == 0)
                {
                    string name = Encoding.Unicode.GetString(buffer, 0, (int)Math.Min(size, (uint)buffer.Length)).TrimEnd('\0').Trim();
                    if (name.Length > 0)
                        return name;
                }
            }
            return id.ToString("D");
        }
    }
}
