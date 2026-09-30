using System;
using System.Collections.Generic;
using System.Globalization;

namespace Buffer
{
    [Flags]
    enum HotkeyModifiers
    {
        None = 0,
        Win = 1,
        Ctrl = 2,
        Alt = 4,
        Shift = 8,
    }

    readonly struct Hotkey : IEquatable<Hotkey>
    {
        public const int VK_V = 0x56;
        public static readonly Hotkey Default = new Hotkey(HotkeyModifiers.Win, VK_V);

        public Hotkey(HotkeyModifiers modifiers, int key)
        {
            Modifiers = modifiers;
            Key = key;
        }

        public HotkeyModifiers Modifiers { get; }
        public int Key { get; }

        public int Pack() => ((int)Modifiers << 16) | (Key & 0xFFFF);

        public static Hotkey Unpack(int value) => new Hotkey((HotkeyModifiers)(value >> 16), value & 0xFFFF);

        public static bool IsModifierKey(int vk) =>
            vk == 0x10 || vk == 0x11 || vk == 0x12 ||
            (vk >= 0xA0 && vk <= 0xA5) ||
            vk == 0x5B || vk == 0x5C;

        static bool IsFunctionKey(int vk) => vk >= 0x70 && vk <= 0x87;

        public static IEnumerable<string> ModifierNames(HotkeyModifiers m)
        {
            if ((m & HotkeyModifiers.Win) != 0) yield return "Win";
            if ((m & HotkeyModifiers.Ctrl) != 0) yield return "Ctrl";
            if ((m & HotkeyModifiers.Alt) != 0) yield return "Alt";
            if ((m & HotkeyModifiers.Shift) != 0) yield return "Shift";
        }

        public IEnumerable<string> Parts()
        {
            foreach (var name in ModifierNames(Modifiers))
                yield return name;
            yield return DisplayKeyName(Key);
        }

        public override string ToString() => string.Join("+", Parts());

        public string Validate()
        {
            if (Key == 0 || IsModifierKey(Key))
                return Loc.Instance.ShortcutNeedsKey;
            bool hasMain = (Modifiers & (HotkeyModifiers.Win | HotkeyModifiers.Ctrl | HotkeyModifiers.Alt)) != 0;
            if (!hasMain && !IsFunctionKey(Key))
                return Loc.Instance.ShortcutNeedsModifier;
            if (Modifiers == HotkeyModifiers.Ctrl && (Key == 'C' || Key == 'V' || Key == 'X'))
                return Loc.Instance.ShortcutReservedClipboard;
            if (Modifiers == HotkeyModifiers.Alt && (Key == 0x09 || Key == 0x73))
                return Loc.Instance.ShortcutReservedSystem;
            return null;
        }

        static readonly Dictionary<int, string> Names = new Dictionary<int, string>
        {
            [0x08] = "Backspace",
            [0x09] = "Tab",
            [0x0D] = "Enter",
            [0x13] = "Pause",
            [0x14] = "CapsLock",
            [0x1B] = "Esc",
            [0x20] = "Space",
            [0x21] = "PageUp",
            [0x22] = "PageDown",
            [0x23] = "End",
            [0x24] = "Home",
            [0x25] = "Left",
            [0x26] = "Up",
            [0x27] = "Right",
            [0x28] = "Down",
            [0x2C] = "PrintScreen",
            [0x2D] = "Insert",
            [0x2E] = "Delete",
            [0x6A] = "Num*",
            [0x6B] = "Num+",
            [0x6D] = "Num-",
            [0x6E] = "Num.",
            [0x6F] = "Num/",
            [0x91] = "ScrollLock",
        };

        // Имя для файла настроек: не зависит от раскладки клавиатуры.
        static string StorageKeyName(int vk)
        {
            if ((vk >= '0' && vk <= '9') || (vk >= 'A' && vk <= 'Z'))
                return ((char)vk).ToString();
            if (IsFunctionKey(vk))
                return "F" + (vk - 0x6F);
            if (vk >= 0x60 && vk <= 0x69)
                return "Num" + (vk - 0x60);
            if (Names.TryGetValue(vk, out var name))
                return name;
            return "0x" + vk.ToString("X2");
        }

        public static string DisplayKeyName(int vk)
        {
            string name = StorageKeyName(vk);
            if (!name.StartsWith("0x", StringComparison.Ordinal))
                return name;
            const uint MAPVK_VK_TO_CHAR = 2;
            uint ch = NativeMethods.MapVirtualKey((uint)vk, MAPVK_VK_TO_CHAR) & 0x7FFF;
            return ch != 0 ? char.ToUpperInvariant((char)ch).ToString() : name;
        }

        public string ToStorageString()
        {
            var parts = new List<string>(ModifierNames(Modifiers)) { StorageKeyName(Key) };
            return string.Join("+", parts);
        }

        public static bool TryParse(string text, out Hotkey hotkey)
        {
            hotkey = default;
            if (string.IsNullOrWhiteSpace(text))
                return false;

            var modifiers = HotkeyModifiers.None;
            int key = 0;
            foreach (var raw in text.Split('+'))
            {
                string part = raw.Trim();
                switch (part.ToLowerInvariant())
                {
                    case "win": modifiers |= HotkeyModifiers.Win; continue;
                    case "ctrl": modifiers |= HotkeyModifiers.Ctrl; continue;
                    case "alt": modifiers |= HotkeyModifiers.Alt; continue;
                    case "shift": modifiers |= HotkeyModifiers.Shift; continue;
                }
                if (key != 0)
                    return false;
                key = ParseKey(part);
                if (key == 0)
                    return false;
            }
            if (key == 0)
                return false;
            hotkey = new Hotkey(modifiers, key);
            return true;
        }

        static int ParseKey(string part)
        {
            if (part.Length == 1)
            {
                char c = char.ToUpperInvariant(part[0]);
                if ((c >= '0' && c <= '9') || (c >= 'A' && c <= 'Z'))
                    return c;
            }
            if (part.StartsWith("0x", StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(part.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int hex))
                return hex;
            if (part.Length > 1 && (part[0] == 'F' || part[0] == 'f') &&
                int.TryParse(part.Substring(1), NumberStyles.None, CultureInfo.InvariantCulture, out int f) && f >= 1 && f <= 24)
                return 0x6F + f;
            if (part.StartsWith("Num", StringComparison.OrdinalIgnoreCase) && part.Length == 4 && char.IsDigit(part[3]))
                return 0x60 + (part[3] - '0');
            foreach (var pair in Names)
            {
                if (string.Equals(pair.Value, part, StringComparison.OrdinalIgnoreCase))
                    return pair.Key;
            }
            return 0;
        }

        public bool Equals(Hotkey other) => Modifiers == other.Modifiers && Key == other.Key;
        public override bool Equals(object obj) => obj is Hotkey other && Equals(other);
        public override int GetHashCode() => Pack();
        public static bool operator ==(Hotkey a, Hotkey b) => a.Equals(b);
        public static bool operator !=(Hotkey a, Hotkey b) => !a.Equals(b);
    }
}
