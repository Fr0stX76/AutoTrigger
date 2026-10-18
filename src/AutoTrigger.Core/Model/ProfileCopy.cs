using System;
using System.Collections.Generic;
using System.Linq;

namespace AutoTrigger.Core.Model
{
    public static class ProfileCopy
    {
        private static readonly EventKind[] Events = { EventKind.Started, EventKind.Closed, EventKind.GotFocus, EventKind.LostFocus };

        public static Profile Duplicate(Profile source, string name)
        {
            var copy = new Profile { Name = name, RestartApp = source.RestartApp };
            foreach (EventKind kind in Events)
            {
                foreach (ProfileAction action in source.For(kind))
                    copy.For(kind).Add(SettingsJson.ReadAction(SettingsJson.WriteAction(action)));
            }
            return copy;
        }

        public static string CopyName(IEnumerable<string> existing, string baseName, string copyWord)
        {
            var taken = new HashSet<string>(existing.Where(n => n != null), StringComparer.CurrentCultureIgnoreCase);
            string name = $"{baseName} ({copyWord})";
            for (int n = 2; taken.Contains(name); n++)
                name = $"{baseName} ({copyWord} {n})";
            return name;
        }
    }
}
