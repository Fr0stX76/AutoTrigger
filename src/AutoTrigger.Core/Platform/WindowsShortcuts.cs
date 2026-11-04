using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using AutoTrigger.Core.Model;

namespace AutoTrigger.Core.Platform
{
    // Shortcut files are written by the shell's ShellLink object, which must be used from a single-threaded apartment.
    public static class WindowsShortcuts
    {
        private const int TextSize = 1024;

        // Every name taken by other shortcuts is an outcome the window explains, not an error: it gives null.
        public static string Save(DesktopShortcut shortcut, string folder)
        {
            string path = PathFor(shortcut, folder);
            if (path == null)
                return null;
            var link = (IShellLinkW)new ShellLink();
            try
            {
                link.SetPath(shortcut.Target);
                link.SetArguments(shortcut.Arguments);
                link.SetWorkingDirectory(shortcut.WorkingDirectory);
                link.SetIconLocation(shortcut.IconPath, 0);
                ((IPersistFile)link).Save(path, true);
            }
            finally
            {
                Marshal.ReleaseComObject(link);
            }
            return path;
        }

        // The first free name, or the first one that already holds this same shortcut, which is then replaced.
        private static string PathFor(DesktopShortcut shortcut, string folder)
        {
            foreach (string name in shortcut.FileNames())
            {
                string path = Path.Combine(folder, name);
                if (!File.Exists(path) || HoldsSame(path, shortcut))
                    return path;
            }
            return null;
        }

        private static bool HoldsSame(string path, DesktopShortcut shortcut)
        {
            var link = (IShellLinkW)new ShellLink();
            try
            {
                ((IPersistFile)link).Load(path, 0);
                var target = new StringBuilder(TextSize);
                link.GetPath(target, target.Capacity, IntPtr.Zero, 0);
                var arguments = new StringBuilder(TextSize);
                link.GetArguments(arguments, arguments.Capacity);
                return shortcut.IsSameAs(target.ToString(), arguments.ToString());
            }
            catch (COMException)
            {
                // A file that cannot be read as a shortcut is someone else's: it is kept.
                return false;
            }
            finally
            {
                Marshal.ReleaseComObject(link);
            }
        }

        [ComImport]
        [Guid("00021401-0000-0000-C000-000000000046")]
        private class ShellLink
        {
        }

        [ComImport]
        [Guid("000214F9-0000-0000-C000-000000000046")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellLinkW
        {
            void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder file, int size, IntPtr findData, uint flags);

            void GetIDList(out IntPtr idList);

            void SetIDList(IntPtr idList);

            void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder name, int size);

            void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);

            void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder folder, int size);

            void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string folder);

            void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder arguments, int size);

            void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string arguments);

            void GetHotkey(out short hotkey);

            void SetHotkey(short hotkey);

            void GetShowCmd(out int showCommand);

            void SetShowCmd(int showCommand);

            void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder iconPath, int size, out int index);

            void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string iconPath, int index);

            void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string relativePath, uint reserved);

            void Resolve(IntPtr window, uint flags);

            void SetPath([MarshalAs(UnmanagedType.LPWStr)] string file);
        }
    }
}
