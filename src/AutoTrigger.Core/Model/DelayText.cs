using System;
using System.Globalization;

namespace AutoTrigger.Core.Model
{
    public static class DelayText
    {
        public static string Format(int ms) => (ms / 1000.0).ToString("0.#", CultureInfo.InvariantCulture);

        public static bool TryParse(string text, out int ms)
        {
            ms = 0;
            string cleaned = (text ?? string.Empty).Trim().Replace(',', '.');
            if (cleaned.Length == 0 || !double.TryParse(cleaned, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out double seconds))
                return false;
            if (seconds > ProfileAction.MaxDelayMs / 1000.0)
                seconds = ProfileAction.MaxDelayMs / 1000.0;
            ms = (int)Math.Round(seconds * 10, MidpointRounding.AwayFromZero) * ProfileAction.DelayStepMs;
            return true;
        }

        public static bool IsAllowed(char c) => char.IsDigit(c) || c == '.' || c == ',';
    }
}
