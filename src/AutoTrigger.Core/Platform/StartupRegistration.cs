using System;
using AutoTrigger.Core.Engine;
using Microsoft.Win32;

namespace AutoTrigger.Core.Platform
{
    public static class StartupRegistration
    {
        public const string ValueName = "AutoTrigger";
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";

        public static bool IsEnabled(string exePath)
        {
            using (RegistryKey run = Registry.CurrentUser.OpenSubKey(RunKey, false))
            {
                if (!(run?.GetValue(ValueName) is string command) || !PathMatcher.SamePath(ExeOf(command), exePath))
                    return false;
            }
            using (RegistryKey approved = Registry.CurrentUser.OpenSubKey(ApprovedKey, false))
            {
                if (approved?.GetValue(ValueName) is byte[] state && state.Length > 0 && (state[0] & 1) == 1)
                    return false;
            }
            return true;
        }

        public static void SetEnabled(string exePath, bool enabled)
        {
            using (RegistryKey run = Registry.CurrentUser.CreateSubKey(RunKey))
            {
                if (enabled)
                    run.SetValue(ValueName, "\"" + exePath + "\"", RegistryValueKind.String);
                else if (run.GetValue(ValueName) != null)
                    run.DeleteValue(ValueName, false);
            }
            using (RegistryKey approved = Registry.CurrentUser.OpenSubKey(ApprovedKey, true))
            {
                if (approved?.GetValue(ValueName) != null)
                    approved.DeleteValue(ValueName, false);
            }
        }

        public static string ExeOf(string command)
        {
            string text = (command ?? string.Empty).Trim();
            if (text.StartsWith("\"", StringComparison.Ordinal))
            {
                int close = text.IndexOf('"', 1);
                return close < 0 ? text.Substring(1) : text.Substring(1, close - 1);
            }
            int exe = text.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            return exe < 0 ? text : text.Substring(0, exe + 4);
        }
    }
}
