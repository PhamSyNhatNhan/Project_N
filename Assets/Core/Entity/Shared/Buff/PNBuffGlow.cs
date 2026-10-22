using System;
using System.Collections.Generic;
using System.Globalization;

// The `glow` of a Buff definition is a colour: "" (white), a preset name (case-insensitive) or hex "#rgb", "#rrggbb", "#rrggbbaa".
// Pure parsing so Core and the smoke suite can check it; the UI turns the result into a tint.
public static class PNBuffGlow
{
    public const string Default = "white";
    private static readonly Dictionary<string, byte[]> presets = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase)
    {
        { "white", new byte[] { 255, 255, 255 } }, { "red", new byte[] { 255, 64, 48 } }, { "orange", new byte[] { 255, 150, 40 } },
        { "gold", new byte[] { 255, 205, 70 } }, { "green", new byte[] { 90, 230, 130 } }, { "aqua", new byte[] { 80, 225, 235 } },
        { "blue", new byte[] { 80, 150, 255 } }, { "purple", new byte[] { 175, 120, 255 } }, { "pink", new byte[] { 255, 130, 190 } },
    };
    public static IEnumerable<string> PresetNames => presets.Keys;

    // Empty or whitespace means the default white. False for anything that is neither a preset nor valid hex.
    public static bool TryParse(string text, out byte r, out byte g, out byte b, out byte a)
    {
        r = g = b = a = 255;
        if (string.IsNullOrWhiteSpace(text)) return true;
        text = text.Trim();
        if (text[0] != '#')
        {
            if (!presets.TryGetValue(text, out var rgb)) return false;
            r = rgb[0]; g = rgb[1]; b = rgb[2]; return true;
        }
        string hex = text.Substring(1);
        if (hex.Length == 3)
        {
            if (!Nibble(hex[0], out r) || !Nibble(hex[1], out g) || !Nibble(hex[2], out b)) return false;
            r = (byte)(r * 17); g = (byte)(g * 17); b = (byte)(b * 17); return true;
        }
        if (hex.Length != 6 && hex.Length != 8) return false;
        for (int i = 0; i < hex.Length; i++) if (!Uri.IsHexDigit(hex[i])) return false;
        r = Pair(hex, 0); g = Pair(hex, 2); b = Pair(hex, 4); if (hex.Length == 8) a = Pair(hex, 6);
        return true;
    }

    public static bool IsValid(string text) { return TryParse(text, out _, out _, out _, out _); }

    private static bool Nibble(char c, out byte value)
    {
        value = 0; if (!Uri.IsHexDigit(c)) return false;
        value = byte.Parse(c.ToString(), NumberStyles.HexNumber, CultureInfo.InvariantCulture); return true;
    }
    private static byte Pair(string hex, int at) { return byte.Parse(hex.Substring(at, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture); }
}
