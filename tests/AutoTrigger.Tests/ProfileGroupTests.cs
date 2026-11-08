using System.Collections.Generic;
using System.Linq;
using AutoTrigger.Core.Json;
using AutoTrigger.Core.Model;

namespace AutoTrigger.Tests
{
    public static partial class Runner
    {
        private static void SettingsFrom160()
        {
            var warnings = new List<string>();
            AppSettings s = SettingsJson.Read(JsonValue.Parse(SettingsFixtures.Version160), warnings);
            Eq(0, warnings.Count, "1.6.0 file: read without warnings: " + string.Join(" | ", warnings));
            Check(s.Profiles.All(p => p.Group.Length == 0), "1.6.0 file: no profile in a group");
            Check(s.ProfilesShown.IsAll, "1.6.0 file: every profile listed");
            Eq(LaunchKind.Profile, s.Applications[0].Launch, "1.6.0 file: launch profile kept");
            Check(s.Profiles.SelectMany(p => p.OnStart).OfType<RunProgramAction>().All(r => r.SkipIfRunning),
                "1.6.0 file: do not start if already running kept");
            SavedWithoutLoss(SettingsFixtures.Version160, s, "1.6.0 file");
        }

        private static void ProfileGroupsSaved()
        {
            var s = NewSettings();
            Profile hdr = AddProfile(s, "HDR on");
            hdr.Group = "Bricks";
            Profile game = AddProfile(s, "Game");
            game.Group = "Games";
            Profile loose = AddProfile(s, "Desktop");
            s.ProfilesShown = ProfileFilter.Of("Games");

            AppSettings read = SettingsJson.Read(SettingsJson.Write(s), new List<string>());
            Eq("Bricks", read.FindProfile(hdr.Id).Group, "group saved");
            Eq(string.Empty, read.FindProfile(loose.Id).Group, "no group saved");
            Check(read.ProfilesShown.IsGroup && read.ProfilesShown.Group == "Games", "group filter saved");

            s.ProfilesShown = ProfileFilter.Ungrouped;
            Check(SettingsJson.Read(SettingsJson.Write(s), new List<string>()).ProfilesShown.IsUngrouped, "no group filter saved");
            s.ProfilesShown = ProfileFilter.All;
            Check(SettingsJson.Read(SettingsJson.Write(s), new List<string>()).ProfilesShown.IsAll, "all profiles filter saved");

            JsonValue json = SettingsJson.Write(s);
            json["window"].Set("profileFilter", "group").Set("profileGroup", " \t ");
            Check(SettingsJson.Read(json, new List<string>()).ProfilesShown.IsAll, "group filter without a name lists every profile");
            json["window"].Set("profileFilter", "something else");
            Check(SettingsJson.Read(json, new List<string>()).ProfilesShown.IsAll, "unknown filter lists every profile");

            JsonValue profiles = json["profiles"];
            profiles.Items.ElementAt(0).Set("group", "  bricks\u0007 ");
            profiles.Items.ElementAt(1).Set("group", "BRICKS");
            profiles.Items.ElementAt(2).Set("group", new string('x', 150));
            AppSettings edited = SettingsJson.Read(json, new List<string>());
            Eq("bricks", edited.Profiles[0].Group, "hand-edited group cleaned of spaces and control characters");
            Eq("bricks", edited.Profiles[1].Group, "same group in other case takes the first spelling");
            Eq(ProfileGroups.MaxNameLength, edited.Profiles[2].Group.Length, "long group name cut");
        }

