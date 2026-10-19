using System;

namespace AutoTrigger.Core.Model
{
    public sealed class KeyCombination : IEquatable<KeyCombination>
    {
        public bool Ctrl { get; set; }

        public bool Alt { get; set; }

        public bool Shift { get; set; }

        public int Key { get; set; }

        public bool IsSet => Key != 0;

        public KeyCombination Copy() => new KeyCombination { Ctrl = Ctrl, Alt = Alt, Shift = Shift, Key = Key };

        public bool Equals(KeyCombination other) =>
            other != null && Ctrl == other.Ctrl && Alt == other.Alt && Shift == other.Shift && Key == other.Key;

        public override bool Equals(object obj) => Equals(obj as KeyCombination);

        public override int GetHashCode() => (Key << 3) | (Ctrl ? 1 : 0) | (Alt ? 2 : 0) | (Shift ? 4 : 0);

        public override string ToString() => KeyNames.Combination(this);
    }
}
