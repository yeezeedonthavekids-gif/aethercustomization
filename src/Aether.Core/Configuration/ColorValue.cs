using System.Globalization;

namespace Aether.Core.Configuration;

/// <summary>ARGB color parsed from #RGB, #RRGGBB or #AARRGGBB.</summary>
public readonly record struct ColorValue(byte A, byte R, byte G, byte B)
{
    public static bool TryParse(string? text, out ColorValue color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var s = text.Trim().TrimStart('#');
        if (s.Length == 3) s = string.Concat(s.Select(c => new string(c, 2)));
        if (s.Length == 6) s = "FF" + s;
        if (s.Length != 8 || !uint.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v)) return false;
        color = new ColorValue((byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v);
        return true;
    }

    public static ColorValue Parse(string text) =>
        TryParse(text, out var c) ? c : throw new FormatException($"'{text}' is not a valid color.");

    public string ToHex(bool includeAlpha = true) =>
        includeAlpha ? $"#{A:X2}{R:X2}{G:X2}{B:X2}" : $"#{R:X2}{G:X2}{B:X2}";

    /// <summary>The 0xAABBGGRR layout used by DWM and the accent registry values.</summary>
    public int ToAbgr() => unchecked((int)((uint)A << 24 | (uint)B << 16 | (uint)G << 8 | R));

    public static ColorValue FromAbgr(int abgr) =>
        new((byte)(abgr >> 24), (byte)abgr, (byte)(abgr >> 8), (byte)(abgr >> 16));

    public ColorValue WithAlpha(byte alpha) => this with { A = alpha };

    public double Luminance
    {
        get
        {
            static double Channel(byte c) { var v = c / 255.0; return v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4); }
            return 0.2126 * Channel(R) + 0.7152 * Channel(G) + 0.0722 * Channel(B);
        }
    }

    public ColorValue Lerp(ColorValue other, double t)
    {
        byte L(byte a, byte b) => (byte)Math.Clamp(Math.Round(a + (b - a) * t), 0, 255);
        return new ColorValue(L(A, other.A), L(R, other.R), L(G, other.G), L(B, other.B));
    }

    public override string ToString() => ToHex();
}