        private static void ProfileGroupRules()
        {
            Eq("Bricks", ProfileGroups.Clean("  Bricks\r\n"), "clean: spaces and line breaks");
            Eq(string.Empty, ProfileGroups.Clean(null), "clean: nothing");
            string emoji = new string('a', ProfileGroups.MaxNameLength - 1) + "\U0001F3AE";
            string cut = ProfileGroups.Clean(emoji);
            Check(cut.Length == ProfileGroups.MaxNameLength - 1 && !char.IsHighSurrogate(cut[cut.Length - 1]), "clean: no half character at the cut");
            Check(ProfileGroups.Same("bricks", " Bricks"), "same group whatever the case");
            Check(!ProfileGroups.Same("Bricks", "Games"), "other group");

            var profiles = new List<Profile>
            {
                new Profile { Name = "a", Group = "Games" },
                new Profile { Name = "b", Group = "bricks" },
                new Profile { Name = "c", Group = "Games" },
                new Profile { Name = "d" },
            };
            Eq("bricks|Games", string.Join("|", ProfileGroups.Names(profiles)), "names: once each, sorted");
            Check(ProfileGroups.IsTaken(profiles, "GAMES", null), "taken: other case of a group");
            Check(!ProfileGroups.IsTaken(profiles, "Bricks", "bricks"), "taken: a group may change its case");
            Check(!ProfileGroups.IsTaken(profiles, "Tests", null), "taken: new name");
            Check(!ProfileGroups.Rename(profiles, "bricks", "games"), "rename: refused onto another group");
            Check(!ProfileGroups.Rename(profiles, "bricks", " "), "rename: refused without a name");
            Check(ProfileGroups.Rename(profiles, "bricks", "Bricks"), "rename: case change");
            Eq("Bricks", profiles[1].Group, "rename: case changed");
            Check(ProfileGroups.Rename(profiles, "games", "Played"), "rename: every profile of the group");
            Eq("Played|Bricks|Played|", string.Join("|", profiles.Select(p => p.Group)), "rename: other profiles untouched");

            ProfileFilter played = ProfileFilter.Of("played");
            Check(played.Shows(profiles[0]) && !played.Shows(profiles[1]) && !played.Shows(profiles[3]), "filter: one group");
            Check(ProfileFilter.Ungrouped.Shows(profiles[3]) && !ProfileFilter.Ungrouped.Shows(profiles[0]), "filter: no group");
            Check(profiles.All(ProfileFilter.All.Shows), "filter: all profiles");
            Check(ProfileFilter.Of("  ").IsUngrouped, "filter: empty name is no group");
            Check(played.Equals(ProfileFilter.Of("PLAYED")) && !played.Equals(ProfileFilter.Ungrouped), "filter: equality ignores case");

            List<string> names = ProfileGroups.Names(profiles);
            Eq("Played", played.Within(names).Group, "within: the spelling of the profiles");
            Check(ProfileFilter.Of("Gone").Within(names).IsAll, "within: a group gone lists every profile");
            Check(ProfileFilter.Ungrouped.Within(new List<string>()).IsAll, "within: no group left lists every profile");
            Check(ReferenceEquals(ProfileFilter.Ungrouped, ProfileFilter.Ungrouped.Within(names)), "within: kept while groups exist");

            var copy = ProfileCopy.Duplicate(profiles[1], "b (copy)");
            Eq("Bricks", copy.Group, "duplicate: same group");
        }

        private static void MoveInFilteredList()
        {
            var items = new List<string> { "A1", "b1", "A2", "b2", "A3" };
            bool shown(string item) => item.StartsWith("A", System.StringComparison.Ordinal);
            Check(Ordering.MoveAmong(items, "A2", -1, shown), "up past a hidden item");
            Eq("A2,A1,b1,b2,A3", string.Join(",", items), "moved before the previous shown item");
            Check(Ordering.MoveAmong(items, "A1", 1, shown), "down past hidden items");
            Eq("A2,b1,b2,A3,A1", string.Join(",", items), "moved after the next shown item");
            Check(!Ordering.MoveAmong(items, "A2", -1, shown), "first shown item cannot go up");
            Check(!Ordering.MoveAmong(items, "A1", 1, shown), "last shown item cannot go down");
            Check(!Ordering.MoveAmong(items, "missing", 1, shown), "unknown item");
        }

        private static void SelectionStaysListed()
        {
            var first = new Profile { Name = "a", Group = "Games" };
            var middle = new Profile { Name = "b", Group = "Games" };
            var last = new Profile { Name = "c", Group = "Games" };
            var row = new List<Profile> { first, middle, last };
            Eq(last.Id, ProfileSelection.Neighbour(row, middle), "neighbour: the next one listed");
            Eq(middle.Id, ProfileSelection.Neighbour(row, last), "neighbour: the previous one at the end");
            Eq(middle.Id, ProfileSelection.Listed(row, middle.Id), "listed: a listed profile stays selected");
            Eq(first.Id, ProfileSelection.Listed(row, System.Guid.NewGuid()), "listed: else the first one listed");

            var loose = new Profile { Name = "d" };
            var profiles = new List<Profile> { first, loose };
            ProfileFilter filter = ProfileFilter.Ungrouped;
            System.Guid next = ProfileSelection.Neighbour(profiles.Where(filter.Shows).ToList(), loose);
            Eq(System.Guid.Empty, next, "neighbour: none when it was the only one listed");
            loose.Group = "Games";
            filter = filter.Within(ProfileGroups.Names(profiles));
            Check(filter.IsUngrouped, "no group stays listed while groups exist");
            Eq(System.Guid.Empty, ProfileSelection.Listed(profiles.Where(filter.Shows), next),
                "last profile without a group moved: no hidden profile in the editor");

            var tool = new Profile { Name = "e", Group = "Tools" };
            profiles = new List<Profile> { first, tool };
            filter = ProfileFilter.Of("Tools");
            next = ProfileSelection.Neighbour(profiles.Where(filter.Shows).ToList(), tool);
            profiles.Remove(tool);
            filter = filter.Within(ProfileGroups.Names(profiles));
            Check(filter.IsAll, "group gone with its last profile: every profile listed");
            Eq(first.Id, ProfileSelection.Listed(profiles.Where(filter.Shows), next), "group gone: the first profile listed is selected");
        }
    }
}
