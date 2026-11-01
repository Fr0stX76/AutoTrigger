using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using AutoTrigger.Core.Json;
using AutoTrigger.Core.Model;
using AutoTrigger.Core.Text;

namespace AutoTrigger.Tests
{
    public static partial class Runner
    {
        private static void LanguageFiles()
        {
            TextCatalog english = TextCatalog.English;
            Check(english.Count > 200, "English texts built in: " + english.Count);
            Check(english.Keys.All(k => english.Get(k).Trim().Length > 0), "no empty English text");

            var used = new HashSet<string>(StringComparer.Ordinal);
            foreach (PropertyInfo property in typeof(Texts).GetProperties(BindingFlags.Public | BindingFlags.Static))
            {
                used.Add(property.Name);
                Check(english.Has(property.Name), "en.json has " + property.Name);
                Eq(0, TextCatalog.Placeholders(english.Get(property.Name)).Count, property.Name + ": no placeholder");
            }
            foreach (MethodInfo method in typeof(Texts).GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Where(m => !m.IsSpecialName && m.ReturnType == typeof(string)))
            {
                used.Add(method.Name);
                Check(english.Has(method.Name), "en.json has " + method.Name);
                string expected = string.Join(",", method.GetParameters().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal));
                Eq(expected, string.Join(",", TextCatalog.Placeholders(english.Get(method.Name))), method.Name + ": placeholders match the code");
            }
            string[] unused = english.Keys.Where(k => !used.Contains(k)).ToArray();
            Eq(string.Empty, string.Join(", ", unused), "every English text is used");

            List<string> builtIn = TextCatalog.BuiltInCodes();
            Check(builtIn.Contains(Languages.English), "en.json built in");
            foreach (string code in builtIn)
            {
                Check(Languages.Supported.Contains(code), code + ".json is a supported language");
                var problems = new List<string>();
                TextCatalog.Parse(TextCatalog.ReadResource(code), code == Languages.English ? null : english, problems);
                Eq(string.Empty, string.Join("; ", problems), code + ".json has no problem");
            }

            Eq("Actions: 4", Texts.ActionCount(4), "named placeholder filled");
            Eq("Display (UID 4294967295)", Texts.ImportedDisplay(4294967295), "large numbers without separators");
        }

