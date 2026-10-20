using System;
using System.Collections.Generic;
using System.Linq;

namespace AutoTrigger.Core.Model
{
    public sealed class ShortcutEntry
    {
        public Profile Profile { get; set; }

        public EventKind Event { get; set; }

        public KeyCombination Keys { get; set; }
    }

    public static class ShortcutRules
    {
        public static readonly EventKind[] ShortcutEvents = { EventKind.Started, EventKind.Closed };

        private static readonly EventKind[] Events = (EventKind[])Enum.GetValues(typeof(EventKind));

        public static List<ShortcutEntry> All(IEnumerable<Profile> profiles)
        {
            var entries = new List<ShortcutEntry>();
            foreach (Profile profile in profiles)
            {
                foreach (EventKind kind in ShortcutEvents)
                {
                    KeyCombination keys = profile.ShortcutFor(kind);
                    if (KeyNames.CanBeShortcut(keys))
                        entries.Add(new ShortcutEntry { Profile = profile, Event = kind, Keys = keys });
                }
            }
            return entries;
        }

        public static List<ShortcutEntry> Sorted(IEnumerable<Profile> profiles)
        {
            List<Profile> ordered = profiles
                .OrderBy(p => (p.Name ?? string.Empty).Trim(), StringComparer.CurrentCultureIgnoreCase)
                .ToList();
            return All(ordered);
        }

        public static ShortcutEntry EntryOf(AppSettings settings, KeyCombination keys, Guid exceptProfileId = default, EventKind exceptEvent = EventKind.Started) =>
            keys == null || !keys.IsSet
                ? null
                : All(settings.Profiles).FirstOrDefault(e => !(e.Profile.Id == exceptProfileId && e.Event == exceptEvent) && keys.Equals(e.Keys));

        public static Profile OwnerOf(AppSettings settings, KeyCombination keys, Guid exceptProfileId = default, EventKind exceptEvent = EventKind.Started) =>
            EntryOf(settings, keys, exceptProfileId, exceptEvent)?.Profile;

        public static Profile SenderOf(AppSettings settings, KeyCombination keys) =>
            keys == null || !keys.IsSet
                ? null
                : settings.Profiles.FirstOrDefault(p => Events.SelectMany(p.For).OfType<PressHotkeyAction>().Any(a => keys.Equals(a.Keys)));
    }
}
