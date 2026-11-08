using System;
using System.Collections.Generic;
using System.Linq;

namespace AutoTrigger.Core.Model
{
    // A group is only a name that profiles share: it exists while one profile has it.
    public static class ProfileGroups
    {
        public const int MaxNameLength = 100;

        // A name typed, pasted or edited by hand in settings.json names the same group once cleaned.
        public static string Clean(string name)
        {
            if (string.IsNullOrEmpty(name))
                return string.Empty;
            string kept = new string(name.Where(c => !char.IsControl(c)).ToArray()).Trim();
            if (kept.Length <= MaxNameLength)
                return kept;
            int length = char.IsHighSurrogate(kept[MaxNameLength - 1]) ? MaxNameLength - 1 : MaxNameLength;
            return kept.Substring(0, length).TrimEnd();
        }

        // Ordinal, so that the language of the window never splits or joins groups.
        public static bool Same(string first, string second) =>
            string.Equals(Clean(first), Clean(second), StringComparison.OrdinalIgnoreCase);

        public static List<string> Names(IEnumerable<Profile> profiles)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var names = new List<string>();
            foreach (Profile profile in profiles)
            {
                string name = Clean(profile.Group);
                if (name.Length > 0 && seen.Add(name))
                    names.Add(name);
            }
            names.Sort(StringComparer.CurrentCultureIgnoreCase);
            return names;
        }

        // One spelling per group, the first in the list, so that "briques" and "Briques" written by hand are one group.
        public static void Normalize(IEnumerable<Profile> profiles)
        {
            var spelling = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (Profile profile in profiles)
            {
                string name = Clean(profile.Group);
                if (name.Length > 0)
                {
                    if (spelling.TryGetValue(name, out string first))
                        name = first;
                    else
                        spelling.Add(name, name);
                }
                profile.Group = name;
            }
        }

        // The group being renamed does not take its own name: changing only the case of a name is allowed.
        public static bool IsTaken(IEnumerable<Profile> profiles, string name, string renamed)
        {
            string wanted = Clean(name);
            return wanted.Length > 0 && !Same(wanted, renamed) && profiles.Any(p => Same(p.Group, wanted));
        }

        public static bool Rename(IList<Profile> profiles, string from, string to)
        {
            string source = Clean(from);
            string target = Clean(to);
            if (source.Length == 0 || target.Length == 0 || IsTaken(profiles, target, source))
                return false;
            foreach (Profile profile in profiles)
            {
                if (Same(profile.Group, source))
                    profile.Group = target;
            }
            return true;
        }
    }

    // What the Profiles page lists: every profile, the profiles without a group, or one group.
    public sealed class ProfileFilter : IEquatable<ProfileFilter>
    {
        public static readonly ProfileFilter All = new ProfileFilter(FilterKind.All, string.Empty);

        public static readonly ProfileFilter Ungrouped = new ProfileFilter(FilterKind.Ungrouped, string.Empty);

        private readonly FilterKind _kind;

        private ProfileFilter(FilterKind kind, string group)
        {
            _kind = kind;
            Group = group;
        }

        private enum FilterKind
        {
            All,
            Ungrouped,
            Group,
        }

        public string Group { get; }

        public bool IsAll => _kind == FilterKind.All;

        public bool IsUngrouped => _kind == FilterKind.Ungrouped;

        public bool IsGroup => _kind == FilterKind.Group;

        public static ProfileFilter Of(string group)
        {
            string name = ProfileGroups.Clean(group);
            return name.Length == 0 ? Ungrouped : new ProfileFilter(FilterKind.Group, name);
        }

        public bool Shows(Profile profile)
        {
            switch (_kind)
            {
                case FilterKind.All: return true;
                case FilterKind.Ungrouped: return ProfileGroups.Clean(profile.Group).Length == 0;
                default: return ProfileGroups.Same(profile.Group, Group);
            }
        }

        // A group no profile has any more, or "no group" when no group is left, would list nothing worth a filter.
        // A group keeps the spelling its profiles have.
        public ProfileFilter Within(IReadOnlyCollection<string> groups)
        {
            if (IsUngrouped && groups.Count == 0)
                return All;
            if (!IsGroup)
                return this;
            string spelled = groups.FirstOrDefault(g => ProfileGroups.Same(g, Group));
            if (spelled == null)
                return All;
            return string.Equals(spelled, Group, StringComparison.Ordinal) ? this : Of(spelled);
        }

        public bool Equals(ProfileFilter other) =>
            other != null && other._kind == _kind && string.Equals(other.Group, Group, StringComparison.OrdinalIgnoreCase);

        public override bool Equals(object obj) => Equals(obj as ProfileFilter);

        public override int GetHashCode() => ((int)_kind * 397) ^ StringComparer.OrdinalIgnoreCase.GetHashCode(Group);
    }
}
