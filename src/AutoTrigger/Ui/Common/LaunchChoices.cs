using System;
using System.Collections.Generic;
using System.Globalization;
using AutoTrigger.Core.Model;
using AutoTrigger.Core.Text;

namespace AutoTrigger.Ui
{
    // The choices of "Launch button" in one list: nothing, the application's own program, or a profile.
    internal static class LaunchChoices
    {
        public const string None = "none";
        public const string Program = "program";

        public static List<KeyValuePair<string, string>> For(AppSettings settings)
        {
            var list = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>(None, Texts.LaunchNone),
                new KeyValuePair<string, string>(Program, Texts.Program),
            };
            foreach (KeyValuePair<Guid, string> profile in Kit.ProfileChoices(settings, false))
                list.Add(new KeyValuePair<string, string>(KeyOf(profile.Key), profile.Value));
            return list;
        }

        public static string KeyOf(AppEntry app)
        {
            switch (app.Launch)
            {
                case LaunchKind.None: return None;
                case LaunchKind.Profile: return KeyOf(app.LaunchProfileId);
                default: return Program;
            }
        }

        public static void Apply(AppEntry app, string key)
        {
            if (key == None || key == Program)
            {
                app.Launch = key == None ? LaunchKind.None : LaunchKind.Program;
                app.LaunchProfileId = Guid.Empty;
            }
            else if (Guid.TryParse(key, out Guid id))
            {
                app.Launch = LaunchKind.Profile;
                app.LaunchProfileId = id;
            }
        }

        private static string KeyOf(Guid profileId) => profileId.ToString("D", CultureInfo.InvariantCulture);
    }
}
