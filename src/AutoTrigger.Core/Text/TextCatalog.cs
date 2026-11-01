using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using AutoTrigger.Core.Json;
using AutoTrigger.Core.Logging;
using AutoTrigger.Core.Model;

namespace AutoTrigger.Core.Text
{
    // The texts of one language. Anything missing or invalid falls back to English, so a partial or broken
    // translation can never leave the window without a text.
    public sealed class TextCatalog
    {
        public const string ResourcePrefix = "AutoTrigger.Lang.";
        public const int MaxFileBytes = 1024 * 1024;

        private static readonly Regex PlaceholderPattern = new Regex(@"\{([A-Za-z]+)\}", RegexOptions.CultureInvariant);
        private static readonly object EnglishLock = new object();
        private static volatile TextCatalog _english;
        private static volatile TextCatalog _active;

        // Set only while a note is written for the log, which stays in English whatever the window shows.
        [ThreadStatic]
        private static TextCatalog _forced;

        private readonly Dictionary<string, string> _texts;

        private TextCatalog(string code, Dictionary<string, string> texts)
        {
            Code = code;
            _texts = texts;
        }

        public string Code { get; }

        public int Count => _texts.Count;

        public IEnumerable<string> Keys => _texts.Keys;

        public static TextCatalog English
        {
            get
            {
                if (_english != null)
                    return _english;
                lock (EnglishLock)
                {
                    if (_english == null)
                        _english = LoadEnglish();
                    return _english;
                }
            }
        }

        public static TextCatalog Active => _forced ?? _active ?? English;

        public static void Activate(TextCatalog catalog) => _active = catalog;

        public static string InEnglish(Func<string> render)
        {
            TextCatalog previous = _forced;
            _forced = English;
            try
            {
                return render();
            }
            finally
            {
                _forced = previous;
            }
        }

        public bool Has(string key) => key != null && _texts.ContainsKey(key);

        public string Get(string key)
        {
            if (key != null && _texts.TryGetValue(key, out string text))
                return text;
            if (!ReferenceEquals(this, English) && English.Has(key))
                return English._texts[key];
            return key ?? string.Empty;
        }

        public string Fill(string key, params string[] namesAndValues)
        {
            string template = Get(key);
            return PlaceholderPattern.Replace(template, match =>
            {
                string name = match.Groups[1].Value;
                for (int i = 0; i + 1 < namesAndValues.Length; i += 2)
                {
                    if (string.Equals(namesAndValues[i], name, StringComparison.Ordinal))
                        return namesAndValues[i + 1] ?? string.Empty;
                }
                return match.Value;
            });
        }

        public static List<string> Placeholders(string text) =>
            PlaceholderPattern.Matches(text ?? string.Empty).Cast<Match>()
                .Select(m => m.Groups[1].Value)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(n => n, StringComparer.Ordinal)
                .ToList();

        // Reads one language file. With a reference, only its keys are accepted, with the same placeholders.
        public static Dictionary<string, string> Parse(string json, TextCatalog reference, List<string> problems)
        {
            var texts = new Dictionary<string, string>(StringComparer.Ordinal);
            JsonValue root;
            try
            {
                root = JsonValue.Parse(json ?? string.Empty);
            }
            catch (JsonException ex)
            {
                problems.Add("not valid JSON (" + ex.Message + ")");
                return texts;
            }
            if (root == null || root.Kind != JsonKind.Object)
            {
                problems.Add("not a JSON object of texts");
                return texts;
            }
            foreach (KeyValuePair<string, JsonValue> property in root.Properties)
            {
                string key = property.Key;
                string text = property.Value?.AsString();
                if (reference != null && !reference.Has(key))
                    problems.Add($"'{key}' is not a text of AutoTrigger");
                else if (text == null)
                    problems.Add($"'{key}' is not a text in quotes");
                else if (text.Trim().Length == 0)
                    problems.Add($"'{key}' is empty");
                else if (reference != null && !SamePlaceholders(text, reference.Get(key)))
                    problems.Add($"'{key}' must contain {Describe(Placeholders(reference.Get(key)))}");
                else
                    texts[key] = text;
            }
            return texts;
        }

        public static TextCatalog FromTexts(string code, Dictionary<string, string> texts) =>
            new TextCatalog(code, new Dictionary<string, string>(texts, StringComparer.Ordinal));

