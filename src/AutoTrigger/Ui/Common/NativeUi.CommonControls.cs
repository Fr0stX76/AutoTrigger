using System;
using System.Runtime.InteropServices;

namespace AutoTrigger.Ui
{
    internal static partial class NativeUi
    {
        // comctl32 version 6 comes from WinSxS through the manifest, not from System32, so the System32-only search does not apply.
        [DllImport("comctl32.dll")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.LegacyBehavior)]
        public static extern int LoadIconWithScaleDown(IntPtr instance, IntPtr name, int cx, int cy, out IntPtr icon);

        [DllImport("comctl32.dll")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.LegacyBehavior)]
        public static extern IntPtr ImageList_GetIcon(IntPtr imageList, int index, uint flags);
    }
}
