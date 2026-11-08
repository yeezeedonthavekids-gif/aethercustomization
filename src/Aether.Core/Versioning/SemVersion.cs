using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Aether.Core.Versioning;

/// <summary>Minimal Semantic Versioning 2.0 implementation (build metadata is ignored for ordering).</summary>
[JsonConverter(typeof(SemVersionJsonConverter))]
public sealed class SemVersion : IComparable<SemVersion>, IEquatable<SemVersion>
{
    public SemVersion(int major, int minor = 0, int patch = 0, string? prerelease = null)
    {
        if (major < 0 || minor < 0 || patch < 0) throw new ArgumentOutOfRangeException(nameof(major));
        Major = major; Minor = minor; Patch = patch;
        Prerelease = string.IsNullOrEmpty(prerelease) ? null : prerelease;
    }

    public int Major { get; }
    public int Minor { get; }
    public int Patch { get; }
    public string? Prerelease { get; }

    public static SemVersion Parse(string text) =>
        TryParse(text, out var v) ? v : throw new FormatException($"'{text}' is not a valid semantic version.");

    public static bool TryParse(string? text, [NotNullWhen(true)] out SemVersion? version)
    {
        version = null;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var s = text.Trim().TrimStart('v', 'V');
        var plus = s.IndexOf('+');
        if (plus >= 0) s = s[..plus];
        string? pre = null;
        var dash = s.IndexOf('-');
        if (dash >= 0) { pre = s[(dash + 1)..]; s = s[..dash]; if (pre.Length == 0) return false; }
        var parts = s.Split('.');
        if (parts.Length is < 1 or > 3) return false;
        var nums = new int[3];
        for (var i = 0; i < parts.Length; i++)
            if (!int.TryParse(parts[i], System.Globalization.NumberStyles.None, null, out nums[i])) return false;
        version = new SemVersion(nums[0], nums[1], nums[2], pre);
        return true;
    }

    public int CompareTo(SemVersion? other)
    {
        if (other is null) return 1;
        var c = Major.CompareTo(other.Major);
        if (c != 0) return c;
        c = Minor.CompareTo(other.Minor);
        if (c != 0) return c;
        c = Patch.CompareTo(other.Patch);
        if (c != 0) return c;
        if (Prerelease is null) return other.Prerelease is null ? 0 : 1;
        if (other.Prerelease is null) return -1;
        return ComparePrerelease(Prerelease, other.Prerelease);
    }

    private static int ComparePrerelease(string a, string b)
    {
        var pa = a.Split('.');
        var pb = b.Split('.');
        for (var i = 0; i < Math.Min(pa.Length, pb.Length); i++)
        {
            var aNum = int.TryParse(pa[i], out var na);
            var bNum = int.TryParse(pb[i], out var nb);
            var c = (aNum, bNum) switch
            {
                (true, true) => na.CompareTo(nb),
                (true, false) => -1,
                (false, true) => 1,
                _ => string.CompareOrdinal(pa[i], pb[i]),
            };
            if (c != 0) return c;
        }
        return pa.Length.CompareTo(pb.Length);
    }

    public bool Equals(SemVersion? other) => CompareTo(other) == 0;
    public override bool Equals(object? obj) => obj is SemVersion v && Equals(v);
    public override int GetHashCode() => HashCode.Combine(Major, Minor, Patch, Prerelease);
    public override string ToString() => Prerelease is null ? $"{Major}.{Minor}.{Patch}" : $"{Major}.{Minor}.{Patch}-{Prerelease}";

    public static bool operator <(SemVersion a, SemVersion b) => a.CompareTo(b) < 0;
    public static bool operator >(SemVersion a, SemVersion b) => a.CompareTo(b) > 0;
    public static bool operator <=(SemVersion a, SemVersion b) => a.CompareTo(b) <= 0;
    public static bool operator >=(SemVersion a, SemVersion b) => a.CompareTo(b) >= 0;
}

public sealed class SemVersionJsonConverter : JsonConverter<SemVersion>
{
    public override SemVersion Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        SemVersion.TryParse(reader.GetString(), out var v) ? v : throw new JsonException($"Invalid version '{reader.GetString()}'.");

    public override void Write(Utf8JsonWriter writer, SemVersion value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString());
}
