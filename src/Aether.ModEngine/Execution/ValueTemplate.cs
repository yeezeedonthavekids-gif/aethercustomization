using System.Globalization;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Aether.Core.Abstractions;
using Aether.Core.Configuration;
using Aether.ModEngine.Manifest;

namespace Aether.ModEngine.Execution;

/// <summary>
/// Resolves operation values against mod settings. Supports whole-value references ("{{key}}"), string
/// interpolation ("Prefix {{key}}"), value maps and a fixed set of transforms. No code is evaluated.
/// </summary>
public static partial class ValueTemplate
{
    [GeneratedRegex(@"\{\{\s*([A-Za-z][A-Za-z0-9_]*)\s*\}\}")]
    private static partial Regex Placeholder();

    public static IEnumerable<string> ReferencedKeys(JsonNode? node)
    {
        if (node is JsonValue v && v.TryGetValue<string>(out var s))
            foreach (Match m in Placeholder().Matches(s)) yield return m.Groups[1].Value;
    }

    public static JsonNode? Resolve(JsonNode? node, JsonObject settings)
    {
        if (node is not JsonValue v || !v.TryGetValue<string>(out var s)) return node?.DeepClone();
        var whole = Placeholder().Match(s);
        if (whole.Success && whole.Length == s.Length)
            return settings[whole.Groups[1].Value]?.DeepClone();
        return JsonValue.Create(Placeholder().Replace(s, m => Stringify(settings[m.Groups[1].Value])));
    }

    public static bool ConditionHolds(OperationCondition? condition, JsonObject settings)
    {
        if (condition is null) return true;
        var actual = settings[condition.Setting];
        if (condition.EqualsValue is not null && !JsonEquals(actual, condition.EqualsValue)) return false;
        if (condition.NotEquals is not null && JsonEquals(actual, condition.NotEquals)) return false;
        return true;
    }

    public static string Stringify(JsonNode? node) => node switch
    {
        null => "",
        JsonValue v when v.TryGetValue<string>(out var s) => s,
        JsonValue v when v.TryGetValue<bool>(out var b) => b ? "true" : "false",
        JsonValue v when SettingValues.TryGetDouble(v, out var d) => d.ToString(CultureInfo.InvariantCulture),
        _ => node.ToJsonString(),
    };

    private static bool JsonEquals(JsonNode? a, JsonNode? b) => Stringify(a) == Stringify(b);

    public static JsonNode? ApplyMapAndTransform(JsonNode? value, Dictionary<string, JsonNode?>? map, ValueTransform transform)
    {
        if (map is not null)
        {
            var key = Stringify(value);
            if (!map.TryGetValue(key, out var mapped))
                throw new InvalidOperationException($"No valueMap entry for '{key}'.");
            value = mapped?.DeepClone();
        }
        return transform switch
        {
            ValueTransform.None => value,
            ValueTransform.ColorToAbgr => JsonValue.Create(ColorValue.Parse(Stringify(value)).ToAbgr()),
            ValueTransform.ColorToRgbTriplet => JsonValue.Create(RgbTriplet(ColorValue.Parse(Stringify(value)))),
            ValueTransform.InvertBool => JsonValue.Create(ToBool(value) ? 0 : 1),
            ValueTransform.BoolToInt => JsonValue.Create(ToBool(value) ? 1 : 0),
            _ => value,
        };
    }

    private static string RgbTriplet(ColorValue c) => $"{c.R} {c.G} {c.B}";

    public static bool ToBool(JsonNode? node) => Stringify(node).ToLowerInvariant() switch
    {
        "true" or "1" => true,
        "false" or "0" or "" => false,
        var s => throw new InvalidOperationException($"'{s}' is not a boolean."),
    };

    public static RegistryData ToRegistryData(JsonNode? node, RegistryDataKind kind)
    {
        var text = Stringify(node);
        return kind switch
        {
            RegistryDataKind.DWord => RegistryData.Dword(ParseInteger<int>(node, text)),
            RegistryDataKind.QWord => RegistryData.Qword(ParseInteger<long>(node, text)),
            RegistryDataKind.String => new RegistryData(RegistryDataKind.String, text),
            RegistryDataKind.ExpandString => new RegistryData(RegistryDataKind.ExpandString, text),
            RegistryDataKind.Binary => RegistryData.Bin(Convert.FromHexString(text.Replace(" ", "").Replace("-", ""))),
            RegistryDataKind.MultiString => new RegistryData(RegistryDataKind.MultiString,
                node is JsonArray arr ? arr.Select(Stringify).ToArray() : text.Split('\n')),
            _ => throw new InvalidOperationException($"Unsupported registry kind {kind}."),
        };
    }

    private static T ParseInteger<T>(JsonNode? node, string text) where T : struct, IBinaryInteger<T>
    {
        if (node is JsonValue v && v.TryGetValue<bool>(out var b)) return b ? T.One : T.Zero;
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase) &&
            ulong.TryParse(text[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var hex))
            return T.CreateTruncating(hex);
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
            return T.CreateTruncating((long)Math.Round(d));
        throw new InvalidOperationException($"'{text}' is not a number.");
    }
}
