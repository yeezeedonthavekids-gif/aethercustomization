using System.Text.Json.Nodes;
using Aether.ModEngine.Manifest;
using Aether.ModEngine.Security;

namespace Aether.ModEngine.Lifecycle;

public enum ModStatus { Disabled, Enabled, Error, Incompatible }

public static class ModSources
{
    public const string BuiltIn = "builtin";
    public const string Package = "package";
}

public sealed record CompatibilityReport(bool IsCompatible, IReadOnlyList<string> Problems, IReadOnlyList<string> Warnings)
{
    public static CompatibilityReport Compatible { get; } = new(true, [], []);
}

/// <summary>A mod as seen by the UI: manifest + persisted state + live status.</summary>
public sealed class InstalledMod
{
    public required ModManifest Manifest { get; init; }
    public required string Source { get; init; }
    public required VerificationLevel Verification { get; init; }
    public string? InstallPath { get; init; }
    public required ModStatus Status { get; init; }
    public required JsonObject Settings { get; init; }
    public required CompatibilityReport Compatibility { get; init; }
    public required IReadOnlyList<string> MissingPermissions { get; init; }
    public string? LastError { get; init; }
    public string? RuntimeStatus { get; init; }
    public DateTimeOffset InstalledAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }

    public string Id => Manifest.Id;
    public bool IsEnabled => Status == ModStatus.Enabled;
    public bool IsBuiltIn => Source == ModSources.BuiltIn;
}

public enum ModActionStatus
{
    Success,
    /// <summary>The user must grant <see cref="ModActionResult.MissingPermissions"/> first.</summary>
    NeedsConsent,
    /// <summary>Refused before touching the system (incompatible, missing admin, dependency...).</summary>
    Blocked,
    /// <summary>Attempted and failed; changes were rolled back.</summary>
    Failed,
}

public sealed record ModActionResult(ModActionStatus Status, string Message, IReadOnlyList<string> Details, IReadOnlyList<string> MissingPermissions)
{
    public bool Succeeded => Status == ModActionStatus.Success;
    public static ModActionResult Ok(string message, IReadOnlyList<string>? details = null) => new(ModActionStatus.Success, message, details ?? [], []);
    public static ModActionResult Blocked(string message, params string[] details) => new(ModActionStatus.Blocked, message, details, []);
    public static ModActionResult Failed(string message, params string[] details) => new(ModActionStatus.Failed, message, details, []);
    public static ModActionResult Consent(IReadOnlyList<string> missing) =>
        new(ModActionStatus.NeedsConsent, "This mod needs your permission before it can run.", [], missing);
}

/// <summary>Everything the install dialog shows before the user confirms.</summary>
public sealed record PackageInspection(
    ModManifest Manifest,
    VerificationResult Verification,
    CompatibilityReport Compatibility,
    IReadOnlyList<string> ValidationErrors,
    IReadOnlyList<string> MissingDependencies,
    string? InstalledVersion,
    bool CanInstall,
    string Summary);

public sealed class ModEngineOptions
{
    public string HostVersion { get; set; } = "0.1.0";
    public Func<bool> AllowUnsignedPackages { get; set; } = () => false;
    public Func<bool> ReapplyOnStartup { get; set; } = () => true;
    public int VersionsToKeep { get; set; } = 3;
}

/// <summary>Portable export format for mod configurations.</summary>
public sealed class ModConfigurationExport
{
    public int Format { get; init; } = 1;
    public DateTimeOffset ExportedAt { get; init; } = DateTimeOffset.UtcNow;
    public List<ModConfigurationEntry> Mods { get; init; } = [];
}

public sealed class ModConfigurationEntry
{
    public required string Id { get; init; }
    public required string Version { get; init; }
    public bool Enabled { get; init; }
    public JsonObject Settings { get; init; } = [];
}

public sealed record ModChangedEvent(string ModId, string Change);