        private static void LanguageFallback()
        {
            TextCatalog english = TextCatalog.English;
            var problems = new List<string>();
            Dictionary<string, string> parsed = TextCatalog.Parse(
                "{\"Save\":\"Enregistrer\",\"Nope\":\"x\",\"ActionCount\":\"Actions\",\"Cancel\":\"  \",\"Delete\":3,\"ImportFailed\":\"{reason} !\"}",
                english, problems);
            Eq(2, parsed.Count, "only valid texts kept");
            Eq("Enregistrer", parsed["Save"], "valid text kept");
            Eq("{reason} !", parsed["ImportFailed"], "placeholders may move");
            Eq(4, problems.Count, "unknown key, missing placeholder, empty and non-text reported: " + string.Join("; ", problems));
            Check(problems.Any(p => p.Contains("'ActionCount' must contain {count}")), "the missing placeholder is named");
            Check(TextCatalog.Parse("{ not json", english, problems).Count == 0, "broken JSON: nothing used");
            Check(TextCatalog.Parse("[1]", english, problems).Count == 0, "not an object: nothing used");

            string dir = Path.Combine(Path.GetTempPath(), "at-lang-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                File.WriteAllText(Path.Combine(dir, "en.json"), "{\"Save\":\"Store\",\"Nope\":\"x\",\"ActionCount\":\"Actions\"}", new UTF8Encoding(false));
                var report = new List<string>();
                var found = new List<string>();
                TextCatalog local = TextCatalog.Load(Languages.English, dir, report, found);
                Eq("Store", local.Get("Save"), "test file: corrected text used");
                Eq("Actions: {count}", local.Get("ActionCount"), "test file: invalid text falls back to English");
                Eq("Cancel", local.Get("Cancel"), "test file: missing text falls back to English");
                Eq(0, report.Count, "problems are not reported as plain information");
                Check(found.Count == 1 && found[0].Contains("1 text used") && found[0].Contains("2 ignored"), "problems reported: " + string.Join("; ", found));

                File.WriteAllText(Path.Combine(dir, "en.json"), "{\"Save\":\"Store\"}", new UTF8Encoding(false));
                report.Clear();
                found.Clear();
                TextCatalog.Load(Languages.English, dir, report, found);
                Check(report.Count == 1 && report[0].Contains("1 text used") && found.Count == 0, "clean test file reported once");

                File.WriteAllText(Path.Combine(dir, "en.json"), "{\"Save\":\"" + new string('x', TextCatalog.MaxFileBytes) + "\"}");
                found.Clear();
                Eq("Save", TextCatalog.Load(Languages.English, dir, report, found).Get("Save"), "oversized test file not read");
                Check(found.Any(p => p.Contains("not read")), "oversized test file reported");

                found.Clear();
                TextCatalog missing = TextCatalog.Load("xx", null, report, found);
                Check(ReferenceEquals(english, missing) && found.Count == 1, "a language not built in falls back to English");

                TextCatalog.Activate(local);
                try
                {
                    var note = new Note(() => Texts.Save + " " + Texts.ActionCount(2));
                    Eq("Store Actions: 2", note.Text, "note in the active language");
                    Eq("Save Actions: 2", note.English, "note in English for the log");
                    Eq("Store", Texts.Save, "active language used again after the English note");
                }
                finally
                {
                    TextCatalog.Activate(null);
                }
                Eq("Save", Texts.Save, "English when no language is active");
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        private static void LanguageChoice()
        {
            Eq("fr", Languages.FromCulture("fr-CA"), "French Canada: French");
            Eq("fr", Languages.FromCulture("fr"), "French: French");
            Eq("zh-Hant", Languages.FromCulture("zh-TW"), "Taiwan: Traditional Chinese");
            Eq("zh-Hant", Languages.FromCulture("zh-HK"), "Hong Kong: Traditional Chinese");
            Eq("zh-Hant", Languages.FromCulture("zh-Hant-MO"), "Hant script: Traditional Chinese");
            Eq("zh-Hans", Languages.FromCulture("zh-CN"), "China: Simplified Chinese");
            Eq("sr-Latn", Languages.FromCulture("sr-Latn-RS"), "Serbian: Latin");
            Eq("nb", Languages.FromCulture("nn-NO"), "Nynorsk: Bokmal");
            Eq("pt", Languages.FromCulture("pt-PT"), "Portugal: Portuguese");
            Eq(null, Languages.FromCulture("ar-SA"), "Arabic: not yet");
            Eq(null, Languages.FromCulture(string.Empty), "no Windows language");
            Eq(37, Languages.Supported.Count, "37 languages");
            Eq(37, Languages.Supported.Distinct().Count(), "no language twice");

            List<string> available = Languages.Available();
            Eq(Languages.English, available[0], "English first");
            string windowsFrench = available.Contains("fr") ? "fr" : Languages.English;
            Eq(windowsFrench, Languages.Resolve(string.Empty, "fr-CA"), "Windows language used when built in");
            Eq(Languages.English, Languages.Resolve(string.Empty, "ar-SA"), "Windows language not built in: English");
            Eq(windowsFrench, Languages.Resolve("tlh", "fr-FR"), "unknown setting: Windows language");
            Eq(Languages.Pseudo, Languages.Resolve(Languages.Pseudo, "fr-FR"), "pseudo language kept");
            Eq(Languages.English, Languages.Resolve(Languages.English, "fr-FR"), "setting wins over Windows");
            string name = Languages.NativeName(Languages.English);
            Check(name.Length > 0 && char.IsUpper(name[0]), "native name starts with a capital: " + name);

            var settings = new AppSettings { Language = "fr" };
            Eq("fr", SettingsJson.Read(JsonValue.Parse(SettingsJson.Write(settings).ToJson()), new List<string>()).Language, "language saved");
            Eq(string.Empty, SettingsJson.Read(JsonValue.Parse("{\"language\":\"..\\\\x\"}"), new List<string>()).Language, "odd language value dropped");
            Eq(string.Empty, SettingsJson.Read(JsonValue.Parse("{}"), new List<string>()).Language, "no language: Windows language");
            settings.ResetPreferences();
            Eq(string.Empty, settings.Language, "reset: Windows language");
        }

        private static void PseudoLanguage()
        {
            string pseudo = TextCatalog.PseudoText("Actions: {count}");
            Check(pseudo.StartsWith("[", StringComparison.Ordinal) && pseudo.EndsWith("]", StringComparison.Ordinal), "brackets: " + pseudo);
            Check(pseudo.Contains("{count}"), "placeholder kept: " + pseudo);
            Check(pseudo.Length >= "Actions: {count}".Length * 1.4, "40 % longer: " + pseudo);
            Check(!pseudo.Contains("Actions"), "accented: " + pseudo);
            TextCatalog catalog = TextCatalog.Pseudo(TextCatalog.English);
            Eq(TextCatalog.English.Count, catalog.Count, "every text in the pseudo language");
            Check(catalog.Fill("ActionCount", "count", "4").Contains(": 4"), "pseudo text filled");
            Eq(Languages.Pseudo, TextCatalog.Load(Languages.Pseudo, null, new List<string>(), new List<string>()).Code, "pseudo language loaded");
        }
    }
}
