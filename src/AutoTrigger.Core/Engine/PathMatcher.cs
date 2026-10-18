using System;
using System.IO;

namespace AutoTrigger.Core.Engine
{
    public static class PathMatcher
    {
        public static string Normalize(string path)
        {
            string normalized = (path ?? string.Empty).Trim();
            if (normalized.Length >= 2 && normalized[0] == '"' && normalized[normalized.Length - 1] == '"')
                normalized = normalized.Substring(1, normalized.Length - 2).Trim();
            if (normalized.Length == 0)
                return normalized;
            normalized = Environment.ExpandEnvironmentVariables(normalized);
            try
            {
                if (Path.IsPathRooted(normalized))
                    normalized = Path.GetFullPath(normalized);
            }
            catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException
                || ex is PathTooLongException || ex is System.Security.SecurityException)
            {
            }
            return normalized.TrimEnd('\\', '/');
        }

        public static bool SamePath(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b))
                return false;
            return string.Equals(Normalize(a), Normalize(b), StringComparison.OrdinalIgnoreCase);
        }

        public static string FileNameKey(string path)
        {
            try
            {
                return Path.GetFileName(Normalize(path)).ToLowerInvariant();
            }
            catch (ArgumentException)
            {
                return string.Empty;
            }
        }
    }
}
