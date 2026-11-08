using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Aether.Core.Abstractions;

namespace Aether.ModEngine.Manifest;

/// <summary>Optional guard: the operation only runs when a setting has (or doesn't have) a value.</summary>
public sealed class OperationCondition
{
    public required string Setting { get; init; }
    public JsonNode? EqualsValue { get; init; }
    public JsonNode? NotEquals { get; init; }

    [JsonPropertyName("equals")]
    public JsonNode? EqualsAlias { get => EqualsValue; init => EqualsValue = value; }
}

/// <summary>Converts a setting value to a registry representation.</summary>
public enum ValueTransform
{
    None,
    /// <summary>#AARRGGBB → DWORD 0xAABBGGRR (DWM / accent registry layout).</summary>
    ColorToAbgr,
    /// <summary>#RRGGBB → "R G B" (Control Panel\Colors layout).</summary>
    ColorToRgbTriplet,
    /// <summary>true → 0, false → 1.</summary>
    InvertBool,
    /// <summary>true → 1, false → 0.</summary>
    BoolToInt,
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "op")]
[JsonDerivedType(typeof(RegistrySetOperation), "registry.set")]
[JsonDerivedType(typeof(RegistryDeleteOperation), "registry.delete")]
[JsonDerivedType(typeof(SystemFlagOperation), "system.flag")]
[JsonDerivedType(typeof(BroadcastOperation), "broadcast")]
[JsonDerivedType(typeof(ShellRefreshOperation), "shell.refresh")]
[JsonDerivedType(typeof(CursorReloadOperation), "cursors.reload")]
[JsonDerivedType(typeof(ExplorerRestartOperation), "explorer.restart")]
public abstract class ModOperation
{
    public OperationCondition? When { get; init; }
    public string? Description { get; init; }
}

public abstract class RegistryOperation : ModOperation
{
    /// <summary>"HKCU" or "HKLM".</summary>
    public required string Hive { get; init; }
    public required string Path { get; init; }
    /// <summary>Value name; empty string addresses the key's default value.</summary>
    public string Name { get; init; } = "";

    public RegistryRoot Root => Hive.ToUpperInvariant() switch
    {
        "HKCU" or "HKEY_CURRENT_USER" => RegistryRoot.CurrentUser,
        "HKLM" or "HKEY_LOCAL_MACHINE" => RegistryRoot.LocalMachine,
        _ => throw new InvalidOperationException($"Unsupported registry hive '{Hive}'."),
    };

    public string Target => $@"{Hive}\{Path}\{(Name.Length == 0 ? "(Default)" : Name)}";
}

public sealed class RegistrySetOperation : RegistryOperation
{
    public RegistryDataKind Kind { get; init; } = RegistryDataKind.DWord;
    /// <summary>Literal value or a "{{settingKey}}" template.</summary>
    public required JsonNode Value { get; init; }
    /// <summary>Maps the resolved value's string form to a final value, e.g. {"true":1,"false":0}.</summary>
    public Dictionary<string, JsonNode?>? ValueMap { get; init; }
    public ValueTransform Transform { get; init; }
}

public sealed class RegistryDeleteOperation : RegistryOperation;

public sealed class SystemFlagOperation : ModOperation
{
    public SystemFlag Flag { get; init; }
    public required JsonNode Value { get; init; }
    public ValueTransform Transform { get; init; }
}

/// <summary>Post-step: broadcasts WM_SETTINGCHANGE after apply and after revert.</summary>
public sealed class BroadcastOperation : ModOperation
{
    public string? Area { get; init; }
}

public sealed class ShellRefreshOperation : ModOperation;

public sealed class CursorReloadOperation : ModOperation;

/// <summary>Post-step: restarts explorer.exe once after apply/revert (deduplicated across operations).</summary>
public sealed class ExplorerRestartOperation : ModOperation;
