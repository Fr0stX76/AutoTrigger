using System;
using System.IO;
using System.Security;
using Microsoft.Win32;

namespace AutoTrigger.Ui
{
    internal static class WindowsTheme
    {
        private const string PersonalizeKey = @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

        // "Choose your default app mode" in Windows settings.
        public static bool AppsUseLight => ReadFlag("AppsUseLightTheme", true);

        // "Choose your default Windows mode": the taskbar, Start and the tray follow it, not the app mode.
        public static bool TaskbarIsLight => ReadFlag("SystemUsesLightTheme", false);

        private static bool ReadFlag(string name, bool fallback)
        {
            try
            {
                return Registry.GetValue(PersonalizeKey, name, null) is int flag ? flag != 0 : fallback;
            }
            catch (Exception ex) when (ex is SecurityException || ex is IOException || ex is UnauthorizedAccessException)
            {
                return fallback;
            }
        }
    }
}