        // The language built into AutoTrigger, then a file of the same name in the settings folder over it:
        // translators see their corrections without building anything.
        public static TextCatalog Load(string code, string overrideDirectory, List<string> report, List<string> problemsFound)
        {
            if (code == Languages.Pseudo)
                return Pseudo(English);

            var texts = new Dictionary<string, string>(StringComparer.Ordinal);
            if (code != Languages.English)
            {
                string builtIn = ReadResource(code);
                if (builtIn == null)
                {
                    problemsFound.Add($"{code}.json is not built into this version; English is used");
                    return English;
                }
                var problems = new List<string>();
                foreach (KeyValuePair<string, string> pair in Parse(builtIn, English, problems))
                    texts[pair.Key] = pair.Value;
                if (problems.Count > 0)
                    problemsFound.Add($"{code}.json built in: {Summary(problems)}");
            }

            string file = overrideDirectory == null ? null : Path.Combine(overrideDirectory, code + ".json");
            if (file != null && File.Exists(file))
            {
                var problems = new List<string>();
                string json = ReadFile(file, problems);
                Dictionary<string, string> local = json == null ? new Dictionary<string, string>() : Parse(json, English, problems);
                foreach (KeyValuePair<string, string> pair in local)
                    texts[pair.Key] = pair.Value;
                string used = $"{code}.json in the settings folder: {CountText.Of(local.Count, "text")} used";
                if (problems.Count > 0)
                    problemsFound.Add(used + ", " + Summary(problems));
                else
                    report.Add(used);
            }

            if (code == Languages.English && texts.Count == 0)
                return English;
            return new TextCatalog(code, texts);
        }

        public static TextCatalog Pseudo(TextCatalog source) =>
            new TextCatalog(Languages.Pseudo, source._texts.ToDictionary(p => p.Key, p => PseudoText(p.Value), StringComparer.Ordinal));

        // Accented and 40 % longer, inside brackets: a cut text, or one left in English, shows at a glance.
        public static string PseudoText(string text)
        {
            var result = new StringBuilder("[");
            int last = 0;
            foreach (Match match in PlaceholderPattern.Matches(text))
            {
                AppendAccented(result, text, last, match.Index);
                result.Append(match.Value);
                last = match.Index + match.Length;
            }
            AppendAccented(result, text, last, text.Length);
            int padding = Math.Max(1, (int)Math.Ceiling(text.Length * 0.4) - 3);
            result.Append(' ').Append('·', padding).Append(']');
            return result.ToString();
        }

        public static string ReadResource(string code)
        {
            using (Stream stream = typeof(TextCatalog).Assembly.GetManifestResourceStream(ResourcePrefix + code + ".json"))
            {
                if (stream == null)
                    return null;
                using (var reader = new StreamReader(stream, Encoding.UTF8))
                    return reader.ReadToEnd();
            }
        }

        public static List<string> BuiltInCodes()
        {
            int start = ResourcePrefix.Length;
            return typeof(TextCatalog).Assembly.GetManifestResourceNames()
                .Where(n => n.StartsWith(ResourcePrefix, StringComparison.Ordinal) && n.EndsWith(".json", StringComparison.Ordinal))
                .Select(n => n.Substring(start, n.Length - start - ".json".Length))
                .OrderBy(c => c, StringComparer.Ordinal)
                .ToList();
        }

        private static TextCatalog LoadEnglish()
        {
            var problems = new List<string>();
            string json = ReadResource(Languages.English);
            Dictionary<string, string> texts = json == null ? new Dictionary<string, string>() : Parse(json, null, problems);
            if (json == null)
                Log.Error("en.json is not built into this version; the window shows text keys");
            else if (problems.Count > 0)
                Log.Error("en.json built in: " + Summary(problems));
            return new TextCatalog(Languages.English, texts);
        }

        private static string ReadFile(string path, List<string> problems)
        {
            try
            {
                var info = new FileInfo(path);
                if (info.Length > MaxFileBytes)
                {
                    problems.Add($"larger than {MaxFileBytes / 1024} KB, not read");
                    return null;
                }
                return File.ReadAllText(path, Encoding.UTF8);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                problems.Add("cannot be read (" + ex.Message + ")");
                return null;
            }
        }

        private static bool SamePlaceholders(string text, string reference) =>
            Placeholders(text).SequenceEqual(Placeholders(reference), StringComparer.Ordinal);

        private static string Describe(List<string> names) =>
            names.Count == 0 ? "no {placeholder}" : string.Join(" and ", names.Select(n => "{" + n + "}"));

        private static string Summary(List<string> problems)
        {
            const int shown = 10;
            string list = string.Join("; ", problems.Take(shown));
            return $"{problems.Count} ignored: {list}" + (problems.Count > shown ? $"; and {problems.Count - shown} more" : string.Empty);
        }

        private static void AppendAccented(StringBuilder result, string text, int start, int end)
        {
            const string plain = "aceinorsuyzACEINORSUYZ";
            const string accented = "áçéîñöŕšûýžÅÇÉÎÑÖŔŠÛÝŽ";
            for (int i = start; i < end; i++)
            {
                int index = plain.IndexOf(text[i]);
                result.Append(index >= 0 ? accented[index] : text[i]);
            }
        }
    }
}
