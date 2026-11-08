using Aether.Core.Configuration;
using Aether.Core.Versioning;

namespace Aether.ModEngine.Manifest;

public enum ModCategory
{
    Taskbar,
    StartMenu,
    FileExplorer,
    WindowsUi,
    Desktop,
    WindowManagement,
    ContextMenus,
    Productivity,
    SystemBehavior,
    VisualEffects,
    Accessibility,
    Gaming,
}

public enum RiskLevel { Low, Medium, High }

public enum RestartRequirement { None, Explorer, SignOut, Reboot }

/// <summary>How a mod is executed. A mod may combine declarative operations with a component.</summary>
public enum ImplementationKind
{
    /// <summary>Data-only operations interpreted by the engine (registry, system parameters, broadcasts).</summary>
    Declarative,
    /// <summary>A built-in AETHER component (C# code shipped with the app) — e.g. schedulers, utilities.</summary>
    Component,
    /// <summary>PowerShell apply/revert scripts. Requires explicit consent bound to the script hash.</summary>
    PowerShell,
}

/// <summary>The manifest at the root of every mod package (<c>aether-mod.json</c>).</summary>
public sealed class ModManifest
{
    public const string FileName = "aether-mod.json";
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required string Author { get; init; }
    public required SemVersion Version { get; init; }
    public ModCategory Category { get; init; }
    public string? Icon { get; init; }
    public List<string> Tags { get; init; } = [];
    public string? Homepage { get; init; }
    public string? License { get; init; }
    /// <summary>Whether the package may be copied into AETHER backups.</summary>
    public bool Redistributable { get; init; } = true;
    public RiskLevel Risk { get; init; } = RiskLevel.Low;
    public RestartRequirement Restart { get; init; } = RestartRequirement.None;
    public ModCompatibility Compatibility { get; init; } = new();
    public List<string> Permissions { get; init; } = [];
    public List<ModDependency> Dependencies { get; init; } = [];
    public List<SettingDefinition> Settings { get; init; } = [];
    public ModImplementation Implementation { get; init; } = new();
    public List<ModOperation> Operations { get; init; } = [];
    public List<ModAction> Actions { get; init; } = [];
    public ModDocumentation Documentation { get; init; } = new();
    public ModUpdateInfo? Update { get; init; }
    public List<string> Changelog { get; init; } = [];
}

public sealed class ModCompatibility
{
    /// <summary>Minimum Windows build (e.g. 22000 for Windows 11).</summary>
    public int? MinBuild { get; init; }
    public int? MinRevision { get; init; }
    public int? MaxBuild { get; init; }
    public bool RequiresAdmin { get; init; }
    /// <summary>Minimum AETHER host version.</summary>
    public string? MinHostVersion { get; init; }
    public List<string> Architectures { get; init; } = [];
    public string? Notes { get; init; }
}

public sealed class ModDependency
{
    public required string Id { get; init; }
    public string VersionRange { get; init; } = "*";
    public bool Optional { get; init; }
}

public sealed class ModImplementation
{
    public ImplementationKind Kind { get; init; } = ImplementationKind.Declarative;
    /// <summary>Component identifier for <see cref="ImplementationKind.Component"/>.</summary>
    public string? Component { get; init; }
    /// <summary>Relative path of the apply script for <see cref="ImplementationKind.PowerShell"/>.</summary>
    public string? ApplyScript { get; init; }
    public string? RevertScript { get; init; }
}

/// <summary>One-shot commands a mod exposes (e.g. "Restart Explorer now").</summary>
public sealed class ModAction
{
    public required string Id { get; init; }
    public required string Label { get; init; }
    public string? Description { get; init; }
    public string? Glyph { get; init; }
    public bool RequiresConfirmation { get; init; }
}

public sealed class ModDocumentation
{
    public string? WhatItChanges { get; init; }
    public string? Install { get; init; }
    public string? Uninstall { get; init; }
    public string? Backup { get; init; }
    public string? Limitations { get; init; }
}

public sealed class ModUpdateInfo
{
    public string? Channel { get; init; }
    public string? FeedUrl { get; init; }
}
