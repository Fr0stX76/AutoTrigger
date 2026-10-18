using System;
using System.Collections.Generic;
using System.Linq;
using AutoTrigger.Core.Logging;
using AutoTrigger.Core.Model;

namespace AutoTrigger.Core.Displays
{
    public static class DisplayLinker
    {
        public static bool Sync(AppSettings settings, IReadOnlyList<MonitorInfo> monitors)
        {
            var unmatched = new List<MonitorInfo>();
            bool changed = RefreshKnownDisplays(settings, monitors, unmatched);
            changed |= RelinkMovedDisplays(settings, monitors, unmatched);
            changed |= LinkImportedDisplays(settings, unmatched);
            changed |= AddNewDisplays(settings, unmatched);
            return changed;
        }

        private static bool RefreshKnownDisplays(AppSettings settings, IReadOnlyList<MonitorInfo> monitors, List<MonitorInfo> unmatched)
        {
            bool changed = false;
            foreach (MonitorInfo monitor in monitors)
            {
                if (string.IsNullOrEmpty(monitor.DevicePath))
                    continue;
                DisplayEntry entry = Find(settings, monitor);
                if (entry == null)
                {
                    unmatched.Add(monitor);
                    continue;
                }
                if (!string.IsNullOrEmpty(monitor.FriendlyName) && entry.FriendlyName != monitor.FriendlyName)
                {
                    entry.FriendlyName = monitor.FriendlyName;
                    changed = true;
                }
            }
            return changed;
        }

        private static bool RelinkMovedDisplays(AppSettings settings, IReadOnlyList<MonitorInfo> monitors, List<MonitorInfo> unmatched)
        {
            bool changed = false;
            var connected = new HashSet<string>(
                monitors.Where(m => !string.IsNullOrEmpty(m.DevicePath)).Select(m => m.DevicePath),
                StringComparer.OrdinalIgnoreCase);
            foreach (MonitorInfo monitor in unmatched.ToList())
            {
                string key = StableKey(monitor.DevicePath);
                if (key == null)
                    continue;
                List<DisplayEntry> same = settings.Displays
                    .Where(d => d.IsLinked && !connected.Contains(d.DevicePath) && key == StableKey(d.DevicePath))
                    .ToList();
                if (same.Count != 1)
                    continue;

                string before = same[0].DevicePath;
                same[0].DevicePath = monitor.DevicePath;
                if (!string.IsNullOrEmpty(monitor.FriendlyName))
                    same[0].FriendlyName = monitor.FriendlyName;
                Log.Info($"{monitor.Name}: same display with a new Windows path (driver reinstalled?); kept ({before} -> {monitor.DevicePath})");
                unmatched.Remove(monitor);
                changed = true;
            }
            return changed;
        }

        private static bool LinkImportedDisplays(AppSettings settings, List<MonitorInfo> unmatched)
        {
            bool changed = false;
            foreach (MonitorInfo monitor in unmatched.ToList())
            {
                DisplayEntry legacy = settings.Displays.FirstOrDefault(d => !d.IsLinked && d.LegacyUid == monitor.TargetId);
                if (legacy == null)
                    continue;
                Link(legacy, monitor, "same id");
                unmatched.Remove(monitor);
                changed = true;
            }

            List<DisplayEntry> legacyLeft = settings.Displays.Where(d => !d.IsLinked && d.LegacyUid >= 0).ToList();
            if (legacyLeft.Count == 1 && unmatched.Count == 1)
            {
                Link(legacyLeft[0], unmatched[0], "only display");
                unmatched.Clear();
                changed = true;
            }
            return changed;
        }

        private static bool AddNewDisplays(AppSettings settings, List<MonitorInfo> unmatched)
        {
            foreach (MonitorInfo monitor in unmatched)
            {
                settings.Displays.Add(new DisplayEntry
                {
                    DevicePath = monitor.DevicePath,
                    FriendlyName = monitor.FriendlyName ?? string.Empty,
                    Managed = true,
                });
                Log.Info($"New display: {monitor.Name}");
            }
            return unmatched.Count > 0;
        }

        public static string StableKey(string devicePath)
        {
            if (string.IsNullOrEmpty(devicePath))
                return null;
            string[] parts = devicePath.Split('#');
            if (parts.Length < 3 || parts[1].Length == 0)
                return null;
            string uid = parts[2].Split('&').LastOrDefault(p => p.StartsWith("UID", StringComparison.OrdinalIgnoreCase));
            return uid == null ? null : (parts[1] + "|" + uid).ToUpperInvariant();
        }

        public static DisplayEntry Find(AppSettings settings, MonitorInfo monitor)
        {
            if (string.IsNullOrEmpty(monitor.DevicePath))
                return null;
            return settings.Displays.FirstOrDefault(d =>
                string.Equals(d.DevicePath, monitor.DevicePath, StringComparison.OrdinalIgnoreCase));
        }

        public static List<MonitorInfo> Managed(AppSettings settings, IReadOnlyList<MonitorInfo> monitors)
        {
            return monitors.Where(m => Find(settings, m)?.Managed ?? false).ToList();
        }

        private static void Link(DisplayEntry entry, MonitorInfo monitor, string how)
        {
            entry.DevicePath = monitor.DevicePath;
            if (!string.IsNullOrEmpty(monitor.FriendlyName))
                entry.FriendlyName = monitor.FriendlyName;
            Log.Info($"Imported display (UID {entry.LegacyUid}) linked to {monitor.Name} ({how})");
        }
    }
}
