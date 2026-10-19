using System;
using System.IO;
using System.Linq;
using AutoTrigger.Core.Json;
using AutoTrigger.Core.Logging;
using AutoTrigger.Core.Model;
using AutoTrigger.Core.Storage;

namespace AutoTrigger.Tests
{
    public static partial class Runner
    {
        private static void LockedSettings()
        {
            string dir = Path.Combine(Path.GetTempPath(), "at-locked-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                using var store = new SettingsStore(dir);
                File.WriteAllText(store.FilePath, "{\"applications\":[{\"name\":\"Kept\",\"path\":\"C:\\\\Games\\\\kept.exe\"}]}");
                string before = File.ReadAllText(store.FilePath);
                LoadResult loaded;
                using (new FileStream(store.FilePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                    loaded = store.Load();
                Eq(LoadSource.Locked, loaded.Source, "file in use: reported as locked");
                Check(!string.IsNullOrEmpty(loaded.Message), "file in use: the user is told");
                Check(store.IsReadOnly, "file in use: nothing will be saved");
                store.SaveSoon(new AppSettings());
                store.FlushPending();
                Eq(before, File.ReadAllText(store.FilePath), "file in use: never overwritten");
                Check(Directory.GetFiles(dir, "settings.invalid-*").Length == 0, "file in use: not set aside");
                Eq(null, store.BackupBeforeImport(), "file in use: no import backup");

                using var later = new SettingsStore(dir);
                LoadResult reread = later.Load();
                Check(reread.Source == LoadSource.Main && reread.Settings.Applications.Single().Name == "Kept", "next start reads it normally");
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        private static void JsonNumberRange()
        {
            Check(Throws<JsonException>(() => JsonValue.Parse("{\"a\":1e999}")), "a number too large is a JSON error");
            Check(Throws<JsonException>(() => JsonValue.Parse("[-1e999]")), "a number too small is a JSON error");
            Eq(1e300, JsonValue.Parse("[1e300]").Items.First().AsNumber().Value, "a large valid number is kept");
            Check(Throws<FormatException>(() => AutoActionsImport.Read(
                "{\"$type\":\"UserAppSettings\",\"ApplicationProfiles\":[],\"Displays\":[{\"UID\":1e999}]}")),
                "import: a number too large gives a readable error");
        }

        private static void UserProfileHidden()
        {
            const string profile = @"C:\Users\Dominic";
            Eq(@"%USERPROFILE%\Games\a.exe started", Log.HideUserProfile(@"C:\Users\Dominic\Games\a.exe started", profile), "profile folder hidden");
            Eq(@"%USERPROFILE%\a and %USERPROFILE%\b", Log.HideUserProfile(@"c:\users\dominic\a and C:\USERS\DOMINIC\b", profile), "every occurrence, any case");
            Eq(@"C:\Users\Dominique\a", Log.HideUserProfile(@"C:\Users\Dominique\a", profile), "a longer name is another folder");
            Eq("%USERPROFILE%", Log.HideUserProfile(profile, profile), "the folder alone");
            Eq(@"D:\Games\a.exe", Log.HideUserProfile(@"D:\Games\a.exe", profile), "other paths unchanged");
            Eq("x", Log.HideUserProfile("x", string.Empty), "no profile: unchanged");
        }

        private static bool Throws<T>(Action action) where T : Exception
        {
            try
            {
                action();
                return false;
            }
            catch (T)
            {
                return true;
            }
        }
    }
}
