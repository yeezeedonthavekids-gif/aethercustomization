using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Aether.Core.Configuration;

public enum SettingKind
{
    Boolean,
    Choice,
    Slider,
    Number,
    Text,
    Color,
    Hotkey,
    Time,
    FilePath,
    FolderPath,
}

public sealed class SettingOption
{
    public required string Value { get; init; }
    public required string Label { get; init; }
}

/// <summary>
/// A single configurable value. Shared by mods, widgets, plugins and themes so the UI can render
/// every configuration surface from one schema-driven form generator.
/// </summary>
public sealed class SettingDefinition
{
    public required string Key { get; init; }
    public required string Label { get; init; }
    public string? Description { get; init; }
    public SettingKind Type { get; init; }
    public JsonNode? Default { get; init; }
    public double? Min { get; init; }
    public double? Max { get; init; }
    public double? Step { get; init; }
    public string? Unit { get; init; }
    public string? Placeholder { get; init; }
    public int? MaxLength { get; init; }
    /// <summary>Optional regular expression a text value must match.</summary>
    public string? Pattern { get; init; }
    public List<SettingOption>? Options { get; init; }
    public bool Advanced { get; init; }
    public string? Group { get; init; }
    /// <summary>Shown only when another boolean setting is true.</summary>
    public string? VisibleWhen { get; init; }
}

public sealed record SettingValidationError(string Key, string Message);

/// <summary>Validates and coerces raw JSON values against <see cref="SettingDefinition"/>s.</summary>
public static class SettingValues
{
    public static JsonObject ResolveDefaults(IEnumerable<SettingDefinition> definitions)
    {
        var result = new JsonObject();
        foreach (var def in definitions)
            result[def.Key] = def.Default?.DeepClone() ?? DefaultFor(def);
        return result;
    }

    /// <summary>Overlays user values on defaults, dropping unknown keys and coercing invalid entries to defaults.</summary>
    public static (JsonObject Values, IReadOnlyList<SettingValidationError> Errors) Resolve(
        IReadOnlyList<SettingDefinition> definitions, JsonObject? userValues)
    {
        var errors = new List<SettingValidationError>();
        var result = ResolveDefaults(definitions);
        if (userValues is null) return (result, errors);

        foreach (var def in definitions)
        {
            if (!userValues.TryGetPropertyValue(def.Key, out var node) || node is null) continue;
            var error = Validate(def, node);
            if (error is null) result[def.Key] = node.DeepClone();
            else errors.Add(new SettingValidationError(def.Key, error));
        }
        return (result, errors);
    }

    public static string? Validate(SettingDefinition def, JsonNode node)
    {
        if (node is not JsonValue value) return "Value must be a scalar.";
        switch (def.Type)
        {
            case SettingKind.Boolean:
                return value.TryGetValue<bool>(out _) ? null : "Expected true or false.";
            case SettingKind.Slider:
            case SettingKind.Number:
                if (!TryGetDouble(value, out var d)) return "Expected a number.";
                if (def.Min is { } min && d < min) return $"Must be at least {min}.";
                if (def.Max is { } max && d > max) return $"Must be at most {max}.";
                return null;
            case SettingKind.Choice:
                if (!value.TryGetValue<string>(out var choice)) return "Expected an option value.";
                return def.Options?.Any(o => o.Value == choice) == true ? null : $"'{choice}' is not a valid option.";
            case SettingKind.Color:
                if (!value.TryGetValue<string>(out var color)) return "Expected a color.";
                return ColorValue.TryParse(color, out _) ? null : "Expected a color in #RRGGBB or #AARRGGBB form.";
            case SettingKind.Time:
                if (!value.TryGetValue<string>(out var time)) return "Expected a time.";
                return TimeOnly.TryParseExact(time, "HH:mm", out _) ? null : "Expected a time in HH:mm form.";
            case SettingKind.Hotkey:
                if (!value.TryGetValue<string>(out var hk)) return "Expected a hotkey.";
                return hk.Length == 0 || Hotkey.TryParse(hk, out _) ? null : "Expected a hotkey such as Ctrl+Alt+K.";
            default:
                if (!value.TryGetValue<string>(out var text)) return "Expected text.";
                if (def.MaxLength is { } len && text.Length > len) return $"Must be {len} characters or fewer.";
                if (def.Pattern is { } pattern && !System.Text.RegularExpressions.Regex.IsMatch(text, pattern, System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromMilliseconds(100)))
                    return "Value has an invalid format.";
                return null;
        }
    }

    public static bool TryGetDouble(JsonValue value, out double result)
    {
        if (value.TryGetValue(out result)) return true;
        if (value.TryGetValue<int>(out var i)) { result = i; return true; }
        if (value.TryGetValue<long>(out var l)) { result = l; return true; }
        if (value.TryGetValue<JsonElement>(out var e) && e.ValueKind == JsonValueKind.Number) { result = e.GetDouble(); return true; }
        return false;
    }

    private static JsonNode? DefaultFor(SettingDefinition def) => def.Type switch
    {
        SettingKind.Boolean => JsonValue.Create(false),
        SettingKind.Slider or SettingKind.Number => JsonValue.Create(def.Min ?? 0),
        SettingKind.Choice => def.Options is { Count: > 0 } o ? JsonValue.Create(o[0].Value) : JsonValue.Create(""),
        SettingKind.Color => JsonValue.Create("#FF000000"),
        SettingKind.Time => JsonValue.Create("00:00"),
        _ => JsonValue.Create(""),
    };

    public static bool GetBool(this JsonObject values, string key, bool fallback = false) =>
        values[key] is JsonValue v && v.TryGetValue<bool>(out var b) ? b : fallback;

    public static double GetDouble(this JsonObject values, string key, double fallback = 0) =>
        values[key] is JsonValue v && TryGetDouble(v, out var d) ? d : fallback;

    public static string GetString(this JsonObject values, string key, string fallback = "") =>
        values[key] is JsonValue v && v.TryGetValue<string>(out var s) ? s : fallback;
}
