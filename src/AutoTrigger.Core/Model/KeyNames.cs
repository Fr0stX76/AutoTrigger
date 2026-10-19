using System;
using System.Collections.Generic;
using System.Globalization;

namespace AutoTrigger.Core.Model
{
    public static class KeyNames
    {
        private static readonly Dictionary<int, string> Named = new Dictionary<int, string>
        {
            [0x08] = "Backspace",
            [0x09] = "Tab",
            [0x0D] = "Enter",
            [0x13] = "Pause",
            [0x1B] = "Esc",
            [0x20] = "Space",
            [0x21] = "Page Up",
            [0x22] = "Page Down",
            [0x23] = "End",
            [0x24] = "Home",
            [0x25] = "Left",
            [0x26] = "Up",
            [0x27] = "Right",
            [0x28] = "Down",
            [0x2C] = "Print Screen",
            [0x2D] = "Insert",
            [0x2E] = "Delete",
            [0x6A] = "Num *",
            [0x6B] = "Num +",
            [0x6D] = "Num -",
            [0x6E] = "Num .",
            [0x6F] = "Num /",
            [0x91] = "Scroll Lock",
        };

        private static readonly HashSet<int> Extended = new HashSet<int>
        {
            0x21, 0x22, 0x23, 0x24, 0x25, 0x26, 0x27, 0x28, 0x2C, 0x2D, 0x2E, 0x6F,
        };

        public static Func<int, string> LayoutName { get; set; }

        public static string Readable(KeyCombination keys) => Combination(keys, LayoutName);

        public static string Combination(KeyCombination keys, Func<int, string> layoutName = null)
        {
            if (keys == null || !keys.IsSet)
                return string.Empty;
            var parts = new List<string>();
            if (keys.Ctrl)
                parts.Add("Ctrl");
            if (keys.Alt)
                parts.Add("Alt");
            if (keys.Shift)
                parts.Add("Shift");
            parts.Add(Name(keys.Key, layoutName));
            return string.Join("+", parts);
        }

        public static string Name(int key, Func<int, string> layoutName = null)
        {
            if (IsDigit(key) || IsLetter(key))
                return ((char)key).ToString();
            if (IsNumpadDigit(key))
                return "Num " + (key - 0x60).ToString(CultureInfo.InvariantCulture);
            if (IsFunctionKey(key))
                return "F" + (key - 0x6F).ToString(CultureInfo.InvariantCulture);
            if (Named.TryGetValue(key, out string name))
                return name;
            string fromLayout = layoutName?.Invoke(key);
            return string.IsNullOrWhiteSpace(fromLayout) ? "Key " + key.ToString("X2", CultureInfo.InvariantCulture) : fromLayout;
        }

        public static bool CanSend(int key) =>
            IsDigit(key) || IsLetter(key) || IsNumpadDigit(key) || IsFunctionKey(key) || Named.ContainsKey(key) || IsPunctuation(key);

        public static bool IsExtended(int key) => Extended.Contains(key);

        public static bool CanBeShortcut(KeyCombination keys) =>
            keys != null && keys.IsSet && CanSend(keys.Key) && (keys.Ctrl || keys.Alt || IsSpareFunctionKey(keys.Key));

        private static bool IsSpareFunctionKey(int key) => key >= 0x7C && key <= 0x87;

        private static bool IsDigit(int key) => key >= 0x30 && key <= 0x39;

        private static bool IsLetter(int key) => key >= 0x41 && key <= 0x5A;

        private static bool IsNumpadDigit(int key) => key >= 0x60 && key <= 0x69;

        private static bool IsFunctionKey(int key) => key >= 0x70 && key <= 0x87;

        private static bool IsPunctuation(int key) => (key >= 0xBA && key <= 0xC0) || (key >= 0xDB && key <= 0xDF) || key == 0xE2;
    }
}
