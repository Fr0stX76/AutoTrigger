using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace AutoTrigger.Core.Model
{
    public sealed class AppVersion
    {
        private static readonly Regex Release = new Regex(@"^v?([0-9]{1,5})\.([0-9]{1,5})\.([0-9]{1,5})$", RegexOptions.CultureInvariant);
        private static readonly Regex Build = new Regex(@"^([0-9]{1,5})\.([0-9]{1,5})\.([0-9]{1,5})(?:[ -]RC[0-9]{1,3})?$",
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        private AppVersion(Match match, bool candidate)
        {
            Major = Number(match, 1);
            Minor = Number(match, 2);
            Patch = Number(match, 3);
            IsCandidate = candidate;
        }

        public int Major { get; }

        public int Minor { get; }

        public int Patch { get; }

        public bool IsCandidate { get; }

        // A release tag from GitHub: "v1.4.0". Nothing else is accepted, since the text comes from the network.
        public static AppVersion ParseRelease(string tag)
        {
            Match match = Release.Match(tag ?? string.Empty);
            return match.Success ? new AppVersion(match, false) : null;
        }

        // This build: "1.3.0" or a release candidate such as "1.3.0 RC2".
        public static AppVersion ParseBuild(string version)
        {
            string text = (version ?? string.Empty).Trim();
            Match match = Build.Match(text);
            return match.Success ? new AppVersion(match, text.IndexOf("RC", StringComparison.OrdinalIgnoreCase) >= 0) : null;
        }

        // A release candidate comes before the release of the same number.
        public bool IsNewerThan(AppVersion other)
        {
            if (other == null)
                return true;
            int result = Major.CompareTo(other.Major);
            if (result == 0)
                result = Minor.CompareTo(other.Minor);
            if (result == 0)
                result = Patch.CompareTo(other.Patch);
            return result != 0 ? result > 0 : other.IsCandidate && !IsCandidate;
        }

        public override string ToString() => string.Format(CultureInfo.InvariantCulture, "{0}.{1}.{2}", Major, Minor, Patch);

        private static int Number(Match match, int group) => int.Parse(match.Groups[group].Value, CultureInfo.InvariantCulture);
    }
}
