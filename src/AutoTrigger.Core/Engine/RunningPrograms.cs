using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace AutoTrigger.Core.Engine
{
    public sealed class RunningProgram
    {
        public string Name { get; set; }

        public string Path { get; set; }
    }

    public static class RunningPrograms
    {
        public static List<RunningProgram> Read(IProcessSystem processes, int selfPid, string windowsFolder)
        {
            var running = new List<ProcessEntry>();
            processes.Snapshot(running);
            var withWindow = new HashSet<int>();
            processes.WindowOwners(withWindow);
            string windows = string.IsNullOrEmpty(windowsFolder) ? null : windowsFolder.TrimEnd('\\') + "\\";
            var byPath = new Dictionary<string, RunningProgram>(StringComparer.OrdinalIgnoreCase);
            foreach (ProcessEntry entry in running)
            {
                if (entry.Pid == selfPid || entry.Pid <= 4)
                    continue;
                ProcessDetails details = processes.Query(entry.Pid);
                if (details == null || string.IsNullOrEmpty(details.Path) || byPath.ContainsKey(details.Path))
                    continue;
                bool inWindows = windows != null && details.Path.StartsWith(windows, StringComparison.OrdinalIgnoreCase);
                if (inWindows && !withWindow.Contains(entry.Pid))
                    continue;
                byPath[details.Path] = new RunningProgram { Name = NameOf(details.Path), Path = details.Path };
            }
            return byPath.Values
                .OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(p => p.Path, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static string NameOf(string path)
        {
            try
            {
                return Path.GetFileNameWithoutExtension(path);
            }
            catch (ArgumentException)
            {
                return path;
            }
        }
    }
}
