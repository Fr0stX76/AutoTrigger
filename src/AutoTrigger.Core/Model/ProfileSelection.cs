using System;
using System.Collections.Generic;
using System.Linq;

namespace AutoTrigger.Core.Model
{
    // The profile that the Profiles page edits next to its list.
    public static class ProfileSelection
    {
        // The profile listed after this one, or before it at the end of the list; none when it was the only one.
        public static Guid Neighbour(IList<Profile> listed, Profile profile)
        {
            int index = listed.IndexOf(profile);
            List<Profile> others = listed.Where(p => p != profile).ToList();
            if (others.Count == 0)
                return Guid.Empty;
            return others[Math.Max(0, Math.Min(index, others.Count - 1))].Id;
        }

        // The editor never shows a profile that the list hides: the first one listed takes its place, or none.
        public static Guid Listed(IEnumerable<Profile> listed, Guid selected)
        {
            List<Profile> shown = listed.ToList();
            return shown.Any(p => p.Id == selected) ? selected : shown.FirstOrDefault()?.Id ?? Guid.Empty;
        }
    }
}
