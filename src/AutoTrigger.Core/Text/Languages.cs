using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace AutoTrigger.Core.Text
{
    // The display languages of Windows, one per language: regional variants share one file, except Chinese,
    // whose two writing systems differ.
    public static class Languages
    {
        public const string English = "en";
        public const string Pseudo = "pseudo";

        public static readonly IReadOnlyList<string> Supported = new[]
        {
            "bg", "ca", "cs", "da", "de", "el", "en", "es", "et", "eu", "fi", "fr", "gl", "hr", "hu", "id", "it", "ja", "ko",
            "lt", "lv", "nb", "nl", "pl", "pt", "ro", "ru", "sk", "sl", "sr-Latn", "sv", "th", "tr", "uk", "vi", "zh-Hans", "zh-Hant",
        };

        private static readonly string[] TraditionalChinese = { "zh-tw", "zh-hk", "zh-mo" };

        // Built-in languages, English first, then by their own name.
        public static List<string> Available()
        {
            List<string> builtIn = TextCatalog.BuiltInCodes();
            return Supported.Where(c => builtIn.Contains(c, StringComparer.Ordinal))
                .OrderBy(c => c == English ? 0 : 1)
                .ThenBy(NativeName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        // The language for a Windows display language such as fr-CA or zh-TW; null when AutoTrigger has none.
        public static string FromCulture(string cultureName)
        {
            if (string.IsNullOrEmpty(cultureName))
                return null;
            string name = cultureName.ToLowerInvariant();
            string language = name.Split('-')[0];
            if (language == "zh")
            {
                bool traditional = name.IndexOf("hant", StringComparison.Ordinal) >= 0
                    || TraditionalChinese.Any(t => name.StartsWith(t, StringComparison.Ordinal));
                return traditional ? "zh-Hant" : "zh-Hans";
            }
            if (language == "sr")
                return "sr-Latn";
            if (language == "no" || language == "nn")
                return "nb";
            return Supported.FirstOrDefault(c => string.Equals(c, language, StringComparison.Ordinal));
        }

        // The setting names a language, or is empty for the Windows one; anything not built in falls back.
        public static string Resolve(string setting, string systemCulture)
        {
            List<string> available = Available();
            if (setting == Pseudo)
                return Pseudo;
            if (!string.IsNullOrEmpty(setting) && available.Contains(setting, StringComparer.Ordinal))
                return setting;
            string fromWindows = FromCulture(systemCulture);
            return fromWindows != null && available.Contains(fromWindows, StringComparer.Ordinal) ? fromWindows : English;
        }

        public static string NativeName(string code)
        {
            if (code == Pseudo)
                return "Pseudo";
            try
            {
                CultureInfo culture = CultureInfo.GetCultureInfo(code);
                string name = culture.NativeName;
                return name.Length == 0 ? code : culture.TextInfo.ToUpper(name[0]) + name.Substring(1);
            }
            catch (CultureNotFoundException)
            {
                return code;
            }
        }

        // .NET's own texts, such as Windows error messages, follow the language of the window.
        public static CultureInfo UiCulture(string code, string systemCulture)
        {
            try
            {
                if (code == Pseudo || code == English && FromCulture(systemCulture) != English)
                    return CultureInfo.GetCultureInfo("en-US");
                if (FromCulture(systemCulture) == code)
                    return CultureInfo.GetCultureInfo(systemCulture);
                return CultureInfo.GetCultureInfo(code);
            }
            catch (CultureNotFoundException)
            {
                return CultureInfo.GetCultureInfo("en-US");
            }
        }
    }
}
