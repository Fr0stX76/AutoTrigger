using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AutoTrigger.Core.Json;
using AutoTrigger.Core.Model;

namespace AutoTrigger.Tests
{
    public static partial class Runner
    {
        private static void IconFileLayout()
        {
            var images = new[]
            {
                new KeyValuePair<int, byte[]>(16, new byte[] { 1, 2, 3 }),
                new KeyValuePair<int, byte[]>(256, new byte[] { 4, 5, 6, 7 }),
                new KeyValuePair<int, byte[]>(32, new byte[] { 8 }),
            };
            byte[] ico = IconFile.FromPngs(images);
            Eq("256,32,16", string.Join(",", IconFile.Sizes(ico)), "sizes, largest first");
            Eq(0, (int)ico[6], "256 is written as 0");
            Eq(6 + 16 * 3 + 8, ico.Length, "header, entries and images");
            Eq(4, (int)ico[6 + 16 + 16 + 16], "first image follows the entries");

            Check(IconFile.Sizes(new byte[] { 0, 0, 1, 0 }).Count == 0, "truncated header: not an icon");
            byte[] cut = ico.Take(ico.Length - 1).ToArray();
            Check(IconFile.Sizes(cut).Count == 0, "image past the end: not an icon");
            Check(IconFile.Sizes(null).Count == 0, "nothing: not an icon");
            bool refused = false;
            try
            {
                IconFile.FromPngs(new[] { new KeyValuePair<int, byte[]>(512, new byte[] { 1 }) });
            }
            catch (ArgumentException)
            {
                refused = true;
            }
            Check(refused, "no usable size: refused");
        }

        private static void CustomIconStoreFiles()
        {
            string dir = Path.Combine(Path.GetTempPath(), "at-icons-" + Guid.NewGuid().ToString("N"));
            try
            {
                var store = new CustomIconStore(dir);
                var game = new AppEntry { Name = "Game", CustomIcon = true };
                var other = new AppEntry { Name = "Other" };
                byte[] ico = IconFile.FromPngs(new[] { new KeyValuePair<int, byte[]>(32, new byte[] { 9, 9 }) });

                Check(store.Existing(game) == null, "flag without a file: the program's icon");
                store.Save(game.Id, ico);
                string first = store.PathOf(game.Id, ico);
                Eq(first, store.Existing(game), "saved icon is shown");
                Check(store.Existing(other) == null, "no flag: the program's icon");
                Check(Path.GetFileName(first).StartsWith(game.Id.ToString("N") + "-", StringComparison.Ordinal), "named after the application id");

                byte[] bigger = IconFile.FromPngs(new[] { new KeyValuePair<int, byte[]>(48, new byte[] { 1, 2, 3 }) });
                store.Save(game.Id, bigger);
                string second = store.Existing(game);
                Check(second != first && File.ReadAllBytes(second).SequenceEqual(bigger), "a new icon gets a new name, so Windows does not show the old one");
                Check(!File.Exists(first), "the older icon is removed");
                Check(!File.Exists(second + ".tmp"), "no temporary file left");
                store.Save(game.Id, bigger);
                Eq(second, store.Existing(game), "the same icon again keeps its name");

                bool refused = false;
                try
                {
                    store.Save(other.Id, new byte[] { 1, 2, 3 });
                }
                catch (ArgumentException)
                {
                    refused = true;
                }
                Check(refused && Directory.GetFiles(dir, other.Id.ToString("N") + "*").Length == 0, "not an icon: nothing written");

                store.Save(other.Id, ico);
                File.WriteAllText(Path.Combine(dir, "notes.txt"), "mine");
                File.WriteAllBytes(second + ".tmp", ico);
                Eq(2, store.RemoveUnused(new[] { game.Id }), "unused icon and stale temporary file removed");
                Check(File.Exists(second) && !File.Exists(store.PathOf(other.Id, ico)), "kept icon stays");
                Check(File.Exists(Path.Combine(dir, "notes.txt")), "other files are left alone");

                byte[] third = IconFile.FromPngs(new[] { new KeyValuePair<int, byte[]>(24, new byte[] { 5 }) });
                string stale = store.PathOf(game.Id, third);
                File.WriteAllBytes(stale, third);
                File.SetLastWriteTimeUtc(stale, File.GetLastWriteTimeUtc(second).AddMinutes(-5));
                Eq(second, store.Existing(game), "an older icon left behind: the newest is shown");
                Eq(1, store.RemoveUnused(new[] { game.Id }), "an older icon left behind is removed at start");
                Check(File.Exists(second) && !File.Exists(stale), "the newest stays");

                store.Delete(game.Id);
                Check(store.Existing(game) == null, "deleted: the program's icon again");
                store.Delete(game.Id);
                Eq(0, new CustomIconStore(Path.Combine(dir, "none")).RemoveUnused(Array.Empty<Guid>()), "no folder: nothing to remove");
            }
            finally
            {
                if (Directory.Exists(dir))
                    Directory.Delete(dir, true);
            }
        }

        private static void CustomIconSettings()
        {
            var s = NewSettings();
            var game = new AppEntry { Name = "Game", Path = @"C:\Games\game.exe", CustomIcon = true };
            var plain = new AppEntry { Name = "Plain", Path = @"C:\Games\plain.exe" };
            s.Applications.Add(game);
            s.Applications.Add(plain);
            AppSettings read = SettingsJson.Read(SettingsJson.Write(s), new List<string>());
            Check(read.FindApplication(game.Id).CustomIcon, "imported icon saved");
            Check(!read.FindApplication(plain.Id).CustomIcon, "no imported icon saved");

            AppSettings old = SettingsJson.Read(JsonValue.Parse(SettingsFixtures.Version160), new List<string>());
            Check(old.Applications.All(a => !a.CustomIcon), "1.6.0 file: no imported icon");

            DesktopShortcut withIcon = DesktopShortcut.For(game, @"C:\Tools\AutoTrigger.exe", @"C:\Data\custom-icons\x.ico");
            Eq(@"C:\Data\custom-icons\x.ico", withIcon.IconPath, "shortcut uses the imported icon");
            Eq(@"C:\Games\game.exe", DesktopShortcut.For(game, @"C:\Tools\AutoTrigger.exe").IconPath, "otherwise the program's icon");
        }
    }
}
