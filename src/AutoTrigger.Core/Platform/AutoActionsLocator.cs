using System;
using System.Diagnostics;
using System.IO;
using System.Security;
using AutoTrigger.Core.Engine;
using AutoTrigger.Core.Model;
using Microsoft.Win32;

namespace AutoTrigger.Core.Platform
{
    public static class AutoActionsLocator
    {
        private const string ProcessName = "AutoActions";
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

        public static bool IsRunning()
        {
            Process[] found = Process.GetProcessesByName(ProcessName);
            foreach (Process p in found)
                p.Dispose();
            return found.Length > 0;
        }

        public static string FindSettingsFile(IProcessSystem processes)
        {
            foreach (Process p in Process.GetProcessesByName(ProcessName))
            {
                using (p)
                {
                    try
                    {
                        string file = FileNear(processes.Query(p.Id)?.Path);
                        if (file != null)
                            return file;
                    }
                    catch (Exception ex) when (ex is InvalidOperationException || ex is System.ComponentModel.Win32Exception)
                    {
                    }
                }
            }
            foreach (RegistryKey hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
            {
                try
                {
                    using (RegistryKey run = hive.OpenSubKey(RunKey, false))
                    {
                        if (run == null)
                            continue;
                        foreach (string name in run.GetValueNames())
                        {
                            if (name.IndexOf(ProcessName, StringComparison.OrdinalIgnoreCase) < 0)
                                continue;
                            string file = FileNear(StartupRegistration.ExeOf(run.GetValue(name) as string));
                            if (file != null)
                                return file;
                        }
                    }
                }
                catch (Exception ex) when (ex is SecurityException || ex is UnauthorizedAccessException || ex is IOException)
                {
                }
            }
            return null;
        }

        private static string FileNear(string exePath)
        {
            if (string.IsNullOrWhiteSpace(exePath))
                return null;
            try
            {
                string folder = Path.GetDirectoryName(Environment.ExpandEnvironmentVariables(exePath.Trim()));
                if (string.IsNullOrEmpty(folder))
                    return null;
                string file = Path.Combine(folder, AutoActionsImport.FileName);
                return File.Exists(file) ? file : null;
            }
            catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException)
            {
                return null;
            }
        }
    }
}
