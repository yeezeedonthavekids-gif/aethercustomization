namespace Aether.Core.Configuration;

[Flags]
public enum HotkeyModifiers { None = 0, Alt = 1, Control = 2, Shift = 4, Win = 8 }

/// <summary>A keyboard shortcut such as "Ctrl+Space". Key names follow virtual-key names (A-Z, 0-9, F1-F24, Space...).</summary>
public readonly record struct Hotkey(HotkeyModifiers Modifiers, string Key)
{
    private static readonly Dictionary<string, int> NamedKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Space"] = 0x20, ["Enter"] = 0x0D, ["Tab"] = 0x09, ["Escape"] = 0x1B, ["Esc"] = 0x1B,
        ["Backspace"] = 0x08, ["Delete"] = 0x2E, ["Insert"] = 0x2D, ["Home"] = 0x24, ["End"] = 0x23,
        ["PageUp"] = 0x21, ["PageDown"] = 0x22, ["Left"] = 0x25, ["Up"] = 0x26, ["Right"] = 0x27, ["Down"] = 0x28,
        ["Pause"] = 0x13, ["PrintScreen"] = 0x2C, ["Oem3"] = 0xC0, ["Backtick"] = 0xC0,
        ["Minus"] = 0xBD, ["Plus"] = 0xBB, ["Comma"] = 0xBC, ["Period"] = 0xBE,
    };

    public static bool TryParse(string? text, out Hotkey hotkey)
    {
        hotkey = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var parts = text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return false;
        var mods = HotkeyModifiers.None;
        foreach (var part in parts[..^1])
        {
            switch (part.ToLowerInvariant())
            {
                case "ctrl" or "control": mods |= HotkeyModifiers.Control; break;
                case "alt": mods |= HotkeyModifiers.Alt; break;
                case "shift": mods |= HotkeyModifiers.Shift; break;
                case "win" or "windows" or "meta": mods |= HotkeyModifiers.Win; break;
                default: return false;
            }
        }
        var key = parts[^1];
        if (VirtualKeyOf(key) is null) return false;
        if (mods == HotkeyModifiers.None) return false; // global hotkeys must have a modifier
        hotkey = new Hotkey(mods, Normalize(key));
        return true;
    }

    public int VirtualKey => VirtualKeyOf(Key) ?? 0;

    public static int? VirtualKeyOf(string key)
    {
        if (NamedKeys.TryGetValue(key, out var vk)) return vk;
        if (key.Length == 1 && char.IsAsciiLetterOrDigit(key[0])) return char.ToUpperInvariant(key[0]);
        if (key.Length is 2 or 3 && (key[0] is 'F' or 'f') && int.TryParse(key[1..], out var f) && f is >= 1 and <= 24) return 0x70 + f - 1;
        return null;
    }

    private static string Normalize(string key) =>
        key.Length == 1 ? key.ToUpperInvariant() : char.ToUpperInvariant(key[0]) + key[1..];

    public override string ToString()
    {
        var parts = new List<string>();
        if (Modifiers.HasFlag(HotkeyModifiers.Control)) parts.Add("Ctrl");
        if (Modifiers.HasFlag(HotkeyModifiers.Alt)) parts.Add("Alt");
        if (Modifiers.HasFlag(HotkeyModifiers.Shift)) parts.Add("Shift");
        if (Modifiers.HasFlag(HotkeyModifiers.Win)) parts.Add("Win");
        parts.Add(Key);
        return string.Join('+', parts);
    }
}
