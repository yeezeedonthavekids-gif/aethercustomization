using Aether.ModEngine.Manifest;

namespace Aether.Services.Tweaks;

public enum TweakArea
{
    Personalization, Taskbar, StartMenu, FileExplorer,
    Performance, Privacy, Notifications, Power, Startup, Accessibility, WindowsBehavior, Gaming, Storage,
}

public sealed record TweakOption(string Value, string Label, IReadOnlyList<ModOperation> Operations);

/// <summary>
/// A single documented Windows setting. Every tweak explains what it changes, why you might want it,
/// side effects, admin/restart requirements and how to revert — and is applied through the mod engine's
/// transactional executor, so the original value is snapshotted and "Restore original" always works.
/// </summary>
public sealed record TweakDefinition
{
    public required string Id { get; init; }
    public required TweakArea Area { get; init; }
    public required string Title { get; init; }
    public required string WhatItChanges { get; init; }
    public required string Why { get; init; }
    public string SideEffects { get; init; } = "None known.";
    public bool RequiresAdmin { get; init; }
    public RestartRequirement Restart { get; init; }
    public string Revert { get; init; } = "Choose 'Restore original' to put back the value you had before AETHER changed it.";
    public int? MinBuild { get; init; }
    public int? MaxBuild { get; init; }
    public string? Group { get; init; }
    public required IReadOnlyList<TweakOption> Options { get; init; }
    /// <summary>Registry value read to detect the current option.</summary>
    public required RegistrySetOperation Probe { get; init; }
    /// <summary>Option assumed when the probed value is missing (the Windows default).</summary>
    public string? DefaultOption { get; init; }
    public IReadOnlyList<string> Keywords { get; init; } = [];

    public bool IsToggle => Options.Count == 2 && Options[0].Value == "on" && Options[1].Value == "off";
}

public sealed record TweakState(TweakDefinition Definition, string? CurrentOption, bool IsModified, bool IsCompatible, string? CompatibilityNote);
