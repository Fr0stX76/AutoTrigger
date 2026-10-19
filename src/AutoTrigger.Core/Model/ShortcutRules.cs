using System;
using System.Linq;

namespace AutoTrigger.Core.Model
{
    public static class ShortcutRules
    {
        private static readonly EventKind[] Events = (EventKind[])Enum.GetValues(typeof(EventKind));

        public static Profile OwnerOf(AppSettings settings, KeyCombination keys, Guid exceptProfileId = default) =>
            keys == null || !keys.IsSet
                ? null
                : settings.Profiles.FirstOrDefault(p => p.Id != exceptProfileId && keys.Equals(p.Shortcut));

        public static Profile SenderOf(AppSettings settings, KeyCombination keys) =>
            keys == null || !keys.IsSet
                ? null
                : settings.Profiles.FirstOrDefault(p => Events.SelectMany(p.For).OfType<PressHotkeyAction>().Any(a => keys.Equals(a.Keys)));
    }
}
